using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class PackageExporterWindowProfileDetailsTests
    {
        [Test]
        public void UpdateProfileDetails_DoesNotThrow_WhenUiHasNotBeenBuiltYet()
        {
            var window = ScriptableObject.CreateInstance<YUCPPackageExporterWindow>();
            var profile = ScriptableObject.CreateInstance<ExportProfile>();

            try
            {
                typeof(YUCPPackageExporterWindow)
                    .GetField("selectedProfile", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(window, profile);

                Assert.DoesNotThrow(() => window.UpdateProfileDetails());
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(window);
            }
        }
    }
}
