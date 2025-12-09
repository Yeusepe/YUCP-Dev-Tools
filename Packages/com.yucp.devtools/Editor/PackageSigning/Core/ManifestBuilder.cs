using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    /// <summary>
    /// Builds PackageManifest.json for signing
    /// </summary>
    public static class ManifestBuilder
    {
        /// <summary>
        /// Build manifest from package archive
        /// </summary>
        public static PackageManifest BuildManifest(
            string packagePath,
            string packageId,
            string version,
            string publisherId,
            string vrchatUserId,
            string gumroadProductId = null,
            string jinxxyProductId = null)
        {
            // Compute archive SHA-256
            string archiveSha256 = ComputeFileHash(packagePath);

            // Compute per-file hashes (simplified - would need to extract from .unitypackage)
            var fileHashes = new Dictionary<string, string>();
            // TODO: Extract files from .unitypackage and compute hashes

            var manifest = new PackageManifest
            {
                authorityId = "unitysign.yucp",
                keyId = "yucp-authority-2025",
                publisherId = publisherId,
                packageId = packageId,
                version = version,
                archiveSha256 = archiveSha256,
                vrchatAuthorUserId = vrchatUserId,
                fileHashes = fileHashes,
                gumroadProductId = gumroadProductId ?? "",
                jinxxyProductId = jinxxyProductId ?? ""
            };

            return manifest;
        }

        /// <summary>
        /// Compute SHA-256 hash of file
        /// </summary>
        private static string ComputeFileHash(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Canonicalize manifest JSON
        /// </summary>
        public static string CanonicalizeManifest(PackageManifest manifest)
        {
            // Simple canonicalization - sort keys alphabetically
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"archiveSha256\":\"{manifest.archiveSha256}\",");
            sb.Append($"\"authorityId\":\"{manifest.authorityId}\",");
            sb.Append($"\"fileHashes\":{{");
            // Add file hashes
            bool first = true;
            foreach (var kvp in manifest.fileHashes)
            {
                if (!first) sb.Append(",");
                sb.Append($"\"{kvp.Key}\":\"{kvp.Value}\"");
                first = false;
            }
            sb.Append($"}},");
            sb.Append($"\"gumroadProductId\":\"{manifest.gumroadProductId ?? ""}\",");
            sb.Append($"\"jinxxyProductId\":\"{manifest.jinxxyProductId ?? ""}\",");
            sb.Append($"\"keyId\":\"{manifest.keyId}\",");
            sb.Append($"\"packageId\":\"{manifest.packageId}\",");
            sb.Append($"\"publisherId\":\"{manifest.publisherId}\",");
            sb.Append($"\"version\":\"{manifest.version}\",");
            sb.Append($"\"vrchatAuthorUserId\":\"{manifest.vrchatAuthorUserId}\"");
            sb.Append("}");
            return sb.ToString();
        }
    }
}
