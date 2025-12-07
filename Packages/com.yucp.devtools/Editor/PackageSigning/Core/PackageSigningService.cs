using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using PackageVerifierData = YUCP.Components.Editor.PackageVerifier.Data;
using PackageSigningData = YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    /// <summary>
    /// HTTP client for signing server API
    /// </summary>
    public class PackageSigningService
    {
        private readonly string _serverUrl;

        public PackageSigningService(string serverUrl)
        {
            _serverUrl = serverUrl.TrimEnd('/');
        }

        /// <summary>
        /// Sign manifest with server
        /// </summary>
        public IEnumerator SignManifest(
            PackageSigningData.PackageManifest manifest,
            PackageSigningData.YucpCertificate certificate,
            byte[] devSignature,
            Action<PackageSigningData.SignatureData, PackageVerifierData.CertificateData[]> onSuccess,
            Action<string> onError)
        {
            // Build payload
            var payload = new SigningRequestPayload
            {
                publisherId = certificate.cert.publisherId,
                vrchatUserId = certificate.cert.vrchatUserId,
                manifest = manifest,
                yucpCert = certificate,
                timestamp = DateTime.UtcNow.ToString("O"),
                nonce = Guid.NewGuid().ToString()
            };

            // Canonicalize payload JSON
            string payloadJson = CanonicalizePayload(payload);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            // Build request
            var requestData = new SigningRequest
            {
                payload = payload,
                devSignature = Convert.ToBase64String(devSignature)
            };

            string requestJson = JsonUtility.ToJson(requestData);
            byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson);

            using (UnityWebRequest request = new UnityWebRequest($"{_serverUrl}/v2/sign-manifest", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(requestBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string responseJson = request.downloadHandler.text;
                        
                        // Parse the full response including certificateChain
                        PackageSigningData.SigningResponse response = JsonUtility.FromJson<PackageSigningData.SigningResponse>(responseJson);
                        
                        // Unity's JsonUtility doesn't properly deserialize nested arrays,
                        // so we need to manually parse certificateChain if present
                        PackageVerifierData.CertificateData[] certificateChain = null;
                        if (response != null && responseJson.Contains("certificateChain"))
                        {
                            try
                            {
                                // Extract the certificateChain array from the JSON
                                int chainStart = responseJson.IndexOf("\"certificateChain\":[");
                                if (chainStart >= 0)
                                {
                                    int bracketCount = 0;
                                    int arrayStart = responseJson.IndexOf('[', chainStart);
                                    int arrayEnd = arrayStart;
                                    
                                    for (int i = arrayStart; i < responseJson.Length; i++)
                                    {
                                        if (responseJson[i] == '[') bracketCount++;
                                        if (responseJson[i] == ']') bracketCount--;
                                        if (bracketCount == 0)
                                        {
                                            arrayEnd = i;
                                            break;
                                        }
                                    }
                                    
                                    if (arrayEnd > arrayStart)
                                    {
                                        string chainJson = responseJson.Substring(arrayStart, arrayEnd - arrayStart + 1);
                                        // Parse the certificate chain array using a wrapper class
                                        CertificateChainWrapper wrapper = JsonUtility.FromJson<CertificateChainWrapper>("{\"Items\":" + chainJson + "}");
                                        if (wrapper != null && wrapper.Items != null)
                                        {
                                            certificateChain = wrapper.Items;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[PackageSigningService] Failed to parse certificateChain from response: {ex.Message}");
                            }
                        }
                        
                        // Convert SigningResponse to SignatureData
                        PackageSigningData.SignatureData signature = new PackageSigningData.SignatureData
                        {
                            algorithm = response.algorithm,
                            keyId = response.keyId,
                            signature = response.signature,
                            certificateIndex = response.certificateIndex
                        };
                        
                        onSuccess?.Invoke(signature, certificateChain);
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke($"Failed to parse response: {ex.Message}");
                    }
                }
                else
                {
                    string errorMessage = request.downloadHandler.text;
                    if (string.IsNullOrEmpty(errorMessage))
                    {
                        errorMessage = $"HTTP {request.responseCode}: {request.error}";
                    }
                    onError?.Invoke(errorMessage);
                }
            }
        }

        /// <summary>
        /// Check package status by hash
        /// </summary>
        public IEnumerator CheckPackageStatus(
            string archiveSha256,
            Action<PackageStatusResponse> onSuccess,
            Action<string> onError)
        {
            using (UnityWebRequest request = UnityWebRequest.Get($"{_serverUrl}/v1/packages/by-hash/{archiveSha256}"))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string responseJson = request.downloadHandler.text;
                        PackageStatusResponse status = JsonUtility.FromJson<PackageStatusResponse>(responseJson);
                        onSuccess?.Invoke(status);
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke($"Failed to parse response: {ex.Message}");
                    }
                }
                else
                {
                    onError?.Invoke($"HTTP {request.responseCode}: {request.error}");
                }
            }
        }

        private string CanonicalizePayload(SigningRequestPayload payload)
        {
            // Simple canonicalization - in production, use proper JSON canonicalization
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"manifest\":{JsonUtility.ToJson(payload.manifest)},");
            sb.Append($"\"nonce\":\"{payload.nonce}\",");
            sb.Append($"\"publisherId\":\"{payload.publisherId}\",");
            sb.Append($"\"timestamp\":\"{payload.timestamp}\",");
            sb.Append($"\"vrchatUserId\":\"{payload.vrchatUserId}\",");
            sb.Append($"\"yucpCert\":{JsonUtility.ToJson(payload.yucpCert)}");
            sb.Append("}");
            return sb.ToString();
        }

        [Serializable]
        private class SigningRequest
        {
            public SigningRequestPayload payload;
            public string devSignature;
        }

        [Serializable]
        private class SigningRequestPayload
        {
            public string publisherId;
            public string vrchatUserId;
            public PackageSigningData.PackageManifest manifest;
            public PackageSigningData.YucpCertificate yucpCert;
            public string timestamp;
            public string nonce;
        }

        [Serializable]
        public class PackageStatusResponse
        {
            public bool known;
            public string status;
            public string publisherId;
            public string publisherName;
            public string packageId;
            public string version;
            public string revocationReason;
        }

        /// <summary>
        /// Wrapper class for parsing certificate chain arrays with Unity's JsonUtility
        /// Unity's JsonUtility requires a wrapper class to deserialize arrays
        /// </summary>
        [Serializable]
        private class CertificateChainWrapper
        {
            public PackageVerifierData.CertificateData[] Items;
        }
    }
}
