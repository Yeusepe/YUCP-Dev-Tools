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
        internal static bool s_isExporting = false;
        
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
            
            // Set exporting flag to prevent reserialization loops
            s_isExporting = true;
            
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
                
                // Transform derived FBXs (ModelImporter) into PatchPackages and authoring sidecars
                progressCallback?.Invoke(0.505f, "Scanning for derived FBXs to convert into PatchPackages...");
                bool hasPatchAssets = ConvertDerivedFbxToPatchAssets(assetsToExport, progressCallback);
                
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
                     
                     // Try to load the asset
                     var loadedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityPath);
                     if (loadedAsset != null)
                     {
                         validAssets.Add(unityPath);
                     }
                     else
                     {
                         // For Packages paths, check if file exists physically as fallback
                         // (Unity might not have imported it yet, but it exists on disk)
                         if (unityPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                         {
                             string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                             string physicalPath = Path.Combine(projectPath, unityPath.Replace('/', Path.DirectorySeparatorChar));
                             if (File.Exists(physicalPath) || Directory.Exists(physicalPath))
                             {
                                 // For .asset and .cs files in Packages, allow them if file exists
                                 // Unity will export the file itself even if not fully imported
                                 if (unityPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || 
                                     unityPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                                 {
                                     // File exists, add it - Unity will export it
                                     validAssets.Add(unityPath);
                                 }
                                 else
                                 {
                                     // For other files, try importing
                                     AssetDatabase.ImportAsset(unityPath, ImportAssetOptions.ForceSynchronousImport);
                                     AssetDatabase.Refresh();
                                     
                                     // Try loading again
                                     loadedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityPath);
                                     if (loadedAsset != null)
                                     {
                                         validAssets.Add(unityPath);
                                     }
                                     else
                                     {
                                         Debug.LogWarning($"[PackageBuilder] Could not load asset (file exists but Unity doesn't recognize it): {unityPath}");
                                     }
                                 }
                             }
                             else
                             {
                                 Debug.LogWarning($"[PackageBuilder] Could not load asset (file does not exist): {unityPath}");
                             }
                         }
                         else
                         {
                             Debug.LogWarning($"[PackageBuilder] Could not load asset: {unityPath}");
                         }
                     }
                 }
                
                if (validAssets.Count == 0 && bundledPackagePaths.Count == 0)
                {
                    throw new InvalidOperationException("No valid assets found to export. Check that the specified folders contain valid Unity assets.");
                }
                
                progressCallback?.Invoke(0.63f, $"Validated {validAssets.Count} assets from export folders");
                
                 // Use validAssets directly - no need for second validation
                 var finalValidAssets = validAssets;
                 
                 // Safety check: Remove any derived FBXs that might have slipped through
                 int safetyRemoved = finalValidAssets.RemoveAll(assetPath =>
                 {
                     if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return false;
                     var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                     if (importer == null) return false;
                     try
                     {
                         var settings = string.IsNullOrEmpty(importer.userData) ? null : JsonUtility.FromJson<DerivedSettings>(importer.userData);
                         if (settings != null && settings.isDerived)
                         {
                             Debug.LogWarning($"[PackageBuilder] Safety check: Removing derived FBX that was still in export list: {assetPath}");
                             return true;
                         }
                     }
                     catch { /* ignore */ }
                     return false;
                 });
                 if (safetyRemoved > 0)
                 {
                     Debug.LogWarning($"[PackageBuilder] Safety check removed {safetyRemoved} derived FBX(s) that were still in the export list");
                 }
                 
                 // If patch temp folder exists, ensure it's included (so newly generated patch assets are exported)
                 if (AssetDatabase.IsValidFolder("Packages/com.yucp.temp/Patches") && !finalValidAssets.Contains("Packages/com.yucp.temp/Patches"))
                 {
                     finalValidAssets = new List<string>(finalValidAssets) { "Packages/com.yucp.temp/Patches" };
                 }
                 
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
                         
                         InjectPackageJsonInstallerAndBundles(tempPackagePath, packageJsonContent, bundledPackagePaths, obfuscatedAssemblies, profile, hasPatchAssets, progressCallback);
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
            finally
            {
                // Always clear exporting flag to prevent reserialization loops
                s_isExporting = false;
            }
        }
        
        /// <summary>
        /// Replace any FBX assets marked as "derived" with a PatchPackage + sidecars generated via PatchBuilder.
        /// Stores settings in the FBX importer userData JSON.
        /// Returns true if any patch assets were created.
        /// </summary>
        private static bool ConvertDerivedFbxToPatchAssets(List<string> assetsToExport, Action<float, string> progressCallback)
        {
            if (assetsToExport == null || assetsToExport.Count == 0) return false;
            
            // Gather derived FBXs in the export set
            Debug.Log($"[PackageBuilder] Scanning {assetsToExport.Count} assets for derived FBXs...");
            var derivedFbxPaths = new List<string>();
            int fbxCount = 0;
            foreach (var assetPath in assetsToExport)
            {
                if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                fbxCount++;
                
                // Normalize path for AssetImporter (prefer relative)
                string normalizedPath = assetPath;
                if (Path.IsPathRooted(normalizedPath))
                {
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    if (normalizedPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedPath = normalizedPath.Substring(projectPath.Length).Replace('\\', '/').TrimStart('/');
                    }
                }
                
                var importer = AssetImporter.GetAtPath(normalizedPath) as ModelImporter;
                if (importer == null)
                {
                    Debug.Log($"[PackageBuilder] Could not get ModelImporter for FBX: {assetPath} (normalized: {normalizedPath})");
                    continue;
                }
                
                try
                {
                    string userDataJson = importer.userData;
                    Debug.Log($"[PackageBuilder] Checking FBX: {assetPath}, userData: '{userDataJson}'");
                    
                    var settings = string.IsNullOrEmpty(userDataJson) ? null : JsonUtility.FromJson<DerivedSettings>(userDataJson);
                    if (settings != null && settings.isDerived && !string.IsNullOrEmpty(settings.baseGuid))
                    {
                        Debug.Log($"[PackageBuilder] Found derived FBX: {assetPath} (baseGuid: {settings.baseGuid})");
                        // Store original path for removal (will normalize during removal)
                        derivedFbxPaths.Add(assetPath);
                    }
                    else if (settings != null)
                    {
                        Debug.Log($"[PackageBuilder] FBX {assetPath} has settings but isDerived={settings.isDerived}, baseGuid={settings.baseGuid}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageBuilder] Failed to parse userData for {assetPath}: {ex.Message}");
                }
            }
            
            Debug.Log($"[PackageBuilder] Found {fbxCount} FBX(s) total, {derivedFbxPaths.Count} derived FBX(s)");
            if (derivedFbxPaths.Count == 0) return false;
            
            progressCallback?.Invoke(0.51f, $"Converting {derivedFbxPaths.Count} derived FBX file(s) into PatchPackages...");
            
            EnsureAuthoringFolder();
            
            var fbxToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var patchAssetsToAdd = new List<string>();
            
            foreach (var modifiedPath in derivedFbxPaths)
            {
                // Normalize path for AssetImporter
                string normalizedModifiedPath = modifiedPath;
                if (Path.IsPathRooted(normalizedModifiedPath))
                {
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    if (normalizedModifiedPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedModifiedPath = normalizedModifiedPath.Substring(projectPath.Length).Replace('\\', '/').TrimStart('/');
                    }
                }
                
                var importer = AssetImporter.GetAtPath(normalizedModifiedPath) as ModelImporter;
                if (importer == null) continue;
                
                DerivedSettings settings = null;
                try { settings = JsonUtility.FromJson<DerivedSettings>(importer.userData); } catch { /* ignore */ }
                if (settings == null || string.IsNullOrEmpty(settings.baseGuid)) continue;
                
                var basePath = AssetDatabase.GUIDToAssetPath(settings.baseGuid);
                if (string.IsNullOrEmpty(basePath))
                {
                    Debug.LogWarning($"[PackageBuilder] Derived FBX has no resolvable Base FBX: {modifiedPath}");
                    continue;
                }
                
                var policy = new PatchPackage.Policy
                {
                    autoApplyThreshold = settings.autoApplyThreshold,
                    reviewThreshold = settings.reviewThreshold,
                    strictTopology = settings.strictTopology
                };
                var hints = new PatchPackage.UIHints
                {
                    friendlyName = string.IsNullOrEmpty(settings.friendlyName)
                        ? System.IO.Path.GetFileNameWithoutExtension(modifiedPath)
                        : settings.friendlyName,
                    thumbnail = null,
                    category = settings.category
                };
                
                var seeds = new PatchPackage.SeedMaps();
                
                // Ensure folders exist before building
                try
                {
                    EnsureAuthoringFolder();
                }
                catch (Exception folderEx)
                {
                    Debug.LogError($"[PackageBuilder] Failed to ensure authoring folder: {folderEx.Message}\n{folderEx.StackTrace}");
                    throw; // Re-throw to prevent continuing with invalid state
                }
                
                try
                {
                    var build = PatchBuilder.Build(basePath, normalizedModifiedPath, policy, hints, seeds);
                    
                    // Save PatchPackage.asset in temp package folder
                    string pkgPath = AssetDatabase.GenerateUniqueAssetPath($"Packages/com.yucp.temp/Patches/PatchPackage_{SanitizeFileName(hints.friendlyName)}.asset");
                    AssetDatabase.CreateAsset(build.patch, pkgPath);
                    // DON'T call SaveAssets() - it triggers reserialization loops
                    // The asset is created and will be included in export by path
                    
                    // CRITICAL: Update the script GUID reference in the .asset file to point to the PatchPackage script in temp package
                    // The asset was created with a reference to the devtools script, but it needs to reference the temp package script
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string physicalAssetPath = Path.Combine(projectPath, pkgPath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(physicalAssetPath))
                    {
                        // Find the PatchPackage.cs script GUID in the temp package
                        string patchPackageScriptPath = "Packages/com.yucp.temp/Editor/PatchPackage.cs";
                        string patchPackageScriptGuid = AssetDatabase.AssetPathToGUID(patchPackageScriptPath);
                        
                        if (!string.IsNullOrEmpty(patchPackageScriptGuid))
                        {
                            // Read the .asset file and update the script GUID and namespace references
                            string assetContent = File.ReadAllText(physicalAssetPath);
                            
                            // Replace the script GUID reference (format: guid: OLDGUID)
                            // Pattern: m_Script: {fileID: 11500000, guid: OLDGUID, type: 3}
                            var guidPattern = new System.Text.RegularExpressions.Regex(@"m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([a-f0-9]{32}),\s*type:\s*\d+\}");
                            if (guidPattern.IsMatch(assetContent))
                            {
                                assetContent = guidPattern.Replace(assetContent, $"m_Script: {{fileID: 11500000, guid: {patchPackageScriptGuid}, type: 3}}");
                            }
                            
                            // Replace namespace references for nested types (e.g., BlendshapeOp, MeshDeltaOp)
                            // Pattern: YUCP.DevTools.Editor.PackageExporter.PatchPackage/TypeName
                            assetContent = System.Text.RegularExpressions.Regex.Replace(
                                assetContent,
                                @"YUCP\.DevTools\.Editor\.PackageExporter\.PatchPackage/(\w+)",
                                "YUCP.PatchRuntime.PatchPackage/$1"
                            );
                            
                            // Also replace the assembly reference if present
                            assetContent = System.Text.RegularExpressions.Regex.Replace(
                                assetContent,
                                @"com\.yucp\.devtools\.Editor",
                                "YUCP.PatchRuntime"
                            );
                            
                            File.WriteAllText(physicalAssetPath, assetContent);
                            Debug.Log($"[PackageBuilder] Updated script GUID and namespace references in {pkgPath} to point to temp package");
                        }
                        else
                        {
                            Debug.LogWarning($"[PackageBuilder] Could not find PatchPackage.cs script GUID in temp package - asset may not load correctly");
                        }
                        
                        patchAssetsToAdd.Add(pkgPath);
                    }
                    else
                    {
                        Debug.LogWarning($"[PackageBuilder] PatchPackage.asset was not created at {pkgPath}");
                    }
                    
                    // Add all sidecar assets using stored paths (more reliable than GetAssetPath)
                    int addedSidecars = 0;
                    foreach (var sidecarPath in build.generatedSidecarPaths)
                    {
                        try
                        {
                            // Verify file exists physically (reuse projectPath from above)
                            string sidecarPhysicalPath = Path.Combine(projectPath, sidecarPath.Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(sidecarPhysicalPath))
                            {
                                patchAssetsToAdd.Add(sidecarPath);
                                addedSidecars++;
                            }
                        }
                        catch (Exception sidecarEx)
                        {
                            Debug.LogWarning($"[PackageBuilder] Failed to add sidecar asset {sidecarPath}: {sidecarEx.Message}");
                        }
                    }
                    
                    // Mark FBX for removal from export list (always remove, even if some sidecars failed)
                    fbxToRemove.Add(modifiedPath);
                    
                    Debug.Log($"[PackageBuilder] Generated PatchPackage for derived FBX: {modifiedPath} → {pkgPath} (removing FBX from export, added {addedSidecars}/{build.generatedSidecars.Count} sidecars)");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PackageBuilder] Failed to build patch for {modifiedPath}: {ex.Message}\n{ex.StackTrace}");
                    // Don't remove FBX if patch building completely failed - let user export the FBX as-is
                }
            }
            
            // Remove derived FBXs from export list
            if (fbxToRemove.Count > 0)
            {
                Debug.Log($"[PackageBuilder] Attempting to remove {fbxToRemove.Count} derived FBX(s) from export list. FBXs to remove: {string.Join(", ", fbxToRemove)}");
                Debug.Log($"[PackageBuilder] Current export list has {assetsToExport.Count} items. Sample: {string.Join(", ", assetsToExport.Take(5))}");
                
                // Normalize paths for comparison (handle both absolute and relative)
                // Use a helper function to normalize consistently
                Func<string, string> normalizePath = (p) =>
                {
                    if (string.IsNullOrEmpty(p)) return p;
                    string normalized = p.Replace('\\', '/');
                    if (Path.IsPathRooted(normalized))
                    {
                        string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
                        if (normalized.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                        {
                            normalized = normalized.Substring(projectPath.Length).TrimStart('/');
                        }
                    }
                    return normalized;
                };
                
                var normalizedToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in fbxToRemove)
                {
                    string normalized = normalizePath(path);
                    normalizedToRemove.Add(normalized);
                    Debug.Log($"[PackageBuilder] Normalized removal target: '{path}' → '{normalized}'");
                }
                
                int removedCount = 0;
                var toRemove = new List<string>();
                foreach (var path in assetsToExport)
                {
                    string normalized = normalizePath(path);
                    if (normalizedToRemove.Contains(normalized))
                    {
                        toRemove.Add(path);
                        Debug.Log($"[PackageBuilder] Marking for removal: '{path}' (normalized: '{normalized}')");
                    }
                }
                
                foreach (var path in toRemove)
                {
                    assetsToExport.Remove(path);
                    removedCount++;
                }
                
                Debug.Log($"[PackageBuilder] Removed {removedCount} derived FBX(s) from export list. Remaining count: {assetsToExport.Count}");
            }
            
            // Add PatchPackage and sidecars to export list
            if (patchAssetsToAdd.Count > 0)
            {
                foreach (var patchAsset in patchAssetsToAdd)
                {
                    if (!assetsToExport.Contains(patchAsset))
                    {
                        assetsToExport.Add(patchAsset);
                    }
                }
                Debug.Log($"[PackageBuilder] Added {patchAssetsToAdd.Count} patch asset(s) to export list");
                return patchAssetsToAdd.Count > 0;
            }
            
            return false;
        }
        
        [Serializable]
        private class DerivedSettings
        {
            public bool isDerived;
            public string baseGuid;
            public float autoApplyThreshold = 0.8f;
            public float reviewThreshold = 0.4f;
            public bool strictTopology = false;
            public string friendlyName;
            public string category;
        }
        
        private static void EnsureAuthoringFolder()
        {
            // Use Packages/com.yucp.temp for patch assets (temp folder that can be cleaned up)
            string tempPackagePath = "Packages/com.yucp.temp";
            string patchesPath = $"{tempPackagePath}/Patches";
            
            // Packages folder structure is different - create via directory operations
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string tempPackagePhysicalPath = Path.Combine(projectPath, tempPackagePath.Replace('/', Path.DirectorySeparatorChar));
            string patchesPhysicalPath = Path.Combine(projectPath, patchesPath.Replace('/', Path.DirectorySeparatorChar));
            
            bool createdPackage = false;
            bool createdPatches = false;
            
            // Create directories if they don't exist
            if (!Directory.Exists(tempPackagePhysicalPath))
            {
                Directory.CreateDirectory(tempPackagePhysicalPath);
                createdPackage = true;
            }
            if (!Directory.Exists(patchesPhysicalPath))
            {
                Directory.CreateDirectory(patchesPhysicalPath);
                createdPatches = true;
            }
            
            // Create package.json for the temp package
            string packageJsonPath = Path.Combine(tempPackagePhysicalPath, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                string packageJson = @"{
  ""name"": ""com.yucp.temp"",
  ""version"": ""0.0.1"",
  ""displayName"": ""YUCP Temporary Patch Assets"",
  ""description"": ""Temporary folder for YUCP patch assets. Can be safely deleted after reverting patches."",
  ""unity"": ""2019.4"",
  ""hideInEditor"": true
}";
                File.WriteAllText(packageJsonPath, packageJson);
            }
            
            // Create .meta files if needed
            if (createdPackage)
            {
                CreateMetaFileIfNeeded(tempPackagePhysicalPath);
            }
            if (createdPatches)
            {
                CreateMetaFileIfNeeded(patchesPhysicalPath);
            }
            
            // Don't force import - just refresh once to avoid reserialization loops
            // Unity will recognize the folders naturally
            AssetDatabase.Refresh();
            
            // Verify folder exists (check physical path as fallback since IsValidFolder may not work immediately)
            if (!Directory.Exists(patchesPhysicalPath))
            {
                throw new InvalidOperationException($"Failed to create {patchesPath} folder (physical path does not exist).");
            }
        }
        
        private static void CreateMetaFileIfNeeded(string physicalPath)
        {
            string metaPath = physicalPath + ".meta";
            if (!File.Exists(metaPath))
            {
                string guid = System.Guid.NewGuid().ToString("N");
                string metaContent = $"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                File.WriteAllText(metaPath, metaContent);
            }
        }
        
        private static string SanitizeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
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
            bool hasPatchAssets,
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
                
                // 2a. Inject Mini Package Guardian (permanent protection layer)
                // Only inject if com.yucp.components is NOT already a dependency (to avoid duplication)
                bool hasYucpComponentsDependency = profile.dependencies != null && 
                    profile.dependencies.Any(d => d.enabled && d.packageName == "com.yucp.components");
                
                if (!hasYucpComponentsDependency)
                {
                    // Find the Mini Guardian
                    string guardianScriptPath = null;
                    string[] foundGuardians = AssetDatabase.FindAssets("PackageGuardianMini t:Script");
                    
                    if (foundGuardians.Length > 0)
                    {
                        guardianScriptPath = AssetDatabase.GUIDToAssetPath(foundGuardians[0]);
                        Debug.Log($"[PackageBuilder] Found Mini Guardian at: {guardianScriptPath}");
                    }
                    
                    if (!string.IsNullOrEmpty(guardianScriptPath) && File.Exists(guardianScriptPath))
                    {
                        // Get the guardian directory to find dependencies
                        string guardianDir = Path.GetDirectoryName(guardianScriptPath);
                        string packageGuardianDir = Directory.GetParent(guardianDir).FullName; // Go up to PackageGuardian folder
                        
                        // 1. Inject GuardianTransaction.cs (core dependency)
                        string transactionPath = Path.Combine(packageGuardianDir, "Core", "Transactions", "GuardianTransaction.cs");
                        if (File.Exists(transactionPath))
                        {
                            string transactionGuid = Guid.NewGuid().ToString("N");
                            string transactionFolder = Path.Combine(tempExtractDir, transactionGuid);
                            Directory.CreateDirectory(transactionFolder);
                            
                            string transactionContent = File.ReadAllText(transactionPath);
                            File.WriteAllText(Path.Combine(transactionFolder, "asset"), transactionContent);
                            // Place in Editor folder so it compiles with Editor scripts
                            File.WriteAllText(Path.Combine(transactionFolder, "pathname"), "Packages/yucp.packageguardian/Editor/Core/Transactions/GuardianTransaction.cs");
                            
                            string transactionMeta = "fileFormatVersion: 2\nguid: " + transactionGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                            File.WriteAllText(Path.Combine(transactionFolder, "asset.meta"), transactionMeta);
                            
                            Debug.Log("[PackageBuilder] Added GuardianTransaction.cs (core dependency)");
                        }
                        
                        // 2. Inject PackageGuardianMini.cs
                        string guardianGuid = Guid.NewGuid().ToString("N");
                        string guardianFolder = Path.Combine(tempExtractDir, guardianGuid);
                        Directory.CreateDirectory(guardianFolder);
                        
                        string guardianContent = File.ReadAllText(guardianScriptPath);
                        File.WriteAllText(Path.Combine(guardianFolder, "asset"), guardianContent);
                        File.WriteAllText(Path.Combine(guardianFolder, "pathname"), "Packages/yucp.packageguardian/Editor/PackageGuardianMini.cs");
                        
                        string guardianMeta = "fileFormatVersion: 2\nguid: " + guardianGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(guardianFolder, "asset.meta"), guardianMeta);
                        
                        // 3. Create package.json for the guardian package
                        string guardianPackageJsonGuid = Guid.NewGuid().ToString("N");
                        string guardianPackageJsonFolder = Path.Combine(tempExtractDir, guardianPackageJsonGuid);
                        Directory.CreateDirectory(guardianPackageJsonFolder);
                        
                        string guardianPackageJson = @"{
  ""name"": ""yucp.packageguardian"",
  ""displayName"": ""YUCP Package Guardian (Mini)"",
  ""version"": ""2.0.0"",
  ""description"": ""Lightweight import protection for YUCP packages. Detects duplicates, reverts failed imports, and ensures error-free installation."",
  ""unity"": ""2019.4""
}";
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "asset"), guardianPackageJson);
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "pathname"), "Packages/yucp.packageguardian/package.json");
                        
                        string guardianPackageJsonMeta = "fileFormatVersion: 2\nguid: " + guardianPackageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "asset.meta"), guardianPackageJsonMeta);
                        
                        Debug.Log("[PackageBuilder] Added Mini Package Guardian (automatic import protection with transaction rollback)");
                    }
                    else
                    {
                        Debug.LogWarning("[PackageBuilder] Could not find PackageGuardianMini.cs - package will have no import protection!");
                        Debug.LogWarning("[PackageBuilder] Install com.yucp.components to enable Mini Guardian bundling");
                    }
                }
                else
                {
                    Debug.Log("[PackageBuilder] Skipping Mini Guardian injection (com.yucp.components already provides full Package Guardian)");
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
                    
                    // Also inject InstallerTransactionManager.cs (required dependency for InstallerTxn)
                    string txnManagerPath = Path.Combine(installerDir, "InstallerTransactionManager.cs");
                    if (File.Exists(txnManagerPath))
                    {
                        string txnManagerGuid = Guid.NewGuid().ToString("N");
                        string txnManagerFolder = Path.Combine(tempExtractDir, txnManagerGuid);
                        Directory.CreateDirectory(txnManagerFolder);
                        
                        string txnManagerContent = File.ReadAllText(txnManagerPath);
                        File.WriteAllText(Path.Combine(txnManagerFolder, "asset"), txnManagerContent);
                        File.WriteAllText(Path.Combine(txnManagerFolder, "pathname"), $"Assets/Editor/YUCP_InstallerTxn_{installerGuid}.cs");
                        
                        string txnManagerMeta = "fileFormatVersion: 2\nguid: " + txnManagerGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(txnManagerFolder, "asset.meta"), txnManagerMeta);
                        
                        Debug.Log("[PackageBuilder] Added InstallerTransactionManager.cs to package");
                    }
                    else
                    {
                        Debug.LogWarning("[PackageBuilder] Could not find InstallerTransactionManager.cs template - installer will fail to compile!");
                    }
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
                
                // 2c. Inject patch runtime scripts if patch assets are present
                if (hasPatchAssets)
                {
                    progressCallback?.Invoke(0.70f, "Injecting patch runtime scripts...");
                    
                    string[] patchScripts = new string[]
                    {
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Data/PatchPackage.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Data/MeshDeltaAsset.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Data/UVLayerAsset.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Data/MaterialOverrideAsset.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Data/BlendshapeFrameAsset.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Data/AppliedPatchState.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/ManifestBuilder.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/Correspondence/MapBuilder.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/Apply/Applicator.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/Backup/BackupManager.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/Validator.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Templates/YUCPPatchCleanup.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Templates/YUCPPatchImporter.cs"
                    };
                    
                    int injectedPatchScripts = 0;
                    foreach (var scriptPath in patchScripts)
                    {
                        string sourceScriptPath = null;
                        
                        // First, try direct path (most reliable)
                        if (File.Exists(scriptPath))
                        {
                            sourceScriptPath = scriptPath;
                        }
                        else
                        {
                            // Fallback: Try to find by filename using AssetDatabase
                            string fileName = Path.GetFileNameWithoutExtension(scriptPath);
                            string[] patchFoundScripts = AssetDatabase.FindAssets($"{fileName} t:Script");
                            
                            // Filter results to find the exact match
                            foreach (var guid in patchFoundScripts)
                            {
                                string foundPath = AssetDatabase.GUIDToAssetPath(guid);
                                if (foundPath.EndsWith(scriptPath.Replace("Packages/com.yucp.devtools/", ""), StringComparison.OrdinalIgnoreCase) ||
                                    foundPath.Replace("\\", "/").EndsWith(scriptPath.Replace("Packages/com.yucp.devtools/", ""), StringComparison.OrdinalIgnoreCase))
                                {
                                    sourceScriptPath = foundPath;
                                    break;
                                }
                            }
                            
                            // If still not found, use first result (best effort)
                            if (string.IsNullOrEmpty(sourceScriptPath) && patchFoundScripts.Length > 0)
                            {
                                sourceScriptPath = AssetDatabase.GUIDToAssetPath(patchFoundScripts[0]);
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(sourceScriptPath) && File.Exists(sourceScriptPath))
                        {
                            string scriptGuid = Guid.NewGuid().ToString("N");
                            string scriptFolder = Path.Combine(tempExtractDir, scriptGuid);
                            Directory.CreateDirectory(scriptFolder);
                            
                            string scriptContent = File.ReadAllText(sourceScriptPath);
                            // Replace namespace to remove DevTools reference
                            scriptContent = scriptContent.Replace(
                                "namespace YUCP.DevTools.Editor.PackageExporter",
                                "namespace YUCP.PatchRuntime"
                            );
                            scriptContent = scriptContent.Replace(
                                "using YUCP.DevTools.Editor.PackageExporter",
                                "using YUCP.PatchRuntime"
                            );
                            
                            string fileName = Path.GetFileName(sourceScriptPath);
                            string targetPath = $"Packages/com.yucp.temp/Editor/{fileName}";
                            
                            File.WriteAllText(Path.Combine(scriptFolder, "asset"), scriptContent);
                            File.WriteAllText(Path.Combine(scriptFolder, "pathname"), targetPath);
                            
                            string scriptMeta = "fileFormatVersion: 2\nguid: " + scriptGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                            File.WriteAllText(Path.Combine(scriptFolder, "asset.meta"), scriptMeta);
                            
                            Debug.Log($"[PackageBuilder] Injected patch script: {fileName} -> {targetPath} (from {sourceScriptPath})");
                            injectedPatchScripts++;
                        }
                        else
                        {
                            Debug.LogError($"[PackageBuilder] Could not find patch script: {scriptPath}. Tried direct path and AssetDatabase search.");
                        }
                    }
                    
                    // Also inject package.json for com.yucp.temp (required for Unity to recognize it as a package)
                    string tempPackageJsonPath = Path.Combine(Application.dataPath, "..", "Packages", "com.yucp.temp", "package.json");
                    string tempPackageJsonGuid = Guid.NewGuid().ToString("N");
                    string tempPackageJsonFolder = Path.Combine(tempExtractDir, tempPackageJsonGuid);
                    Directory.CreateDirectory(tempPackageJsonFolder);
                    
                    string tempPackageJsonContent;
                    if (File.Exists(tempPackageJsonPath))
                    {
                        tempPackageJsonContent = File.ReadAllText(tempPackageJsonPath);
                        Debug.Log("[PackageBuilder] Injected package.json for com.yucp.temp into package");
                    }
                    else
                    {
                        // Create a default package.json if it doesn't exist
                        tempPackageJsonContent = @"{
  ""name"": ""com.yucp.temp"",
  ""version"": ""0.0.1"",
  ""displayName"": ""YUCP Temporary Patch Assets"",
  ""description"": ""Temporary folder for YUCP patch assets. Can be safely deleted after reverting patches."",
  ""unity"": ""2019.4"",
  ""hideInEditor"": true
}";
                        Debug.Log("[PackageBuilder] Created and injected default package.json for com.yucp.temp into package");
                    }
                    
                    File.WriteAllText(Path.Combine(tempPackageJsonFolder, "asset"), tempPackageJsonContent);
                    File.WriteAllText(Path.Combine(tempPackageJsonFolder, "pathname"), "Packages/com.yucp.temp/package.json");
                    
                    string tempPackageJsonMeta = "fileFormatVersion: 2\nguid: " + tempPackageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(tempPackageJsonFolder, "asset.meta"), tempPackageJsonMeta);
                    
                    // CRITICAL: Create an assembly definition (.asmdef) file for the Editor scripts
                    // Scripts in Packages folders need an .asmdef to compile properly
                    string asmdefGuid = Guid.NewGuid().ToString("N");
                    string asmdefFolder = Path.Combine(tempExtractDir, asmdefGuid);
                    Directory.CreateDirectory(asmdefFolder);
                    
                    string asmdefContent = @"{
  ""name"": ""YUCP.PatchRuntime"",
  ""rootNamespace"": """",
  ""references"": [],
  ""includePlatforms"": [
    ""Editor""
  ],
  ""excludePlatforms"": [],
  ""allowUnsafeCode"": false,
  ""overrideReferences"": false,
  ""precompiledReferences"": [],
  ""autoReferenced"": true,
  ""defineConstraints"": [],
  ""versionDefines"": [],
  ""noEngineReferences"": false
}";
                    File.WriteAllText(Path.Combine(asmdefFolder, "asset"), asmdefContent);
                    File.WriteAllText(Path.Combine(asmdefFolder, "pathname"), "Packages/com.yucp.temp/Editor/YUCP.PatchRuntime.asmdef");
                    
                    string asmdefMeta = "fileFormatVersion: 2\nguid: " + asmdefGuid + "\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(asmdefFolder, "asset.meta"), asmdefMeta);
                    
                    Debug.Log("[PackageBuilder] Created and injected assembly definition for patch runtime scripts");
                    
                    if (injectedPatchScripts > 0)
                    {
                        Debug.Log($"[PackageBuilder] Injected {injectedPatchScripts} patch runtime script(s) into package");
                    }
                    else
                    {
                        Debug.LogWarning("[PackageBuilder] Could not find patch runtime scripts - patch functionality will not work!");
                    }
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

