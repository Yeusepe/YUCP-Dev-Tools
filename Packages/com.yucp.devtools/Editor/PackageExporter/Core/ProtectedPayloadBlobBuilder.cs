using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class ProtectedPayloadBlobBuilder
    {
        private static readonly byte[] BlobMagic = Encoding.ASCII.GetBytes("YUCPBLOB");
        private const byte BlobVersion = 1;
        private const string CipherName = "aes-256-cbc+hmac-sha256";
        private const string ArchiveFormat = "zip";

        internal sealed class BuildResult
        {
            public string blobFilePath;
            public string contentKeyBase64;
            public ProtectedPayloadDescriptor descriptor;
        }

        internal static BuildResult BuildFromUnityPackage(
            string sourceUnityPackagePath,
            string packageId,
            string packageName)
        {
            if (string.IsNullOrWhiteSpace(sourceUnityPackagePath) || !File.Exists(sourceUnityPackagePath))
                throw new FileNotFoundException("Synthetic payload package was not found.", sourceUnityPackagePath);

            string tempRoot = Path.Combine(Path.GetTempPath(), "YUCP_ProtectedPayload", Guid.NewGuid().ToString("N"));
            string extractRoot = Path.Combine(tempRoot, "extract");
            string archivePath = Path.Combine(tempRoot, "payload.zip");
            string blobPath = Path.Combine(tempRoot, $"{SanitizeFileName(packageName ?? packageId ?? "ProtectedPayload")}_{Guid.NewGuid():N}.yucpblob");

            try
            {
                Directory.CreateDirectory(extractRoot);
                ExtractUnityPackage(sourceUnityPackagePath, extractRoot);

                string[] payloadAssetPaths;
                int entryCount = BuildPlaintextArchive(extractRoot, archivePath, out payloadAssetPaths);
                if (entryCount == 0)
                    throw new InvalidOperationException("Synthetic payload package did not contain any protected payload files.");

                byte[] contentKey = new byte[32];
                RandomNumberGenerator.Fill(contentKey);
                EncryptArchiveToBlob(archivePath, blobPath, contentKey);

                var descriptor = new ProtectedPayloadDescriptor
                {
                    formatVersion = "1",
                    protectedAssetId = Guid.NewGuid().ToString("N"),
                    blobAssetPath = "",
                    cipher = CipherName,
                    archiveFormat = ArchiveFormat,
                    ciphertextSha256 = ComputeFileSha256Hex(blobPath),
                    ciphertextSize = new FileInfo(blobPath).Length,
                    plaintextSha256 = ComputeFileSha256Hex(archivePath),
                    plaintextSize = new FileInfo(archivePath).Length,
                    entryCount = entryCount,
                    payloadAssetPaths = payloadAssetPaths ?? Array.Empty<string>(),
                    requiresOnlineUnlock = true,
                    requiresBrokeredMaterialization = true,
                    brokerProtocolVersion = 1,
                };
                descriptor.manifestBindingSha256 =
                    ProtectedPayloadIntegrityUtility.ComputeManifestBindingSha256(descriptor);
                return new BuildResult
                {
                    blobFilePath = blobPath,
                    contentKeyBase64 = Convert.ToBase64String(contentKey),
                    descriptor = descriptor,
                };
            }
            catch
            {
                TryDeleteFile(blobPath);
                throw;
            }
            finally
            {
                TryDeleteDirectory(extractRoot);
                TryDeleteFile(archivePath);
            }
        }

        private static void ExtractUnityPackage(string unityPackagePath, string extractRoot)
        {
            using var fileStream = File.OpenRead(unityPackagePath);
            using var gzipStream = new GZipInputStream(fileStream);
            using var tarArchive = TarArchive.CreateInputTarArchive(gzipStream, Encoding.UTF8);
            tarArchive.ExtractContents(extractRoot);
        }

        private static int BuildPlaintextArchive(
            string extractRoot,
            string archivePath,
            out string[] payloadAssetPaths)
        {
            int logicalEntryCount = 0;
            var logicalAssetPaths = new List<string>();
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);

            foreach (string entryDirectory in Directory.GetDirectories(extractRoot))
            {
                string pathnameFile = Path.Combine(entryDirectory, "pathname");
                if (!File.Exists(pathnameFile))
                    continue;

                string destinationPath = NormalizeUnityPath(File.ReadAllText(pathnameFile));
                if (string.IsNullOrWhiteSpace(destinationPath) || ShouldSkipPath(destinationPath))
                    continue;

                string assetFile = Path.Combine(entryDirectory, "asset");
                string assetMetaFile = Path.Combine(entryDirectory, "asset.meta");
                bool isFolderEntry = !Path.HasExtension(destinationPath) && !File.Exists(assetFile);

                if (!isFolderEntry && File.Exists(assetFile))
                {
                    AddFileEntry(archive, assetFile, destinationPath);
                    logicalEntryCount++;
                    logicalAssetPaths.Add(destinationPath);
                }

                if (File.Exists(assetMetaFile))
                {
                    AddFileEntry(archive, assetMetaFile, destinationPath + ".meta");
                }
            }

            payloadAssetPaths = logicalAssetPaths
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return logicalEntryCount;
        }

        private static void AddFileEntry(ZipArchive archive, string sourcePath, string entryPath)
        {
            string normalizedPath = NormalizeUnityPath(entryPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                return;

            var entry = archive.CreateEntry(normalizedPath, CompressionLevel.Optimal);
            using var input = File.OpenRead(sourcePath);
            using var output = entry.Open();
            input.CopyTo(output);
        }

        private static void EncryptArchiveToBlob(string archivePath, string blobPath, byte[] contentKey)
        {
            byte[] plaintext = File.ReadAllBytes(archivePath);

            using var aes = Aes.Create();
            aes.Key = contentKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            byte[] ciphertext;
            using (var encryptor = aes.CreateEncryptor())
            using (var memoryStream = new MemoryStream())
            using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
            {
                cryptoStream.Write(plaintext, 0, plaintext.Length);
                cryptoStream.FlushFinalBlock();
                ciphertext = memoryStream.ToArray();
            }

            byte[] macInput = new byte[BlobMagic.Length + 1 + aes.IV.Length + ciphertext.Length];
            Buffer.BlockCopy(BlobMagic, 0, macInput, 0, BlobMagic.Length);
            macInput[BlobMagic.Length] = BlobVersion;
            Buffer.BlockCopy(aes.IV, 0, macInput, BlobMagic.Length + 1, aes.IV.Length);
            Buffer.BlockCopy(ciphertext, 0, macInput, BlobMagic.Length + 1 + aes.IV.Length, ciphertext.Length);

            byte[] tag;
            using (var hmac = new HMACSHA256(DeriveMacKey(contentKey)))
            {
                tag = hmac.ComputeHash(macInput);
            }

            using var output = File.Create(blobPath);
            output.Write(BlobMagic, 0, BlobMagic.Length);
            output.WriteByte(BlobVersion);
            output.Write(aes.IV, 0, aes.IV.Length);
            output.Write(tag, 0, tag.Length);
            output.Write(ciphertext, 0, ciphertext.Length);
        }

        private static bool ShouldSkipPath(string destinationPath)
        {
            string normalized = NormalizeUnityPath(destinationPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return true;

            if (normalized.Equals("Assets/YUCP_PackageInfo.json", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Assets/YUCP_ProtectedPayload.json", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Assets/package.json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalized.StartsWith("Assets/YUCP_TempInstall_", StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalized.StartsWith("Packages/yucp.installed-packages/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages/yucp.packageguardian/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string NormalizeUnityPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            return path.Replace('\\', '/').Trim();
        }

        private static byte[] DeriveMacKey(byte[] contentKey)
        {
            byte[] prefix = Encoding.UTF8.GetBytes("YUCP|protected-payload|mac|");
            byte[] material = new byte[prefix.Length + contentKey.Length];
            Buffer.BlockCopy(prefix, 0, material, 0, prefix.Length);
            Buffer.BlockCopy(contentKey, 0, material, prefix.Length, contentKey.Length);
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(material);
        }

        private static string ComputeFileSha256Hex(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(stream);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "protected-payload";

            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim('-', ' ');
        }

        private static void TryDeleteFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            try
            {
                File.Delete(filePath);
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            try
            {
                Directory.Delete(directoryPath, true);
            }
            catch
            {
            }
        }
    }
}
