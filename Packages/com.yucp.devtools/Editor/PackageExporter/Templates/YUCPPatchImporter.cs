using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

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
                Debug.Log($"[YUCP PatchImporter] {message}");
            }
            catch { }
        }
        
        static YUCPPatchImporter()
        {
            WriteLog("YUCPPatchImporter static constructor called - importer is loaded and compiled!");
            EditorApplication.delayCall += CheckForPatchesOnLoad;
        }
        
        private static HashSet<string> processedPatches = new HashSet<string>();
        private static bool hasCheckedOnLoad = false;
        
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
                                // Don't clear the EditorPrefs yet - let it be cleared on successful application
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
            
            int patchCount = 0;
            foreach (var path in importedAssets ?? new string[0])
            {
                // Skip derived assets - they're outputs, not inputs
                if (path.Contains("/Derived/") || path.Contains("\\Derived\\"))
                {
                    continue;
                }
                
                // Only process DerivedFbxAsset files in Patches folder
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
                    TryApplyPatch(path);
                }
            }
            
            WriteLog($"Processed {patchCount} patch(es) from {importedAssets?.Length ?? 0} imported assets");
        }
        
        private static void TryApplyPatch(string patchPath)
        {
            WriteLog($"TryApplyPatch called for: {patchPath}");
            try
            {
                // Find DerivedFbxAsset type
                var derivedFbxAssetType = System.Type.GetType("YUCP.PatchRuntime.DerivedFbxAsset, YUCP.PatchRuntime");
                if (derivedFbxAssetType == null)
                {
                    var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
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
                
                if (derivedFbxAssetType == null)
                {
                    WriteLog("  ERROR: DerivedFbxAsset type not found");
                    Debug.LogWarning($"[YUCP PatchImporter] DerivedFbxAsset type not found. Ensure patch scripts are in temp package.");
                    return;
                }
                
                // Fix script GUID and namespace references if needed
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
                            // Replace all occurrences of the old namespace
                            assetContent = System.Text.RegularExpressions.Regex.Replace(
                                assetContent,
                                @"YUCP\.DevTools\.Editor\.PackageExporter\.DerivedFbxAsset/(\w+)",
                                "YUCP.PatchRuntime.DerivedFbxAsset/$1"
                            );
                            // Also replace any other references to the old namespace
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
                        
                        // Also check for any remaining old namespace patterns
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
                        }
                    }
                }
                
                // Force import
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
                var patchType = patchObj.GetType();
                
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
                
                string baseFbxPath = AssetDatabase.GUIDToAssetPath(baseFbxGuid);
                if (string.IsNullOrEmpty(baseFbxPath) || !baseFbxPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    WriteLog($"  ERROR: baseFbxGuid {baseFbxGuid} does not resolve to a valid FBX");
                    Debug.LogWarning($"[YUCP PatchImporter] baseFbxGuid {baseFbxGuid} does not resolve to a valid FBX");
                    return;
                }
                
                WriteLog($"  Using direct targeting: baseFbxGuid={baseFbxGuid} -> {baseFbxPath}");
                
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
                    
                    // Force a refresh to ensure Unity recognizes the GUID change
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                WriteLog($"  ERROR in ApplyPatch: {ex.Message}\n{ex.StackTrace}");
                Debug.LogError($"[YUCP PatchImporter] Error applying patch: {ex.Message}");
            }
        }
    }
}

