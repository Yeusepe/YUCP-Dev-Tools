using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace YUCP.PatchCleanup
{
    /// <summary>
    /// Standalone patch importer that applies DerivedFbxAsset patches on import.
    /// This script is included in the temp package and handles patch application.
    /// Uses direct GUID targeting and preserves the original derived FBX GUID for prefab compatibility.
    /// </summary>
    public class YUCPPatchImporter : AssetPostprocessor
    {
        private static string GetLogFilePath()
        {
            string logDir = Path.Combine(Application.dataPath, "..", "Logs");
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);
            return Path.Combine(logDir, "YUCP_PatchImporter.log");
        }
        
        private static void WriteLog(string message)
        {
            try
            {
                string logPath = GetLogFilePath();
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(logPath, $"[{timestamp}] {message}\n");
            }
            catch { }
        }
        
        static YUCPPatchImporter()
        {
            WriteLog("YUCPPatchImporter static constructor called - importer is loaded and compiled!");
            EditorApplication.delayCall += CheckForPatchesOnLoad;
            
            // Clean up DLLs on editor load
            EditorApplication.delayCall += CleanupDllsOnLoad;
        }
        
        private static HashSet<string> processedPatches = new HashSet<string>();
        private static bool hasCheckedOnLoad = false;
        private static HashSet<string> importedTempFiles = new HashSet<string>();
        private static HashSet<string> createdDerivedFbxPaths = new HashSet<string>();
        private static bool s_hasCleanedOnLoad = false;
        
        private static void CheckForPatchesOnLoad()
        {
            if (hasCheckedOnLoad) return;
            hasCheckedOnLoad = true;
            
            WriteLog("CheckForPatchesOnLoad called - checking for existing patches");
            
            try
            {
                // First, check if there's a pending patch retry after package installation
                string patchPathKey = "YUCP.DerivedFbxBuilder.PendingPatchPath";
                string pendingPatchPath = EditorPrefs.GetString(patchPathKey, "");
                if (!string.IsNullOrEmpty(pendingPatchPath))
                {
                    WriteLog($"Found pending patch retry after package installation: {pendingPatchPath}");
                    
                    // Wait a moment for packages to finish resolving
                    EditorApplication.delayCall += () =>
                    {
                        EditorApplication.delayCall += () =>
                        {
                            // Check if Unity FBX Exporter is now available
                            bool fbxExporterAvailable = false;
                            try
                            {
                                var modelExporterType = System.Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");
                                if (modelExporterType != null)
                                {
                                    var exportMethod = modelExporterType.GetMethod("ExportObject", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                    fbxExporterAvailable = exportMethod != null;
                                }
                            }
                            catch { }
                            
                            if (fbxExporterAvailable)
                            {
                                WriteLog("Unity FBX Exporter is now available. Automatically retrying patch application...");
                                // Clear the processed flag for this patch so it can be retried
                                if (processedPatches != null && processedPatches.Contains(pendingPatchPath))
                                {
                                    processedPatches.Remove(pendingPatchPath);
                                }
                                // Reset the hasCheckedOnLoad flag so patches are checked again
                                hasCheckedOnLoad = false;
                                // Keep EditorPrefs until successful application
                                CheckForPatchesOnLoad();
                            }
                            else
                            {
                                WriteLog("Unity FBX Exporter still not available. Waiting for package resolution...");
                                // Try again after a longer delay (packages might still be downloading)
                                EditorApplication.delayCall += () =>
                                {
                                    hasCheckedOnLoad = false;
                                    CheckForPatchesOnLoad();
                                };
                            }
                        };
                    };
                }
                
                string patchesFolder = "Packages/com.yucp.temp/Patches";
                if (!AssetDatabase.IsValidFolder(patchesFolder))
                {
                    WriteLog($"Patches folder does not exist: {patchesFolder}");
                    return;
                }
                
                string[] allGuids = AssetDatabase.FindAssets("", new[] { patchesFolder });
                WriteLog($"Found {allGuids.Length} asset(s) in Patches folder");
                
                int appliedCount = 0;
                foreach (var guid in allGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    
                    if (path.Contains("DerivedFbxAsset") && path.EndsWith(".asset") && path.Contains("com.yucp.temp"))
                    {
                        WriteLog($"Found existing patch: {path} - attempting to apply");
                        
                        if (processedPatches != null && processedPatches.Contains(path))
                        {
                            WriteLog($"  Skipping (already processed): {path}");
                            continue;
                        }
                        
                        if (processedPatches == null)
                            processedPatches = new HashSet<string>();
                        processedPatches.Add(path);
                        
                        TryApplyPatch(path);
                        appliedCount++;
                    }
                }
                
                WriteLog($"Applied {appliedCount} existing patch(es) on load");
            }
            catch (Exception ex)
            {
                WriteLog($"Error checking for existing patches: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private static void CleanupDllsOnLoad()
        {
            if (s_hasCleanedOnLoad) return;
            s_hasCleanedOnLoad = true;
            
            try
            {
                WriteLog("Checking for DLLs to clean up on editor load...");
                
                string pluginsPath = "Packages/com.yucp.temp/Plugins";
                if (!AssetDatabase.IsValidFolder(pluginsPath))
                {
                    return;
                }
                
                string[] dllGuids = AssetDatabase.FindAssets("t:DefaultAsset", new[] { pluginsPath });
                int deletedCount = 0;
                
                foreach (var guid in dllGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Unload the DLL first if possible
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                            if (asset != null)
                            {
                                Resources.UnloadAsset(asset);
                            }
                            
                            // Delete the DLL
                            AssetDatabase.DeleteAsset(path);
                            deletedCount++;
                            WriteLog($"  Cleaned up DLL on load: {path}");
                        }
                        catch (Exception ex)
                        {
                            WriteLog($"  Warning: Could not delete DLL {path}: {ex.Message}");
                        }
                    }
                }
                
                if (deletedCount > 0)
                {
                    AssetDatabase.Refresh();
                    WriteLog($"Cleaned up {deletedCount} DLL(s) on editor load");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"ERROR in CleanupDllsOnLoad: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private static void DeleteDllsFromPluginsBeforePatching()
        {
            try
            {
                WriteLog("Deleting DLLs from Plugins folder before patching to ensure Library/YUCP/ DLLs are used...");
                
                string pluginsPath = "Packages/com.yucp.temp/Plugins";
                if (!AssetDatabase.IsValidFolder(pluginsPath))
                {
                    return;
                }
                
                string[] dllGuids = AssetDatabase.FindAssets("t:DefaultAsset", new[] { pluginsPath });
                int deletedCount = 0;
                
                foreach (var guid in dllGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Force unload the DLL first
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                            if (asset != null)
                            {
                                Resources.UnloadAsset(asset);
                                asset = null;
                            }
                            
                            // Use direct file deletion to bypass Unity's locks
                            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                            string physicalPath = Path.Combine(projectPath, path.Replace('/', Path.DirectorySeparatorChar));
                            
                            if (File.Exists(physicalPath))
                            {
                                // Remove read-only attribute
                                File.SetAttributes(physicalPath, FileAttributes.Normal);
                                
                                // Delete the file directly
                                File.Delete(physicalPath);
                                
                                // Delete .meta file
                                string metaPath = physicalPath + ".meta";
                                if (File.Exists(metaPath))
                                {
                                    File.SetAttributes(metaPath, FileAttributes.Normal);
                                    File.Delete(metaPath);
                                }
                                
                                deletedCount++;
                                WriteLog($"  Deleted DLL before patching: {path}");
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteLog($"  Warning: Could not delete DLL {path} before patching: {ex.Message}");
                        }
                    }
                }
                
                if (deletedCount > 0)
                {
                    AssetDatabase.Refresh();
                    WriteLog($"Deleted {deletedCount} DLL(s) from Plugins before patching");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"ERROR in DeleteDllsFromPluginsBeforePatching: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private static void CleanupDllsAfterPatchApplication()
        {
            try
            {
                WriteLog("Cleaning up DLLs after patch application...");
                
                // Free the loaded DLL libraries first to release file locks
                try
                {
                    var hdiffPatchWrapperType = System.Type.GetType("YUCP.DevTools.Editor.PackageExporter.HDiffPatchWrapper, yucp.devtools.Editor");
                    if (hdiffPatchWrapperType != null)
                    {
                        var freeDllsMethod = hdiffPatchWrapperType.GetMethod("FreeDlls", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (freeDllsMethod != null)
                        {
                            freeDllsMethod.Invoke(null, null);
                            WriteLog("  Freed loaded DLL libraries");
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"  Warning: Could not free DLL libraries: {ex.Message}");
                }
                
                // Use delayCall to wait for file handles to be released
                EditorApplication.delayCall += () =>
                {
                    CleanupDllsFromPluginsFolder();
                };
            }
            catch (Exception ex)
            {
                WriteLog($"ERROR in CleanupDllsAfterPatchApplication: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private static void CleanupDllsFromPluginsFolder()
        {
            try
            {
                string pluginsPath = "Packages/com.yucp.temp/Plugins";
                if (!AssetDatabase.IsValidFolder(pluginsPath))
                {
                    return;
                }
                
                string[] dllGuids = AssetDatabase.FindAssets("t:DefaultAsset", new[] { pluginsPath });
                int deletedCount = 0;
                
                foreach (var guid in dllGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Force unload the DLL first
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                            if (asset != null)
                            {
                                Resources.UnloadAsset(asset);
                                asset = null;
                            }
                            
                            // Use direct file deletion to bypass Unity's locks
                            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                            string physicalPath = Path.Combine(projectPath, path.Replace('/', Path.DirectorySeparatorChar));
                            
                            if (File.Exists(physicalPath))
                            {
                                // Remove read-only attribute
                                File.SetAttributes(physicalPath, FileAttributes.Normal);
                                
                                // Delete the file directly
                                File.Delete(physicalPath);
                                
                                // Delete .meta file
                                string metaPath = physicalPath + ".meta";
                                if (File.Exists(metaPath))
                                {
                                    File.SetAttributes(metaPath, FileAttributes.Normal);
                                    File.Delete(metaPath);
                                }
                                
                                deletedCount++;
                                WriteLog($"  Deleted DLL after patch application: {path}");
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteLog($"  Warning: Could not delete DLL {path}: {ex.Message}");
                        }
                    }
                }
                
                if (deletedCount > 0)
                {
                    AssetDatabase.Refresh();
                    WriteLog($"Cleaned up {deletedCount} DLL(s) from Plugins folder");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"ERROR in CleanupDllsFromPluginsFolder: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            WriteLog($"OnPostprocessAllAssets called with {importedAssets?.Length ?? 0} imported assets");
            
            // Skip if we're in the middle of an export
            try
            {
                var packageBuilderType = System.Type.GetType("YUCP.DevTools.Editor.PackageExporter.PackageBuilder, Assembly-CSharp-Editor");
                if (packageBuilderType != null)
                {
                    var isExportingField = packageBuilderType.GetField("s_isExporting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (isExportingField != null && (bool)isExportingField.GetValue(null))
                    {
                        WriteLog("Export in progress, skipping patch detection");
                        return;
                    }
                }
            }
            catch { }
            
            if (processedPatches == null)
                processedPatches = new HashSet<string>();
            
            if (importedTempFiles == null)
                importedTempFiles = new HashSet<string>();
            
            foreach (var path in importedAssets ?? new string[0])
            {
                if (path.Contains("com.yucp.temp") && 
                    !path.Contains("/Derived/") && !path.Contains("\\Derived\\"))
                {
                    bool isInTempFolder = path.Contains("/Patches/") || path.Contains("\\Patches\\") ||
                                         path.Contains("/Editor/") || path.Contains("\\Editor\\") ||
                                         path.Contains("/Plugins/") || path.Contains("\\Plugins\\");
                    
                    bool isRootFile = path.Contains("com.yucp.temp/package.json") ||
                                     path.Contains("com.yucp.temp\\package.json") ||
                                     (path.Contains("com.yucp.temp") && 
                                      (path.EndsWith(".asmdef") || path.EndsWith(".asmdef.meta") ||
                                       path.EndsWith(".asmdef.json") || path.EndsWith(".asmdef.json.meta")));
                    
                    if (isInTempFolder || isRootFile)
                    {
                        importedTempFiles.Add(path);
                        WriteLog($"  Tracked imported temp file for cleanup: {path}");
                    }
                }
            }
            
            int patchCount = 0;
            foreach (var path in importedAssets ?? new string[0])
            {
                // Skip derived assets - they're outputs, not inputs
                if (path.Contains("/Derived/") || path.Contains("\\Derived\\"))
                {
                    continue;
                }
                
                if (!path.Contains("/Patches/") && !path.Contains("\\Patches\\"))
                {
                    continue;
                }
                
                if (processedPatches.Contains(path))
                {
                    continue;
                }
                
                if (path.Contains("DerivedFbxAsset") && path.EndsWith(".asset") && path.Contains("com.yucp.temp"))
                {
                    patchCount++;
                    WriteLog($"  FOUND PATCH: {path}");
                    processedPatches.Add(path);
                    
                    // Check if scripts were just imported - if so, wait for compilation
                    bool scriptsJustImported = false;
                    foreach (var importedPath in importedAssets ?? new string[0])
                    {
                        if (importedPath.Contains("com.yucp.temp/Editor") && 
                            (importedPath.EndsWith(".cs") || importedPath.EndsWith(".asmdef")))
                        {
                            scriptsJustImported = true;
                            WriteLog($"  Scripts were just imported, waiting for compilation before applying patch...");
                            break;
                        }
                    }
                    
                    if (scriptsJustImported)
                    {
                        // Wait for compilation with nested delayCall
                        EditorApplication.delayCall += () =>
                        {
                            EditorApplication.delayCall += () =>
                            {
                                EditorApplication.delayCall += () =>
                                {
                                    WriteLog($"  Retrying patch application after script compilation: {path}");
                                    TryApplyPatch(path);
                                };
                            };
                        };
                    }
                    else
                    {
                        TryApplyPatch(path);
                    }
                }
            }
            
            WriteLog($"Processed {patchCount} patch(es) from {importedAssets?.Length ?? 0} imported assets");
        }
        
        private static void TryApplyPatch(string patchPath)
        {
            WriteLog($"TryApplyPatch called for: {patchPath}");
            try
            {
                // First, fix script GUID and namespace references if needed (must happen before type lookup)
                string derivedFbxAssetScriptPath = "Packages/com.yucp.temp/Editor/DerivedFbxAsset.cs";
                string derivedFbxAssetScriptGuid = AssetDatabase.AssetPathToGUID(derivedFbxAssetScriptPath);
                
                if (!string.IsNullOrEmpty(derivedFbxAssetScriptGuid))
                {
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string physicalAssetPath = Path.Combine(projectPath, patchPath.Replace('/', Path.DirectorySeparatorChar));
                    
                    if (File.Exists(physicalAssetPath))
                    {
                        string assetContent = File.ReadAllText(physicalAssetPath);
                        bool needsUpdate = false;
                        
                        // Fix script GUID
                        var guidPattern = new System.Text.RegularExpressions.Regex(@"m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([a-f0-9]{32}),\s*type:\s*\d+\}");
                        var match = guidPattern.Match(assetContent);
                        
                        if (match.Success)
                        {
                            string currentGuid = match.Groups[1].Value;
                            if (currentGuid != derivedFbxAssetScriptGuid)
                            {
                                WriteLog($"  Fixing script GUID reference: {currentGuid} -> {derivedFbxAssetScriptGuid}");
                                assetContent = guidPattern.Replace(assetContent, $"m_Script: {{fileID: 11500000, guid: {derivedFbxAssetScriptGuid}, type: 3}}");
                                needsUpdate = true;
                            }
                        }
                        
                        // Fix namespace references - catch all variations
                        bool namespaceFixed = false;
                        
                        // Fix nested type references: YUCP.DevTools.Editor.PackageExporter.DerivedFbxAsset/EmbeddedBlendshapeOp
                        if (System.Text.RegularExpressions.Regex.IsMatch(assetContent, @"YUCP\.DevTools\.Editor\.PackageExporter"))
                        {
                            WriteLog($"  Fixing namespace references (DevTools -> PatchRuntime)...");
                            assetContent = System.Text.RegularExpressions.Regex.Replace(
                                assetContent,
                                @"YUCP\.DevTools\.Editor\.PackageExporter\.DerivedFbxAsset/(\w+)",
                                "YUCP.PatchRuntime.DerivedFbxAsset/$1"
                            );
                            assetContent = System.Text.RegularExpressions.Regex.Replace(
                                assetContent,
                                @"YUCP\.DevTools\.Editor\.PackageExporter",
                                "YUCP.PatchRuntime"
                            );
                            namespaceFixed = true;
                            needsUpdate = true;
                        }
                        
                        // Fix assembly references: com.yucp.devtools.Editor
                        if (System.Text.RegularExpressions.Regex.IsMatch(assetContent, @"com\.yucp\.devtools\.Editor"))
                        {
                            WriteLog($"  Fixing assembly references...");
                            assetContent = System.Text.RegularExpressions.Regex.Replace(
                                assetContent,
                                @"com\.yucp\.devtools\.Editor",
                                "YUCP.PatchRuntime"
                            );
                            namespaceFixed = true;
                            needsUpdate = true;
                        }
                        
                        if (System.Text.RegularExpressions.Regex.IsMatch(assetContent, @"YUCP\.DevTools"))
                        {
                            WriteLog($"  Fixing remaining DevTools namespace references...");
                            assetContent = System.Text.RegularExpressions.Regex.Replace(
                                assetContent,
                                @"YUCP\.DevTools[^\s,}]+",
                                "YUCP.PatchRuntime"
                            );
                            namespaceFixed = true;
                            needsUpdate = true;
                        }
                        
                        if (namespaceFixed)
                        {
                            WriteLog($"  Namespace fix applied");
                        }
                        
                        if (needsUpdate)
                        {
                            File.WriteAllText(physicalAssetPath, assetContent);
                            AssetDatabase.ImportAsset(patchPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                            WriteLog($"  Reimported asset after fixing references");
                            
                            // Wait a moment for Unity to process the reimport before continuing
                            EditorApplication.delayCall += () =>
                            {
                                TryApplyPatchInternal(patchPath);
                            };
                            return;
                        }
                    }
                }
                
                // Continue with type lookup
                TryApplyPatchInternal(patchPath);
            }
            catch (Exception ex)
            {
                WriteLog($"  ERROR in TryApplyPatch: {ex.Message}\n{ex.StackTrace}");
                Debug.LogWarning($"[YUCP PatchImporter] Error processing patch {patchPath}: {ex.Message}");
            }
        }
        
        private static void TryApplyPatchInternal(string patchPath)
        {
            try
            {
                // Find DerivedFbxAsset type
                // Try multiple approaches since assembly loading can be timing-sensitive
                System.Type derivedFbxAssetType = null;
                
                // First, try direct type lookup with assembly name
                try
                {
                    derivedFbxAssetType = System.Type.GetType("YUCP.PatchRuntime.DerivedFbxAsset, YUCP.PatchRuntime");
                    if (derivedFbxAssetType != null)
                    {
                        WriteLog($"  Found DerivedFbxAsset type via direct lookup: {derivedFbxAssetType.Assembly.FullName}");
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"  Direct type lookup failed: {ex.Message}");
                }
                
                // If not found, search all loaded assemblies
                if (derivedFbxAssetType == null)
                {
                    var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                    WriteLog($"  Searching {assemblies.Length} loaded assemblies for DerivedFbxAsset type...");
                    
                    foreach (var asm in assemblies)
                    {
                        try
                        {
                            // Check if this assembly is likely to contain the type
                            if (asm.FullName.Contains("YUCP.PatchRuntime") || 
                                asm.GetName().Name == "YUCP.PatchRuntime" ||
                                asm.FullName.Contains("PatchRuntime"))
                            {
                                WriteLog($"  Checking assembly: {asm.FullName}");
                                derivedFbxAssetType = asm.GetType("YUCP.PatchRuntime.DerivedFbxAsset");
                                if (derivedFbxAssetType != null)
                                {
                                    WriteLog($"  Found DerivedFbxAsset type in assembly: {asm.FullName}");
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Ignore exceptions during type lookup
                            WriteLog($"  Exception checking assembly {asm.FullName}: {ex.Message}");
                        }
                    }
                    
                    // If still not found, do a broader search
                    if (derivedFbxAssetType == null)
                    {
                        WriteLog("  Broad search: checking all assemblies for DerivedFbxAsset...");
                        foreach (var asm in assemblies)
                        {
                            try
                            {
                                derivedFbxAssetType = asm.GetType("YUCP.PatchRuntime.DerivedFbxAsset");
                                if (derivedFbxAssetType != null)
                                {
                                    WriteLog($"  Found DerivedFbxAsset type in assembly: {asm.FullName}");
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }
                
                if (derivedFbxAssetType == null)
                {
                    WriteLog("  WARNING: DerivedFbxAsset type not found - assembly may not be compiled yet, will retry");
                    
                    // Check if the script file exists but assembly hasn't compiled yet
                    string derivedFbxAssetScriptPath = "Packages/com.yucp.temp/Editor/DerivedFbxAsset.cs";
                    if (File.Exists(Path.Combine(Application.dataPath, "..", derivedFbxAssetScriptPath.Replace('/', Path.DirectorySeparatorChar))))
                    {
                        // Script exists but type not found - assembly needs to compile
                        // Retry after a delay to allow compilation
                        WriteLog("  Script file exists, waiting for assembly compilation...");
                        EditorApplication.delayCall += () =>
                        {
                            EditorApplication.delayCall += () =>
                            {
                                // Retry after compilation
                                if (processedPatches != null && processedPatches.Contains(patchPath))
                                {
                                    processedPatches.Remove(patchPath);
                                }
                                WriteLog($"  Retrying patch application after compilation delay: {patchPath}");
                                TryApplyPatch(patchPath);
                            };
                        };
                        return;
                    }
                    else
                    {
                        // Script doesn't exist - this is a real error
                        WriteLog("  ERROR: DerivedFbxAsset script file not found");
                        Debug.LogWarning($"[YUCP PatchImporter] DerivedFbxAsset type not found for patch {patchPath}. Script file missing. Ensure patch scripts are in temp package if patches are needed.");
                        return;
                    }
                }
                
                // Force import to ensure asset is up to date
                AssetDatabase.ImportAsset(patchPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                
                // Load the asset
                var patch = AssetDatabase.LoadAssetAtPath(patchPath, derivedFbxAssetType);
                if (patch == null)
                {
                    // Validate file exists and check size before loading (very large files can cause crashes)
                    string physicalPath = patchPath.Replace("Packages/", "").Replace("Assets/", "");
                    physicalPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), physicalPath);
                    
                    if (File.Exists(physicalPath))
                    {
                        long fileSize = new FileInfo(physicalPath).Length;
                        // Warn if file is very large (over 100MB) - might cause serialization issues
                        if (fileSize > 100 * 1024 * 1024)
                        {
                            WriteLog($"  WARNING: Patch file is very large ({fileSize / (1024 * 1024)}MB), may cause issues");
                            Debug.LogWarning($"[YUCP PatchImporter] Patch file is very large: {patchPath} ({fileSize / (1024 * 1024)}MB)");
                        }
                    }
                    
                    var genericObj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(patchPath);
                    if (genericObj != null && derivedFbxAssetType.IsAssignableFrom(genericObj.GetType()))
                    {
                        patch = genericObj;
                    }
                }
                
                if (patch == null)
                {
                    WriteLog($"  ERROR: Could not load patch asset at path: {patchPath}");
                    Debug.LogWarning($"[YUCP PatchImporter] Could not load patch asset: {patchPath}");
                    return;
                }
                
                // Check if the asset has hdiffFilePath (required for binary patching)
                try
                {
                    var hdiffFilePathField = patch.GetType().GetField("hdiffFilePath");
                    if (hdiffFilePathField != null)
                    {
                        string hdiffFilePath = hdiffFilePathField.GetValue(patch) as string;
                        if (string.IsNullOrEmpty(hdiffFilePath))
                        {
                            Debug.LogError($"[YUCP PatchImporter] Patch asset {patchPath} has no hdiffFilePath. This asset was created with an incompatible format.\n" +
                                "Please delete this asset file and re-export your package.\n" +
                                $"Delete: {patchPath}");
                            WriteLog($"  ERROR: Patch asset has no hdiffFilePath");
                            return;
                        }
                        
                        // Verify .hdiff file exists
                        string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                        string hdiffPhysicalPath = Path.Combine(projectPath, hdiffFilePath.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(hdiffPhysicalPath))
                        {
                            Debug.LogError($"[YUCP PatchImporter] .hdiff file not found: {hdiffPhysicalPath}\n" +
                                "The patch file may be missing from the package. Please re-export your package.");
                            WriteLog($"  ERROR: .hdiff file not found: {hdiffFilePath}");
                            return;
                        }
                        
                        WriteLog($"  Found .hdiff file: {hdiffFilePath}");
                    }
                    else
                    {
                        Debug.LogWarning($"[YUCP PatchImporter] Patch asset {patchPath} does not have hdiffFilePath field. This may be an old format patch.");
                        WriteLog($"  WARNING: Patch asset missing hdiffFilePath field");
                    }
                }
                catch (Exception checkEx)
                {
                    Debug.LogError($"[YUCP PatchImporter] Patch asset {patchPath} appears to be corrupted or incompatible.\n" +
                        "This may be due to a format change. Please delete this asset file and re-export your package.\n" +
                        $"Delete: {patchPath}\nError: {checkEx.Message}");
                    return;
                }
                
                WriteLog($"  Patch asset loaded successfully: {patch.GetType().FullName}");
                ApplyPatch(patch, patchPath);
            }
            catch (Exception ex)
            {
                WriteLog($"  ERROR in TryApplyPatch: {ex.Message}\n{ex.StackTrace}");
                Debug.LogWarning($"[YUCP PatchImporter] Error processing patch {patchPath}: {ex.Message}");
            }
        }
        
        private static void ApplyPatch(UnityEngine.Object patchObj, string patchPath)
        {
            try
            {
                // Delete DLLs from Plugins BEFORE patching
                DeleteDllsFromPluginsBeforePatching();
                
                var patchType = patchObj.GetType();
                
                // Check for kitbash mode (multi-source)
                var modeField = patchType.GetField("mode");
                int mode = 0; // Default: SingleBaseHdiff
                if (modeField != null)
                {
                    var modeValue = modeField.GetValue(patchObj);
                    if (modeValue != null)
                    {
                        mode = (int)modeValue;
                    }
                }
                
                // Mode 1 = KitbashRecipeHdiff (multi-source)
                if (mode == 1)
                {
                    ApplyKitbashPatch(patchObj, patchPath);
                    return;
                }
                
                // Mode 0 = SingleBaseHdiff (original flow)
                // Get patch properties using reflection
                var baseFbxGuidField = patchType.GetField("baseFbxGuid");
                var derivedFbxGuidField = patchType.GetField("derivedFbxGuid");
                var targetFbxNameField = patchType.GetField("targetFbxName");
                var originalDerivedFbxPathField = patchType.GetField("originalDerivedFbxPath");
                var uiHintsField = patchType.GetField("uiHints");
                
                string baseFbxGuid = baseFbxGuidField != null ? baseFbxGuidField.GetValue(patchObj) as string : null;
                string derivedFbxGuid = derivedFbxGuidField != null ? derivedFbxGuidField.GetValue(patchObj) as string : null;
                string targetFbxName = targetFbxNameField != null ? targetFbxNameField.GetValue(patchObj) as string : null;
                string originalDerivedFbxPath = originalDerivedFbxPathField != null ? originalDerivedFbxPathField.GetValue(patchObj) as string : null;
                
                // Get friendly name from UIHints
                string friendlyName = Path.GetFileNameWithoutExtension(patchPath);
                if (uiHintsField != null)
                {
                    var uiHints = uiHintsField.GetValue(patchObj);
                    if (uiHints != null)
                    {
                        var friendlyNameField = uiHints.GetType().GetField("friendlyName");
                        if (friendlyNameField != null)
                        {
                            var fn = friendlyNameField.GetValue(uiHints) as string;
                            if (!string.IsNullOrEmpty(fn))
                                friendlyName = fn;
                        }
                    }
                }
                
                if (string.IsNullOrEmpty(targetFbxName))
                {
                    targetFbxName = friendlyName;
                }
                
                // Direct targeting: use baseFbxGuid to find the target FBX
                if (string.IsNullOrEmpty(baseFbxGuid))
                {
                    WriteLog($"  ERROR: baseFbxGuid is missing from patch");
                    Debug.LogWarning($"[YUCP PatchImporter] Patch missing baseFbxGuid, cannot apply");
                    return;
                }
                
                // Refresh AssetDatabase to ensure all assets are indexed
                AssetDatabase.Refresh();
                
                string baseFbxPath = AssetDatabase.GUIDToAssetPath(baseFbxGuid);
                
                // If GUID doesn't resolve, try refreshing and waiting for import to complete
                if (string.IsNullOrEmpty(baseFbxPath) || !baseFbxPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    WriteLog($"  WARNING: baseFbxGuid {baseFbxGuid} does not resolve immediately (path: '{baseFbxPath ?? "null"}'), waiting for AssetDatabase refresh...");
                    Debug.LogWarning($"[YUCP PatchImporter] baseFbxGuid {baseFbxGuid} does not resolve immediately. Running diagnostics...");
                    
                    // ========== COMPREHENSIVE DIAGNOSTICS ==========
                    
                    // Diagnostic 1: Validate GUID format
                    WriteLog($"  [DIAG] GUID Format Check:");
                    WriteLog($"    GUID: {baseFbxGuid}");
                    WriteLog($"    Length: {baseFbxGuid?.Length ?? 0} (expected: 32)");
                    WriteLog($"    Is Valid Format: {!string.IsNullOrEmpty(baseFbxGuid) && baseFbxGuid.Length == 32 && Regex.IsMatch(baseFbxGuid, @"^[0-9a-f]{32}$", RegexOptions.IgnoreCase)}");
                    
                    // Diagnostic 2: Check if GUID exists in AssetDatabase at all (any asset type)
                    WriteLog($"  [DIAG] AssetDatabase GUID Lookup:");
                    string anyAssetPath = AssetDatabase.GUIDToAssetPath(baseFbxGuid);
                    WriteLog($"    GUID resolves to: {(string.IsNullOrEmpty(anyAssetPath) ? "NOTHING" : anyAssetPath)}");
                    if (!string.IsNullOrEmpty(anyAssetPath))
                    {
                        WriteLog($"    Asset Type: {AssetDatabase.GetMainAssetTypeAtPath(anyAssetPath)?.Name ?? "Unknown"}");
                        WriteLog($"    Is FBX: {anyAssetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)}");
                    }
                    
                    // Diagnostic 3: Search all FBX files in AssetDatabase
                    WriteLog($"  [DIAG] FBX Files in AssetDatabase:");
                    try
                    {
                        string[] allFbxGuids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets" });
                        WriteLog($"    Found {allFbxGuids.Length} GameObject assets (potential FBX files)");
                        
                        // Also search for ModelImporter assets
                        string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { "Assets" });
                        WriteLog($"    Found {modelGuids.Length} Model assets");
                        
                        // Check first 50 assets for FBX extension
                        int fbxCount = 0;
                        var fbxGuidMap = new Dictionary<string, string>();
                        foreach (var guid in allFbxGuids.Take(50))
                        {
                            try
                            {
                                string path = AssetDatabase.GUIDToAssetPath(guid);
                                if (!string.IsNullOrEmpty(path) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                                {
                                    fbxCount++;
                                    fbxGuidMap[guid] = path;
                                    if (guid == baseFbxGuid)
                                    {
                                        WriteLog($"    *** MATCH FOUND! GUID {guid} -> {path}");
                                        baseFbxPath = path;
                                    }
                                }
                            }
                            catch { }
                        }
                        WriteLog($"    Found {fbxCount} actual .fbx files in first 50 assets");
                        
                        // Check if our GUID is in the list
                        if (fbxGuidMap.ContainsKey(baseFbxGuid))
                        {
                            WriteLog($"    *** baseFbxGuid found in AssetDatabase! Path: {fbxGuidMap[baseFbxGuid]}");
                            baseFbxPath = fbxGuidMap[baseFbxGuid];
                        }
                    }
                    catch (Exception diagEx)
                    {
                        WriteLog($"    Error searching AssetDatabase: {diagEx.Message}");
                    }
                    
                    // Diagnostic 4: Search for .fbx files on disk and check their GUIDs
                    WriteLog($"  [DIAG] FBX Files on Disk:");
                    try
                    {
                        string[] fbxFiles = Directory.GetFiles(Application.dataPath, "*.fbx", SearchOption.AllDirectories);
                        WriteLog($"    Found {fbxFiles.Length} .fbx files on disk");
                        
                        int checkedCount = 0;
                        int matchingCount = 0;
                        var guidMatches = new List<string>();
                        
                        // Check all FBX files (not just first 20)
                        foreach (var fbxFile in fbxFiles)
                        {
                            try
                            {
                                string relativePath = fbxFile.Replace(Application.dataPath, "Assets").Replace('\\', '/');
                                string guid = AssetDatabase.AssetPathToGUID(relativePath);
                                checkedCount++;
                                
                                if (guid == baseFbxGuid)
                                {
                                    WriteLog($"    *** MATCH FOUND on disk! File: {relativePath}, GUID: {guid}");
                                    guidMatches.Add(relativePath);
                                    if (string.IsNullOrEmpty(baseFbxPath))
                                    {
                                        baseFbxPath = relativePath;
                                    }
                                    matchingCount++;
                                }
                                
                                // Also check for partial GUID matches (first 8 chars)
                                if (guid.Length >= 8 && baseFbxGuid.Length >= 8 && 
                                    guid.Substring(0, 8).Equals(baseFbxGuid.Substring(0, 8), StringComparison.OrdinalIgnoreCase) &&
                                    guid != baseFbxGuid)
                                {
                                    WriteLog($"    Partial GUID match (first 8 chars): {relativePath} -> {guid}");
                                }
                            }
                            catch (Exception)
                            {
                                // Silently continue - some files might not be importable
                            }
                        }
                        
                        WriteLog($"    Checked {checkedCount} files, found {matchingCount} exact GUID matches");
                        if (matchingCount > 0)
                        {
                            WriteLog($"    Matching files: {string.Join(", ", guidMatches)}");
                        }
                    }
                    catch (Exception diagEx)
                    {
                        WriteLog($"    Error searching disk: {diagEx.Message}");
                        WriteLog($"    Stack trace: {diagEx.StackTrace}");
                    }
                    
                    // Diagnostic 5: Search for GUID in .meta files directly
                    WriteLog($"  [DIAG] Searching .meta files for GUID:");
                    try
                    {
                        string[] metaFiles = Directory.GetFiles(Application.dataPath, "*.meta", SearchOption.AllDirectories);
                        WriteLog($"    Found {metaFiles.Length} .meta files");
                        
                        int metaMatches = 0;
                        var matchingMetaFiles = new List<string>();
                        
                        foreach (var metaFile in metaFiles.Take(1000)) // Limit to first 1000 for performance
                        {
                            try
                            {
                                string metaContent = File.ReadAllText(metaFile);
                                if (metaContent.Contains(baseFbxGuid))
                                {
                                    string assetFile = metaFile.Substring(0, metaFile.Length - 5); // Remove .meta
                                    string relativePath = assetFile.Replace(Application.dataPath, "Assets").Replace('\\', '/');
                                    
                                    // Check if it's actually an FBX file
                                    if (File.Exists(assetFile) && assetFile.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                                    {
                                        metaMatches++;
                                        matchingMetaFiles.Add(relativePath);
                                        WriteLog($"    *** Found GUID in .meta file: {relativePath}");
                                        
                                        // Verify the GUID in the meta file
                                        var guidMatch = Regex.Match(metaContent, @"guid:\s*([0-9a-f]{32})", RegexOptions.IgnoreCase);
                                        if (guidMatch.Success)
                                        {
                                            string metaGuid = guidMatch.Groups[1].Value;
                                            if (metaGuid.Equals(baseFbxGuid, StringComparison.OrdinalIgnoreCase))
                                            {
                                                WriteLog($"      Confirmed: .meta file contains exact GUID match!");
                                                if (string.IsNullOrEmpty(baseFbxPath))
                                                {
                                                    baseFbxPath = relativePath;
                                                }
                                            }
                                            else
                                            {
                                                WriteLog($"      WARNING: .meta file GUID mismatch! Expected: {baseFbxGuid}, Found: {metaGuid}");
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                // Continue searching
                            }
                        }
                        
                        WriteLog($"    Found {metaMatches} .meta files containing the GUID");
                        if (metaMatches > 0)
                        {
                            WriteLog($"    Matching .meta files: {string.Join(", ", matchingMetaFiles)}");
                        }
                    }
                    catch (Exception diagEx)
                    {
                        WriteLog($"    Error searching .meta files: {diagEx.Message}");
                    }
                    
                    // Diagnostic 6: Check for similar GUIDs (typos, case differences)
                    WriteLog($"  [DIAG] Checking for similar GUIDs:");
                    try
                    {
                        string guidLower = baseFbxGuid.ToLowerInvariant();
                        string[] allGuids = AssetDatabase.FindAssets("", new[] { "Assets" });
                        WriteLog($"    Searching through {allGuids.Length} assets for similar GUIDs...");
                        
                        var similarGuids = new List<string>();
                        foreach (var guid in allGuids.Take(1000))
                        {
                            if (guid.ToLowerInvariant() == guidLower && guid != baseFbxGuid)
                            {
                                string path = AssetDatabase.GUIDToAssetPath(guid);
                                if (!string.IsNullOrEmpty(path) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                                {
                                    similarGuids.Add($"{guid} -> {path}");
                                }
                            }
                        }
                        
                        if (similarGuids.Count > 0)
                        {
                            WriteLog($"    Found {similarGuids.Count} similar GUIDs (case difference?):");
                            foreach (var similar in similarGuids)
                            {
                                WriteLog($"      {similar}");
                            }
                        }
                        else
                        {
                            WriteLog($"    No similar GUIDs found");
                        }
                    }
                    catch (Exception diagEx)
                    {
                        WriteLog($"    Error checking similar GUIDs: {diagEx.Message}");
                    }
                    
                    // Diagnostic 7: Check AssetDatabase state
                    WriteLog($"  [DIAG] AssetDatabase State:");
                    WriteLog($"    Application data path: {Application.dataPath}");
                    WriteLog($"    Unity version: {Application.unityVersion}");
                    WriteLog($"    Is playing: {Application.isPlaying}");
                    WriteLog($"    Is editor: {Application.isEditor}");
                    
                    // Diagnostic 8: List all FBX files with their GUIDs (sample)
                    WriteLog($"  [DIAG] Sample FBX Files and GUIDs (first 10):");
                    try
                    {
                        string[] sampleFbx = Directory.GetFiles(Application.dataPath, "*.fbx", SearchOption.AllDirectories).Take(10).ToArray();
                        foreach (var fbx in sampleFbx)
                        {
                            try
                            {
                                string relPath = fbx.Replace(Application.dataPath, "Assets").Replace('\\', '/');
                                string guid = AssetDatabase.AssetPathToGUID(relPath);
                                WriteLog($"      {Path.GetFileName(fbx)} -> GUID: {guid}");
                            }
                            catch { }
                        }
                    }
                    catch (Exception diagEx)
                    {
                        WriteLog($"    Error listing sample FBX files: {diagEx.Message}");
                    }
                    
                    WriteLog($"  [DIAG] Diagnostics complete. baseFbxPath resolved to: {(string.IsNullOrEmpty(baseFbxPath) ? "NULL" : baseFbxPath)}");
                    
                    // If still not found, retry with delays
                    if (string.IsNullOrEmpty(baseFbxPath) || !baseFbxPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteLog($"  Scheduling retry after AssetDatabase refresh...");
                        
                        // Retry after delay to allow AssetDatabase to finish indexing
                        EditorApplication.delayCall += () =>
                        {
                            EditorApplication.delayCall += () =>
                            {
                                AssetDatabase.Refresh();
                                string retryBaseFbxPath = AssetDatabase.GUIDToAssetPath(baseFbxGuid);
                                
                                if (string.IsNullOrEmpty(retryBaseFbxPath) || !retryBaseFbxPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Try one more time with a longer delay
                                    WriteLog($"  [RETRY 1] First retry failed. Resolved path: '{retryBaseFbxPath ?? "null"}'. Trying again with longer delay...");
                                    Debug.LogWarning($"[YUCP PatchImporter] First retry failed for baseFbxGuid {baseFbxGuid}. Path resolved to: '{retryBaseFbxPath ?? "null"}'");
                                    
                                    EditorApplication.delayCall += () =>
                                    {
                                        AssetDatabase.Refresh();
                                        string finalBaseFbxPath = AssetDatabase.GUIDToAssetPath(baseFbxGuid);
                                        
                                        if (string.IsNullOrEmpty(finalBaseFbxPath) || !finalBaseFbxPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                                        {
                                            WriteLog($"  [RETRY 2] Final retry failed. Resolved path: '{finalBaseFbxPath ?? "null"}'");
                                            
                                            // Final diagnostic summary
                                            WriteLog($"  [FINAL DIAG] Summary of failure:");
                                            WriteLog($"    - baseFbxGuid: {baseFbxGuid}");
                                            WriteLog($"    - Attempted 3 times (initial + 2 retries)");
                                            WriteLog($"    - Final resolved path: {(string.IsNullOrEmpty(finalBaseFbxPath) ? "NULL" : finalBaseFbxPath)}");
                                            
                                            // Quick check: Is the GUID in any .meta file?
                                            try
                                            {
                                                string[] allMetaFiles = Directory.GetFiles(Application.dataPath, "*.meta", SearchOption.AllDirectories);
                                                int guidInMetaCount = 0;
                                                foreach (var meta in allMetaFiles.Take(500))
                                                {
                                                    try
                                                    {
                                                        if (File.ReadAllText(meta).Contains(baseFbxGuid))
                                                        {
                                                            guidInMetaCount++;
                                                            string assetFile = meta.Substring(0, meta.Length - 5);
                                                            string relPath = assetFile.Replace(Application.dataPath, "Assets").Replace('\\', '/');
                                                            WriteLog($"    - GUID found in .meta file: {relPath}");
                                                            if (guidInMetaCount >= 5) break; // Limit output
                                                        }
                                                    }
                                                    catch { }
                                                }
                                                WriteLog($"    - GUID found in {guidInMetaCount} .meta file(s)");
                                            }
                                            catch (Exception finalDiagEx)
                                            {
                                                WriteLog($"    - Error in final diagnostic: {finalDiagEx.Message}");
                                            }
                                            
                                            WriteLog($"  ERROR: baseFbxGuid {baseFbxGuid} still does not resolve after retries. " +
                                                $"GUID may be incorrect or base FBX is not in the project.");
                                            Debug.LogError($"[YUCP PatchImporter] baseFbxGuid {baseFbxGuid} does not resolve to a valid FBX after retries. " +
                                                $"The base FBX with this GUID may not be in the project, or the GUID is incorrect. " +
                                                $"Please ensure the base FBX is imported before importing the patch package. " +
                                                $"Check the log file for detailed diagnostics.");
                                            return;
                                        }
                                        
                                        // Success on final retry - continue with patching
                                        WriteLog($"  [RETRY 2] Successfully resolved baseFbxGuid on final retry: {finalBaseFbxPath}");
                                        Debug.Log($"[YUCP PatchImporter] Successfully resolved baseFbxGuid {baseFbxGuid} on final retry: {finalBaseFbxPath}");
                                        ApplyPatchWithBasePath(patchObj, patchPath, finalBaseFbxPath, baseFbxGuid, derivedFbxGuid, targetFbxName, originalDerivedFbxPath);
                                    };
                                }
                                else
                                {
                                    // Success on first retry
                                    WriteLog($"  [RETRY 1] Successfully resolved baseFbxGuid on first retry: {retryBaseFbxPath}");
                                    Debug.Log($"[YUCP PatchImporter] Successfully resolved baseFbxGuid {baseFbxGuid} on first retry: {retryBaseFbxPath}");
                                    ApplyPatchWithBasePath(patchObj, patchPath, retryBaseFbxPath, baseFbxGuid, derivedFbxGuid, targetFbxName, originalDerivedFbxPath);
                                }
                            };
                        };
                        return;
                    }
                }
                
                WriteLog($"  Using direct targeting: baseFbxGuid={baseFbxGuid} -> {baseFbxPath}");
                ApplyPatchWithBasePath(patchObj, patchPath, baseFbxPath, baseFbxGuid, derivedFbxGuid, targetFbxName, originalDerivedFbxPath);
            }
            catch (Exception ex)
            {
                WriteLog($"  ERROR in ApplyPatch: {ex.Message}\n{ex.StackTrace}");
                Debug.LogError($"[YUCP PatchImporter] Error applying patch: {ex.Message}");
            }
        }
        
        private static void ApplyPatchWithBasePath(UnityEngine.Object patchObj, string patchPath, string baseFbxPath, string baseFbxGuid, string derivedFbxGuid, string targetFbxName, string originalDerivedFbxPath)
        {
            try
            {
                // Delete DLLs from Plugins BEFORE patching
                DeleteDllsFromPluginsBeforePatching();
                
                // Get DerivedFbxBuilder type
                var derivedFbxBuilderType = System.Type.GetType("YUCP.PatchRuntime.DerivedFbxBuilder, YUCP.PatchRuntime");
                if (derivedFbxBuilderType == null)
                {
                    var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var asm in assemblies)
                    {
                        try
                        {
                            derivedFbxBuilderType = asm.GetType("YUCP.PatchRuntime.DerivedFbxBuilder");
                            if (derivedFbxBuilderType != null)
                                break;
                        }
                        catch { }
                    }
                }
                
                if (derivedFbxBuilderType == null)
                {
                    WriteLog("  ERROR: DerivedFbxBuilder type not found");
                    Debug.LogError($"[YUCP PatchImporter] DerivedFbxBuilder type not found");
                    return;
                }
                
                // Get BuildDerivedFbx method
                var buildMethod = derivedFbxBuilderType.GetMethod("BuildDerivedFbx", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (buildMethod == null)
                {
                    WriteLog("  ERROR: BuildDerivedFbx method not found");
                    Debug.LogError($"[YUCP PatchImporter] BuildDerivedFbx method not found");
                    return;
                }
                
                // Determine output path - use original path if available, otherwise construct from target name
                string outputPath;
                if (!string.IsNullOrEmpty(originalDerivedFbxPath))
                {
                    outputPath = originalDerivedFbxPath;
                    WriteLog($"  Using original path: {outputPath}");
                }
                else
                {
                    // Fallback: try to find by GUID or construct path
                    if (!string.IsNullOrEmpty(derivedFbxGuid))
                    {
                        string pathByGuid = AssetDatabase.GUIDToAssetPath(derivedFbxGuid);
                        if (!string.IsNullOrEmpty(pathByGuid) && pathByGuid.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                        {
                            outputPath = pathByGuid;
                            WriteLog($"  Found path by GUID: {outputPath}");
                        }
                        else
                        {
                            outputPath = $"Packages/com.yucp.temp/Derived/{targetFbxName}.fbx";
                            WriteLog($"  Using fallback path: {outputPath}");
                        }
                    }
                    else
                    {
                        outputPath = $"Packages/com.yucp.temp/Derived/{targetFbxName}.fbx";
                        WriteLog($"  Using default path: {outputPath}");
                    }
                }
                
                WriteLog($"  Building derived FBX: {baseFbxPath} -> {outputPath}");
                WriteLog($"  Preserving GUID: {derivedFbxGuid ?? "none"}");
                
                // Store patch path for automatic retry if package installation is needed
                string patchPathKey = "YUCP.DerivedFbxBuilder.PendingPatchPath";
                EditorPrefs.SetString(patchPathKey, patchPath);
                
                // Call BuildDerivedFbx
                object result = null;
                try
                {
                    result = buildMethod.Invoke(null, new object[] { baseFbxPath, patchObj, outputPath, derivedFbxGuid ?? string.Empty });
                    
                    // Clear retry flag on success
                    EditorPrefs.DeleteKey(patchPathKey);
                }
                catch (System.Reflection.TargetInvocationException tie)
                {
                    WriteLog($"  ERROR: TargetInvocationException: {tie.Message}");
                    if (tie.InnerException != null)
                    {
                        WriteLog($"  Inner Exception: {tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
                        WriteLog($"  Stack Trace: {tie.InnerException.StackTrace}");
                        Debug.LogError($"[YUCP PatchImporter] Error invoking BuildDerivedFbx: {tie.InnerException.GetType().Name}: {tie.InnerException.Message}\n{tie.InnerException.StackTrace}");
                    }
                    throw;
                }
                catch (System.Exception ex)
                {
                    WriteLog($"  ERROR: Exception: {ex.GetType().Name}: {ex.Message}");
                    WriteLog($"  Stack Trace: {ex.StackTrace}");
                    Debug.LogError($"[YUCP PatchImporter] Error invoking BuildDerivedFbx: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
                
                string createdPath = result as string;
                
                if (string.IsNullOrEmpty(createdPath))
                {
                    WriteLog($"  ERROR: BuildDerivedFbx returned null");
                    Debug.LogError($"[YUCP PatchImporter] Failed to build derived FBX");
                    return;
                }
                
                WriteLog($"  Successfully created derived FBX: {createdPath}");
                Debug.Log($"[YUCP PatchImporter] Successfully created derived FBX: {createdPath}");
                
                // Track this derived FBX for cleanup
                if (createdDerivedFbxPaths == null)
                    createdDerivedFbxPaths = new HashSet<string>();
                createdDerivedFbxPaths.Add(createdPath);
                
                // Check if override original references is enabled
                CheckAndHandleOverrideOriginalReferences(baseFbxPath, baseFbxGuid, createdPath, patchPath);
                
                // Clean up imported temp files after successful FBX build
                // Use nested delayCall
                EditorApplication.delayCall += () =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        CleanupImportedTempFiles();
                        // Clean patching scripts from derived FBX importers
                        CleanupDerivedFbxImporterSettings();
                        // Also clean up DLLs after patches are applied
                        CleanupDllsAfterPatchApplication();
                    };
                };
                
                // Update prefab references if GUID was preserved
                if (!string.IsNullOrEmpty(derivedFbxGuid))
                {
                    WriteLog($"  Finding prefabs referencing original derived FBX (GUID: {derivedFbxGuid})...");
                    
                    // Find all prefabs that reference the original derived FBX by GUID
                    // Even though we preserved the GUID, Unity may need a refresh to recognize the new Prefab
                    string[] allPrefabGuids = AssetDatabase.FindAssets("t:Prefab");
                    int updatedCount = 0;
                    
                    foreach (var prefabGuid in allPrefabGuids)
                    {
                        string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                        if (string.IsNullOrEmpty(prefabPath)) continue;
                        
                        // Check if this prefab depends on the original derived FBX
                        string[] dependencies = AssetDatabase.GetDependencies(prefabPath, false);
                        bool referencesDerivedFbx = false;
                        
                        foreach (var depPath in dependencies)
                        {
                            string depGuid = AssetDatabase.AssetPathToGUID(depPath);
                            if (depGuid == derivedFbxGuid)
                            {
                                referencesDerivedFbx = true;
                                break;
                            }
                        }
                        
                        if (referencesDerivedFbx)
                        {
                            WriteLog($"    Found prefab referencing derived FBX: {prefabPath}");
                            
                            // Load the prefab and check if it needs updating
                            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                            if (prefab != null)
                            {
                                // Force Unity to refresh the reference by reimporting the prefab
                                // Since we preserved the GUID, Unity should recognize the new Prefab
                                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                                updatedCount++;
                            }
                        }
                    }
                    
                    // Also update mesh references in prefabs
                    var backupManagerType = System.Type.GetType("YUCP.PatchRuntime.BackupManager, YUCP.PatchRuntime");
                    if (backupManagerType != null)
                    {
                        var updatePrefabMethod = backupManagerType.GetMethod("UpdatePrefabMeshReferences", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (updatePrefabMethod != null)
                        {
                            try
                            {
                                // Load the created prefab to get derived meshes
                                var createdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(createdPath);
                                if (createdPrefab != null)
                                {
                                    // Build mesh map: mesh name -> derived mesh
                                    var meshMap = new Dictionary<string, Mesh>();
                                    foreach (var meshFilter in createdPrefab.GetComponentsInChildren<MeshFilter>(true))
                                    {
                                        if (meshFilter.sharedMesh != null && !meshMap.ContainsKey(meshFilter.sharedMesh.name))
                                            meshMap[meshFilter.sharedMesh.name] = meshFilter.sharedMesh;
                                    }
                                    foreach (var skinnedMeshRenderer in createdPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                                    {
                                        if (skinnedMeshRenderer.sharedMesh != null && !meshMap.ContainsKey(skinnedMeshRenderer.sharedMesh.name))
                                            meshMap[skinnedMeshRenderer.sharedMesh.name] = skinnedMeshRenderer.sharedMesh;
                                    }
                                    
                                    // Update mesh references in all prefabs that reference the derived FBX
                                    foreach (var prefabGuid in allPrefabGuids)
                                    {
                                        string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                                        if (string.IsNullOrEmpty(prefabPath)) continue;
                                        
                                        string[] dependencies = AssetDatabase.GetDependencies(prefabPath, false);
                                        bool referencesDerivedFbx = false;
                                        foreach (var depPath in dependencies)
                                        {
                                            if (AssetDatabase.AssetPathToGUID(depPath) == derivedFbxGuid)
                                            {
                                                referencesDerivedFbx = true;
                                                break;
                                            }
                                        }
                                        
                                        if (referencesDerivedFbx && meshMap.Count > 0)
                                        {
                                            // Update mesh references in this prefab
                                            updatePrefabMethod.Invoke(null, new object[] { prefabPath, baseFbxPath, meshMap });
                                        }
                                    }
                                    
                                    WriteLog($"  Updated {updatedCount} prefab(s) and mesh references");
                                }
                            }
                            catch (Exception prefabEx)
                            {
                                WriteLog($"  Warning: Failed to update prefab mesh references: {prefabEx.Message}");
                            }
                        }
                    }
                    else
                    {
                        WriteLog($"  Updated {updatedCount} prefab(s) (mesh reference update not available)");
                    }
                    
                    // Force a refresh
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                WriteLog($"  ERROR in ApplyPatchWithBasePath: {ex.Message}\n{ex.StackTrace}");
                Debug.LogError($"[YUCP PatchImporter] Error applying patch with base path: {ex.Message}");
            }
        }
        
        private static void CleanupImportedTempFiles()
        {
            try
            {
                if (importedTempFiles == null || importedTempFiles.Count == 0)
                {
                    WriteLog("No imported temp files to clean up");
                    return;
                }
                
                WriteLog($"Cleaning up {importedTempFiles.Count} imported temp file(s) from package...");
                
                int deletedCount = 0;
                var filesToDelete = new List<string>(importedTempFiles);
                
                foreach (var path in filesToDelete)
                {
                    if (path.Contains("/Derived/") || path.Contains("\\Derived\\"))
                    {
                        WriteLog($"  Skipping Derived folder file: {path}");
                        continue;
                    }
                    
                    // STRICT CHECK: Only delete files that are explicitly in Packages/com.yucp.temp
                    // This prevents accidental deletion of user assets (like ExportProfile files)
                    bool isInTempPackage = path.StartsWith("Packages/com.yucp.temp/", StringComparison.OrdinalIgnoreCase) ||
                                          path.StartsWith("Packages\\com.yucp.temp\\", StringComparison.OrdinalIgnoreCase);
                    
                    if (!isInTempPackage)
                    {
                        WriteLog($"  Skipping file outside temp package (safety check): {path}");
                        continue;
                    }
                    
                    // Additional check: must be in specific temp package subfolders
                    bool isInTempFolder = (path.Contains("/Patches/") || path.Contains("\\Patches\\")) ||
                                         (path.Contains("/Editor/") || path.Contains("\\Editor\\")) ||
                                         (path.Contains("/Plugins/") || path.Contains("\\Plugins\\"));
                    
                    bool isRootFile = path.Contains("com.yucp.temp/package.json") ||
                                     path.Contains("com.yucp.temp\\package.json") ||
                                     (path.Contains("com.yucp.temp") && 
                                      (path.EndsWith(".asmdef") || path.EndsWith(".asmdef.meta") ||
                                       path.EndsWith(".asmdef.json") || path.EndsWith(".asmdef.json.meta")));
                    
                    if (!isInTempFolder && !isRootFile)
                    {
                        WriteLog($"  Skipping file outside cleanup folders: {path}");
                        continue;
                    }
                    
                    // FINAL SAFETY CHECK: Never delete anything in Assets folder
                    if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteLog($"  SAFETY: Skipping file in Assets folder (should never be deleted): {path}");
                        continue;
                    }
                    
                    try
                    {
                        // For DLLs, use direct file deletion to bypass Unity's locks
                        // For other files, use AssetDatabase.DeleteAsset
                        bool isDll = path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                        
                        if (isDll)
                        {
                            // Force unload the DLL first
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                            if (asset != null)
                            {
                                Resources.UnloadAsset(asset);
                                asset = null;
                            }
                            
                            // Use direct file deletion for DLLs
                            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                            string physicalPath = Path.Combine(projectPath, path.Replace('/', Path.DirectorySeparatorChar));
                            
                            if (File.Exists(physicalPath))
                            {
                                File.SetAttributes(physicalPath, FileAttributes.Normal);
                                File.Delete(physicalPath);
                                
                                // Delete .meta file
                                string metaPath = physicalPath + ".meta";
                                if (File.Exists(metaPath))
                                {
                                    File.SetAttributes(metaPath, FileAttributes.Normal);
                                    File.Delete(metaPath);
                                }
                                
                                deletedCount++;
                                WriteLog($"  Deleted DLL (direct): {path}");
                            }
                        }
                        else
                        {
                            // Use AssetDatabase.DeleteAsset for non-DLL files
                            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null || File.Exists(Path.Combine(Application.dataPath, "..", path.Replace('/', Path.DirectorySeparatorChar))))
                            {
                                AssetDatabase.DeleteAsset(path);
                                deletedCount++;
                                WriteLog($"  Deleted: {path}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"  Warning: Failed to delete {path}: {ex.Message}");
                    }
                }
                
                // Clear the tracked files
                importedTempFiles.Clear();
                
                try
                {
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string tempPackagePath = Path.Combine(projectPath, "Packages", "com.yucp.temp");
                    
                    string[] foldersToCheck = new string[]
                    {
                        Path.Combine(tempPackagePath, "Patches"),
                        Path.Combine(tempPackagePath, "Editor"),
                        Path.Combine(tempPackagePath, "Plugins")
                    };
                    
                    foreach (var folder in foldersToCheck)
                    {
                        if (Directory.Exists(folder))
                        {
                            try
                            {
                                var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                                    .Where(f => !f.EndsWith(".meta")).ToArray();
                                
                                if (files.Length == 0)
                                {
                                    Directory.Delete(folder, true);
                                    WriteLog($"  Deleted empty folder: {folder}");
                                }
                            }
                            catch (Exception ex)
                            {
                                WriteLog($"  Warning: Could not delete folder {folder}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"  Warning: Error cleaning up empty folders: {ex.Message}");
                }
                
                // Try to clean up Unity's Temp/Export Package directory
                // This helps prevent "Access is denied" errors on re-import
                try
                {
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string tempExportPath = Path.Combine(projectPath, "Temp", "Export Package");
                    if (Directory.Exists(tempExportPath))
                    {
                        // Try to delete the directory, continue if it's locked
                        try
                        {
                            // Delete files individually first, then directory
                            var files = Directory.GetFiles(tempExportPath, "*", SearchOption.AllDirectories);
                            foreach (var file in files)
                            {
                                try
                                {
                                    File.SetAttributes(file, FileAttributes.Normal);
                                    File.Delete(file);
                                }
                                catch { }
                            }
                            
                            Directory.Delete(tempExportPath, true);
                            WriteLog($"  Cleaned up Unity Temp/Export Package directory");
                        }
                        catch (Exception ex)
                        {
                            WriteLog($"  Could not delete Temp/Export Package (may be locked by Unity): {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"  Warning: Could not access Temp/Export Package: {ex.Message}");
                }
                
                AssetDatabase.Refresh();
                
                WriteLog($"Cleanup complete: deleted {deletedCount} file(s) from Packages/com.yucp.temp");
            }
            catch (Exception ex)
            {
                WriteLog($"ERROR in CleanupImportedTempFiles: {ex.Message}\n{ex.StackTrace}");
                Debug.LogWarning($"[YUCP PatchImporter] Error cleaning up temp files: {ex.Message}");
            }
        }
        
        private static void CleanupDerivedFbxImporterSettings()
        {
            try
            {
                if (createdDerivedFbxPaths == null || createdDerivedFbxPaths.Count == 0)
                {
                    WriteLog("No derived FBX files to clean up importer settings for");
                    return;
                }
                
                WriteLog($"Cleaning up ModelImporter settings for {createdDerivedFbxPaths.Count} derived FBX file(s)...");
                
                int cleanedCount = 0;
                var pathsToClean = new List<string>(createdDerivedFbxPaths);
                
                foreach (var fbxPath in pathsToClean)
                {
                    try
                    {
                        if (!fbxPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        
                        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                        if (importer == null)
                        {
                            WriteLog($"  Skipping {fbxPath}: Not a ModelImporter");
                            continue;
                        }
                        
                        // Check if userData contains patching information
                        if (string.IsNullOrEmpty(importer.userData))
                        {
                            WriteLog($"  Skipping {fbxPath}: No userData");
                            continue;
                        }
                        
                        try
                        {
                            // Try to parse as DerivedSettings
                            var settings = JsonUtility.FromJson<DerivedSettings>(importer.userData);
                            if (settings != null && settings.isDerived)
                            {
                                // Clear the patching information
                                importer.userData = "";
                                EditorUtility.SetDirty(importer);
                                importer.SaveAndReimport();
                                cleanedCount++;
                                WriteLog($"  Cleaned patching settings from: {fbxPath}");
                            }
                        }
                        catch
                        {
                            // If parsing fails, check if it contains patching-related strings
                            string userDataLower = importer.userData.ToLowerInvariant();
                            if (userDataLower.Contains("isderived") || userDataLower.Contains("derived") || userDataLower.Contains("patch"))
                            {
                                // Clear userData if it contains patching-related content
                                importer.userData = "";
                                EditorUtility.SetDirty(importer);
                                importer.SaveAndReimport();
                                cleanedCount++;
                                WriteLog($"  Cleaned patching settings from: {fbxPath} (pattern match)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"  Warning: Failed to clean importer settings for {fbxPath}: {ex.Message}");
                    }
                }
                
                // Clear the tracked paths after cleaning
                createdDerivedFbxPaths.Clear();
                
                if (cleanedCount > 0)
                {
                    AssetDatabase.Refresh();
                    WriteLog($"Cleaned ModelImporter settings for {cleanedCount} derived FBX file(s)");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"ERROR in CleanupDerivedFbxImporterSettings: {ex.Message}\n{ex.StackTrace}");
                Debug.LogWarning($"[YUCP PatchImporter] Error cleaning derived FBX importer settings: {ex.Message}");
            }
        }
        
        private static void CheckAndHandleOverrideOriginalReferences(string baseFbxPath, string baseFbxGuid, string newFbxPath, string patchPath)
        {
            try
            {
                // Check if override is enabled in the patch asset
                bool overrideEnabled = false;
                
                try
                {
                    var patchAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(patchPath);
                    if (patchAsset != null)
                    {
                        var overrideField = patchAsset.GetType().GetField("overrideOriginalReferences");
                        if (overrideField != null)
                        {
                            overrideEnabled = (bool)overrideField.GetValue(patchAsset);
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"  Warning: Could not read overrideOriginalReferences from patch: {ex.Message}");
                }
                
                if (!overrideEnabled)
                {
                    WriteLog("Override Original References is not enabled, skipping GUID swap");
                    return;
                }
                
                WriteLog("Override Original References is enabled, checking for confirmation...");
                
                // Get the new FBX GUID
                string newFbxGuid = AssetDatabase.AssetPathToGUID(newFbxPath);
                if (string.IsNullOrEmpty(newFbxGuid))
                {
                    WriteLog("  ERROR: Could not get GUID for new FBX");
                    return;
                }
                
                if (string.IsNullOrEmpty(baseFbxGuid))
                {
                    WriteLog("  ERROR: baseFbxGuid is empty, cannot override references");
                    return;
                }
                
                // Show confirmation dialog
                string baseFbxName = Path.GetFileNameWithoutExtension(baseFbxPath);
                bool confirmed = EditorUtility.DisplayDialog(
                    "Override Original References",
                    $"This will replace all references to the original FBX \"{baseFbxName}\" with the new derived FBX.\n\n" +
                    $"Original FBX GUID: {baseFbxGuid}\n" +
                    $"New FBX GUID: {newFbxGuid}\n\n" +
                    "This operation can be reversed via Tools->YUCP->Revert GUID Override.\n\n" +
                    "Do you want to proceed?",
                    "Yes, Override References",
                    "Cancel"
                );
                
                if (!confirmed)
                {
                    WriteLog("User cancelled override operation");
                    return;
                }
                
                WriteLog($"User confirmed override. Swapping GUIDs: {baseFbxGuid} -> {newFbxGuid}");
                
                // Perform GUID swap
                int swappedCount = SwapGuidReferences(baseFbxGuid, newFbxGuid, baseFbxPath);
                
                if (swappedCount > 0)
                {
                    // Store swap info for reversal
                    StoreGuidSwapInfo(baseFbxGuid, newFbxGuid, baseFbxPath, newFbxPath);
                    
                    AssetDatabase.Refresh();
                    WriteLog($"Successfully swapped GUIDs in {swappedCount} file(s)");
                    Debug.Log($"[YUCP PatchImporter] Overrode references: {swappedCount} file(s) now reference the new FBX instead of the original");
                }
                else
                {
                    WriteLog("No files found with references to swap");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"ERROR in CheckAndHandleOverrideOriginalReferences: {ex.Message}\n{ex.StackTrace}");
                Debug.LogError($"[YUCP PatchImporter] Error handling override original references: {ex.Message}");
            }
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
        
        private static int SwapGuidReferences(string oldGuid, string newGuid, string oldFbxPath)
        {
            int swappedCount = 0;
            try
            {
                WriteLog($"Swapping GUID references: {oldGuid} -> {newGuid}");
                
                string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                
                // Get the new FBX path to exclude it
                string newFbxPath = AssetDatabase.GUIDToAssetPath(newGuid);
                string newFbxPhysicalPath = string.IsNullOrEmpty(newFbxPath) ? null : 
                    Path.Combine(projectPath, newFbxPath.Replace('/', Path.DirectorySeparatorChar));
                
                // Find all .meta files in Assets and Packages (skip Library, Temp, etc.)
                string[] searchPaths = new string[]
                {
                    Path.Combine(projectPath, "Assets"),
                    Path.Combine(projectPath, "Packages")
                };
                
                foreach (var searchPath in searchPaths)
                {
                    if (!Directory.Exists(searchPath))
                        continue;
                    
                    string[] allMetaFiles = Directory.GetFiles(searchPath, "*.meta", SearchOption.AllDirectories);
                    
                    foreach (var metaFile in allMetaFiles)
                    {
                        try
                        {
                            // Skip the new FBX's meta file itself
                            if (!string.IsNullOrEmpty(newFbxPhysicalPath) && 
                                metaFile.Equals(newFbxPhysicalPath + ".meta", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            
                            // Skip the old FBX's meta file
                            string assetPath = metaFile.Substring(0, metaFile.Length - 5);
                            string relativePath = assetPath.Replace(projectPath, "").Replace('\\', '/').TrimStart('/');
                            string assetGuid = AssetDatabase.AssetPathToGUID(relativePath);
                            
                            if (assetGuid == oldGuid)
                            {
                                continue;
                            }
                            
                            string content = File.ReadAllText(metaFile);
                            bool wasModified = false;
                            
                            string oldGuidPattern = @"\b" + oldGuid + @"\b";
                            if (System.Text.RegularExpressions.Regex.IsMatch(content, oldGuidPattern))
                            {
                                content = System.Text.RegularExpressions.Regex.Replace(content, oldGuidPattern, newGuid);
                                wasModified = true;
                            }
                            
                            if (wasModified)
                            {
                                File.WriteAllText(metaFile, content);
                                swappedCount++;
                                WriteLog($"  Swapped GUID in: {relativePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteLog($"  Warning: Could not process {metaFile}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"ERROR in SwapGuidReferences: {ex.Message}\n{ex.StackTrace}");
            }
            
            return swappedCount;
        }
        
        private static void StoreGuidSwapInfo(string oldGuid, string newGuid, string oldPath, string newPath)
        {
            try
            {
                string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string swapInfoPath = Path.Combine(projectPath, "Library", "YUCP", "guid_swaps.json");
                
                Directory.CreateDirectory(Path.GetDirectoryName(swapInfoPath));
                
                var swapInfo = new GuidSwapInfo
                {
                    oldGuid = oldGuid,
                    newGuid = newGuid,
                    oldPath = oldPath,
                    newPath = newPath,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                
                List<GuidSwapInfo> swaps = new List<GuidSwapInfo>();
                
                // Load existing swaps
                if (File.Exists(swapInfoPath))
                {
                    try
                    {
                        string existingJson = File.ReadAllText(swapInfoPath);
                        var existingSwaps = JsonUtility.FromJson<GuidSwapList>(existingJson);
                        if (existingSwaps != null && existingSwaps.swaps != null)
                        {
                            swaps = existingSwaps.swaps;
                        }
                    }
                    catch { }
                }
                
                swaps.RemoveAll(s => s.oldGuid == oldGuid);
                
                // Add new swap
                swaps.Add(swapInfo);
                
                // Save
                var swapList = new GuidSwapList { swaps = swaps };
                string json = JsonUtility.ToJson(swapList, true);
                File.WriteAllText(swapInfoPath, json);
                
                WriteLog($"Stored GUID swap info for reversal");
            }
            catch (Exception ex)
            {
                WriteLog($"ERROR in StoreGuidSwapInfo: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        [Serializable]
        private class GuidSwapInfo
        {
            public string oldGuid;
            public string newGuid;
            public string oldPath;
            public string newPath;
            public string timestamp;
        }
        
        [Serializable]
        private class GuidSwapList
        {
            public List<GuidSwapInfo> swaps = new List<GuidSwapInfo>();
        }
        
        [MenuItem("Tools/YUCP/Others/Utilities/Revert GUID Override")]
        public static void RevertGuidOverride()
        {
            try
            {
                string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string swapInfoPath = Path.Combine(projectPath, "Library", "YUCP", "guid_swaps.json");
                
                if (!File.Exists(swapInfoPath))
                {
                    EditorUtility.DisplayDialog("No GUID Swaps", "No GUID override operations found to revert.", "OK");
                    return;
                }
                
                string json = File.ReadAllText(swapInfoPath);
                var swapList = JsonUtility.FromJson<GuidSwapList>(json);
                
                if (swapList == null || swapList.swaps == null || swapList.swaps.Count == 0)
                {
                    EditorUtility.DisplayDialog("No GUID Swaps", "No GUID override operations found to revert.", "OK");
                    return;
                }
                
                // Build list of swaps for display
                string swapListText = string.Join("\n", swapList.swaps.Select(s => 
                    $"  • {Path.GetFileName(s.oldPath)} -> {Path.GetFileName(s.newPath)} ({s.timestamp})"
                ));
                
                // Confirm reversal
                bool confirmed = EditorUtility.DisplayDialog(
                    "Revert GUID Override",
                    $"Found {swapList.swaps.Count} GUID override operation(s):\n\n{swapListText}\n\n" +
                    "This will restore all references to the original FBX.\n\n" +
                    "Do you want to revert all of them?",
                    "Yes, Revert All",
                    "Cancel"
                );
                
                if (!confirmed)
                {
                    return;
                }
                
                int revertedCount = 0;
                foreach (var swap in swapList.swaps)
                {
                    WriteLog($"Reverting GUID swap: {swap.newGuid} -> {swap.oldGuid}");
                    int count = SwapGuidReferences(swap.newGuid, swap.oldGuid, swap.newPath);
                    revertedCount += count;
                }
                
                // Delete swap info file after reverting all
                File.Delete(swapInfoPath);
                
                AssetDatabase.Refresh();
                
                EditorUtility.DisplayDialog(
                    "Reversion Complete",
                    $"Successfully reverted {revertedCount} GUID reference(s) from {swapList.swaps.Count} operation(s).",
                    "OK"
                );
                
                WriteLog($"Reverted {revertedCount} GUID reference(s)");
                Debug.Log($"[YUCP PatchImporter] Reverted {revertedCount} GUID reference(s)");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to revert GUID override: {ex.Message}", "OK");
                WriteLog($"ERROR in RevertGuidOverride: {ex.Message}\n{ex.StackTrace}");
                Debug.LogError($"[YUCP PatchImporter] Error reverting GUID override: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Applies a kitbash (multi-source) patch.
        /// Builds a synthetic base from the recipe, then applies the hdiff.
        /// </summary>
        private static void ApplyKitbashPatch(UnityEngine.Object patchObj, string patchPath)
        {
            try
            {
                WriteLog($"ApplyKitbashPatch called for: {patchPath}");
                
                var patchType = patchObj.GetType();
                
                // Get kitbash-specific fields
                var kitbashRecipeJsonField = patchType.GetField("kitbashRecipeJson");
                var recipeHashField = patchType.GetField("recipeHash");
                var requiredSourceGuidsField = patchType.GetField("requiredSourceGuids");
                var hdiffFilePathField = patchType.GetField("hdiffFilePath");
                var derivedFbxGuidField = patchType.GetField("derivedFbxGuid");
                var originalDerivedFbxPathField = patchType.GetField("originalDerivedFbxPath");
                var embeddedMetaField = patchType.GetField("embeddedMetaFileContent");
                var uiHintsField = patchType.GetField("uiHints");
                
                string kitbashRecipeJson = kitbashRecipeJsonField?.GetValue(patchObj) as string;
                string recipeHash = recipeHashField?.GetValue(patchObj) as string;
                string[] requiredSourceGuids = requiredSourceGuidsField?.GetValue(patchObj) as string[];
                string hdiffFilePath = hdiffFilePathField?.GetValue(patchObj) as string;
                string derivedFbxGuid = derivedFbxGuidField?.GetValue(patchObj) as string;
                string originalDerivedFbxPath = originalDerivedFbxPathField?.GetValue(patchObj) as string;
                string embeddedMetaContent = embeddedMetaField?.GetValue(patchObj) as string;
                
                // Get friendly name from UIHints
                string friendlyName = Path.GetFileNameWithoutExtension(patchPath);
                if (uiHintsField != null)
                {
                    var uiHints = uiHintsField.GetValue(patchObj);
                    if (uiHints != null)
                    {
                        var friendlyNameField = uiHints.GetType().GetField("friendlyName");
                        if (friendlyNameField != null)
                        {
                            var fn = friendlyNameField.GetValue(uiHints) as string;
                            if (!string.IsNullOrEmpty(fn))
                                friendlyName = fn;
                        }
                    }
                }
                
                // Validate required data
                if (string.IsNullOrEmpty(kitbashRecipeJson))
                {
                    WriteLog("  ERROR: kitbashRecipeJson is missing from patch");
                    Debug.LogError($"[YUCP PatchImporter] Kitbash patch missing recipe JSON: {patchPath}");
                    return;
                }
                
                if (requiredSourceGuids == null || requiredSourceGuids.Length == 0)
                {
                    WriteLog("  ERROR: requiredSourceGuids is missing from patch");
                    Debug.LogError($"[YUCP PatchImporter] Kitbash patch missing source GUIDs: {patchPath}");
                    return;
                }
                
                // Verify all source FBXs are present
                WriteLog($"  Verifying {requiredSourceGuids.Length} source FBX(s)...");
                List<string> missingGuids = new List<string>();
                foreach (var guid in requiredSourceGuids)
                {
                    string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(sourcePath))
                    {
                        missingGuids.Add(guid);
                        WriteLog($"    Missing source: {guid}");
                    }
                    else
                    {
                        WriteLog($"    Found source: {guid} -> {sourcePath}");
                    }
                }
                
                if (missingGuids.Count > 0)
                {
                    string missingList = string.Join(", ", missingGuids);
                    Debug.LogError($"[YUCP PatchImporter] Kitbash patch requires missing source FBXs: {missingList}.\n" +
                        "Please ensure all required source FBXs are present in the project.");
                    WriteLog($"  ERROR: Missing {missingGuids.Count} source FBX(s)");
                    return;
                }
                
                // Build synthetic base from recipe
                WriteLog($"  Building synthetic base from recipe (hash: {recipeHash})...");
                string syntheticBasePath = BuildSyntheticBaseFromRecipe(kitbashRecipeJson, recipeHash);
                
                if (string.IsNullOrEmpty(syntheticBasePath))
                {
                    Debug.LogError($"[YUCP PatchImporter] Failed to build synthetic base for kitbash: {patchPath}");
                    WriteLog("  ERROR: BuildSyntheticBaseFromRecipe returned null");
                    return;
                }
                
                WriteLog($"  Synthetic base built at: {syntheticBasePath}");
                
                // Now apply the patch using the synthetic base - same flow as single-base
                // The synthetic base acts as the "base FBX" for patching
                WriteLog($"  Applying patch with synthetic base...");
                
                // Since synthetic base is in Library (not Assets), we need to handle it differently
                // The patch contains the hdiff that, when applied to the synthetic base, produces the derived FBX
                // Use the same ApplyPatchWithBasePath but with the synthetic base
                ApplyPatchWithBasePath(patchObj, patchPath, syntheticBasePath, 
                    null, // No base GUID for synthetic base (it's generated)
                    derivedFbxGuid,
                    friendlyName,
                    originalDerivedFbxPath);
                
                WriteLog($"  Kitbash patch applied successfully");
                Debug.Log($"[YUCP PatchImporter] Kitbash patch applied: {patchPath}");
                
                CleanupDllsAfterPatchApplication();
            }
            catch (Exception ex)
            {
                WriteLog($"  ERROR in ApplyKitbashPatch: {ex.Message}\n{ex.StackTrace}");
                Debug.LogError($"[YUCP PatchImporter] Error applying kitbash patch {patchPath}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Builds a synthetic base FBX from a recipe JSON.
        /// Uses caching by recipe hash.
        /// </summary>
        private static string BuildSyntheticBaseFromRecipe(string recipeJson, string recipeHash)
        {
            try
            {
                // Check cache first
                string cacheDir = Path.Combine(Application.dataPath, "..", "Library", "YUCP", "KitbashCache");
                Directory.CreateDirectory(cacheDir);
                
                string cachedPath = Path.Combine(cacheDir, $"{recipeHash}.fbx");
                if (File.Exists(cachedPath))
                {
                    WriteLog($"    Using cached synthetic base: {cachedPath}");
                    return cachedPath;
                }
                
                // Check if FBX Exporter is available
                var exporterType = System.Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");
                if (exporterType == null)
                {
                    Debug.LogError("[YUCP PatchImporter] Unity FBX Exporter package (com.unity.formats.fbx) is required for kitbash mode but not installed.\n" +
                        "Please install it via Package Manager: Window > Package Manager > + > Add package by name > com.unity.formats.fbx");
                    return null;
                }
                
                // Get ExportObject method
                var exportMethod = exporterType.GetMethod("ExportObject", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, new Type[] { typeof(string), typeof(UnityEngine.Object) }, null);
                
                if (exportMethod == null)
                {
                    Debug.LogError("[YUCP PatchImporter] FBX Exporter found but ExportObject method not accessible");
                    return null;
                }
                
                // Parse recipe JSON to get parts
                var parts = ParseRecipeJsonParts(recipeJson);
                if (parts == null || parts.Count == 0)
                {
                    WriteLog("    WARNING: Recipe has no valid parts");
                    return null;
                }
                
                WriteLog($"    Recipe has {parts.Count} parts");
                
                // Assemble GameObjects from parts
                GameObject root = new GameObject("SyntheticBase");
                try
                {
                    foreach (var part in parts)
                    {
                        if (string.IsNullOrEmpty(part.sourceFbxGuid))
                        {
                            WriteLog($"      Part '{part.displayName}' has no source GUID, skipping");
                            continue;
                        }
                        
                        string sourcePath = AssetDatabase.GUIDToAssetPath(part.sourceFbxGuid);
                        if (string.IsNullOrEmpty(sourcePath))
                        {
                            WriteLog($"      Part '{part.displayName}' source not found: {part.sourceFbxGuid}");
                            continue;
                        }
                        
                        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                        if (sourcePrefab == null)
                        {
                            WriteLog($"      Failed to load source: {sourcePath}");
                            continue;
                        }
                        
                        // Find specific mesh path if specified
                        Transform sourceTransform = sourcePrefab.transform;
                        if (!string.IsNullOrEmpty(part.meshPath))
                        {
                            var found = sourcePrefab.transform.Find(part.meshPath);
                            if (found != null)
                                sourceTransform = found;
                        }
                        
                        // Instantiate the part
                        GameObject partInstance = UnityEngine.Object.Instantiate(sourceTransform.gameObject);
                        partInstance.name = !string.IsNullOrEmpty(part.displayName) ? part.displayName : sourceTransform.name;
                        partInstance.transform.SetParent(root.transform);
                        partInstance.transform.localPosition = part.positionOffset;
                        partInstance.transform.localRotation = part.rotationOffset;
                        partInstance.transform.localScale = Vector3.Scale(Vector3.one, part.scaleMultiplier);
                        
                        WriteLog($"      Added part: {partInstance.name} from {sourcePath}");
                    }
                    
                    // Export to FBX
                    object result = exportMethod.Invoke(null, new object[] { cachedPath, root });
                    if (result == null || !File.Exists(cachedPath))
                    {
                        WriteLog("    ERROR: FBX export failed");
                        return null;
                    }
                    
                    WriteLog($"    Synthetic base exported to: {cachedPath}");
                    return cachedPath;
                }
                finally
                {
                    // Cleanup
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"    ERROR in BuildSyntheticBaseFromRecipe: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }
        
        /// <summary>
        /// Simple part data for JSON parsing.
        /// </summary>
        private class KitbashPartData
        {
            public string sourceFbxGuid;
            public string displayName;
            public string meshPath;
            public Vector3 positionOffset;
            public Quaternion rotationOffset = Quaternion.identity;
            public Vector3 scaleMultiplier = Vector3.one;
        }
        
        /// <summary>
        /// Parses the parts array from recipe JSON.
        /// </summary>
        private static List<KitbashPartData> ParseRecipeJsonParts(string recipeJson)
        {
            var parts = new List<KitbashPartData>();
            
            try
            {
                // Manual JSON parsing for parts array
                // Format: {"parts":[{"sourceFbxGuid":"...","displayName":"...","meshPath":"...",...},...]}
                
                int partsStart = recipeJson.IndexOf("\"parts\"");
                if (partsStart < 0) return parts;
                
                int arrayStart = recipeJson.IndexOf('[', partsStart);
                if (arrayStart < 0) return parts;
                
                int arrayEnd = FindMatchingBracket(recipeJson, arrayStart, '[', ']');
                if (arrayEnd < 0) return parts;
                
                string partsArrayJson = recipeJson.Substring(arrayStart, arrayEnd - arrayStart + 1);
                
                // Find each object in the array
                int depth = 0;
                int objStart = -1;
                for (int i = 0; i < partsArrayJson.Length; i++)
                {
                    char c = partsArrayJson[i];
                    if (c == '{')
                    {
                        if (depth == 0) objStart = i;
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0 && objStart >= 0)
                        {
                            string objJson = partsArrayJson.Substring(objStart, i - objStart + 1);
                            var part = ParsePartObject(objJson);
                            if (part != null) parts.Add(part);
                            objStart = -1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"      ERROR parsing recipe JSON: {ex.Message}");
            }
            
            return parts;
        }
        
        /// <summary>
        /// Parses a single part object from JSON.
        /// </summary>
        private static KitbashPartData ParsePartObject(string json)
        {
            var part = new KitbashPartData();
            
            part.sourceFbxGuid = ExtractJsonString(json, "sourceFbxGuid");
            part.displayName = ExtractJsonString(json, "displayName");
            part.meshPath = ExtractJsonString(json, "meshPath");
            
            // Parse vectors (simplified - assumes default values if not found)
            // positionOffset, rotationOffset as quaternion, scaleMultiplier
            // For now, use defaults
            part.positionOffset = Vector3.zero;
            part.rotationOffset = Quaternion.identity;
            part.scaleMultiplier = Vector3.one;
            
            return !string.IsNullOrEmpty(part.sourceFbxGuid) ? part : null;
        }
        
        /// <summary>
        /// Extracts a string value from JSON.
        /// </summary>
        private static string ExtractJsonString(string json, string key)
        {
            string searchKey = $"\"{key}\"";
            int keyIndex = json.IndexOf(searchKey);
            if (keyIndex < 0) return null;
            
            int colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return null;
            
            // Skip whitespace
            int valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;
            
            if (valueStart >= json.Length || json[valueStart] != '"') return null;
            
            // Find end quote
            int valueEnd = json.IndexOf('"', valueStart + 1);
            if (valueEnd < 0) return null;
            
            return json.Substring(valueStart + 1, valueEnd - valueStart - 1);
        }
        
        /// <summary>
        /// Finds the matching closing bracket.
        /// </summary>
        private static int FindMatchingBracket(string s, int start, char open, char close)
        {
            int depth = 0;
            for (int i = start; i < s.Length; i++)
            {
                if (s[i] == open) depth++;
                else if (s[i] == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}

