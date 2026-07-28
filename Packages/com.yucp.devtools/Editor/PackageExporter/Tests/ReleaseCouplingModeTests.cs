using System.Reflection;
using NUnit.Framework;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class ServerFirstExportTests
    {
        /// <summary>
        /// License-locked direct exports are retired: in-Unity license verification
        /// was removed in the native broker cutover, so a direct export embedding a
        /// license requirement produced a package nobody could import. The profile
        /// flag still accepts writes (the dormant UI is kept for a future return)
        /// but must always read false, so no profile can produce a locked export.
        /// </summary>
        [Test]
        public void RequiresLicenseVerification_IsRetiredAndAlwaysReadsFalse()
        {
            var profile = UnityEngine.ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                profile.requiresLicenseVerification = true;

                Assert.That(profile.requiresLicenseVerification, Is.False);

                var requiresLicenseVerification = typeof(PackageBuilder).GetMethod(
                    "RequiresLicenseVerification",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(requiresLicenseVerification, Is.Not.Null);
                Assert.That(
                    (bool)requiresLicenseVerification.Invoke(null, new object[] { profile }),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void RetiredLicenseFlag_DoesNotTriggerDerivedFbxServerUnlock()
        {
            var profile = UnityEngine.ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                profile.requiresLicenseVerification = true;
                var shouldRequireDerivedFbxServerUnlock = typeof(PackageBuilder).GetMethod(
                    "ShouldRequireDerivedFbxServerUnlock",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(shouldRequireDerivedFbxServerUnlock, Is.Not.Null);
                Assert.That(
                    (bool)shouldRequireDerivedFbxServerUnlock.Invoke(null, new object[] { profile }),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }
    }
}
