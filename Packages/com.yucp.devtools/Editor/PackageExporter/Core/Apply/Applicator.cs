using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Applies PatchPackage operations to a target FBX by generating derived Unity assets and optional prefab variants.
	/// </summary>
	public static class Applicator
	{
		private static HashSet<string> processedPatches = new HashSet<string>();
		
		private static void WriteLog(string message)
		{
			try
			{
				string logDir = Path.Combine(Application.dataPath, "..", "Logs");
				if (!Directory.Exists(logDir))
					Directory.CreateDirectory(logDir);
				string logPath = Path.Combine(logDir, "YUCP_PatchImporter.log");
				string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
				File.AppendAllText(logPath, $"[{timestamp}] [Applicator] {message}\n");
				Debug.Log($"[Applicator] {message}");
			}
			catch { }
		}
		
		public static AppliedPatchState ApplyToTarget(string baseFbxPath, PatchPackage patch, float score, string mapId, out List<UnityEngine.Object> derivedAssets)
		{
			derivedAssets = new List<UnityEngine.Object>();

			WriteLog($"ApplyToTarget called: patch={AssetDatabase.GetAssetPath(patch)}, target={baseFbxPath}, score={score}");

			// CRITICAL: Check if this patch+target combination is already being processed or has been processed
			// This prevents infinite loops when OnPostprocessAllAssets is triggered
			string patchPath = AssetDatabase.GetAssetPath(patch);
			string patchTargetKey = $"{patchPath}|{baseFbxPath}";
			
			// Check our processed set FIRST (fastest check)
			if (processedPatches != null && processedPatches.Contains(patchTargetKey))
			{
				WriteLog($"Patch+target combination already processed: {patchTargetKey}, skipping");
				Debug.Log($"[Applicator] Patch+target combination already processed: {patchTargetKey}, skipping");
				// Try to find existing state
				string checkDerivedDir = "Packages/com.yucp.temp/Derived";
				if (AssetDatabase.IsValidFolder(checkDerivedDir))
				{
					string[] stateGuids = AssetDatabase.FindAssets("t:AppliedPatchState", new[] { checkDerivedDir });
					foreach (var guid in stateGuids)
					{
						var existingState = AssetDatabase.LoadAssetAtPath<AppliedPatchState>(AssetDatabase.GUIDToAssetPath(guid));
						if (existingState != null && existingState.patch == patch)
						{
							derivedAssets = existingState.producedDerivedAssets ?? new List<UnityEngine.Object>();
							return existingState;
						}
					}
				}
				// If no state found, return null to indicate skip
				return null;
			}
			
			// Check if patch has already been applied to this target by checking AppliedPatchState
			string derivedDir = "Packages/com.yucp.temp/Derived";
			if (AssetDatabase.IsValidFolder(derivedDir))
			{
				string[] stateGuids = AssetDatabase.FindAssets("t:AppliedPatchState", new[] { derivedDir });
				foreach (var guid in stateGuids)
				{
					var existingState = AssetDatabase.LoadAssetAtPath<AppliedPatchState>(AssetDatabase.GUIDToAssetPath(guid));
					if (existingState != null && existingState.patch == patch && existingState.targetManifestId == ManifestBuilder.BuildForFbx(baseFbxPath).manifestId)
					{
						// Patch already applied to this target - mark as processed and return existing state
						if (processedPatches == null)
							processedPatches = new HashSet<string>();
						processedPatches.Add(patchTargetKey);
						Debug.Log($"[Applicator] Patch already applied to {baseFbxPath}, skipping");
						derivedAssets = existingState.producedDerivedAssets ?? new List<UnityEngine.Object>();
						return existingState;
					}
				}
			}
			
			// Mark as being processed IMMEDIATELY to prevent re-entry
			if (processedPatches == null)
				processedPatches = new HashSet<string>();
			processedPatches.Add(patchTargetKey);
			WriteLog($"Marked patch+target as processing: {patchTargetKey}");

			// Load base meshes
			WriteLog($"Loading meshes from: {baseFbxPath}");
			var baseMeshes = LoadMeshesByName(baseFbxPath);
			WriteLog($"Found {baseMeshes.Count} mesh(es) in base FBX");

			// Create derived cache folder
			derivedDir = EnsureDerivedDir();
			
			// CRITICAL: Use StartAssetEditing to batch all CreateAsset calls
			// This prevents OnPostprocessAllAssets from being triggered for each individual asset
			WriteLog($"Processing {patch.ops.Count} operation(s)");
			AssetDatabase.StartAssetEditing();

			// Process ops
			int opIndex = 0;
			foreach (var op in patch.ops)
			{
				opIndex++;
				WriteLog($"  Processing op {opIndex}/{patch.ops.Count}: {op.GetType().Name}");
				
				switch (op)
				{
					case PatchPackage.MeshDeltaOp meshOp:
					{
						if (!baseMeshes.TryGetValue(meshOp.targetMeshName, out var baseMesh) || meshOp.meshDelta == null)
							continue;
						var newMesh = Object.Instantiate(baseMesh);
						newMesh.name = $"{baseMesh.name}_Patched";

						// Apply deltas
						if (meshOp.meshDelta.positionDeltas != null && meshOp.meshDelta.positionDeltas.Length == baseMesh.vertexCount)
						{
							var v = newMesh.vertices;
							for (int i = 0; i < v.Length; i++) v[i] += meshOp.meshDelta.positionDeltas[i];
							newMesh.vertices = v;
						}
						if (meshOp.meshDelta.normalDeltas != null && meshOp.meshDelta.normalDeltas.Length == baseMesh.vertexCount)
						{
							var n = newMesh.normals;
							if (n != null && n.Length == baseMesh.vertexCount)
							{
								for (int i = 0; i < n.Length; i++) n[i] += meshOp.meshDelta.normalDeltas[i];
								newMesh.normals = n;
							}
						}
						if (meshOp.meshDelta.tangentDeltas != null && meshOp.meshDelta.tangentDeltas.Length == baseMesh.vertexCount)
						{
							var t = newMesh.tangents;
							if (t != null && t.Length == baseMesh.vertexCount)
							{
								for (int i = 0; i < t.Length; i++)
								{
									var add = meshOp.meshDelta.tangentDeltas[i];
									t[i] = new Vector4(t[i].x + add.x, t[i].y + add.y, t[i].z + add.z, t[i].w);
								}
								newMesh.tangents = t;
							}
						}
						newMesh.RecalculateBounds();

						if (!Validator.ValidateMesh(newMesh, out var err1))
						{
							WriteLog($"    Mesh validation failed: {err1}");
							Debug.LogError($"[Applicator] Mesh validation failed: {err1}");
							Object.DestroyImmediate(newMesh);
							break;
						}

						string meshAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{derivedDir}/{newMesh.name}.asset");
						WriteLog($"    Creating mesh delta asset: {meshAssetPath}");
						AssetDatabase.CreateAsset(newMesh, meshAssetPath);
						derivedAssets.Add(newMesh);
						break;
					}
					case PatchPackage.UVLayerOp uvOp:
					{
						if (!baseMeshes.TryGetValue(uvOp.targetMeshName, out var baseMesh) || uvOp.uvLayer == null)
							continue;
						var newMesh = Object.Instantiate(baseMesh);
						newMesh.name = $"{baseMesh.name}_UV{uvOp.channel}";

						SetUvs(newMesh, uvOp.channel, uvOp.uvLayer.uvs);
						if (!Validator.ValidateMesh(newMesh, out var err2))
						{
							Debug.LogError($"[Applicator] Mesh validation failed: {err2}");
							Object.DestroyImmediate(newMesh);
							break;
						}
						string meshAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{derivedDir}/{newMesh.name}.asset");
						WriteLog($"    Creating UV mesh asset: {meshAssetPath}");
						AssetDatabase.CreateAsset(newMesh, meshAssetPath);
						derivedAssets.Add(newMesh);
						break;
					}
					case PatchPackage.BlendshapeOp bsOp:
					{
						if (!baseMeshes.TryGetValue(bsOp.targetMeshName, out var baseMesh))
							continue;
						var newMesh = Object.Instantiate(baseMesh);
						newMesh.name = $"{baseMesh.name}_BS";
						if (bsOp.synthesizedFrame != null)
						{
							newMesh.AddBlendShapeFrame(
								bsOp.blendshapeName,
								bsOp.synthesizedFrame.frameWeight,
								bsOp.synthesizedFrame.deltaVertices,
								bsOp.synthesizedFrame.deltaNormals,
								bsOp.synthesizedFrame.deltaTangents
							);
						}
						// scale existing frames could be applied here in future iterations
						if (!Validator.ValidateMesh(newMesh, out var err3))
						{
							Debug.LogError($"[Applicator] Mesh validation failed: {err3}");
							Object.DestroyImmediate(newMesh);
							break;
						}
						string meshAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{derivedDir}/{newMesh.name}.asset");
						WriteLog($"    Creating blendshape mesh asset: {meshAssetPath}");
						AssetDatabase.CreateAsset(newMesh, meshAssetPath);
						derivedAssets.Add(newMesh);
						break;
					}
					case PatchPackage.MaterialOverrideOp matOp:
					{
						if (matOp.overridesAsset != null)
							derivedAssets.Add(matOp.overridesAsset);
						break;
					}
					case PatchPackage.AnimationOverrideOp animOp:
					{
						if (animOp.clip != null)
							derivedAssets.Add(animOp.clip);
						break;
					}
					case PatchPackage.SkinWeightDeltaOp _:
					{
						// For MVP, skip; future: implement reweight application onto a cloned mesh
						break;
					}
				}
			}

			// Create AppliedPatchState
			var state = ScriptableObject.CreateInstance<AppliedPatchState>();
			state.patch = patch;
			state.targetManifestId = ManifestBuilder.BuildForFbx(baseFbxPath).manifestId;
			state.correspondenceMapId = mapId;
			state.confidenceScore = score;
			state.enabledForTarget = true;
			state.producedDerivedAssets = derivedAssets.ToList();
			state.appliedByUser = System.Environment.UserName;

			string statePath = AssetDatabase.GenerateUniqueAssetPath($"{derivedDir}/AppliedPatchState_{Path.GetFileNameWithoutExtension(baseFbxPath)}.asset");
			WriteLog($"Creating AppliedPatchState: {statePath}");
			AssetDatabase.CreateAsset(state, statePath);
			derivedAssets.Add(state);

			// CRITICAL: Stop asset editing - this batches all CreateAsset calls
			// Unity will automatically save assets when StopAssetEditing is called
			WriteLog($"Stopping asset editing. Created {derivedAssets.Count} derived asset(s)");
			AssetDatabase.StopAssetEditing();
			
			// Note: patchTargetKey was already added to processedPatches at the start of the method
			// This prevents re-entry even if OnPostprocessAllAssets fires
			
			WriteLog($"ApplyToTarget completed successfully for: {patchTargetKey}");

			return state;
		}

		private static Dictionary<string, Mesh> LoadMeshesByName(string assetPath)
		{
			var dict = new Dictionary<string, Mesh>();
			var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
			foreach (var a in assets)
			{
				if (a is Mesh m && !dict.ContainsKey(m.name))
					dict.Add(m.name, m);
			}
			return dict;
		}

		private static string EnsureDerivedDir()
		{
			// Use Packages/com.yucp.temp/Derived (temp package location)
			string tempPackagePath = "Packages/com.yucp.temp";
			string derivedPath = $"{tempPackagePath}/Derived";
			
			// Check if folder exists physically (more reliable for Packages)
			string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			string physicalPath = Path.Combine(projectPath, derivedPath.Replace('/', Path.DirectorySeparatorChar));
			
			if (!Directory.Exists(physicalPath))
			{
				// Create directories physically
				Directory.CreateDirectory(physicalPath);
				// DON'T import or refresh - this triggers OnPostprocessAllAssets and causes loops
				// Unity will recognize the folder when assets are created in it
			}
			
			return derivedPath;
		}

		private static void SetUvs(Mesh mesh, int channel, Vector2[] uvs)
		{
			switch (channel)
			{
				case 0: mesh.uv = uvs; break;
				case 1: mesh.uv2 = uvs; break;
				case 2: mesh.uv3 = uvs; break;
				case 3: mesh.uv4 = uvs; break;
#if UNITY_2019_4_OR_NEWER
				case 4: mesh.uv5 = uvs; break;
				case 5: mesh.uv6 = uvs; break;
				case 6: mesh.uv7 = uvs; break;
				case 7: mesh.uv8 = uvs; break;
#endif
			}
		}
	}
}


