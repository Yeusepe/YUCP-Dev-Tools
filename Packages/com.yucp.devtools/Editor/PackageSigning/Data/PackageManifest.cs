using System;
using System.Collections.Generic;
using YUCP.Components.Editor.PackageVerifier.Data;

namespace YUCP.DevTools.Editor.PackageSigning.Data
{
    [Serializable]
    public class PackageManifest
    {
        public string authorityId;
        public string keyId;
        public string publisherId;
        public string packageId;
        public string version;
        public string archiveSha256;
        public string vrchatAuthorUserId;
        public Dictionary<string, string> fileHashes; // file path -> SHA256 hash
        public CertificateData[] certificateChain; // Certificate chain: [Publisher, Intermediate?, Root]
        public string gumroadProductId; // Gumroad product ID
        public string jinxxyProductId; // Jinxxy product ID
    }
}
