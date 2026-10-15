using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class InstalledPackageDependencyFilterTests
    {
        private const string VrcSdkDll = "Packages/com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A.dll";

        [Test]
        public void SkipsOtherInstalledPackageFilesUnlessOptedIn()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();

            try
            {
                profile.foldersToExport = new List<string> { "Assets/MyAvatar", "Packages/com.creator.tool/Runtime" };
                var exported = AssetCollector.GetExportedPackageNames(profile);

                Assert.That(AssetCollector.GetSkippedPackageName(VrcSdkDll, profile, exported), Is.EqualTo("com.vrchat.avatars"));
                Assert.That(AssetCollector.GetSkippedPackageName("Assets/MyAvatar/Body.fbx", profile, exported), Is.Null);
                // The profile's own package and YUCP's injected runtime still ship
                Assert.That(AssetCollector.GetSkippedPackageName("Packages/com.creator.tool/Shared/Util.cs", profile, exported), Is.Null);
                Assert.That(AssetCollector.GetSkippedPackageName("Packages/com.yucp.temp/Editor/YUCP.PatchRuntime.dll", profile, exported), Is.Null);

                profile.bundleInstalledPackageFiles = true;
                Assert.That(AssetCollector.GetSkippedPackageName(VrcSdkDll, profile, exported), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
