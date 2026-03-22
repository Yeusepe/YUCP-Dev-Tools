using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using PackageVerifierData = YUCP.Importer.Editor.PackageVerifier.Data;
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
                request.SetRequestHeader("Accept-Encoding", "identity");

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
                string url = $"{_serverUrl}/v1/packages/{archiveSha256}";
                using var req = UnityWebRequest.Get(url);
                req.SetRequestHeader("Accept-Encoding", "identity");
                var op = req.SendWebRequest();
                while (!op.isDone)
                    await System.Threading.Tasks.Task.Yield();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    PackageStatusResponse status = JsonUtility.FromJson<PackageStatusResponse>(req.downloadHandler.text);
                    onSuccess?.Invoke(status);
                }
                else
                {
                    onError?.Invoke($"HTTP {req.responseCode}: {req.error}");
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
                request.SetRequestHeader("Accept-Encoding", "identity");
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
        /// Tries to restore an existing active certificate from the server for this machine's key.
        /// Call this before RequestCertificateAsync — if the server already has a cert for this
        /// machine, return it directly without consuming a rate-limit slot.
        /// Returns the raw certificate JSON on success, null if none found.
        /// </summary>
        public async System.Threading.Tasks.Task<string> RestoreCertificateAsync(
            string accessToken, string devPublicKey)
        {
            try
            {
                using var req = UnityWebRequest.Get($"{_serverUrl}/v1/certificates/me");
                req.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                req.SetRequestHeader("X-Dev-Public-Key", devPublicKey);
                req.SetRequestHeader("Accept-Encoding", "identity");
                var op = req.SendWebRequest();
                while (!op.isDone)
                    await System.Threading.Tasks.Task.Yield();

                if (req.result != UnityWebRequest.Result.Success) return null;

                string json = req.downloadHandler.text;

                // Extract "certificate" object from { "certificate": {...} }
                int certIdx = json.IndexOf("\"certificate\"", StringComparison.Ordinal);
                if (certIdx < 0) return null;
                int braceIdx = json.IndexOf('{', certIdx);
                if (braceIdx < 0) return null;
                int depth = 0, end = braceIdx;
                for (int i = braceIdx; i < json.Length; i++)
                {
                    if (json[i] == '{') depth++;
                    else if (json[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
                }
                return json.Substring(braceIdx, end - braceIdx + 1);
            }
            catch
            {
                return null;
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

                string body = $"{{\"devPublicKey\":\"{EscapeJson(devPublicKey)}\",\"publisherName\":\"{EscapeJson(publisherName)}\"}}";
                UnityEngine.Debug.Log($"[YUCP Cert] Body: {body}");
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

                using var req = new UnityWebRequest($"{_serverUrl}/v1/certificates", "POST");
                req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Authorization",    $"Bearer {accessToken}");
                req.SetRequestHeader("Content-Type",     "application/json");
                req.SetRequestHeader("Accept-Encoding",  "identity");

                var op = req.SendWebRequest();
                while (!op.isDone)
                    await System.Threading.Tasks.Task.Yield();

                string json = req.downloadHandler.text;
                UnityEngine.Debug.Log($"[YUCP Cert] Response {req.responseCode}: {json.Substring(0, Math.Min(300, json.Length))}");

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string errMsg = ExtractErrorMessage(json) ?? $"Server error ({req.responseCode}).";
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

        /// <summary>
        /// Fetches the YUCP CA root public key from GET /v1/keys and caches it in SigningSettings.
        /// Called automatically after a successful sign-in so the key is always server-authoritative.
        /// </summary>
        public async System.Threading.Tasks.Task<bool> FetchAndCacheRootPublicKeyAsync()
        {
            try
            {
                using var req = UnityWebRequest.Get($"{_serverUrl}/v1/keys");
                req.SetRequestHeader("Accept-Encoding", "identity");
                var op = req.SendWebRequest();
                while (!op.isDone)
                    await System.Threading.Tasks.Task.Yield();

                if (req.result != UnityWebRequest.Result.Success) return false;

                string json = req.downloadHandler.text;

                // Parse x field from first key: { "keys": [{ "x": "...", "kid": "..." }] }
                int xIdx = json.IndexOf("\"x\"", StringComparison.Ordinal);
                if (xIdx < 0) return false;
                int colonIdx = json.IndexOf(':', xIdx + 3);
                if (colonIdx < 0) return false;
                int q1 = json.IndexOf('"', colonIdx + 1);
                if (q1 < 0) return false;
                int q2 = json.IndexOf('"', q1 + 1);
                if (q2 < 0) return false;
                string publicKey = json.Substring(q1 + 1, q2 - q1 - 1);

                if (string.IsNullOrEmpty(publicKey)) return false;

                // Persist in SigningSettings asset
                string[] guids = AssetDatabase.FindAssets("t:PackageSigningData.SigningSettings");
                if (guids.Length == 0) guids = AssetDatabase.FindAssets("t:SigningSettings");
                if (guids.Length == 0) return false;

                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var settings = AssetDatabase.LoadAssetAtPath<PackageSigningData.SigningSettings>(path);
                if (settings == null) return false;

                settings.yucpRootPublicKeyBase64 = publicKey;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                UnityEngine.Debug.Log($"[YUCP Keys] Root public key cached from server (kid from /v1/keys).");
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[YUCP Keys] Could not fetch root public key: {ex.Message}");
                return false;
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
