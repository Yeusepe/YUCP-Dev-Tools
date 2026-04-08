using NUnit.Framework;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class EmbeddedTextEncodingUtilityTests
    {
        [Test]
        public void EncodeAndDecode_RoundTripsMetaTextWithoutLeavingYamlMarkers()
        {
            const string metaText =
                "fileFormatVersion: 2\n" +
                "guid: PLACEHOLDER_GUID\n" +
                "testRef: {fileID: 2100000, guid: 1234567890abcdef1234567890abcdef, type: 2}\n";

            string encoded = EmbeddedTextEncodingUtility.Encode(metaText);

            Assert.That(encoded, Does.StartWith("yucp-b64:"));
            Assert.That(encoded, Does.Not.Contain("guid: PLACEHOLDER_GUID"));
            Assert.That(encoded, Does.Not.Contain("{fileID:"));
            Assert.That(EmbeddedTextEncodingUtility.TryDecode(encoded, out string decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(metaText));
        }

        [Test]
        public void TryDecode_LeavesLegacyPlainTextUntouched()
        {
            const string legacyText = "guid: PLACEHOLDER_GUID\nexternalObjects: {}\n";

            Assert.That(EmbeddedTextEncodingUtility.TryDecode(legacyText, out string decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(legacyText));
        }
    }
}
