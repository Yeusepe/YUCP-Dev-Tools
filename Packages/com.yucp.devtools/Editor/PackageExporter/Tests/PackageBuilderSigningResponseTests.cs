using System.Reflection;
using NUnit.Framework;
using YUCP.DevTools.Editor.PackageSigning.Core;
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

        [Test]
        public void NormalizeSigningError_ReturnsFriendlyGuidance_ForArchivedPackageConflict()
        {
            const string rawError = "{\"error\":\"PACKAGE_ARCHIVED\",\"message\":\"Archived packages cannot be updated. Restore the package before signing or changing it.\"}";

            string normalized = PackageSigningService.NormalizeSigningError(409, rawError);

            Assert.That(normalized, Is.EqualTo(
                "This package is archived in your package registry. Restore it from Certificates & Billing, then retry the export."
            ));
        }

        [Test]
        public void BuildSessionFromTokenResponse_ParsesCamelCaseRefreshTokenFields()
        {
            const string tokenJson = "{"
                + "\"access_token\":\"header.payload.signature\","
                + "\"accessTokenExpiresAt\":4102444800,"
                + "\"refreshToken\":\"refresh-token-value\","
                + "\"refreshTokenExpiresAt\":4102531200,"
                + "\"scope\":\"cert:issue profile:read\""
                + "}";

            object session = InvokeBuildSessionFromTokenResponse(tokenJson);

            Assert.That(session, Is.Not.Null);
            Assert.That(GetStringField(session, "accessToken"), Is.EqualTo("header.payload.signature"));
            Assert.That(GetLongField(session, "accessTokenExpiresAt"), Is.EqualTo(4102444800));
            Assert.That(GetStringField(session, "refreshToken"), Is.EqualTo("refresh-token-value"));
            Assert.That(GetLongField(session, "refreshTokenExpiresAt"), Is.EqualTo(4102531200));
            Assert.That(GetStringField(session, "scope"), Is.EqualTo("cert:issue profile:read"));
        }

        [Test]
        public void DescribeTokenResponse_RecognizesCamelCaseTokenFields()
        {
            const string tokenJson = "{"
                + "\"accessToken\":\"access-token-value\","
                + "\"refreshToken\":\"refresh-token-value\","
                + "\"scope\":\"cert:issue profile:read\""
                + "}";

            MethodInfo describeMethod = typeof(YucpOAuthService).GetMethod(
                "DescribeTokenResponse",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(describeMethod, Is.Not.Null, "Expected YucpOAuthService.DescribeTokenResponse to exist.");

            string summary = describeMethod.Invoke(null, new object[] { tokenJson }) as string;

            Assert.That(summary, Does.Contain("hasAccessToken: true"));
            Assert.That(summary, Does.Contain("hasRefreshToken: true"));
            Assert.That(summary, Does.Contain("cert:issue profile:read"));
        }

        private static object InvokeBuildSessionFromTokenResponse(string tokenJson)
        {
            MethodInfo buildMethod = typeof(YucpOAuthService).GetMethod(
                "BuildSessionFromTokenResponse",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(buildMethod, Is.Not.Null, "Expected YucpOAuthService.BuildSessionFromTokenResponse to exist.");
            return buildMethod.Invoke(null, new object[] { tokenJson, null });
        }

        private static string GetStringField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' on {target.GetType().FullName}.");
            return field.GetValue(target) as string;
        }

        private static long GetLongField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' on {target.GetType().FullName}.");
            return (long)field.GetValue(target);
        }
    }
}
