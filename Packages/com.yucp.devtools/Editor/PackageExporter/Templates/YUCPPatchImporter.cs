using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace YUCP.PatchCleanup
{
    /// <summary>
    /// Standalone patch importer that applies patches on import.
    /// This script is included in the temp package and handles patch application.
    /// All required code (ManifestBuilder, MapBuilder, Applicator, etc.) is in the temp package.
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
            // Schedule a delayed check for patches that might have been imported before scripts compiled
            EditorApplication.delayCall += CheckForPatchesOnLoad;
        }
        
        private static HashSet<string> processedPatches = new HashSet<string>();
        private static bool hasCheckedOnLoad = false;
        
        private static void CheckForPatchesOnLoad()
        {
            if (hasCheckedOnLoad) return;
            hasCheckedOnLoad = true;
            
            WriteLog("CheckForPatchesOnLoad called - checking for existing patches");
            
            // Check if there are any PatchPackage assets already in the project
            // This handles the case where patches were imported before scripts compiled
            try
            {
                // Search for all .asset files in the Patches folder
                string patchesFolder = "Packages/com.yucp.temp/Patches";
                if (!AssetDatabase.IsValidFolder(patchesFolder))
                {
                    WriteLog($"Patches folder does not exist: {patchesFolder}");
                    return;
                }
                
                // Find all assets in the Patches folder
                string[] allGuids = AssetDatabase.FindAssets("", new[] { patchesFolder });
                WriteLog($"Found {allGuids.Length} asset(s) in Patches folder");
                
                int appliedCount = 0;
                foreach (var guid in allGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    
                    // Look for PatchPackage assets specifically
                    if (path.Contains("PatchPackage") && path.EndsWith(".asset") && path.Contains("com.yucp.temp"))
                    {
                        WriteLog($"Found existing patch: {path} - attempting to apply");
                        
                        // Skip if already processed
                        if (processedPatches != null && processedPatches.Contains(path))
                        {
                            WriteLog($"  Skipping (already processed): {path}");
                            continue;
                        }
                        
                        // Mark as processed before applying
                        if (processedPatches == null)
                            processedPatches = new HashSet<string>();
                        processedPatches.Add(path);
                        
                        // Apply the patch
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
            
            // Skip if we're in the middle of an export (prevents reserialization loops)
            try
            {
                var packageBuilderType = System.Type.GetType("YUCP.DevTools.Editor.PackageExporter.PackageBuilder, Assembly-CSharp-Editor");
                if (packageBuilderType != null)
                {
                    WriteLog("PackageBuilder type found (devtools present)");
                    var isExportingField = packageBuilderType.GetField("s_isExporting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (isExportingField != null && (bool)isExportingField.GetValue(null))
                    {
                        WriteLog("Export in progress, skipping patch detection");
                        return; // Skip patch application during export
                    }
                }
                else
                {
                    WriteLog("PackageBuilder type NOT found (standalone mode - good)");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Error checking export flag: {ex.Message}");
            }
            
            // Look for PatchPackage imports - IGNORE assets in Derived folder (they're outputs, not inputs)
            // CRITICAL: Only process patches that are ACTUALLY being imported right now
            // Track processed patches to prevent re-processing
            if (processedPatches == null)
                processedPatches = new HashSet<string>();
            
            int patchCount = 0;
            foreach (var path in importedAssets ?? new string[0])
            {
                WriteLog($"Checking imported asset: {path}");
                
                // Skip derived assets - they're outputs from patch application, not patch packages
                if (path.Contains("/Derived/") || path.Contains("\\Derived\\"))
                {
                    WriteLog($"  Skipping (Derived folder): {path}");
                    continue;
                }
                    
                // Skip anything not in Patches folder - patches should only be in Patches folder
                if (!path.Contains("/Patches/") && !path.Contains("\\Patches\\"))
                {
                    WriteLog($"  Skipping (not in Patches folder): {path}");
                    continue;
                }
                
                // Skip if we've already processed this patch
                if (processedPatches.Contains(path))
                {
                    WriteLog($"  Skipping (already processed): {path}");
                    continue;
                }
                
                if (path.Contains("PatchPackage") && path.EndsWith(".asset") && path.Contains("com.yucp.temp"))
                {
                    patchCount++;
                    WriteLog($"  FOUND PATCH: {path}");
                    processedPatches.Add(path); // Mark as processed BEFORE applying
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
                // Try multiple type names - the namespace might be different
                var patchType = System.Type.GetType("YUCP.PatchRuntime.PatchPackage, Assembly-CSharp-Editor");
                if (patchType == null)
                {
                    WriteLog("  PatchPackage type not found with YUCP.PatchRuntime namespace, trying alternatives...");
                    // Try without namespace
                    patchType = System.Type.GetType("PatchPackage, Assembly-CSharp-Editor");
                }
                if (patchType == null)
                {
                    // Try finding by searching all types
                    var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var asm in assemblies)
                    {
                        try
                        {
                            patchType = asm.GetType("YUCP.PatchRuntime.PatchPackage");
                            if (patchType != null)
                            {
                                WriteLog($"  Found PatchPackage type in assembly: {asm.FullName}");
                                break;
                            }
                        }
                        catch { }
                    }
                }
                
                if (patchType == null)
                {
                    WriteLog("  ERROR: PatchPackage type not found in any assembly. Listing available types...");
                    var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var asm in assemblies)
                    {
                        try
                        {
                            var types = asm.GetTypes().Where(t => t.Name.Contains("PatchPackage")).ToArray();
                            if (types.Length > 0)
                            {
                                WriteLog($"  Found PatchPackage-related types in {asm.FullName}: {string.Join(", ", types.Select(t => t.FullName))}");
                            }
                        }
                        catch { }
                    }
                    Debug.LogWarning($"[YUCP PatchImporter] PatchPackage type not found. Ensure patch scripts are in temp package.");
                    return;
                }
                
                WriteLog($"  PatchPackage type found: {patchType.FullName}");
                
                // Try to fix the script GUID reference in the .asset file if it's wrong
                string patchPackageScriptPath = "Packages/com.yucp.temp/Editor/PatchPackage.cs";
                string patchPackageScriptGuid = AssetDatabase.AssetPathToGUID(patchPackageScriptPath);
                
                if (!string.IsNullOrEmpty(patchPackageScriptGuid))
                {
                    WriteLog($"  PatchPackage script GUID: {patchPackageScriptGuid}");
                    
                    // Read and fix the .asset file if needed
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string physicalAssetPath = Path.Combine(projectPath, patchPath.Replace('/', Path.DirectorySeparatorChar));
                    
                    if (File.Exists(physicalAssetPath))
                    {
                        string assetContent = File.ReadAllText(physicalAssetPath);
                        bool needsUpdate = false;
                        
                        // Fix script GUID reference
                        var guidPattern = new System.Text.RegularExpressions.Regex(@"m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([a-f0-9]{32}),\s*type:\s*\d+\}");
                        var match = guidPattern.Match(assetContent);
                        
                        if (match.Success)
                        {
                            string currentGuid = match.Groups[1].Value;
                            if (currentGuid != patchPackageScriptGuid)
                            {
                                WriteLog($"  Fixing script GUID reference: {currentGuid} -> {patchPackageScriptGuid}");
                                assetContent = guidPattern.Replace(assetContent, $"m_Script: {{fileID: 11500000, guid: {patchPackageScriptGuid}, type: 3}}");
                                needsUpdate = true;
                            }
                        }
                        
                        // Fix namespace references for nested types (e.g., BlendshapeOp, MeshDeltaOp)
                        if (System.Text.RegularExpressions.Regex.IsMatch(assetContent, @"YUCP\.DevTools\.Editor\.PackageExporter\.PatchPackage/"))
                        {
                            WriteLog($"  Fixing namespace references for nested types...");
                            assetContent = System.Text.RegularExpressions.Regex.Replace(
                                assetContent,
                                @"YUCP\.DevTools\.Editor\.PackageExporter\.PatchPackage/(\w+)",
                                "YUCP.PatchRuntime.PatchPackage/$1"
                            );
                            needsUpdate = true;
                        }
                        
                        // Fix assembly references
                        if (System.Text.RegularExpressions.Regex.IsMatch(assetContent, @"com\.yucp\.devtools\.Editor"))
                        {
                            WriteLog($"  Fixing assembly references...");
                            assetContent = System.Text.RegularExpressions.Regex.Replace(
                                assetContent,
                                @"com\.yucp\.devtools\.Editor",
                                "YUCP.PatchRuntime"
                            );
                            needsUpdate = true;
                        }
                        
                        if (needsUpdate)
                        {
                            File.WriteAllText(physicalAssetPath, assetContent);
                            
                            // Force reimport after fixing
                            AssetDatabase.ImportAsset(patchPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                            WriteLog($"  Reimported asset after fixing references");
                        }
                    }
                }
                
                // Force import the asset first - Unity may not have imported it yet
                WriteLog($"  Forcing import of patch asset...");
                AssetDatabase.ImportAsset(patchPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                
                // Try loading with the specific type first
                var patch = AssetDatabase.LoadAssetAtPath(patchPath, patchType);
                
                // If that fails, try loading as generic ScriptableObject and casting
                if (patch == null)
                {
                    WriteLog($"  Type-specific load failed, trying generic ScriptableObject...");
                    var genericObj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(patchPath);
                    if (genericObj != null)
                    {
                        WriteLog($"  Loaded as generic ScriptableObject, type: {genericObj.GetType().FullName}");
                        // Check if it's assignable to our patch type
                        if (patchType.IsAssignableFrom(genericObj.GetType()))
                        {
                            patch = genericObj;
                            WriteLog($"  Successfully cast to PatchPackage type");
                        }
                        else
                        {
                            WriteLog($"  Type mismatch: expected {patchType.FullName}, got {genericObj.GetType().FullName}");
                        }
                    }
                }
                
                // If still null, try loading by GUID
                if (patch == null)
                {
                    WriteLog($"  Generic load failed, trying by GUID...");
                    string guid = AssetDatabase.AssetPathToGUID(patchPath);
                    if (!string.IsNullOrEmpty(guid))
                    {
                        WriteLog($"  Found GUID: {guid}");
                        var guidObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(guid));
                        if (guidObj != null && patchType.IsAssignableFrom(guidObj.GetType()))
                        {
                            patch = guidObj;
                            WriteLog($"  Loaded via GUID");
                        }
                    }
                }
                
                if (patch == null)
                {
                    WriteLog($"  ERROR: Could not load patch asset at path: {patchPath}");
                    WriteLog($"  File exists: {File.Exists(patchPath)}");
                    WriteLog($"  AssetDatabase.Contains: {AssetDatabase.Contains(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(patchPath))}");
                    Debug.LogWarning($"[YUCP PatchImporter] Could not load patch asset: {patchPath}. The asset may be missing its script reference. Try re-importing the package.");
                    return;
                }
                
                WriteLog($"  Patch asset loaded successfully: {patch.GetType().FullName}");
                ApplyPatch(patch, patchPath);
            }
            catch (Exception ex)
            {
                WriteLog($"  ERROR in TryApplyPatch: {ex.Message}\n{ex.StackTrace}");
                Debug.LogWarning($"[YUCP PatchImporter] Error processing patch {patchPath}: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private static void ApplyPatch(UnityEngine.Object patchObj, string patchPath)
        {
            try
            {
                // Use reflection to access patch properties and methods
                var patchType = patchObj.GetType();
                var sourceManifestIdProp = patchType.GetProperty("sourceManifestId") as System.Reflection.MemberInfo ?? patchType.GetField("sourceManifestId");
                var opsProp = patchType.GetProperty("ops") as System.Reflection.MemberInfo ?? patchType.GetField("ops");
                var policyProp = patchType.GetProperty("policy") as System.Reflection.MemberInfo ?? patchType.GetField("policy");
                var uiHintsProp = patchType.GetProperty("uiHints") as System.Reflection.MemberInfo ?? patchType.GetField("uiHints");
                var seedMapsProp = patchType.GetProperty("seedMaps") as System.Reflection.MemberInfo ?? patchType.GetField("seedMaps");
                
                if (sourceManifestIdProp == null || opsProp == null || policyProp == null)
                {
                    Debug.LogWarning($"[YUCP PatchImporter] Patch missing required properties");
                    return;
                }
                
                string sourceManifestId = (GetMemberValue(sourceManifestIdProp, patchObj) ?? "").ToString();
                var ops = GetMemberValue(opsProp, patchObj) as System.Collections.IList;
                var policy = GetMemberValue(policyProp, patchObj);
                var uiHints = uiHintsProp != null ? GetMemberValue(uiHintsProp, patchObj) : null;
                var seedMaps = seedMapsProp != null ? GetMemberValue(seedMapsProp, patchObj) : null;
                
                if (ops == null || ops.Count == 0)
                {
                    Debug.Log($"[YUCP PatchImporter] Patch has no operations, skipping");
                    return;
                }
                
                // Get friendly name
                string friendlyName = Path.GetFileNameWithoutExtension(patchPath);
                if (uiHints != null)
                {
                    var friendlyNameProp = uiHints.GetType().GetProperty("friendlyName") as System.Reflection.MemberInfo ?? uiHints.GetType().GetField("friendlyName");
                    if (friendlyNameProp != null)
                    {
                        var fn = GetMemberValue(friendlyNameProp, uiHints);
                        if (fn != null && !string.IsNullOrEmpty(fn.ToString()))
                            friendlyName = fn.ToString();
                    }
                }
                
                // Get policy thresholds
                var autoApplyThresholdProp = policy.GetType().GetProperty("autoApplyThreshold") as System.Reflection.MemberInfo ?? policy.GetType().GetField("autoApplyThreshold");
                var reviewThresholdProp = policy.GetType().GetProperty("reviewThreshold") as System.Reflection.MemberInfo ?? policy.GetType().GetField("reviewThreshold");
                float autoApplyThreshold = 0.8f;
                float reviewThreshold = 0.4f;
                if (autoApplyThresholdProp != null)
                    autoApplyThreshold = Convert.ToSingle(GetMemberValue(autoApplyThresholdProp, policy));
                if (reviewThresholdProp != null)
                    reviewThreshold = Convert.ToSingle(GetMemberValue(reviewThresholdProp, policy));
                
                // Find candidate FBX models in the project
                WriteLog($"  Searching for FBX models in project...");
                var modelGuids = AssetDatabase.FindAssets("t:Model");
                var modelPaths = modelGuids.Select(AssetDatabase.GUIDToAssetPath).ToList();
                WriteLog($"  Found {modelPaths.Count} FBX model(s) in project");
                
                WriteLog($"  Looking for patch runtime types...");
                
                // Get ManifestBuilder type (using YUCP.PatchRuntime namespace from temp package)
                // Try with the correct assembly name first
                var manifestBuilderType = System.Type.GetType("YUCP.PatchRuntime.ManifestBuilder, YUCP.PatchRuntime");
                var mapBuilderType = System.Type.GetType("YUCP.PatchRuntime.MapBuilder, YUCP.PatchRuntime");
                var applicatorType = System.Type.GetType("YUCP.PatchRuntime.Applicator, YUCP.PatchRuntime");
                var backupManagerType = System.Type.GetType("YUCP.PatchRuntime.BackupManager, YUCP.PatchRuntime");
                
                // If not found, search all assemblies
                if (manifestBuilderType == null || mapBuilderType == null || applicatorType == null || backupManagerType == null)
                {
                    var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var asm in assemblies)
                    {
                        try
                        {
                            if (manifestBuilderType == null)
                                manifestBuilderType = asm.GetType("YUCP.PatchRuntime.ManifestBuilder");
                            if (mapBuilderType == null)
                                mapBuilderType = asm.GetType("YUCP.PatchRuntime.MapBuilder");
                            if (applicatorType == null)
                                applicatorType = asm.GetType("YUCP.PatchRuntime.Applicator");
                            if (backupManagerType == null)
                                backupManagerType = asm.GetType("YUCP.PatchRuntime.BackupManager");
                            
                            if (manifestBuilderType != null && mapBuilderType != null && applicatorType != null && backupManagerType != null)
                                break;
                        }
                        catch { }
                    }
                }
                
                WriteLog($"    ManifestBuilder: {(manifestBuilderType != null ? manifestBuilderType.FullName : "NOT FOUND")}");
                WriteLog($"    MapBuilder: {(mapBuilderType != null ? mapBuilderType.FullName : "NOT FOUND")}");
                WriteLog($"    Applicator: {(applicatorType != null ? applicatorType.FullName : "NOT FOUND")}");
                WriteLog($"    BackupManager: {(backupManagerType != null ? backupManagerType.FullName : "NOT FOUND")}");
                
                if (manifestBuilderType == null || mapBuilderType == null || applicatorType == null || backupManagerType == null)
                {
                    WriteLog("  ERROR: Required patch application types not found. Searching all assemblies...");
                    var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var asm in assemblies)
                    {
                        try
                        {
                            var types = asm.GetTypes().Where(t => t.Name.Contains("ManifestBuilder") || t.Name.Contains("MapBuilder") || t.Name.Contains("Applicator")).ToArray();
                            if (types.Length > 0)
                            {
                                WriteLog($"    Found in {asm.FullName}: {string.Join(", ", types.Select(t => t.FullName))}");
                            }
                        }
                        catch { }
                    }
                    Debug.LogError($"[YUCP PatchImporter] Required patch application types not found. Ensure all patch scripts are in temp package.");
                    return;
                }
                
                // Get static methods
                var buildForFbxMethod = manifestBuilderType.GetMethod("BuildForFbx", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var buildMapMethod = mapBuilderType.GetMethod("Build", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var applyToTargetMethod = applicatorType.GetMethod("ApplyToTarget", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var backupPrefabsMethod = backupManagerType.GetMethod("BackupPrefabsReferencing", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var createPatchedPrefabsMethod = backupManagerType.GetMethod("CreatePatchedPrefabCopies", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                
                if (buildForFbxMethod == null || buildMapMethod == null || applyToTargetMethod == null)
                {
                    Debug.LogError($"[YUCP PatchImporter] Required patch application methods not found.");
                    return;
                }
                
                foreach (var modelPath in modelPaths)
                {
                    try
                    {
                        WriteLog($"  Processing FBX: {modelPath}");
                        
                        // Build manifest for target
                        var targetManifest = buildForFbxMethod.Invoke(null, new object[] { modelPath });
                        if (targetManifest == null)
                        {
                            WriteLog($"    Could not build manifest, skipping");
                            continue;
                        }
                        
                        // Build correspondence map
                        var v1 = Activator.CreateInstance(manifestBuilderType.GetNestedType("Manifest"));
                        var manifestIdField = v1.GetType().GetField("manifestId");
                        if (manifestIdField != null)
                            manifestIdField.SetValue(v1, sourceManifestId);
                        var meshesField = v1.GetType().GetField("meshes");
                        if (meshesField != null)
                            meshesField.SetValue(v1, Activator.CreateInstance(typeof(List<>).MakeGenericType(manifestBuilderType.GetNestedType("MeshInfo"))));
                        
                        var map = buildMapMethod.Invoke(null, new object[] { v1, targetManifest, seedMaps });
                        
                        // Compute score based on operation targets
                        int targets = 0;
                        int hits = 0;
                        foreach (var op in ops)
                        {
                            targets++;
                            var opType = op.GetType();
                            var targetMeshNameProp = opType.GetProperty("targetMeshName") as System.Reflection.MemberInfo ?? opType.GetField("targetMeshName");
                            if (targetMeshNameProp != null)
                            {
                                string targetMeshName = (GetMemberValue(targetMeshNameProp, op) ?? "").ToString();
                                var meshesProp = targetManifest.GetType().GetProperty("meshes") as System.Reflection.MemberInfo ?? targetManifest.GetType().GetField("meshes");
                                if (meshesProp != null)
                                {
                                    var meshes = GetMemberValue(meshesProp, targetManifest) as System.Collections.IEnumerable;
                                    if (meshes != null)
                                    {
                                        foreach (var mesh in meshes)
                                        {
                                            var nameProp = mesh.GetType().GetProperty("name") as System.Reflection.MemberInfo ?? mesh.GetType().GetField("name");
                                            if (nameProp != null && (GetMemberValue(nameProp, mesh) ?? "").ToString() == targetMeshName)
                                            {
                                                hits++;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        float score = targets > 0 ? (float)hits / targets : 0f;
                        
                        WriteLog($"    Score: {score:0.##} (targets: {targets}, hits: {hits}, autoApplyThreshold: {autoApplyThreshold}, reviewThreshold: {reviewThreshold})");
                        
                        bool auto = score >= autoApplyThreshold;
                        bool ask = score >= reviewThreshold && score < autoApplyThreshold;
                        
                        if (auto)
                        {
                            WriteLog($"    AUTO-APPLYING patch '{friendlyName}' to {modelPath} (score {score:0.##})");
                            Debug.Log($"[YUCP PatchImporter] Auto-applying patch '{friendlyName}' to {modelPath} (score {score:0.##})");
                            
                            // Backup prefabs
                            if (backupPrefabsMethod != null)
                            {
                                backupPrefabsMethod.Invoke(null, new object[] { modelPath, null });
                            }
                            
                            // Apply patch
                            var applyResult = applyToTargetMethod.Invoke(null, new object[] { modelPath, patchObj, score, "name-map", null });
                            if (applyResult != null)
                            {
                                var state = applyResult;
                                var derivedAssetsField = state.GetType().GetField("derivedAssets");
                                var derivedAssets = derivedAssetsField?.GetValue(state) as List<UnityEngine.Object>;
                                
                                // Create patched prefab copies
                                if (createPatchedPrefabsMethod != null && derivedAssets != null)
                                {
                                    createPatchedPrefabsMethod.Invoke(null, new object[] { modelPath, derivedAssets });
                                }
                                
                                // Record applied patch
                                RecordAppliedPatch(patchObj, modelPath, state, derivedAssets ?? new List<UnityEngine.Object>());
                            }
                        }
                        else if (ask)
                        {
                            Debug.Log($"[YUCP PatchImporter] Patch '{friendlyName}' candidate for {modelPath} (score {score:0.##}). Review recommended.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[YUCP PatchImporter] Error applying patch to {modelPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PatchImporter] Error applying patch: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private static void RecordAppliedPatch(UnityEngine.Object patch, string targetFbxPath, object state, List<UnityEngine.Object> derivedAssets)
        {
            try
            {
                string patchPackagePath = AssetDatabase.GetAssetPath(patch);
                string statePath = "";
                if (state != null)
                {
                    statePath = AssetDatabase.GetAssetPath(state as UnityEngine.Object);
                }
                var derivedPaths = derivedAssets
                    .Select(a => AssetDatabase.GetAssetPath(a))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
                
                // Call cleanup script's RecordAppliedPatch method
                var cleanupType = System.Type.GetType("YUCP.PatchCleanup.YUCPPatchCleanup, Assembly-CSharp-Editor");
                if (cleanupType != null)
                {
                    var recordMethod = cleanupType.GetMethod("RecordAppliedPatch", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (recordMethod != null)
                    {
                        var patchType = patch.GetType();
                        var uiHintsProp = patchType.GetProperty("uiHints") as System.Reflection.MemberInfo ?? patchType.GetField("uiHints");
                        string friendlyName = Path.GetFileNameWithoutExtension(patchPackagePath);
                        if (uiHintsProp != null)
                        {
                            var uiHints = GetMemberValue(uiHintsProp, patch);
                            if (uiHints != null)
                            {
                                var friendlyNameProp = uiHints.GetType().GetProperty("friendlyName") as System.Reflection.MemberInfo ?? uiHints.GetType().GetField("friendlyName");
                                if (friendlyNameProp != null)
                                {
                                    var fn = GetMemberValue(friendlyNameProp, uiHints);
                                    if (fn != null && !string.IsNullOrEmpty(fn.ToString()))
                                        friendlyName = fn.ToString();
                                }
                            }
                        }
                        
                        recordMethod.Invoke(null, new object[] { 
                            friendlyName,
                            patchPackagePath,
                            targetFbxPath,
                            statePath,
                            derivedPaths
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PatchImporter] Failed to record applied patch: {ex.Message}");
            }
        }
        
        private static object GetMemberValue(System.Reflection.MemberInfo member, object obj)
        {
            if (member is System.Reflection.PropertyInfo prop)
                return prop.GetValue(obj);
            if (member is System.Reflection.FieldInfo field)
                return field.GetValue(obj);
            return null;
        }
    }
}

