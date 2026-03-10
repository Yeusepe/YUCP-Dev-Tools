using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEditor;
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
        /// Returns the configured server URL from SigningSettings, or null if not set.
        /// Used by YUCPImportMonitor for consumer-side registry verification.
        /// </summary>
        public static string GetServerUrl()
        {
            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length == 0) return null;
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var settings = AssetDatabase.LoadAssetAtPath<PackageSigningData.SigningSettings>(path);
            return string.IsNullOrEmpty(settings?.serverUrl) ? null : settings.serverUrl;
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
        /// <summary>
        /// Task-based HTTP check against the YUCP registry — no coroutine runner needed.
        /// Fire-and-forget from YUCPImportMonitor: _ = service.CheckPackageStatusAsync(...)
        /// </summary>
        public async System.Threading.Tasks.Task CheckPackageStatusAsync(
            string archiveSha256,
            Action<PackageStatusResponse> onSuccess,
            Action<string> onError)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(10) };
                string url = $"{_serverUrl}/v1/packages/{archiveSha256}";
                System.Net.Http.HttpResponseMessage response = await client.GetAsync(url);
                string json = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    PackageStatusResponse status = JsonUtility.FromJson<PackageStatusResponse>(json);
                    onSuccess?.Invoke(status);
                }
                else
                {
                    onError?.Invoke($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Registry check error: {ex.Message}");
            }
        }

        public IEnumerator CheckPackageStatus(
            string archiveSha256,
            Action<PackageStatusResponse> onSuccess,
            Action<string> onError)
        {
            using (UnityWebRequest request = UnityWebRequest.Get($"{_serverUrl}/v1/packages/{archiveSha256}"))
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

        /// <summary>
        /// Request a signing certificate from the YUCP CA using a YUCP OAuth access token.
        /// Returns the raw certificate JSON on success for import via CertificateManager.ImportAndVerifyFromJson.
        ///
        /// Body sent: { devPublicKey, publisherName }
        /// Server extracts yucpUserId from the Bearer token.
        /// Response: { success: true, certificate: CertEnvelope }
        /// </summary>
        public async System.Threading.Tasks.Task<(bool success, string error, string certJson)>
            RequestCertificateAsync(string accessToken, string devPublicKey, string publisherName)
        {
            try
            {
                // Log token type to help diagnose "no token payload" — JWTs have 2 dots
                int dotCount = 0;
                foreach (char c in accessToken) if (c == '.') dotCount++;
                bool isJwt = dotCount == 2;
                UnityEngine.Debug.Log($"[YUCP Cert] RequestCertificateAsync token_type={(isJwt?"JWT":"opaque")} length={accessToken.Length} url={_serverUrl}/v1/certificates");

                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

                string body = $"{{\"devPublicKey\":\"{EscapeJson(devPublicKey)}\",\"publisherName\":\"{EscapeJson(publisherName)}\"}}";
                UnityEngine.Debug.Log($"[YUCP Cert] Body: {body}");
                var content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json");

                var resp = await http.PostAsync($"{_serverUrl}/v1/certificates", content);
                string json = await resp.Content.ReadAsStringAsync();
                UnityEngine.Debug.Log($"[YUCP Cert] Response {(int)resp.StatusCode}: {json.Substring(0, Math.Min(300, json.Length))}");

                if (!resp.IsSuccessStatusCode)
                {
                    string errMsg = ExtractErrorMessage(json) ?? $"Server error ({(int)resp.StatusCode}).";
                    return (false, errMsg, null);
                }

                // Response: { "success": true, "certificate": { "cert": {...}, "signature": {...} } }
                // Extract the "certificate" object
                int certIdx = json.IndexOf("\"certificate\"", StringComparison.Ordinal);
                if (certIdx >= 0)
                {
                    int braceIdx = json.IndexOf('{', certIdx);
                    if (braceIdx >= 0)
                    {
                        int depth = 0, end = braceIdx;
                        for (int i = braceIdx; i < json.Length; i++)
                        {
                            if (json[i] == '{') depth++;
                            else if (json[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
                        }
                        return (true, null, json.Substring(braceIdx, end - braceIdx + 1));
                    }
                }
                return (false, "Invalid response format from server.", null);
            }
            catch (Exception ex)
            {
                return (false, $"Network error: {ex.Message}", null);
            }
        }

        private static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

        private static string ExtractErrorMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            foreach (var key in new[] { "error", "message" })
            {
                var search = $"\"{key}\"";
                int idx = json.IndexOf(search, StringComparison.Ordinal);
                if (idx < 0) continue;
                int colonIdx = json.IndexOf(':', idx + search.Length);
                if (colonIdx < 0) continue;
                int quoteIdx = json.IndexOf('"', colonIdx + 1);
                if (quoteIdx < 0) continue;
                int endIdx = json.IndexOf('"', quoteIdx + 1);
                if (endIdx < 0) continue;
                return json.Substring(quoteIdx + 1, endIdx - quoteIdx - 1);
            }
            return null;
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
            // v2 fields: impersonation / registry conflict detection (Layer 1 + 3)
            public bool ownershipConflict;
            public string registeredOwnerYucpUserId;
            public string signingYucpUserId;
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
