using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;
using YUCP.DevTools.Editor.PackageSigning.Data;
using YUCP.DevTools.Editor.PackageSigning.Crypto;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    /// <summary>
    /// Service to fetch package information from the signing server
    /// </summary>
    public static class PackageInfoService
    {
        [System.Serializable]
        public class PackageInfo
        {
            public string packageId;
            public string version;
            public string archiveSha256;
            public string status; // active, revoked, unlinked
            public string reason;
            public string createdAt;
            public string publisherName;
        }

        [System.Serializable]
        public class PublisherPackagesResponse
        {
            public List<PackageInfo> packages;
        }

        [System.Serializable]
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
        /// Get all packages for the current publisher
        /// </summary>
        public static void GetPublisherPackages(Action<List<PackageInfo>> onSuccess, Action<string> onError = null)
        {
            var settings = GetSigningSettings();
            if (settings == null || !settings.HasValidCertificate())
            {
                onError?.Invoke("No valid certificate found");
                return;
            }

            string serverUrl = settings.serverUrl;
            if (string.IsNullOrEmpty(serverUrl))
            {
                onError?.Invoke("Server URL not configured");
                return;
            }

            string url = $"{serverUrl.TrimEnd('/')}/v1/publisher/packages";
            
            // Get certificate for authentication
            var cert = CertificateManager.GetCurrentCertificate();
            if (cert == null)
            {
                onError?.Invoke("Certificate not found");
                return;
            }

            // Use a helper MonoBehaviour to run the coroutine
            var helper = new GameObject("PackageInfoServiceHelper");
            helper.hideFlags = HideFlags.HideAndDontSave;
            var monoHelper = helper.AddComponent<PackageInfoServiceHelper>();
            monoHelper.StartCoroutine(FetchPackagesCoroutine(url, cert, (packages) => {
                UnityEngine.Object.DestroyImmediate(helper);
                onSuccess?.Invoke(packages);
            }, (error) => {
                UnityEngine.Object.DestroyImmediate(helper);
                onError?.Invoke(error);
            }));
        }

        /// <summary>
        /// Get package status by archive hash
        /// </summary>
        public static void GetPackageStatus(string archiveSha256, Action<PackageStatusResponse> onSuccess, Action<string> onError = null)
        {
            var settings = GetSigningSettings();
            if (settings == null)
            {
                onError?.Invoke("Signing settings not found");
                return;
            }

            string serverUrl = settings.serverUrl;
            if (string.IsNullOrEmpty(serverUrl))
            {
                onError?.Invoke("Server URL not configured");
                return;
            }

            string url = $"{serverUrl.TrimEnd('/')}/v1/packages/by-hash/{archiveSha256}";
            
            // Use a helper MonoBehaviour to run the coroutine
            var helper = new GameObject("PackageInfoServiceHelper");
            helper.hideFlags = HideFlags.HideAndDontSave;
            var monoHelper = helper.AddComponent<PackageInfoServiceHelper>();
            monoHelper.StartCoroutine(FetchPackageStatusCoroutine(url, (status) => {
                UnityEngine.Object.DestroyImmediate(helper);
                onSuccess?.Invoke(status);
            }, (error) => {
                UnityEngine.Object.DestroyImmediate(helper);
                onError?.Invoke(error);
            }));
        }

        private static IEnumerator FetchPackagesCoroutine(string url, Data.YucpCertificate cert, 
            Action<List<PackageInfo>> onSuccess, Action<string> onError)
        {
            Debug.Log($"[PackageInfoService] Fetching packages from: {url}");
            
            // Create payload matching server expectations
            var payload = new PackageRequestPayload
            {
                publisherId = cert.cert.publisherId,
                vrchatUserId = cert.cert.vrchatUserId,
                yucpCert = cert,
                timestamp = DateTime.UtcNow.ToString("O")
            };

            // Canonicalize payload JSON to match server's format exactly
            // Server uses: JSON.stringify(obj, Object.keys(obj).sort())
            // This produces keys in alphabetical order: publisherId, timestamp, vrchatUserId, yucpCert
            string payloadJson = CanonicalizePayload(payload);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            
            Debug.Log($"[PackageInfoService] Payload canonicalized (full): {payloadJson}");
            Debug.Log($"[PackageInfoService] Payload bytes length: {payloadBytes.Length}");
            
            // Get dev public key for logging
            var devKey = DevKeyManager.GetOrCreateDevKey();
            Debug.Log($"[PackageInfoService] Dev public key (base64): {devKey.publicKey}");
            byte[] devPublicKeyBytes = Convert.FromBase64String(devKey.publicKey);
            Debug.Log($"[PackageInfoService] Dev public key length: {devPublicKeyBytes.Length} bytes (expected: 32)");
            
            // Verify the public key matches what's in the certificate
            Debug.Log($"[PackageInfoService] Cert dev public key: {cert.cert.devPublicKey}");
            if (devKey.publicKey != cert.cert.devPublicKey)
            {
                Debug.LogWarning($"[PackageInfoService] WARNING: Dev public key mismatch! Current key doesn't match certificate.");
            }
            
            // Sign the canonicalized payload
            byte[] devSignature = DevKeyManager.SignData(payloadBytes);
            string devSignatureBase64 = Convert.ToBase64String(devSignature);
            Debug.Log($"[PackageInfoService] Dev signature (base64): {devSignatureBase64}");
            Debug.Log($"[PackageInfoService] Dev signature length: {devSignature.Length} bytes (expected: 64)");

            // Build request body
            var requestData = new PackageRequest
            {
                payload = payload,
                devSignature = devSignatureBase64
            };

            string requestJson = JsonUtility.ToJson(requestData);
            byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson);
            
            Debug.Log($"[PackageInfoService] Request body length: {requestBytes.Length} bytes");
            Debug.Log($"[PackageInfoService] Sending POST request...");

            // Send POST request with JSON body
            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(requestBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                Debug.Log($"[PackageInfoService] Request completed. Result: {request.result}, Response Code: {request.responseCode}");

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    Debug.Log($"[PackageInfoService] Response received: {responseText.Substring(0, Math.Min(500, responseText.Length))}...");
                    
                    try
                    {
                        var response = JsonUtility.FromJson<PublisherPackagesResponse>(responseText);
                        Debug.Log($"[PackageInfoService] Successfully parsed response. Package count: {response?.packages?.Count ?? 0}");
                        onSuccess?.Invoke(response.packages ?? new List<PackageInfo>());
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PackageInfoService] Failed to parse response: {ex.Message}\nResponse text: {responseText}");
                        onError?.Invoke($"Failed to parse response: {ex.Message}");
                    }
                }
                else
                {
                    string responseText = request.downloadHandler.text;
                    string errorMessage = responseText;
                    
                    if (string.IsNullOrEmpty(errorMessage))
                    {
                        errorMessage = $"HTTP {request.responseCode}: {request.error}";
                    }
                    
                    Debug.LogError($"[PackageInfoService] Request failed!\n" +
                        $"URL: {url}\n" +
                        $"Result: {request.result}\n" +
                        $"Response Code: {request.responseCode}\n" +
                        $"Error: {request.error}\n" +
                        $"Response Body: {responseText}");
                    
                    onError?.Invoke(errorMessage);
                }
            }
        }

        private static string CanonicalizePayload(PackageRequestPayload payload)
        {
            // Match server's recursive canonicalizeJson function
            // Server recursively sorts keys at all levels alphabetically
            return CanonicalizeJson(payload);
        }
        
        /// <summary>
        /// Recursively canonicalize JSON to match server's format
        /// Sorts keys alphabetically at all levels
        /// </summary>
        private static string CanonicalizeJson(object obj)
        {
            if (obj == null)
            {
                return "null";
            }
            
            var objType = obj.GetType();
            
            // Handle arrays and lists
            if (objType.IsArray)
            {
                var array = (Array)obj;
                var items = new List<string>();
                foreach (var item in array)
                {
                    items.Add(CanonicalizeJson(item));
                }
                return "[" + string.Join(",", items) + "]";
            }
            
            if (obj is System.Collections.IList list)
            {
                var items = new List<string>();
                foreach (var item in list)
                {
                    items.Add(CanonicalizeJson(item));
                }
                return "[" + string.Join(",", items) + "]";
            }
            
            // Handle objects (serializable classes)
            if (objType.IsClass && !objType.IsPrimitive && objType != typeof(string))
            {
                // Get all serializable fields
                var fields = objType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .Where(f => !f.IsStatic)
                    .OrderBy(f => f.Name)
                    .ToList();
                
                var items = new List<string>();
                foreach (var field in fields)
                {
                    var value = field.GetValue(obj);
                    var key = EscapeJsonString(field.Name);
                    var jsonValue = CanonicalizeJson(value);
                    items.Add($"\"{key}\":{jsonValue}");
                }
                return "{" + string.Join(",", items) + "}";
            }
            
            // Handle primitives
            if (obj is string str)
            {
                return $"\"{EscapeJsonString(str)}\"";
            }
            
            if (obj is bool b)
            {
                return b ? "true" : "false";
            }
            
            if (obj is int || obj is long || obj is short || obj is byte || obj is uint || obj is ulong || obj is ushort || obj is sbyte)
            {
                return obj.ToString();
            }
            
            if (obj is float || obj is double || obj is decimal)
            {
                return obj.ToString();
            }
            
            // Fallback to JSON serialization
            return JsonUtility.ToJson(obj);
        }
        
        private static string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }

        [System.Serializable]
        private class PackageRequest
        {
            public PackageRequestPayload payload;
            public string devSignature;
        }

        [System.Serializable]
        private class PackageRequestPayload
        {
            public string publisherId;
            public string vrchatUserId;
            public Data.YucpCertificate yucpCert;
            public string timestamp;
        }

        private static IEnumerator FetchPackageStatusCoroutine(string url, 
            Action<PackageStatusResponse> onSuccess, Action<string> onError)
        {
            Debug.Log($"[PackageInfoService] Fetching package status from: {url}");
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                Debug.Log($"[PackageInfoService] Status request completed. Result: {request.result}, Response Code: {request.responseCode}");

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    Debug.Log($"[PackageInfoService] Status response: {responseText}");
                    
                    try
                    {
                        var response = JsonUtility.FromJson<PackageStatusResponse>(responseText);
                        Debug.Log($"[PackageInfoService] Successfully parsed status response. Known: {response?.known ?? false}");
                        onSuccess?.Invoke(response);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PackageInfoService] Failed to parse status response: {ex.Message}\nResponse text: {responseText}");
                        onError?.Invoke($"Failed to parse response: {ex.Message}");
                    }
                }
                else
                {
                    string responseText = request.downloadHandler.text;
                    Debug.LogError($"[PackageInfoService] Status request failed!\n" +
                        $"URL: {url}\n" +
                        $"Result: {request.result}\n" +
                        $"Response Code: {request.responseCode}\n" +
                        $"Error: {request.error}\n" +
                        $"Response Body: {responseText}");
                    
                    string errorMessage = responseText;
                    if (string.IsNullOrEmpty(errorMessage))
                    {
                        errorMessage = $"HTTP {request.responseCode}: {request.error}";
                    }
                    onError?.Invoke(errorMessage);
                }
            }
        }

        /// <summary>
        /// Revoke/remove a package by its ID
        /// </summary>
        public static void RevokePackage(string packageId, string reason, Action onSuccess, Action<string> onError = null)
        {
            var settings = GetSigningSettings();
            if (settings == null || !settings.HasValidCertificate())
            {
                onError?.Invoke("No valid certificate found");
                return;
            }

            string serverUrl = settings.serverUrl;
            if (string.IsNullOrEmpty(serverUrl))
            {
                onError?.Invoke("Server URL not configured");
                return;
            }

            string url = $"{serverUrl.TrimEnd('/')}/v1/publisher/packages/{packageId}/unlink";
            
            // Get certificate for authentication
            var cert = CertificateManager.GetCurrentCertificate();
            if (cert == null)
            {
                onError?.Invoke("Certificate not found");
                return;
            }

            // Use a helper MonoBehaviour to run the coroutine
            var helper = new GameObject("PackageInfoServiceHelper");
            helper.hideFlags = HideFlags.HideAndDontSave;
            var monoHelper = helper.AddComponent<PackageInfoServiceHelper>();
            monoHelper.StartCoroutine(RevokePackageCoroutine(url, cert, packageId, reason, () => {
                UnityEngine.Object.DestroyImmediate(helper);
                onSuccess?.Invoke();
            }, (error) => {
                UnityEngine.Object.DestroyImmediate(helper);
                onError?.Invoke(error);
            }));
        }

        private static IEnumerator RevokePackageCoroutine(string url, Data.YucpCertificate cert, string packageId, string reason,
            Action onSuccess, Action<string> onError)
        {
            Debug.Log($"[PackageInfoService] Revoking package {packageId} from: {url}");
            
            // Create payload for authentication
            var payload = new RevokePackagePayload
            {
                publisherId = cert.cert.publisherId,
                vrchatUserId = cert.cert.vrchatUserId,
                yucpCert = cert,
                timestamp = DateTime.UtcNow.ToString("O")
            };

            string payloadJson = CanonicalizeJson(payload);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            
            // Sign the canonicalized payload
            byte[] devSignature = DevKeyManager.SignData(payloadBytes);
            string devSignatureBase64 = Convert.ToBase64String(devSignature);

            // Build request body - server expects { payload, devSignature, reason, status }
            var requestData = new RevokePackageRequest
            {
                payload = payload,
                devSignature = devSignatureBase64,
                reason = reason ?? "Revoked by publisher",
                status = "revoked"
            };

            string requestJson = JsonUtility.ToJson(requestData);
            byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson);

            // Send POST request
            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(requestBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[PackageInfoService] Package {packageId} revoked successfully");
                    onSuccess?.Invoke();
                }
                else
                {
                    string responseText = request.downloadHandler.text;
                    string errorMessage = responseText;
                    if (string.IsNullOrEmpty(errorMessage))
                    {
                        errorMessage = $"HTTP {request.responseCode}: {request.error}";
                    }
                    Debug.LogError($"[PackageInfoService] Failed to revoke package: {errorMessage}");
                    onError?.Invoke(errorMessage);
                }
            }
        }

        [System.Serializable]
        private class RevokePackageRequest
        {
            public RevokePackagePayload payload;
            public string devSignature;
            public string reason;
            public string status = "revoked"; // Server accepts 'revoked' or 'unlinked'
        }
        
        [System.Serializable]
        private class RevokePackagePayload
        {
            public string publisherId;
            public string vrchatUserId;
            public Data.YucpCertificate yucpCert;
            public string timestamp;
        }

        private static SigningSettings GetSigningSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<SigningSettings>(path);
            }
            return null;
        }
    }

    /// <summary>
    /// Helper MonoBehaviour for running coroutines in editor
    /// </summary>
    internal class PackageInfoServiceHelper : MonoBehaviour
    {
        // Empty MonoBehaviour just for running coroutines
    }
}

