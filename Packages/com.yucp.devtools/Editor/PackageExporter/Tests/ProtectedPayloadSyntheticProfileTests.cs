using System.Reflection;
using NUnit.Framework;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class ProtectedPayloadSyntheticProfileTests
    {
        [Test]
        public void CreateSyntheticProtectedPayloadProfile_PreservesLicenseGateButSkipsNestedPayloadWrap()
        {
            var original = UnityEngine.ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                original.requiresLicenseVerification = true;
                original.packageId = "pkg-protected-import";

                var method = typeof(PackageBuilder).GetMethod(
                    "CreateSyntheticProtectedPayloadProfile",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(method, Is.Not.Null, "Expected PackageBuilder.CreateSyntheticProtectedPayloadProfile to exist.");

                var clone = method.Invoke(null, new object[] { original }) as ExportProfile;
                Assert.That(clone, Is.Not.Null);
                Assert.That(clone, Is.Not.SameAs(original));
                Assert.That(clone.requiresLicenseVerification, Is.True);
                Assert.That(clone.packageId, Is.EqualTo("pkg-protected-import"));
                Assert.That(clone.UsesProtectedPayload(), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(original);
            }
        }
    }
}
