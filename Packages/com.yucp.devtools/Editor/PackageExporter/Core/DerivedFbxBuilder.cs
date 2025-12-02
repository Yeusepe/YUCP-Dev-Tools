using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Builds derived FBX files using HDiffPatch binary patching.
	/// Adapted from CocoTools CocoPatch.cs and CocoUtils.cs implementation.
	/// </summary>
	public static class DerivedFbxBuilder
	{
		/// <summary>
		/// Builds a derived FBX by applying a binary patch to the base FBX.
		/// Adapted from CocoTools CocoPatch.cs ExecuteProcess() method.
		/// </summary>
		public static string BuildDerivedFbx(string baseFbxPath, DerivedFbxAsset derivedAsset, string outputPath, string targetGuid)
		{
			if (string.IsNullOrEmpty(baseFbxPath) || derivedAsset == null)
			{
				Debug.LogError("[DerivedFbxBuilder] Invalid inputs: baseFbxPath or derivedAsset is null");
				return null;
			}
			
			if (string.IsNullOrEmpty(derivedAsset.hdiffFilePath))
			{
				Debug.LogError("[DerivedFbxBuilder] DerivedFbxAsset has no hdiffFilePath. Cannot apply patch.");
				return null;
			}
			
			string fbxPath = outputPath;
			if (!fbxPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
				fbxPath += ".fbx";
			
			try
			{
				// Verify base FBX exists
				if (!File.Exists(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), baseFbxPath.Replace('/', Path.DirectorySeparatorChar))))
			{
					Debug.LogError($"[DerivedFbxBuilder] Base FBX not found at: {baseFbxPath}");
					return null;
				}
				
				// Verify base FBX matches expected hash/manifest
				if (!VerifyBaseFbx(baseFbxPath, derivedAsset))
				{
					Debug.LogError($"[DerivedFbxBuilder] Base FBX verification failed. The base FBX may not match the expected version.");
					return null;
				}
			
				// Get physical paths
				// Adapted from CocoTools CocoPatch.cs path handling
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				string basePhysicalPath = Path.Combine(projectPath, baseFbxPath.Replace('/', Path.DirectorySeparatorChar));
				string hdiffPhysicalPath = Path.Combine(projectPath, derivedAsset.hdiffFilePath.Replace('/', Path.DirectorySeparatorChar));
				string outputPhysicalPath = Path.Combine(projectPath, fbxPath.Replace('/', Path.DirectorySeparatorChar));
				
				// Ensure output directory exists
				Directory.CreateDirectory(Path.GetDirectoryName(outputPhysicalPath));
				
				// Apply binary patch
				// Adapted from CocoTools CocoPatch.cs hpatch_unity call
				var patchResult = HDiffPatchWrapper.ApplyPatch(
					basePhysicalPath,
					hdiffPhysicalPath,
					outputPhysicalPath,
					(str) => Debug.Log($"[DerivedFbxBuilder] HPatch: {str}"),
					(str) => Debug.LogError($"[DerivedFbxBuilder] HPatch Error: {str}")
				);
				
				if (patchResult != THPatchResult.HPATCH_SUCCESS)
				{
					Debug.LogError($"[DerivedFbxBuilder] Failed to apply binary patch: {patchResult}");
				return null;
				}
				
				if (!File.Exists(outputPhysicalPath))
				{
					Debug.LogError($"[DerivedFbxBuilder] Patched FBX file was not created at: {outputPhysicalPath}");
				return null;
			}
			
				// Handle meta file (GUID preservation)
				TryCopyMetaWithGuid(outputPhysicalPath, derivedAsset?.originalDerivedFbxPath, baseFbxPath, targetGuid);
				
				// Import the patched FBX
				// Adapted from CocoTools CocoUtils.ForceOverwrite() AssetDatabase.ImportAsset call
				AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
			AssetDatabase.Refresh();
			
				Debug.Log($"[DerivedFbxBuilder] Successfully created patched FBX: {fbxPath}");
				
				return fbxPath;
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"[DerivedFbxBuilder] Error applying binary patch: {ex.Message}\n{ex.StackTrace}");
			return null;
		}
		}
		
		/// <summary>
		/// Verifies that the base FBX matches the expected version.
		/// Checks file hash and/or manifest ID.
		/// </summary>
		private static bool VerifyBaseFbx(string baseFbxPath, DerivedFbxAsset derivedAsset)
		{
			try
			{
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				string basePhysicalPath = Path.Combine(projectPath, baseFbxPath.Replace('/', Path.DirectorySeparatorChar));
				
				if (!File.Exists(basePhysicalPath))
				{
					Debug.LogError($"[DerivedFbxBuilder] Base FBX file does not exist: {basePhysicalPath}");
					return false;
				}
				
				// Check hash if available
				if (!string.IsNullOrEmpty(derivedAsset.baseFbxHash))
				{
					string computedHash = ComputeFileHash(basePhysicalPath);
					if (computedHash != derivedAsset.baseFbxHash)
					{
						Debug.LogError($"[DerivedFbxBuilder] Base FBX hash mismatch. Expected: {derivedAsset.baseFbxHash}, Got: {computedHash}");
						return false;
					}
					Debug.Log($"[DerivedFbxBuilder] Base FBX hash verification passed: {computedHash}");
				}
				
				// Check manifest ID if available
				if (!string.IsNullOrEmpty(derivedAsset.sourceManifestId))
				{
					var manifest = ManifestBuilder.BuildForFbx(baseFbxPath);
					if (manifest.manifestId != derivedAsset.sourceManifestId)
					{
						Debug.LogWarning($"[DerivedFbxBuilder] Base FBX manifest ID mismatch. Expected: {derivedAsset.sourceManifestId}, Got: {manifest.manifestId}. Continuing anyway...");
						// Continue on manifest mismatch, just warn
				}
				else
				{
						Debug.Log($"[DerivedFbxBuilder] Base FBX manifest ID verification passed: {manifest.manifestId}");
					}
				}
				
				// Check GUID if available
				if (!string.IsNullOrEmpty(derivedAsset.baseFbxGuid))
				{
					string baseGuid = AssetDatabase.AssetPathToGUID(baseFbxPath);
					if (baseGuid != derivedAsset.baseFbxGuid)
					{
						Debug.LogWarning($"[DerivedFbxBuilder] Base FBX GUID mismatch. Expected: {derivedAsset.baseFbxGuid}, Got: {baseGuid}. Continuing anyway...");
						// Continue on GUID mismatch, just warn
				}
				else
				{
						Debug.Log($"[DerivedFbxBuilder] Base FBX GUID verification passed: {baseGuid}");
					}
				}
				
				return true;
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"[DerivedFbxBuilder] Error verifying base FBX: {ex.Message}");
				return false;
			}
		}
		
		/// <summary>
		/// Computes MD5 hash of a file.
		/// </summary>
		private static string ComputeFileHash(string filePath)
		{
			using (var md5 = MD5.Create())
			{
				using (var stream = File.OpenRead(filePath))
				{
					byte[] hash = md5.ComputeHash(stream);
					return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
				}
			}
		}
		
		/// <summary>
		/// Copies meta file with GUID preservation.
		/// Adapted from CocoTools CocoUtils.ForceOverwrite() approach.
		/// </summary>
		private static void TryCopyMetaWithGuid(string physicalOutputPath, string originalDerivedFbxPath, string baseFbxPath, string targetGuid)
		{
			string outputMetaPath = physicalOutputPath + ".meta";
			string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			
			// Try to copy from original derived FBX meta first
			if (!string.IsNullOrEmpty(originalDerivedFbxPath))
			{
				try
				{
					string originalPhysical = Path.Combine(projectPath, originalDerivedFbxPath.Replace('/', Path.DirectorySeparatorChar));
					string originalMeta = originalPhysical + ".meta";
					
					if (File.Exists(originalMeta))
					{
						string metaContent = File.ReadAllText(originalMeta);
			if (!string.IsNullOrEmpty(targetGuid))
			{
							metaContent = System.Text.RegularExpressions.Regex.Replace(
								metaContent,
								@"guid:\s*[a-f0-9]{32}",
								$"guid: {targetGuid}",
								System.Text.RegularExpressions.RegexOptions.IgnoreCase
							);
						}
						
						File.WriteAllText(outputMetaPath, metaContent);
						Debug.Log($"[DerivedFbxBuilder] Copied original derived FBX .meta from '{originalDerivedFbxPath}' to output");
						return;
					}
				}
				catch (System.Exception ex)
				{
					Debug.LogWarning($"[DerivedFbxBuilder] Failed to copy original .meta: {ex.Message}");
				}
			}
			
			// Fall back to base FBX meta
			if (!string.IsNullOrEmpty(baseFbxPath))
			{
				try
				{
					string basePhysical = Path.Combine(projectPath, baseFbxPath.Replace('/', Path.DirectorySeparatorChar));
					string baseMeta = basePhysical + ".meta";
					
					if (File.Exists(baseMeta))
					{
						string metaContent = File.ReadAllText(baseMeta);
						if (!string.IsNullOrEmpty(targetGuid))
						{
							metaContent = System.Text.RegularExpressions.Regex.Replace(
								metaContent,
								@"guid:\s*[a-f0-9]{32}",
								$"guid: {targetGuid}",
								System.Text.RegularExpressions.RegexOptions.IgnoreCase
							);
						}
						
						File.WriteAllText(outputMetaPath, metaContent);
						Debug.Log($"[DerivedFbxBuilder] Copied base FBX .meta from '{baseFbxPath}' to output");
						return;
					}
				}
				catch (System.Exception ex)
				{
					Debug.LogWarning($"[DerivedFbxBuilder] Failed to copy base .meta: {ex.Message}");
				}
			}
			
			// If no meta file found, create one with target GUID
			if (!string.IsNullOrEmpty(targetGuid))
			{
				try
				{
					// Use MetaFileManager if available, otherwise create basic meta
					var metaFileManagerType = System.Type.GetType("YUCP.DevTools.Editor.PackageExporter.MetaFileManager, yucp.devtools.Editor");
					if (metaFileManagerType != null)
					{
						var writeGuidMethod = metaFileManagerType.GetMethod("WriteGuid", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
						if (writeGuidMethod != null)
						{
							writeGuidMethod.Invoke(null, new object[] { physicalOutputPath, targetGuid });
							Debug.Log($"[DerivedFbxBuilder] Created .meta file with GUID: {targetGuid}");
				return;
			}
				}
			}
			catch (System.Exception ex)
			{
					Debug.LogWarning($"[DerivedFbxBuilder] Failed to create .meta file: {ex.Message}");
				}
			}
		}
	}
}
