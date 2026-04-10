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

        [Test]
        public void SyntheticProtectedPayloadProfile_KeepsDerivedPatchLicenseGate()
        {
            var original = UnityEngine.ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                original.requiresLicenseVerification = true;
                original.packageId = "pkg-protected-import";

                var createClone = typeof(PackageBuilder).GetMethod(
                    "CreateSyntheticProtectedPayloadProfile",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var anyRuntimeGate = typeof(PackageBuilder).GetMethod(
                    "AnyProfileUsesProtectedPayloadRuntime",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var derivedGate = typeof(PackageBuilder).GetMethod(
                    "ShouldRequireDerivedFbxServerUnlock",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(createClone, Is.Not.Null);
                Assert.That(anyRuntimeGate, Is.Not.Null);
                Assert.That(derivedGate, Is.Not.Null);

                var clone = createClone.Invoke(null, new object[] { original }) as ExportProfile;
                Assert.That(clone, Is.Not.Null);

                Assert.That((bool)anyRuntimeGate.Invoke(null, new object[] { original }), Is.True);
                Assert.That((bool)derivedGate.Invoke(null, new object[] { original }), Is.True);
                Assert.That((bool)anyRuntimeGate.Invoke(null, new object[] { clone }), Is.True);
                Assert.That((bool)derivedGate.Invoke(null, new object[] { clone }), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(original);
            }
        }
    }
}
