using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Generates binary diff patches (.hdiff files) from Base (v1) vs Modified (v2) FBX.
	/// Adapted from CocoTools approach: https://github.com/coco1337/CocoTools
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
		/// Builds a DerivedFbxAsset with binary diff (.hdiff) file.
		/// Adapted from CocoTools CocoDiff.cs ExecuteProcess() method.
		/// </summary>
		public static DerivedFbxAsset BuildDerivedFbxAsset(string baseFbxPath, string modifiedFbxPath, DerivedFbxAsset.Policy policy, DerivedFbxAsset.UIHints hints, DerivedFbxAsset.SeedMaps seeds)
		{
			var v1 = ManifestBuilder.BuildForFbx(baseFbxPath);
			
			var asset = ScriptableObject.CreateInstance<DerivedFbxAsset>();
			asset.sourceManifestId = v1.manifestId;
			asset.policy = policy;
			asset.uiHints = hints;
			asset.seedMaps = seeds ?? new DerivedFbxAsset.SeedMaps();
			asset.targetFbxName = hints.friendlyName;
			
			// Generate .hdiff file
			// Adapted from CocoTools CocoDiff.cs ExecuteProcess() - uses Directory.GetCurrentDirectory()
			string projectPath = Directory.GetCurrentDirectory();
			
			// Get physical paths - CocoTools uses Path.Combine(Directory.GetCurrentDirectory(), AssetDatabase.GetAssetPath(...))
			string basePhysicalPath = Path.Combine(projectPath, baseFbxPath);
			string modifiedPhysicalPath = Path.Combine(projectPath, modifiedFbxPath);
			
			// Normalize paths (ensure they exist)
			basePhysicalPath = Path.GetFullPath(basePhysicalPath);
			modifiedPhysicalPath = Path.GetFullPath(modifiedPhysicalPath);
			
			if (!File.Exists(basePhysicalPath))
			{
				Debug.LogError($"[PatchBuilder] Base FBX file not found: {basePhysicalPath}");
				UnityEngine.Object.DestroyImmediate(asset);
				return null;
			}
			
			if (!File.Exists(modifiedPhysicalPath))
			{
				Debug.LogError($"[PatchBuilder] Modified FBX file not found: {modifiedPhysicalPath}");
				UnityEngine.Object.DestroyImmediate(asset);
				return null;
			}
			
			// Generate output path for .hdiff file
			// Adapted from CocoTools: output in same directory as target file, or in Patches folder
			string hdiffFileName = $"DerivedFbxAsset_{SanitizeFileName(hints.friendlyName)}.hdiff";
			string hdiffDir = GetTempAuthoringAssetPath("");
			string hdiffDirPhysical = Path.GetFullPath(Path.Combine(projectPath, hdiffDir.Replace('/', Path.DirectorySeparatorChar)));
			string hdiffOutputPath = Path.Combine(hdiffDirPhysical, hdiffFileName);
			
			// Ensure directory exists
			Directory.CreateDirectory(hdiffDirPhysical);
			
			// Normalize output path and ensure Windows-style backslashes (DLL may require this)
			hdiffOutputPath = Path.GetFullPath(hdiffOutputPath);
			basePhysicalPath = Path.GetFullPath(basePhysicalPath);
			modifiedPhysicalPath = Path.GetFullPath(modifiedPhysicalPath);
			
			// Delete existing .hdiff file if it exists (HDiffPatch doesn't overwrite by default)
			// Adapted from CocoTools approach - they use temp file then overwrite, but we can just delete
			if (File.Exists(hdiffOutputPath))
			{
				try
				{
					File.Delete(hdiffOutputPath);
					Debug.Log($"[PatchBuilder] Deleted existing .hdiff file: {hdiffOutputPath}");
				}
				catch (System.Exception ex)
				{
					Debug.LogWarning($"[PatchBuilder] Could not delete existing .hdiff file: {ex.Message}");
				}
			}
			
			// Create binary diff using HDiffPatch
			// Adapted from CocoTools CocoDiff.cs ExecuteProcess()
			Debug.Log($"[PatchBuilder] Creating .hdiff file:\n  Base: {basePhysicalPath}\n  Modified: {modifiedPhysicalPath}\n  Output: {hdiffOutputPath}");
			
			var diffResult = HDiffPatchWrapper.CreateDiff(
				basePhysicalPath,
				modifiedPhysicalPath,
				hdiffOutputPath,
				(str) => Debug.Log($"[PatchBuilder] HDiff: {str}"),
				(str) => Debug.LogError($"[PatchBuilder] HDiff Error: {str}")
			);
			
			if (diffResult != THDiffResult.HDIFF_SUCCESS)
			{
				Debug.LogError($"[PatchBuilder] Failed to create .hdiff file: {diffResult}\n" +
					$"  Base path: {basePhysicalPath} (exists: {File.Exists(basePhysicalPath)})\n" +
					$"  Modified path: {modifiedPhysicalPath} (exists: {File.Exists(modifiedPhysicalPath)})\n" +
					$"  Output path: {hdiffOutputPath} (dir exists: {Directory.Exists(Path.GetDirectoryName(hdiffOutputPath))})");
				UnityEngine.Object.DestroyImmediate(asset);
				return null;
			}
			
			if (!File.Exists(hdiffOutputPath))
			{
				Debug.LogError($"[PatchBuilder] .hdiff file was not created at: {hdiffOutputPath}");
				UnityEngine.Object.DestroyImmediate(asset);
				return null;
			}
			
			// Generate .meta file for .hdiff file so Unity recognizes it as an exportable asset
			string hdiffMetaPath = hdiffOutputPath + ".meta";
			if (!File.Exists(hdiffMetaPath))
			{
				try
				{
					string metaGuid = System.Guid.NewGuid().ToString("N");
					string metaContent = "fileFormatVersion: 2\n" +
					                     $"guid: {metaGuid}\n" +
					                     "DefaultImporter:\n" +
					                     "  externalObjects: {}\n" +
					                     "  userData:\n" +
					                     "  assetBundleName:\n" +
					                     "  assetBundleVariant:\n";
					File.WriteAllText(hdiffMetaPath, metaContent);
					Debug.Log($"[PatchBuilder] Created .meta file for .hdiff: {hdiffMetaPath}");
				}
				catch (System.Exception ex)
				{
					Debug.LogWarning($"[PatchBuilder] Could not create .meta file for .hdiff: {ex.Message}");
				}
			}
			
			// Store relative path to .hdiff file
			string hdiffRelativePath = Path.Combine(hdiffDir, hdiffFileName).Replace(Path.DirectorySeparatorChar, '/');
			asset.hdiffFilePath = hdiffRelativePath;
			
			// Extract and embed the meta file content from the original derived FBX
			// This preserves humanoid Avatar mappings and other ModelImporter settings
			ExtractAndEmbedMetaFile(modifiedFbxPath, asset);
			
			// Compute optional base FBX hash for verification
			try
			{
				using (var md5 = System.Security.Cryptography.MD5.Create())
				{
					using (var stream = File.OpenRead(basePhysicalPath))
					{
						byte[] hash = md5.ComputeHash(stream);
						asset.baseFbxHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
					}
				}
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"[PatchBuilder] Could not compute base FBX hash: {ex.Message}");
			}
			
			Debug.Log($"[PatchBuilder] Successfully created .hdiff file: {hdiffRelativePath}");
			
			return asset;
		}
		
		/// <summary>
		/// Extracts and embeds the meta file content from the original derived FBX.
		/// This preserves humanoid Avatar mappings and ModelImporter settings.
		/// The GUID will be replaced when recreating the meta file.
		/// </summary>
		private static void ExtractAndEmbedMetaFile(string modifiedFbxPath, DerivedFbxAsset asset)
		{
			if (string.IsNullOrEmpty(modifiedFbxPath))
			{
				Debug.LogWarning("[PatchBuilder] Cannot extract meta file: modifiedFbxPath is null or empty");
				return;
			}
			
			try
			{
				string projectPath = Directory.GetCurrentDirectory();
				string metaPath = Path.Combine(projectPath, modifiedFbxPath) + ".meta";
				metaPath = Path.GetFullPath(metaPath);
				
				if (!File.Exists(metaPath))
				{
					Debug.LogWarning($"[PatchBuilder] Meta file not found for original derived FBX: {metaPath}. " +
						$"Humanoid Avatar mappings will need to be reconfigured after import.");
					return;
				}
				
				// Read the entire meta file content
				string metaContent = File.ReadAllText(metaPath);
				
				// Remove the GUID line - it will be replaced with the target GUID when recreating
				// This prevents conflicts and ensures the correct GUID is used
				metaContent = System.Text.RegularExpressions.Regex.Replace(
					metaContent,
					@"guid:\s*[a-f0-9]{32}",
					"guid: PLACEHOLDER_GUID",
					System.Text.RegularExpressions.RegexOptions.IgnoreCase
				);
				
				asset.embeddedMetaFileContent = metaContent;
				Debug.Log($"[PatchBuilder] Extracted and embedded meta file content from '{modifiedFbxPath}' (preserves humanoid Avatar mappings)");
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[PatchBuilder] Failed to extract meta file from '{modifiedFbxPath}': {ex.Message}. " +
					$"Humanoid Avatar mappings will need to be reconfigured after import.");
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


