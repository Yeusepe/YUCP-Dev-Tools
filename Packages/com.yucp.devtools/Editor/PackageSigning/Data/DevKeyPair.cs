using System;

namespace YUCP.DevTools.Editor.PackageSigning.Data
{
    [Serializable]
    public class DevKeyPair
    {
        public string algorithm = "Ed25519";
        public string publicKey; // BASE64
        public string privateKey; // BASE64 (encrypted)
    }
}
