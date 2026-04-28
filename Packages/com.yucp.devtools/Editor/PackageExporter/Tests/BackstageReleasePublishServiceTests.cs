using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class BackstageReleasePublishServiceTests
    {
        [Test]
        public void CollectCatalogProductIds_DeduplicatesAndPreservesPrimaryFallback()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                profile.licenseProductIds.Add("prod_primary");
                profile.licenseProductIds.Add(" prod_secondary ");
                profile.licenseProductIds.Add("prod_primary");
                profile.licenseProductId = "prod_secondary";

                var ids = BackstageReleasePublishService.CollectCatalogProductIds(profile);

                CollectionAssert.AreEqual(new[] { "prod_primary", "prod_secondary" }, ids);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void BuildMetadata_UsesProfileDescriptionAndMinimumUnityVersion()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                profile.description = "Compact server-first delivery";
                profile.minimumUnityVersion = "2022.3";

                var metadata = BackstageReleasePublishService.BuildMetadata(profile);

                Assert.That(metadata, Is.Not.Null);
                Assert.That(metadata.description, Is.EqualTo("Compact server-first delivery"));
                Assert.That(metadata.unity, Is.EqualTo("2022.3"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Validate_FailsWhenBackstagePublishingHasNoLicenseProducts()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                profile.packageName = "ServerFirst";
                profile.version = "1.2.3";
                profile.foldersToExport.Add(Application.dataPath);
                profile.publishReleaseAfterExport = true;

                bool isValid = profile.Validate(out string errorMessage);

                Assert.That(isValid, Is.False);
                Assert.That(errorMessage, Does.Contain("canonical YUCP product ID"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void InferContentType_MapsUnityPackageToOctetStream()
        {
            Assert.That(
                BackstageReleasePublishService.InferContentType(@"C:\Exports\example.unitypackage"),
                Is.EqualTo("application/octet-stream")
            );
            Assert.That(
                BackstageReleasePublishService.InferContentType(@"C:\Exports\example.zip"),
                Is.EqualTo("application/zip")
            );
        }
    }
}
