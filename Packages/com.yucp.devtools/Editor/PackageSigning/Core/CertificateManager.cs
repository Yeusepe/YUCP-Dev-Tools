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
        private static readonly string AuthorityId = "unitysign.yucp";

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
                YucpCertificate cert = JsonUtility.FromJson<YucpCertificate>(json);

                if (cert == null || cert.cert == null || cert.signature == null)
                {
                    return new CertificateVerificationResult
                    {
                        valid = false,
                        error = "Invalid certificate format"
                    };
                }

                // Verify certificate signature
                var certData = cert.cert;
                var signature = cert.signature;

                if (signature.algorithm != "Ed25519" || signature.keyId != "yucp-root-2025")
                {
                    return new CertificateVerificationResult
                    {
                        valid = false,
                        error = "Invalid signature algorithm or key ID"
                    };
                }

                // Get root public key from settings
                var settings = GetSigningSettings();
                if (settings == null || string.IsNullOrEmpty(settings.yucpRootPublicKeyBase64))
                {
                    return new CertificateVerificationResult
                    {
                        valid = false,
                        error = "YUCP root public key not configured"
                    };
                }

                byte[] rootPublicKey = Convert.FromBase64String(settings.yucpRootPublicKeyBase64);

                // Canonicalize cert JSON
                string canonicalJson = CanonicalizeJson(certData);
                byte[] certBytes = Encoding.UTF8.GetBytes(canonicalJson);

                // Verify signature
                byte[] signatureBytes = Convert.FromBase64String(signature.value);
                bool signatureValid = Ed25519Wrapper.Verify(certBytes, signatureBytes, rootPublicKey);

                if (!signatureValid)
                {
                    return new CertificateVerificationResult
                    {
                        valid = false,
                        error = "Invalid certificate signature"
                    };
                }

                // Check expiration
                if (DateTime.TryParse(certData.expiresAt, out DateTime expiresAt))
                {
                    if (expiresAt < DateTime.UtcNow)
                    {
                        return new CertificateVerificationResult
                        {
                            valid = false,
                            error = "Certificate expired",
                            expired = true
                        };
                    }
                }

                // Verify dev public key matches current dev key
                string currentDevPublicKey = DevKeyManager.GetPublicKeyBase64();
                if (certData.devPublicKey != currentDevPublicKey)
                {
                    return new CertificateVerificationResult
                    {
                        valid = false,
                        error = "Certificate dev public key does not match current dev key"
                    };
                }

                // Store full certificate in settings
                var signingSettings = GetOrCreateSigningSettings();
                signingSettings.StoreCertificate(json); // Store full certificate JSON
                signingSettings.publisherId = certData.publisherId;
                signingSettings.publisherName = certData.publisherName;
                signingSettings.vrchatUserId = certData.vrchatUserId;
                signingSettings.vrchatDisplayName = certData.vrchatDisplayName;
                signingSettings.devPublicKey = certData.devPublicKey;
                signingSettings.certificateExpiresAt = certData.expiresAt;
                
                EditorUtility.SetDirty(signingSettings);
                AssetDatabase.SaveAssets();

                return new CertificateVerificationResult
                {
                    valid = true,
                    publisherId = certData.publisherId,
                    publisherName = certData.publisherName,
                    vrchatUserId = certData.vrchatUserId,
                    vrchatDisplayName = certData.vrchatDisplayName,
                    expiresAt = expiresAt,
                    certificate = cert // Return full certificate
                };
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
        /// Canonicalize JSON (sort keys, no whitespace)
        /// </summary>
        private static string CanonicalizeJson(YucpCertificate.CertData certData)
        {
            // Simple canonicalization - sort keys alphabetically
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"devPublicKey\":\"{certData.devPublicKey}\",");
            sb.Append($"\"expiresAt\":\"{certData.expiresAt}\",");
            sb.Append($"\"issuedAt\":\"{certData.issuedAt}\",");
            sb.Append($"\"issuer\":\"{certData.issuer}\",");
            sb.Append($"\"nonce\":\"{certData.nonce}\",");
            sb.Append($"\"publisherId\":\"{certData.publisherId}\",");
            sb.Append($"\"publisherName\":\"{certData.publisherName}\",");
            sb.Append($"\"schemaVersion\":{certData.schemaVersion},");
            sb.Append($"\"vrchatDisplayName\":\"{certData.vrchatDisplayName}\",");
            sb.Append($"\"vrchatUserId\":\"{certData.vrchatUserId}\"");
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
