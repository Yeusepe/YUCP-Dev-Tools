using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Computes a semantic operation log from Base (v1) vs Modified (v2) FBX.
	/// </summary>
	public static class PatchBuilder
	{
		public class BuildResult
		{
			public PatchPackage patch;
			public List<UnityEngine.Object> generatedSidecars = new List<UnityEngine.Object>();
			public List<string> generatedSidecarPaths = new List<string>(); // Store paths explicitly
		}

		public static BuildResult Build(string baseFbxPath, string modifiedFbxPath, PatchPackage.Policy policy, PatchPackage.UIHints hints, PatchPackage.SeedMaps seeds)
		{
			var v1 = ManifestBuilder.BuildForFbx(baseFbxPath);
			var v2 = ManifestBuilder.BuildForFbx(modifiedFbxPath);
			var map = MapBuilder.Build(v1, v2, seeds);

			var patch = ScriptableObject.CreateInstance<PatchPackage>();
			patch.sourceManifestId = v1.manifestId;
			patch.policy = policy;
			patch.uiHints = hints;
			patch.seedMaps = seeds ?? new PatchPackage.SeedMaps();

			var result = new BuildResult { patch = patch };

			// Load meshes for both FBXs
			var baseMeshes = LoadMeshesByName(baseFbxPath);
			var modMeshes = LoadMeshesByName(modifiedFbxPath);

			// Mesh deltas (same name, same vertex count)
			foreach (var kvp in map.meshMap)
			{
				if (!baseMeshes.TryGetValue(kvp.Key, out var baseMesh) || !modMeshes.TryGetValue(kvp.Value, out var modMesh))
					continue;

				if (baseMesh.vertexCount != modMesh.vertexCount)
				{
					if (policy.strictTopology)
						continue;
				}
				else
				{
					var delta = ScriptableObject.CreateInstance<MeshDeltaAsset>();
					delta.targetMeshName = kvp.Key;
					delta.vertexCount = baseMesh.vertexCount;
					delta.positionDeltas = new Vector3[delta.vertexCount];
					delta.normalDeltas = new Vector3[delta.vertexCount];
					delta.tangentDeltas = new Vector3[delta.vertexCount];

					var baseVerts = baseMesh.vertices;
					var baseNormals = baseMesh.normals;
					var baseTangents = baseMesh.tangents;
					var modVerts = modMesh.vertices;
					var modNormals = modMesh.normals;
					var modTangents = modMesh.tangents;

					for (int i = 0; i < delta.vertexCount; i++)
					{
						delta.positionDeltas[i] = i < modVerts.Length && i < baseVerts.Length ? (modVerts[i] - baseVerts[i]) : Vector3.zero;
						delta.normalDeltas[i] = (i < modNormals.Length && i < baseNormals.Length) ? (modNormals[i] - baseNormals[i]) : Vector3.zero;
						var bt = (i < baseTangents.Length) ? baseTangents[i] : Vector4.zero;
						var mt = (i < modTangents.Length) ? modTangents[i] : Vector4.zero;
						delta.tangentDeltas[i] = new Vector3(mt.x - bt.x, mt.y - bt.y, mt.z - bt.z);
					}

					string savePath = GetTempAuthoringAssetPath($"MeshDelta_{SanitizeFileName(kvp.Key)}.asset");
					AssetDatabase.CreateAsset(delta, savePath);
					result.generatedSidecars.Add(delta);
					result.generatedSidecarPaths.Add(savePath);

					var op = new PatchPackage.MeshDeltaOp
					{
						targetMeshName = kvp.Key,
						meshDelta = delta
					};
					patch.ops.Add(op);
				}

				// UV channels: if modified has extra channel, capture it (simple MVP)
				int baseUvChannels = CountUvChannels(baseMesh);
				int modUvChannels = CountUvChannels(modMesh);
				for (int ch = 0; ch < 8; ch++)
				{
					bool baseHas = baseUvChannels > ch;
					bool modHas = modUvChannels > ch;
					if (!baseHas && modHas)
					{
						var uvAsset = ScriptableObject.CreateInstance<UVLayerAsset>();
						uvAsset.targetMeshName = kvp.Key;
						uvAsset.channel = ch;
						uvAsset.uvs = GetUvs(modMesh, ch);

						string uvPath = GetTempAuthoringAssetPath($"UVLayer_ch{ch}_{SanitizeFileName(kvp.Key)}.asset");
						AssetDatabase.CreateAsset(uvAsset, uvPath);
						result.generatedSidecars.Add(uvAsset);
						result.generatedSidecarPaths.Add(uvPath);

						var op = new PatchPackage.UVLayerOp
						{
							targetMeshName = kvp.Key,
							channel = ch,
							uvLayer = uvAsset,
							replaceExisting = false
						};
						patch.ops.Add(op);
					}
				}

				// Blendshape scale or frame synthesis (simple: if bs exists in both, record scale 1; if only in modified, capture frame at 100 weight from modified)
				var baseBs = GetBlendshapeSet(baseMesh);
				var modBs = GetBlendshapeSet(modMesh);
				foreach (var name in modBs)
				{
					if (baseBs.Contains(name))
					{
						patch.ops.Add(new PatchPackage.BlendshapeOp
						{
							targetMeshName = kvp.Key,
							blendshapeName = name,
							scale = 1f,
							synthesizedFrame = null
						});
					}
					else
					{
						var frame = ExtractBlendshapeFrame(modMesh, name, 0);
						if (frame != null)
						{
							frame.targetMeshName = kvp.Key;
							string fpath = GetTempAuthoringAssetPath($"Blendshape_{SanitizeFileName(name)}_{SanitizeFileName(kvp.Key)}.asset");
							AssetDatabase.CreateAsset(frame, fpath);
							result.generatedSidecars.Add(frame);
							result.generatedSidecarPaths.Add(fpath);

							patch.ops.Add(new PatchPackage.BlendshapeOp
							{
								targetMeshName = kvp.Key,
								blendshapeName = name,
								scale = 1f,
								synthesizedFrame = frame
							});
						}
					}
				}
			}

			// Material overrides are author-selected accessories; for MVP we defer to accessories list (handled by exporter UI)
			return result;
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

		private static int CountUvChannels(Mesh mesh)
		{
			int channels = 0;
			if (mesh.uv != null && mesh.uv.Length == mesh.vertexCount) channels++;
			if (mesh.uv2 != null && mesh.uv2.Length == mesh.vertexCount) channels++;
			if (mesh.uv3 != null && mesh.uv3.Length == mesh.vertexCount) channels++;
			if (mesh.uv4 != null && mesh.uv4.Length == mesh.vertexCount) channels++;
#if UNITY_2019_4_OR_NEWER
			if (mesh.uv5 != null && mesh.uv5.Length == mesh.vertexCount) channels++;
			if (mesh.uv6 != null && mesh.uv6.Length == mesh.vertexCount) channels++;
			if (mesh.uv7 != null && mesh.uv7.Length == mesh.vertexCount) channels++;
			if (mesh.uv8 != null && mesh.uv8.Length == mesh.vertexCount) channels++;
#endif
			return channels;
		}

		private static Vector2[] GetUvs(Mesh mesh, int channel)
		{
			switch (channel)
			{
				case 0: return mesh.uv;
				case 1: return mesh.uv2;
				case 2: return mesh.uv3;
				case 3: return mesh.uv4;
#if UNITY_2019_4_OR_NEWER
				case 4: return mesh.uv5;
				case 5: return mesh.uv6;
				case 6: return mesh.uv7;
				case 7: return mesh.uv8;
#endif
				default: return new Vector2[0];
			}
		}

		private static HashSet<string> GetBlendshapeSet(Mesh mesh)
		{
			var set = new HashSet<string>();
			int count = mesh.blendShapeCount;
			for (int i = 0; i < count; i++)
				set.Add(mesh.GetBlendShapeName(i));
			return set;
		}

		private static BlendshapeFrameAsset ExtractBlendshapeFrame(Mesh mesh, string name, int frameIndex)
		{
			int idx = -1;
			for (int i = 0; i < mesh.blendShapeCount; i++)
				if (mesh.GetBlendShapeName(i) == name) { idx = i; break; }
			if (idx < 0) return null;

			var frame = ScriptableObject.CreateInstance<BlendshapeFrameAsset>();
			frame.blendshapeName = name;
			frame.frameWeight = 100f;
			frame.deltaVertices = new Vector3[mesh.vertexCount];
			frame.deltaNormals = new Vector3[mesh.vertexCount];
			frame.deltaTangents = new Vector3[mesh.vertexCount];

			var dv = new Vector3[mesh.vertexCount];
			var dn = new Vector3[mesh.vertexCount];
			var dt = new Vector3[mesh.vertexCount];
			mesh.GetBlendShapeFrameVertices(idx, frameIndex, dv, dn, dt);
			for (int i = 0; i < mesh.vertexCount; i++)
			{
				frame.deltaVertices[i] = dv[i];
				frame.deltaNormals[i] = dn[i];
				frame.deltaTangents[i] = dt[i];
			}
			return frame;
		}

		private static string GetTempAuthoringAssetPath(string fileName)
		{
			// Use Packages/com.yucp.temp/Patches for patch assets
			// Folders should already exist (created by EnsureAuthoringFolder)
			string dir = "Packages/com.yucp.temp/Patches";
			
			// Verify physical folder exists (more reliable than IsValidFolder for Packages)
			string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			string physicalPath = Path.Combine(projectPath, dir.Replace('/', Path.DirectorySeparatorChar));
			
			if (!Directory.Exists(physicalPath))
			{
				throw new InvalidOperationException($"Folder {dir} does not exist. Ensure EnsureAuthoringFolder() was called first.");
			}
			
			return $"{dir}/{fileName}";
		}

		private static string SanitizeFileName(string name)
		{
			foreach (char c in System.IO.Path.GetInvalidFileNameChars())
				name = name.Replace(c, '_');
			return name;
		}
		
		/// <summary>
		/// Builds a DerivedFbxAsset with all data embedded (no separate sidecar assets).
		/// </summary>
		public static DerivedFbxAsset BuildDerivedFbxAsset(string baseFbxPath, string modifiedFbxPath, DerivedFbxAsset.Policy policy, DerivedFbxAsset.UIHints hints, DerivedFbxAsset.SeedMaps seeds)
		{
			var v1 = ManifestBuilder.BuildForFbx(baseFbxPath);
			var v2 = ManifestBuilder.BuildForFbx(modifiedFbxPath);
			var map = MapBuilder.Build(v1, v2, ConvertSeedMaps(seeds));
			
			var asset = ScriptableObject.CreateInstance<DerivedFbxAsset>();
			asset.sourceManifestId = v1.manifestId;
			asset.policy = policy;
			asset.uiHints = hints;
			asset.seedMaps = seeds ?? new DerivedFbxAsset.SeedMaps();
			asset.targetFbxName = hints.friendlyName;
			
			// Load meshes for both FBXs
			var baseMeshes = LoadMeshesByName(baseFbxPath);
			var modMeshes = LoadMeshesByName(modifiedFbxPath);
			
			// Mesh deltas (same name, same vertex count)
			foreach (var kvp in map.meshMap)
			{
				if (!baseMeshes.TryGetValue(kvp.Key, out var baseMesh) || !modMeshes.TryGetValue(kvp.Value, out var modMesh))
					continue;
				
				if (baseMesh.vertexCount != modMesh.vertexCount)
				{
					if (policy.strictTopology)
						continue;
				}
				else
				{
					// Create embedded mesh delta operation
					var op = new DerivedFbxAsset.EmbeddedMeshDeltaOp
					{
						targetMeshName = kvp.Key,
						vertexCount = baseMesh.vertexCount,
						positionDeltas = new Vector3[baseMesh.vertexCount],
						normalDeltas = new Vector3[baseMesh.vertexCount],
						tangentDeltas = new Vector3[baseMesh.vertexCount]
					};
					
					var baseVerts = baseMesh.vertices;
					var baseNormals = baseMesh.normals;
					var baseTangents = baseMesh.tangents;
					var modVerts = modMesh.vertices;
					var modNormals = modMesh.normals;
					var modTangents = modMesh.tangents;
					
					for (int i = 0; i < op.vertexCount; i++)
					{
						op.positionDeltas[i] = i < modVerts.Length && i < baseVerts.Length ? (modVerts[i] - baseVerts[i]) : Vector3.zero;
						op.normalDeltas[i] = (i < modNormals.Length && i < baseNormals.Length) ? (modNormals[i] - baseNormals[i]) : Vector3.zero;
						var bt = (i < baseTangents.Length) ? baseTangents[i] : Vector4.zero;
						var mt = (i < modTangents.Length) ? modTangents[i] : Vector4.zero;
						op.tangentDeltas[i] = new Vector3(mt.x - bt.x, mt.y - bt.y, mt.z - bt.z);
					}
					
					asset.operations.Add(op);
				}
				
				// UV channels: if modified has extra channel, capture it
				int baseUvChannels = CountUvChannels(baseMesh);
				int modUvChannels = CountUvChannels(modMesh);
				for (int ch = 0; ch < 8; ch++)
				{
					bool baseHas = baseUvChannels > ch;
					bool modHas = modUvChannels > ch;
					if (!baseHas && modHas)
					{
						var uvOp = new DerivedFbxAsset.EmbeddedUVLayerOp
						{
							targetMeshName = kvp.Key,
							channel = ch,
							uvs = GetUvs(modMesh, ch),
							replaceExisting = false
						};
						asset.operations.Add(uvOp);
					}
				}
				
				// Blendshape operations - extract full frame data for ALL blendshapes in modified FBX
				// Use sparse storage (only non-zero deltas) like BlendShare for efficiency
				var baseBs = GetBlendshapeSet(baseMesh);
				var modBs = GetBlendshapeSet(modMesh);
				foreach (var name in modBs)
				{
					// Always extract full blendshape frame from modified FBX (as user requested)
						var frame = ExtractBlendshapeFrame(modMesh, name, 0);
						if (frame != null)
					{
						// Validate array sizes to prevent serialization issues
						if (frame.deltaVertices != null && frame.deltaVertices.Length == modMesh.vertexCount)
						{
							var bsOp = new DerivedFbxAsset.EmbeddedBlendshapeOp
							{
								targetMeshName = kvp.Key,
								blendshapeName = name,
								scale = 1f,
								frameWeight = frame.frameWeight,
								vertexIndices = new List<int>(),
								deltaVertices = new List<Vector3>(),
								normalIndices = new List<int>(),
								deltaNormals = new List<Vector3>(),
								tangentIndices = new List<int>(),
								deltaTangents = new List<Vector3>()
							};
							
							// Convert full arrays to sparse storage (only non-zero deltas)
							for (int i = 0; i < modMesh.vertexCount; i++)
							{
								if (frame.deltaVertices[i] != Vector3.zero)
								{
									bsOp.vertexIndices.Add(i);
									bsOp.deltaVertices.Add(frame.deltaVertices[i]);
								}
								
								if (frame.deltaNormals != null && frame.deltaNormals.Length == modMesh.vertexCount && frame.deltaNormals[i] != Vector3.zero)
								{
									bsOp.normalIndices.Add(i);
									bsOp.deltaNormals.Add(frame.deltaNormals[i]);
								}
								
								if (frame.deltaTangents != null && frame.deltaTangents.Length == modMesh.vertexCount && frame.deltaTangents[i] != Vector3.zero)
								{
									bsOp.tangentIndices.Add(i);
									bsOp.deltaTangents.Add(frame.deltaTangents[i]);
								}
							}
							
							asset.operations.Add(bsOp);
						}
						else
						{
							Debug.LogWarning($"[PatchBuilder] Skipping blendshape {name} for mesh {kvp.Key} - vertex count mismatch (expected {modMesh.vertexCount}, got {frame.deltaVertices?.Length ?? 0})");
						}
							
							// Clean up temporary frame asset
							UnityEngine.Object.DestroyImmediate(frame);
						}
					}
				}
			
			// Capture bone hierarchy changes
			CaptureBoneHierarchyChanges(v1, v2, asset);
			
			// Capture material assignment changes
			CaptureMaterialAssignmentChanges(v1, v2, asset);
			
			// Capture mesh reorganization changes
			CaptureMeshReorganizationChanges(v1, v2, asset);
			
			return asset;
		}
		
		private static void CaptureBoneHierarchyChanges(ManifestBuilder.Manifest v1, ManifestBuilder.Manifest v2, DerivedFbxAsset asset)
		{
			var baseBones = v1.bones.ToDictionary(b => b.path);
			var modBones = v2.bones.ToDictionary(b => b.path);
			
			foreach (var modBone in v2.bones)
			{
				if (baseBones.TryGetValue(modBone.path, out var baseBone))
				{
					// Check if transform changed
					if (baseBone.localPosition != modBone.localPosition ||
					    baseBone.localRotation != modBone.localRotation ||
					    baseBone.localScale != modBone.localScale)
					{
						asset.operations.Add(new DerivedFbxAsset.EmbeddedBoneHierarchyOp
						{
							bonePath = modBone.path,
							localPosition = modBone.localPosition,
							localRotation = modBone.localRotation,
							localScale = modBone.localScale,
							parentBonePath = modBone.parentPath
						});
					}
				}
				else
				{
					// New bone
					asset.operations.Add(new DerivedFbxAsset.EmbeddedBoneHierarchyOp
					{
						bonePath = modBone.path,
						localPosition = modBone.localPosition,
						localRotation = modBone.localRotation,
						localScale = modBone.localScale,
						parentBonePath = modBone.parentPath
					});
				}
			}
		}
		
		private static void CaptureMaterialAssignmentChanges(ManifestBuilder.Manifest v1, ManifestBuilder.Manifest v2, DerivedFbxAsset asset)
		{
			var baseRenderers = v1.renderers.ToDictionary(r => r.path);
			var modRenderers = v2.renderers.ToDictionary(r => r.path);
			
			foreach (var modRenderer in v2.renderers)
			{
				if (baseRenderers.TryGetValue(modRenderer.path, out var baseRenderer))
				{
					// Check if materials changed
					bool materialsChanged = false;
					if (baseRenderer.materialNames == null || modRenderer.materialNames == null ||
					    baseRenderer.materialNames.Length != modRenderer.materialNames.Length)
					{
						materialsChanged = true;
					}
					else
					{
						for (int i = 0; i < baseRenderer.materialNames.Length; i++)
						{
							if (baseRenderer.materialNames[i] != modRenderer.materialNames[i])
							{
								materialsChanged = true;
								break;
							}
						}
					}
					
					if (materialsChanged)
					{
						asset.operations.Add(new DerivedFbxAsset.EmbeddedMaterialAssignmentOp
						{
							rendererPath = modRenderer.path,
							materialNames = modRenderer.materialNames,
							materialGuids = modRenderer.materialGuids
						});
					}
				}
				else
				{
					// New renderer
					asset.operations.Add(new DerivedFbxAsset.EmbeddedMaterialAssignmentOp
					{
						rendererPath = modRenderer.path,
						materialNames = modRenderer.materialNames,
						materialGuids = modRenderer.materialGuids
					});
				}
			}
		}
		
		private static void CaptureMeshReorganizationChanges(ManifestBuilder.Manifest v1, ManifestBuilder.Manifest v2, DerivedFbxAsset asset)
		{
			var baseMeshes = v1.meshes.ToDictionary(m => m.name);
			var modMeshes = v2.meshes.ToDictionary(m => m.name);
			
			// Check for removed meshes
			foreach (var baseMesh in v1.meshes)
			{
				if (!modMeshes.ContainsKey(baseMesh.name))
				{
					asset.operations.Add(new DerivedFbxAsset.EmbeddedMeshReorganizationOp
					{
						type = DerivedFbxAsset.EmbeddedMeshReorganizationOp.ReorganizationType.Remove,
						meshName = baseMesh.name,
						meshPath = ""
					});
				}
			}
			
			// Check for new meshes
			foreach (var modMesh in v2.meshes)
			{
				if (!baseMeshes.ContainsKey(modMesh.name))
				{
					asset.operations.Add(new DerivedFbxAsset.EmbeddedMeshReorganizationOp
					{
						type = DerivedFbxAsset.EmbeddedMeshReorganizationOp.ReorganizationType.Add,
						meshName = modMesh.name,
						meshPath = ""
					});
				}
			}
		}
		
		private static PatchPackage.SeedMaps ConvertSeedMaps(DerivedFbxAsset.SeedMaps seeds)
		{
			if (seeds == null) return new PatchPackage.SeedMaps();
			
			var result = new PatchPackage.SeedMaps();
			foreach (var pair in seeds.boneAliases)
				result.boneAliases.Add(new PatchPackage.StringPair { from = pair.from, to = pair.to });
			foreach (var pair in seeds.materialAliases)
				result.materialAliases.Add(new PatchPackage.StringPair { from = pair.from, to = pair.to });
			foreach (var pair in seeds.blendshapeAliases)
				result.blendshapeAliases.Add(new PatchPackage.StringPair { from = pair.from, to = pair.to });
			
			return result;
		}
	}
}


