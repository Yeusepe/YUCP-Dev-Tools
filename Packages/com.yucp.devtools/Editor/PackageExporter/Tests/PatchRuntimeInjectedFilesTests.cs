using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class PatchRuntimeInjectedFilesTests
    {
        [Test]
        public void PatchRuntimeInjectedFiles_BinaryAndMetaExist()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(
                projectRoot,
                "Packages",
                "com.yucp.devtools",
                "Editor",
                "PackageExporter",
                "Binaries",
                "YUCP.PatchRuntime.dll");

            Assert.That(File.Exists(dllPath), Is.True, "Expected the checked-in patch runtime binary to exist.");
            Assert.That(File.Exists(dllPath + ".meta"), Is.True, "Expected the checked-in patch runtime binary to have a Unity .meta file.");
        }

        [Test]
        public void PatchRuntimeInjectedFiles_InjectsPatchRuntimeBinaryIntoTempEditorFolder()
        {
            MethodInfo method = typeof(PackageBuilder).GetMethod(
                "TryInjectPrecompiledPatchRuntime",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            string tempExtractDir = Path.Combine(Path.GetTempPath(), "yucp-precompiled-patch-runtime-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempExtractDir);

            try
            {
                bool injected = (bool)method.Invoke(null, new object[] { tempExtractDir });
                Assert.That(injected, Is.True, "Expected the patch runtime binary to be emitted.");

                string[] pathnameFiles = Directory.GetFiles(tempExtractDir, "pathname", SearchOption.AllDirectories);
                string matchedPathnameFile = System.Array.Find(pathnameFiles, pathnameFile =>
                {
                    string pathname = File.ReadAllText(pathnameFile).Replace('\\', '/');
                    return string.Equals(
                        pathname,
                        "Packages/com.yucp.temp/Editor/YUCP.PatchRuntime.dll",
                        System.StringComparison.Ordinal);
                });

                Assert.That(matchedPathnameFile, Is.Not.Null, "Expected an emitted patch runtime DLL pathname.");
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
        public void PatchRuntimeInjectedFiles_ResolvesDerivedFbxAssetScriptReferenceFromBinary()
        {
            MethodInfo method = typeof(PackageBuilder).GetMethod(
                "TryGetPatchRuntimeScriptReference",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            object[] args = { null, 0L };
            bool resolved = (bool)method.Invoke(null, args);

            Assert.That(resolved, Is.True, "Expected to resolve a DLL-backed DerivedFbxAsset script reference.");
            Assert.That(args[0] as string, Is.Not.Null.And.Not.Empty);
            Assert.That((long)args[1], Is.GreaterThan(0));
        }

        [Test]
        public void PatchRuntimeInjectedFiles_HDiffPatchWrapperStaysSelfContained()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string wrapperPath = Path.Combine(
                projectRoot,
                "build-src",
                "YUCP.PatchRuntime",
                "Core",
                "HDiffPatchWrapper.cs");

            string wrapperSource = File.ReadAllText(wrapperPath);

            Assert.That(wrapperSource, Does.Not.Contain("using YUCP.DevTools.Editor.Security;"));
            Assert.That(wrapperSource, Does.Not.Contain("TrustedFileUtility."));
        }

        [Test]
        public void PatchRuntimeInjectedFiles_OnLoadProcessingSkipsInAuthoringWorkspace()
        {
            MethodInfo method = typeof(YUCP.PatchCleanup.YUCPPatchImporter).GetMethod(
                "ShouldSkipPatchProcessing",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            bool shouldSkip = (bool)method.Invoke(null, new object[] { "on load", null });

            Assert.That(shouldSkip, Is.True);
        }
    }
}
