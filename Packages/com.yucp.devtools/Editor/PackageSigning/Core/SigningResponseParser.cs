using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using PackageVerifierData = YUCP.Importer.Editor.PackageVerifier.Data;
using PackageSigningData = YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    internal static class SigningResponseParser
    {
        internal static PackageSigningData.SigningResponse Parse(string responseJson, string logContext)
        {
            if (string.IsNullOrEmpty(responseJson))
                return null;

            try
            {
                JObject root;
                using (var stringReader = new StringReader(responseJson))
                using (var jsonReader = new JsonTextReader(stringReader) { DateParseHandling = DateParseHandling.None })
                {
                    root = JObject.Load(jsonReader);
                }

                var response = new PackageSigningData.SigningResponse
                {
                    algorithm = root.Value<string>("algorithm"),
                    keyId = root.Value<string>("keyId"),
                    signature = root.Value<string>("signature"),
                    certificateIndex = root.Value<int?>("certificateIndex") ?? 0,
                };

                if (root["certificateChain"] is JArray chainArray)
                {
                    response.certificateChain = chainArray
                        .OfType<JObject>()
                        .Select(ParseCertificate)
                        .Where(certificate => certificate != null)
                        .ToArray();
                }

                return response;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{logContext}] Failed to parse signing response: {ex.Message}");
                return null;
            }
        }

        private static PackageVerifierData.CertificateData ParseCertificate(JObject certificateJson)
        {
            if (certificateJson == null)
                return null;

            return new PackageVerifierData.CertificateData
            {
                keyId = certificateJson.Value<string>("keyId"),
                publicKey = certificateJson.Value<string>("publicKey"),
                signature = certificateJson.Value<string>("signature"),
                issuerKeyId = certificateJson.Value<string>("issuerKeyId"),
                certificateType = ParseCertificateType(certificateJson["certificateType"]),
                publisherId = certificateJson.Value<string>("publisherId"),
                notBefore = certificateJson.Value<string>("notBefore"),
                notAfter = certificateJson.Value<string>("notAfter"),
            };
        }

        private static PackageVerifierData.CertificateType ParseCertificateType(JToken certificateTypeToken)
        {
            if (certificateTypeToken == null || certificateTypeToken.Type == JTokenType.Null)
                return PackageVerifierData.CertificateType.Root;

            if (certificateTypeToken.Type == JTokenType.Integer)
            {
                int enumValue = certificateTypeToken.Value<int>();
                if (Enum.IsDefined(typeof(PackageVerifierData.CertificateType), enumValue))
                    return (PackageVerifierData.CertificateType)enumValue;
                return PackageVerifierData.CertificateType.Root;
            }

            string enumName = certificateTypeToken.Value<string>();
            if (Enum.TryParse(enumName, ignoreCase: true, out PackageVerifierData.CertificateType parsedType))
                return parsedType;

            return PackageVerifierData.CertificateType.Root;
        }
    }
}
