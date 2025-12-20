using System;
using YUCP.Components.Editor.PackageVerifier.Data;

namespace YUCP.DevTools.Editor.PackageSigning.Data
{
    /// <summary>
    /// Response from the signing server, including signature and certificate chain
    /// </summary>
    [Serializable]
    public class SigningResponse
    {
        public string algorithm;
        public string keyId;
        public string signature; // BASE64
        public int certificateIndex; // Index in certificateChain that signed this manifest
        public CertificateData[] certificateChain; // Certificate chain: [Publisher, Intermediate?, Root]
    }
}































