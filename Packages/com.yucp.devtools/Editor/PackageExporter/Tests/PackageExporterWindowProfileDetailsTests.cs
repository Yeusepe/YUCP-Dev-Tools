using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.DevTools.Editor.PackageSigning.UI;

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

        [Test]
        public void CreateMetadataSection_DoesNotRenderPackageRegistry()
        {
            var window = ScriptableObject.CreateInstance<YUCPPackageExporterWindow>();
            var profile = ScriptableObject.CreateInstance<ExportProfile>();

            try
            {
                var metadataSection = typeof(YUCPPackageExporterWindow)
                    .GetMethod("CreateMetadataSection", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(window, new object[] { profile }) as VisualElement;

                Assert.That(metadataSection, Is.Not.Null);
                Assert.That(ContainsLabel(metadataSection, "Package Registry"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PackageSigningTab_RendersPackageRegistry_WhenProfileIsProvided()
        {
            var profile = ScriptableObject.CreateInstance<ExportProfile>();

            try
            {
                var signingTab = new PackageSigningTab(profile);
                VisualElement root = signingTab.CreateUI();

                Assert.That(root, Is.Not.Null);
                Assert.That(ContainsLabel(root, "Package Registry"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static bool ContainsLabel(VisualElement root, string text)
        {
            if (root == null)
            {
                return false;
            }

            foreach (VisualElement element in root.Query<VisualElement>().ToList())
            {
                if (element is Label label && label.text == text)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
