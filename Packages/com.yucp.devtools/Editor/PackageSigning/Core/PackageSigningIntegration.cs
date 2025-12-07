using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageVerifierData = YUCP.Components.Editor.PackageVerifier.Data;
using PackageSigningData = YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    /// <summary>
    /// Integration helper for signing packages during export
    /// </summary>
    public static class PackageSigningIntegration
    {
        /// <summary>
        /// Sign a package after it's been created
        /// This should be called after the .unitypackage file is created
        /// </summary>
        public static IEnumerator SignPackage(
            string packagePath,
            string packageId,
            string version,
            Action<float, string> progressCallback,
            Action<bool, string> onComplete)
        {
            // Check prerequisites
            var settings = GetSigningSettings();
            if (settings == null || !settings.HasValidCertificate())
            {
                onComplete?.Invoke(false, "No valid certificate found. Please import a certificate first.");
                yield break;
            }

            progressCallback?.Invoke(0.1f, "Preparing signing...");

            // Get certificate from settings (now stores full certificate JSON)
            var certificate = CertificateManager.GetCurrentCertificate();
            if (certificate == null)
            {
                onComplete?.Invoke(false, "Failed to load certificate from settings. Please import a certificate first.");
                yield break;
            }

            progressCallback?.Invoke(0.2f, "Computing package hash...");

            // Compute archive SHA-256
            string archiveSha256 = ComputeFileHash(packagePath);

            progressCallback?.Invoke(0.3f, "Building manifest...");

            // Build manifest
            var manifest = ManifestBuilder.BuildManifest(
                packagePath,
                packageId,
                version,
                settings.publisherId,
                settings.vrchatUserId
            );

            progressCallback?.Invoke(0.4f, "Creating signing request...");

            // Create signing request payload
            var payload = new SigningRequestPayload
            {
                publisherId = settings.publisherId,
                vrchatUserId = settings.vrchatUserId,
                manifest = manifest,
                yucpCert = certificate,
                timestamp = DateTime.UtcNow.ToString("O"),
                nonce = Guid.NewGuid().ToString()
            };

            // Canonicalize and sign payload
            string payloadJson = CanonicalizePayload(payload);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            byte[] devSignature = DevKeyManager.SignData(payloadBytes);

            progressCallback?.Invoke(0.5f, "Sending signing request to server...");

            // Send to server
            var signingService = new PackageSigningService(settings.serverUrl);
            bool requestComplete = false;
            PackageSigningData.SignatureData signatureData = null;
            PackageVerifierData.CertificateData[] certificateChain = null;
            string errorMessage = null;

            yield return signingService.SignManifest(
                manifest,
                certificate,
                devSignature,
                (signature, chain) =>
                {
                    signatureData = signature;
                    certificateChain = chain;
                    requestComplete = true;
                },
                (error) =>
                {
                    errorMessage = error;
                    requestComplete = true;
                }
            );

            if (!requestComplete)
            {
                onComplete?.Invoke(false, "Signing request timed out");
                yield break;
            }

            if (signatureData == null)
            {
                onComplete?.Invoke(false, errorMessage ?? "Signing failed");
                yield break;
            }

            // Extract certificate chain from response and add to manifest
            if (certificateChain != null && certificateChain.Length > 0)
            {
                manifest.certificateChain = certificateChain;
                Debug.Log($"[PackageSigningIntegration] Added certificate chain with {certificateChain.Length} certificates to manifest");
            }
            else
            {
                Debug.LogWarning("[PackageSigningIntegration] Server response did not include certificate chain");
            }

            progressCallback?.Invoke(0.8f, "Embedding signature...");

            // Embed manifest and signature
            SignatureEmbedder.EmbedSigningData(manifest, signatureData);

            // 1. Re-export the package with the signing assets included
            // 2. Or inject the signing data into the existing package (like PackageBuilder does with other assets)
            // For now, the signing data is embedded in the project and would need to be included in a re-export

            progressCallback?.Invoke(1.0f, "Signing complete");

            onComplete?.Invoke(true, "Package signed successfully. Note: Signing data must be included in package export.");
        }

        private static Data.SigningSettings GetSigningSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<Data.SigningSettings>(path);
            }
            return null;
        }

        private static string ComputeFileHash(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string CanonicalizePayload(SigningRequestPayload payload)
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
        private class SigningRequestPayload
        {
            public string publisherId;
            public string vrchatUserId;
            public PackageSigningData.PackageManifest manifest;
            public PackageSigningData.YucpCertificate yucpCert;
            public string timestamp;
            public string nonce;
        }
    }
}
