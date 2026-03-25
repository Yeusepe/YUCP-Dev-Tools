using System.Reflection;
using NUnit.Framework;
using PackageVerifierData = YUCP.Importer.Editor.PackageVerifier.Data;
using PackageSigningData = YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class PackageBuilderSigningResponseTests
    {
        [Test]
        public void ParseSigningResponseJson_PreservesCertificateTypes_FromStringValues()
        {
            const string responseJson = "{"
                + "\"success\":true,"
                + "\"algorithm\":\"Ed25519\","
                + "\"keyId\":\"yucp-publisher:test-nonce\","
                + "\"certificateIndex\":0,"
                + "\"certificateChain\":["
                + "{"
                + "\"keyId\":\"yucp-publisher:test-nonce\","
                + "\"publicKey\":\"publisher-public-key\","
                + "\"signature\":\"publisher-signature\","
                + "\"issuerKeyId\":\"yucp-root-2025\","
                + "\"certificateType\":\"Publisher\","
                + "\"publisherId\":\"publisher-123\","
                + "\"notBefore\":\"2026-03-24T00:00:00.000Z\","
                + "\"notAfter\":\"2026-06-24T00:00:00.000Z\""
                + "},"
                + "{"
                + "\"keyId\":\"yucp-root-2025\","
                + "\"publicKey\":\"root-public-key\","
                + "\"certificateType\":\"Root\""
                + "}"
                + "]"
                + "}";

            var parseMethod = typeof(PackageBuilder).GetMethod(
                "ParseSigningResponseJson",
                BindingFlags.NonPublic | BindingFlags.Static
            );

            Assert.That(parseMethod, Is.Not.Null, "Expected PackageBuilder.ParseSigningResponseJson to exist.");

            var parsed = parseMethod.Invoke(null, new object[] { responseJson }) as PackageSigningData.SigningResponse;

            Assert.That(parsed, Is.Not.Null, "Parsed signing response should not be null.");
            Assert.That(parsed.certificateChain, Is.Not.Null);
            Assert.That(parsed.certificateChain.Length, Is.EqualTo(2));
            Assert.That(parsed.certificateChain[0].certificateType, Is.EqualTo(PackageVerifierData.CertificateType.Publisher));
            Assert.That(parsed.certificateChain[1].certificateType, Is.EqualTo(PackageVerifierData.CertificateType.Root));
            Assert.That(parsed.certificateChain[0].notBefore, Is.EqualTo("2026-03-24T00:00:00.000Z"));
            Assert.That(parsed.certificateChain[0].notAfter, Is.EqualTo("2026-06-24T00:00:00.000Z"));
        }
    }
}
