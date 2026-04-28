using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using YUCP.DevTools.Editor.PackageSigning.Core;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class BackstageReleasePublishService
    {
        private const string ApiBaseUrlEnvironmentVariable = "YUCP_API_BASE_URL";
        private const string AccessTokenEnvironmentVariable = "YUCP_ACCESS_TOKEN";

        [Serializable]
        private sealed class UploadUrlResponse
        {
            public string uploadUrl;
        }

        [Serializable]
        private sealed class UploadStorageResponse
        {
            public string storageId;
        }

        [Serializable]
        internal sealed class BackstageReleaseMetadata
        {
            public string description;
            public string unity;
        }

        [Serializable]
        private sealed class PublishBackstageReleaseRequest
        {
            public string[] catalogProductIds;
            public string storageId;
            public string version;
            public string zipSha256;
            public string channel;
            public string packageName;
            public string displayName;
            public string description;
            public string repositoryVisibility;
            public string defaultChannel;
            public string unityVersion;
            public BackstageReleaseMetadata metadata;
            public string deliveryName;
            public string contentType;
        }

        [Serializable]
        private sealed class PublishBackstageReleaseResponse
        {
            public string deliveryPackageReleaseId;
            public string zipSha256;
            public string version;
            public string channel;
        }

        internal sealed class PublishResult
        {
            public bool success;
            public bool wasPublished;
            public string errorMessage;
            public string deliveryPackageReleaseId;
            public string version;
            public string channel;
        }

        internal static PublishResult PublishExportedPackage(
            ExportProfile profile,
            string outputPath,
            Action<float, string> progressCallback = null)
        {
            var result = new PublishResult
            {
                success = true,
                wasPublished = false,
            };

            if (profile == null || !profile.publishReleaseAfterExport)
            {
                return result;
            }

            if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            {
                result.success = false;
                result.errorMessage = "Backstage publishing could not find the exported package on disk.";
                return result;
            }

            List<string> catalogProductIds = CollectCatalogProductIds(profile);
            if (catalogProductIds.Count == 0)
            {
                result.success = false;
                result.errorMessage =
                    "Backstage publishing requires at least one canonical YUCP product ID in License Product ID(s).";
                return result;
            }

            string apiBaseUrl = ResolveApiBaseUrl();
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                result.success = false;
                result.errorMessage =
                    $"Backstage publishing requires {ApiBaseUrlEnvironmentVariable} or Signing Settings to define the YUCP API base URL.";
                return result;
            }

            string accessToken = ResolveAccessToken(apiBaseUrl);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                result.success = false;
                result.errorMessage =
                    $"Backstage publishing requires a creator sign-in or {AccessTokenEnvironmentVariable} in batch mode.";
                return result;
            }

            string packageId = string.IsNullOrWhiteSpace(profile.packageId)
                ? PackageIdManager.AssignPackageId(profile)
                : profile.packageId.Trim();
            string channel = profile.GetResolvedPublishChannel();
            string deliveryName = profile.GetResolvedPublishDeliveryName() ?? Path.GetFileName(outputPath);
            string contentType = InferContentType(outputPath);
            byte[] bytes = File.ReadAllBytes(outputPath);
            string zipSha256 = ComputeSha256Hex(bytes);

            try
            {
                progressCallback?.Invoke(0.1f, "Requesting Backstage upload URL...");
                string uploadUrl = RequestUploadUrl(apiBaseUrl, accessToken, packageId);

                progressCallback?.Invoke(0.45f, "Uploading exported package to Backstage...");
                string storageId = UploadArtifact(uploadUrl, bytes, contentType);

                progressCallback?.Invoke(0.8f, "Publishing Backstage release...");
                PublishBackstageReleaseResponse publishResponse = PublishRelease(
                    apiBaseUrl,
                    accessToken,
                    packageId,
                    BuildPublishRequest(profile, catalogProductIds, storageId, zipSha256, channel, deliveryName, contentType)
                );

                result.success = true;
                result.wasPublished = true;
                result.deliveryPackageReleaseId = publishResponse?.deliveryPackageReleaseId;
                result.version = publishResponse?.version ?? profile.version;
                result.channel = publishResponse?.channel ?? channel;
                progressCallback?.Invoke(1f, "Backstage release published.");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BackstageReleasePublishService] Publish failed: {ex.Message}");
                result.success = false;
                result.errorMessage = ex.Message;
                return result;
            }
        }

        internal static List<string> CollectCatalogProductIds(ExportProfile profile)
        {
            return profile?.GetResolvedLicenseProductIds() ?? new List<string>();
        }

        internal static BackstageReleaseMetadata BuildMetadata(ExportProfile profile)
        {
            if (profile == null)
            {
                return null;
            }

            string description = NormalizeOptional(profile.description);
            string unityVersion = NormalizeOptional(profile.minimumUnityVersion);
            if (string.IsNullOrEmpty(description) && string.IsNullOrEmpty(unityVersion))
            {
                return null;
            }

            return new BackstageReleaseMetadata
            {
                description = description,
                unity = unityVersion,
            };
        }

        internal static string InferContentType(string outputPath)
        {
            return outputPath != null && outputPath.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)
                ? "application/octet-stream"
                : "application/zip";
        }

        private static PublishBackstageReleaseRequest BuildPublishRequest(
            ExportProfile profile,
            List<string> catalogProductIds,
            string storageId,
            string zipSha256,
            string channel,
            string deliveryName,
            string contentType)
        {
            return new PublishBackstageReleaseRequest
            {
                catalogProductIds = catalogProductIds.ToArray(),
                storageId = storageId,
                version = profile.version,
                zipSha256 = zipSha256,
                channel = channel,
                packageName = NormalizeOptional(profile.packageName),
                displayName = NormalizeOptional(profile.packageName),
                description = NormalizeOptional(profile.description),
                repositoryVisibility = profile.GetResolvedPublishRepositoryVisibility(),
                defaultChannel = channel,
                unityVersion = NormalizeOptional(profile.minimumUnityVersion),
                metadata = BuildMetadata(profile),
                deliveryName = NormalizeOptional(deliveryName),
                contentType = contentType,
            };
        }

        private static string ResolveApiBaseUrl()
        {
            string envOverride = NormalizeOptional(Environment.GetEnvironmentVariable(ApiBaseUrlEnvironmentVariable));
            if (!string.IsNullOrEmpty(envOverride))
            {
                return envOverride;
            }

            var settings = SigningSettingsLocator.Load();
            return settings != null ? NormalizeOptional(settings.GetEffectiveServerUrl()) : null;
        }

        private static string ResolveAccessToken(string apiBaseUrl)
        {
            string envToken = NormalizeOptional(Environment.GetEnvironmentVariable(AccessTokenEnvironmentVariable));
            if (!string.IsNullOrEmpty(envToken))
            {
                return envToken;
            }

            return YucpOAuthService.GetValidAccessTokenAsync(apiBaseUrl).GetAwaiter().GetResult();
        }

        private static string RequestUploadUrl(string apiBaseUrl, string accessToken, string packageId)
        {
            string url = BuildApiUrl(apiBaseUrl, $"/api/packages/{UnityWebRequest.EscapeURL(packageId)}/backstage/upload-url");
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("Accept-Encoding", "identity");

                string responseText = SendRequest(request, "Failed to create Backstage upload URL");
                var response = JsonUtility.FromJson<UploadUrlResponse>(responseText);
                string uploadUrl = NormalizeOptional(response?.uploadUrl);
                if (string.IsNullOrEmpty(uploadUrl))
                {
                    throw new Exception("Backstage upload URL response did not include uploadUrl.");
                }
                return uploadUrl;
            }
        }

        private static string UploadArtifact(string uploadUrl, byte[] bytes, string contentType)
        {
            using (var request = new UnityWebRequest(uploadUrl, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(bytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", contentType);
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("Accept-Encoding", "identity");

                string responseText = SendRequest(request, "Failed to upload Backstage package artifact");
                var response = JsonUtility.FromJson<UploadStorageResponse>(responseText);
                string storageId = NormalizeOptional(response?.storageId);
                if (string.IsNullOrEmpty(storageId))
                {
                    throw new Exception("Backstage upload did not return storageId.");
                }
                return storageId;
            }
        }

        private static PublishBackstageReleaseResponse PublishRelease(
            string apiBaseUrl,
            string accessToken,
            string packageId,
            PublishBackstageReleaseRequest payload)
        {
            string url = BuildApiUrl(apiBaseUrl, $"/api/packages/{UnityWebRequest.EscapeURL(packageId)}/backstage/releases");
            string payloadJson = JsonUtility.ToJson(payload);
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payloadJson));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("Accept-Encoding", "identity");

                string responseText = SendRequest(request, "Failed to publish Backstage release");
                var response = JsonUtility.FromJson<PublishBackstageReleaseResponse>(responseText);
                if (string.IsNullOrWhiteSpace(response?.deliveryPackageReleaseId))
                {
                    throw new Exception("Backstage release response did not include deliveryPackageReleaseId.");
                }
                return response;
            }
        }

        private static string SendRequest(UnityWebRequest request, string fallbackMessage)
        {
            request.timeout = 60;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                System.Threading.Thread.Sleep(25);
            }

            string responseText = request.downloadHandler?.text ?? string.Empty;
            if (request.result == UnityWebRequest.Result.Success)
            {
                return responseText;
            }

            string responseMessage = NormalizeOptional(responseText);
            throw new Exception(
                string.IsNullOrEmpty(responseMessage)
                    ? $"{fallbackMessage} ({request.responseCode}: {request.error})"
                    : responseMessage
            );
        }

        private static string BuildApiUrl(string apiBaseUrl, string path)
        {
            return new Uri(new Uri(apiBaseUrl.TrimEnd('/') + "/"), path.TrimStart('/')).ToString();
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static string NormalizeOptional(string value)
        {
            string normalized = value?.Trim();
            return string.IsNullOrEmpty(normalized) ? null : normalized;
        }
    }
}
