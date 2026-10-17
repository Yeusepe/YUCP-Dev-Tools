using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class ExportTempCleanupTests
    {
        [TestCase("YUCP_Temp_", ".unitypackage", false)]
        [TestCase("YUCP_Temp_", ".unitypackage.tmp", false)]
        [TestCase("YUCP_ContentWithIcon_", ".unitypackage", false)]
        [TestCase("YUCP_PackageExtract_", "", true)]
        [TestCase("YUCP_PackageIcon_", "", true)]
        [TestCase("YUCP_Obfuscation_", "", true)]
        [TestCase("YUCP_Texture_", ".png", false)]
        public void StartupCleanup_RemovesDeadOwners_AndPreservesLiveExports(string prefix, string suffix, bool isDirectory)
        {
            string root = Path.Combine(Path.GetTempPath(), "yucp-startup-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string live = ExportTempCleanup.CreatePath(prefix, suffix, root);
            string dead = Path.Combine(root, prefix + "2147483647_1_" + Guid.NewGuid().ToString("N") + suffix);
            string reusedPid;
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
                reusedPid = Path.Combine(root, prefix + process.Id + "_1_" + Guid.NewGuid().ToString("N") + suffix);
            try
            {
                foreach (string path in new[] { live, dead, reusedPid })
                {
                    if (isDirectory)
                    {
                        Directory.CreateDirectory(Path.Combine(path, "nested"));
                        File.WriteAllText(Path.Combine(path, "nested", "asset"), "partial export");
                    }
                    else
                        File.WriteAllText(path, "partial export");
                }
                // Age alone must never make a running export eligible for cleanup.
                if (isDirectory)
                    Directory.SetLastWriteTimeUtc(live, DateTime.UtcNow.AddDays(-30));
                else
                    File.SetLastWriteTimeUtc(live, DateTime.UtcNow.AddDays(-30));

                ExportTempCleanup.CleanupStaleFiles(root, cleanLegacy: false, textureCache: prefix == "YUCP_Texture_");

                Assert.That(File.Exists(dead) || Directory.Exists(dead), Is.False);
                Assert.That(File.Exists(reusedPid) || Directory.Exists(reusedPid), Is.False, "A reused PID is not the original owner.");
                Assert.That(File.Exists(live) || Directory.Exists(live), Is.True);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StartupCleanup_OnlyRemovesRecognizedLegacyNames_WhenAllowed(bool cleanLegacy)
        {
            string root = Path.Combine(Path.GetTempPath(), "yucp-startup-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string id = Guid.NewGuid().ToString("N");
            string[] legacyFiles = { "YUCP_Temp_" + id + ".unitypackage", "YUCP_Temp_" + id + ".unitypackage.tmp", "YUCP_ContentWithIcon_" + id + ".unitypackage" };
            string[] unrelatedFiles = { "YUCP_Temp_notes.unitypackage", "YUCP_Temp_" + id + ".unitypackage.backup", "YUCP_Temp_999999999999999999_1_" + id + ".unitypackage", "Finished.unitypackage" };
            string[] legacyDirectories = { "YUCP_PackageExtract_" + id, "YUCP_PackageIcon_" + id, "YUCP_Obfuscation_" + id };
            string wrongType = Path.Combine(root, "YUCP_Temp_" + id + ".unitypackage.tmp");
            try
            {
                foreach (string name in legacyFiles.Concat(unrelatedFiles))
                    File.WriteAllText(Path.Combine(root, name), "test");
                foreach (string name in legacyDirectories)
                    Directory.CreateDirectory(Path.Combine(root, name));
                // A directory masquerading as a package file must also be left alone.
                File.Delete(wrongType);
                Directory.CreateDirectory(wrongType);

                ExportTempCleanup.CleanupStaleFiles(root, cleanLegacy);

                foreach (string name in legacyFiles.Where(name => Path.Combine(root, name) != wrongType))
                    Assert.That(File.Exists(Path.Combine(root, name)), Is.EqualTo(!cleanLegacy));
                foreach (string name in legacyDirectories)
                    Assert.That(Directory.Exists(Path.Combine(root, name)), Is.EqualTo(!cleanLegacy));
                foreach (string name in unrelatedFiles)
                    Assert.That(File.Exists(Path.Combine(root, name)), Is.True);
                Assert.That(Directory.Exists(wrongType), Is.True);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void StartupCleanup_RemovesLegacyTextureCopies_WithoutRemovingOtherCacheFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "yucp-startup-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string[] legacy = { "PackageIcon_-123.png", "Banner_" + Guid.NewGuid().ToString("N") + ".png" };
            string[] unrelated = { "Source.png", "YUCP_Texture_invalid_123.png", "notes.txt" };
            try
            {
                foreach (string name in legacy.Concat(unrelated)) File.WriteAllText(Path.Combine(root, name), "test");
                ExportTempCleanup.CleanupStaleFiles(root, cleanLegacy: true, textureCache: true);
                foreach (string name in legacy) Assert.That(File.Exists(Path.Combine(root, name)), Is.False);
                foreach (string name in unrelated) Assert.That(File.Exists(Path.Combine(root, name)), Is.True);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test, Platform("Win")]
        public void StartupCleanup_LockedStaleFileDoesNotBlockOthers_AndIsRetriedNextTime()
        {
            string root = Path.Combine(Path.GetTempPath(), "yucp-startup-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string locked = Path.Combine(root, "YUCP_Temp_2147483647_1_" + Guid.NewGuid().ToString("N") + ".unitypackage");
            string other = Path.Combine(root, "YUCP_Temp_2147483647_1_" + Guid.NewGuid().ToString("N") + ".unitypackage");
            try
            {
                File.WriteAllText(locked, "test");
                File.WriteAllText(other, "test");
                using (File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    LogAssert.Expect(LogType.Warning, new Regex("Could not delete temporary path '" + Regex.Escape(locked)));
                    ExportTempCleanup.CleanupStaleFiles(root, cleanLegacy: false);
                    Assert.That(File.Exists(locked), Is.True);
                    Assert.That(File.Exists(other), Is.False);
                }
                ExportTempCleanup.CleanupStaleFiles(root, cleanLegacy: false);
                Assert.That(File.Exists(locked), Is.False);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Delete_RemovesFilesAndDirectories_AndToleratesMissingPaths()
        {
            string directory = Path.Combine(Path.GetTempPath(), "yucp-cleanup-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            string file = Path.Combine(directory, "nested", "partial.unitypackage");
            File.WriteAllText(file, "partial export");
            try
            {
                ExportTempCleanup.Delete(null, "", directory + ".missing", file, directory, directory);
                Assert.That(Directory.Exists(directory), Is.False);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test, Platform("Win")]
        public void Delete_LockedFileWarns_AndDoesNotPreventRemainingCleanup()
        {
            string lockedFile = Path.GetTempFileName();
            string otherFile = Path.GetTempFileName();
            try
            {
                using (File.Open(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    LogAssert.Expect(LogType.Warning, new Regex("Could not delete temporary path '" + Regex.Escape(lockedFile)));
                    Assert.DoesNotThrow(() => ExportTempCleanup.Delete(lockedFile, otherFile));
                    Assert.That(File.Exists(lockedFile), Is.True);
                    Assert.That(File.Exists(otherFile), Is.False);
                }
            }
            finally
            {
                File.Delete(lockedFile);
                File.Delete(otherFile);
            }
        }

        [TestCase(0f)]
        [TestCase(0.68f)]
        [TestCase(0.86f)]
        [TestCase(0.9f)]
        public void ExportPackage_CleansTemporaryFiles_OnSuccessAndFailure(float failAtProgress)
        {
            // The exporter also cleans generated patch assets; do not disturb an author's workspace.
            if (Directory.Exists("Packages/com.yucp.temp"))
                Assert.Ignore("A patch authoring workspace is present.");

            string id = Guid.NewGuid().ToString("N");
            string assetFolder = "Assets/YucpCleanupTest_" + id;
            string outputFolder = Path.Combine(Path.GetTempPath(), "yucp-cleanup-output-" + id);
            string cacheFolder = Path.GetFullPath("Library/YUCP/PackageExporter/TempAssets");
            var before = FindTemporaryPaths(cacheFolder);
            var observed = new HashSet<string>();
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            var icon = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Directory.CreateDirectory(assetFolder);
                File.WriteAllText(assetFolder + "/Content.txt", "Export cleanup regression test");
                AssetDatabase.ImportAsset(assetFolder + "/Content.txt", ImportAssetOptions.ForceSynchronousImport);
                icon.Apply();
                profile.packageName = "CleanupTest_" + id;
                profile.foldersToExport.Add(assetFolder);
                profile.exportPath = outputFolder;
                profile.generatePackageJson = false;
                profile.embedYucpMetadata = false;
                profile.icon = icon;

                if (failAtProgress > 0)
                {
                    if (failAtProgress == 0.68f)
                        LogAssert.Expect(LogType.Error, "[PackageBuilder] ExportPackage threw exception: Injected cleanup test failure");
                    LogAssert.Expect(LogType.Error, new Regex("\\[PackageBuilder\\] Export failed .*Injected cleanup test failure"));
                    LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Injected cleanup test failure"));
                }

                var result = PackageBuilder.ExportPackage(profile, (progress, status) =>
                {
                    if (progress == 0.68f || progress == 0.86f || progress == 0.9f)
                        observed.UnionWith(FindTemporaryPaths(cacheFolder).Except(before));
                    if (failAtProgress > 0 && progress == failAtProgress)
                        throw new InvalidOperationException("Injected cleanup test failure");
                });

                Assert.That(result.success, Is.EqualTo(failAtProgress == 0));
                Assert.That(observed.Any(path => Path.GetFileName(path).StartsWith("YUCP_Temp_")), Is.True);
                if (failAtProgress != 0.68f)
                {
                    Assert.That(observed.Any(path => Path.GetFileName(path).StartsWith("YUCP_ContentWithIcon_")), Is.True);
                    Assert.That(observed.Any(path => path.EndsWith(".png")), Is.True);
                }
                Assert.That(FindTemporaryPaths(cacheFolder).Except(before), Is.Empty);
                Assert.That(observed.Where(path => File.Exists(path) || Directory.Exists(path)), Is.Empty);
                if (failAtProgress == 0 || failAtProgress == 0.9f)
                    Assert.That(File.Exists(profile.GetOutputFilePath()), Is.True, "Cleanup must preserve the exported package.");
            }
            finally
            {
                ExportTempCleanup.Delete(FindTemporaryPaths(cacheFolder).Except(before).ToArray());
                ExportTempCleanup.Delete(outputFolder);
                AssetDatabase.DeleteAsset(assetFolder);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(icon);
            }
        }

        [Test, Platform("Win")]
        public void RepackingFailure_RemovesIntermediateArchiveAndExtractionDirectory()
        {
            string packagePath = Path.Combine(Path.GetTempPath(), "yucp-repack-test-" + Guid.NewGuid().ToString("N") + ".unitypackage");
            var before = Directory.GetDirectories(Path.GetTempPath(), "YUCP_PackageExtract_*");
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            FileStream originalLock = null;
            try
            {
                profile.embedYucpMetadata = false;
                typeof(PackageBuilder).GetMethod("CreateEmptyUnityPackage", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { packagePath });
                Type contextType = typeof(PackageBuilder).GetNestedType("PackageEmbedContext", BindingFlags.NonPublic);
                object context = Activator.CreateInstance(contextType, new object[] { profile, null });
                Action<float, string> lockOriginalBeforeRepacking = (progress, status) =>
                {
                    if (progress == 0.799f)
                        originalLock = File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                };

                var exception = Assert.Throws<TargetInvocationException>(() =>
                    typeof(PackageBuilder).GetMethod("InjectPackageJsonInstallerAndBundles", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { packagePath, null, new Dictionary<string, string>(), new List<AssemblyObfuscationSettings>(), profile, false, null, context, lockOriginalBeforeRepacking }));

                Assert.That(originalLock, Is.Not.Null, "The test must reach repacking.");
                Assert.That(exception.InnerException, Is.InstanceOf<IOException>());
                Assert.That(File.Exists(packagePath), Is.True);
                Assert.That(File.Exists(packagePath + ".tmp"), Is.False);
                Assert.That(Directory.GetDirectories(Path.GetTempPath(), "YUCP_PackageExtract_*").Except(before), Is.Empty);
            }
            finally
            {
                originalLock?.Dispose();
                ExportTempCleanup.Delete(packagePath, packagePath + ".tmp");
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        private static HashSet<string> FindTemporaryPaths(string cacheFolder)
        {
            string owner;
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
                owner = $"_{process.Id}_{process.StartTime.ToUniversalTime().Ticks}_";
            var paths = new HashSet<string>(Directory.GetFileSystemEntries(Path.GetTempPath(), "YUCP_*")
                .Where(path => Path.GetFileName(path).Contains(owner)));
            if (Directory.Exists(cacheFolder))
                paths.UnionWith(Directory.GetFiles(cacheFolder).Where(path => Path.GetFileName(path).Contains(owner)));
            return paths;
        }
    }
}
