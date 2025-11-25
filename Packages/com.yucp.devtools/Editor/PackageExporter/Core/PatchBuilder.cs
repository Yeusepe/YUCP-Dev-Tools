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
	}
}


