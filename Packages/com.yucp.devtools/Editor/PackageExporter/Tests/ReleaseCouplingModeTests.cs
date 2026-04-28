using System.Reflection;
using NUnit.Framework;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class ServerFirstExportTests
    {
        [Test]
        public void RequiresLicenseVerification_IsDetectedForServerFirstExport()
        {
            var profile = UnityEngine.ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                profile.requiresLicenseVerification = true;
                var requiresLicenseVerification = typeof(PackageBuilder).GetMethod(
                    "RequiresLicenseVerification",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(requiresLicenseVerification, Is.Not.Null);
                Assert.That((bool)requiresLicenseVerification.Invoke(null, new object[] { profile }), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void RequiresLicenseVerification_DoesNotSkipDerivedFbxPatchAuthoring()
        {
            var profile = UnityEngine.ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                profile.requiresLicenseVerification = true;
                var shouldRequireDerivedFbxServerUnlock = typeof(PackageBuilder).GetMethod(
                    "ShouldRequireDerivedFbxServerUnlock",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(shouldRequireDerivedFbxServerUnlock, Is.Not.Null);
                Assert.That((bool)shouldRequireDerivedFbxServerUnlock.Invoke(null, new object[] { profile }), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }
    }
}
