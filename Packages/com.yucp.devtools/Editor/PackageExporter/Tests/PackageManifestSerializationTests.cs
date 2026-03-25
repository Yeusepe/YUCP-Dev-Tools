using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PackageVerifierData = YUCP.Importer.Editor.PackageVerifier.Data;
using PackageSigningData = YUCP.DevTools.Editor.PackageSigning.Data;
using YUCP.DevTools.Editor.PackageSigning.Core;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class PackageManifestSerializationTests
    {
        [Test]
        public void SerializeManifest_PreservesNullOptionalCertificateFields_AndFileHashes()
        {
            var manifest = new PackageSigningData.PackageManifest
            {
                authorityId = "unitysign.yucp",
                keyId = "yucp-authority-2025",
                publisherId = "publisher-123",
                packageId = "package-123",
                version = "1.0.0",
                archiveSha256 = "abc123",
                vrchatAuthorUserId = "",
                gumroadProductId = "gumroad-product",
                jinxxyProductId = "",
                fileHashes = new Dictionary<string, string>(),
                certificateChain = new[]
                {
                    new PackageVerifierData.CertificateData
                    {
                        keyId = "yucp-publisher:test",
                        publicKey = "publisher-public-key",
                        signature = "publisher-signature",
                        issuerKeyId = "yucp-root-2025",
                        certificateType = PackageVerifierData.CertificateType.Publisher,
                        publisherId = "publisher-123",
                        notBefore = "2026-03-24T00:00:00.000Z",
                        notAfter = "2026-06-24T00:00:00.000Z",
                    },
                    new PackageVerifierData.CertificateData
                    {
                        keyId = "yucp-root-2025",
                        publicKey = "root-public-key",
                        certificateType = PackageVerifierData.CertificateType.Root,
                        signature = null,
                        issuerKeyId = null,
                        publisherId = null,
                        notBefore = null,
                        notAfter = null,
                    },
                },
            };

            var serializeMethod = typeof(PackageSigningJson).GetMethod(
                "SerializeManifest",
                BindingFlags.Public | BindingFlags.Static
            );

            Assert.That(serializeMethod, Is.Not.Null);

            string json = serializeMethod.Invoke(null, new object[] { manifest }) as string;

            Assert.That(json, Is.Not.Null);
            StringAssert.Contains("\"fileHashes\": {}", json);
            StringAssert.Contains("\"certificateType\": \"Root\"", json);
            StringAssert.Contains("\"issuerKeyId\": null", json);
            StringAssert.Contains("\"signature\": null", json);
            StringAssert.DoesNotContain("\"issuerKeyId\": \"\"", json);
            StringAssert.DoesNotContain("\"signature\": \"\"", json);
        }
    }
}
