using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    /// <summary>
    /// Fetches creator-owned package identities from the YUCP dashboard API.
    /// </summary>
    public static class PackageRegistryService
    {
        [Serializable]
        public class CreatorPackageSummary
        {
            public string packageId;
            public string packageName;
            public long registeredAt;
            public long updatedAt;
        }

        [Serializable]
        private class CreatorPackagesResponse
        {
            public CreatorPackageSummary[] packages;
        }

        public static void GetCreatorPackages(
            string serverUrl,
            Action<List<CreatorPackageSummary>> onSuccess,
            Action<string> onError = null)
        {
            _ = GetCreatorPackagesAsync(serverUrl, onSuccess, onError);
        }

        private static async System.Threading.Tasks.Task GetCreatorPackagesAsync(
            string serverUrl,
            Action<List<CreatorPackageSummary>> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                EditorApplication.delayCall += () => onError?.Invoke("Server URL not configured");
                return;
            }

            string accessToken = await YucpOAuthService.GetValidAccessTokenAsync(serverUrl);
            if (string.IsNullOrEmpty(accessToken))
            {
                EditorApplication.delayCall += () =>
                    onError?.Invoke("Sign in with your creator account before loading packages.");
                return;
            }

            string url = $"{serverUrl.TrimEnd('/')}/api/packages";
            EditorApplication.delayCall += () =>
            {
                var helper = new GameObject("PackageRegistryServiceHelper");
                helper.hideFlags = HideFlags.HideAndDontSave;
                var monoHelper = helper.AddComponent<PackageInfoServiceHelper>();
                monoHelper.StartCoroutine(FetchCreatorPackagesCoroutine(
                    url,
                    accessToken,
                    packages =>
                    {
                        UnityEngine.Object.DestroyImmediate(helper);
                        onSuccess?.Invoke(packages);
                    },
                    error =>
                    {
                        UnityEngine.Object.DestroyImmediate(helper);
                        onError?.Invoke(error);
                    }));
            };
        }

        private static IEnumerator FetchCreatorPackagesCoroutine(
            string url,
            string accessToken,
            Action<List<CreatorPackageSummary>> onSuccess,
            Action<string> onError)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("Accept-Encoding", "identity");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    try
                    {
                        var response = JsonUtility.FromJson<CreatorPackagesResponse>(responseText);
                        var packages = response?.packages?.ToList() ?? new List<CreatorPackageSummary>();
                        packages = packages
                            .OrderBy(pkg => string.IsNullOrWhiteSpace(pkg.packageName) ? pkg.packageId : pkg.packageName, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(pkg => pkg.packageId, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        onSuccess?.Invoke(packages);
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke($"Failed to parse packages response: {ex.Message}");
                    }
                }
                else
                {
                    string responseText = request.downloadHandler.text;
                    string errorMessage = string.IsNullOrEmpty(responseText)
                        ? $"HTTP {request.responseCode}: {request.error}"
                        : responseText;
                    onError?.Invoke(errorMessage);
                }
            }
        }
    }
}
