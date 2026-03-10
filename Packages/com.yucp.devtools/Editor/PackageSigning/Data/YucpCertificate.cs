using System;

namespace YUCP.DevTools.Editor.PackageSigning.Data
{
    [Serializable]
    public class YucpCertificate
    {
        /// <summary>
        /// Identity anchors embedded in schemaVersion 2 certificates.
        /// Binds the certificate to multiple verified identity providers.
        /// </summary>
        [Serializable]
        public class IdentityAnchors
        {
            /// <summary>Stable Better Auth user ID — primary identity anchor.</summary>
            public string yucpUserId;
            public string discordUserId;
            public string emailHash;      // SHA-256 of lowercase email, base64
        }

        [Serializable]
        public class CertData
        {
            public int schemaVersion;
            public string issuer;
            public string publisherId;
            public string publisherName;
            // v1 fields (optional; null for v2-only certs)
            public string vrchatUserId;
            public string vrchatDisplayName;
            public string devPublicKey;
            public string issuedAt;
            public string expiresAt;
            public string nonce;
            // v2 fields (null for v1 certs)
            /// <summary>Better Auth user ID of the certificate owner.</summary>
            public string yucpUserId;
            public IdentityAnchors identityAnchors;
        }

        [Serializable]
        public class SignatureData
        {
            public string algorithm;
            public string keyId;
            public string value; // BASE64 signature
        }

        public CertData cert;
        public SignatureData signature;
    }
}
