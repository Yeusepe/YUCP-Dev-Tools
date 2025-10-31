using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Orchestrates the complete package export process including obfuscation and icon injection.
    /// Handles validation, folder filtering, DLL obfuscation, and final package creation.
    /// </summary>
    public static class PackageBuilder
    {
        public class ExportResult
        {
            public bool success;
            public string errorMessage;
            public string outputPath;
            public float buildTimeSeconds;
            public int filesExported;
            public int assembliesObfuscated;
        }
        
        /// <summary>
        /// Export a package based on the provided profile
        /// </summary>
        public static ExportResult ExportPackage(ExportProfile profile, Action<float, string> progressCallback = null)
        {
            var result = new ExportResult();
            var startTime = DateTime.Now;
            
            try
            {
                // Save all assets before export
                progressCallback?.Invoke(0.01f, "Saving all project assets...");
                AssetDatabase.SaveAssets();
                
                // Validate profile
                progressCallback?.Invoke(0.05f, "Validating export profile...");
                if (!profile.Validate(out string errorMessage))
                {
                    result.success = false;
                    result.errorMessage = errorMessage;
                    return result;
                }
                
                // Handle obfuscation if enabled
                if (profile.enableObfuscation)
                {
                    if (!ConfuserExManager.IsInstalled())
                    {
                        progressCallback?.Invoke(0.1f, "ConfuserEx not found - downloading...");
                    }
                    else
                    {
                        progressCallback?.Invoke(0.1f, "ConfuserEx ready");
                    }
                    
                    if (!ConfuserExManager.EnsureInstalled((progress, status) =>
                    {
                        progressCallback?.Invoke(0.1f + progress * 0.1f, status);
                    }))
                    {
                        result.success = false;
                        result.errorMessage = "Failed to install ConfuserEx";
                        return result;
                    }
                    
                    progressCallback?.Invoke(0.2f, "Obfuscating assemblies...");
                    
                    // Show warning about obfuscation time
                    if (profile.assembliesToObfuscate.Count(a => a.enabled) > 3)
                    {
                        Debug.Log("[PackageBuilder] Obfuscating multiple assemblies - this may take several minutes. Please wait...");
                    }
                    
                    if (!ConfuserExManager.ObfuscateAssemblies(
                        profile.assembliesToObfuscate,
                        profile.obfuscationPreset,
                        (progress, status) =>
                        {
                            progressCallback?.Invoke(0.2f + progress * 0.3f, status);
                        }))
                    {
                        result.success = false;
                        result.errorMessage = "Assembly obfuscation failed";
                        return result;
                    }
                    
                    result.assembliesObfuscated = profile.assembliesToObfuscate.Count(a => a.enabled);
                }
                
                // Build list of assets to export
                progressCallback?.Invoke(0.5f, $"Collecting assets from {profile.foldersToExport.Count} folders...");
                
                // Collect all assets, then filter out obfuscated assembly files
                List<string> assetsToExport = CollectAssetsToExport(profile);
                
                // Exclude .cs and .asmdef files from obfuscated assemblies (DLL will be included instead)
                if (profile.assembliesToObfuscate != null && profile.assembliesToObfuscate.Count > 0)
                {
                    var obfuscatedAsmdefPaths = profile.assembliesToObfuscate
                        .Where(a => a.enabled)
                        .Select(a => Path.GetFullPath(a.asmdefPath).Replace("\\", "/"))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    
                    assetsToExport = assetsToExport.Where(assetPath => {
                        string fullPath = Path.GetFullPath(assetPath).Replace("\\", "/");
                        string extension = Path.GetExtension(fullPath).ToLower();
                        
                        // Check if this file belongs to an obfuscated assembly
                        if (extension == ".cs" || extension == ".asmdef")
                        {
                            string fileDir = Path.GetDirectoryName(fullPath).Replace("\\", "/");
                            foreach (var asmdefPath in obfuscatedAsmdefPaths)
                            {
                                string asmdefDir = Path.GetDirectoryName(asmdefPath).Replace("\\", "/");
                                if (fileDir.StartsWith(asmdefDir, StringComparison.OrdinalIgnoreCase))
                                {
                                    Debug.Log($"[PackageBuilder] Excluding obfuscated assembly file from export folders: {assetPath}");
                                    return false; // Exclude this file
                                }
                            }
                        }
                        return true; // Include this file
                    }).ToList();
                }
                
                if (assetsToExport.Count == 0)
                {
                    result.success = false;
                    result.errorMessage = "No assets found to export";
                    return result;
                }
                
                progressCallback?.Invoke(0.52f, $"Found {assetsToExport.Count} assets in export folders (excluded obfuscated assemblies)");
                
                // Track bundled dependencies to inject later (AssetDatabase.ExportPackage can't handle files without .meta)
                var bundledPackagePaths = new Dictionary<string, string>(); // packageName -> packagePath
                var bundledDeps = profile.dependencies.Where(d => d.enabled && d.exportMode == DependencyExportMode.Bundle).ToList();
                if (bundledDeps.Count > 0)
                {
                    progressCallback?.Invoke(0.55f, $"Preparing to bundle {bundledDeps.Count} dependencies...");
                    
                    foreach (var dep in bundledDeps)
                    {
                        var depPackageInfo = DependencyScanner.ScanInstalledPackages()
                            .FirstOrDefault(p => p.packageName == dep.packageName);
                        
                        if (depPackageInfo != null && Directory.Exists(depPackageInfo.packagePath))
                        {
                            bundledPackagePaths[dep.packageName] = depPackageInfo.packagePath;
                            Debug.Log($"[PackageBuilder] Will bundle complete package: {dep.packageName} from {depPackageInfo.packagePath}");
                        }
                        else
                        {
                            Debug.LogWarning($"[PackageBuilder] Bundled package not found: {dep.packageName}");
                        }
                    }
                }
                
                 // Generate package.json if needed (but don't add to Unity export - will inject later)
                 string packageJsonContent = null;
                 if (profile.generatePackageJson)
                 {
                     progressCallback?.Invoke(0.58f, "Generating package.json...");
                     
                     // Generate the content but don't create a file in the Assets folder yet
                     // This avoids Unity import issues
                     packageJsonContent = DependencyScanner.GeneratePackageJson(
                         profile,
                         profile.dependencies,
                         null
                     );
                     
                     Debug.Log("[PackageBuilder] Generated package.json content (will inject after export)");
                 }
                
                result.filesExported = assetsToExport.Count;                
                // Create temp package path
                progressCallback?.Invoke(0.6f, "Exporting Unity package...");
                
                string tempPackagePath = Path.Combine(Path.GetTempPath(), $"YUCP_Temp_{Guid.NewGuid():N}.unitypackage");
                
                 // Build export options (Interactive mode is never used in programmatic exports)
                 ExportPackageOptions options = ExportPackageOptions.Default;
                 if (profile.includeDependencies)
                     options |= ExportPackageOptions.IncludeDependencies;
                 if (profile.recurseFolders)
                     options |= ExportPackageOptions.Recurse;
                
                 // Convert all assets to Unity-relative paths and validate
                 progressCallback?.Invoke(0.61f, $"Validating {assetsToExport.Count} assets...");
                 
                 var validAssets = new List<string>();
                 foreach (string asset in assetsToExport)
                 {
                     string unityPath = GetRelativePackagePath(asset);
                     
                     if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityPath) != null)
                     {
                         validAssets.Add(unityPath);
                     }
                     else
                     {
                         Debug.LogWarning($"[PackageBuilder] Could not load asset: {unityPath}");
                     }
                 }
                
                if (validAssets.Count == 0 && bundledPackagePaths.Count == 0)
                {
                    throw new InvalidOperationException("No valid assets found to export. Check that the specified folders contain valid Unity assets.");
                }
                
                progressCallback?.Invoke(0.63f, $"Validated {validAssets.Count} assets from export folders");
                
                 // Use validAssets directly - no need for second validation
                 var finalValidAssets = validAssets;
                 
                 if (finalValidAssets.Count == 0)
                 {
                     throw new InvalidOperationException("No valid assets remain for export after final validation.");
                 }
                 
                 progressCallback?.Invoke(0.65f, $"Exporting {finalValidAssets.Count} assets to Unity package...");
                 
                 // Export the package
                 try
                 {
                     AssetDatabase.ExportPackage(
                         finalValidAssets.ToArray(),
                         tempPackagePath,
                         options
                     );
                     progressCallback?.Invoke(0.7f, "Unity package export completed");
                 }
                 catch (Exception ex)
                 {
                     Debug.LogError($"[PackageBuilder] ExportPackage threw exception: {ex.Message}");
                     throw;
                 }
                 
                 // Wait for export to complete - Unity's export is synchronous but file I/O might be async
                 AssetDatabase.Refresh();
                 
                 // Wait for file to be created (with retry and longer delays)
                 int retryCount = 0;
                 while (!File.Exists(tempPackagePath) && retryCount < 30) // Increased to 30 attempts (6 seconds)
                 {
                     if (retryCount % 5 == 0) // Log every 5th attempt
                     {
                         Debug.Log($"[PackageBuilder] Waiting for temp package... attempt {retryCount + 1}/30");
                     }
                     System.Threading.Thread.Sleep(200); // Wait 200ms
                     retryCount++;
                 }
                 
                 // Verify the file was actually created
                 if (!File.Exists(tempPackagePath))
                 {
                     // Check if the temp directory is accessible
                     string tempDir = Path.GetDirectoryName(tempPackagePath);
                     Debug.LogError($"[PackageBuilder] Temp directory: {tempDir}");
                     Debug.LogError($"[PackageBuilder] Temp directory exists: {Directory.Exists(tempDir)}");
                     Debug.LogError($"[PackageBuilder] Temp directory writable: {CheckDirectoryWritable(tempDir)}");
                     
                     throw new FileNotFoundException($"Package export failed - temp file not created after retries: {tempPackagePath}");
                 }
                
                 Debug.Log($"[PackageBuilder] Package exported to temp location: {tempPackagePath}");
                 
                 // Inject package.json, auto-installer, and bundled packages into the .unitypackage
                 if (!string.IsNullOrEmpty(packageJsonContent) || bundledPackagePaths.Count > 0)
                 {
                     progressCallback?.Invoke(0.75f, "Injecting package.json, installer, and bundled packages...");
                     
                     try
                     {
                         // Pass obfuscated assemblies info so bundled packages can replace source with DLLs
                         var obfuscatedAssemblies = profile.enableObfuscation 
                             ? profile.assembliesToObfuscate.Where(a => a.enabled).ToList() 
                             : new List<AssemblyObfuscationSettings>();
                         
                         InjectPackageJsonInstallerAndBundles(tempPackagePath, packageJsonContent, bundledPackagePaths, obfuscatedAssemblies, profile, progressCallback);
                         Debug.Log("[PackageBuilder] Successfully injected package.json, auto-installer, and bundled packages");
                     }
                     catch (Exception ex)
                     {
                         Debug.LogWarning($"[PackageBuilder] Failed to inject content: {ex.Message}");
                     }
                 }
                 
                 // Get final output path
                 string finalOutputPath = profile.GetOutputFilePath();
                
                // Ensure output directory exists
                string outputDir = Path.GetDirectoryName(finalOutputPath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                // Add icon if specified
                bool iconAdded = false;
                if (profile.icon != null)
                {
                    progressCallback?.Invoke(0.8f, "Adding package icon...");
                    
                    string iconPath = AssetDatabase.GetAssetPath(profile.icon);
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        string fullIconPath = Path.GetFullPath(iconPath);
                        
                        if (PackageIconInjector.AddIconToPackage(tempPackagePath, fullIconPath, finalOutputPath))
                        {
                            Debug.Log("[PackageBuilder] Icon successfully added to package");
                            iconAdded = true;
                        }
                        else
                        {
                            Debug.LogWarning("[PackageBuilder] Failed to add icon, using package without icon");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[PackageBuilder] Could not find icon asset path");
                    }
                }
                
                // Copy temp package to final location if icon wasn't added
                if (!iconAdded)
                {
                    progressCallback?.Invoke(0.85f, "Copying package to output location...");
                    File.Copy(tempPackagePath, finalOutputPath, true);
                }
                
                progressCallback?.Invoke(0.9f, "Cleaning up temporary files...");
                
                // Clean up temp package
                if (File.Exists(tempPackagePath))
                {
                    File.Delete(tempPackagePath);
                }
                
                // Restore original DLLs if obfuscation was used
                if (profile.enableObfuscation)
                {
                    progressCallback?.Invoke(0.95f, "Restoring original assemblies...");
                    ConfuserExManager.RestoreOriginalDlls(profile.assembliesToObfuscate);
                }
                
                progressCallback?.Invoke(0.98f, "Saving export statistics...");
                
                // Update profile statistics
                profile.RecordExport();
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                
                progressCallback?.Invoke(1.0f, "Export complete!");
                
                // Build result
                result.success = true;
                result.outputPath = finalOutputPath;
                result.buildTimeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;
                
                Debug.Log($"[PackageBuilder] Export successful! Package saved to: {finalOutputPath}");
                Debug.Log($"[PackageBuilder] Build time: {result.buildTimeSeconds:F2}s | Files: {result.filesExported} | Obfuscated: {result.assembliesObfuscated}");
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Export failed: {ex.Message}");
                Debug.LogException(ex);
                
                // Restore original DLLs on error
                if (profile.enableObfuscation)
                {
                    try
                    {
                        ConfuserExManager.RestoreOriginalDlls(profile.assembliesToObfuscate);
                    }
                    catch
                    {
                        // Ignore restoration errors
                    }
                }
                
                 result.success = false;
                 result.errorMessage = ex.Message;
                 result.buildTimeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;
                 
                 return result;
            }
        }
        
        /// <summary>
        /// Generate a package.json file for the export
        /// </summary>
        private static string GeneratePackageJson(ExportProfile profile)
        {
            try
            {
                // Look for existing package.json in first export folder
                string existingPackageJsonPath = null;
                foreach (string folder in profile.foldersToExport)
                {
                    string testPath = Path.Combine(folder, "package.json");
                    if (File.Exists(testPath))
                    {
                        existingPackageJsonPath = testPath;
                        break;
                    }
                }
                
                // Generate package.json content
                string packageJsonContent = DependencyScanner.GeneratePackageJson(
                    profile,
                    profile.dependencies,
                    existingPackageJsonPath
                );
                
                // If we have an existing package.json, update it in place
                if (!string.IsNullOrEmpty(existingPackageJsonPath))
                {
                    File.WriteAllText(existingPackageJsonPath, packageJsonContent);
                    AssetDatabase.Refresh();
                    Debug.Log($"[PackageBuilder] Updated existing package.json: {existingPackageJsonPath}");
                    return existingPackageJsonPath;
                }
                
                 // Otherwise, create a temporary package.json in the first export folder
                 if (profile.foldersToExport.Count > 0)
                 {
                     string tempPackageJsonPath = Path.Combine(profile.foldersToExport[0], "package.json");
                     
                     // Ensure the file is created with proper permissions and timestamp
                     File.WriteAllText(tempPackageJsonPath, packageJsonContent);
                     
                     // Force file system sync and refresh
                     File.SetLastWriteTime(tempPackageJsonPath, DateTime.Now);
                     AssetDatabase.Refresh();
                     
                     // Wait a moment for Unity to process the file
                     System.Threading.Thread.Sleep(100);
                     AssetDatabase.Refresh();
                     
                     Debug.Log($"[PackageBuilder] Created package.json: {tempPackageJsonPath}");
                     return tempPackageJsonPath;
                 }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Failed to generate package.json: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Convert absolute path to Unity-relative path (Assets/... or Packages/...)
        /// </summary>
        private static string GetRelativePackagePath(string absolutePath)
        {
            // If already a Unity-relative path, return as-is
            if (absolutePath.StartsWith("Assets/") || absolutePath.StartsWith("Packages/"))
            {
                return absolutePath;
            }
            
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            
            // Normalize both paths for comparison (use forward slashes)
            string normalizedInput = absolutePath.Replace('\\', '/');
            string normalizedProject = projectPath.Replace('\\', '/');
            
            if (normalizedInput.StartsWith(normalizedProject))
            {
                string relative = normalizedInput.Substring(normalizedProject.Length);
                
                if (relative.StartsWith("/"))
                {
                    relative = relative.Substring(1);
                }
                
                return relative;
            }
            
            return absolutePath;
        }
        
        /// <summary>
        /// Collect all assets to export based on profile settings
        /// </summary>
        private static List<string> CollectAssetsToExport(ExportProfile profile)
        {
            var assets = new HashSet<string>();
            
            // If export inspector has been used and assets are selected, use that instead of folder scan
            if (profile.HasScannedAssets && profile.discoveredAssets != null && profile.discoveredAssets.Count > 0)
            {
                Debug.Log($"[PackageBuilder] Using Export Inspector asset selection ({profile.discoveredAssets.Count} discovered assets)");
                
                // Only include assets that are explicitly marked as included
                var includedAssets = profile.discoveredAssets
                    .Where(a => a.included && !a.isFolder)
                    .Select(a => GetRelativePackagePath(a.assetPath))
                    .Where(path => !string.IsNullOrEmpty(path))
                    .ToList();
                
                Debug.Log($"[PackageBuilder] Export Inspector: {includedAssets.Count} assets marked for inclusion");
                
                return includedAssets;
            }
            
            // Otherwise use traditional folder scanning
            Debug.Log($"[PackageBuilder] Using traditional folder scanning (Export Inspector not used)");
            
            foreach (string folder in profile.foldersToExport)
            {
                string assetFolder = folder;
                
                // Convert absolute path to relative path if needed
                if (Path.IsPathRooted(folder))
                {
                    // This is an absolute path - try to make it relative to current project
                    string currentProjectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string normalizedFolder = folder.Replace('\\', '/');
                    string normalizedProject = currentProjectPath.Replace('\\', '/');
                    
                    if (normalizedFolder.StartsWith(normalizedProject))
                    {
                        // Path is within current project
                        assetFolder = normalizedFolder.Substring(normalizedProject.Length + 1);
                    }
                    else
                    {
                        // Path is from a different project - try to use just the last part
                        string folderName = Path.GetFileName(folder);
                        string possiblePath = Path.Combine("Assets", folderName);
                        if (AssetDatabase.IsValidFolder(possiblePath))
                        {
                            assetFolder = possiblePath;
                            Debug.Log($"[PackageBuilder] Resolved cross-project path: {folder} -> {assetFolder}");
                        }
                        else
                        {
                            Debug.LogWarning($"[PackageBuilder] Folder from different project not found in current project: {folder}");
                            continue;
                        }
                    }
                }
                else if (!folder.StartsWith("Assets") && !folder.StartsWith("Packages"))
                {
                    // Relative path that doesn't start with Assets or Packages
                    assetFolder = Path.Combine("Assets", folder).Replace('\\', '/');
                }
                
                // Ensure we have a valid Unity path format
                if (!assetFolder.StartsWith("Assets") && !assetFolder.StartsWith("Packages"))
                {
                    Debug.LogWarning($"[PackageBuilder] Invalid folder path (must start with Assets or Packages): {assetFolder}");
                    continue;
                }
                
                // Check if the folder exists in AssetDatabase
                if (!AssetDatabase.IsValidFolder(assetFolder))
                {
                    Debug.LogWarning($"[PackageBuilder] Folder not found in AssetDatabase: {assetFolder}");
                    continue;
                }
                
                // Refresh AssetDatabase to ensure the folder is recognized
                AssetDatabase.Refresh();
                
                // Check if the folder exists in AssetDatabase
                if (!AssetDatabase.IsValidFolder(assetFolder))
                {
                    Debug.LogWarning($"[PackageBuilder] Folder not recognized by AssetDatabase: {assetFolder}. Creating meta file...");
                    
                    // Try to create a .meta file for the folder
                    string metaPath = assetFolder + ".meta";
                    if (!File.Exists(metaPath))
                    {
                        try
                        {
                            // Create a basic .meta file
                            string metaContent = $"fileFormatVersion: 2\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                            File.WriteAllText(metaPath, metaContent);
                            AssetDatabase.Refresh();
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"[PackageBuilder] Failed to create meta file for {assetFolder}: {ex.Message}");
                            continue;
                        }
                    }
                }
                
                // Get all assets in this folder
                string[] guids = AssetDatabase.FindAssets("", new[] { assetFolder });
                
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    
                    // Apply exclusion filters
                    if (ShouldExcludeAsset(assetPath, profile))
                    {
                        continue;
                    }
                    
                    assets.Add(assetPath);
                }
            }
            
            return assets.ToList();
        }
        
        /// <summary>
        /// Check if an asset should be excluded based on filters
        /// </summary>
        private static bool ShouldExcludeAsset(string assetPath, ExportProfile profile)
        {
            // Check file pattern exclusions
            string fileName = Path.GetFileName(assetPath);
            foreach (string pattern in profile.excludeFilePatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;
                
                // Simple wildcard matching
                if (WildcardMatch(fileName, pattern))
                {
                    return true;
                }
            }
            
            // Check folder name exclusions
            string[] pathParts = assetPath.Split('/', '\\');
            foreach (string folderName in profile.excludeFolderNames)
            {
                if (string.IsNullOrWhiteSpace(folderName))
                    continue;
                
                if (pathParts.Any(part => part.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Simple wildcard matching (* and ? support)
        /// </summary>
        private static bool WildcardMatch(string text, string pattern)
        {
            // Convert wildcard pattern to regex
            string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            
            return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        /// <summary>
        /// Check if a directory is writable
        /// </summary>
        private static bool CheckDirectoryWritable(string directoryPath)
        {
            try
            {
                string testFile = Path.Combine(directoryPath, $"test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Inject package.json, DirectVpmInstaller, and bundled packages into a .unitypackage file
        /// </summary>
        private static void InjectPackageJsonInstallerAndBundles(
            string unityPackagePath, 
            string packageJsonContent, 
            Dictionary<string, string> bundledPackagePaths,
            List<AssemblyObfuscationSettings> obfuscatedAssemblies,
            ExportProfile profile,
            Action<float, string> progressCallback = null)
        {
            // Unity packages are tar.gz archives
            // We need to:
            // 1. Extract the package
            // 2. Add package.json and DirectVpmInstaller.cs as new assets
            // 3. Recompress
            
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"YUCP_PackageExtract_{Guid.NewGuid():N}");
            
            try
            {
                // Create temp directory
                Directory.CreateDirectory(tempExtractDir);
                
                // Extract the .unitypackage (it's a tar.gz)
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
                using (var fileStream = File.OpenRead(unityPackagePath))
                using (var gzipStream = new ICSharpCode.SharpZipLib.GZip.GZipInputStream(fileStream))
                using (var tarArchive = ICSharpCode.SharpZipLib.Tar.TarArchive.CreateInputTarArchive(gzipStream, System.Text.Encoding.UTF8))
                {
                    tarArchive.ExtractContents(tempExtractDir);
                }
#else
                Debug.LogError("[PackageBuilder] ICSharpCode.SharpZipLib not available. Package injection disabled.");
                return;
#endif
                
                // Create a new folder for package.json in the tar structure
                // Unity packages have a specific structure: each asset gets a GUID folder with:
                // - asset (the actual file)
                // - asset.meta (metadata)
                // - pathname (path in the project)
                
                // 1. Inject package.json (temporary, will be deleted by installer)
                if (!string.IsNullOrEmpty(packageJsonContent))
                {
                    string packageJsonGuid = Guid.NewGuid().ToString("N");
                    string packageJsonFolder = Path.Combine(tempExtractDir, packageJsonGuid);
                    Directory.CreateDirectory(packageJsonFolder);
                    
                    File.WriteAllText(Path.Combine(packageJsonFolder, "asset"), packageJsonContent);
                    // Use a unique path to avoid conflicts between multiple package imports
                    File.WriteAllText(Path.Combine(packageJsonFolder, "pathname"), $"Assets/YUCP_TempInstall_{packageJsonGuid}.json");
                    
                    string packageJsonMeta = "fileFormatVersion: 2\nguid: " + packageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(packageJsonFolder, "asset.meta"), packageJsonMeta);
                }
                
                // 2a. Inject YUCPPackageGuardian.cs (permanent protection layer)
                // Only inject if com.yucp.components is NOT already a dependency (to avoid duplication)
                bool hasYucpComponentsDependency = profile.dependencies != null && 
                    profile.dependencies.Any(d => d.enabled && d.packageName == "com.yucp.components");
                
                if (!hasYucpComponentsDependency)
                {
                    string guardianScriptPath = null;
                    string[] foundGuardians = AssetDatabase.FindAssets("YUCPPackageGuardian t:Script");
                    
                    if (foundGuardians.Length > 0)
                    {
                        guardianScriptPath = AssetDatabase.GUIDToAssetPath(foundGuardians[0]);
                        Debug.Log($"[PackageBuilder] Found YUCPPackageGuardian at: {guardianScriptPath}");
                    }
                    
                    if (!string.IsNullOrEmpty(guardianScriptPath) && File.Exists(guardianScriptPath))
                    {
                        string guardianGuid = Guid.NewGuid().ToString("N");
                        string guardianFolder = Path.Combine(tempExtractDir, guardianGuid);
                        Directory.CreateDirectory(guardianFolder);
                        
                        string guardianContent = File.ReadAllText(guardianScriptPath);
                        File.WriteAllText(Path.Combine(guardianFolder, "asset"), guardianContent);
                        // Use Packages path to make it part of the imported package, not Assets/Editor
                        File.WriteAllText(Path.Combine(guardianFolder, "pathname"), $"Packages/yucp.packageguardian/Editor/YUCPPackageGuardian.cs");
                        
                        string guardianMeta = "fileFormatVersion: 2\nguid: " + guardianGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(guardianFolder, "asset.meta"), guardianMeta);
                        
                        // Also create a minimal package.json for the guardian
                        string guardianPackageJsonGuid = Guid.NewGuid().ToString("N");
                        string guardianPackageJsonFolder = Path.Combine(tempExtractDir, guardianPackageJsonGuid);
                        Directory.CreateDirectory(guardianPackageJsonFolder);
                        
                        string guardianPackageJson = @"{
  ""name"": ""yucp.packageguardian"",
  ""displayName"": ""YUCP Package Guardian"",
  ""version"": ""1.0.0"",
  ""description"": ""Automatic import protection for YUCP packages"",
  ""unity"": ""2022.3""
}";
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "asset"), guardianPackageJson);
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "pathname"), "Packages/yucp.packageguardian/package.json");
                        
                        string guardianPackageJsonMeta = "fileFormatVersion: 2\nguid: " + guardianPackageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "asset.meta"), guardianPackageJsonMeta);
                        
                        Debug.Log("[PackageBuilder] Added YUCPPackageGuardian package (permanent import protection)");
                    }
                    else
                    {
                        Debug.LogWarning("[PackageBuilder] Could not find YUCPPackageGuardian.cs - package will have no import protection!");
                    }
                }
                else
                {
                    Debug.Log("[PackageBuilder] Skipping YUCPPackageGuardian injection (com.yucp.components already provides it)");
                }
                
                // 2b. Inject DirectVpmInstaller.cs
                // Try to find the script in the package
                string installerScriptPath = null;
                string[] foundScripts = AssetDatabase.FindAssets("DirectVpmInstaller t:Script");
                
                if (foundScripts.Length > 0)
                {
                    installerScriptPath = AssetDatabase.GUIDToAssetPath(foundScripts[0]);
                    Debug.Log($"[PackageBuilder] Found DirectVpmInstaller at: {installerScriptPath}");
                }
                
                if (!string.IsNullOrEmpty(installerScriptPath) && File.Exists(installerScriptPath))
                {
                    string installerGuid = Guid.NewGuid().ToString("N");
                    string installerFolder = Path.Combine(tempExtractDir, installerGuid);
                    Directory.CreateDirectory(installerFolder);
                    
                    string installerContent = File.ReadAllText(installerScriptPath);
                    File.WriteAllText(Path.Combine(installerFolder, "asset"), installerContent);
                    // Use unique path to avoid conflicts with other package installers
                    File.WriteAllText(Path.Combine(installerFolder, "pathname"), $"Assets/Editor/YUCP_Installer_{installerGuid}.cs");
                    
                    string installerMeta = "fileFormatVersion: 2\nguid: " + installerGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(installerFolder, "asset.meta"), installerMeta);
                    
                    // Also inject the .asmdef to isolate the installer from compilation errors
                    string installerDir = Path.GetDirectoryName(installerScriptPath);
                    string asmdefPath = Path.Combine(installerDir, "DirectVpmInstaller.asmdef");
                    
                    if (File.Exists(asmdefPath))
                    {
                        string asmdefGuid = Guid.NewGuid().ToString("N");
                        string asmdefFolder = Path.Combine(tempExtractDir, asmdefGuid);
                        Directory.CreateDirectory(asmdefFolder);
                        
                        string asmdefContent = File.ReadAllText(asmdefPath);
                        // Ensure the injected asmdef has a unique assembly name to avoid collisions
                        // with any template asmdef included in com.yucp.components or previous installers
                        try
                        {
                            string uniqueAssemblyName = $"YUCP.DirectVpmInstaller.{installerGuid}";
                            asmdefContent = System.Text.RegularExpressions.Regex.Replace(
                                asmdefContent,
                                "\"name\"\\s*:\\s*\"[^\"]*\"",
                                "\"name\": \"" + uniqueAssemblyName + "\""
                            );
                        }
                        catch { /* best-effort replacement */ }
                        File.WriteAllText(Path.Combine(asmdefFolder, "asset"), asmdefContent);
                        File.WriteAllText(Path.Combine(asmdefFolder, "pathname"), $"Assets/Editor/YUCP_Installer_{installerGuid}.asmdef");
                        
                        string asmdefMeta = "fileFormatVersion: 2\nguid: " + asmdefGuid + "\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(asmdefFolder, "asset.meta"), asmdefMeta);
                        
                        Debug.Log("[PackageBuilder] Added DirectVpmInstaller.asmdef to package");
                    }
                    
                    Debug.Log("[PackageBuilder] Added DirectVpmInstaller.cs to package");
                }
                else
                {
                    Debug.LogWarning("[PackageBuilder] Could not find DirectVpmInstaller.cs template");
                }
                
                // 2a. Inject FullDomainReload.cs (helper for installer)
                string fullReloadScriptPath = null;
                string[] foundReloadScripts = AssetDatabase.FindAssets("FullDomainReload t:Script");
                
                if (foundReloadScripts.Length > 0)
                {
                    fullReloadScriptPath = AssetDatabase.GUIDToAssetPath(foundReloadScripts[0]);
                    Debug.Log($"[PackageBuilder] Found FullDomainReload at: {fullReloadScriptPath}");
                }
                
                if (!string.IsNullOrEmpty(fullReloadScriptPath) && File.Exists(fullReloadScriptPath))
                {
                    string reloadGuid = Guid.NewGuid().ToString("N");
                    string reloadFolder = Path.Combine(tempExtractDir, reloadGuid);
                    Directory.CreateDirectory(reloadFolder);
                    
                    string reloadContent = File.ReadAllText(fullReloadScriptPath);
                    File.WriteAllText(Path.Combine(reloadFolder, "asset"), reloadContent);
                    File.WriteAllText(Path.Combine(reloadFolder, "pathname"), $"Assets/Editor/YUCP_FullDomainReload_{reloadGuid}.cs");
                    
                    string reloadMeta = "fileFormatVersion: 2\nguid: " + reloadGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(reloadFolder, "asset.meta"), reloadMeta);
                    
                    Debug.Log("[PackageBuilder] Added FullDomainReload.cs to package");
                }
                else
                {
                    Debug.LogWarning("[PackageBuilder] Could not find FullDomainReload.cs template");
                }
                
                // 3. Inject bundled packages (ALL files including those without .meta)
                if (bundledPackagePaths.Count > 0)
                {
                    int totalBundledFiles = 0;
                    int packageIndex = 0;
                    
                    foreach (var bundledPackage in bundledPackagePaths)
                    {
                        packageIndex++;
                        string packageName = bundledPackage.Key;
                        string packagePath = bundledPackage.Value;
                        
                        progressCallback?.Invoke(0.75f + (0.05f * packageIndex / bundledPackagePaths.Count), 
                            $"Injecting bundled package {packageIndex}/{bundledPackagePaths.Count}: {packageName}...");
                        
                        // Build a set of obfuscated assembly names for this package (for quick lookup)
                        var obfuscatedAsmdefPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var obfuscatedDllPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // asmdefPath -> dllPath
                        
                        foreach (var obfuscatedAsm in obfuscatedAssemblies)
                        {
                            // Check if this obfuscated assembly belongs to the current bundled package
                            if (obfuscatedAsm.asmdefPath.Replace("\\", "/").Contains($"/{packageName}/"))
                            {
                                // Normalize path with forward slashes for consistent comparison
                                string normalizedAsmdefPath = Path.GetFullPath(obfuscatedAsm.asmdefPath).Replace("\\", "/");
                                obfuscatedAsmdefPaths.Add(normalizedAsmdefPath);
                                
                                // Get the obfuscated DLL path from Library/ScriptAssemblies
                                var assemblyInfo = new AssemblyScanner.AssemblyInfo(obfuscatedAsm.assemblyName, obfuscatedAsm.asmdefPath);
                                if (assemblyInfo.exists)
                                {
                                    obfuscatedDllPaths[normalizedAsmdefPath] = assemblyInfo.dllPath;
                                    Debug.Log($"[PackageBuilder] Will replace source code with obfuscated DLL for: {obfuscatedAsm.assemblyName}");
                                    Debug.Log($"[PackageBuilder]   - Asmdef path (normalized): {normalizedAsmdefPath}");
                                    Debug.Log($"[PackageBuilder]   - DLL path: {assemblyInfo.dllPath}");
                                }
                            }
                        }
                        
                        // Get all files in the package (excluding .meta)
                        string[] allFiles = Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories);
                        int filesAdded = 0;
                        int filesReplaced = 0;
                        
                        // Track which asmdef directories have been processed (to avoid adding files multiple times)
                        var processedAsmdefDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        
                        foreach (string filePath in allFiles)
                        {
                            // Skip .meta files
                            if (filePath.EndsWith(".meta"))
                                continue;
                            
                            // Calculate the relative path within the package
                            string relativePath = filePath.Substring(packagePath.Length).TrimStart('\\', '/');
                            
                            // Check if this file belongs to an obfuscated assembly
                            string fileDir = Path.GetDirectoryName(filePath);
                            string asmdefInDir = obfuscatedAsmdefPaths.FirstOrDefault(asmdefPath => 
                            {
                                string asmdefDir = Path.GetDirectoryName(asmdefPath);
                                return fileDir.Replace("\\", "/").StartsWith(asmdefDir.Replace("\\", "/"));
                            });
                            
                            bool isInObfuscatedAssembly = !string.IsNullOrEmpty(asmdefInDir);
                            
                            // Check if this is a script file that could cause compilation errors
                            string extension = Path.GetExtension(filePath).ToLower();
                            bool isCompilableScript = extension == ".cs" || extension == ".asmdef";
                            
                            // Skip ConfuserEx project files
                            if (extension == ".crproj")
                            {
                                Debug.Log($"[PackageBuilder] Skipping ConfuserEx project file: {relativePath}");
                                continue;
                            }
                            
                            // Skip .cs files if they belong to an obfuscated assembly (DLL will be added instead)
                            bool shouldSkipCsFile = false;
                            if (extension == ".cs")
                            {
                                if (isInObfuscatedAssembly)
                                {
                                    Debug.Log($"[PackageBuilder] DEBUG: Skipping source file via isInObfuscatedAssembly (will use obfuscated DLL): {relativePath}");
                                    Debug.Log($"[PackageBuilder] DEBUG:   File dir: {fileDir}");
                                    Debug.Log($"[PackageBuilder] DEBUG:   Matched asmdef: {asmdefInDir}");
                                    shouldSkipCsFile = true;
                                }
                                else
                                {
                                    // Also check by full path comparison
                                    string fullPath = Path.GetFullPath(filePath).Replace("\\", "/");
                                    foreach (var asmdefPath in obfuscatedAsmdefPaths)
                                    {
                                        string asmdefDir = Path.GetDirectoryName(asmdefPath).Replace("\\", "/");
                                        if (fullPath.StartsWith(asmdefDir + "/", StringComparison.OrdinalIgnoreCase))
                                        {
                                            Debug.Log($"[PackageBuilder] DEBUG: Skipping source file by full path match (will use obfuscated DLL): {relativePath}");
                                            Debug.Log($"[PackageBuilder] DEBUG:   File full path: {fullPath}");
                                            Debug.Log($"[PackageBuilder] DEBUG:   Matched asmdef dir: {asmdefDir}");
                                            shouldSkipCsFile = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            
                            if (shouldSkipCsFile)
                            {
                                continue;
                            }
                            
                            // Skip .asmdef if it belongs to an obfuscated assembly (DLL doesn't need it)
                            if (extension == ".asmdef")
                            {
                                string asmdefFullPath = Path.GetFullPath(filePath).Replace("\\", "/");
                                
                                // DEBUG: Log when we encounter ANY .asmdef
                                Debug.Log($"[PackageBuilder] DEBUG: Found .asmdef: {relativePath}");
                                Debug.Log($"[PackageBuilder] DEBUG:   Full path: {asmdefFullPath}");
                                Debug.Log($"[PackageBuilder] DEBUG:   isInObfuscatedAssembly: {isInObfuscatedAssembly}");
                                Debug.Log($"[PackageBuilder] DEBUG:   In obfuscatedAsmdefPaths: {obfuscatedAsmdefPaths.Contains(asmdefFullPath)}");
                                
                                if (obfuscatedAsmdefPaths.Contains(asmdefFullPath))
                                {
                                    Debug.Log($"[PackageBuilder] Skipping asmdef (replaced by obfuscated DLL): {relativePath}");
                                    
                                    // Add the obfuscated DLL instead (only once per asmdef)
                                    if (!processedAsmdefDirs.Contains(asmdefFullPath))
                                    {
                                        processedAsmdefDirs.Add(asmdefFullPath);
                                        
                                        if (obfuscatedDllPaths.TryGetValue(asmdefFullPath, out string dllPath))
                                        {
                                            // Add the obfuscated DLL
                                            string dllFileName = Path.GetFileName(dllPath);
                                            string dllRelativePath = Path.Combine(Path.GetDirectoryName(relativePath), dllFileName).Replace("\\", "/");
                                            string dllUnityPathname = $"Packages/{packageName}/{dllRelativePath}";
                                            
                                            Debug.Log($"[PackageBuilder] DEBUG: Adding obfuscated DLL");
                                            Debug.Log($"[PackageBuilder] DEBUG:   DLL file: {dllFileName}");
                                            Debug.Log($"[PackageBuilder] DEBUG:   DLL relative path: {dllRelativePath}");
                                            Debug.Log($"[PackageBuilder] DEBUG:   DLL Unity pathname: {dllUnityPathname}");
                                            
                                            string dllGuid = Guid.NewGuid().ToString("N");
                                            string dllMetaContent = GenerateMetaForFile(dllPath, dllGuid);
                                            
                                            string dllFolder = Path.Combine(tempExtractDir, dllGuid);
                                            Directory.CreateDirectory(dllFolder);
                                            
                                            File.Copy(dllPath, Path.Combine(dllFolder, "asset"), true);
                                            File.WriteAllText(Path.Combine(dllFolder, "pathname"), dllUnityPathname);
                                            File.WriteAllText(Path.Combine(dllFolder, "asset.meta"), dllMetaContent);
                                            
                                            filesAdded++;
                                            filesReplaced++;
                                            Debug.Log($"[PackageBuilder] Added obfuscated DLL: {dllFileName} (replaces source code)");
                                        }
                                        else
                                        {
                                            Debug.LogWarning($"[PackageBuilder] DEBUG: Could not find DLL path for asmdef: {asmdefFullPath}");
                                        }
                                    }
                                    else
                                    {
                                        Debug.Log($"[PackageBuilder] DEBUG: Already processed this asmdef");
                                    }
                                    
                                    continue;
                                }
                                else
                                {
                                    Debug.Log($"[PackageBuilder] DEBUG: Asmdef NOT in obfuscatedAsmdefPaths set");
                                    Debug.Log($"[PackageBuilder] DEBUG: obfuscatedAsmdefPaths contains {obfuscatedAsmdefPaths.Count} entries:");
                                    foreach (var path in obfuscatedAsmdefPaths)
                                    {
                                        Debug.Log($"[PackageBuilder] DEBUG:     - {path}");
                                    }
                                }
                            }
                            
                            // Create pathname for Unity package (put in Packages folder)
                            // Add .yucp_disabled to compilable files to prevent compilation until dependencies are ready
                            string unityPathname = $"Packages/{packageName}/{relativePath.Replace('\\', '/')}";
                            if (isCompilableScript)
                            {
                                unityPathname += ".yucp_disabled";
                            }
                            
                            // GUID handling strategy:
                            // - For .yucp_disabled files: Generate NEW GUID to avoid conflicts with enabled version
                            // - For normal files: Preserve original GUID to maintain references
                            string fileGuid = null;
                            string metaContent = null;
                            string originalMetaPath = filePath + ".meta";
                            
                            if (isCompilableScript)
                            {
                                // Generate new GUID for disabled files (prevents GUID conflicts on re-import)
                                fileGuid = Guid.NewGuid().ToString("N");
                                metaContent = GenerateMetaForFile(filePath, fileGuid);
                            }
                            else if (File.Exists(originalMetaPath))
                            {
                                // Preserve original GUID for non-script files (safe, no renaming occurs)
                                string originalMeta = File.ReadAllText(originalMetaPath);
                                var guidMatch = System.Text.RegularExpressions.Regex.Match(originalMeta, @"guid:\s*([a-f0-9]{32})");
                                if (guidMatch.Success)
                                {
                                    fileGuid = guidMatch.Groups[1].Value;
                                    metaContent = originalMeta;
                                }
                            }
                            
                            // If no GUID found, generate new one
                            if (string.IsNullOrEmpty(fileGuid))
                            {
                                fileGuid = Guid.NewGuid().ToString("N");
                                metaContent = GenerateMetaForFile(filePath, fileGuid);
                            }
                            
                            // Create GUID folder
                            string fileFolder = Path.Combine(tempExtractDir, fileGuid);
                            Directory.CreateDirectory(fileFolder);
                            
                            // Copy the actual file
                            File.Copy(filePath, Path.Combine(fileFolder, "asset"), true);
                            
                            // Write pathname
                            File.WriteAllText(Path.Combine(fileFolder, "pathname"), unityPathname);
                            
                            // Write .meta
                            File.WriteAllText(Path.Combine(fileFolder, "asset.meta"), metaContent);
                            
                            filesAdded++;
                        }
                        
                        totalBundledFiles += filesAdded;
                        
                        if (filesReplaced > 0)
                        {
                            Debug.Log($"[PackageBuilder] Bundled package {packageName}: {filesAdded} files ({filesReplaced} assemblies replaced with obfuscated DLLs)");
                        }
                        else
                        {
                            Debug.Log($"[PackageBuilder] Bundled complete package {packageName}: {filesAdded} files");
                        }
                    }
                    
                    Debug.Log($"[PackageBuilder] Total bundled package files injected: {totalBundledFiles}");
                }
                
                // Recompress the package
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
                string tempOutputPath = unityPackagePath + ".tmp";
                
                using (var outputStream = File.Create(tempOutputPath))
                using (var gzipStream = new ICSharpCode.SharpZipLib.GZip.GZipOutputStream(outputStream))
                using (var tarArchive = ICSharpCode.SharpZipLib.Tar.TarArchive.CreateOutputTarArchive(gzipStream, System.Text.Encoding.UTF8))
                {
                    tarArchive.RootPath = tempExtractDir.Replace('\\', '/');
                    if (tarArchive.RootPath.EndsWith("/"))
                        tarArchive.RootPath = tarArchive.RootPath.Remove(tarArchive.RootPath.Length - 1);
                    
                    AddDirectoryFilesToTar(tarArchive, tempExtractDir, true);
                }
                
                // Replace original with new package
                File.Delete(unityPackagePath);
                File.Move(tempOutputPath, unityPackagePath);
#endif
            }
            finally
            {
                // Clean up temp directory
                if (Directory.Exists(tempExtractDir))
                {
                    try
                    {
                        Directory.Delete(tempExtractDir, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }
        
        /// <summary>
        /// Helper to recursively add files to a tar archive
        /// </summary>
        private static void AddDirectoryFilesToTar(object tarArchive, string sourceDirectory, bool recurse)
        {
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
            var archive = tarArchive as ICSharpCode.SharpZipLib.Tar.TarArchive;
            if (archive == null) return;
            
            var filenames = Directory.GetFiles(sourceDirectory);
            foreach (string filename in filenames)
            {
                var entry = ICSharpCode.SharpZipLib.Tar.TarEntry.CreateEntryFromFile(filename);
                archive.WriteEntry(entry, false);
            }

            if (recurse)
            {
                var directories = Directory.GetDirectories(sourceDirectory);
                foreach (string directory in directories)
                    AddDirectoryFilesToTar(archive, directory, recurse);
            }
#else
            Debug.LogError("[PackageBuilder] ICSharpCode.SharpZipLib not available. Please install the ICSharpCode.SharpZipLib package.");
#endif
        }
        
        /// <summary>
        /// Generate appropriate .meta file content based on file extension
        /// </summary>
        private static string GenerateMetaForFile(string filePath, string guid)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            
            // C# scripts
            if (extension == ".cs")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Assembly definitions
            if (extension == ".asmdef")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nAssemblyDefinitionImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Text files (.md, .txt, .json, etc.)
            if (extension == ".md" || extension == ".txt" || extension == ".json" || extension == ".xml")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Compute shaders
            if (extension == ".compute")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nComputeShaderImporter:\n  externalObjects: {{}}\n  currentAPIMask: 4\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Shader files
            if (extension == ".shader" || extension == ".cginc" || extension == ".hlsl")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nShaderImporter:\n  externalObjects: {{}}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Images
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nTextureImporter:\n  internalIDToNameTable: []\n  externalObjects: {{}}\n  serializedVersion: 11\n  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 1\n    sRGBTexture: 1\n    linearTexture: 0\n    fadeOut: 0\n    borderMipMap: 0\n    mipMapsPreserveCoverage: 0\n    alphaTestReferenceValue: 0.5\n    mipMapFadeDistanceStart: 1\n    mipMapFadeDistanceEnd: 3\n  bumpmap:\n    convertToNormalMap: 0\n    externalNormalMap: 0\n    heightScale: 0.25\n    normalMapFilter: 0\n  isReadable: 0\n  streamingMipmaps: 0\n  streamingMipmapsPriority: 0\n  grayScaleToAlpha: 0\n  generateCubemap: 6\n  cubemapConvolution: 0\n  seamlessCubemap: 0\n  textureFormat: 1\n  maxTextureSize: 2048\n  textureSettings:\n    serializedVersion: 2\n    filterMode: -1\n    aniso: -1\n    mipBias: -100\n    wrapU: -1\n    wrapV: -1\n    wrapW: -1\n  nPOTScale: 1\n  lightmap: 0\n  compressionQuality: 50\n  spriteMode: 0\n  spriteExtrude: 1\n  spriteMeshType: 1\n  alignment: 0\n  spritePivot: {{x: 0.5, y: 0.5}}\n  spritePixelsToUnits: 100\n  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}\n  spriteGenerateFallbackPhysicsShape: 1\n  alphaUsage: 1\n  alphaIsTransparency: 0\n  spriteTessellationDetail: -1\n  textureType: 0\n  textureShape: 1\n  singleChannelComponent: 0\n  maxTextureSizeSet: 0\n  compressionQualitySet: 0\n  textureFormatSet: 0\n  applyGammaDecoding: 0\n  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 2048\n    resizeAlgorithm: 0\n    textureFormat: -1\n    textureCompression: 1\n    compressionQuality: 50\n    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n    overridden: 0\n    androidETC2FallbackOverride: 0\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n  spriteSheet:\n    serializedVersion: 2\n    sprites: []\n    outline: []\n    physicsShape: []\n    bones: []\n    spriteID:\n    internalID: 0\n    vertices: []\n    indices:\n    edges: []\n    weights: []\n    secondaryTextures: []\n  spritePackingTag:\n  pSDRemoveMatte: 0\n  pSDShowRemoveMatteOption: 0\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Fonts
            if (extension == ".ttf" || extension == ".otf")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nTrueTypeFontImporter:\n  externalObjects: {{}}\n  serializedVersion: 4\n  fontSize: 16\n  forceTextureCase: -2\n  characterSpacing: 0\n  characterPadding: 1\n  includeFontData: 1\n  fontName:\n  fontNames:\n  - \n  fallbackFontReferences: []\n  customCharacters:\n  fontRenderingMode: 0\n  ascentCalculationMode: 1\n  useLegacyBoundsCalculation: 0\n  shouldRoundAdvanceValue: 1\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // UI Elements (.uxml, .uss)
            if (extension == ".uxml" || extension == ".uss")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {{}}\n  serializedVersion: 2\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n  script: {{fileID: 13804, guid: 0000000000000000e000000000000000, type: 0}}\n";
            }
            
            // SVG files
            if (extension == ".svg")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {{}}\n  serializedVersion: 2\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n  script: {{fileID: 11500000, guid: a57477913897c46af91b7aeb59411556, type: 3}}\n  svgType: 0\n  texturedSpriteMeshType: 0\n  svgPixelsPerUnit: 100\n  gradientResolution: 64\n  alignment: 0\n  customPivot: {{x: 0, y: 0}}\n  generatePhysicsShape: 0\n  viewportOptions: 0\n  preserveViewport: 0\n  advancedMode: 0\n  predefinedResolutionIndex: 1\n  targetResolution: 1080\n  resolutionMultiplier: 1\n  stepDistance: 10\n  samplingStepDistance: 100\n  maxCordDeviationEnabled: 0\n  maxCordDeviation: 1\n  maxTangentAngleEnabled: 0\n  maxTangentAngle: 5\n  keepTextureAspectRatio: 1\n  textureSize: 256\n  textureWidth: 256\n  textureHeight: 256\n  wrapMode: 0\n  filterMode: 1\n  sampleCount: 4\n  preserveSVGImageAspect: 0\n  useSVGPixelsPerUnit: 0\n  meshCompression: 0\n  spriteData:\n    name:\n    originalName:\n    pivot: {{x: 0, y: 0}}\n    border: {{x: 0, y: 0, z: 0, w: 0}}\n    rect:\n      serializedVersion: 2\n      x: 0\n      y: 0\n      width: 0\n      height: 0\n    alignment: 0\n    tessellationDetail: 0\n    bones: []\n    spriteID:\n    internalID: 0\n    vertices: []\n    indices:\n    edges: []\n    weights: []\n";
            }
            
            // Default for unknown file types
            return $"fileFormatVersion: 2\nguid: {guid}\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
        }
        
        /// <summary>
        /// Export multiple profiles in sequence
        /// </summary>
        public static List<ExportResult> ExportMultiple(List<ExportProfile> profiles, Action<int, int, float, string> progressCallback = null)
        {
            var results = new List<ExportResult>();
            
            for (int i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                
                Debug.Log($"[PackageBuilder] Exporting profile {i + 1}/{profiles.Count}: {profile.name}");
                
                var result = ExportPackage(profile, (progress, status) =>
                {
                    progressCallback?.Invoke(i, profiles.Count, progress, status);
                });
                
                results.Add(result);
                
                if (!result.success)
                {
                    Debug.LogError($"[PackageBuilder] Export failed for profile '{profile.name}': {result.errorMessage}");
                }
            }
            
            return results;
        }
    }
}

