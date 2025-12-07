using System;

namespace YUCP.DevTools.Editor.PackageSigning.Data
{
    [Serializable]
    public class SignatureData
    {
        public string algorithm;
        public string keyId;
        public string signature; // BASE64
        public int certificateIndex; // Index in certificateChain that signed this manifest (0 = Publisher)
    }
}
