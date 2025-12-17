using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Global import monitor for YUCP packages.
    /// This script is part of com.yucp.components and monitors for incoming package imports.
    /// Provides the same protection as YUCPPackageGuardian (bundled version).
    /// </summary>
    [InitializeOnLoad]
    public class YUCPImportMonitor : AssetPostprocessor
    {
        private static HashSet<string> processedImports = new HashSet<string>();
        
        static YUCPImportMonitor()
        {
            // On editor load, check if standalone guardian should be removed
            EditorApplication.delayCall += CheckForStandaloneGuardian;
        }
        
        /// <summary>
        /// Remove standalone guardian if com.yucp.components is present
        /// </summary>
        private static void CheckForStandaloneGuardian()
        {
            try
            {
                string standaloneGuardianPath = Path.Combine(Application.dataPath, "..", "Packages", "yucp.packageguardian");
                
                if (Directory.Exists(standaloneGuardianPath))
                {
                    // Only remove the standalone guardian when the integrated guardian is enabled.
                    // If Package Guardian is disabled in settings, removing the standalone guardian breaks
                    // .yucp_disabled resolution in projects that rely on it.
                    bool integratedEnabled = true;
                    try
                    {
                        // Resolve via reflection to avoid a hard dependency from devtools -> components.
                        var type = Type.GetType("YUCP.Components.PackageGuardian.Editor.Settings.PackageGuardianSettings, YUCP.Components.Editor");
                        var isEnabled = type?.GetMethod("IsEnabled", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (isEnabled != null)
                        {
                            integratedEnabled = (bool)isEnabled.Invoke(null, null);
                        }
                    }
                    catch
                    {
                        // If we can't read settings, err on the side of not deleting user content.
                        integratedEnabled = false;
                    }

                    if (!integratedEnabled)
                    {
                        Debug.LogWarning("[YUCP ImportMonitor] Found standalone guardian, but Package Guardian is disabled. Keeping standalone guardian so .yucp_disabled resolution still works.");
                        return;
                    }

                    Debug.Log("[YUCP ImportMonitor] Found standalone guardian - removing (com.yucp.components provides protection)...");
                    try
                    {
                        Directory.Delete(standaloneGuardianPath, true);
                        Debug.Log("[YUCP ImportMonitor] Removed standalone guardian package");
                        AssetDatabase.Refresh();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[YUCP ImportMonitor] Could not auto-remove standalone guardian: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP ImportMonitor] Error checking for standalone guardian: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Runs when assets are imported - detects .yucp_disabled files and handles conflicts
        /// </summary>
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
			// Skip if we're in the middle of an export (prevents reserialization loops)
			if (PackageBuilder.s_isExporting)
			{
				return; // Skip patch application during export
			}
			
			// Handle .yucp_disabled conflicts (existing behavior)
			var disabledFiles = importedAssets.Where(a => a.EndsWith(".yucp_disabled")).ToArray();
			if (disabledFiles.Length > 0)
			{
				Debug.Log($"[YUCP ImportMonitor] Detected {disabledFiles.Length} .yucp_disabled files being imported");
				HandleImportConflicts(disabledFiles);
			}

			// Detect PatchPackage assets and orchestrate application
			// Only process patches that are ACTUALLY being imported right now
			// Do NOT scan folders - that causes infinite loops when derived assets trigger this callback
			var importedPatches = new System.Collections.Generic.List<PatchPackage>();
			
			// Track which patches we've already processed
			if (processedImports == null)
				processedImports = new HashSet<string>();
			
			// Check ONLY the imported assets array
			foreach (var path in importedAssets)
			{
				// Skip derived assets - they're outputs from patch application, not patch packages
				if (path.Contains("/Derived/") || path.Contains("\\Derived\\"))
					continue;
					
				// Skip anything not in Patches folder - patches should only be in Patches folder
				if (!path.Contains("/Patches/") && !path.Contains("\\Patches\\"))
					continue;
					
				// Skip if we've already processed this patch
				if (processedImports.Contains(path))
					continue;
				
				// Only process PatchPackage assets
				if (path.Contains("PatchPackage") && path.EndsWith(".asset"))
				{
					var patch = AssetDatabase.LoadAssetAtPath<PatchPackage>(path);
					if (patch != null)
					{
						importedPatches.Add(patch);
						processedImports.Add(path); // Mark as processed
					}
				}
			}

			if (importedPatches.Count > 0)
			{
				try
				{
					ApplyImportedPatches(importedPatches);
				}
				catch (System.Exception ex)
				{
					Debug.LogError($"[YUCP ImportMonitor] Error applying imported PatchPackages: {ex.Message}");
				}
			}
        }
		
		private static void ApplyImportedPatches(System.Collections.Generic.List<PatchPackage> patches)
		{
			// Check if patches have already been applied
			var appliedPatches = new HashSet<string>();
			string derivedDir = "Packages/com.yucp.temp/Derived";
			if (AssetDatabase.IsValidFolder(derivedDir))
			{
				string[] stateGuids = AssetDatabase.FindAssets("t:AppliedPatchState", new[] { derivedDir });
				foreach (var guid in stateGuids)
				{
					var state = AssetDatabase.LoadAssetAtPath<AppliedPatchState>(AssetDatabase.GUIDToAssetPath(guid));
					if (state != null && state.patch != null)
					{
						string patchPath = AssetDatabase.GetAssetPath(state.patch);
						if (!string.IsNullOrEmpty(patchPath))
							appliedPatches.Add(patchPath);
					}
				}
			}
			
			// Find candidate FBX models in the project
			var modelGuids = AssetDatabase.FindAssets("t:Model");
			var modelPaths = modelGuids.Select(AssetDatabase.GUIDToAssetPath).ToList();

			foreach (var patch in patches)
			{
				string patchPath = AssetDatabase.GetAssetPath(patch);
				// Skip if this patch has already been applied
				if (appliedPatches.Contains(patchPath))
				{
					Debug.Log($"[YUCP ImportMonitor] Patch '{patch.uiHints.friendlyName}' already applied, skipping");
					continue;
				}
				
				foreach (var modelPath in modelPaths)
				{
					// Check if this patch+target combination is already being processed
					// Use the same key format as Applicator uses
					string patchTargetKey = $"{patchPath}|{modelPath}";
					
					// Check Applicator's processed set via reflection
					bool alreadyProcessing = false;
					try
					{
						var applicatorType = typeof(Applicator);
						var processedField = applicatorType.GetField("processedPatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
						if (processedField != null)
						{
							var processedSet = processedField.GetValue(null) as HashSet<string>;
							if (processedSet != null && processedSet.Contains(patchTargetKey))
							{
								alreadyProcessing = true;
							}
						}
					}
					catch { }
					
					if (alreadyProcessing)
					{
						Debug.Log($"[YUCP ImportMonitor] Patch '{patch.uiHints.friendlyName}' already being processed for {modelPath}, skipping");
						continue;
					}
					
					// Also check if patch has already been applied to this specific target
					string targetManifestId = ManifestBuilder.BuildForFbx(modelPath).manifestId;
					bool alreadyApplied = false;
					if (AssetDatabase.IsValidFolder(derivedDir))
					{
						string[] stateGuids = AssetDatabase.FindAssets("t:AppliedPatchState", new[] { derivedDir });
						foreach (var guid in stateGuids)
						{
							var existingState = AssetDatabase.LoadAssetAtPath<AppliedPatchState>(AssetDatabase.GUIDToAssetPath(guid));
							if (existingState != null && existingState.patch == patch && existingState.targetManifestId == targetManifestId)
							{
								alreadyApplied = true;
								break;
							}
						}
					}
					
					if (alreadyApplied)
					{
						Debug.Log($"[YUCP ImportMonitor] Patch '{patch.uiHints.friendlyName}' already applied to {modelPath}, skipping");
						continue;
					}
					
					// Build manifest for target and compute a naive score
					var targetManifest = ManifestBuilder.BuildForFbx(modelPath);
					// Basic compatibility check: at least one mesh name overlap
					var baseManifestJsonId = patch.sourceManifestId;
					// We do not have v1 manifest content here; heuristic: proceed to mapping using names and target manifest only
					var v1 = new ManifestBuilder.Manifest { manifestId = baseManifestJsonId, meshes = new System.Collections.Generic.List<ManifestBuilder.MeshInfo>() };
					var v2 = targetManifest;
					var map = MapBuilder.Build(v1, v2, patch.seedMaps);

					// Name overlap score: compare patch mesh ops targets vs target meshes
					int targets = 0;
					int hits = 0;
					foreach (var op in patch.ops)
					{
						if (op is PatchPackage.MeshDeltaOp m)
						{
							targets++;
							if (v2.meshes.Any(mm => mm.name == m.targetMeshName)) hits++;
						}
						else if (op is PatchPackage.UVLayerOp u)
						{
							targets++;
							if (v2.meshes.Any(mm => mm.name == u.targetMeshName)) hits++;
						}
						else if (op is PatchPackage.BlendshapeOp b)
						{
							targets++;
							if (v2.meshes.Any(mm => mm.name == b.targetMeshName)) hits++;
						}
					}
					float score = targets > 0 ? (float)hits / targets : 0f;

					bool auto = score >= patch.policy.autoApplyThreshold;
					bool ask = score >= patch.policy.reviewThreshold && score < patch.policy.autoApplyThreshold;

					if (auto)
					{
						Debug.Log($"[YUCP ImportMonitor] Auto-applying patch '{patch.uiHints.friendlyName}' to {modelPath} (score {score:0.##})");
						BackupManager.BackupPrefabsReferencing(modelPath, out var _);
						var state = Applicator.ApplyToTarget(modelPath, patch, score, mapId: "name-map", out var derived);
						
						// Check if application was skipped (returns null if already processed)
						if (state == null)
						{
							Debug.Log($"[YUCP ImportMonitor] Patch application was skipped (already processed)");
							continue;
						}
						
						// Optionally create patched prefab copies
						BackupManager.CreatePatchedPrefabCopies(modelPath, derived);
						
						// Record applied patch for cleanup/revert functionality
						RecordAppliedPatch(patch, modelPath, state, derived);
					}
					else if (ask)
					{
						Debug.Log($"[YUCP ImportMonitor] Patch '{patch.uiHints.friendlyName}' candidate for {modelPath} (score {score:0.##}). Review recommended.");
					}
					else
					{
						// skip silently
					}
				}
			}
		}
		
		private static void RecordAppliedPatch(PatchPackage patch, string targetFbxPath, AppliedPatchState state, List<UnityEngine.Object> derivedAssets)
		{
			try
			{
				string patchPackagePath = AssetDatabase.GetAssetPath(patch);
				string statePath = AssetDatabase.GetAssetPath(state);
				var derivedPaths = derivedAssets
					.Select(a => AssetDatabase.GetAssetPath(a))
					.Where(p => !string.IsNullOrEmpty(p))
					.ToList();
				
				// Use reflection to call the cleanup script's RecordAppliedPatch method
				// This avoids direct dependency since cleanup script is in temp package
				var cleanupType = System.Type.GetType("YUCP.PatchCleanup.YUCPPatchCleanup, Assembly-CSharp");
				if (cleanupType != null)
				{
					var recordMethod = cleanupType.GetMethod("RecordAppliedPatch", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
					if (recordMethod != null)
					{
						recordMethod.Invoke(null, new object[] { 
							patch.uiHints.friendlyName ?? patch.name,
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
				Debug.LogWarning($"[YUCP ImportMonitor] Failed to record applied patch: {ex.Message}");
			}
		}
        
        private static void HandleImportConflicts(string[] disabledFiles)
        {
            try
            {
                var conflictingFiles = new List<string>();
                var safeFiles = new List<string>();
                
                // Analyze each disabled file
                foreach (string disabledAssetPath in disabledFiles)
                {
                    // Convert to full path
                    string disabledFullPath = Path.GetFullPath(disabledAssetPath);
                    string enabledFullPath = disabledFullPath.Substring(0, disabledFullPath.Length - ".yucp_disabled".Length);
                    
                    if (File.Exists(enabledFullPath))
                    {
                        // Conflict detected - enabled version already exists
                        conflictingFiles.Add(disabledAssetPath);
                    }
                    else
                    {
                        // No conflict - this is a new file
                        safeFiles.Add(disabledAssetPath);
                    }
                }
                
                if (conflictingFiles.Count == 0)
                {
                    Debug.Log($"[YUCP ImportMonitor] No conflicts detected. {safeFiles.Count} new files importing.");
                    return;
                }
                
                // Determine if this is a duplicate or an update
                string detectionResult = DetectImportType(conflictingFiles);
                
                if (detectionResult == "DUPLICATE")
                {
                    // Same version already installed - DO NOT delete incoming .yucp_disabled files automatically.
                    // Deleting is destructive and makes it impossible to inspect/repair imports.
                    Debug.LogWarning($"[YUCP ImportMonitor] DUPLICATE IMPORT DETECTED!");
                    Debug.LogWarning($"[YUCP ImportMonitor] Package is already installed. Leaving {conflictingFiles.Count} conflicting .yucp_disabled file(s) in place.");
                }
                else if (detectionResult == "UPDATE")
                {
                    // Newer version - delete the OLD enabled files to make room for new ones
                    Debug.Log($"[YUCP ImportMonitor] UPDATE DETECTED!");
                    Debug.Log($"[YUCP ImportMonitor] Removing {conflictingFiles.Count} old files to make room for update...");
                    
                    DeleteOldEnabledFiles(conflictingFiles);
                }
                else
                {
                    // Unknown - use heuristic
                    // If >90% of files conflict, probably duplicate
                    float conflictPercentage = (float)conflictingFiles.Count / disabledFiles.Length;
                    
                    if (conflictPercentage > 0.9f)
                    {
                        Debug.LogWarning($"[YUCP ImportMonitor] {conflictPercentage:P0} of files conflict - assuming DUPLICATE (non-destructive).");
                        Debug.LogWarning($"[YUCP ImportMonitor] Leaving incoming .yucp_disabled file(s) in place. If this is truly a duplicate, you can delete them manually.");
                    }
                    else
                    {
                        Debug.Log($"[YUCP ImportMonitor] {conflictPercentage:P0} of files conflict - assuming UPDATE");
                        DeleteOldEnabledFiles(conflictingFiles);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP ImportMonitor] Error handling import conflicts: {ex.Message}");
                Debug.LogError("[YUCP ImportMonitor] Import will proceed but may have GUID conflicts.");
            }
        }
        
        private static string DetectImportType(List<string> conflictingFiles)
        {
            try
            {
                // Try to find temp install JSON
                string[] tempJsonFiles = Directory.GetFiles(Application.dataPath, "YUCP_TempInstall_*.json", SearchOption.TopDirectoryOnly);
                
                if (tempJsonFiles.Length > 0)
                {
                    string jsonContent = File.ReadAllText(tempJsonFiles[0]);
                    var nameMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, @"""name""\s*:\s*""([^""]+)""");
                    var newVersionMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, @"""version""\s*:\s*""([^""]+)""");
                    
                    if (nameMatch.Success && newVersionMatch.Success)
                    {
                        string packageName = nameMatch.Groups[1].Value;
                        string newVersion = newVersionMatch.Groups[1].Value;
                        
                        // Check existing package.json
                        string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                        string existingPackageJson = Path.Combine(packagesPath, packageName, "package.json");
                        
                        if (File.Exists(existingPackageJson))
                        {
                            string existingJson = File.ReadAllText(existingPackageJson);
                            var existingVersionMatch = System.Text.RegularExpressions.Regex.Match(existingJson, @"""version""\s*:\s*""([^""]+)""");
                            
                            if (existingVersionMatch.Success)
                            {
                                string existingVersion = existingVersionMatch.Groups[1].Value;
                                
                                int comparison = CompareVersions(existingVersion, newVersion);
                                
                                if (comparison == 0)
                                {
                                    Debug.Log($"[YUCP ImportMonitor] Version match: {packageName} v{newVersion} (DUPLICATE)");
                                    return "DUPLICATE";
                                }
                                else if (comparison < 0)
                                {
                                    Debug.Log($"[YUCP ImportMonitor] Version upgrade: {packageName} {existingVersion} → {newVersion} (UPDATE)");
                                    return "UPDATE";
                                }
                                else
                                {
                                    Debug.LogWarning($"[YUCP ImportMonitor] Version downgrade: {packageName} {existingVersion} → {newVersion}");
                                    return "DOWNGRADE"; // Treat as update (delete old, install new)
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP ImportMonitor] Could not determine import type: {ex.Message}");
            }
            
            return "UNKNOWN";
        }
        
        private static void DeleteConflictingDisabledFiles(List<string> conflictingDisabledAssetPaths)
        {
            int deletedCount = 0;
            
            foreach (string assetPath in conflictingDisabledAssetPaths)
            {
                try
                {
                    string fullPath = Path.GetFullPath(assetPath);
                    
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        string metaPath = fullPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YUCP ImportMonitor] Failed to delete '{Path.GetFileName(assetPath)}': {ex.Message}");
                }
            }
            
            if (deletedCount > 0)
            {
                Debug.Log($"[YUCP ImportMonitor] Deleted {deletedCount} duplicate .yucp_disabled files");
                AssetDatabase.Refresh();
            }
        }
        
        private static void DeleteOldEnabledFiles(List<string> conflictingDisabledAssetPaths)
        {
            int deletedCount = 0;
            
            foreach (string disabledAssetPath in conflictingDisabledAssetPaths)
            {
                try
                {
                    string disabledFullPath = Path.GetFullPath(disabledAssetPath);
                    string enabledFullPath = disabledFullPath.Substring(0, disabledFullPath.Length - ".yucp_disabled".Length);
                    
                    if (File.Exists(enabledFullPath))
                    {
                        // Delete the OLD enabled file to make room for the new one
                        File.Delete(enabledFullPath);
                        string metaPath = enabledFullPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        deletedCount++;
                        
                        Debug.Log($"[YUCP ImportMonitor] Deleted old file to make room for update: {Path.GetFileName(enabledFullPath)}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YUCP ImportMonitor] Failed to delete old file '{Path.GetFileName(disabledAssetPath)}': {ex.Message}");
                }
            }
            
            if (deletedCount > 0)
            {
                Debug.Log($"[YUCP ImportMonitor] Deleted {deletedCount} old files to prepare for update");
                AssetDatabase.Refresh();
            }
        }
        
        private static int CompareVersions(string v1, string v2)
        {
            try
            {
                var version1 = ParseVersion(v1);
                var version2 = ParseVersion(v2);
                
                if (version1.major != version2.major)
                    return version1.major.CompareTo(version2.major);
                if (version1.minor != version2.minor)
                    return version1.minor.CompareTo(version2.minor);
                return version1.patch.CompareTo(version2.patch);
            }
            catch
            {
                return 0;
            }
        }
        
        private static (int major, int minor, int patch) ParseVersion(string version)
        {
            version = version.Trim().TrimStart('v', 'V');
            int dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
                version = version.Substring(0, dashIndex);
            
            var parts = version.Split('.');
            int major = parts.Length > 0 && int.TryParse(parts[0], out int m) ? m : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : 0;
            int patch = parts.Length > 2 && int.TryParse(parts[2], out int p) ? p : 0;
            
            return (major, minor, patch);
        }
    }
}

