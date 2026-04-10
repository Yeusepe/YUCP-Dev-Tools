using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using YUCP.DevTools.Editor.PackageSigning.Core;
using YUCP.DevTools.Editor.PackageSigning.Data;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class PrecompiledInstallerRuntimeTests
    {
        [Test]
        public void PrecompiledInstallerRuntime_BinaryAndMetaExist()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(
                projectRoot,
                "Packages",
                "com.yucp.devtools",
                "Editor",
                "PackageExporter",
                "Binaries",
                "YUCP.DirectVpmInstaller.Template.dll");

            Assert.That(File.Exists(dllPath), Is.True, "Expected the checked-in precompiled installer runtime binary to exist.");
            Assert.That(File.Exists(dllPath + ".meta"), Is.True, "Expected the checked-in precompiled installer runtime binary to have a Unity .meta file.");
        }

        [Test]
        public void PrecompiledInstallerRuntime_AbsorbsYucpPatchImporterInjection()
        {
            FieldInfo field = typeof(PackageBuilder).GetField(
                "s_patchRuntimeInjectedFiles",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo method = typeof(PackageBuilder).GetMethod(
                "IsPatchRuntimeHandledByPrecompiledInstaller",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            Assert.That(method, Is.Not.Null);

            object patchImporterEntry = ((IEnumerable)field.GetValue(null))
                .Cast<object>()
                .FirstOrDefault(item =>
                    string.Equals(
                        item.GetType().GetField("targetPath")?.GetValue(item) as string,
                        "Packages/com.yucp.temp/Editor/YUCPPatchImporter.cs",
                        System.StringComparison.OrdinalIgnoreCase));

            Assert.That(patchImporterEntry, Is.Not.Null);
            Assert.That((bool)method.Invoke(null, new[] { patchImporterEntry }), Is.True);
        }

        [Test]
        public void PrecompiledInstallerRuntime_StillInjectsInstallerPreflightBootstrap()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string installerScriptPath = Path.Combine(
                projectRoot,
                "Packages",
                "com.yucp.devtools",
                "Editor",
                "PackageExporter",
                "Templates",
                "DirectVpmInstaller.cs");
            string tempExtractDir = Path.Combine(Path.GetTempPath(), "yucp-preflight-bootstrap-" + System.Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(tempExtractDir);

            try
            {
                MethodInfo method = typeof(PackageBuilder).GetMethod(
                    "TryInjectInstallerPreflightBootstrap",
                    BindingFlags.Static | BindingFlags.NonPublic);

                Assert.That(method, Is.Not.Null);
                Assert.That(File.Exists(installerScriptPath), Is.True, "Expected the DirectVpmInstaller template source to exist.");

                bool injected = (bool)method.Invoke(null, new object[]
                {
                    tempExtractDir,
                    "Packages/com.yucp.components/Editor",
                    installerScriptPath,
                });

                Assert.That(injected, Is.True, "Expected the installer preflight bootstrap to be emitted.");

                string[] pathnameFiles = Directory.GetFiles(tempExtractDir, "pathname", SearchOption.AllDirectories);
                string matchedPathnameFile = pathnameFiles.FirstOrDefault(pathnameFile =>
                {
                    string pathname = File.ReadAllText(pathnameFile).Replace('\\', '/');
                    return pathname.StartsWith("Packages/com.yucp.components/Editor/YUCP_InstallerPreflight_", System.StringComparison.Ordinal) &&
                           pathname.EndsWith(".cs", System.StringComparison.Ordinal);
                });

                Assert.That(matchedPathnameFile, Is.Not.Null, "Expected an emitted installer preflight bootstrap pathname.");

                string assetContent = File.ReadAllText(Path.Combine(Path.GetDirectoryName(matchedPathnameFile), "asset"));
                Assert.That(assetContent, Does.Contain("InstallerPreflight_"), "Expected the emitted bootstrap asset to contain a concrete installer preflight class name.");
            }
            finally
            {
                if (Directory.Exists(tempExtractDir))
                {
                    Directory.Delete(tempExtractDir, true);
                }
            }
        }

        [Test]
        public void PrecompiledInstallerRuntime_ResolvesSigningRootFromProtectedShellMetadata()
        {
            string tempExtractDir = Path.Combine(Path.GetTempPath(), "yucp-signing-root-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempExtractDir);

            try
            {
                string entryFolder = Path.Combine(tempExtractDir, "shell-metadata");
                Directory.CreateDirectory(entryFolder);
                File.WriteAllText(
                    Path.Combine(entryFolder, "pathname"),
                    "Packages/yucp.installed-packages/Wasbeer/YUCP_PackageInfo.json");

                MethodInfo method = typeof(PackageBuilder).GetMethod(
                    "ResolveSigningRootPathname",
                    BindingFlags.Static | BindingFlags.NonPublic);

                Assert.That(method, Is.Not.Null);

                string signingRoot = method.Invoke(null, new object[] { tempExtractDir }) as string;
                Assert.That(signingRoot, Is.EqualTo("Packages/yucp.installed-packages/Wasbeer"));
            }
            finally
            {
                if (Directory.Exists(tempExtractDir))
                {
                    Directory.Delete(tempExtractDir, true);
                }
            }
        }

        [Test]
        public void PrecompiledInstallerRuntime_InjectsSigningFilesIntoProtectedShellRoot()
        {
            string packagePath = null;
            try
            {
                SignatureEmbedder.EmbedSigningData(
                    new PackageManifest
                    {
                        authorityId = "unitysign.yucp",
                        keyId = "authority-key",
                        publisherId = "publisher-123",
                        packageId = "package-123",
                        version = "1.0.0",
                        archiveSha256 = "deadbeef",
                        fileHashes = new Dictionary<string, string>(),
                        certificateChain = Array.Empty<YUCP.Importer.Editor.PackageVerifier.Data.CertificateData>(),
                    },
                    new SignatureData
                    {
                        algorithm = "ed25519",
                        keyId = "authority-key",
                        signature = "c2lnbmF0dXJl",
                        certificateIndex = 0,
                    });

                packagePath = CreateUnityPackage(new Dictionary<string, byte[]>
                {
                    ["asset-shell/pathname"] = Encoding.UTF8.GetBytes("Packages/yucp.installed-packages/Wasbeer/YUCP_PackageInfo.json"),
                    ["asset-shell/asset"] = Encoding.UTF8.GetBytes("{\"packageName\":\"Wasbeer\"}"),
                });

                MethodInfo method = typeof(PackageBuilder).GetMethod(
                    "InjectSigningDataIntoPackage",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);

                method.Invoke(null, new object[] { packagePath });

                string[] pathnames = ReadPackagePathnames(packagePath);
                Assert.That(pathnames, Has.Some.EqualTo("Packages/yucp.installed-packages/Wasbeer/_Signing/PackageManifest.json"));
                Assert.That(pathnames, Has.Some.EqualTo("Packages/yucp.installed-packages/Wasbeer/_Signing/PackageManifest.sig"));
                Assert.That(pathnames, Has.None.EqualTo("Assets/_Signing/PackageManifest.json"));
                Assert.That(pathnames, Has.None.EqualTo("Assets/_Signing/PackageManifest.sig"));
            }
            finally
            {
                SignatureEmbedder.RemoveSigningData();
                DeleteIfPresent(packagePath);
            }
        }

        private static string CreateUnityPackage(IReadOnlyDictionary<string, byte[]> entries)
        {
            string packagePath = Path.Combine(Path.GetTempPath(), $"yucp-exporter-test-{Guid.NewGuid():N}.unitypackage");
            using var fileStream = File.Create(packagePath);
            using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal, leaveOpen: false);
            foreach (var entry in entries)
            {
                WriteTarEntry(gzipStream, entry.Key.Replace('\\', '/'), entry.Value ?? Array.Empty<byte>());
            }

            gzipStream.Write(new byte[1024], 0, 1024);
            return packagePath;
        }

        private static string[] ReadPackagePathnames(string packagePath)
        {
            var results = new List<string>();
            using var fileStream = File.OpenRead(packagePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: false);

            while (TryReadTarEntry(gzipStream, out string entryName, out byte[] data))
            {
                if (entryName.EndsWith("/pathname", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(Encoding.UTF8.GetString(data).Trim());
                }
            }

            return results.ToArray();
        }

        private static bool TryReadTarEntry(Stream stream, out string entryName, out byte[] data)
        {
            entryName = null;
            data = null;

            byte[] header = new byte[512];
            int offset = 0;
            while (offset < header.Length)
            {
                int read = stream.Read(header, offset, header.Length - offset);
                if (read <= 0)
                {
                    return false;
                }

                offset += read;
            }

            if (header.All(b => b == 0))
            {
                return false;
            }

            entryName = ReadNullTerminatedAscii(header, 0, 100);
            long size = ReadOctal(header, 124, 12);
            data = new byte[size];

            int totalRead = 0;
            while (totalRead < size)
            {
                int read = stream.Read(data, totalRead, (int)size - totalRead);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Unexpected end of tar entry.");
                }

                totalRead += read;
            }

            int padding = (int)((512 - (size % 512)) % 512);
            if (padding > 0)
            {
                byte[] paddingBytes = new byte[padding];
                int skipped = 0;
                while (skipped < padding)
                {
                    int read = stream.Read(paddingBytes, skipped, padding - skipped);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException("Unexpected end of tar padding.");
                    }

                    skipped += read;
                }
            }

            return true;
        }

        private static string ReadNullTerminatedAscii(byte[] buffer, int offset, int length)
        {
            int count = 0;
            while (count < length && buffer[offset + count] != 0)
            {
                count++;
            }

            return Encoding.ASCII.GetString(buffer, offset, count).Trim();
        }

        private static long ReadOctal(byte[] buffer, int offset, int length)
        {
            string octal = ReadNullTerminatedAscii(buffer, offset, length).Trim();
            if (string.IsNullOrWhiteSpace(octal))
            {
                return 0;
            }

            return Convert.ToInt64(octal, 8);
        }

        private static void WriteTarEntry(Stream stream, string entryName, byte[] data)
        {
            byte[] header = new byte[512];
            WriteAscii(header, 0, 100, entryName);
            WriteOctal(header, 100, 8, 0644);
            WriteOctal(header, 108, 8, 0);
            WriteOctal(header, 116, 8, 0);
            WriteOctal(header, 124, 12, data.LongLength);
            WriteOctal(header, 136, 12, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            for (int i = 148; i < 156; i++)
            {
                header[i] = 0x20;
            }
            header[156] = (byte)'0';
            WriteAscii(header, 257, 6, "ustar");
            WriteAscii(header, 263, 2, "00");

            int checksum = 0;
            for (int i = 0; i < header.Length; i++)
            {
                checksum += header[i];
            }

            string checksumText = Convert.ToString(checksum, 8).PadLeft(6, '0');
            WriteAscii(header, 148, 6, checksumText);
            header[154] = 0;
            header[155] = 0x20;

            stream.Write(header, 0, header.Length);
            if (data.Length > 0)
            {
                stream.Write(data, 0, data.Length);
            }

            int padding = (int)((512 - (data.Length % 512)) % 512);
            if (padding > 0)
            {
                stream.Write(new byte[padding], 0, padding);
            }
        }

        private static void WriteAscii(byte[] buffer, int offset, int length, string value)
        {
            string normalized = value ?? string.Empty;
            byte[] bytes = Encoding.ASCII.GetBytes(normalized);
            Array.Copy(bytes, 0, buffer, offset, Math.Min(length, bytes.Length));
        }

        private static void WriteOctal(byte[] buffer, int offset, int length, long value)
        {
            string octal = Convert.ToString(value, 8);
            string padded = octal.PadLeft(length - 1, '0');
            WriteAscii(buffer, offset, length - 1, padded);
            buffer[offset + length - 1] = 0;
        }

        private static void DeleteIfPresent(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
