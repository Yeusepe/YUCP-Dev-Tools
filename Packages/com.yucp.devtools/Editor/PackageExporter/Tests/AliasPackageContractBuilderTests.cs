using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class AliasPackageContractBuilderTests
    {
        [Test]
        public void GeneratePackageJson_NeverWritesAliasContract_EvenForLicensedPublishedPackage()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                // Even a fully-configured licensed + published profile must not bake server-authorized
                // delivery into the exported package — only the server authorizes that at install time.
                profile.packageName = "Alias Contract";
                profile.version = "1.2.3";
                profile.packageId = "creator.alias";
                profile.publishChannel = "beta";
                profile.licenseProductId = "product-primary";
                profile.licenseProductIds.Add("product-secondary");
                profile.requiresLicenseVerification = true;
                profile.publishReleaseAfterExport = true;

                var dependencies = new List<PackageDependency>
                {
                    new PackageDependency
                    {
                        packageName = "com.yucp.importer",
                        packageVersion = "0.4.0",
                        versionMode = DependencyVersionMode.Latest,
                        enabled = true,
                        exportMode = DependencyExportMode.Dependency,
                        isVpmDependency = true,
                    }
                };

                JObject packageJson = JObject.Parse(DependencyScanner.GeneratePackageJson(profile, dependencies));

                Assert.That(packageJson["yucp"], Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void GeneratePackageJson_OmitsAliasContractWhenPackageIdIsMissing()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                profile.packageName = "Alias Contract";
                profile.version = "1.2.3";

                JObject packageJson = JObject.Parse(
                    DependencyScanner.GeneratePackageJson(profile, new List<PackageDependency>()));

                Assert.That(packageJson["yucp"], Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
