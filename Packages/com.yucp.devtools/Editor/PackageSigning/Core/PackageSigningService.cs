using System;
using System.Collections;
using System.Collections.Generic;
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
                        
                        PackageSigningData.SigningResponse response =
                            SigningResponseParser.Parse(responseJson, "PackageSigningService");
                        PackageVerifierData.CertificateData[] certificateChain = response?.certificateChain;
                         
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

                return ExtractCertificateEnvelopeJson(req.downloadHandler.text);
            }
            catch
            {
                return null;
            }
        }

        public string RestoreCertificate(string accessToken, string devPublicKey)
        {
            try
            {
                using var req = UnityWebRequest.Get($"{_serverUrl}/v1/certificates/me");
                req.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                req.SetRequestHeader("X-Dev-Public-Key", devPublicKey);
                req.SetRequestHeader("Accept-Encoding", "identity");
                req.timeout = 30;

                var op = req.SendWebRequest();
                while (!op.isDone)
                    System.Threading.Thread.Sleep(25);

                if (req.result != UnityWebRequest.Result.Success)
                    return null;

                return ExtractCertificateEnvelopeJson(req.downloadHandler.text);
            }
            catch
            {
                return null;
            }
        }

        public async System.Threading.Tasks.Task<CertificateAccountState> GetCertificateAccountStateAsync(
            string accessToken,
            string devPublicKey)
        {
            try
            {
                using var req = UnityWebRequest.Get($"{_serverUrl}/v1/certificates/devices");
                req.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                req.SetRequestHeader("Accept-Encoding", "identity");

                var op = req.SendWebRequest();
                while (!op.isDone)
                    await System.Threading.Tasks.Task.Yield();

                string json = req.downloadHandler?.text ?? "";
                if (req.result != UnityWebRequest.Result.Success)
                {
                    string rawError = ExtractErrorMessage(json) ?? $"Server error ({req.responseCode}).";
                    return CreateErrorAccountState(req.responseCode, rawError);
                }

                return ParseCertificateAccountState(req.responseCode, json, devPublicKey);
            }
            catch (Exception ex)
            {
                return CreateErrorAccountState(0, $"Network error: {ex.Message}");
            }
        }

        public CertificateAccountState GetCertificateAccountState(string accessToken, string devPublicKey)
        {
            try
            {
                using var req = UnityWebRequest.Get($"{_serverUrl}/v1/certificates/devices");
                req.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                req.SetRequestHeader("Accept-Encoding", "identity");
                req.timeout = 30;

                var op = req.SendWebRequest();
                while (!op.isDone)
                    System.Threading.Thread.Sleep(25);

                string json = req.downloadHandler?.text ?? "";
                if (req.result != UnityWebRequest.Result.Success)
                {
                    string rawError = ExtractErrorMessage(json) ?? $"Server error ({req.responseCode}).";
                    return CreateErrorAccountState(req.responseCode, rawError);
                }

                return ParseCertificateAccountState(req.responseCode, json, devPublicKey);
            }
            catch (Exception ex)
            {
                return CreateErrorAccountState(0, $"Network error: {ex.Message}");
            }
        }

        public static string NormalizeCertificateRequestError(
            long responseCode,
            string rawError,
            bool currentDeviceKnown = false)
        {
            string message = string.IsNullOrWhiteSpace(rawError)
                ? $"Server error ({responseCode})."
                : rawError.Trim();
            bool looksLikeGrace = message.IndexOf("Billing grace period active", StringComparison.OrdinalIgnoreCase) >= 0;
            bool looksLikePlanRequired =
                message.IndexOf("subscription", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("payment required", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("plan required", StringComparison.OrdinalIgnoreCase) >= 0;
            bool looksLikeDeviceLimit = message.IndexOf("Device limit reached", StringComparison.OrdinalIgnoreCase) >= 0;

            if (responseCode == 401)
            {
                return message.IndexOf("No active certificate found for this machine", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "No active certificate exists for this machine. Open Certificates & Billing to enroll or restore the correct device."
                    : "Your creator session expired or is no longer valid. Sign in again and retry.";
            }

            if (responseCode == 402 || looksLikeGrace)
            {
                if (looksLikeGrace)
                {
                    return currentDeviceKnown
                        ? "Billing grace is active. Restore this machine's existing certificate or reopen Certificates & Billing to fix billing."
                        : "Billing grace is active. Existing enrolled devices can still sign, but this machine cannot enroll until billing is fixed.";
                }

                return "An active certificate subscription is required before this machine can enroll or sign. Open Certificates & Billing to continue.";
            }

            if (responseCode == 403)
            {
                return "This account is not allowed to enroll certificates on the current signing server.";
            }

            if ((responseCode == 409 && looksLikeDeviceLimit) || looksLikeDeviceLimit)
            {
                return "Your certificate plan has reached its device limit. Revoke another device or upgrade the plan from Certificates & Billing.";
            }

            if (looksLikePlanRequired)
            {
                return "An active certificate subscription is required before this machine can enroll or sign. Open Certificates & Billing to continue.";
            }

            return message;
        }

        public static string NormalizeSigningError(long responseCode, string rawError)
        {
            string message = string.IsNullOrWhiteSpace(rawError)
                ? $"HTTP {responseCode}"
                : rawError.Trim();
            bool looksLikeGrace = message.IndexOf("Billing grace period active", StringComparison.OrdinalIgnoreCase) >= 0;
            bool looksLikePlanRequired =
                message.IndexOf("subscription", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("payment required", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("plan required", StringComparison.OrdinalIgnoreCase) >= 0;
            bool looksLikeRevoked = message.IndexOf("Certificate has been revoked", StringComparison.OrdinalIgnoreCase) >= 0;
            bool looksLikeArchivedPackage =
                message.IndexOf("PACKAGE_ARCHIVED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("Archived packages cannot be updated", StringComparison.OrdinalIgnoreCase) >= 0;

            if (responseCode == 401)
            {
                return looksLikeRevoked
                    ? "This signing certificate has been revoked. Restore the correct device or manage devices in Certificates & Billing."
                    : "Signing authentication failed. Re-import or restore the correct certificate, then try again.";
            }

            if (responseCode == 402 || looksLikeGrace)
            {
                return looksLikeGrace
                    ? "Billing grace is active. Existing enrolled devices can keep signing, but this machine cannot enroll a new certificate until billing is fixed."
                    : "Package signing is blocked until the certificate subscription is active again. Open Certificates & Billing to fix billing.";
            }

            if (responseCode == 409)
            {
                if (looksLikeArchivedPackage)
                {
                    return "This package is archived in your package registry. Restore it from Certificates & Billing, then retry the export.";
                }

                return message.IndexOf("nonce", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "The signing proof was already used. Retry the export to generate a fresh signing proof."
                    : message;
            }

            if (looksLikeRevoked)
            {
                return "This signing certificate has been revoked. Restore the correct device or manage devices in Certificates & Billing.";
            }

            if (looksLikePlanRequired)
            {
                return "Package signing is blocked until the certificate subscription is active again. Open Certificates & Billing to fix billing.";
            }

            return message;
        }

        private static string BuildAccountStateReason(CertificateDevicesResponse state)
        {
            if (state == null || state.billing == null)
                return "Certificate account status is unavailable.";

            if (!string.IsNullOrEmpty(state.billing.reason))
                return state.billing.reason;

            if (!state.billing.billingEnabled)
                return "Certificate billing is unmanaged for this signing server.";

            switch (state.billing.status ?? "")
            {
                case "active":
                    if (state.deviceCapReachedForCurrentMachine)
                    {
                        return "This plan has no free device slots. Manage devices or upgrade the plan before enrolling this machine.";
                    }

                    if (state.currentDeviceKnown)
                    {
                        return "This machine already has an active signing device on your account.";
                    }

                    return "Certificate signing is active for this account.";

                case "grace":
                    return state.currentDeviceKnown
                        ? "Billing grace is active. This machine can still sign once its existing certificate is restored."
                        : "Billing grace is active. Existing enrolled devices can still sign, but this machine cannot enroll until billing is fixed.";

                case "inactive":
                    return "A certificate subscription is required before this machine can enroll or sign.";

                case "suspended":
                    return "Certificate signing is suspended until billing is restored.";

                case "unmanaged":
                    return "Certificate billing is unmanaged for this signing server.";

                default:
                    return "Certificate account status is unavailable.";
            }
        }

        private static CertificateAccountState CreateErrorAccountState(long responseCode, string error)
        {
            return new CertificateAccountState
            {
                responseCode = responseCode,
                error = error,
                devices = Array.Empty<CertificateDeviceInfo>(),
                billing = new CertificateBillingInfo
                {
                    status = "unknown",
                },
            };
        }

        /// <summary>
        /// Request a signing certificate from the YUCP CA using a YUCP OAuth access token.
        /// Returns the raw certificate JSON on success for import via CertificateManager.ImportAndVerifyFromJson.
        ///
        /// Body sent: { devPublicKey, publisherName }
        /// Server extracts yucpUserId from the Bearer token.
        /// Response: { success: true, certificate: CertEnvelope }
        /// </summary>
        public async System.Threading.Tasks.Task<(bool success, long responseCode, string error, string certJson)>
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
                    return (false, req.responseCode, errMsg, null);
                }

                // Response: { "success": true, "certificate": { "cert": {...}, "signature": {...} } }
                // Extract the "certificate" object
                string certJson = ExtractCertificateEnvelopeJson(json);
                return string.IsNullOrEmpty(certJson)
                    ? (false, req.responseCode, "Invalid response format from server.", null)
                    : (true, req.responseCode, null, certJson);
            }
            catch (Exception ex)
            {
                return (false, 0, $"Network error: {ex.Message}", null);
            }
        }

        public (bool success, long responseCode, string error, string certJson)
            RequestCertificate(string accessToken, string devPublicKey, string publisherName)
        {
            try
            {
                string body = $"{{\"devPublicKey\":\"{EscapeJson(devPublicKey)}\",\"publisherName\":\"{EscapeJson(publisherName)}\"}}";
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

                using var req = new UnityWebRequest($"{_serverUrl}/v1/certificates", "POST");
                req.uploadHandler = new UploadHandlerRaw(bodyBytes);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept-Encoding", "identity");
                req.timeout = 30;

                var op = req.SendWebRequest();
                while (!op.isDone)
                    System.Threading.Thread.Sleep(25);

                string json = req.downloadHandler.text;
                if (req.result != UnityWebRequest.Result.Success)
                {
                    string errMsg = ExtractErrorMessage(json) ?? $"Server error ({req.responseCode}).";
                    return (false, req.responseCode, errMsg, null);
                }

                string certJson = ExtractCertificateEnvelopeJson(json);
                return string.IsNullOrEmpty(certJson)
                    ? (false, req.responseCode, "Invalid response format from server.", null)
                    : (true, req.responseCode, null, certJson);
            }
            catch (Exception ex)
            {
                return (false, 0, $"Network error: {ex.Message}", null);
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
                var trustedKeys = ParseTrustedRootKeys(json);
                if (trustedKeys.Count == 0) return false;

                // Persist in SigningSettings asset
                string[] guids = AssetDatabase.FindAssets("t:PackageSigningData.SigningSettings");
                if (guids.Length == 0) guids = AssetDatabase.FindAssets("t:SigningSettings");
                if (guids.Length == 0) return false;

                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var settings = AssetDatabase.LoadAssetAtPath<PackageSigningData.SigningSettings>(path);
                if (settings == null) return false;

                settings.SetTrustedRootKeysForServer(_serverUrl, trustedKeys);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                UnityEngine.Debug.Log($"[YUCP Keys] Cached {trustedKeys.Count} root key(s) from server /v1/keys.");
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

        private static List<PackageSigningData.TrustedRootKey> ParseTrustedRootKeys(string json)
        {
            var response = JsonUtility.FromJson<TrustedRootKeysResponse>(json);
            var trustedKeys = new List<PackageSigningData.TrustedRootKey>();
            if (response?.keys == null)
                return trustedKeys;

            foreach (var key in response.keys)
            {
                if (key == null || string.IsNullOrWhiteSpace(key.x))
                    continue;

                trustedKeys.Add(new PackageSigningData.TrustedRootKey
                {
                    keyId = key.kid?.Trim() ?? "",
                    algorithm = string.IsNullOrWhiteSpace(key.crv) ? "Ed25519" : key.crv.Trim(),
                    publicKeyBase64 = key.x.Trim(),
                });
            }

            return trustedKeys;
        }

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

        private static string ExtractCertificateEnvelopeJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            int certIdx = json.IndexOf("\"certificate\"", StringComparison.Ordinal);
            if (certIdx < 0)
                return null;

            int braceIdx = json.IndexOf('{', certIdx);
            if (braceIdx < 0)
                return null;

            int depth = 0;
            int end = braceIdx;
            for (int i = braceIdx; i < json.Length; i++)
            {
                if (json[i] == '{')
                    depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }

            return json.Substring(braceIdx, end - braceIdx + 1);
        }

        private static CertificateAccountState ParseCertificateAccountState(
            long responseCode,
            string json,
            string devPublicKey)
        {
            var response = JsonUtility.FromJson<CertificateDevicesResponse>(json);
            if (response == null || response.billing == null)
            {
                return CreateErrorAccountState(0, "Invalid certificate account response from server.");
            }

            response.responseCode = responseCode;
            response.devices ??= Array.Empty<CertificateDeviceInfo>();
            response.currentDeviceKnown = !string.IsNullOrEmpty(devPublicKey) &&
                Array.Exists(response.devices, device =>
                    device != null &&
                    string.Equals(device.devPublicKey, devPublicKey, StringComparison.Ordinal));
            response.deviceCapReachedForCurrentMachine =
                !response.currentDeviceKnown &&
                response.billing.deviceCap > 0 &&
                response.billing.activeDeviceCount >= response.billing.deviceCap;
            response.error = BuildAccountStateReason(response);
            return response;
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

        [Serializable]
        public class CertificateDeviceInfo
        {
            public string certNonce;
            public string devPublicKey;
            public string publisherId;
            public string publisherName;
            public long issuedAt;
            public long expiresAt;
            public string status;
        }

        [Serializable]
        public class CertificateBillingInfo
        {
            public bool billingEnabled;
            public string status;
            public string planKey;
            public int deviceCap;
            public int activeDeviceCount;
            public bool allowEnrollment;
            public bool allowSigning;
            public int signQuotaPerPeriod;
            public int auditRetentionDays;
            public string supportTier;
            public long currentPeriodEnd;
            public long graceUntil;
            public string reason;
            public CertificateCapabilityState[] capabilities;
        }

        [Serializable]
        public class CertificatePlanInfo
        {
            public string planKey;
            public string slug;
            public string productId;
            public string displayName;
            public string description;
            public string[] highlights;
            public int priority;
            public int deviceCap;
            public int signQuotaPerPeriod;
            public int auditRetentionDays;
            public string supportTier;
            public int billingGraceDays;
            public string[] capabilities;
        }

        [Serializable]
        public class CertificateCapabilityState
        {
            public string capabilityKey;
            public string status;
        }

        [Serializable]
        public class CertificateAccountState
        {
            public long responseCode;
            public string error;
            public bool currentDeviceKnown;
            public bool deviceCapReachedForCurrentMachine;
            public string workspaceKey;
            public string creatorProfileId;
            public CertificateDeviceInfo[] devices;
            public CertificateBillingInfo billing;
            public CertificatePlanInfo[] availablePlans;
        }

        [Serializable]
        private class CertificateDevicesResponse : CertificateAccountState
        {
        }

        [Serializable]
        private class TrustedRootKeysResponse
        {
            public TrustedRootKeyResponse[] keys;
        }

        [Serializable]
        private class TrustedRootKeyResponse
        {
            public string kid;
            public string crv;
            public string x;
        }

    }
}
