using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class PatchRuntimeInjectedFilesTests
    {
        [Test]
        public void PatchRuntimeInjectedFiles_OnlyIncludeCompileSafeSupportFiles()
        {
            FieldInfo field = typeof(PackageBuilder).GetField(
                "s_patchRuntimeInjectedFiles",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var injectedFiles = ((IEnumerable)field.GetValue(null))
                .Cast<object>()
                .Select(item => new
                {
                    sourcePath = item.GetType().GetField("sourcePath")?.GetValue(item) as string,
                    targetPath = item.GetType().GetField("targetPath")?.GetValue(item) as string,
                })
                .ToArray();

            Assert.That(injectedFiles.Any(file =>
                file.sourcePath == "Packages/com.yucp.devtools/Editor/PackageExporter/Core/EmbeddedTextEncodingUtility.cs" &&
                file.targetPath == "Packages/com.yucp.temp/Editor/EmbeddedTextEncodingUtility.cs"), Is.True);

            Assert.That(injectedFiles.Any(file =>
                file.sourcePath == "Packages/com.yucp.devtools/Editor/PackageExporter/Core/ProtectedAssetUnlockService.cs" &&
                file.targetPath == "Packages/com.yucp.temp/Editor/ProtectedAssetUnlockService.cs"), Is.False);
        }
    }
}
