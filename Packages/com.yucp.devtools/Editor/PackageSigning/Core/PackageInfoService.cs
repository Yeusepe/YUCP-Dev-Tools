using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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

        // Server returns { products: [ { productId, displayName, providers: [{provider, providerProductRef}], owner } ] }
        [System.Serializable]
        public class ServerProduct
        {
            public string productId;
            public string displayName;
            // providers array — Unity's JsonUtility can't deserialize nested arrays, so we
            // parse this manually. The field is kept so JsonUtility doesn't strip the object.
            public string owner; // null = own, non-null = collaborator owner name
        }

        [System.Serializable]
        public class ServerProductsResponse
        {
            public ServerProduct[] products;
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
            _ = GetPublisherPackagesAsync(onSuccess, onError);
        }

        private static async System.Threading.Tasks.Task GetPublisherPackagesAsync(Action<List<PackageInfo>> onSuccess, Action<string> onError)
        {
            var settings = GetSigningSettings();
            if (settings == null)
            {
                EditorApplication.delayCall += () => onError?.Invoke("Signing settings not found");
                return;
            }

            string serverUrl = settings.serverUrl;
            if (string.IsNullOrEmpty(serverUrl))
            {
                EditorApplication.delayCall += () => onError?.Invoke("Server URL not configured");
                return;
            }

            string accessToken = await YucpOAuthService.GetValidAccessTokenAsync(serverUrl);
            if (string.IsNullOrEmpty(accessToken))
            {
                EditorApplication.delayCall += () => onError?.Invoke("Not signed in — please sign in with your Creator Account first.");
                return;
            }

            string url = $"{serverUrl.TrimEnd('/')}/v1/products";

            EditorApplication.delayCall += () =>
            {
                var helper = new GameObject("PackageInfoServiceHelper");
                helper.hideFlags = HideFlags.HideAndDontSave;
                var monoHelper = helper.AddComponent<PackageInfoServiceHelper>();
                monoHelper.StartCoroutine(FetchPackagesCoroutine(url, accessToken, packages =>
                {
                    UnityEngine.Object.DestroyImmediate(helper);
                    onSuccess?.Invoke(packages);
                }, error =>
                {
                    UnityEngine.Object.DestroyImmediate(helper);
                    onError?.Invoke(error);
                }));
            };
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

        private static IEnumerator FetchPackagesCoroutine(string url, string accessToken,
            Action<List<PackageInfo>> onSuccess, Action<string> onError)
        {
            Debug.Log($"[PackageInfoService] Fetching products from: {url}");

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("Accept-Encoding", "identity");

                yield return request.SendWebRequest();

                Debug.Log($"[PackageInfoService] Request completed. Result: {request.result}, Response Code: {request.responseCode}");

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    Debug.Log($"[PackageInfoService] Response received: {responseText.Substring(0, Math.Min(500, responseText.Length))}...");

                    try
                    {
                        // Server returns { products: [ { productId, displayName, providers: [...], owner } ] }
                        // Parse with JsonUtility via the wrapper type
                        var serverResp = JsonUtility.FromJson<ServerProductsResponse>(responseText);
                        var packages = new List<PackageInfo>();
                        if (serverResp?.products != null)
                        {
                            foreach (var prod in serverResp.products)
                            {
                                packages.Add(new PackageInfo
                                {
                                    packageId    = prod.productId,
                                    publisherName = prod.owner ?? "(you)",
                                    status       = "active",
                                    version      = "",
                                    archiveSha256 = "",
                                    createdAt    = "",
                                });
                            }
                        }
                        Debug.Log($"[PackageInfoService] Products parsed: {packages.Count}");
                        onSuccess?.Invoke(packages);
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
                    string errorMessage = string.IsNullOrEmpty(responseText)
                        ? $"HTTP {request.responseCode}: {request.error}"
                        : responseText;

                    Debug.LogError($"[PackageInfoService] Request failed!\n" +
                        $"URL: {url}\nResult: {request.result}\n" +
                        $"Response Code: {request.responseCode}\n" +
                        $"Error: {request.error}\nResponse Body: {responseText}");

                    onError?.Invoke(errorMessage);
                }
            }
        }


        private static IEnumerator FetchPackageStatusCoroutine(string url, 
            Action<PackageStatusResponse> onSuccess, Action<string> onError)
        {
            Debug.Log($"[PackageInfoService] Fetching package status from: {url}");
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Accept-Encoding", "identity");
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

            string payloadJson = JsonUtility.ToJson(payload);
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
                request.SetRequestHeader("Accept-Encoding", "identity");

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

