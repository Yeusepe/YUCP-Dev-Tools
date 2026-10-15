using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public class YUCPImportMonitor : AssetPostprocessor
    {
        private static HashSet<string> processedImports = new HashSet<string>();

        /// <summary>
        /// Runs when assets are imported - detects .yucp_disabled files and handles conflicts
        /// </summary>
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {

			if (PackageBuilder.s_isExporting)
			{
				return;
			}


			var disabledFiles = importedAssets.Where(a => a.EndsWith(".yucp_disabled")).ToArray();
			if (disabledFiles.Length > 0)
			{
				Debug.Log($"[YUCP ImportMonitor] Detected {disabledFiles.Length} .yucp_disabled files being imported");
				HandleImportConflicts(disabledFiles);
			}




			var importedPatches = new System.Collections.Generic.List<PatchPackage>();


			if (processedImports == null)
				processedImports = new HashSet<string>();


			foreach (var path in importedAssets)
			{

				if (path.Contains("/Derived/") || path.Contains("\\Derived\\"))
					continue;


				if (!path.Contains("/Patches/") && !path.Contains("\\Patches\\"))
					continue;


				if (processedImports.Contains(path))
					continue;


				if (path.Contains("PatchPackage") && path.EndsWith(".asset"))
				{
					var patch = AssetDatabase.LoadAssetAtPath<PatchPackage>(path);
					if (patch != null)
					{
						importedPatches.Add(patch);
						processedImports.Add(path);
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


			var modelGuids = AssetDatabase.FindAssets("t:Model");
			var modelPaths = modelGuids.Select(AssetDatabase.GUIDToAssetPath).ToList();

			foreach (var patch in patches)
			{
				string patchPath = AssetDatabase.GetAssetPath(patch);

				if (appliedPatches.Contains(patchPath))
				{
					Debug.Log($"[YUCP ImportMonitor] Patch '{patch.uiHints.friendlyName}' already applied, skipping");
					continue;
				}

				foreach (var modelPath in modelPaths)
				{


					string patchTargetKey = $"{patchPath}|{modelPath}";


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


					var targetManifest = ManifestBuilder.BuildForFbx(modelPath);

					var baseManifestJsonId = patch.sourceManifestId;

					var v1 = new ManifestBuilder.Manifest { manifestId = baseManifestJsonId, meshes = new System.Collections.Generic.List<ManifestBuilder.MeshInfo>() };
					var v2 = targetManifest;
					var map = MapBuilder.Build(v1, v2, patch.seedMaps);


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


						if (state == null)
						{
							Debug.Log($"[YUCP ImportMonitor] Patch application was skipped (already processed)");
							continue;
						}


						BackupManager.CreatePatchedPrefabCopies(modelPath, derived);


						RecordAppliedPatch(patch, modelPath, state, derived);
					}
					else if (ask)
					{
						Debug.Log($"[YUCP ImportMonitor] Patch '{patch.uiHints.friendlyName}' candidate for {modelPath} (score {score:0.##}). Review recommended.");
					}
					else
					{

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


                foreach (string disabledAssetPath in disabledFiles)
                {

                    string disabledFullPath = Path.GetFullPath(disabledAssetPath);
                    string enabledFullPath = disabledFullPath.Substring(0, disabledFullPath.Length - ".yucp_disabled".Length);

                    if (File.Exists(enabledFullPath))
                    {

                        conflictingFiles.Add(disabledAssetPath);
                    }
                    else
                    {

                        safeFiles.Add(disabledAssetPath);
                    }
                }

                if (conflictingFiles.Count == 0)
                {
                    Debug.Log($"[YUCP ImportMonitor] No conflicts detected. {safeFiles.Count} new files importing.");
                    return;
                }


                string detectionResult = DetectImportType(conflictingFiles);

                if (detectionResult == "DUPLICATE")
                {


                    Debug.LogWarning($"[YUCP ImportMonitor] DUPLICATE IMPORT DETECTED!");
                    Debug.LogWarning($"[YUCP ImportMonitor] Package is already installed. Leaving {conflictingFiles.Count} conflicting .yucp_disabled file(s) in place.");
                }
                else if (detectionResult == "UPDATE" || detectionResult == "DOWNGRADE")
                {

                    Debug.LogWarning($"[YUCP ImportMonitor] UPDATE DETECTED! Leaving {conflictingFiles.Count} conflicting .yucp_disabled file(s) in place.");
                }
                else
                {


                    float conflictPercentage = (float)conflictingFiles.Count / disabledFiles.Length;

                    if (conflictPercentage > 0.9f)
                    {
                        Debug.LogWarning($"[YUCP ImportMonitor] {conflictPercentage:P0} of files conflict - assuming DUPLICATE (non-destructive).");
                        Debug.LogWarning($"[YUCP ImportMonitor] Leaving incoming .yucp_disabled file(s) in place. If this is truly a duplicate, you can delete them manually.");
                    }
                    else
                    {
                        Debug.LogWarning($"[YUCP ImportMonitor] {conflictPercentage:P0} of files conflict - assuming UPDATE (safe mode). Leaving conflicting .yucp_disabled file(s) in place.");
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

                string[] tempJsonFiles = Array.Empty<string>();
                try
                {
                    string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string installedRoot = Path.Combine(projectRoot, "Packages", "yucp.installed-packages");
                    if (Directory.Exists(installedRoot))
                    {
                        tempJsonFiles = Directory.GetFiles(installedRoot, "YUCP_TempInstall_*.json", SearchOption.AllDirectories);
                    }
                }
                catch { }

                if (tempJsonFiles.Length == 0)
                {
                    tempJsonFiles = Directory.GetFiles(Application.dataPath, "YUCP_TempInstall_*.json", SearchOption.TopDirectoryOnly);
                }

                if (tempJsonFiles.Length > 0)
                {
                    string jsonContent = File.ReadAllText(tempJsonFiles[0]);
                    var nameMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, @"""name""\s*:\s*""([^""]+)""");
                    var newVersionMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, @"""version""\s*:\s*""([^""]+)""");

                    if (nameMatch.Success && newVersionMatch.Success)
                    {
                        string packageName = nameMatch.Groups[1].Value;
                        string newVersion = newVersionMatch.Groups[1].Value;


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
                                    return "DOWNGRADE";
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
