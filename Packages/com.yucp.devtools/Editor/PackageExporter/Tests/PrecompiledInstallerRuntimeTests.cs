using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
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
    }
}
