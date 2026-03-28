using System;
using System.IO;
using System.Reflection;
using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using NUnit.Framework;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class ProtectedPayloadBlobBuilderTests
    {
        private static MethodInfo GetShouldSkipPathMethod()
        {
            var method = typeof(ProtectedPayloadBlobBuilder).GetMethod(
                "ShouldSkipPath",
                BindingFlags.NonPublic | BindingFlags.Static
            );

            Assert.That(method, Is.Not.Null, "Expected ProtectedPayloadBlobBuilder.ShouldSkipPath to exist.");
            return method;
        }

        private static bool ShouldSkipPath(string path)
        {
            return (bool)GetShouldSkipPathMethod().Invoke(null, new object[] { path });
        }

        private static MethodInfo GetBuildFromUnityPackageMethod()
        {
            var method = typeof(ProtectedPayloadBlobBuilder).GetMethod(
                "BuildFromUnityPackage",
                BindingFlags.NonPublic | BindingFlags.Static
            );

            Assert.That(method, Is.Not.Null, "Expected ProtectedPayloadBlobBuilder.BuildFromUnityPackage to exist.");
            return method;
        }

        [Test]
        public void ShouldSkipPath_DoesNotStripTempPatchRuntimeAssets()
        {
            Assert.That(ShouldSkipPath("Packages/com.yucp.temp/Editor/YUCPPatchImporter.cs"), Is.False);
            Assert.That(ShouldSkipPath("Packages/com.yucp.temp/Editor/DerivedFbxBuilder.cs"), Is.False);
            Assert.That(ShouldSkipPath("Packages/com.yucp.temp/Editor/YUCP.PatchRuntime.asmdef"), Is.False);
            Assert.That(ShouldSkipPath("Packages/com.yucp.temp/Plugins/hdiffz.dll"), Is.False);
            Assert.That(ShouldSkipPath("Packages/com.yucp.temp/package.json"), Is.False);
        }

        [Test]
        public void ShouldSkipPath_StillSkipsShellOnlyFiles()
        {
            Assert.That(ShouldSkipPath("Assets/YUCP_PackageInfo.json"), Is.True);
            Assert.That(ShouldSkipPath("Assets/YUCP_ProtectedPayload.json"), Is.True);
            Assert.That(ShouldSkipPath("Packages/yucp.installed-packages/Editor/YUCP_Installer_test.cs"), Is.True);
            Assert.That(ShouldSkipPath("Packages/yucp.packageguardian/Editor/PackageGuardianMini.cs"), Is.True);
        }

        [Test]
        public void BuildFromUnityPackage_MarksProtectedPayloadAsBrokerRequired()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), $"yucp-protected-payload-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            try
            {
                string extractedRoot = Path.Combine(tempRoot, "extract");
                string entryRoot = Path.Combine(extractedRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(entryRoot);

                File.WriteAllText(Path.Combine(entryRoot, "pathname"), "Assets/Protected/TestAsset.prefab");
                File.WriteAllBytes(Path.Combine(entryRoot, "asset"), new byte[] { 1, 2, 3, 4 });
                File.WriteAllText(
                    Path.Combine(entryRoot, "asset.meta"),
                    "fileFormatVersion: 2\nguid: 11111111111111111111111111111111\n");

                string unityPackagePath = Path.Combine(tempRoot, "protected.unitypackage");
                CreateUnityPackage(extractedRoot, unityPackagePath);

                object result = GetBuildFromUnityPackageMethod().Invoke(
                    null,
                    new object[] { unityPackagePath, "pkg-protected-broker", "Protected Broker Package" });

                Assert.That(result, Is.Not.Null);

                Type resultType = result.GetType();
                string blobFilePath = resultType.GetField("blobFilePath")?.GetValue(result) as string;
                object descriptor = resultType.GetField("descriptor")?.GetValue(result);

                Assert.That(blobFilePath, Is.Not.Null.And.Not.Empty);
                Assert.That(File.Exists(blobFilePath), Is.True);
                Assert.That(descriptor, Is.Not.Null);

                Type descriptorType = descriptor.GetType();
                bool requiresBrokeredMaterialization = (bool)(descriptorType
                    .GetField("requiresBrokeredMaterialization")
                    ?.GetValue(descriptor) ?? false);
                int brokerProtocolVersion = (int)(descriptorType
                    .GetField("brokerProtocolVersion")
                    ?.GetValue(descriptor) ?? 0);

                Assert.That(requiresBrokeredMaterialization, Is.True);
                Assert.That(brokerProtocolVersion, Is.EqualTo(1));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        private static void CreateUnityPackage(string sourceRoot, string unityPackagePath)
        {
            using var output = File.Create(unityPackagePath);
            using var gzip = new GZipOutputStream(output);
            using var tar = new TarOutputStream(gzip, Encoding.UTF8);

            foreach (string filePath in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceRoot, filePath).Replace('\\', '/');
                var entry = TarEntry.CreateTarEntry(relativePath);
                entry.Size = new FileInfo(filePath).Length;
                tar.PutNextEntry(entry);

                using var input = File.OpenRead(filePath);
                input.CopyTo(tar);
                tar.CloseEntry();
            }
        }
    }
}
