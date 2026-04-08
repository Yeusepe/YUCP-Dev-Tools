using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class PackageIconSourcePathTests
    {
        [Test]
        public void ResolvePackageIconSourcePath_CreatesPngPayloadForMemoryTexture()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            string iconPath = null;

            try
            {
                texture.SetPixel(0, 0, Color.magenta);
                texture.SetPixel(1, 0, Color.cyan);
                texture.SetPixel(0, 1, Color.yellow);
                texture.SetPixel(1, 1, Color.white);
                texture.Apply(false, false);

                var method = typeof(PackageBuilder).GetMethod(
                    "ResolvePackageIconSourcePath",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(method, Is.Not.Null);

                iconPath = method.Invoke(null, new object[] { texture, "UnitTestIcon" }) as string;

                Assert.That(iconPath, Is.Not.Null.And.Not.Empty);
                Assert.That(iconPath, Does.EndWith(".png"));
                Assert.That(File.Exists(iconPath), Is.True);
            }
            finally
            {
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    File.Delete(iconPath);
                }

                Object.DestroyImmediate(texture);
            }
        }
    }
}
