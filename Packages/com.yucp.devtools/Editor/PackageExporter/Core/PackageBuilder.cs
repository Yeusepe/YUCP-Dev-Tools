using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
#endif
using UnityEditor;
using UnityEngine;
using YUCP.Components.Editor.PackageManager;
using PackageVerifierData = YUCP.Components.Editor.PackageVerifier.Data;
using YUCP.DevTools.Editor.PackageSigning.Core;
using PackageSigningData = YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Orchestrates the complete package export process including obfuscation and icon injection.
    /// Handles validation, folder filtering, DLL obfuscation, and final package creation.
    /// </summary>
    public static class PackageBuilder
    {
        internal static bool s_isExporting = false;
        
        private const string DefaultGridPlaceholderPath = "Packages/com.yucp.devtools/Resources/DefaultGrid.png";
        
        private static bool IsDefaultGridPlaceholder(Texture2D texture)
        {
            if (texture == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(texture);
            return assetPath == DefaultGridPlaceholderPath;
        }
        
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
        /// Export a package using the provided profile
        /// </summary>
        public static ExportResult ExportPackage(ExportProfile profile, Action<float, string> progressCallback = null)
        {
            var result = new ExportResult();
            var startTime = DateTime.Now;
            
            // Set exporting flag
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
                    
                    
                    if (!ConfuserExManager.ObfuscateAssemblies(
                        profile.assembliesToObfuscate,
                        profile.obfuscationPreset,
                        (progress, status) =>
                        {
                            progressCallback?.Invoke(0.2f + progress * 0.3f, status);
                        },
                        profile.advancedObfuscationSettings))
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
                
                int excludedCount = assetsToExport.RemoveAll(assetPath =>
                {
                    string fullPath = Path.GetFullPath(assetPath);
                    if (ShouldExcludeAsset(assetPath, profile))
                    {
                        Debug.LogWarning($"[PackageBuilder] Force-excluding asset in excluded folder: {assetPath}");
                        return true;
                    }
                    return false;
                });
                
                if (excludedCount > 0)
                {
                    Debug.Log($"[PackageBuilder] Force-excluded {excludedCount} assets that were in excluded folders");
                }
                
                if (profile.icon != null && !IsDefaultGridPlaceholder(profile.icon))
                {
                    string iconPath = AssetDatabase.GetAssetPath(profile.icon);
                    if (!string.IsNullOrEmpty(iconPath) && iconPath.StartsWith("Assets/"))
                    {
                        // Convert to relative path and add if not already included
                        string relativeIconPath = GetRelativePackagePath(iconPath);
                        if (!string.IsNullOrEmpty(relativeIconPath) && !assetsToExport.Contains(relativeIconPath))
                        {
                            assetsToExport.Add(relativeIconPath);
                            Debug.Log($"[PackageBuilder] Added icon texture to export: {relativeIconPath}");
                        }
                    }
                }

                if (profile.banner != null)
                {
                    string bannerPath = AssetDatabase.GetAssetPath(profile.banner);
                    if (!string.IsNullOrEmpty(bannerPath) && bannerPath.StartsWith("Assets/"))
                    {
                        // Convert to relative path and add if not already included
                        string relativeBannerPath = GetRelativePackagePath(bannerPath);
                        if (!string.IsNullOrEmpty(relativeBannerPath) && !assetsToExport.Contains(relativeBannerPath))
                        {
                            assetsToExport.Add(relativeBannerPath);
                            Debug.Log($"[PackageBuilder] Added banner texture to export: {relativeBannerPath}");
                        }
                    }
                }

                // Add product link icons to export (both customIcon and auto-fetched icon)
                if (profile.productLinks != null)
                {
                    foreach (var link in profile.productLinks)
                    {
                        // Check customIcon first, then auto-fetched icon
                        Texture2D iconToAdd = link.customIcon ?? link.icon;
                        if (iconToAdd != null)
                        {
                            string linkIconPath = AssetDatabase.GetAssetPath(iconToAdd);
                            
                            // If icon is not a Unity asset (e.g., loaded from URL), save it as a temporary asset
                            if (string.IsNullOrEmpty(linkIconPath) || !linkIconPath.StartsWith("Assets/"))
                            {
                                Debug.Log($"[PackageBuilder] Product link '{link.label}' has icon but it's not a project asset. Saving as temporary asset...");
                                linkIconPath = SaveTextureAsTemporaryAsset(iconToAdd, link.label ?? "ProductLink");
                                if (!string.IsNullOrEmpty(linkIconPath))
                                {
                                    Debug.Log($"[PackageBuilder] Saved product link icon as temporary asset: {linkIconPath}");
                                    
                                    // Load the saved asset and assign it back to link.icon so it persists
                                    // This ensures the icon is available when GeneratePackageMetadataJson is called
                                    Texture2D savedIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(linkIconPath);
                                    if (savedIcon != null && link.customIcon == null)
                                    {
                                        link.icon = savedIcon;
                                        EditorUtility.SetDirty(profile);
                                        Debug.Log($"[PackageBuilder] Updated link.icon to reference saved asset: {linkIconPath}");
                                    }
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(linkIconPath) && linkIconPath.StartsWith("Assets/"))
                            {
                                // Convert to relative path and add if not already included
                                string relativeLinkIconPath = GetRelativePackagePath(linkIconPath);
                                if (!string.IsNullOrEmpty(relativeLinkIconPath) && !assetsToExport.Contains(relativeLinkIconPath))
                                {
                                    assetsToExport.Add(relativeLinkIconPath);
                                    Debug.Log($"[PackageBuilder] Added product link icon to export: {relativeLinkIconPath} (source: {(link.customIcon != null ? "customIcon" : "icon")})");
                                }
                            }
                        }
                    }
                }
                
                // Manually collect dependencies if enabled (respects ignore list)
                CollectFilteredDependencies(assetsToExport, profile, progressCallback);
                
                progressCallback?.Invoke(0.54f, $"Total assets after dependency collection: {assetsToExport.Count}");
                
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
                        }
                        else
                        {
                            Debug.LogWarning($"[PackageBuilder] Bundled package not found: {dep.packageName}");
                        }
                    }
                }
                
                 // Generate package.json if needed (will inject later)
                 string packageJsonContent = null;
                 if (profile.generatePackageJson)
                 {
                     progressCallback?.Invoke(0.58f, "Generating package.json...");
                     
                     packageJsonContent = DependencyScanner.GeneratePackageJson(
                         profile,
                         profile.dependencies,
                         null
                     );
                 }
                
                result.filesExported = assetsToExport.Count;                
                // Create temp package path
                progressCallback?.Invoke(0.6f, "Exporting Unity package...");
                
                string tempPackagePath = Path.Combine(Path.GetTempPath(), $"YUCP_Temp_{Guid.NewGuid():N}.unitypackage");
                
                 ExportPackageOptions options = ExportPackageOptions.Default;
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
                
                progressCallback?.Invoke(0.64f, "Performing final ignore list check...");
                int ignoredRemoved = finalValidAssets.RemoveAll(assetPath =>
                {
                    if (ShouldExcludeAsset(assetPath, profile))
                    {
                        Debug.LogWarning($"[PackageBuilder] Final filter: Removing asset in ignored folder: {assetPath}");
                        return true;
                    }
                    return false;
                });
                
                if (ignoredRemoved > 0)
                {
                    Debug.LogWarning($"[PackageBuilder] Final filter removed {ignoredRemoved} asset(s) that were in ignored folders");
                }
                
                // Only specific DerivedFbxAsset files are added
                // are added via patchAssetsToAdd in ConvertDerivedFbxToPatchAssets
                
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
                
                
                // Generate package metadata JSON
                string packageMetadataJson = GeneratePackageMetadataJson(profile);
                
                // Inject package.json, auto-installer, bundled packages, and metadata into the .unitypackage
                if (!string.IsNullOrEmpty(packageJsonContent) || bundledPackagePaths.Count > 0 || !string.IsNullOrEmpty(packageMetadataJson))
                {
                    progressCallback?.Invoke(0.75f, "Injecting package.json, installer, bundled packages, and metadata...");
                    
                    try
                    {
                        // Pass obfuscated assemblies info so bundled packages can replace source with DLLs
                        var obfuscatedAssemblies = profile.enableObfuscation 
                            ? profile.assembliesToObfuscate.Where(a => a.enabled).ToList() 
                            : new List<AssemblyObfuscationSettings>();
                        
                        InjectPackageJsonInstallerAndBundles(tempPackagePath, packageJsonContent, bundledPackagePaths, obfuscatedAssemblies, profile, hasPatchAssets, packageMetadataJson, progressCallback);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageBuilder] Failed to inject content: {ex.Message}");
                    }
                }
                
                // At this point tempPackagePath contains the full package contents
                // (all assets, package.json/installer/bundles/metadata), but no icon
                // and no signing data yet.
                
                // Get final output path
                string finalOutputPath = profile.GetOutputFilePath();
                
                // Ensure output directory exists
                string outputDir = Path.GetDirectoryName(finalOutputPath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                // We'll produce the final signed package at this path:
                string contentPackagePath = tempPackagePath;
                
                if (profile.icon != null && !IsDefaultGridPlaceholder(profile.icon))
                {
                    progressCallback?.Invoke(0.8f, "Adding package icon...");
                    
                    string iconPath = AssetDatabase.GetAssetPath(profile.icon);
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        string fullIconPath = Path.GetFullPath(iconPath);
                        
                        // Write icon-injected package to a new temp file, which becomes our content package
                        string iconTempPath = Path.Combine(Path.GetTempPath(), $"YUCP_ContentWithIcon_{Guid.NewGuid():N}.unitypackage");
                        
                        if (PackageIconInjector.AddIconToPackage(tempPackagePath, fullIconPath, iconTempPath))
                        {
                            contentPackagePath = iconTempPath;
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
                
                // Assign packageId if not already assigned (before signing)
                progressCallback?.Invoke(0.81f, "Assigning package ID...");
                string packageId = PackageIdManager.AssignPackageId(profile);
                if (string.IsNullOrEmpty(packageId))
                {
                    Debug.LogWarning("[PackageBuilder] Failed to assign packageId, continuing without it");
                }
                else
                {
                    Debug.Log($"[PackageBuilder] Using packageId: {packageId}");
                }

                // Sign package if certificate is available, using the fully-prepared contentPackagePath
                bool packageSigned = false;
                try
                {
                    var signingSettings = GetSigningSettings();
                    if (signingSettings != null && signingSettings.HasValidCertificate())
                    {
                        progressCallback?.Invoke(0.82f, "Signing package...");
                        packageSigned = SignPackageBeforeExport(contentPackagePath, profile, progressCallback);
                        if (packageSigned)
                        {
                            progressCallback?.Invoke(0.84f, "Package signed successfully");
                        }
                        else
                        {
                            Debug.LogWarning("[PackageBuilder] Package signing failed, continuing without signature");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageBuilder] Package signing error: {ex.Message}");
                }
                
                // Copy signed (or unsigned) content package to final location
                progressCallback?.Invoke(0.86f, "Copying package to output location...");
                File.Copy(contentPackagePath, finalOutputPath, true);
                
                progressCallback?.Invoke(0.9f, "Cleaning up temporary files...");
                
                if (packageSigned)
                {
                    try
                    {
                        SignatureEmbedder.RemoveSigningData();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageBuilder] Failed to clean up signing data: {ex.Message}");
                    }
                }
                
                // Clean up temp package
                if (File.Exists(tempPackagePath))
                {
                    File.Delete(tempPackagePath);
                }
                
                string tempEditorPath = "Packages/com.yucp.temp/Editor";
                if (AssetDatabase.IsValidFolder(tempEditorPath))
                {
                    try
                    {
                        AssetDatabase.DeleteAsset(tempEditorPath);
                        Debug.Log("[PackageBuilder] Cleaned up temp Editor folder");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageBuilder] Error cleaning up temp Editor folder: {ex.Message}");
                        // Fallback: try physical deletion
                        try
                        {
                            string physicalEditorPath = Path.Combine(Application.dataPath, "..", "Packages", "com.yucp.temp", "Editor");
                            if (Directory.Exists(physicalEditorPath))
                            {
                                Directory.Delete(physicalEditorPath, true);
                            }
                        }
                        catch (Exception ex2)
                        {
                            Debug.LogWarning($"[PackageBuilder] Fallback cleanup also failed: {ex2.Message}");
                        }
                    }
                }
                
                string tempPluginsPath = "Packages/com.yucp.temp/Plugins";
                if (AssetDatabase.IsValidFolder(tempPluginsPath))
                {
                    try
                    {
                        AssetDatabase.DeleteAsset(tempPluginsPath);
                        Debug.Log("[PackageBuilder] Cleaned up temp Plugins folder");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageBuilder] Error cleaning up temp Plugins folder: {ex.Message}");
                        // Fallback: try physical deletion
                        try
                        {
                            string physicalPluginsPath = Path.Combine(Application.dataPath, "..", "Packages", "com.yucp.temp", "Plugins");
                            if (Directory.Exists(physicalPluginsPath))
                            {
                                Directory.Delete(physicalPluginsPath, true);
                            }
                        }
                        catch (Exception ex2)
                        {
                            Debug.LogWarning($"[PackageBuilder] Fallback cleanup also failed: {ex2.Message}");
                        }
                    }
                }
                
                // Refresh AssetDatabase to reflect deletions
                AssetDatabase.Refresh();
                
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
                
                // Track export for milestones
                try
                {
                    System.Type milestoneTrackerType = null;
                    
                    // Try to find the type by searching through all loaded assemblies
                    foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        milestoneTrackerType = assembly.GetType("YUCP.Components.Editor.SupportBanner.MilestoneTracker");
                        if (milestoneTrackerType != null)
                            break;
                    }
                    
                    if (milestoneTrackerType != null)
                    {
                        var incrementMethod = milestoneTrackerType.GetMethod("IncrementExportCount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (incrementMethod != null)
                        {
                            incrementMethod.Invoke(null, null);
                        }
                    }
                }
                catch (System.Exception)
                {
                    // Silently fail milestone tracking
                }
                result.outputPath = finalOutputPath;
                result.buildTimeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;
                
                // Register exported package in ExportedPackageRegistry
                if (!string.IsNullOrEmpty(packageId))
                {
                    try
                    {
                        progressCallback?.Invoke(0.99f, "Registering export...");
                        var exportedRegistry = ExportedPackageRegistry.GetOrCreate();
                        var signingSettings = GetSigningSettings();
                        string publisherId = signingSettings?.publisherId ?? "";
                        
                        // Compute archive hash for registration
                        string archiveSha256 = "";
                        try
                        {
                            using (var sha256 = System.Security.Cryptography.SHA256.Create())
                            using (var stream = File.OpenRead(finalOutputPath))
                            {
                                byte[] hash = sha256.ComputeHash(stream);
                                archiveSha256 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[PackageBuilder] Failed to compute archive hash for registry: {ex.Message}");
                        }
                        
                        exportedRegistry.RegisterExport(
                            packageId,
                            profile.packageName,
                            publisherId,
                            profile.version,
                            archiveSha256,
                            finalOutputPath
                        );
                        
                        Debug.Log($"[PackageBuilder] Registered export: {packageId} v{profile.version}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageBuilder] Failed to register export: {ex.Message}");
                    }
                }
                
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
                // Clear exporting flag
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
            
            try
            {
                string patchesPath = "Packages/com.yucp.temp/Patches";
                if (AssetDatabase.IsValidFolder(patchesPath))
                {
                    string[] allGuids = AssetDatabase.FindAssets("", new[] { patchesPath });
                    int cleanedCount = 0;
                    foreach (var guid in allGuids)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        string fileName = Path.GetFileNameWithoutExtension(assetPath);
                        
                        if (assetPath.EndsWith(".asset") && 
                            (fileName.StartsWith("MeshDelta_") || 
                             fileName.StartsWith("UVLayer_") || 
                             fileName.StartsWith("Blendshape_") ||
                             fileName.StartsWith("PatchPackage_")))
                        {
                            AssetDatabase.DeleteAsset(assetPath);
                            cleanedCount++;
                        }
                    }
                    if (cleanedCount > 0)
                    {
                        AssetDatabase.Refresh();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PackageBuilder] Failed to clean up old sidecar assets: {ex.Message}");
            }
            
            // Gather derived FBXs in the export set
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
                    continue;
                }
                
                try
                {
                    string userDataJson = importer.userData;
                    
                    var settings = string.IsNullOrEmpty(userDataJson) ? null : JsonUtility.FromJson<DerivedSettings>(userDataJson);
                    if (settings != null && settings.isDerived && !string.IsNullOrEmpty(settings.baseGuid))
                    {
                        // Store original path for removal (will normalize during removal)
                        derivedFbxPaths.Add(assetPath);
                    }
                    else if (settings != null)
                    {
                        if (settings.isDerived && string.IsNullOrEmpty(settings.baseGuid))
                        {
                            Debug.LogWarning($"[PackageBuilder] FBX {assetPath} is marked as derived but has no baseGuid assigned. " +
                                $"Please assign the base FBX in the ModelImporter inspector. This FBX will NOT be converted to a PatchPackage.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageBuilder] Failed to parse userData for {assetPath}: {ex.Message}");
                }
            }
            
            
            // Warn if there are FBXs that might need to be marked as derived
            if (fbxCount > 0 && derivedFbxPaths.Count == 0)
            {
                Debug.LogWarning($"[PackageBuilder] Found {fbxCount} FBX(s) in export, but none are marked as 'derived'. " +
                    $"If any of these FBXs are modifications of a base FBX, mark them as 'Export As Patch (Derived)' in the ModelImporter inspector " +
                    $"and assign the base FBX. Otherwise, the full FBX will be exported instead of a PatchPackage.");
            }
            
            if (derivedFbxPaths.Count == 0) return false;
            
            progressCallback?.Invoke(0.51f, $"Converting {derivedFbxPaths.Count} derived FBX file(s) into PatchPackages...");
            
            EnsureAuthoringFolder();
            
            var fbxToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var patchAssetsToAdd = new List<string>();
            
            foreach (var modifiedPath in derivedFbxPaths)
            {
                // Normalize path for AssetImporter
                string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string normalizedModifiedPath = modifiedPath;
                if (Path.IsPathRooted(normalizedModifiedPath))
                {
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
                
                var policy = new DerivedFbxAsset.Policy();
                var hints = new DerivedFbxAsset.UIHints
                {
                    friendlyName = string.IsNullOrEmpty(settings.friendlyName)
                        ? System.IO.Path.GetFileNameWithoutExtension(modifiedPath)
                        : settings.friendlyName,
                    thumbnail = null,
                    category = settings.category
                };
                
                bool overrideOriginalReferences = settings.overrideOriginalReferences;
                
                var seeds = new DerivedFbxAsset.SeedMaps();
                
                // Read the derived FBX GUID from its .meta file
                string physicalModifiedPath = Path.IsPathRooted(normalizedModifiedPath) 
                    ? normalizedModifiedPath 
                    : Path.Combine(projectPath, normalizedModifiedPath.Replace('/', Path.DirectorySeparatorChar));
                string derivedFbxGuid = MetaFileManager.ReadGuid(physicalModifiedPath);
                
                if (string.IsNullOrEmpty(derivedFbxGuid))
                {
                    Debug.LogWarning($"[PackageBuilder] Could not read GUID from derived FBX .meta file: {normalizedModifiedPath}. A new GUID will be generated on import.");
                    // Generate a fallback identifier from the path
                    derivedFbxGuid = System.Guid.NewGuid().ToString("N");
                }
                
                try
                {
                    EnsureAuthoringFolder();
                }
                catch (Exception folderEx)
                {
                    Debug.LogError($"[PackageBuilder] Failed to ensure authoring folder: {folderEx.Message}\n{folderEx.StackTrace}");
                    throw;
                }
                
                try
                {
                    // Build DerivedFbxAsset with all data embedded
                    var derivedAsset = PatchBuilder.BuildDerivedFbxAsset(basePath, normalizedModifiedPath, policy, hints, seeds);
                    
                    if (derivedAsset == null)
                    {
                        Debug.LogError($"[PackageBuilder] BuildDerivedFbxAsset returned null for {modifiedPath}. .hdiff file creation may have failed.");
                        continue;
                    }
                    
                    // Store GUIDs for direct targeting and prefab compatibility
                    derivedAsset.baseFbxGuid = settings.baseGuid;
                    derivedAsset.derivedFbxGuid = derivedFbxGuid ?? string.Empty;
                    derivedAsset.originalDerivedFbxPath = normalizedModifiedPath;
                    derivedAsset.overrideOriginalReferences = overrideOriginalReferences;
                    
                    // Use derived FBX GUID as filename identifier
                    string fileName = $"DerivedFbxAsset_{derivedFbxGuid.Substring(0, 8)}_{SanitizeFileName(hints.friendlyName)}.asset";
                    string pkgPath = $"Packages/com.yucp.temp/Patches/{fileName}";
                    
                    // Delete existing file if it exists (same derived FBX = same patch asset file)
                    string physicalAssetPath = Path.Combine(projectPath, pkgPath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(physicalAssetPath))
                    {
                        File.Delete(physicalAssetPath);
                        if (File.Exists(physicalAssetPath + ".meta"))
                        {
                            File.Delete(physicalAssetPath + ".meta");
                        }
                        AssetDatabase.Refresh();
                    }
                    
                    try
                    {
                        AssetDatabase.CreateAsset(derivedAsset, pkgPath);
                    }
                    catch (Exception createEx)
                    {
                        Debug.LogError($"[PackageBuilder] Failed to create DerivedFbxAsset at {pkgPath}: {createEx.Message}\n{createEx.StackTrace}");
                        UnityEngine.Object.DestroyImmediate(derivedAsset);
                        continue;
                    }
                    
                    if (File.Exists(physicalAssetPath))
                    {
                        // Find the DerivedFbxAsset.cs script GUID in the temp package
                        string derivedFbxAssetScriptPath = "Packages/com.yucp.temp/Editor/DerivedFbxAsset.cs";
                        string derivedFbxAssetScriptGuid = AssetDatabase.AssetPathToGUID(derivedFbxAssetScriptPath);
                        
                        if (!string.IsNullOrEmpty(derivedFbxAssetScriptGuid))
                        {
                            // Read the .asset file and update the script GUID and namespace references
                            string assetContent = File.ReadAllText(physicalAssetPath);
                            
                            var guidPattern = new System.Text.RegularExpressions.Regex(@"m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([a-f0-9]{32}),\s*type:\s*\d+\}");
                            if (guidPattern.IsMatch(assetContent))
                            {
                                assetContent = guidPattern.Replace(assetContent, $"m_Script: {{fileID: 11500000, guid: {derivedFbxAssetScriptGuid}, type: 3}}");
                            }
                            
                            // Replace namespace references for nested types (e.g., EmbeddedBlendshapeOp, EmbeddedMeshDeltaOp)
                            // Unity serializes nested types in YAML format. The format can be:
                            // - m_Type: YUCP.DevTools.Editor.PackageExporter.DerivedFbxAsset/EmbeddedBlendshapeOp, YUCP.PatchRuntime
                            // - type: YUCP.DevTools.Editor.PackageExporter.DerivedFbxAsset/EmbeddedBlendshapeOp
                            // - YUCP.DevTools.Editor.PackageExporter.DerivedFbxAsset/EmbeddedBlendshapeOp, YUCP.PatchRuntime
                            
                            string originalContent = assetContent;
                            
                            // Use simple string replacement first (most reliable)
                            assetContent = assetContent.Replace(
                                "YUCP.DevTools.Editor.PackageExporter.DerivedFbxAsset/",
                                "YUCP.PatchRuntime.DerivedFbxAsset/"
                            );
                            assetContent = assetContent.Replace(
                                "YUCP.DevTools.Editor.PackageExporter",
                                "YUCP.PatchRuntime"
                            );
                            
                            assetContent = assetContent.Replace(
                                "com.yucp.devtools.Editor",
                                "YUCP.PatchRuntime"
                            );
                            assetContent = System.Text.RegularExpressions.Regex.Replace(
                                assetContent,
                                @"YUCP\.DevTools[^\s/]+/",
                                "YUCP.PatchRuntime.DerivedFbxAsset/",
                                System.Text.RegularExpressions.RegexOptions.Multiline
                            );
                            
                            assetContent = System.Text.RegularExpressions.Regex.Replace(
                                assetContent,
                                @"YUCP\.DevTools[^\s,}]+",
                                "YUCP.PatchRuntime",
                                System.Text.RegularExpressions.RegexOptions.Multiline
                            );
                            
                            if (assetContent != originalContent)
                            {
                                Debug.Log($"[PackageBuilder] Fixed namespace references in DerivedFbxAsset at {pkgPath}");
                                File.WriteAllText(physicalAssetPath, assetContent);
                                
                                string verifyContent = File.ReadAllText(physicalAssetPath);
                                if (verifyContent.Contains("YUCP.DevTools.Editor.PackageExporter"))
                                {
                                    Debug.LogWarning($"[PackageBuilder] Namespace fix may not have worked completely. Old namespace still found in {pkgPath}");
                                    // Try one more aggressive replacement
                                    verifyContent = verifyContent.Replace("YUCP.DevTools", "YUCP.PatchRuntime");
                                    File.WriteAllText(physicalAssetPath, verifyContent);
                                }
                                
                                // Force Unity to reimport the asset so it recognizes the namespace changes
                                AssetDatabase.ImportAsset(pkgPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate | ImportAssetOptions.DontDownloadFromCacheServer);
                                
                                // Give Unity a moment to process the import
                                AssetDatabase.Refresh();
                            }
                            else
                            {
                                // Even if no changes detected, ensure asset is saved
                                File.WriteAllText(physicalAssetPath, assetContent);
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[PackageBuilder] Could not find DerivedFbxAsset.cs script GUID in temp package - asset may not load correctly");
                        }
                        
                        patchAssetsToAdd.Add(pkgPath);
                        
                        // Add .hdiff file to export list
                        if (!string.IsNullOrEmpty(derivedAsset.hdiffFilePath))
                        {
                            string hdiffPhysicalPath = Path.Combine(projectPath, derivedAsset.hdiffFilePath.Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(hdiffPhysicalPath))
                            {
                                patchAssetsToAdd.Add(derivedAsset.hdiffFilePath);
                                Debug.Log($"[PackageBuilder] Added .hdiff file to export: {derivedAsset.hdiffFilePath}");
                            }
                            else
                            {
                                Debug.LogWarning($"[PackageBuilder] .hdiff file not found at: {hdiffPhysicalPath}");
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[PackageBuilder] DerivedFbxAsset.asset was not created at {pkgPath}");
                    }
                    
                    // Mark FBX for removal from export list (binary patch replaces the FBX)
                    fbxToRemove.Add(modifiedPath);
                    
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PackageBuilder] Failed to build patch for derived FBX {modifiedPath}: {ex.Message}\n{ex.StackTrace}");
                    Debug.LogError($"[PackageBuilder] The FBX will be exported as-is instead of being converted to a PatchPackage. " +
                        $"Please check that the base FBX exists and both FBXs have compatible mesh structures.");
                    // Keep FBX if patch building completely failed - let user export the FBX as-is
                }
            }
            
            // Remove derived FBXs from export list
            if (fbxToRemove.Count > 0)
            {
                
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
                }
                
                int removedCount = 0;
                var toRemove = new List<string>();
                foreach (var path in assetsToExport)
                {
                    string normalized = normalizePath(path);
                    if (normalizedToRemove.Contains(normalized))
                    {
                        toRemove.Add(path);
                    }
                }
                
                foreach (var path in toRemove)
                {
                    assetsToExport.Remove(path);
                    removedCount++;
                }
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
                return patchAssetsToAdd.Count > 0;
            }
            
            return false;
        }
        
        [Serializable]
        private class DerivedSettings
        {
            public bool isDerived;
            public string baseGuid;
            public string friendlyName;
            public string category;
            public bool overrideOriginalReferences = false;
        }
        
        private static void EnsureAuthoringFolder()
        {
            string tempPackagePath = "Packages/com.yucp.temp";
            string patchesPath = $"{tempPackagePath}/Patches";
            
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string tempPackagePhysicalPath = Path.Combine(projectPath, tempPackagePath.Replace('/', Path.DirectorySeparatorChar));
            string patchesPhysicalPath = Path.Combine(projectPath, patchesPath.Replace('/', Path.DirectorySeparatorChar));
            
            bool createdPackage = false;
            bool createdPatches = false;
            
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
            
            AssetDatabase.Refresh();
            
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
                    return existingPackageJsonPath;
                }
                
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
        /// Save a Texture2D as a temporary asset if it's not already a Unity asset.
        /// Returns the asset path, or null if saving failed.
        /// </summary>
        public static string SaveTextureAsTemporaryAsset(Texture2D texture, string baseName)
        {
            if (texture == null) return null;
            
            try
            {
                // Create temporary directory for product link icons
                string tempDir = "Assets/YUCP/Temp/ProductLinkIcons";
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                    AssetDatabase.Refresh();
                }
                
                // Generate unique filename
                string sanitizedName = SanitizeFileName(baseName);
                string fileName = $"{sanitizedName}_{texture.GetInstanceID()}.png";
                string filePath = Path.Combine(tempDir, fileName);
                
                // Encode texture to PNG bytes
                byte[] pngData = texture.EncodeToPNG();
                if (pngData == null || pngData.Length == 0)
                {
                    Debug.LogWarning($"[PackageBuilder] Failed to encode texture to PNG for '{baseName}'");
                    return null;
                }
                
                // Write PNG file
                File.WriteAllBytes(filePath, pngData);
                
                // Import the asset
                AssetDatabase.ImportAsset(filePath, ImportAssetOptions.ForceUpdate);
                
                // Return the asset path
                return filePath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Failed to save texture as temporary asset: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Generate YUCP_PackageInfo.json metadata for the export
        /// </summary>
        private static string GeneratePackageMetadataJson(ExportProfile profile)
        {
            try
            {
                // Create serializable metadata with string paths for icon/banner
                var metadataJson = new PackageMetadataJson
                {
                    packageName = profile.packageName ?? "",
                    version = profile.version ?? "",
                    author = profile.author ?? "",
                    description = profile.description ?? "",
                    productLinks = new List<ProductLinkJson>()
                };

                // Convert product links
                if (profile.productLinks != null)
                {
                    Debug.Log($"[PackageBuilder] Serializing {profile.productLinks.Count} product links");
                    foreach (var link in profile.productLinks)
                    {
                        var linkJson = new ProductLinkJson
                        {
                            label = link.label ?? "",
                            url = link.url ?? ""
                        };
                        
                        // Add icon path - check customIcon first, then auto-fetched icon
                        Texture2D iconToUse = link.customIcon ?? link.icon;
                        if (iconToUse != null)
                        {
                            string iconPath = AssetDatabase.GetAssetPath(iconToUse);
                            
                            // If icon is not a Unity asset (e.g., loaded from URL), save it as a temporary asset
                            if (string.IsNullOrEmpty(iconPath) || !iconPath.StartsWith("Assets/"))
                            {
                                Debug.Log($"[PackageBuilder] Product link '{link.label}' has icon but it's not a project asset. Saving as temporary asset...");
                                iconPath = SaveTextureAsTemporaryAsset(iconToUse, link.label ?? "ProductLink");
                                if (string.IsNullOrEmpty(iconPath))
                                {
                                    Debug.LogWarning($"[PackageBuilder] Failed to save product link icon as asset for '{link.label}'");
                                }
                                else
                                {
                                    Debug.Log($"[PackageBuilder] Saved product link icon as temporary asset: {iconPath}");
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(iconPath) && iconPath.StartsWith("Assets/"))
                            {
                                linkJson.icon = iconPath;
                                Debug.Log($"[PackageBuilder] Added product link icon path: {iconPath} for link '{link.label}' (source: {(link.customIcon != null ? "customIcon" : "icon")})");
                            }
                        }
                        else
                        {
                            Debug.Log($"[PackageBuilder] Product link '{link.label}' has no icon (neither customIcon nor icon)");
                        }
                        
                        metadataJson.productLinks.Add(linkJson);
                    }
                }
                else
                {
                    Debug.Log("[PackageBuilder] No product links to serialize");
                }

                // Get version rule name
                string versionRuleName = "semver";
                if (profile.customVersionRule != null && !string.IsNullOrEmpty(profile.customVersionRule.ruleName))
                {
                    versionRuleName = profile.customVersionRule.ruleName;
                }
                metadataJson.versionRule = versionRuleName;
                metadataJson.versionRuleName = versionRuleName;

                // Add icon path if exists
                if (profile.icon != null)
                {
                    string iconPath = AssetDatabase.GetAssetPath(profile.icon);
                    if (!string.IsNullOrEmpty(iconPath) && iconPath.StartsWith("Assets/"))
                    {
                        metadataJson.icon = iconPath;
                    }
                }

                // Add banner path if exists
                if (profile.banner != null)
                {
                    string bannerPath = AssetDatabase.GetAssetPath(profile.banner);
                    if (!string.IsNullOrEmpty(bannerPath) && bannerPath.StartsWith("Assets/"))
                    {
                        metadataJson.banner = bannerPath;
                    }
                }

                // Serialize to JSON
                return JsonUtility.ToJson(metadataJson, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Failed to generate package metadata: {ex.Message}");
                return null;
            }
        }

        [Serializable]
        private class PackageMetadataJson
        {
            public string packageName;
            public string version;
            public string author;
            public string description;
            public string icon;
            public string banner;
            public List<ProductLinkJson> productLinks;
            public string versionRule;
            public string versionRuleName;
        }

        [Serializable]
        private class ProductLinkJson
        {
            public string label;
            public string url;
            public string icon; // Path to custom icon texture
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
        /// Collect all assets to export using profile settings
        /// </summary>
        private static List<string> CollectAssetsToExport(ExportProfile profile)
        {
            var assets = new HashSet<string>();
            
            if (profile.HasScannedAssets && profile.discoveredAssets != null && profile.discoveredAssets.Count > 0)
            {
                
                // Only include assets that are explicitly marked as included AND not ignored
                var includedAssets = profile.discoveredAssets
                    .Where(a => a.included && !a.isFolder)
                    .Select(a => GetRelativePackagePath(a.assetPath))
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Where(assetPath => !ShouldExcludeAsset(assetPath, profile))
                    .ToList();
                
                
                return includedAssets;
            }
            
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
                
                if (!AssetDatabase.IsValidFolder(assetFolder))
                {
                    Debug.LogWarning($"[PackageBuilder] Folder not found in AssetDatabase: {assetFolder}");
                    continue;
                }
                
                AssetDatabase.Refresh();
                
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
        /// Check if an asset should be excluded using filters
        /// </summary>
        private static bool ShouldExcludeAsset(string assetPath, ExportProfile profile)
        {
            // Convert to full path for comprehensive exclusion checking
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullPath;
            
            // Handle both Unity-relative paths (Assets/...) and full paths
            if (Path.IsPathRooted(assetPath))
            {
                // Already a full path
                fullPath = Path.GetFullPath(assetPath);
            }
            else
            {
                // Unity-relative path - combine with project root
                fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            }
            
            return AssetCollector.ShouldIgnoreFile(fullPath, profile);
        }
        
        /// <summary>
        /// Manually collect and filter dependencies for assets, respecting ignore lists
        /// Recursively collects dependencies up to a maximum depth to catch nested dependencies
        /// </summary>
        private static void CollectFilteredDependencies(List<string> assetsToExport, ExportProfile profile, Action<float, string> progressCallback)
        {
            if (!profile.includeDependencies)
                return;
            
            progressCallback?.Invoke(0.53f, "Collecting dependencies (respecting ignore list)...");
            
            var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toProcess = new Queue<string>();
            const int maxDepth = 5; // Prevent infinite loops while still catching nested deps
            
            // Mark original assets as processed and add to queue for dependency collection
            foreach (var asset in assetsToExport)
            {
                string unityPath = GetRelativePackagePath(asset);
                if (!string.IsNullOrEmpty(unityPath))
                {
                    processedPaths.Add(unityPath);
                    toProcess.Enqueue(unityPath);
                }
            }
            
            // Recursively collect dependencies with depth limiting
            int depth = 0;
            int itemsAtCurrentDepth = toProcess.Count;
            
            while (toProcess.Count > 0 && depth < maxDepth)
            {
                if (itemsAtCurrentDepth == 0)
                {
                    depth++;
                    itemsAtCurrentDepth = toProcess.Count;
                }
                
                string assetPath = toProcess.Dequeue();
                itemsAtCurrentDepth--;
                
                try
                {
                    // Get dependencies (non-recursive - we handle recursion manually)
                    string[] dependencies = AssetDatabase.GetDependencies(assetPath, recursive: false);
                    
                    foreach (string dep in dependencies)
                    {
                        // Skip self-reference
                        if (dep == assetPath)
                            continue;
                        
                        // Skip if already processed
                        if (processedPaths.Contains(dep))
                            continue;
                        
                        processedPaths.Add(dep);
                        
                        // Check if dependency should be excluded BEFORE adding to queue
                        if (ShouldExcludeAsset(dep, profile))
                        {
                            Debug.Log($"[PackageBuilder] Excluding dependency (in ignore list): {dep}");
                            continue;
                        }
                        
                        // Add to dependencies and queue for further dependency collection
                        allDependencies.Add(dep);
                        if (depth < maxDepth - 1)
                        {
                            toProcess.Enqueue(dep);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageBuilder] Error getting dependencies for {assetPath}: {ex.Message}");
                }
            }
            
            // Add filtered dependencies to export list
            if (allDependencies.Count > 0)
            {
                Debug.Log($"[PackageBuilder] Adding {allDependencies.Count} filtered dependencies to export (checked {depth} levels deep)");
                assetsToExport.AddRange(allDependencies);
            }
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
            string packageMetadataJson,
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
                
                // 1. Inject package.json (temporary, will be deleted by installer)
                if (!string.IsNullOrEmpty(packageJsonContent))
                {
                    string packageJsonGuid = Guid.NewGuid().ToString("N");
                    string packageJsonFolder = Path.Combine(tempExtractDir, packageJsonGuid);
                    Directory.CreateDirectory(packageJsonFolder);
                    
                    File.WriteAllText(Path.Combine(packageJsonFolder, "asset"), packageJsonContent);
                    // Use a unique path
                    File.WriteAllText(Path.Combine(packageJsonFolder, "pathname"), $"Assets/YUCP_TempInstall_{packageJsonGuid}.json");
                    
                    string packageJsonMeta = "fileFormatVersion: 2\nguid: " + packageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(packageJsonFolder, "asset.meta"), packageJsonMeta);
                }

                // 1b. Inject YUCP_PackageInfo.json (permanent metadata)
                if (!string.IsNullOrEmpty(packageMetadataJson))
                {
                    string metadataGuid = Guid.NewGuid().ToString("N");
                    string metadataFolder = Path.Combine(tempExtractDir, metadataGuid);
                    Directory.CreateDirectory(metadataFolder);
                    
                    // Write asset file
                    File.WriteAllText(Path.Combine(metadataFolder, "asset"), packageMetadataJson);
                    
                    // Write .meta file (TextScriptImporter)
                    string metaGuid = Guid.NewGuid().ToString("N");
                    string metaPath = Path.Combine(metadataFolder, "asset.meta");
                    string metaContent = $"fileFormatVersion: 2\nguid: {metaGuid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(metaPath, metaContent);
                    
                    // Write pathname (destination path in project)
                    File.WriteAllText(Path.Combine(metadataFolder, "pathname"), "Assets/YUCP_PackageInfo.json");
                }
                
                // 2a. Inject Mini Package Guardian (permanent protection layer)
                //
                // Requirements:
                // - Must be fully self-contained (must NOT reference YUCP.Components.*), because it is bundled for projects
                //   that don't have com.yucp.components installed.
                // - Must NOT be injected if the exported package.json will install com.yucp.components (avoids conflicts).
                bool hasYucpComponentsDependency = profile.dependencies != null &&
                    profile.dependencies.Any(d => d.enabled && d.packageName == "com.yucp.components");

                bool packageJsonWillInstallYucpComponents =
                    !string.IsNullOrEmpty(packageJsonContent) &&
                    packageJsonContent.IndexOf("\"com.yucp.components\"", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!hasYucpComponentsDependency && !packageJsonWillInstallYucpComponents)
                {
                    string guardianTemplatePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Templates/PackageGuardianMini.cs";
                    string transactionTemplatePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Templates/GuardianTransaction.cs";

                    if (!File.Exists(guardianTemplatePath) || !File.Exists(transactionTemplatePath))
                    {
                        Debug.LogWarning("[PackageBuilder] Mini Guardian templates missing in com.yucp.devtools. Skipping yucp.packageguardian injection.");
                    }
                    else
                    {
                        // 1. Inject GuardianTransaction.cs (core dependency)
                        string transactionGuid = Guid.NewGuid().ToString("N");
                        string transactionFolder = Path.Combine(tempExtractDir, transactionGuid);
                        Directory.CreateDirectory(transactionFolder);

                        string transactionContent = File.ReadAllText(transactionTemplatePath);
                        File.WriteAllText(Path.Combine(transactionFolder, "asset"), transactionContent);
                        File.WriteAllText(Path.Combine(transactionFolder, "pathname"), "Packages/yucp.packageguardian/Editor/Core/Transactions/GuardianTransaction.cs");

                        string transactionMeta = "fileFormatVersion: 2\nguid: " + transactionGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(transactionFolder, "asset.meta"), transactionMeta);

                        // 2. Inject PackageGuardianMini.cs (self-contained implementation)
                        string guardianGuid = Guid.NewGuid().ToString("N");
                        string guardianFolder = Path.Combine(tempExtractDir, guardianGuid);
                        Directory.CreateDirectory(guardianFolder);

                        string guardianContent = File.ReadAllText(guardianTemplatePath);
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
  ""description"": ""Lightweight import protection for YUCP packages. Resolves .yucp_disabled files and preserves meta GUIDs."",
  ""unity"": ""2019.4""
}";
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "asset"), guardianPackageJson);
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "pathname"), "Packages/yucp.packageguardian/package.json");

                        string guardianPackageJsonMeta = "fileFormatVersion: 2\nguid: " + guardianPackageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "asset.meta"), guardianPackageJsonMeta);
                    }
                }
                
                // 2b. Inject DirectVpmInstaller.cs
                // Try to find the script in the package
                string installerScriptPath = null;
                string[] foundScripts = AssetDatabase.FindAssets("DirectVpmInstaller t:Script");
                
                if (foundScripts.Length > 0)
                {
                    installerScriptPath = AssetDatabase.GUIDToAssetPath(foundScripts[0]);
                }
                
                if (!string.IsNullOrEmpty(installerScriptPath) && File.Exists(installerScriptPath))
                {
                    string installerGuid = Guid.NewGuid().ToString("N");
                    string installerFolder = Path.Combine(tempExtractDir, installerGuid);
                    Directory.CreateDirectory(installerFolder);
                    
                    string installerContent = File.ReadAllText(installerScriptPath);
                    File.WriteAllText(Path.Combine(installerFolder, "asset"), installerContent);
                    // Use unique path
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
                        // Use a unique assembly name for the injected asmdef
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
                        
                    }
                    
                    
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
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Data/DerivedFbxAsset.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/MetaFileManager.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/DerivedFbxBuilder.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/HDiffPatchWrapper.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/ManifestBuilder.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/Correspondence/MapBuilder.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/Backup/BackupManager.cs",
                        "Packages/com.yucp.devtools/Editor/PackageExporter/Core/Validator.cs",
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
                    }
                    
                    File.WriteAllText(Path.Combine(tempPackageJsonFolder, "asset"), tempPackageJsonContent);
                    File.WriteAllText(Path.Combine(tempPackageJsonFolder, "pathname"), "Packages/com.yucp.temp/package.json");
                    
                    string tempPackageJsonMeta = "fileFormatVersion: 2\nguid: " + tempPackageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(tempPackageJsonFolder, "asset.meta"), tempPackageJsonMeta);
                    
                    // Create an assembly definition (.asmdef) file for the Editor scripts
                    string asmdefGuid = Guid.NewGuid().ToString("N");
                    string asmdefFolder = Path.Combine(tempExtractDir, asmdefGuid);
                    Directory.CreateDirectory(asmdefFolder);
                    
                    string asmdefContent = @"{
  ""name"": ""YUCP.PatchRuntime"",
  ""rootNamespace"": """",
  ""references"": [
    ""Unity.Formats.Fbx.Editor""
  ],
  ""includePlatforms"": [
    ""Editor""
  ],
  ""excludePlatforms"": [],
  ""allowUnsafeCode"": false,
  ""overrideReferences"": true,
  ""precompiledReferences"": [],
  ""autoReferenced"": true,
  ""defineConstraints"": [],
  ""versionDefines"": [
    {
      ""name"": ""com.unity.formats.fbx"",
      ""expression"": ""4.0.0"",
      ""define"": ""UNITY_FORMATS_FBX""
    }
  ],
  ""noEngineReferences"": false
}";
                    File.WriteAllText(Path.Combine(asmdefFolder, "asset"), asmdefContent);
                    File.WriteAllText(Path.Combine(asmdefFolder, "pathname"), "Packages/com.yucp.temp/Editor/YUCP.PatchRuntime.asmdef");
                    
                    string asmdefMeta = "fileFormatVersion: 2\nguid: " + asmdefGuid + "\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(asmdefFolder, "asset.meta"), asmdefMeta);
                    
                    
                    string[] hdiffDlls = new string[]
                    {
                        "Packages/com.yucp.devtools/Plugins/hdiffz.dll",
                        "Packages/com.yucp.devtools/Plugins/hpatchz.dll"
                    };
                    
                    foreach (var dllPath in hdiffDlls)
                    {
                        if (File.Exists(dllPath))
                        {
                            string dllGuid = Guid.NewGuid().ToString("N");
                            string dllFolder = Path.Combine(tempExtractDir, dllGuid);
                            Directory.CreateDirectory(dllFolder);
                            
                            string fileName = Path.GetFileName(dllPath);
                            string targetPath = $"Packages/com.yucp.temp/Plugins/{fileName}";
                            
                            File.Copy(dllPath, Path.Combine(dllFolder, "asset"), true);
                            File.WriteAllText(Path.Combine(dllFolder, "pathname"), targetPath);
                            
                            // Copy the .meta file if it exists
                            string metaPath = dllPath + ".meta";
                            if (File.Exists(metaPath))
                            {
                                string metaContent = File.ReadAllText(metaPath);
                                File.WriteAllText(Path.Combine(dllFolder, "asset.meta"), metaContent);
                            }
                            else
                            {
                                // Create a basic .meta file for the DLL
                                string dllMeta = "fileFormatVersion: 2\nguid: " + dllGuid + "\nPluginImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  iconMap: {}\n  executionOrder: {}\n  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n  - first:\n      : Any\n    second:\n      enabled: 0\n  - first:\n      Any: \n    second:\n      enabled: 1\n  - first:\n      Editor: Editor\n    second:\n      enabled: 1\n      settings:\n        CPU: AnyCPU\n        DefaultValueInitialized: true\n        OS: AnyOS\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
                                File.WriteAllText(Path.Combine(dllFolder, "asset.meta"), dllMeta);
                            }
                            
                            Debug.Log($"[PackageBuilder] Copied HDiffPatch DLL to temp package: {fileName}");
                        }
                        else
                        {
                            Debug.LogWarning($"[PackageBuilder] HDiffPatch DLL not found: {dllPath}");
                        }
                    }
                    
                    if (injectedPatchScripts > 0)
                    {
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
                                }
                            }
                        }
                        
                        string[] allFiles = Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories);
                        int filesAdded = 0;
                        int filesReplaced = 0;
                        
                        // Track which asmdef directories have been processed
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
                                continue;
                            }
                            
                            // Skip .cs files if they belong to an obfuscated assembly (DLL will be added instead)
                            bool shouldSkipCsFile = false;
                            if (extension == ".cs")
                            {
                                if (isInObfuscatedAssembly)
                                {
                                    shouldSkipCsFile = true;
                                }
                                else
                                {
                                    string fullPath = Path.GetFullPath(filePath).Replace("\\", "/");
                                    foreach (var asmdefPath in obfuscatedAsmdefPaths)
                                    {
                                        string asmdefDir = Path.GetDirectoryName(asmdefPath).Replace("\\", "/");
                                        if (fullPath.StartsWith(asmdefDir + "/", StringComparison.OrdinalIgnoreCase))
                                        {
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
                                
                                if (obfuscatedAsmdefPaths.Contains(asmdefFullPath))
                                {
                                    
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
                                            
                                            string dllGuid = Guid.NewGuid().ToString("N");
                                            string dllMetaContent = GenerateMetaForFile(dllPath, dllGuid);
                                            
                                            string dllFolder = Path.Combine(tempExtractDir, dllGuid);
                                            Directory.CreateDirectory(dllFolder);
                                            
                                            File.Copy(dllPath, Path.Combine(dllFolder, "asset"), true);
                                            File.WriteAllText(Path.Combine(dllFolder, "pathname"), dllUnityPathname);
                                            File.WriteAllText(Path.Combine(dllFolder, "asset.meta"), dllMetaContent);
                                            
                                            filesAdded++;
                                            filesReplaced++;
                                        }
                                        else
                                        {
                                            Debug.LogWarning($"[PackageBuilder] Could not find DLL path for asmdef: {asmdefFullPath}");
                                        }
                                    }
                                    
                                    continue;
                                }
                            }
                            
                            string unityPathname = $"Packages/{packageName}/{relativePath.Replace('\\', '/')}";
                            if (isCompilableScript)
                            {
                                unityPathname += ".yucp_disabled";
                            }
                            
                            // GUID handling strategy:
                            // - For .yucp_disabled files: Generate NEW GUID, but store ORIGINAL GUID in meta userData for restoration
                            // - For normal files: Preserve original GUID to maintain references
                            string fileGuid = null;
                            string metaContent = null;
                            string originalMetaPath = filePath + ".meta";
                            string originalGuid = null;
                            try
                            {
                                if (File.Exists(originalMetaPath))
                                {
                                    string originalMeta = File.ReadAllText(originalMetaPath);
                                    var guidMatch = System.Text.RegularExpressions.Regex.Match(originalMeta, @"guid:\s*([a-f0-9]{32})");
                                    if (guidMatch.Success)
                                        originalGuid = guidMatch.Groups[1].Value;
                                }
                            }
                            catch { /* best-effort */ }
                            
                            if (isCompilableScript)
                            {
                                // Generate new GUID for disabled files (prevents GUID conflicts on re-import)
                                fileGuid = Guid.NewGuid().ToString("N");
                                // Store original GUID token so the installer/resolver can restore GUID on enable.
                                metaContent = GenerateMetaForFileWithOriginalGuid(filePath, fileGuid, originalGuid);
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
                        }
                        else
                        {
                        }
                    }
                    
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
        /// Generate appropriate .meta file content using file extension
        /// </summary>
        private static string GenerateMetaForFileWithOriginalGuid(string filePath, string guid, string originalGuid)
        {
            string baseMeta = GenerateMetaForFile(filePath, guid);

            if (string.IsNullOrEmpty(originalGuid))
                return baseMeta;

            // Store original GUID in a Unity-stable string format.
            // Unity expects userData to be a string; inline YAML objects may be dropped.
            string token = $"YUCP_ORIGINAL_GUID={originalGuid}";

            baseMeta = System.Text.RegularExpressions.Regex.Replace(
                baseMeta,
                @"(\s+)userData:\s*\r?\n",
                $"$1userData: {token}\n",
                System.Text.RegularExpressions.RegexOptions.Multiline
            );

            return baseMeta;
        }

        /// <summary>
        /// Generate appropriate .meta file content using file extension
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
        /// Sign package before final export - creates manifest, gets signature from server, and embeds it
        /// </summary>
        private static bool SignPackageBeforeExport(string packagePath, ExportProfile profile, Action<float, string> progressCallback)
        {
            try
            {
                var settings = GetSigningSettings();
                if (settings == null || !settings.HasValidCertificate())
                {
                    return false;
                }

                var certificate = CertificateManager.GetCurrentCertificate();
                if (certificate == null)
                {
                    Debug.LogWarning("[PackageBuilder] No certificate found for signing");
                    return false;
                }

                // Verify dev key matches certificate
                string currentDevPublicKey = DevKeyManager.GetPublicKeyBase64();
                if (certificate.cert.devPublicKey != currentDevPublicKey)
                {
                    Debug.LogError($"[PackageBuilder] Dev public key mismatch! Certificate expects: {certificate.cert.devPublicKey}, Current: {currentDevPublicKey}");
                    Debug.LogError("[PackageBuilder] The certificate was issued for a different dev key. Please regenerate or import the correct certificate.");
                    return false;
                }

                Debug.Log($"[PackageBuilder] Dev public key matches certificate: {currentDevPublicKey}");

                progressCallback?.Invoke(0.821f, "Computing package hash...");
                
                // Compute archive SHA-256 using canonical content hashing:
                // - Decompress .unitypackage
                // - Enumerate all assets
                // - Ignore Assets/_Signing/*
                // - Hash (pathname UTF8 + 0x00 + asset bytes) in sorted pathname order
                string archiveSha256 = ComputeArchiveHashExcludingSigningData(packagePath);

                progressCallback?.Invoke(0.722f, "Building manifest...");

                // Build manifest (use packageId if available, otherwise fallback to packageName)
                string manifestPackageId = !string.IsNullOrEmpty(profile.packageId) ? profile.packageId : profile.packageName;
                var manifest = YUCP.DevTools.Editor.PackageSigning.Core.ManifestBuilder.BuildManifest(
                    packagePath,
                    manifestPackageId,
                    profile.version,
                    settings.publisherId,
                    settings.vrchatUserId,
                    profile.gumroadProductId,
                    profile.jinxxyProductId
                );
                
                // Override the hash computed by BuildManifest (which also computes it from packagePath)
                // We want to use the hash we just computed to ensure consistency
                manifest.archiveSha256 = archiveSha256;

                progressCallback?.Invoke(0.723f, "Signing manifest with dev key...");

                // Create signing request payload (matching PackageSigningService format)
                var payloadObj = new SigningRequestPayload
                {
                    publisherId = settings.publisherId,
                    vrchatUserId = settings.vrchatUserId,
                    manifest = manifest,
                    yucpCert = certificate,
                    timestamp = System.DateTime.UtcNow.ToString("O"),
                    nonce = System.Guid.NewGuid().ToString()
                };

                // Canonicalize payload JSON (must match PackageSigningService format exactly)
                // The server verifies the signature against the canonicalized payload
                string payloadJson = CanonicalizeSigningPayload(payloadObj);
                byte[] payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadJson);
                
                byte[] devSignature = DevKeyManager.SignData(payloadBytes);

                progressCallback?.Invoke(0.724f, "Sending signing request to server...");

                // Send to server using synchronous HTTP request
                PackageSigningData.SigningResponse signingResponse = SendSigningRequestSynchronously(
                    settings.serverUrl,
                    payloadObj,
                    devSignature,
                    progressCallback
                );

                if (signingResponse == null)
                {
                    Debug.LogWarning("[PackageBuilder] Signing failed - no signature received from server");
                    return false;
                }

                // Extract certificate chain from response and add to manifest
                if (signingResponse.certificateChain != null && signingResponse.certificateChain.Length > 0)
                {
                    manifest.certificateChain = signingResponse.certificateChain;
                }
                else
                {
                    Debug.LogWarning("[PackageBuilder] Server response did not include certificate chain");
                }

                // Convert SigningResponse to SignatureData for embedding
                PackageSigningData.SignatureData signatureData = new PackageSigningData.SignatureData
                {
                    algorithm = signingResponse.algorithm,
                    keyId = signingResponse.keyId,
                    signature = signingResponse.signature,
                    certificateIndex = signingResponse.certificateIndex
                };

                progressCallback?.Invoke(0.728f, "Embedding signature in package...");

                SignatureEmbedder.EmbedSigningData(manifest, signatureData);
                
                // Inject signing data into the package
                InjectSigningDataIntoPackage(packagePath);

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Signing error: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Canonicalize signing payload to match server's expected format
        /// Uses recursive canonicalization like PackageInfoService (server expects this)
        /// </summary>
        private static string CanonicalizeSigningPayload(SigningRequestPayload payload)
        {
            // Use recursive canonicalization to match server's format
            return CanonicalizeJsonRecursive(payload);
        }

        /// <summary>
        /// Recursively canonicalize JSON to match server's format
        /// Sorts keys alphabetically at all levels (like PackageInfoService)
        /// </summary>
        private static string CanonicalizeJsonRecursive(object obj)
        {
            if (obj == null)
            {
                return "null";
            }
            
            var objType = obj.GetType();
            
            // Handle dictionaries (must come before IList check)
            if (obj is System.Collections.IDictionary dict)
            {
                // Skip empty dictionaries (server omits them)
                if (dict.Count == 0)
                {
                    return "{}"; // Return empty object, but caller should skip this field
                }
                
                var items = new List<string>();
                var keys = new List<object>();
                foreach (var key in dict.Keys)
                {
                    keys.Add(key);
                }
                
                // Sort keys alphabetically (convert to string for comparison)
                keys.Sort((a, b) => string.Compare(a?.ToString() ?? "", b?.ToString() ?? "", StringComparison.Ordinal));
                
                foreach (var key in keys)
                {
                    var value = dict[key];
                    var keyStr = EscapeJsonString(key?.ToString() ?? "");
                    var jsonValue = CanonicalizeJsonRecursive(value);
                    items.Add($"\"{keyStr}\":{jsonValue}");
                }
                return "{" + string.Join(",", items) + "}";
            }
            
            // Handle arrays and lists
            if (objType.IsArray)
            {
                var array = (Array)obj;
                var items = new List<string>();
                foreach (var item in array)
                {
                    items.Add(CanonicalizeJsonRecursive(item));
                }
                return "[" + string.Join(",", items) + "]";
            }
            
            if (obj is System.Collections.IList list)
            {
                var items = new List<string>();
                foreach (var item in list)
                {
                    items.Add(CanonicalizeJsonRecursive(item));
                }
                return "[" + string.Join(",", items) + "]";
            }
            
            // Handle objects (serializable classes)
            if (objType.IsClass && !objType.IsPrimitive && objType != typeof(string))
            {
                // Get all serializable fields
                var fields = objType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .Where(f => !f.IsStatic)
                    .OrderBy(f => f.Name)
                    .ToList();
                
                var items = new List<string>();
                foreach (var field in fields)
                {
                    var value = field.GetValue(obj);
                    
                    // Include all values (null becomes "null", empty dicts become "{}")
                    // Server's canonicalizeJson includes all keys, even if null or empty
                    var key = EscapeJsonString(field.Name);
                    var jsonValue = CanonicalizeJsonRecursive(value);
                    items.Add($"\"{key}\":{jsonValue}");
                }
                return "{" + string.Join(",", items) + "}";
            }
            
            // Handle primitives
            if (obj is string str)
            {
                return $"\"{EscapeJsonString(str)}\"";
            }
            
            if (obj is bool b)
            {
                return b ? "true" : "false";
            }
            
            if (obj is int || obj is long || obj is short || obj is byte || obj is uint || obj is ulong || obj is ushort || obj is sbyte)
            {
                return obj.ToString();
            }
            
            if (obj is float || obj is double || obj is decimal)
            {
                return obj.ToString();
            }
            
            // Fallback to JSON serialization
            return JsonUtility.ToJson(obj);
        }

        /// <summary>
        /// Escape JSON string
        /// </summary>
        private static string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }

        /// <summary>
        /// Send signing request to server synchronously
        /// </summary>
        private static PackageSigningData.SigningResponse SendSigningRequestSynchronously(
            string serverUrl,
            SigningRequestPayload payload,
            byte[] devSignature,
            Action<float, string> progressCallback)
        {
            try
            {
                // Manually construct JSON request body to ensure fileHashes dictionary is included
                // Unity's JsonUtility doesn't serialize Dictionary fields, so we need to build it manually
                string canonicalizedPayload = CanonicalizeSigningPayload(payload);
                string devSignatureBase64 = System.Convert.ToBase64String(devSignature);
                
                // Build the request JSON manually: {"payload":{...},"devSignature":"..."}
                string requestJson = $"{{\"payload\":{canonicalizedPayload},\"devSignature\":\"{EscapeJsonString(devSignatureBase64)}\"}}";
                byte[] requestBytes = System.Text.Encoding.UTF8.GetBytes(requestJson);

                string url = $"{serverUrl.TrimEnd('/')}/v2/sign-manifest";

                // Use UnityWebRequest and wait for it
                using (var request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
                {
                    request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(requestBytes);
                    request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.timeout = 30;

                    var operation = request.SendWebRequest();

                    // Wait for request to complete
                    while (!operation.isDone)
                    {
                        progressCallback?.Invoke(0.725f + (operation.progress * 0.002f), "Waiting for server response...");
                        System.Threading.Thread.Sleep(50);
                    }

                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        string responseJson = request.downloadHandler.text;
                        
                        // Parse the full response including certificateChain
                        PackageSigningData.SigningResponse response = JsonUtility.FromJson<PackageSigningData.SigningResponse>(responseJson);
                        
                        // Unity's JsonUtility doesn't properly deserialize nested arrays,
                        // so we need to manually parse certificateChain if present
                        if (response != null && responseJson.Contains("certificateChain"))
                        {
                            try
                            {
                                // Use a simple JSON parsing approach for the certificateChain array
                                // Extract the certificateChain array from the JSON
                                int chainStart = responseJson.IndexOf("\"certificateChain\":[");
                                if (chainStart >= 0)
                                {
                                    int bracketCount = 0;
                                    int arrayStart = responseJson.IndexOf('[', chainStart);
                                    int arrayEnd = arrayStart;
                                    
                                    for (int i = arrayStart; i < responseJson.Length; i++)
                                    {
                                        if (responseJson[i] == '[') bracketCount++;
                                        if (responseJson[i] == ']') bracketCount--;
                                        if (bracketCount == 0)
                                        {
                                            arrayEnd = i;
                                            break;
                                        }
                                    }
                                    
                                    if (arrayEnd > arrayStart)
                                    {
                                        string chainJson = responseJson.Substring(arrayStart, arrayEnd - arrayStart + 1);
                                        // Parse the certificate chain array using a wrapper class (Unity JsonUtility needs wrapper for arrays)
                                        CertificateChainWrapper wrapper = JsonUtility.FromJson<CertificateChainWrapper>("{\"Items\":" + chainJson + "}");
                                        if (wrapper != null && wrapper.Items != null)
                                        {
                                            // Unity's JsonUtility may not properly deserialize enum strings in nested objects,
                                            // so we manually fix the certificateType enum values from the JSON
                                            foreach (var cert in wrapper.Items)
                                            {
                                                if (cert != null)
                                                {
                                                    // Find this certificate's certificateType in the JSON
                                                    int certStart = chainJson.IndexOf($"\"keyId\":\"{cert.keyId}\"");
                                                    if (certStart >= 0)
                                                    {
                                                        // Look for certificateType field after this keyId
                                                        int typeStart = chainJson.IndexOf("\"certificateType\":\"", certStart);
                                                        if (typeStart >= 0 && typeStart < certStart + 500) // Within reasonable distance
                                                        {
                                                            typeStart += "\"certificateType\":\"".Length;
                                                            int typeEnd = chainJson.IndexOf("\"", typeStart);
                                                            if (typeEnd > typeStart)
                                                            {
                                                                string typeStr = chainJson.Substring(typeStart, typeEnd - typeStart);
                                                                if (System.Enum.TryParse<PackageVerifierData.CertificateType>(typeStr, true, out var parsedType))
                                                                {
                                                                    cert.certificateType = parsedType;
                                                                }
                                                                else
                                                                {
                                                                    Debug.LogWarning($"[PackageBuilder] Failed to parse certificateType '{typeStr}' for {cert.keyId}");
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            response.certificateChain = wrapper.Items;
                                        }
                                        else
                                        {
                                            Debug.LogWarning("[PackageBuilder] Failed to parse certificate chain array from wrapper");
                                        }
                                    }
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogWarning($"[PackageBuilder] Failed to parse certificateChain from response: {ex.Message}");
                            }
                        }
                        
                        return response;
                    }
                    else
                    {
                        string error = request.downloadHandler.text;
                        if (string.IsNullOrEmpty(error))
                        {
                            error = $"HTTP {request.responseCode}: {request.error}";
                        }
                        Debug.LogError($"[PackageBuilder] Signing request failed: HTTP {request.responseCode}, Error: {error}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Signing request exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Inject signing data folder into the package
        /// </summary>
        private static void InjectSigningDataIntoPackage(string packagePath)
        {
            string signingFolder = "Assets/_Signing";
            if (!AssetDatabase.IsValidFolder(signingFolder))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("", new[] { signingFolder });
            if (guids.Length == 0)
            {
                return;
            }

            // Use the same injection mechanism as package.json
            // Extract package, add signing files, repackage
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"YUCP_Signing_{Guid.NewGuid():N}");
            
            try
            {
                Directory.CreateDirectory(tempExtractDir);

                // Extract package (using same approach as PackageIconInjector)
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
                using (Stream inStream = File.OpenRead(packagePath))
                using (Stream gzipStream = new GZipInputStream(inStream))
                {
                    var tarArchive = TarArchive.CreateInputTarArchive(gzipStream, Encoding.UTF8);
                    tarArchive.ExtractContents(tempExtractDir);
                    tarArchive.Close();
                }

                // Remove existing signing files to avoid duplicates
                string[] existingFolders = Directory.GetDirectories(tempExtractDir);
                foreach (string folder in existingFolders)
                {
                    string pathnameFile = Path.Combine(folder, "pathname");
                    if (File.Exists(pathnameFile))
                    {
                        string pathname = File.ReadAllText(pathnameFile).Trim();
                        if (pathname.StartsWith("Assets/_Signing/", StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.Delete(folder, true);
                        }
                    }
                }

                // Add signing files
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
                        continue;

                    string fileName = Path.GetFileName(assetPath);
                    string fileGuid = Guid.NewGuid().ToString("N");
                    string fileFolder = Path.Combine(tempExtractDir, fileGuid);
                    Directory.CreateDirectory(fileFolder);

                    // Copy asset file
                    File.Copy(assetPath, Path.Combine(fileFolder, "asset"), true);

                    File.WriteAllText(Path.Combine(fileFolder, "pathname"), assetPath);

                    // Create .meta file
                    string metaGuid = Guid.NewGuid().ToString("N");
                    string metaContent = GenerateMetaForFile(assetPath, metaGuid);
                    File.WriteAllText(Path.Combine(fileFolder, "asset.meta"), metaContent);
                }

                // Repackage (using same approach as PackageIconInjector)
                string tempPackagePath = packagePath + ".tmp";
                using (Stream outStream = File.Create(tempPackagePath))
                using (Stream gzipStream = new GZipOutputStream(outStream))
                {
                    var tarArchive = TarArchive.CreateOutputTarArchive(gzipStream);
                    
                    // Set root path (case sensitive, must use forward slashes, must not end with slash)
                    tarArchive.RootPath = tempExtractDir.Replace('\\', '/');
                    if (tarArchive.RootPath.EndsWith("/"))
                        tarArchive.RootPath = tarArchive.RootPath.TrimEnd('/');

                    // Add all files from extracted directory
                    var filenames = Directory.GetFiles(tempExtractDir, "*", SearchOption.AllDirectories);
                    foreach (var filename in filenames)
                    {
                        var relativePath = filename.Substring(tempExtractDir.Length);
                        if (relativePath.StartsWith("\\") || relativePath.StartsWith("/"))
                            relativePath = relativePath.Substring(1);
                        relativePath = relativePath.Replace('\\', '/');
                        
                        var tarEntry = TarEntry.CreateEntryFromFile(filename);
                        tarEntry.Name = relativePath;
                        tarArchive.WriteEntry(tarEntry, true);
                    }
                    
                    tarArchive.Close();
                }

                // Replace original with signed version
                File.Delete(packagePath);
                File.Move(tempPackagePath, packagePath);

#else
                Debug.LogWarning("[PackageBuilder] ICSharpCode.SharpZipLib not available - cannot inject signing data. Please install SharpZipLib.");
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Failed to inject signing data: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempExtractDir))
                        Directory.Delete(tempExtractDir, true);
                }
                catch { }
            }
        }

        /// <summary>
        /// Get signing settings
        /// </summary>
        private static PackageSigningData.SigningSettings GetSigningSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<PackageSigningData.SigningSettings>(path);
            }
            return null;
        }

        /// <summary>
        /// Compute canonical archive hash over package contents, excluding signing data.
        /// Hash is SHA-256 over a deterministic stream of:
        ///   UTF8(pathname) + 0x00 + asset-bytes, in sorted pathname order,
        /// ignoring any Assets/_Signing/* entries.
        /// </summary>
        private static string ComputeArchiveHashExcludingSigningData(string packagePath)
        {
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"YUCP_Hash_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempExtractDir);

            try
            {
                using (Stream inStream = File.OpenRead(packagePath))
                using (Stream gzipStream = new GZipInputStream(inStream))
                {
                    var tarArchive = TarArchive.CreateInputTarArchive(gzipStream, Encoding.UTF8);
                    tarArchive.ExtractContents(tempExtractDir);
                    tarArchive.Close();
                }

                // Collect all non-signing assets
                var entries = new System.Collections.Generic.List<(string pathname, string assetPath)>();

                string[] folders = Directory.GetDirectories(tempExtractDir);
                foreach (string folder in folders)
                {
                    string pathnameFile = Path.Combine(folder, "pathname");
                    string assetFile = Path.Combine(folder, "asset");

                    if (!File.Exists(pathnameFile) || !File.Exists(assetFile))
                        continue;

                    string pathname = File.ReadAllText(pathnameFile).Trim().Replace('\\', '/');

                    // Skip signing data
                    if (pathname.StartsWith("Assets/_Signing/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    entries.Add((pathname, assetFile));
                }

                // Sort by pathname for determinism
                entries.Sort((a, b) => string.CompareOrdinal(a.pathname, b.pathname));

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    foreach (var entry in entries)
                    {
                        byte[] pathBytes = Encoding.UTF8.GetBytes(entry.pathname);
                        sha256.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);

                        // Separator byte to avoid path/data ambiguity
                        byte[] sep = new byte[] { 0x00 };
                        sha256.TransformBlock(sep, 0, 1, null, 0);

                        byte[] data = File.ReadAllBytes(entry.assetPath);
                        sha256.TransformBlock(data, 0, data.Length, null, 0);
                    }

                    sha256.TransformFinalBlock(System.Array.Empty<byte>(), 0, 0);
                    return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempExtractDir))
                        Directory.Delete(tempExtractDir, true);
                }
                catch { }
            }
#else
            throw new System.InvalidOperationException("SharpZipLib (TarArchive/GZipInputStream) not available in this Unity version; cannot compute archive hash.");
#endif
        }

        /// <summary>
        /// Signing request payload structure (matches PackageSigningService)
        /// </summary>
        [Serializable]
        private class SigningRequestPayload
        {
            public string publisherId;
            public string vrchatUserId;
            public PackageSigningData.PackageManifest manifest;
            public PackageSigningData.YucpCertificate yucpCert;
            public string timestamp;
            public string nonce;
        }

        /// <summary>
        /// Signing request structure (matches PackageSigningService)
        /// </summary>
        [Serializable]
        private class SigningRequest
        {
            public SigningRequestPayload payload;
            public string devSignature;
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

        /// <summary>
        /// Wrapper class for parsing certificate chain arrays with Unity's JsonUtility
        /// Unity's JsonUtility requires a wrapper class to deserialize arrays
        /// </summary>
        [Serializable]
        private class CertificateChainWrapper
        {
            public PackageVerifierData.CertificateData[] Items;
        }
    }
}


