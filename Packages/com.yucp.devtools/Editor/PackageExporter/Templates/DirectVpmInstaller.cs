using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

namespace YUCP.DirectVpmInstaller
{
    [InitializeOnLoad]
    public static class DirectVpmInstaller
    {
        
        static DirectVpmInstaller()
        {
            EditorApplication.delayCall += CheckAndInstallVpmPackages;
        }
        
        private static void CheckAndInstallVpmPackages()
        {
            // Note: Duplicate import prevention is handled by YUCPImportMonitor (global AssetPostprocessor)
            // in the com.yucp.components package, which runs BEFORE this installer
            
            // Clear stale lock if present (crash recovery)
            try { if (InstallerTxn.HasMarker("lock") && InstallerTxn.IsMarkerStale("lock", TimeSpan.FromMinutes(10))) InstallerTxn.ClearMarker("lock"); } catch { }

            // Clean up any old installer scripts first to prevent duplicate class definitions on re-import
            CleanupInstallerScript();

            // Find any YUCP temp install JSON files
            string[] tempJsonFiles = Directory.GetFiles(Application.dataPath, "YUCP_TempInstall_*.json", SearchOption.TopDirectoryOnly);
            
            if (tempJsonFiles.Length == 0)
            {
                // If a previous install completed, ensure cleanup convergence
                if (InstallerTxn.HasMarker("complete"))
                {
                    CleanupInstallerScript();
                }
                return;
            }
            
            string packageJsonPath = tempJsonFiles[0]; // Use the first one found
            
            try
            {
                // Signal import coordination
                InstallerTxn.SetMarker("pending");
                InstallerTxn.SetMarker("lock");
                var packageInfo = JObject.Parse(File.ReadAllText(packageJsonPath));
                string bundledPackageName = packageInfo["name"]?.Value<string>();
                string bundledPackageVersion = packageInfo["version"]?.Value<string>();
                
                // Check if this bundled package is already installed
                if (!string.IsNullOrEmpty(bundledPackageName))
                {
                    string existingPackagePath = Path.Combine(Application.dataPath, "..", "Packages", bundledPackageName);
                    if (Directory.Exists(existingPackagePath))
                    {
                        string existingPackageJson = Path.Combine(existingPackagePath, "package.json");
                        if (File.Exists(existingPackageJson))
                        {
                            try
                            {
                                var existingData = JObject.Parse(File.ReadAllText(existingPackageJson));
                                string existingVersion = existingData["version"]?.Value<string>();
                                
                                if (!string.IsNullOrEmpty(existingVersion) && !string.IsNullOrEmpty(bundledPackageVersion))
                                {
                                    if (CompareVersions(existingVersion, bundledPackageVersion) >= 0)
                                    {
                                        Debug.Log($"[DirectVpmInstaller] Bundled package {bundledPackageName}@{bundledPackageVersion} is already installed (current: {existingVersion}). Skipping extraction.");
                                        
                                        // Still enable any .yucp_disabled files (might be from previous failed install)
                                        string txnIdEarly = InstallerTxn.Begin();
                                        bool enableOkEarly = false;
                                        try
                                        {
                                            EnableBundledPackagesTransactional();
                                            enableOkEarly = true;
                                        }
                                        catch (Exception exEnable)
                                        {
                                            Debug.LogError($"[DirectVpmInstaller] Failed while enabling bundled packages: {exEnable.Message}. Rolling back...");
                                            InstallerTxn.Rollback();
                                            throw;
                                        }
                                        finally
                                        {
                                            if (enableOkEarly)
                                            {
                                                InstallerTxn.Commit();
                                                if (!InstallerTxn.VerifyManifest())
                                                    throw new Exception("Post-install manifest verification failed");
                                            }
                                        }
                                        // Mark install complete and cleanup
                                        InstallerTxn.SetMarker("complete");
                                        CleanupTemporaryFiles(packageJsonPath);
                                        return;
                                    }
                                    else
                                    {
                                        Debug.Log($"[DirectVpmInstaller] Upgrading bundled package {bundledPackageName} from {existingVersion} to {bundledPackageVersion}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[DirectVpmInstaller] Failed to check existing package version: {ex.Message}");
                            }
                        }
                    }
                }
                
                var vpmDependencies = packageInfo["vpmDependencies"] as JObject;
                var vpmRepositories = packageInfo["vpmRepositories"] as JObject;
                
                if (vpmDependencies == null || vpmDependencies.Count == 0)
                {
                    InstallerTxn.SetMarker("complete");
                    CleanupTemporaryFiles(packageJsonPath);
                    return;
                }
                
                // Seed repository list from the bundled package (if any)
                var repositories = new Dictionary<string, string>();
                if (vpmRepositories != null && vpmRepositories.Count > 0)
                {
                    repositories = vpmRepositories.Properties()
                        .ToDictionary(p => p.Name, p => p.Value.ToString());
                }
                
                // Merge user VCC repositories (do not write them into package.json)
                try
                {
                    string vccSettingsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "VRChatCreatorCompanion",
                        "settings.json"
                    );
                    
                    if (File.Exists(vccSettingsPath))
                    {
                        var settings = JObject.Parse(File.ReadAllText(vccSettingsPath));
                        var userRepos = settings["userRepos"] as JArray;
                        
                        if (userRepos != null)
                        {
                            foreach (var repoObj in userRepos)
                            {
                                var repo = repoObj as JObject;
                                if (repo == null) continue;
                                
                                string repoUrl = repo["url"]?.ToString();
                                string repoName = repo["name"]?.ToString();
                                string repoId = repo["id"]?.ToString();
                                
                                if (string.IsNullOrEmpty(repoUrl)) continue;
                                
                                string key = !string.IsNullOrEmpty(repoName) ? repoName : repoId;
                                if (!string.IsNullOrEmpty(key) && !repositories.ContainsKey(key))
                                {
                                    repositories[key] = repoUrl;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DirectVpmInstaller] Failed to read VCC repositories: {ex.Message}");
                }
                
                // Always ensure VRChat Official / Curated repos are available for resolution
                const string vrchatOfficialName = "VRChat Official";
                const string vrchatCuratedName = "VRChat Curated";
                const string vrchatOfficialUrl = "https://vrchat.github.io/packages/index.json";
                const string vrchatCuratedUrl = "https://vrchat-community.github.io/vpm-listing-curated/index.json";

                if (!repositories.ContainsKey(vrchatOfficialName))
                    repositories[vrchatOfficialName] = vrchatOfficialUrl;
                if (!repositories.ContainsKey(vrchatCuratedName))
                    repositories[vrchatCuratedName] = vrchatCuratedUrl;

                // Direct (top-level) dependencies for the UI prompt
                var packagesToInstall = new List<Tuple<string, string>>();

                // Work queue + set to install dependencies recursively (transitive closure)
                var installQueue = new Queue<Tuple<string, string>>();
                var plannedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var dep in vpmDependencies.Properties())
                {
                    string packageName = dep.Name;
                    string versionRequirement = dep.Value.ToString();
                    
                    if (!IsPackageInstalled(packageName, versionRequirement))
                    {
                        var tuple = new Tuple<string, string>(packageName, versionRequirement);
                        packagesToInstall.Add(tuple);
                        if (plannedPackages.Add(packageName))
                        {
                            installQueue.Enqueue(tuple);
                        }
                    }
                }
                
                if (packagesToInstall.Count == 0)
                {
                    CleanupTemporaryFiles(packageJsonPath);
                    return;
                }
                
                string packageList = string.Join("\n", packagesToInstall.Select(p => $"  - {p.Item1}@{p.Item2}"));
                bool install = EditorUtility.DisplayDialog(
                    "Install VPM Dependencies",
                    $"This package requires the following VPM dependencies:\n\n{packageList}\n\nWould you like to install them now?\n\n(Required for compilation)",
                    "Install",
                    "Cancel"
                );
                
                if (!install)
                {
                    Debug.LogWarning("[DirectVpmInstaller] Dependencies not installed. Compilation errors may occur.");
                    CleanupTemporaryFiles(packageJsonPath);
                    return;
                }
                
                // Lock assemblies and disable auto-refresh
                EditorApplication.LockReloadAssemblies();
                AssetDatabase.DisallowAutoRefresh();
                AssetDatabase.StartAssetEditing();
                
                try
                {
                    Debug.Log("[DirectVpmInstaller] Installing dependencies with compilation locked (including transitive vpmDependencies)...");
                    
                    // Install all requested packages, then recursively install their vpmDependencies
                    bool allSucceeded = true;
                    while (installQueue.Count > 0)
                    {
                        var package = installQueue.Dequeue();
                        
                        // Double-check we still need this package (it may have been installed as a transitive dependency)
                        if (IsPackageInstalled(package.Item1, package.Item2))
                            continue;
                        
                        if (!InstallPackage(package.Item1, package.Item2, repositories))
                        {
                            allSucceeded = false;
                            continue;
                        }
                        
                        // After successful install, read its package.json and enqueue its own vpmDependencies
                        try
                        {
                            EnqueueTransitiveDependencies(package.Item1, repositories, installQueue, plannedPackages);
                        }
                        catch (Exception exDeps)
                        {
                            Debug.LogWarning($"[DirectVpmInstaller] Failed to resolve transitive dependencies for {package.Item1}: {exDeps.Message}");
                        }
                    }
                    
                if (allSucceeded)
                {
                    Debug.Log("[DirectVpmInstaller] Dependencies installed. Enabling bundled packages...");
                    
                    // Enable bundled packages while still locked with transaction/rollback safety
                    string txnId = InstallerTxn.Begin();
                    bool enableOk = false;
                    try
                    {
                        EnableBundledPackagesTransactional();
                        // Post-commit manifest verification happens after commit below
                        enableOk = true;
                    }
                    catch (Exception exEnable)
                    {
                        Debug.LogError($"[DirectVpmInstaller] Failed while enabling bundled packages: {exEnable.Message}. Rolling back...");
                        InstallerTxn.Rollback();
                        throw;
                    }
                    finally
                    {
                        if (enableOk)
                        {
                            // Verify enabled files match expected hashes BEFORE clearing transaction state
                            if (!InstallerTxn.VerifyManifest())
                                throw new Exception("Post-install manifest verification failed");
                            InstallerTxn.Commit();
                        }
                    }
                    
                    // NOTE: Do NOT add bundled packages to vpm-manifest.json - they are local packages, not from repositories
                    // Adding them causes VPM Resolver to try to resolve them from repos, resulting in "package not found" errors
                    
                    // Fix self-references in the installed package's package.json (if it exists)
                    if (!string.IsNullOrEmpty(bundledPackageName))
                    {
                        string installedPackagePath = Path.Combine(Application.dataPath, "..", "Packages", bundledPackageName);
                        string installedPackageJson = Path.Combine(installedPackagePath, "package.json");
                        if (File.Exists(installedPackageJson))
                        {
                            try
                            {
                                string packageJsonContent = File.ReadAllText(installedPackageJson);
                                var packageJson = JObject.Parse(packageJsonContent);
                                bool modified = false;
                                
                                string normalizedPackageName = bundledPackageName.ToLower().Replace(" ", ".");
                                
                                // Check vpmDependencies
                                var vpmDeps = packageJson["vpmDependencies"] as JObject;
                                if (vpmDeps != null)
                                {
                                    var toRemove = new List<string>();
                                    foreach (var dep in vpmDeps.Properties())
                                    {
                                        string depName = dep.Name;
                                        string normalizedDepName = depName.ToLower().Replace(" ", ".");
                                        if (string.Equals(normalizedDepName, normalizedPackageName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            toRemove.Add(depName);
                                        }
                                    }
                                    
                                    foreach (var depName in toRemove)
                                    {
                                        vpmDeps.Remove(depName);
                                        modified = true;
                                        Debug.Log($"[DirectVpmInstaller] Removed self-referential vpmDependency: {depName} from installed package.json");
                                    }
                                    
                                    if (vpmDeps.Count == 0)
                                    {
                                        packageJson.Remove("vpmDependencies");
                                        modified = true;
                                    }
                                }
                                
                                // Check dependencies
                                var deps = packageJson["dependencies"] as JObject;
                                if (deps != null)
                                {
                                    var toRemove = new List<string>();
                                    foreach (var dep in deps.Properties())
                                    {
                                        string depName = dep.Name;
                                        string normalizedDepName = depName.ToLower().Replace(" ", ".");
                                        if (string.Equals(normalizedDepName, normalizedPackageName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            toRemove.Add(depName);
                                        }
                                    }
                                    
                                    foreach (var depName in toRemove)
                                    {
                                        deps.Remove(depName);
                                        modified = true;
                                        Debug.Log($"[DirectVpmInstaller] Removed self-referential dependency: {depName} from installed package.json");
                                    }
                                    
                                    if (deps.Count == 0)
                                    {
                                        packageJson.Remove("dependencies");
                                        modified = true;
                                    }
                                }
                                
                                if (modified)
                                {
                                    File.WriteAllText(installedPackageJson, packageJson.ToString(Newtonsoft.Json.Formatting.Indented));
                                    Debug.Log($"[DirectVpmInstaller] Fixed self-references in installed package.json: {installedPackageJson}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[DirectVpmInstaller] Failed to fix self-references in installed package.json: {ex.Message}");
                            }
                        }
                    }
                    
                    // Clean up temporary files
                    InstallerTxn.SetMarker("complete");
                    CleanupTemporaryFiles(packageJsonPath);
                }
                    else
                    {
                        CleanupTemporaryFiles(packageJsonPath);
                    }
                }
                finally
                {
                    // Unlock everything in one atomic operation
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.AllowAutoRefresh();
                    EditorApplication.UnlockReloadAssemblies();
                    
                    Debug.Log("[DirectVpmInstaller] Unlocked. Triggering full domain reload...");
                    
                    // Force a focus-grade full domain reload (includes UPM resolve, compile, and reload)
                    FullDomainReload.Run(() =>
                    {
                        Debug.Log("[DirectVpmInstaller] Installation complete. Domain fully reloaded with all dependencies functional.");
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DirectVpmInstaller] Error: {ex.Message}");
                try { InstallerTxn.SetMarker("error"); } catch { }
            }
            finally
            {
                // Clear coordination markers
                try { InstallerTxn.ClearMarker("lock"); } catch { }
                try { InstallerTxn.ClearMarker("pending"); } catch { }
            }
        }
        
        private static void EnableBundledPackagesTransactional()
        {
            var movedFiles = new List<Tuple<string, string>>(); // Track for potential rollback
            
            try
            {
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                if (!Directory.Exists(packagesPath))
                    return;
                
                // Preflight checks: free space, write permissions
                if (!HasSufficientDiskSpace(packagesPath, 100 * 1024 * 1024L))
                    throw new Exception("Insufficient disk space for installation");
                TestWritePermission(packagesPath);

                // Find all .yucp_disabled files in Packages folder
                string[] disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                // Apply ignore patterns if present
                var ignored = LoadInstallerIgnore(packagesPath);
                if (ignored.Count > 0)
                {
                    disabledFiles = disabledFiles.Where(f => !ignored.Any(p => WildcardMatch(NormalizePath(f), p))).ToArray();
                }
                int enabledCount = 0;
                int skippedCount = 0;
                
                foreach (string disabledFile in disabledFiles)
                {
                    try
                    {
                        InstallerTxn.EnableDisabledFile(disabledFile);
                        enabledCount++;
                    }
                    catch (Exception fileEx)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to process '{Path.GetFileName(disabledFile)}': {fileEx.Message}");
                        // Continue with other files
                    }
                }
                
                if (enabledCount > 0)
                {
                    Debug.Log($"[DirectVpmInstaller] Enabled {enabledCount} bundled package files ({skippedCount} skipped as already up-to-date)");
                }
                else if (skippedCount > 0)
                {
                    Debug.Log($"[DirectVpmInstaller] All {skippedCount} bundled files were already up-to-date");
                }
                
                // Final cleanup pass: remove any remaining .yucp_disabled files
                InstallerTxn.CleanupOrphanedDisabledFiles();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DirectVpmInstaller] Critical error enabling bundled packages: {ex.Message}");
                
                // Attempt rollback
                // Rollback handled by caller via InstallerTxn.Rollback()
                throw;
            }
        }

        private static bool HasSufficientDiskSpace(string pathInDrive, long requiredBytes)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(pathInDrive));
                foreach (var di in DriveInfo.GetDrives())
                {
                    if (string.Equals(di.Name, root, StringComparison.OrdinalIgnoreCase))
                    {
                        return di.AvailableFreeSpace > requiredBytes;
                    }
                }
            }
            catch { }
            return true;
        }

        private static void TestWritePermission(string folder)
        {
            string testFile = null;
            try
            {
                testFile = Path.Combine(folder, $".yucp_write_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "test");
            }
            finally
            {
                try { if (testFile != null && File.Exists(testFile)) File.Delete(testFile); } catch { }
            }
        }

        private static List<string> LoadInstallerIgnore(string packagesPath)
        {
            var patterns = new List<string>();
            try
            {
                var ignorePath = Path.Combine(packagesPath, ".yucpinstallerignore");
                if (File.Exists(ignorePath))
                {
                    foreach (var line in File.ReadAllLines(ignorePath))
                    {
                        var t = (line ?? "").Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        patterns.Add(t.Replace('\\', '/'));
                    }
                }
            }
            catch { }
            return patterns;
        }

        private static string NormalizePath(string p) => (Path.GetFullPath(p).Replace('\\', '/'));

        private static bool WildcardMatch(string text, string pattern)
        {
            // Simple wildcard (*) match, case-insensitive
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(text, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        // Cleanup moved to InstallerTxn
        
        private static void RollbackMovedFiles(List<Tuple<string, string>> movedFiles)
        {
            if (movedFiles == null || movedFiles.Count == 0)
                return;
            
            Debug.LogWarning($"[DirectVpmInstaller] Attempting to rollback {movedFiles.Count} moved files...");
            
            foreach (var move in movedFiles)
            {
                try
                {
                    string originalPath = move.Item1; // .yucp_disabled
                    string newPath = move.Item2;      // enabled path
                    
                    if (File.Exists(newPath) && !File.Exists(originalPath))
                    {
                        File.Move(newPath, originalPath);
                        
                        string newMeta = newPath + ".meta";
                        string originalMeta = originalPath + ".meta";
                        if (File.Exists(newMeta))
                        {
                            File.Move(newMeta, originalMeta);
                        }
                        
                        Debug.Log($"[DirectVpmInstaller] Rolled back: {Path.GetFileName(originalPath)}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DirectVpmInstaller] Failed to rollback file: {ex.Message}");
                }
            }
        }
        
        private static void CleanupTemporaryFiles(string packageJsonPath)
        {
            try
            {
                // Delete the temporary package.json
                if (File.Exists(packageJsonPath))
                {
                    File.Delete(packageJsonPath);
                    string metaPath = packageJsonPath + ".meta";
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                    
                    Debug.Log($"[DirectVpmInstaller] Cleaned up temporary file: {packageJsonPath}");
                }
                
                // Clean up the installer script itself
                CleanupInstallerScript();
                
                // Find and delete the installer script itself
                string editorPath = Path.Combine(Application.dataPath, "Editor");
                if (Directory.Exists(editorPath))
                {
                    string[] installerScripts = Directory.GetFiles(editorPath, "YUCP_Installer_*.cs", SearchOption.TopDirectoryOnly);
                    foreach (string installerPath in installerScripts)
                    {
                        File.Delete(installerPath);
                        string metaPath = installerPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        
                        Debug.Log($"[DirectVpmInstaller] Deleted installer script: {installerPath}");
                    }
                    
                    // Also delete the .asmdef
                    string[] asmdefFiles = Directory.GetFiles(editorPath, "YUCP_Installer_*.asmdef", SearchOption.TopDirectoryOnly);
                    foreach (string asmdefPath in asmdefFiles)
                    {
                        File.Delete(asmdefPath);
                        string metaPath = asmdefPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        
                        Debug.Log($"[DirectVpmInstaller] Deleted installer asmdef: {asmdefPath}");
                    }
                    
                    // Also delete the InstallerTransactionManager script
                    string[] txnScripts = Directory.GetFiles(editorPath, "YUCP_InstallerTxn_*.cs", SearchOption.TopDirectoryOnly);
                    foreach (string txnPath in txnScripts)
                    {
                        File.Delete(txnPath);
                        string metaPath = txnPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        
                        Debug.Log($"[DirectVpmInstaller] Deleted InstallerTransactionManager script: {txnPath}");
                    }
                    
                    // Also delete the FullDomainReload helper script
                    string[] reloadScripts = Directory.GetFiles(editorPath, "YUCP_FullDomainReload_*.cs", SearchOption.TopDirectoryOnly);
                    foreach (string reloadPath in reloadScripts)
                    {
                        File.Delete(reloadPath);
                        string metaPath = reloadPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        
                        Debug.Log($"[DirectVpmInstaller] Deleted FullDomainReload script: {reloadPath}");
                    }
                    
                    // Also delete the InstallerHealthTools script
                    string[] healthToolsScripts = Directory.GetFiles(editorPath, "YUCP_InstallerHealthTools_*.cs", SearchOption.TopDirectoryOnly);
                    foreach (string healthToolsPath in healthToolsScripts)
                    {
                        File.Delete(healthToolsPath);
                        string metaPath = healthToolsPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        
                        Debug.Log($"[DirectVpmInstaller] Deleted InstallerHealthTools script: {healthToolsPath}");
                    }
                }
                
                // Delete all .yucp_disabled files from bundled packages (orphaned after enabling)
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                if (Directory.Exists(packagesPath))
                {
                    string[] disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                    int deletedCount = 0;
                    
                    foreach (string disabledFile in disabledFiles)
                    {
                        File.Delete(disabledFile);
                        string metaPath = disabledFile + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        
                        deletedCount++;
                    }
                    
                    if (deletedCount > 0)
                    {
                        Debug.Log($"[DirectVpmInstaller] Deleted {deletedCount} orphaned .yucp_disabled files");
                    }
                }
                
                // Organize YUCP-generated artifacts (e.g. YUCP_PackageInfo.json) into a local package
                OrganizeYucpArtifacts();
                
                // Clean up signing folder if it exists (from signed package imports)
                string signingFolder = Path.Combine(Application.dataPath, "_Signing");
                if (Directory.Exists(signingFolder))
                {
                    try
                    {
                        Directory.Delete(signingFolder, true);
                        string signingMeta = signingFolder + ".meta";
                        if (File.Exists(signingMeta))
                            File.Delete(signingMeta);
                        Debug.Log($"[DirectVpmInstaller] Cleaned up signing folder: {signingFolder}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to cleanup signing folder: {ex.Message}");
                    }
                }

                // Refresh AssetDatabase to reflect file deletions and moves
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to cleanup temporary files: {ex.Message}");
            }
        }

        /// <summary>
        /// Move YUCP-generated artifacts (metadata and helper assets) out of Assets/
        /// into a dedicated local package under Packages/yucp.installed-packages.
        /// This runs regardless of whether com.yucp.components is installed.
        /// </summary>
        private static void OrganizeYucpArtifacts()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string packagesRoot = Path.Combine(projectRoot, "Packages");
                string installedRoot = Path.Combine(packagesRoot, "yucp.installed-packages");

                if (!Directory.Exists(packagesRoot))
                    Directory.CreateDirectory(packagesRoot);
                if (!Directory.Exists(installedRoot))
                    Directory.CreateDirectory(installedRoot);

                // Read package name from YUCP_PackageInfo.json if present
                string metadataDiskPath = Path.Combine(Application.dataPath, "YUCP_PackageInfo.json");
                string packageFolderName = null;

                if (File.Exists(metadataDiskPath))
                {
                    try
                    {
                        string json = File.ReadAllText(metadataDiskPath);
                        var meta = JsonUtility.FromJson<YucpPackageMetadata>(json);
                        if (meta != null && !string.IsNullOrEmpty(meta.packageName))
                        {
                            packageFolderName = MakeSafeFolderName(meta.packageName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to read YUCP_PackageInfo.json metadata: {ex.Message}");
                    }
                }

                if (string.IsNullOrEmpty(packageFolderName))
                {
                    packageFolderName = "package-" + Guid.NewGuid().ToString("N");
                }

                string packageFolderDiskPath = Path.Combine(installedRoot, packageFolderName);
                if (!Directory.Exists(packageFolderDiskPath))
                {
                    Directory.CreateDirectory(packageFolderDiskPath);
                }

                // Move YUCP_PackageInfo.json into the installed-packages container if it exists (disk-level move)
                if (File.Exists(metadataDiskPath))
                {
                    string targetMetadataDiskPath = Path.Combine(packageFolderDiskPath, "YUCP_PackageInfo.json");
                    try
                    {
                        // Ensure parent exists
                        Directory.CreateDirectory(Path.GetDirectoryName(targetMetadataDiskPath) ?? packageFolderDiskPath);

                        // Delete existing target if it exists
                        if (File.Exists(targetMetadataDiskPath))
                        {
                            File.Delete(targetMetadataDiskPath);
                        }
                        string dstMeta = targetMetadataDiskPath + ".meta";
                        if (File.Exists(dstMeta))
                        {
                            File.Delete(dstMeta);
                        }

                        // Move JSON
                        File.Move(metadataDiskPath, targetMetadataDiskPath);
                        // Move .meta if present
                        string srcMeta = metadataDiskPath + ".meta";
                        if (File.Exists(srcMeta))
                        {
                            File.Move(srcMeta, dstMeta);
                        }

                        Debug.Log($"[DirectVpmInstaller] Moved YUCP_PackageInfo.json to '{targetMetadataDiskPath}'");
                    }
                    catch (Exception moveEx)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to move YUCP_PackageInfo.json to installed-packages: {moveEx.Message}");
                    }
                }

                // Optionally move YUCP/ExportProfiles into the same container if present (disk-level move).
                // This keeps exporter profiles from cluttering the Assets root.
                string exportProfilesDiskPath = Path.Combine(Application.dataPath, "YUCP", "ExportProfiles");
                if (Directory.Exists(exportProfilesDiskPath))
                {
                    string targetProfilesDiskParent = Path.Combine(packageFolderDiskPath, "YUCP");
                    string targetProfilesDiskPath = Path.Combine(targetProfilesDiskParent, "ExportProfiles");
                    try
                    {
                        Directory.CreateDirectory(targetProfilesDiskParent);
                        
                        // Delete existing target directory if it exists
                        if (Directory.Exists(targetProfilesDiskPath))
                        {
                            Directory.Delete(targetProfilesDiskPath, true);
                        }
                        // Also delete .meta if present
                        string targetMeta = targetProfilesDiskPath + ".meta";
                        if (File.Exists(targetMeta))
                        {
                            File.Delete(targetMeta);
                        }
                        
                        Directory.Move(exportProfilesDiskPath, targetProfilesDiskPath);

                        Debug.Log($"[DirectVpmInstaller] Moved ExportProfiles folder to '{targetProfilesDiskPath}'");
                    }
                    catch (Exception moveEx)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to move ExportProfiles folder to installed-packages: {moveEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] OrganizeYucpArtifacts failed: {ex.Message}");
            }
        }

        [Serializable]
        private class YucpPackageMetadata
        {
            public string packageName;
            public string version;
            public string author;
            public string description;
        }

        /// <summary>
        /// Generate a filesystem-safe folder name from a package name.
        /// </summary>
        private static string MakeSafeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "package-" + Guid.NewGuid().ToString("N");

            char[] invalid = Path.GetInvalidFileNameChars();
            var safeChars = new char[name.Length];
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    c = '-';
                else if (c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '\"' || c == '<' || c == '>' || c == '|')
                    c = '-';
                else if (Array.IndexOf(invalid, c) >= 0)
                    c = '-';

                safeChars[i] = c;
            }

            string safe = new string(safeChars).Trim('-');
            if (string.IsNullOrEmpty(safe))
                safe = "package-" + Guid.NewGuid().ToString("N");
            return safe;
        }
        
        private static bool IsPackageInstalled(string packageName, string versionRequirement)
        {
            string packageJsonPath = $"Packages/{packageName}/package.json";
            if (!File.Exists(packageJsonPath))
                return false;
            
            try
            {
                var packageData = JObject.Parse(File.ReadAllText(packageJsonPath));
                string installedVersion = packageData["version"]?.ToString();
                return !string.IsNullOrEmpty(installedVersion) && VersionSatisfiesRequirement(installedVersion, versionRequirement);
            }
            catch
            {
                return false;
            }
        }
        
        private static bool InstallPackage(string packageName, string versionRequirement, Dictionary<string, string> repositories)
        {
            try
            {
                string downloadUrl = null;
                string resolvedVersion = null;
                
                foreach (var repo in repositories)
                {
                    try
                    {
                        var repoData = JObject.Parse(new WebClient().DownloadString(repo.Value));
                        var packages = repoData["packages"] as JObject;
                        
                        if (packages?[packageName] == null)
                            continue;
                        
                        var packageData = packages[packageName] as JObject;
                        var versions = packageData["versions"] as JObject;
                        
                        if (versions == null)
                            continue;
                        
                        string bestVersion = null;
                        string bestUrl = null;
                        
                        foreach (var versionEntry in versions.Properties())
                        {
                            try
                            {
                                string version = versionEntry.Name;
                                if (VersionSatisfiesRequirement(version, versionRequirement))
                                {
                                    if (bestVersion == null || CompareVersions(version, bestVersion) > 0)
                                    {
                                        bestVersion = version;
                                        bestUrl = (versionEntry.Value as JObject)?["url"]?.ToString();
                                    }
                                }
                            }
                            catch { }
                        }
                        
                        if (bestVersion != null && bestUrl != null)
                        {
                            downloadUrl = bestUrl;
                            resolvedVersion = bestVersion;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to check repository {repo.Key}: {ex.Message}");
                    }
                }
                
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Debug.LogError($"[DirectVpmInstaller] Package {packageName} not found in any repository");
                    return false;
                }
                
                string tempZipPath = Path.Combine(Path.GetTempPath(), $"{packageName}.zip");
                string packageDestination = $"Packages/{packageName}";
                
                new WebClient().DownloadFile(downloadUrl, tempZipPath);
                
                if (Directory.Exists(packageDestination))
                    Directory.Delete(packageDestination, true);
                
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, packageDestination);
                File.Delete(tempZipPath);
                
                // Add to VPM manifest so VCC recognizes it as installed
                // VPM packages from repositories should be in both dependencies and locked
                AddToVpmManifest(packageName, resolvedVersion, addToDependencies: true);
                
                Debug.Log($"[DirectVpmInstaller] Installed {packageName}@{resolvedVersion}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DirectVpmInstaller] Failed to install {packageName}: {ex.Message}");
                return false;
            }
        }
        
        private static void AddToVpmManifest(string packageName, string version, bool addToDependencies = true)
        {
            try
            {
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "vpm-manifest.json");
                JObject manifest;
                
                if (File.Exists(manifestPath))
                {
                    manifest = JObject.Parse(File.ReadAllText(manifestPath));
                }
                else
                {
                    // Create new manifest if it doesn't exist
                    manifest = new JObject();
                    manifest["dependencies"] = new JObject();
                    manifest["locked"] = new JObject();
                }
                
                // Add to dependencies section only if this is a VPM package from a repository
                // Bundled packages (local imports) should NOT be in dependencies
                if (addToDependencies)
                {
                    var dependencies = manifest["dependencies"] as JObject;
                    if (dependencies == null)
                    {
                        dependencies = new JObject();
                        manifest["dependencies"] = dependencies;
                    }
                    dependencies[packageName] = new JObject
                    {
                        ["version"] = version
                    };
                }
                else
                {
                    // Remove from dependencies if it exists (bundled packages shouldn't be there)
                    var dependencies = manifest["dependencies"] as JObject;
                    if (dependencies != null && dependencies[packageName] != null)
                    {
                        dependencies.Remove(packageName);
                    }
                }
                
                // Always add to locked section (both VPM and bundled packages are installed)
                var locked = manifest["locked"] as JObject;
                if (locked == null)
                {
                    locked = new JObject();
                    manifest["locked"] = locked;
                }
                locked[packageName] = new JObject
                {
                    ["version"] = version
                };
                
                // Save manifest
                File.WriteAllText(manifestPath, manifest.ToString(Newtonsoft.Json.Formatting.Indented));
                Debug.Log($"[DirectVpmInstaller] Added {packageName}@{version} to vpm-manifest.json (locked{(addToDependencies ? " + dependencies" : " only")})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to update vpm-manifest.json: {ex.Message}");
            }
        }
        
        private static bool VersionSatisfiesRequirement(string installedVersion, string requirement)
        {
            requirement = requirement.Trim();
            
            if (requirement.StartsWith(">="))
            {
                string minVersion = requirement.Substring(2).Trim();
                return CompareVersions(installedVersion, minVersion) >= 0;
            }
            
            if (requirement.StartsWith("^"))
            {
                string baseVersion = requirement.Substring(1).Trim();
                var baseParts = ParseVersion(baseVersion);
                var installedParts = ParseVersion(installedVersion);
                
                if (baseParts.major != installedParts.major)
                    return false;
                
                return CompareVersions(installedVersion, baseVersion) >= 0;
            }
            
            if (requirement.StartsWith("~"))
            {
                string baseVersion = requirement.Substring(1).Trim();
                var baseParts = ParseVersion(baseVersion);
                var installedParts = ParseVersion(installedVersion);
                
                if (baseParts.major != installedParts.major || baseParts.minor != installedParts.minor)
                    return false;
                
                return CompareVersions(installedVersion, baseVersion) >= 0;
            }
            
            return CompareVersions(installedVersion, requirement) >= 0;
        }
        
        private static int CompareVersions(string version1, string version2)
        {
            var v1 = ParseVersion(version1);
            var v2 = ParseVersion(version2);
            
            if (v1.major != v2.major) return v1.major.CompareTo(v2.major);
            if (v1.minor != v2.minor) return v1.minor.CompareTo(v2.minor);
            return v1.patch.CompareTo(v2.patch);
        }
        
        private static (int major, int minor, int patch) ParseVersion(string version)
        {
            version = version.Trim().TrimStart('v', 'V');
            int dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
                version = version.Substring(0, dashIndex);
            
            var parts = version.Split('.');
            int major = parts.Length > 0 ? int.Parse(parts[0]) : 0;
            int minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            int patch = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            
            return (major, minor, patch);
        }
        
        private static void CleanupInstallerScript()
        {
            try
            {
                // Find and delete all YUCP installer scripts
                string editorPath = Path.Combine(Application.dataPath, "Editor");
                if (!Directory.Exists(editorPath))
                    return;
                
                // Find all YUCP installer files (NOT guardian - guardian stays permanently)
                string[] installerFiles = Directory.GetFiles(editorPath, "YUCP_Installer_*.cs", SearchOption.TopDirectoryOnly);
                string[] installerTxnFiles = Directory.GetFiles(editorPath, "YUCP_InstallerTxn_*.cs", SearchOption.TopDirectoryOnly);
                string[] installerHealthToolsFiles = Directory.GetFiles(editorPath, "YUCP_InstallerHealthTools_*.cs", SearchOption.TopDirectoryOnly);
                string[] installerAsmDefs = Directory.GetFiles(editorPath, "YUCP_Installer_*.asmdef", SearchOption.TopDirectoryOnly);
                
                int deletedCount = 0;
                
                foreach (string file in installerFiles.Concat(installerTxnFiles).Concat(installerHealthToolsFiles).Concat(installerAsmDefs))
                {
                    try
                    {
                        File.Delete(file);
                        string metaFile = file + ".meta";
                        if (File.Exists(metaFile))
                            File.Delete(metaFile);
                        
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to delete installer file '{Path.GetFileName(file)}': {ex.Message}");
                    }
                }
                
                if (deletedCount > 0)
                {
                    Debug.Log($"[DirectVpmInstaller] Cleaned up {deletedCount} installer script(s) to prevent duplicate assembly errors");
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Error during installer script cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// After installing a package, read its package.json and enqueue any vpmDependencies
        /// that are not yet installed, so that dependencies-of-dependencies are pulled in.
        /// Also merges any vpmRepositories from the installed package into the shared repository list.
        /// </summary>
        private static void EnqueueTransitiveDependencies(
            string packageName,
            Dictionary<string, string> repositories,
            Queue<Tuple<string, string>> installQueue,
            HashSet<string> plannedPackages)
        {
            if (string.IsNullOrEmpty(packageName))
                return;

            try
            {
                // Resolve Packages/<packageName>/package.json relative to project root
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string packageJsonPath = Path.Combine(projectRoot, "Packages", packageName, "package.json");

                if (!File.Exists(packageJsonPath))
                    return;

                var json = JObject.Parse(File.ReadAllText(packageJsonPath));
                var vpmDependencies = json["vpmDependencies"] as JObject;
                var vpmRepositories = json["vpmRepositories"] as JObject;

                // Merge any new repositories from this package
                if (vpmRepositories != null)
                {
                    foreach (var repo in vpmRepositories.Properties())
                    {
                        repositories[repo.Name] = repo.Value.ToString();
                    }
                }

                if (vpmDependencies == null || vpmDependencies.Count == 0)
                    return;

                foreach (var dep in vpmDependencies.Properties())
                {
                    string depName = dep.Name;
                    string versionRequirement = dep.Value.ToString();

                    // Skip if already installed at a satisfying version
                    if (IsPackageInstalled(depName, versionRequirement))
                        continue;

                    // Skip if we've already planned to install this package
                    if (!plannedPackages.Add(depName))
                        continue;

                    installQueue.Enqueue(new Tuple<string, string>(depName, versionRequirement));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to read transitive vpmDependencies for {packageName}: {ex.Message}");
            }
        }
        
        [MenuItem("Tools/YUCP/Others/Installation/Install VPM Dependencies")]
        public static void ManualInstallVpmDependencies()
        {
            CheckAndInstallVpmPackages();
        }
    }
}
