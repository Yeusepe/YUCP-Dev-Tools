using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class AliasPackageContractBuilderTests
    {
        [Test]
        public void GeneratePackageJson_WritesAliasContractAndMinimumImporterVersion()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                profile.packageName = "Alias Contract";
                profile.version = "1.2.3";
                profile.packageId = "creator.alias";
                profile.publishChannel = "beta";
                profile.licenseProductId = "product-primary";
                profile.licenseProductIds.Add("product-secondary");

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
                JObject yucp = packageJson["yucp"] as JObject;

                Assert.That(yucp, Is.Not.Null);
                Assert.That((string)yucp["kind"], Is.EqualTo("alias-v1"));
                Assert.That((string)yucp["aliasId"], Is.EqualTo("creator.alias"));
                Assert.That((string)yucp["installStrategy"], Is.EqualTo("server-authorized"));
                Assert.That((string)yucp["importerPackage"], Is.EqualTo("com.yucp.importer"));
                Assert.That((string)yucp["minImporterVersion"], Is.EqualTo("0.4.0"));
                Assert.That((string)yucp["channel"], Is.EqualTo("beta"));
                CollectionAssert.AreEquivalent(
                    new[] { "product-secondary", "product-primary" },
                    yucp["catalogProductIds"]?.Values<string>());
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
