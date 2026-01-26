using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Creates backups of prefabs referencing the FBX and generates simple patched prefab copies.
	/// </summary>
	public static class BackupManager
	{
		[Serializable]
		private class DerivedSettings
		{
			public bool isDerived;
			public List<string> baseGuids = new List<string>();
			public string friendlyName;
			public string category;
		}
		
		public static void BackupPrefabsReferencing(string baseFbxPath, out List<string> backedUpPrefabs)
		{
			backedUpPrefabs = new List<string>();
			
			// Only create backups when patches are being APPLIED, not during export
			// Check if we're in export mode: if a MODIFIED (derived) FBX exists for this base FBX, skip backups
			if (string.IsNullOrEmpty(baseFbxPath) || !File.Exists(baseFbxPath))
			{
				Debug.LogWarning($"[BackupManager] Skipping backup - base FBX path invalid: {baseFbxPath}");
				return;
			}
			
			// Check if there's a modified FBX (derived) that references this base FBX
			// If there is, we're in export mode and should NOT create backups
			var baseGuid = AssetDatabase.AssetPathToGUID(baseFbxPath);
			string[] allModelGuids = AssetDatabase.FindAssets("t:Model");
			foreach (var guid in allModelGuids)
			{
				string modelPath = AssetDatabase.GUIDToAssetPath(guid);
				if (modelPath == baseFbxPath) continue; // Skip the base FBX itself
				
				// Check if this FBX is marked as derived with this base FBX
				var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
				if (importer != null && !string.IsNullOrEmpty(importer.userData))
				{
					try
					{
						var settings = JsonUtility.FromJson<DerivedSettings>(importer.userData);
						if (settings.isDerived && settings.baseGuids != null && settings.baseGuids.Contains(baseGuid))
						{
							// Found a modified FBX for this base - we're in export mode, skip backups
							return;
						}
					}
					catch { }
				}
			}
			
			string[] allPrefabGuids = AssetDatabase.FindAssets("t:Prefab");
			
			// Check if any prefabs actually reference this FBX before creating folders
			bool hasReferencingPrefabs = false;
			foreach (var guid in allPrefabGuids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var deps = AssetDatabase.GetDependencies(path, true);
				if (deps.Contains(baseFbxPath))
				{
					hasReferencingPrefabs = true;
					break;
				}
			}
			
			if (!hasReferencingPrefabs)
			{
				return;
			}

			string backupRoot = EnsureBackupRoot();
			// Use single backup folder per FBX instead of timestamped folders
			string fbxName = Path.GetFileNameWithoutExtension(baseFbxPath);
			string stampFolder = Path.Combine(backupRoot, fbxName);
			
			// Check if folder exists physically
			string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			string physicalPath = Path.Combine(projectPath, stampFolder.Replace('/', Path.DirectorySeparatorChar));
			if (!Directory.Exists(physicalPath))
			{
				Directory.CreateDirectory(physicalPath);
				AssetDatabase.ImportAsset(stampFolder, ImportAssetOptions.ForceSynchronousImport);
				AssetDatabase.Refresh();
			}

			foreach (var guid in allPrefabGuids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var deps = AssetDatabase.GetDependencies(path, true);
				if (deps.Contains(baseFbxPath))
				{
					string relDir = Path.GetDirectoryName(path);
					string destDir = Path.Combine(stampFolder, relDir);
					Directory.CreateDirectory(destDir);

					string destPath = Path.Combine(destDir, Path.GetFileName(path)).Replace("\\", "/");
					if (AssetDatabase.CopyAsset(path, destPath))
					{
						backedUpPrefabs.Add(destPath);
					}
				}
			}
			AssetDatabase.Refresh();
		}

		public static List<string> CreatePatchedPrefabCopies(string baseFbxPath, IEnumerable<UnityEngine.Object> derivedAssets)
		{
			// Simple strategy: duplicate each referring prefab as <Name>_Patched and leave replacement decisions to user for now.
			var created = new List<string>();
			string[] allPrefabGuids = AssetDatabase.FindAssets("t:Prefab");
			string patchedDir = EnsurePatchedRoot();

			// Build a map of base mesh names to patched mesh assets
			var meshMap = new Dictionary<string, Mesh>();
			var baseMeshes = LoadMeshesFromFbx(baseFbxPath);
			foreach (var derivedAsset in derivedAssets)
			{
				if (derivedAsset is Mesh patchedMesh)
				{
					// Extract base mesh name from patched mesh name (e.g., "MeshName_Patched" -> "MeshName")
					string baseName = patchedMesh.name;
					if (baseName.EndsWith("_Patched"))
						baseName = baseName.Substring(0, baseName.Length - "_Patched".Length);
					else if (baseName.EndsWith("_UV"))
						baseName = baseName.Substring(0, baseName.Length - "_UV".Length);
					else if (baseName.EndsWith("_BS"))
						baseName = baseName.Substring(0, baseName.Length - "_BS".Length);
					
					// Find matching base mesh
					foreach (var kvp in baseMeshes)
					{
						if (kvp.Key == baseName || patchedMesh.name.StartsWith(kvp.Key))
						{
							meshMap[kvp.Key] = patchedMesh;
							break;
						}
					}
				}
			}

			foreach (var guid in allPrefabGuids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var deps = AssetDatabase.GetDependencies(path, true);
				if (deps.Contains(baseFbxPath))
				{
					string destPath = AssetDatabase.GenerateUniqueAssetPath($"{patchedDir}/{Path.GetFileNameWithoutExtension(path)}_Patched.prefab");
					if (AssetDatabase.CopyAsset(path, destPath))
					{
						// Update mesh references in the copied prefab
						UpdatePrefabMeshReferences(destPath, baseFbxPath, meshMap);
						created.Add(destPath);
					}
				}
			}
			AssetDatabase.Refresh();
			return created;
		}
		
		private static Dictionary<string, Mesh> LoadMeshesFromFbx(string fbxPath)
		{
			var dict = new Dictionary<string, Mesh>();
			var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
			foreach (var asset in assets)
			{
				if (asset is Mesh mesh && !dict.ContainsKey(mesh.name))
					dict.Add(mesh.name, mesh);
			}
			return dict;
		}
		
		public static void UpdatePrefabMeshReferences(string prefabPath, string baseFbxPath, Dictionary<string, Mesh> meshMap)
		{
			if (meshMap.Count == 0) return;
			
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
			if (prefab == null) return;
			
			bool modified = false;
			
			// Update MeshFilter components
			var meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
			foreach (var mf in meshFilters)
			{
				if (mf.sharedMesh != null)
				{
					string meshName = mf.sharedMesh.name;
					if (meshMap.TryGetValue(meshName, out var patchedMesh))
					{
						mf.sharedMesh = patchedMesh;
						modified = true;
					}
				}
			}
			
			// Update SkinnedMeshRenderer components
			var skinnedRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			foreach (var smr in skinnedRenderers)
			{
				if (smr.sharedMesh != null)
				{
					string meshName = smr.sharedMesh.name;
					if (meshMap.TryGetValue(meshName, out var patchedMesh))
					{
						smr.sharedMesh = patchedMesh;
						modified = true;
					}
				}
			}
			
			if (modified)
			{
				PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
				AssetDatabase.SaveAssets();
			}
		}

		private static string EnsureBackupRoot()
		{
			// Use Packages/com.yucp.temp/Backups (temp package location)
			// Reuse single backup folder instead of creating timestamped ones
			string tempPackagePath = "Packages/com.yucp.temp";
			string backupPath = $"{tempPackagePath}/Backups";
			
			// Check if folder exists physically (more reliable for Packages)
			string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			string physicalPath = Path.Combine(projectPath, backupPath.Replace('/', Path.DirectorySeparatorChar));
			
			if (!Directory.Exists(physicalPath))
			{
				// Create directories physically
				Directory.CreateDirectory(physicalPath);
				// Import to make Unity recognize it
				AssetDatabase.ImportAsset(backupPath, ImportAssetOptions.ForceSynchronousImport);
				AssetDatabase.Refresh();
			}
			
			return backupPath;
		}

		private static string EnsurePatchedRoot()
		{
			// Use Packages/com.yucp.temp/PatchedPrefabs (temp package location)
			string tempPackagePath = "Packages/com.yucp.temp";
			string patchedPath = $"{tempPackagePath}/PatchedPrefabs";
			
			// Check if folder exists physically (more reliable for Packages)
			string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			string physicalPath = Path.Combine(projectPath, patchedPath.Replace('/', Path.DirectorySeparatorChar));
			
			if (!Directory.Exists(physicalPath))
			{
				// Create directories physically
				Directory.CreateDirectory(physicalPath);
				// Import to make Unity recognize it
				AssetDatabase.ImportAsset(patchedPath, ImportAssetOptions.ForceSynchronousImport);
				AssetDatabase.Refresh();
			}
			
			return patchedPath;
		}
	}
}

