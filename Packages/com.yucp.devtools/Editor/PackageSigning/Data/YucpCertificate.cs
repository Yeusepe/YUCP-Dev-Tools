using System;

namespace YUCP.DevTools.Editor.PackageSigning.Data
{
    [Serializable]
    public class YucpCertificate
    {
        [Serializable]
        public class CertData
        {
            public int schemaVersion;
            public string issuer;
            public string publisherId;
            public string publisherName;
            public string vrchatUserId;
            public string vrchatDisplayName;
            public string devPublicKey;
            public string issuedAt;
            public string expiresAt;
            public string nonce;
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
