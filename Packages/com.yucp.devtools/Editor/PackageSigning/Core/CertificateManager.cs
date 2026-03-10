using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using YUCP.DevTools.Editor.PackageSigning.Data;
using YUCP.DevTools.Editor.PackageSigning.Crypto;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    /// <summary>
    /// Manages yucp_cert certificate import and verification
    /// </summary>
    public static class CertificateManager
    {
        // Note: AuthorityId reserved for future use
        // private static readonly string AuthorityId = "unitysign.yucp";

        /// <summary>
        /// Import and verify yucp_cert certificate
        /// </summary>
        public static CertificateVerificationResult ImportAndVerify(string certFilePath)
        {
            try
            {
                // Load certificate JSON
                if (!File.Exists(certFilePath))
                {
                    return new CertificateVerificationResult
                    {
                        valid = false,
                        error = "Certificate file not found"
                    };
                }

                string json = File.ReadAllText(certFilePath);
                return VerifyAndStoreCertJson(json);
            }
            catch (Exception ex)
            {
                return new CertificateVerificationResult
                {
                    valid = false,
                    error = $"Certificate verification failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Import and verify a certificate from a JSON string (e.g., returned by the YUCP CA API).
        /// Identical verification logic to ImportAndVerify but avoids the filesystem round-trip.
        /// </summary>
        public static CertificateVerificationResult ImportAndVerifyFromJson(string certJson)
        {
            if (string.IsNullOrEmpty(certJson))
                return new CertificateVerificationResult { valid = false, error = "Certificate JSON is empty" };
            try
            {
                return VerifyAndStoreCertJson(certJson);
            }
            catch (Exception ex)
            {
                return new CertificateVerificationResult
                {
                    valid = false,
                    error = $"Certificate verification failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Shared cert-processing logic used by both ImportAndVerify (file) and ImportAndVerifyFromJson (API).
        /// </summary>
        private static CertificateVerificationResult VerifyAndStoreCertJson(string json)
        {
            YucpCertificate cert = JsonUtility.FromJson<YucpCertificate>(json);
            if (cert == null || cert.cert == null || cert.signature == null)
                return new CertificateVerificationResult { valid = false, error = "Invalid certificate format" };

            var certData  = cert.cert;
            var signature = cert.signature;

            if (signature.algorithm != "Ed25519" || signature.keyId != "yucp-root-2025")
                return new CertificateVerificationResult { valid = false, error = "Invalid signature algorithm or key ID" };

            var settings = GetSigningSettings();
            if (settings == null || string.IsNullOrEmpty(settings.yucpRootPublicKeyBase64))
                return new CertificateVerificationResult { valid = false, error = "YUCP root public key not configured" };

            byte[] rootPublicKey  = Convert.FromBase64String(settings.yucpRootPublicKeyBase64);
            string canonicalJson  = CanonicalizeJson(certData);
            byte[] certBytes      = Encoding.UTF8.GetBytes(canonicalJson);
            byte[] signatureBytes = Convert.FromBase64String(signature.value);

            if (!Ed25519Wrapper.Verify(certBytes, signatureBytes, rootPublicKey))
                return new CertificateVerificationResult { valid = false, error = "Invalid certificate signature" };

            DateTime expiresAt = DateTime.MinValue;
            if (DateTime.TryParse(certData.expiresAt, out expiresAt) && expiresAt < DateTime.UtcNow)
                return new CertificateVerificationResult { valid = false, error = "Certificate expired", expired = true };

            string currentDevPublicKey = DevKeyManager.GetPublicKeyBase64();
            if (certData.devPublicKey != currentDevPublicKey)
                return new CertificateVerificationResult { valid = false, error = "Certificate dev public key does not match current dev key" };

            var signingSettings = GetOrCreateSigningSettings();
            signingSettings.StoreCertificate(json);
            signingSettings.publisherId         = certData.publisherId;
            signingSettings.publisherName       = certData.publisherName;
            signingSettings.vrchatUserId        = certData.vrchatUserId;
            signingSettings.vrchatDisplayName   = certData.vrchatDisplayName;
            signingSettings.devPublicKey        = certData.devPublicKey;
            signingSettings.certificateExpiresAt = certData.expiresAt;
            EditorUtility.SetDirty(signingSettings);
            AssetDatabase.SaveAssets();

            return new CertificateVerificationResult
            {
                valid             = true,
                publisherId       = certData.publisherId,
                publisherName     = certData.publisherName,
                vrchatUserId      = certData.vrchatUserId,
                vrchatDisplayName = certData.vrchatDisplayName,
                expiresAt         = expiresAt,
                certificate       = cert
            };
        }

        /// <summary>
        /// Get current certificate from settings
        /// </summary>
        public static YucpCertificate GetCurrentCertificate()
        {
            var settings = GetSigningSettings();
            if (settings == null || !settings.HasValidCertificate())
                return null;

            string certJson = settings.GetCertificateJson();
            if (string.IsNullOrEmpty(certJson))
                return null;

            try
            {
                return JsonUtility.FromJson<YucpCertificate>(certJson);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Canonicalize JSON (sort keys, no whitespace).
        /// MUST stay in sync with canonicalizeCert() in Gumroad/convex/lib/yucpCrypto.ts.
        ///
        /// v1 (schemaVersion == 1): fields devPublicKey..vrchatUserId (no yucpUserId)
        /// v2 (schemaVersion == 2): adds yucpUserId + identityAnchors in alphabetical position
        /// </summary>
        private static string CanonicalizeJson(YucpCertificate.CertData certData)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            // Alphabetical key order must match canonicalizeCert() in yucpCrypto.ts
            sb.Append($"\"devPublicKey\":\"{certData.devPublicKey}\",");
            sb.Append($"\"expiresAt\":\"{certData.expiresAt}\",");

            if (certData.schemaVersion >= 2 && certData.identityAnchors != null)
            {
                // identityAnchors (i-d) < issuedAt (i-s) alphabetically
                var anchors = certData.identityAnchors;
                sb.Append("\"identityAnchors\":{");
                bool firstAnchor = true;
                // Inner keys in alphabetical order: discordUserId, emailHash, yucpUserId
                if (!string.IsNullOrEmpty(anchors.discordUserId))
                {
                    sb.Append($"\"discordUserId\":\"{anchors.discordUserId}\"");
                    firstAnchor = false;
                }
                if (!string.IsNullOrEmpty(anchors.emailHash))
                {
                    if (!firstAnchor) sb.Append(",");
                    sb.Append($"\"emailHash\":\"{anchors.emailHash}\"");
                    firstAnchor = false;
                }
                if (!string.IsNullOrEmpty(anchors.yucpUserId))
                {
                    if (!firstAnchor) sb.Append(",");
                    sb.Append($"\"yucpUserId\":\"{anchors.yucpUserId}\"");
                }
                sb.Append("},");
            }

            sb.Append($"\"issuedAt\":\"{certData.issuedAt}\",");
            sb.Append($"\"issuer\":\"{certData.issuer}\",");
            sb.Append($"\"nonce\":\"{certData.nonce}\",");
            sb.Append($"\"publisherId\":\"{certData.publisherId}\",");
            sb.Append($"\"publisherName\":\"{certData.publisherName}\",");
            sb.Append($"\"schemaVersion\":{certData.schemaVersion}");

            // v1-only fields: vrchat* come after schemaVersion alphabetically
            if (certData.schemaVersion < 2 && certData.vrchatDisplayName != null)
            {
                sb.Append($",\"vrchatDisplayName\":\"{certData.vrchatDisplayName}\"");
                sb.Append($",\"vrchatUserId\":\"{certData.vrchatUserId}\"");
            }

            // v2: yucpUserId (y) comes last — after schemaVersion (s) alphabetically
            if (certData.schemaVersion >= 2 && certData.yucpUserId != null)
            {
                sb.Append($",\"yucpUserId\":\"{certData.yucpUserId}\"");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static Data.SigningSettings GetSigningSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<Data.SigningSettings>(path);
            }
            return null;
        }

        private static Data.SigningSettings GetOrCreateSigningSettings()
        {
            var settings = GetSigningSettings();
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<Data.SigningSettings>();
                string path = "Assets/YUCP/SigningSettings.asset";
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                AssetDatabase.CreateAsset(settings, path);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }

        public class CertificateVerificationResult
        {
            public bool valid;
            public string error;
            public bool expired;
            public string publisherId;
            public string publisherName;
            public string vrchatUserId;
            public string vrchatDisplayName;
            public DateTime expiresAt;
            public YucpCertificate certificate; // Full certificate
        }
    }
}
