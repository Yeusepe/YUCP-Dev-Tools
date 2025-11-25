using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.PatchCleanup
{
    /// <summary>
    /// Smart cleanup script for YUCP patches.
    /// Tracks applied patches and allows selective reversion without deleting the entire temp folder.
    /// </summary>
    [InitializeOnLoad]
    public static class YUCPPatchCleanup
    {
        private const string PATCH_STATE_FILE = "Packages/com.yucp.temp/patch_state.json";
        
        static YUCPPatchCleanup()
        {
            // Auto-cleanup on editor load if temp package is empty
            EditorApplication.delayCall += CheckAndCleanupIfEmpty;
            // Clean up old Assets folders on first load (only once)
            EditorApplication.delayCall += CleanupOldAssetsFolders;
        }
        
        private static bool s_hasCleanedOldFolders = false;
        
        private static void CleanupOldAssetsFolders()
        {
            // Only run once to avoid loops
            if (s_hasCleanedOldFolders) return;
            s_hasCleanedOldFolders = true;
            
            try
            {
                string assetsPath = Application.dataPath;
                string projectPath = Path.GetDirectoryName(assetsPath);
                
                // Clean up old _YUCP Backups folders
                string oldBackupsPath = Path.Combine(assetsPath, "_YUCP Backups");
                if (Directory.Exists(oldBackupsPath))
                {
                    Debug.Log($"[YUCP PatchCleanup] Removing old backup folder: {oldBackupsPath}");
                    Directory.Delete(oldBackupsPath, true);
                    AssetDatabase.Refresh();
                }
                
                // Clean up old .yucp folders (Unity doesn't support these, so they may have been created as numbered variants)
                // Look for _yucp, _yucp 1, _yucp 2, etc.
                var yucpFolders = Directory.GetDirectories(assetsPath, "_yucp*", SearchOption.TopDirectoryOnly);
                foreach (var folder in yucpFolders)
                {
                    string folderName = Path.GetFileName(folder);
                    if (folderName.StartsWith("_yucp") || folderName == ".yucp")
                    {
                        Debug.Log($"[YUCP PatchCleanup] Removing old temp folder: {folder}");
                        Directory.Delete(folder, true);
                    }
                }
                
                // Also check for .yucp folder (if it somehow exists)
                string oldYucpPath = Path.Combine(assetsPath, ".yucp");
                if (Directory.Exists(oldYucpPath))
                {
                    Debug.Log($"[YUCP PatchCleanup] Removing old .yucp folder: {oldYucpPath}");
                    Directory.Delete(oldYucpPath, true);
                    AssetDatabase.Refresh();
                }
                
                if (yucpFolders.Length > 0 || Directory.Exists(oldBackupsPath) || Directory.Exists(oldYucpPath))
                {
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PatchCleanup] Error cleaning up old Assets folders: {ex.Message}");
            }
        }
        
        private static void CheckAndCleanupIfEmpty()
        {
            try
            {
                string patchesPath = "Packages/com.yucp.temp/Patches";
                if (AssetDatabase.IsValidFolder(patchesPath))
                {
                    string[] assets = AssetDatabase.FindAssets("", new[] { patchesPath });
                    if (assets.Length == 0)
                    {
                        // No patches, check if we should clean up the temp package
                        CleanupEmptyTempPackage();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PatchCleanup] Error checking for cleanup: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Revert patches applied to a specific FBX model
        /// </summary>
        [MenuItem("Tools/YUCP/Revert Patches for Selected FBX")]
        public static void RevertPatchesForSelected()
        {
            var selected = Selection.activeObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select an FBX model to revert patches for.", "OK");
                return;
            }
            
            string assetPath = AssetDatabase.GetAssetPath(selected);
            if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Invalid Selection", "Please select an FBX model file.", "OK");
                return;
            }
            
            RevertPatchesForFbx(assetPath);
        }
        
        /// <summary>
        /// Revert all patches for a specific FBX
        /// </summary>
        public static void RevertPatchesForFbx(string fbxPath)
        {
            try
            {
                var state = LoadPatchState();
                if (state == null || state.appliedPatches == null || state.appliedPatches.Count == 0)
                {
                    EditorUtility.DisplayDialog("No Patches", $"No patches found for {Path.GetFileName(fbxPath)}", "OK");
                    return;
                }
                
                // Find patches applied to this FBX
                var patchesForFbx = state.appliedPatches
                    .Where(p => string.Equals(p.targetFbxPath, fbxPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                if (patchesForFbx.Count == 0)
                {
                    EditorUtility.DisplayDialog("No Patches", $"No patches found for {Path.GetFileName(fbxPath)}", "OK");
                    return;
                }
                
                // Confirm reversion
                string patchNames = string.Join("\n", patchesForFbx.Select(p => $"  - {p.patchName}"));
                bool confirmed = EditorUtility.DisplayDialog(
                    "Revert Patches",
                    $"Revert {patchesForFbx.Count} patch(es) for {Path.GetFileName(fbxPath)}?\n\n{patchNames}",
                    "Revert",
                    "Cancel"
                );
                
                if (!confirmed) return;
                
                // Revert each patch
                int revertedCount = 0;
                foreach (var patchInfo in patchesForFbx)
                {
                    if (RevertSinglePatch(patchInfo))
                    {
                        revertedCount++;
                    }
                }
                
                // Remove from state
                state.appliedPatches.RemoveAll(p => patchesForFbx.Contains(p));
                SavePatchState(state);
                
                AssetDatabase.Refresh();
                
                EditorUtility.DisplayDialog("Patches Reverted", $"Successfully reverted {revertedCount} patch(es) for {Path.GetFileName(fbxPath)}", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to revert patches: {ex.Message}", "OK");
                Debug.LogError($"[YUCP PatchCleanup] Error reverting patches: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Revert a single patch by removing its derived assets and restoring the original FBX
        /// </summary>
        private static bool RevertSinglePatch(PatchInfo patchInfo)
        {
            try
            {
                // Find AppliedPatchState asset if it exists (use reflection to avoid dependency)
                string stateAssetPath = patchInfo.appliedStatePath;
                if (!string.IsNullOrEmpty(stateAssetPath) && File.Exists(stateAssetPath))
                {
                    try
                    {
                        var stateAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(stateAssetPath);
                        if (stateAsset != null)
                        {
                            // Try to disable via reflection
                            var enabledProp = stateAsset.GetType().GetProperty("enabled");
                            if (enabledProp != null && enabledProp.GetValue(stateAsset) is bool enabled && enabled)
                            {
                                enabledProp.SetValue(stateAsset, false);
                                EditorUtility.SetDirty(stateAsset);
                                AssetDatabase.SaveAssets();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[YUCP PatchCleanup] Could not disable patch state: {ex.Message}");
                    }
                }
                
                // Delete derived assets
                if (patchInfo.derivedAssets != null)
                {
                    foreach (var derivedPath in patchInfo.derivedAssets)
                    {
                        if (File.Exists(derivedPath))
                        {
                            AssetDatabase.DeleteAsset(derivedPath);
                        }
                    }
                }
                
                // Delete patch package asset
                if (!string.IsNullOrEmpty(patchInfo.patchPackagePath) && File.Exists(patchInfo.patchPackagePath))
                {
                    AssetDatabase.DeleteAsset(patchInfo.patchPackagePath);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PatchCleanup] Error reverting patch {patchInfo.patchName}: {ex.Message}");
                return false;
            }
        }
        
        [MenuItem("Tools/YUCP/Cleanup Empty Temp Package")]
        public static void CleanupEmptyTempPackage()
        {
            try
            {
                string tempPackagePath = Path.Combine(Application.dataPath, "..", "Packages", "com.yucp.temp");
                if (!Directory.Exists(tempPackagePath))
                {
                    EditorUtility.DisplayDialog("Cleanup", "Temp package does not exist.", "OK");
                    return;
                }
                
                // Check if there are any assets
                string patchesPath = Path.Combine(tempPackagePath, "Patches");
                if (Directory.Exists(patchesPath))
                {
                    string[] files = Directory.GetFiles(patchesPath, "*", SearchOption.AllDirectories)
                        .Where(f => !f.EndsWith(".meta")).ToArray();
                    
                    if (files.Length > 0)
                    {
                        bool confirmed = EditorUtility.DisplayDialog(
                            "Cleanup Temp Package",
                            $"Temp package contains {files.Length} file(s). Delete everything?",
                            "Delete All",
                            "Cancel"
                        );
                        
                        if (!confirmed) return;
                    }
                }
                
                // Delete the entire temp package
                Directory.Delete(tempPackagePath, true);
                AssetDatabase.Refresh();
                
                EditorUtility.DisplayDialog("Cleanup Complete", "Temp package has been removed.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to cleanup temp package: {ex.Message}", "OK");
                Debug.LogError($"[YUCP PatchCleanup] Error cleaning up temp package: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Record that a patch was applied
        /// </summary>
        public static void RecordAppliedPatch(string patchName, string patchPackagePath, string targetFbxPath, string appliedStatePath, List<string> derivedAssets)
        {
            try
            {
                var state = LoadPatchState() ?? new PatchState { appliedPatches = new List<PatchInfo>() };
                
                state.appliedPatches.Add(new PatchInfo
                {
                    patchName = patchName,
                    patchPackagePath = patchPackagePath,
                    targetFbxPath = targetFbxPath,
                    appliedStatePath = appliedStatePath,
                    derivedAssets = derivedAssets ?? new List<string>(),
                    appliedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
                
                SavePatchState(state);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PatchCleanup] Failed to record applied patch: {ex.Message}");
            }
        }
        
        private static PatchState LoadPatchState()
        {
            try
            {
                string statePath = Path.Combine(Application.dataPath, "..", PATCH_STATE_FILE.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(statePath))
                {
                    string json = File.ReadAllText(statePath);
                    return JsonUtility.FromJson<PatchState>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PatchCleanup] Failed to load patch state: {ex.Message}");
            }
            return null;
        }
        
        private static void SavePatchState(PatchState state)
        {
            try
            {
                string statePath = Path.Combine(Application.dataPath, "..", PATCH_STATE_FILE.Replace('/', Path.DirectorySeparatorChar));
                string dir = Path.GetDirectoryName(statePath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                string json = JsonUtility.ToJson(state, true);
                File.WriteAllText(statePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PatchCleanup] Failed to save patch state: {ex.Message}");
            }
        }
        
        [Serializable]
        private class PatchState
        {
            public List<PatchInfo> appliedPatches = new List<PatchInfo>();
        }
        
        [Serializable]
        private class PatchInfo
        {
            public string patchName;
            public string patchPackagePath;
            public string targetFbxPath;
            public string appliedStatePath;
            public List<string> derivedAssets;
            public string appliedDate;
        }
    }
}

