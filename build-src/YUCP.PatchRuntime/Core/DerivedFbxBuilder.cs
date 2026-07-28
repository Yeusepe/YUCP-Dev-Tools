using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace YUCP.PatchRuntime
{
	/// <summary>
	/// Builds derived FBX files using HDiffPatch binary patching.
	/// Adapted from CocoTools CocoPatch.cs and CocoUtils.cs implementation.
	/// </summary>
	public static class DerivedFbxBuilder
	{
		private const string ProtectedAssetUnlockServiceTypeName =
			"YUCP.Importer.Editor.PackageManager.Core.ProtectedAssetUnlockService";

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

			string wrappedContentKey = null;
			if (derivedAsset.requiresLicense && !string.IsNullOrEmpty(derivedAsset.licensePackageId))
			{
				string protectedAssetId = derivedAsset.requiresServerUnlock ? derivedAsset.protectedAssetId : null;
				if (!TryAuthorizeProtectedAsset(
					derivedAsset.licensePackageId,
					protectedAssetId,
					out wrappedContentKey,
					out var authorizationError))
				{
					Debug.LogError(
						$"[DerivedFbxBuilder] License required for package '{derivedAsset.licensePackageId}'. " +
						$"{authorizationError}");
					return null;
				}
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
				var resolvedBasePathsByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				string encryptedDiffPath = entries[0].hdiffFilePath;
				
				foreach (var entry in entries)
				{
					if (entry == null || string.IsNullOrEmpty(entry.baseGuid) || string.IsNullOrEmpty(entry.shareEnc))
					{
						Debug.LogError("[DerivedFbxBuilder] Invalid patch entry (missing baseGuid/shareEnc).");
						return null;
					}
					
					string basePath = ResolveBaseFbxPath(projectPath, entry, "Base FBX");
					if (string.IsNullOrEmpty(basePath))
					{
						Debug.LogError(BuildBaseResolutionFailureMessage("Required base FBX", entry, projectPath));
						return null;
					}
					
					string basePhysicalPath = ResolvePhysicalPath(projectPath, basePath);
					if (!File.Exists(basePhysicalPath))
					{
						Debug.LogError(BuildBaseFileMissingMessage("Required base FBX", basePath));
						return null;
					}
					
					ComputeBaseHashAndMask(basePhysicalPath, out var baseHash, out var mask);
					string baseHashHex = BytesToHex(baseHash);
					if (!string.IsNullOrEmpty(entry.baseHash) && !string.Equals(entry.baseHash, baseHashHex, StringComparison.OrdinalIgnoreCase))
					{
						Debug.LogError(BuildBaseHashMismatchMessage("Required base FBX", basePath, entry.baseHash, baseHashHex));
						return null;
					}
					
					byte[] shareEnc = Convert.FromBase64String(entry.shareEnc);
					byte[] share = XorBytes(shareEnc, mask);
					shares.Add(share);
					resolvedBasePathsByGuid[entry.baseGuid] = basePath;
				}
				
				byte[] recoveryKey = RecoverKey(shares);
				byte[] contentKey = recoveryKey;
				if (derivedAsset.requiresServerUnlock && !string.IsNullOrEmpty(derivedAsset.protectedAssetId))
				{
					if (string.IsNullOrEmpty(wrappedContentKey) ||
					    !ProtectedContentKeyUtility.TryUnwrapContentKey(wrappedContentKey, recoveryKey, out contentKey))
					{
						Debug.LogError("[DerivedFbxBuilder] Failed to unwrap protected content key for this derived asset.");
						return null;
					}
				}
				
				string encryptedPhysicalPath = ResolvePhysicalPath(projectPath, encryptedDiffPath);
				if (!File.Exists(encryptedPhysicalPath))
				{
					Debug.LogError($"[DerivedFbxBuilder] Encrypted diff file not found: {encryptedDiffPath}");
					return null;
				}
				
				string canonicalBaseGuid = derivedAsset.canonicalBaseGuid;
				if (string.IsNullOrEmpty(canonicalBaseGuid))
					canonicalBaseGuid = entries[0].baseGuid;
				
				string canonicalBasePath = null;
				if (!string.IsNullOrEmpty(canonicalBaseGuid))
				{
					resolvedBasePathsByGuid.TryGetValue(canonicalBaseGuid, out canonicalBasePath);
				}
				if (string.IsNullOrEmpty(canonicalBasePath))
				{
					DerivedFbxAsset.PatchEntry canonicalEntry = entries.FirstOrDefault(e =>
						e != null &&
						string.Equals(e.baseGuid, canonicalBaseGuid, StringComparison.OrdinalIgnoreCase));
					canonicalBasePath = canonicalEntry != null
						? ResolveBaseFbxPath(projectPath, canonicalEntry, "Canonical base FBX")
						: AssetDatabase.GUIDToAssetPath(canonicalBaseGuid);
					if (string.IsNullOrEmpty(canonicalBasePath))
					{
						Debug.LogError(canonicalEntry != null
							? BuildBaseResolutionFailureMessage("Canonical base FBX", canonicalEntry, projectPath)
							: BuildGuidOnlyResolutionFailureMessage("Canonical base FBX", canonicalBaseGuid, projectPath));
						return null;
					}
				}
				if (string.IsNullOrEmpty(canonicalBasePath))
				{
					Debug.LogError(BuildGuidOnlyResolutionFailureMessage("Canonical base FBX", canonicalBaseGuid, projectPath));
					return null;
				}
				
				string canonicalPhysicalPath = ResolvePhysicalPath(projectPath, canonicalBasePath);
				if (!File.Exists(canonicalPhysicalPath))
				{
					Debug.LogError(BuildBaseFileMissingMessage("Canonical base FBX", canonicalBasePath));
					return null;
				}
				
				string tempDiffPath = Path.Combine(projectPath, "Library", "YUCP", $"patch_{Guid.NewGuid():N}.hdiff");
				if (!DecryptDiffFile(encryptedPhysicalPath, tempDiffPath, contentKey))
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

		private static string BuildBaseResolutionFailureMessage(string label, DerivedFbxAsset.PatchEntry entry, string projectPath)
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine($"[DerivedFbxBuilder] {label} could not be resolved. The derived FBX was not generated.");
			sb.AppendLine("This usually means the original base FBX is not imported yet, its .meta GUID changed, or it was moved away from the exported fallback path.");
			AppendGuidResolutionDetails(sb, projectPath, entry?.baseGuid);

			string rawFallback = entry?.basePathFallback;
			if (string.IsNullOrWhiteSpace(rawFallback))
			{
				sb.AppendLine("Direct path fallback: none was exported for this required base.");
			}
			else
			{
				string fallbackPath = NormalizeProjectRelativeAssetPath(rawFallback);
				if (string.IsNullOrEmpty(fallbackPath))
				{
					sb.AppendLine($"Direct path fallback: exported value '{rawFallback}' is not a valid project path. It must start with Assets/ or Packages/.");
				}
				else
				{
					string fallbackPhysicalPath = ResolvePhysicalPath(projectPath, fallbackPath);
					string fallbackStatus = File.Exists(fallbackPhysicalPath)
						? "exists"
						: "does not exist in this project";
					sb.AppendLine($"Direct path fallback: '{fallbackPath}' {fallbackStatus}.");
				}
			}

			if (!string.IsNullOrEmpty(entry?.baseHash))
			{
				sb.AppendLine($"Required base hash: {ShortHash(entry.baseHash)}. A same-looking FBX with different bytes will still be rejected.");
			}

			sb.Append("Fix: import the exact original base FBX first. If a patcher regenerated the GUID, keep the FBX at the exported fallback path or re-export this derived FBX with the correct direct path fallback.");
			return sb.ToString().TrimEnd();
		}

		private static string BuildGuidOnlyResolutionFailureMessage(string label, string guid, string projectPath)
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine($"[DerivedFbxBuilder] {label} could not be resolved. The derived FBX was not generated.");
			AppendGuidResolutionDetails(sb, projectPath, guid);
			sb.Append("Fix: import the exact original base FBX first, then retry the package import.");
			return sb.ToString().TrimEnd();
		}

		private static void AppendGuidResolutionDetails(System.Text.StringBuilder sb, string projectPath, string guid)
		{
			if (string.IsNullOrEmpty(guid))
			{
				sb.AppendLine("GUID lookup: no GUID was exported for this required base.");
				return;
			}

			string guidPath = AssetDatabase.GUIDToAssetPath(guid);
			if (string.IsNullOrEmpty(guidPath))
			{
				sb.AppendLine($"GUID lookup: no asset in this project has GUID '{guid}'.");
				return;
			}

			string guidPhysicalPath = ResolvePhysicalPath(projectPath, guidPath);
			string status = File.Exists(guidPhysicalPath)
				? "exists"
				: "resolved, but the file is missing on disk";
			sb.AppendLine($"GUID lookup: '{guid}' -> '{guidPath}' ({status}).");
		}

		private static string BuildBaseFileMissingMessage(string label, string basePath)
		{
			return $"[DerivedFbxBuilder] {label} resolved to '{basePath}', but the file is missing on disk. " +
			       "Reimport or restore the base FBX before retrying. The derived FBX was not generated.";
		}

		private static string BuildBaseHashMismatchMessage(string label, string basePath, string expectedHash, string actualHash)
		{
			return $"[DerivedFbxBuilder] {label} hash mismatch. A base FBX was found at '{basePath}', but it is not the exact file this patch was exported against.\n" +
			       $"Expected SHA256: {ShortHash(expectedHash)}\n" +
			       $"Actual SHA256:   {ShortHash(actualHash)}\n" +
			       "Fix: import the exact original base FBX. Same-looking FBXs, regenerated exports, or different variants are intentionally rejected.";
		}

		private static string ShortHash(string hash)
		{
			if (string.IsNullOrEmpty(hash))
				return "<none>";
			return hash.Length <= 16 ? hash : hash.Substring(0, 16) + "...";
		}

		private static string ResolveBaseFbxPath(string projectPath, DerivedFbxAsset.PatchEntry entry, string label)
		{
			if (entry == null)
			{
				return null;
			}

			if (!string.IsNullOrEmpty(entry.baseGuid))
			{
				string guidPath = AssetDatabase.GUIDToAssetPath(entry.baseGuid);
				if (!string.IsNullOrEmpty(guidPath))
				{
					string guidPhysicalPath = ResolvePhysicalPath(projectPath, guidPath);
					if (File.Exists(guidPhysicalPath))
					{
						return guidPath;
					}

					Debug.LogWarning($"[DerivedFbxBuilder] {label} GUID resolved to '{guidPath}', but the file was missing.");
				}
			}

			string fallbackPath = NormalizeProjectRelativeAssetPath(entry.basePathFallback);
			if (string.IsNullOrEmpty(fallbackPath))
			{
				return null;
			}

			string fallbackPhysicalPath = ResolvePhysicalPath(projectPath, fallbackPath);
			if (!File.Exists(fallbackPhysicalPath))
			{
				Debug.LogWarning($"[DerivedFbxBuilder] {label} direct path fallback does not exist: {fallbackPath}");
				return null;
			}

			Debug.LogWarning(
				$"[DerivedFbxBuilder] Using advanced direct path fallback '{fallbackPath}' for base GUID '{entry.baseGuid}'. " +
				"GUID lookup is safer; this fallback can break if the base FBX is moved or renamed.");
			return fallbackPath;
		}

		private static string NormalizeProjectRelativeAssetPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return string.Empty;
			}

			string normalized = path.Trim().Replace('\\', '/');
			string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');

			if (Path.IsPathRooted(normalized))
			{
				string rooted = Path.GetFullPath(normalized).Replace('\\', '/');
				if (!rooted.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
				{
					return string.Empty;
				}

				normalized = rooted.Substring(projectRoot.Length).TrimStart('/');
			}

			if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
				!normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
			{
				return string.Empty;
			}

			return normalized;
		}

		private static bool TryAuthorizeProtectedAsset(
			string packageId,
			string protectedAssetId,
			out string wrappedContentKey,
			out string error)
		{
			wrappedContentKey = null;
			error = null;

			Type serviceType = AppDomain.CurrentDomain
				.GetAssemblies()
				.Select(assembly => assembly.GetType(ProtectedAssetUnlockServiceTypeName, false))
				.FirstOrDefault(type => type != null);

			if (serviceType == null)
			{
				error = "Protected asset unlock requires com.yucp.importer. Re-export without license verification or install the importer before importing this package.";
				return false;
			}

			MethodInfo method = serviceType.GetMethod(
				"TryAuthorizePackage",
				BindingFlags.Public | BindingFlags.Static,
				null,
				new[] { typeof(string), typeof(string), typeof(string).MakeByRefType(), typeof(string).MakeByRefType() },
				null);

			if (method == null)
			{
				error = "Could not find the YUCP importer protected unlock entry point.";
				return false;
			}

			try
			{
				object[] args = { packageId, protectedAssetId, null, null };
				bool isAuthorized = method.Invoke(null, args) is bool result && result;
				wrappedContentKey = args[2] as string;
				error = args[3] as string;

				if (!isAuthorized && string.IsNullOrEmpty(error))
				{
					error = "Protected asset authorization failed.";
				}

				return isAuthorized;
			}
			catch (TargetInvocationException ex)
			{
				error = ex.InnerException?.Message ?? ex.Message;
				return false;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
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
					if (!EmbeddedTextEncodingUtility.TryDecode(derivedAsset.embeddedMetaFileContent, out string metaContent))
					{
						Debug.LogWarning("[DerivedFbxBuilder] Failed to decode embedded .meta content from DerivedFbxAsset.");
						metaContent = null;
					}

					if (!string.IsNullOrEmpty(metaContent))
					{
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

					Debug.LogWarning("[DerivedFbxBuilder] Embedded .meta content was empty after decoding.");
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
