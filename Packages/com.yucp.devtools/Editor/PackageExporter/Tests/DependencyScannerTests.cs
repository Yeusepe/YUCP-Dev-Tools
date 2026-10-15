using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class DependencyScannerTests
    {
        [TestCase("{ 'name': 'com.example.shader', 'vpmDependencies': {} }", true)]
        [TestCase("{ 'name': 'com.example.registry' }", false)]
        public void VpmClassificationRecognizesPackagesImportedWithoutAVpmLockEntry(string manifest, bool expected)
        {
            string folder = Path.Combine(Path.GetTempPath(), "yucp-vpm-classification-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, "package.json"), manifest);
                Assert.That(DependencyScanner.HasVpmManifest(folder), Is.EqualTo(expected));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Test]
        public void ExplicitRepositoryCorrectsAnOldUpmClassificationDuringExport()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                profile.packageName = "com.example.export";
                profile.version = "1.0.0";
                var dependency = new PackageDependency("com.example.shader", "1.2.3", "Shader", false)
                {
                    enabled = true,
                    vpmRepositoryUrl = "https://example.com/index.json"
                };
                var manifest = JObject.Parse(DependencyScanner.GeneratePackageJson(profile, new List<PackageDependency> { dependency }));
                Assert.That(manifest["dependencies"], Is.Null);
                Assert.That((string)manifest["vpmDependencies"][dependency.packageName], Is.EqualTo(">=0.0.0"));
                Assert.That((string)manifest["vpmRepositories"][dependency.packageName], Is.EqualTo(dependency.vpmRepositoryUrl));
                Assert.That(dependency.isVpmDependency, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void ScanIncludesRegisteredCachePackagesAndTheirResolvedPaths()
        {
            var registered = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            Assert.That(registered.Any(p => p.source == PackageSource.Registry), Is.True);
            var scanned = DependencyScanner.ScanInstalledPackages();
            foreach (var package in registered)
            {
                var found = scanned.Single(p => p.packageName == package.name);
                Assert.That(found.packagePath, Is.EqualTo(package.resolvedPath));
                Assert.That(found.version, Is.EqualTo(package.version));
                Assert.That(found.source, Is.EqualTo(package.source));
            }
        }

        [Test]
        public void ScanExcludesBuiltinsButRetainsUnityRegistryPackages()
        {
            var packages = new List<DependencyScanner.PackageInfo>
            {
                new DependencyScanner.PackageInfo("com.unity.modules.audio", "1.0.0", "Audio", "", false, "") { source = PackageSource.BuiltIn },
                new DependencyScanner.PackageInfo("com.unity.textmeshpro", "3.0.6", "TextMeshPro", "", false, "") { source = PackageSource.Registry }
            };
            Assert.That(DependencyScanner.ConvertToPackageDependencies(packages).Select(p => p.packageName),
                Is.EqualTo(new[] { "com.unity.textmeshpro" }));
        }

        [TestCase("0.2.9", "^0.2.3", true)]
        [TestCase("0.3.0", "^0.2.3", false)]
        [TestCase("1.5.0", ">=1.0.0 <2.0.0", true)]
        [TestCase("2.0.0", ">=1.0.0 <2.0.0", false)]
        [TestCase("1.2.3-beta.1", ">=1.0.0", false)]
        [TestCase("garbage", "*", false)]
        public void VersionRangesFollowSemanticVersioning(string version, string requirement, bool matches)
        {
            Assert.That(DependencyScanner.VersionSatisfiesRequirement(version, requirement), Is.EqualTo(matches));
        }

        [Test]
        public void StableReleaseSortsAfterPrerelease()
        {
            Assert.That(DependencyScanner.CompareVersions("1.2.3", "1.2.3-beta.9"), Is.GreaterThan(0));
            Assert.That(DependencyScanner.CompareVersions("1.2.3-beta.10", "1.2.3-beta.2"), Is.GreaterThan(0));
        }

        [Test]
        public void ManifestRemovesDeselectedDependenciesAndHonorsSpecificVersions()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            string manifest = Path.GetTempFileName();
            try
            {
                profile.packageName = "com.example.export";
                profile.version = "1.0.0";
                File.WriteAllText(manifest, "{ 'dependencies': { 'old': '1.0.0' }, 'vpmDependencies': { 'old': '*' }, 'vpmRepositories': { 'old': 'https://example.com' } }");
                var dependency = new PackageDependency("com.example.dependency", "1.0.0", "Example", false)
                {
                    enabled = true,
                    versionMode = DependencyVersionMode.Specific,
                    specificVersion = "2.3.4"
                };
                var generated = JObject.Parse(DependencyScanner.GeneratePackageJson(profile, new List<PackageDependency> { dependency }, manifest));
                Assert.That((string)generated["dependencies"][dependency.packageName], Is.EqualTo("2.3.4"));
                Assert.That(generated["dependencies"]["old"], Is.Null);
                Assert.That(generated["vpmDependencies"], Is.Null);
                Assert.That(generated["vpmRepositories"], Is.Null);

                dependency.enabled = false;
                generated = JObject.Parse(DependencyScanner.GeneratePackageJson(profile, new List<PackageDependency> { dependency }, manifest));
                Assert.That(generated["dependencies"], Is.Null);
            }
            finally
            {
                File.Delete(manifest);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ExplicitVpmRepositoryPreservesDependencyWithoutAnOnlineLookup()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                profile.packageName = "com.example.export";
                profile.version = "1.0.0";
                var dependency = new PackageDependency("com.example.vpm", "1.0.0", "Example", true)
                {
                    enabled = true,
                    vpmRepositoryUrl = "https://example.com/repository.json"
                };
                var generated = JObject.Parse(DependencyScanner.GeneratePackageJson(profile, new List<PackageDependency> { dependency }));
                Assert.That((string)generated["vpmDependencies"][dependency.packageName], Is.EqualTo(">=0.0.0"));
                Assert.That((string)generated["vpmRepositories"][dependency.packageName], Is.EqualTo(dependency.vpmRepositoryUrl));
                dependency.vpmRepositoryUrl = "file:///not-a-repository";
                Assert.Throws<InvalidOperationException>(() => DependencyScanner.GeneratePackageJson(profile, new List<PackageDependency> { dependency }));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void AutoDetectionFindsScriptableObjectReferencesWithoutClearingManualChoices()
        {
            string folder = "Assets/YUCPDependencyTest-" + Guid.NewGuid().ToString("N");
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ExportProfile>(), folder + "/Subject.asset");
                profile.foldersToExport = new List<string> { folder };
                var detected = new PackageDependency("com.yucp.devtools", "1.0.0", "DevTools", false);
                var manual = new PackageDependency("com.example.manual", "1.0.0", "Manual", false) { enabled = true };
                profile.dependencies = new List<PackageDependency> { detected, manual };
                DependencyScanner.AutoDetectUsedDependencies(profile);
                Assert.That(detected.enabled, Is.True);
                Assert.That(manual.enabled, Is.True);
                Assert.That(EditorUtility.IsDirty(profile), Is.True);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void RepositoryCacheObservesLocalFileChanges()
        {
            string path = Path.GetTempFileName();
            try
            {
                Type type = typeof(DependencyScanner).GetNestedType("VpmRepositorySource", BindingFlags.NonPublic);
                object source = Activator.CreateInstance(type);
                type.GetField("localPath").SetValue(source, path);
                MethodInfo load = typeof(DependencyScanner).GetMethod("TryLoadRepoJson", BindingFlags.NonPublic | BindingFlags.Static);
                object[] arguments = { source, null };
                File.WriteAllText(path, "{ 'revision': 1 }");
                Assert.That(load.Invoke(null, arguments), Is.True);
                Assert.That((int)((JObject)arguments[1])["revision"], Is.EqualTo(1));
                File.WriteAllText(path, "{ 'revision': 2 }");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
                Assert.That(load.Invoke(null, arguments), Is.True);
                Assert.That((int)((JObject)arguments[1])["revision"], Is.EqualTo(2));
            }
            finally { File.Delete(path); }
        }

        [TestCase("2147483648.0.0", false)]
        [TestCase("1.2.3-beta.1+build.2", true)]
        [TestCase("2026.10.13.4", true)]
        [TestCase("1", true)]
        [TestCase("1.2", true)]
        [TestCase("not-a-version", false)]
        public void VersionUtilityValidatesWithoutThrowing(string version, bool valid)
        {
            Assert.That(VersionUtility.IsValidVersion(version), Is.EqualTo(valid));
        }

        [Test]
        public void MissingIncludedProfilesAreReportedThroughSerializedData()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                using (var serialized = new SerializedObject(profile))
                {
                    var guids = serialized.FindProperty("includedProfileGuids");
                    guids.arraySize = 1;
                    guids.GetArrayElementAtIndex(0).stringValue = Guid.NewGuid().ToString("N");
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                Assert.That(CompositeProfileResolver.ValidateIncludedProfiles(profile, out var errors), Is.False);
                Assert.That(errors.Any(error => error.Contains("missing or deleted")), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void InstallerContainsNoGuardianOrForcedReloadTypes()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "YUCP.DirectVpmInstaller.Runtime");
            Assert.That(assembly.GetTypes().Any(t => t.FullName.Contains("Guardian") || t.Name == "FullDomainReload"), Is.False);
        }

        [Test]
        public void UnityDependencyPreflightRejectsUrlsAndFindsMissingRegistryPackages()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "YUCP.DirectVpmInstaller.Runtime");
            var method = assembly.GetType("YUCP.DirectVpmInstaller.DirectVpmInstaller")
                .GetMethod("GetMissingUnityDependencies", BindingFlags.NonPublic | BindingFlags.Static);
            var dependencies = JObject.Parse("{ 'com.example.missing': '1.2.3' }");
            Assert.That((List<string>)method.Invoke(null, new object[] { dependencies }), Is.EqualTo(new[] { "com.example.missing@1.2.3" }));
            dependencies["com.example.missing"] = "https://example.com/repository.git";
            Assert.That(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { dependencies })).InnerException,
                Is.TypeOf<InvalidDataException>());
        }
    }
}
