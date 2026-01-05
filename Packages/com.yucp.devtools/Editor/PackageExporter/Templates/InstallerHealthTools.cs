using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace YUCP.DirectVpmInstaller
{
    internal static class InstallerHealthTools
    {
        [MenuItem("Tools/YUCP/Others/Installation/Validate Install")] 
        private static void ValidateInstall()
        {
            var report = BuildReport(dryRun: true);
            EditorUtility.DisplayDialog("YUCP Validate Install", report, "OK");
        }

        [MenuItem("Tools/YUCP/Others/Installation/Repair Install")] 
        private static void RepairInstall()
        {
            var report = BuildReport(dryRun: false);
            EditorUtility.DisplayDialog("YUCP Repair Install", report, "OK");
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/YUCP/Others/Installation/Clean Import Artifacts")]
        private static void CleanImportArtifacts()
        {
            int cleanedCount = 0;
            var cleanedItems = new List<string>();
            
            try
            {
                string editorPath = Path.Combine(Application.dataPath, "Editor");
                string assetsPath = Application.dataPath;
                
                // Clean up installer scripts
                if (Directory.Exists(editorPath))
                {
                    // YUCP_Installer_*.cs
                    string[] installerScripts = Directory.GetFiles(editorPath, "YUCP_Installer_*.cs", SearchOption.TopDirectoryOnly);
                    foreach (string script in installerScripts)
                    {
                        try
                        {
                            File.Delete(script);
                            string meta = script + ".meta";
                            if (File.Exists(meta)) File.Delete(meta);
                            cleanedCount++;
                            cleanedItems.Add($"Installer script: {Path.GetFileName(script)}");
                        }
                        catch { }
                    }
                    
                    // YUCP_Installer_*.asmdef
                    string[] installerAsmdefs = Directory.GetFiles(editorPath, "YUCP_Installer_*.asmdef", SearchOption.TopDirectoryOnly);
                    foreach (string asmdef in installerAsmdefs)
                    {
                        try
                        {
                            File.Delete(asmdef);
                            string meta = asmdef + ".meta";
                            if (File.Exists(meta)) File.Delete(meta);
                            cleanedCount++;
                            cleanedItems.Add($"Installer asmdef: {Path.GetFileName(asmdef)}");
                        }
                        catch { }
                    }
                    
                    // YUCP_InstallerTxn_*.cs
                    string[] txnScripts = Directory.GetFiles(editorPath, "YUCP_InstallerTxn_*.cs", SearchOption.TopDirectoryOnly);
                    foreach (string script in txnScripts)
                    {
                        try
                        {
                            File.Delete(script);
                            string meta = script + ".meta";
                            if (File.Exists(meta)) File.Delete(meta);
                            cleanedCount++;
                            cleanedItems.Add($"Transaction script: {Path.GetFileName(script)}");
                        }
                        catch { }
                    }
                    
                    // YUCP_FullDomainReload_*.cs
                    string[] reloadScripts = Directory.GetFiles(editorPath, "YUCP_FullDomainReload_*.cs", SearchOption.TopDirectoryOnly);
                    foreach (string script in reloadScripts)
                    {
                        try
                        {
                            File.Delete(script);
                            string meta = script + ".meta";
                            if (File.Exists(meta)) File.Delete(meta);
                            cleanedCount++;
                            cleanedItems.Add($"Reload script: {Path.GetFileName(script)}");
                        }
                        catch { }
                    }
                    
                    // YUCP_InstallerHealthTools_*.cs
                    string[] healthToolsScripts = Directory.GetFiles(editorPath, "YUCP_InstallerHealthTools_*.cs", SearchOption.TopDirectoryOnly);
                    foreach (string script in healthToolsScripts)
                    {
                        try
                        {
                            File.Delete(script);
                            string meta = script + ".meta";
                            if (File.Exists(meta)) File.Delete(meta);
                            cleanedCount++;
                            cleanedItems.Add($"Health tools script: {Path.GetFileName(script)}");
                        }
                        catch { }
                    }
                }
                
                // Clean up temporary JSON files
                string[] tempJsonFiles = Directory.GetFiles(assetsPath, "YUCP_TempInstall_*.json", SearchOption.TopDirectoryOnly);
                foreach (string json in tempJsonFiles)
                {
                    try
                    {
                        File.Delete(json);
                        string meta = json + ".meta";
                        if (File.Exists(meta)) File.Delete(meta);
                        cleanedCount++;
                        cleanedItems.Add($"Temp install JSON: {Path.GetFileName(json)}");
                    }
                    catch { }
                }
                
                // Clean up signing folder
                string signingFolder = Path.Combine(assetsPath, "_Signing");
                if (Directory.Exists(signingFolder))
                {
                    try
                    {
                        Directory.Delete(signingFolder, true);
                        string signingMeta = signingFolder + ".meta";
                        if (File.Exists(signingMeta)) File.Delete(signingMeta);
                        cleanedCount++;
                        cleanedItems.Add("Signing folder: Assets/_Signing");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[YUCP] Failed to delete signing folder: {ex.Message}");
                    }
                }
                
                // Show results
                if (cleanedCount > 0)
                {
                    string message = $"Cleaned up {cleanedCount} import artifact(s):\n\n" +
                                    string.Join("\n", cleanedItems.Take(15));
                    
                    if (cleanedItems.Count > 15)
                    {
                        message += $"\n\n... and {cleanedItems.Count - 15} more.";
                    }
                    
                    EditorUtility.DisplayDialog(
                        "Import Artifacts Cleaned",
                        message,
                        "OK"
                    );
                    
                    AssetDatabase.Refresh();
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "No Artifacts Found",
                        "No import artifacts were found to clean up.",
                        "OK"
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP] Error cleaning import artifacts: {ex.Message}");
                EditorUtility.DisplayDialog(
                    "Error",
                    $"Failed to clean import artifacts:\n\n{ex.Message}",
                    "OK"
                );
            }
        }

        [MenuItem("Tools/YUCP/Others/Installation/Fix Self Referencing Dependency")]
        private static void FixSelfReferencingDependency()
        {
            int fixedCount = 0;
            var fixedPackages = new List<string>();
            
            try
            {
                // Fix vpm-manifest.json
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                string vpmManifestPath = Path.Combine(packagesPath, "vpm-manifest.json");
                
                if (File.Exists(vpmManifestPath))
                {
                    try
                    {
                        string manifestJson = File.ReadAllText(vpmManifestPath);
                        var manifest = JObject.Parse(manifestJson);
                        bool modified = false;
                        
                        // Get locked packages (installed packages)
                        var locked = manifest["locked"] as JObject;
                        var dependencies = manifest["dependencies"] as JObject;
                        
                        if (locked != null && dependencies != null)
                        {
                            // Only remove packages from dependencies if they are locally installed (bundled packages)
                            // Local packages shouldn't be in dependencies - they should only be in locked
                            var toRemove = new List<string>();
                            foreach (var dep in dependencies.Properties())
                            {
                                string packageName = dep.Name;
                                
                                // Check if this package exists locally in Packages folder
                                // (indicating it's a bundled/local package, not from a repository)
                                string packageDir = Path.Combine(packagesPath, packageName);
                                bool isLocalPackage = Directory.Exists(packageDir);
                                
                                // Also check for YUCP metadata indicating it's a YUCP-imported package
                                // Note: isYucpPackage is currently unused but kept for potential future use
#pragma warning disable CS0219 // Variable is assigned but never used
                                bool isYucpPackage = false;
#pragma warning restore CS0219
                                if (isLocalPackage)
                                {
                                    // Check for YUCP_PackageInfo.json in the package or in yucp.installed-packages
                                    string yucpMetadataPath = Path.Combine(packageDir, "YUCP_PackageInfo.json");
                                    if (!File.Exists(yucpMetadataPath))
                                    {
                                        string installedPackagesPath = Path.Combine(packagesPath, "yucp.installed-packages");
                                        if (Directory.Exists(installedPackagesPath))
                                        {
                                            string[] subdirs = Directory.GetDirectories(installedPackagesPath);
                                            foreach (string subdir in subdirs)
                                            {
                                                yucpMetadataPath = Path.Combine(subdir, "YUCP_PackageInfo.json");
                                                if (File.Exists(yucpMetadataPath))
                                                {
                                                    try
                                                    {
                                                        string metadataJson = File.ReadAllText(yucpMetadataPath);
                                                        var metadata = JObject.Parse(metadataJson);
                                                        string metadataPackageName = metadata["packageName"]?.ToString();
                                                        if (string.Equals(metadataPackageName, packageName, StringComparison.OrdinalIgnoreCase))
                                                        {
                                                            isYucpPackage = true;
                                                            break;
                                                        }
                                                    }
                                                    catch { }
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        isYucpPackage = true;
                                    }
                                }
                                
                                // Remove if it's a local package (bundled packages shouldn't be in dependencies)
                                if (isLocalPackage)
                                {
                                    toRemove.Add(packageName);
                                }
                            }
                            
                            foreach (var packageName in toRemove)
                            {
                                dependencies.Remove(packageName);
                                modified = true;
                                fixedCount++;
                                fixedPackages.Add($"vpm-manifest.json: {packageName}");
                                Debug.Log($"[YUCP] Removed local/bundled package from dependencies: {packageName} from vpm-manifest.json");
                            }
                        }
                        
                        if (modified)
                        {
                            File.WriteAllText(vpmManifestPath, manifest.ToString(Newtonsoft.Json.Formatting.Indented));
                            Debug.Log($"[YUCP] Fixed vpm-manifest.json - removed {fixedCount} self-referential dependencies");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[YUCP] Failed to fix vpm-manifest.json: {ex.Message}");
                    }
                }
                
                // Fix package.json files in Packages folder (including installed packages)
                if (Directory.Exists(packagesPath))
                {
                    // Get all package.json files, including in subdirectories
                    string[] allPackageJsonFiles = Directory.GetFiles(packagesPath, "package.json", SearchOption.AllDirectories);
                    
                    foreach (string packageJsonPath in allPackageJsonFiles)
                    {
                        // Skip package.json files in node_modules or other non-package locations
                        if (packageJsonPath.Contains("node_modules") || packageJsonPath.Contains("Library"))
                            continue;
                        
                        try
                        {
                            string packageJsonContent = File.ReadAllText(packageJsonPath);
                            var packageJson = JObject.Parse(packageJsonContent);
                            
                            string packageName = packageJson["name"]?.ToString();
                            if (string.IsNullOrEmpty(packageName))
                                continue;
                            
                            // Normalize package name for comparison
                            string normalizedPackageName = packageName.ToLower().Replace(" ", ".");
                            
                            bool modified = false;
                            
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
                                    fixedCount++;
                                    string relativePath = packageJsonPath.Replace(Path.GetFullPath(packagesPath), "Packages").Replace('\\', '/');
                                    fixedPackages.Add($"{relativePath}: {depName} (vpmDependencies)");
                                    Debug.Log($"[YUCP] Removed self-referential vpmDependency: {depName} from {packageJsonPath}");
                                }
                                
                                // Remove vpmDependencies section if empty
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
                                    fixedCount++;
                                    string relativePath = packageJsonPath.Replace(Path.GetFullPath(packagesPath), "Packages").Replace('\\', '/');
                                    fixedPackages.Add($"{relativePath}: {depName} (dependencies)");
                                    Debug.Log($"[YUCP] Removed self-referential dependency: {depName} from {packageJsonPath}");
                                }
                                
                                // Remove dependencies section if empty
                                if (deps.Count == 0)
                                {
                                    packageJson.Remove("dependencies");
                                    modified = true;
                                }
                            }
                            
                            if (modified)
                            {
                                File.WriteAllText(packageJsonPath, packageJson.ToString(Newtonsoft.Json.Formatting.Indented));
                                Debug.Log($"[YUCP] Fixed package.json: {packageJsonPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[YUCP] Failed to check/fix {packageJsonPath}: {ex.Message}");
                        }
                    }
                }
                
                // Show results
                if (fixedCount > 0)
                {
                    string message = $"Fixed {fixedCount} self-referential dependency issue(s):\n\n" +
                                    string.Join("\n", fixedPackages.Take(10));
                    
                    if (fixedPackages.Count > 10)
                    {
                        message += $"\n\n... and {fixedPackages.Count - 10} more.";
                    }
                    
                    EditorUtility.DisplayDialog(
                        "Self-Referential Dependencies Fixed",
                        message,
                        "OK"
                    );
                    
                    AssetDatabase.Refresh();
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "No Issues Found",
                        "No self-referential dependencies were found in vpm-manifest.json or package.json files.",
                        "OK"
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP] Error fixing self-referential dependencies: {ex.Message}");
                EditorUtility.DisplayDialog(
                    "Error",
                    $"Failed to fix self-referential dependencies:\n\n{ex.Message}",
                    "OK"
                );
            }
        }

        private static string BuildReport(bool dryRun)
        {
            var sb = new System.Text.StringBuilder();
            string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");

            // 1) Orphan .yucp_disabled
            int orphanCount = 0;
            try
            {
                foreach (var file in Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories))
                {
                    orphanCount++;
                    sb.AppendLine("Orphan disabled: " + Short(file));
                    if (!dryRun)
                    {
                        try { File.Delete(file); File.Delete(file + ".meta"); } catch { }
                    }
                }
            }
            catch { }

            // 2) Duplicate asmdef names
            var asmNameToFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var asmdef in Directory.GetFiles(Path.GetDirectoryName(Application.dataPath) ?? "", "*.asmdef", SearchOption.AllDirectories))
                {
                    try
                    {
                        var json = File.ReadAllText(asmdef);
                        var match = System.Text.RegularExpressions.Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                        if (match.Success)
                        {
                            var name = match.Groups[1].Value;
                            if (!asmNameToFiles.TryGetValue(name, out var list))
                                list = asmNameToFiles[name] = new List<string>();
                            list.Add(asmdef);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            foreach (var kv in asmNameToFiles.Where(kv => kv.Value.Count > 1))
            {
                sb.AppendLine($"Duplicate asmdef name '{kv.Key}':");
                foreach (var f in kv.Value) sb.AppendLine("  - " + Short(f));
            }

            // 3) Missing meta for scripts (basic)
            int missingMeta = 0;
            foreach (var cs in Directory.GetFiles(packagesPath, "*.cs", SearchOption.AllDirectories))
            {
                var meta = cs + ".meta";
                if (!File.Exists(meta))
                {
                    missingMeta++;
                    sb.AppendLine("Missing meta: " + Short(cs));
                    if (!dryRun)
                    {
                        try { File.WriteAllText(meta, "fileFormatVersion: 2\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"); } catch { }
                    }
                }
            }

            if (orphanCount == 0 && asmNameToFiles.All(kv => kv.Value.Count < 2) && missingMeta == 0)
                sb.AppendLine("No issues detected.");

            return sb.ToString();
        }

        private static string Short(string p) => p.Replace(Path.GetDirectoryName(Application.dataPath) + Path.DirectorySeparatorChar, "");
    }
}





