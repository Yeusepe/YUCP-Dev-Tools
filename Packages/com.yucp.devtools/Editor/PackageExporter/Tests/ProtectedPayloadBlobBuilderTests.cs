using System.Reflection;
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
    }
}
