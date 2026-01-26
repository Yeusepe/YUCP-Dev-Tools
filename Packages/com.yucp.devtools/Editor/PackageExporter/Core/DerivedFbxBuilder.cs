using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
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
			if (derivedAsset == null)
			{
				Debug.LogError("[DerivedFbxBuilder] Invalid inputs: derivedAsset is null");
				return null;
			}
			
			if (derivedAsset.entries == null || derivedAsset.entries.Count == 0)
			{
				Debug.LogError("[DerivedFbxBuilder] DerivedFbxAsset has no patch entries. Cannot apply patch.");
				return null;
			}
			
			string fbxPath = outputPath;
			if (!fbxPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
				fbxPath += ".fbx";
			
			try
			{
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				string outputPhysicalPath = ResolvePhysicalPath(projectPath, fbxPath);
				
				// Ensure output directory exists
				Directory.CreateDirectory(Path.GetDirectoryName(outputPhysicalPath));
				
				var entries = derivedAsset.entries;
				if (entries == null || entries.Count == 0)
				{
					Debug.LogError("[DerivedFbxBuilder] No patch entries found.");
					return null;
				}
				
				var shares = new List<byte[]>();
				string encryptedDiffPath = entries[0].hdiffFilePath;
				
				foreach (var entry in entries)
				{
					if (entry == null || string.IsNullOrEmpty(entry.baseGuid) || string.IsNullOrEmpty(entry.shareEnc))
					{
						Debug.LogError("[DerivedFbxBuilder] Invalid patch entry (missing baseGuid/shareEnc).");
						return null;
					}
					
					string basePath = AssetDatabase.GUIDToAssetPath(entry.baseGuid);
					if (string.IsNullOrEmpty(basePath))
					{
						Debug.LogError($"[DerivedFbxBuilder] Base FBX not found for GUID: {entry.baseGuid}");
						return null;
					}
					
					string basePhysicalPath = ResolvePhysicalPath(projectPath, basePath);
					if (!File.Exists(basePhysicalPath))
					{
						Debug.LogError($"[DerivedFbxBuilder] Base FBX file missing: {basePath}");
						return null;
					}
					
					ComputeBaseHashAndMask(basePhysicalPath, out var baseHash, out var mask);
					string baseHashHex = BytesToHex(baseHash);
					if (!string.IsNullOrEmpty(entry.baseHash) && !string.Equals(entry.baseHash, baseHashHex, StringComparison.OrdinalIgnoreCase))
					{
						Debug.LogError($"[DerivedFbxBuilder] Base FBX hash mismatch: {basePath}");
						return null;
					}
					
					byte[] shareEnc = Convert.FromBase64String(entry.shareEnc);
					byte[] share = XorBytes(shareEnc, mask);
					shares.Add(share);
				}
				
				byte[] key = RecoverKey(shares);
				
				string encryptedPhysicalPath = ResolvePhysicalPath(projectPath, encryptedDiffPath);
				if (!File.Exists(encryptedPhysicalPath))
				{
					Debug.LogError($"[DerivedFbxBuilder] Encrypted diff file not found: {encryptedDiffPath}");
					return null;
				}
				
				string canonicalBaseGuid = derivedAsset.canonicalBaseGuid;
				if (string.IsNullOrEmpty(canonicalBaseGuid))
					canonicalBaseGuid = entries[0].baseGuid;
				
				string canonicalBasePath = AssetDatabase.GUIDToAssetPath(canonicalBaseGuid);
				if (string.IsNullOrEmpty(canonicalBasePath))
				{
					Debug.LogError($"[DerivedFbxBuilder] Canonical base FBX not found for GUID: {canonicalBaseGuid}");
					return null;
				}
				
				string canonicalPhysicalPath = ResolvePhysicalPath(projectPath, canonicalBasePath);
				if (!File.Exists(canonicalPhysicalPath))
				{
					Debug.LogError($"[DerivedFbxBuilder] Canonical base FBX file missing: {canonicalBasePath}");
					return null;
				}
				
				string tempDiffPath = Path.Combine(projectPath, "Library", "YUCP", $"patch_{Guid.NewGuid():N}.hdiff");
				if (!DecryptDiffFile(encryptedPhysicalPath, tempDiffPath, key))
				{
					Debug.LogError("[DerivedFbxBuilder] Failed to decrypt diff payload.");
					return null;
				}
				
				// Delete output file if it already exists (HPatch doesn't allow overwriting)
				if (File.Exists(outputPhysicalPath))
				{
					try
					{
						File.SetAttributes(outputPhysicalPath, FileAttributes.Normal);
						File.Delete(outputPhysicalPath);
						Debug.Log($"[DerivedFbxBuilder] Deleted existing output file before patching: {outputPhysicalPath}");
					}
					catch (System.Exception ex)
					{
						Debug.LogWarning($"[DerivedFbxBuilder] Could not delete existing output file (may be locked): {ex.Message}. HPatch may fail.");
					}
				}
				
				var patchResult = HDiffPatchWrapper.ApplyPatch(
					canonicalPhysicalPath,
					tempDiffPath,
					outputPhysicalPath,
					(str) => Debug.Log($"[DerivedFbxBuilder] HPatch: {str}"),
					(str) => Debug.LogError($"[DerivedFbxBuilder] HPatch Error: {str}")
				);
				
				try
				{
					File.Delete(tempDiffPath);
				}
				catch { }
				
				if (patchResult != THPatchResult.HPATCH_SUCCESS)
				{
					Debug.LogWarning($"[DerivedFbxBuilder] Failed to apply binary patch: {patchResult} (base: {canonicalBasePath})");
					return null;
				}
				
				if (!File.Exists(outputPhysicalPath))
				{
					Debug.LogWarning($"[DerivedFbxBuilder] Patched FBX file was not created at: {outputPhysicalPath}");
					return null;
				}
				
				TryCopyMetaWithGuid(outputPhysicalPath, derivedAsset?.originalDerivedFbxPath, canonicalBasePath, targetGuid, derivedAsset);
				
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

		private static bool IsAssetDatabasePath(string path)
		{
			if (string.IsNullOrEmpty(path)) return false;
			return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
			       path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
		}
		
		private static string ResolvePhysicalPath(string projectPath, string path)
		{
			if (string.IsNullOrEmpty(path)) return path;
			string normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
			if (Path.IsPathRooted(normalized))
				return Path.GetFullPath(normalized);
			return Path.GetFullPath(Path.Combine(projectPath, normalized));
		}

		private static void ComputeBaseHashAndMask(string path, out byte[] baseHash, out byte[] mask)
		{
			using (var shaBase = System.Security.Cryptography.SHA256.Create())
			using (var shaMask = System.Security.Cryptography.SHA256.Create())
			using (var fs = File.OpenRead(path))
			{
				byte[] prefix = System.Text.Encoding.UTF8.GetBytes("YUCP|mask|");
				shaMask.TransformBlock(prefix, 0, prefix.Length, null, 0);
				
				byte[] buffer = new byte[64 * 1024];
				int read;
				while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
				{
					shaBase.TransformBlock(buffer, 0, read, null, 0);
					shaMask.TransformBlock(buffer, 0, read, null, 0);
				}
				
				shaBase.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
				shaMask.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
				
				baseHash = shaBase.Hash;
				mask = shaMask.Hash;
			}
		}

		private static string BytesToHex(byte[] data)
		{
			var sb = new System.Text.StringBuilder(data.Length * 2);
			for (int i = 0; i < data.Length; i++)
				sb.Append(data[i].ToString("x2"));
			return sb.ToString();
		}

		private static byte[] XorBytes(byte[] a, byte[] b)
		{
			int len = Math.Min(a.Length, b.Length);
			byte[] result = new byte[len];
			for (int i = 0; i < len; i++)
			{
				result[i] = (byte)(a[i] ^ b[i]);
			}
			return result;
		}

		private static byte[] RecoverKey(List<byte[]> shares)
		{
			if (shares == null || shares.Count == 0)
				return Array.Empty<byte>();
			byte[] key = new byte[shares[0].Length];
			foreach (var share in shares)
			{
				key = XorBytes(key, share);
			}
			return key;
		}

		private static bool DecryptDiffFile(string inputPath, string outputPath, byte[] key)
		{
			try
			{
				byte[] payload = File.ReadAllBytes(inputPath);
				byte[] magic = System.Text.Encoding.ASCII.GetBytes("YUCPHDIF1");
				if (payload.Length < magic.Length + 16 + 32)
					return false;
				
				for (int i = 0; i < magic.Length; i++)
				{
					if (payload[i] != magic[i])
						return false;
				}
				
				int offset = magic.Length;
				byte[] iv = new byte[16];
				Buffer.BlockCopy(payload, offset, iv, 0, iv.Length);
				offset += iv.Length;
				
				byte[] hmac = new byte[32];
				Buffer.BlockCopy(payload, offset, hmac, 0, hmac.Length);
				offset += hmac.Length;
				
				byte[] ciphertext = new byte[payload.Length - offset];
				Buffer.BlockCopy(payload, offset, ciphertext, 0, ciphertext.Length);
				
				byte[] hmacKey;
				using (var sha = System.Security.Cryptography.SHA256.Create())
				{
					byte[] prefix = System.Text.Encoding.UTF8.GetBytes("YUCP|hmac|");
					byte[] data = new byte[prefix.Length + key.Length];
					Buffer.BlockCopy(prefix, 0, data, 0, prefix.Length);
					Buffer.BlockCopy(key, 0, data, prefix.Length, key.Length);
					hmacKey = sha.ComputeHash(data);
				}
				
				byte[] computed;
				using (var h = new System.Security.Cryptography.HMACSHA256(hmacKey))
				{
					byte[] ivAndCipher = new byte[iv.Length + ciphertext.Length];
					Buffer.BlockCopy(iv, 0, ivAndCipher, 0, iv.Length);
					Buffer.BlockCopy(ciphertext, 0, ivAndCipher, iv.Length, ciphertext.Length);
					computed = h.ComputeHash(ivAndCipher);
				}
				
				if (!computed.SequenceEqual(hmac))
					return false;
				
				using (var aes = System.Security.Cryptography.Aes.Create())
				{
					aes.KeySize = 256;
					aes.Mode = System.Security.Cryptography.CipherMode.CBC;
					aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
					aes.Key = key;
					aes.IV = iv;
					
					using (var ms = new MemoryStream())
					using (var cs = new System.Security.Cryptography.CryptoStream(ms, aes.CreateDecryptor(), System.Security.Cryptography.CryptoStreamMode.Write))
					{
						cs.Write(ciphertext, 0, ciphertext.Length);
						cs.FlushFinalBlock();
						File.WriteAllBytes(outputPath, ms.ToArray());
					}
				}
				
				return true;
			}
			catch
			{
				return false;
			}
		}
		/// <summary>
		/// Creates meta file using embedded content from DerivedFbxAsset, preserving humanoid Avatar mappings.
		/// Falls back to original derived FBX meta file if embedded content is not available.
		/// Never uses base FBX meta to avoid incompatible humanoid mappings.
		/// </summary>
		private static void TryCopyMetaWithGuid(string physicalOutputPath, string originalDerivedFbxPath, string baseFbxPath, string targetGuid, DerivedFbxAsset derivedAsset = null)
		{
			string outputMetaPath = physicalOutputPath + ".meta";
			string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			
			// Priority 1: Use embedded meta file content from DerivedFbxAsset (best option - works in any project)
			if (derivedAsset != null && !string.IsNullOrEmpty(derivedAsset.embeddedMetaFileContent))
			{
				try
				{
					string metaContent = derivedAsset.embeddedMetaFileContent;
					
					// Replace placeholder GUID with actual target GUID
					if (!string.IsNullOrEmpty(targetGuid))
					{
						metaContent = System.Text.RegularExpressions.Regex.Replace(
							metaContent,
							@"guid:\s*PLACEHOLDER_GUID",
							$"guid: {targetGuid}",
							System.Text.RegularExpressions.RegexOptions.IgnoreCase
						);
						
						// Also handle case where GUID might have been extracted as-is
						metaContent = System.Text.RegularExpressions.Regex.Replace(
							metaContent,
							@"guid:\s*[a-f0-9]{32}",
							$"guid: {targetGuid}",
							System.Text.RegularExpressions.RegexOptions.IgnoreCase
						);
					}
					
					File.WriteAllText(outputMetaPath, metaContent);
					Debug.Log($"[DerivedFbxBuilder] Recreated .meta file from embedded content (preserves humanoid Avatar mappings)");
					return;
				}
				catch (System.Exception ex)
				{
					Debug.LogWarning($"[DerivedFbxBuilder] Failed to recreate .meta from embedded content: {ex.Message}");
				}
			}
			
			// Priority 2: Try to copy from original derived FBX meta (fallback if embedded content not available)
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
						Debug.Log($"[DerivedFbxBuilder] Copied original derived FBX .meta from '{originalDerivedFbxPath}' to output (preserves humanoid Avatar mappings)");
						return;
					}
				}
				catch (System.Exception ex)
				{
					Debug.LogWarning($"[DerivedFbxBuilder] Failed to copy original derived FBX .meta: {ex.Message}");
				}
			}
			
			// Priority 3: Create fresh meta file (Unity will regenerate Avatar)
			// WARNING: Embedded content and original meta file not available
			Debug.LogWarning($"[DerivedFbxBuilder] Embedded meta content and original derived FBX .meta file not available. " +
				$"Creating fresh .meta file. Unity will regenerate the Avatar/humanoid mapping on import. " +
				$"If this derived FBX uses humanoid rigging, you may need to reconfigure the Avatar mapping after import.");
			
			// Create fresh meta file with target GUID (Unity will regenerate Avatar and import settings)
			if (!string.IsNullOrEmpty(targetGuid))
			{
				try
				{
					if (MetaFileManager.WriteGuid(physicalOutputPath, targetGuid))
					{
						Debug.Log($"[DerivedFbxBuilder] Created fresh .meta file with GUID: {targetGuid}. Unity will regenerate Avatar on import.");
						return;
					}
				}
				catch (System.Exception ex)
				{
					Debug.LogError($"[DerivedFbxBuilder] Failed to create fresh .meta file: {ex.Message}");
				}
			}
			else
			{
				Debug.LogError("[DerivedFbxBuilder] Cannot create .meta file: targetGuid is null or empty");
			}
		}
	}
}
