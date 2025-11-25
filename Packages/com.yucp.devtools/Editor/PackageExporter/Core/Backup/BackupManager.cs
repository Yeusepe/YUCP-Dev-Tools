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
			public string baseGuid;
			public float autoApplyThreshold = 0.8f;
			public float reviewThreshold = 0.4f;
			public bool strictTopology = false;
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
						if (settings.isDerived && settings.baseGuid == baseGuid)
						{
							// Found a modified FBX for this base - we're in export mode, skip backups
							Debug.Log($"[BackupManager] Skipping backup - modified FBX exists ({modelPath}), we're in export mode");
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
				Debug.Log($"[BackupManager] No prefabs reference {baseFbxPath}, skipping backup");
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

			foreach (var guid in allPrefabGuids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var deps = AssetDatabase.GetDependencies(path, true);
				if (deps.Contains(baseFbxPath))
				{
					string destPath = AssetDatabase.GenerateUniqueAssetPath($"{patchedDir}/{Path.GetFileNameWithoutExtension(path)}_Patched.prefab");
					if (AssetDatabase.CopyAsset(path, destPath))
					{
						created.Add(destPath);
					}
				}
			}
			AssetDatabase.Refresh();
			return created;
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


