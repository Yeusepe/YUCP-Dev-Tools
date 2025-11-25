using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Builds immutable FBX manifests and persists them under Library/YUCP/Manifests/<hash>.json.
	/// </summary>
	public static class ManifestBuilder
	{
		[Serializable]
		public class Manifest
		{
			public string manifestId;
			public string assetGuid;
			public string assetPath;
			public string units;
			public string axis;
			public List<MeshInfo> meshes = new List<MeshInfo>();
			public List<MaterialInfo> materials = new List<MaterialInfo>();
			public List<string> blendshapeNames = new List<string>();
			public List<string> animationClips = new List<string>();
		}

		[Serializable]
		public class MeshInfo
		{
			public string name;
			public int vertexCount;
			public int subMeshCount;
			public int uvChannels;
		}

		[Serializable]
		public class MaterialInfo
		{
			public string name;
			public string shaderName;
		}

		public static Manifest BuildForFbx(string fbxAssetPath)
		{
			var manifest = new Manifest
			{
				assetPath = fbxAssetPath,
				assetGuid = AssetDatabase.AssetPathToGUID(fbxAssetPath),
				units = "meters",
				axis = "YUp"
			};

			var assets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
			foreach (var obj in assets)
			{
				if (obj is Mesh mesh)
				{
					var info = new MeshInfo
					{
						name = mesh.name,
						vertexCount = mesh.vertexCount,
						subMeshCount = mesh.subMeshCount,
						uvChannels = CountUvChannels(mesh)
					};
					manifest.meshes.Add(info);
				}
				else if (obj is Material mat)
				{
					manifest.materials.Add(new MaterialInfo
					{
						name = mat.name,
						shaderName = mat.shader != null ? mat.shader.name : ""
					});
				}
				else if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
				{
					manifest.animationClips.Add(clip.name);
				}
			}

			// Blendshape names (from meshes)
			foreach (var obj in assets)
			{
				if (obj is Mesh mesh)
				{
					int count = mesh.blendShapeCount;
					for (int i = 0; i < count; i++)
					{
						string bs = mesh.GetBlendShapeName(i);
						if (!manifest.blendshapeNames.Contains(bs))
							manifest.blendshapeNames.Add(bs);
					}
				}
			}

			// Compute manifestId as a stable hash of summarized content
			string summary = $"{manifest.assetGuid}|{string.Join(",", manifest.meshes.Select(m => $"{m.name}:{m.vertexCount}:{m.subMeshCount}:{m.uvChannels}"))}|{string.Join(",", manifest.materials.Select(m => $"{m.name}:{m.shaderName}"))}|{string.Join(",", manifest.blendshapeNames)}|{string.Join(",", manifest.animationClips)}";
			manifest.manifestId = ComputeHash(summary);

			Persist(manifest);
			return manifest;
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

		private static string ComputeHash(string input)
		{
			using (var sha = System.Security.Cryptography.SHA256.Create())
			{
				var bytes = System.Text.Encoding.UTF8.GetBytes(input);
				var hashBytes = sha.ComputeHash(bytes);
				return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
			}
		}

		private static void Persist(Manifest manifest)
		{
			string libraryDir = Path.Combine("Library", "YUCP", "Manifests");
			if (!Directory.Exists(libraryDir))
				Directory.CreateDirectory(libraryDir);

			string path = Path.Combine(libraryDir, $"{manifest.manifestId}.json");
			var json = JsonUtility.ToJson(manifest, true);
			File.WriteAllText(path, json);
		}
	}
}




