using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace YUCP.DevTools.Editor.PackageSigning.Data
{
    [Serializable]
    public class TrustedRootKey
    {
        public string keyId = "";
        public string algorithm = "Ed25519";
        public string publicKeyBase64 = "";
    }

    /// <summary>
    /// A named certificate provider (signing authority) with its own server URL and root public key.
    /// </summary>
    [Serializable]
    public class CertificateProvider
    {
        [Tooltip("Display name for this certificate provider")]
        public string name = "";

        [Tooltip("API server URL for certificate issuance and package signing")]
        public string serverUrl = SigningSettings.DefaultServerUrl;

        [Tooltip("Optional web account base URL for certificate billing and device management. Leave empty to derive from the server URL.")]
        public string accountAppUrl = "";

        [Tooltip("Root public key (Ed25519, base64) used to verify certificates issued by this provider. Leave empty to use the global YUCP root key.")]
        [TextArea(1, 3)]
        public string rootPublicKeyBase64 = "";

        [Tooltip("Trusted root keys fetched from the authoritative signing server /v1/keys endpoint.")]
        public List<TrustedRootKey> trustedRootKeys = new List<TrustedRootKey>();
    }

    [CreateAssetMenu(fileName = "SigningSettings", menuName = "YUCP/Package Signing Settings")]
    public class SigningSettings : ScriptableObject
    {
        public const string DefaultServerUrl = "https://api.creators.yucp.club";

        [Header("Server Configuration")]
        [Tooltip("Default signing server URL. Used when no per-profile or per-provider URL is configured.")]
        public string serverUrl = DefaultServerUrl;

        [Tooltip("Default web account base URL for certificate billing and device management. Leave empty to derive from the signing server URL.")]
        public string accountAppUrl = "";

        [Header("Certificate Providers")]
        [Tooltip("Named certificate providers. Each provider has its own server URL and root public key. The first entry is used as the default when no per-profile override is set.")]
        public List<CertificateProvider> certificateProviders = new List<CertificateProvider>();
        
        [Header("Certificate")]
        [SerializeField] private string _certificateJson; // Full certificate JSON stored here
        public string publisherId;
        public string publisherName;
        public string vrchatUserId;
        public string vrchatDisplayName;
        public string devPublicKey;
        public string certificateExpiresAt;
        
        [Header("YUCP Root Public Key")]
        [TextArea(3, 10)]
        public string yucpRootPublicKeyBase64 = "y+8Zs9/mS1MFZFeF4CFjwqe0nsLW8lCcwmyvBx6H0Zo=";

        [Tooltip("Trusted root keys fetched from the authoritative signing server /v1/keys endpoint.")]
        public List<TrustedRootKey> yucpTrustedRootKeys = new List<TrustedRootKey>();

        /// <summary>
        /// Returns the effective server URL: first provider's URL if providers are configured,
        /// otherwise the legacy <see cref="serverUrl"/> field.
        /// </summary>
        public string GetEffectiveServerUrl()
        {
            if (certificateProviders != null && certificateProviders.Count > 0
                && !string.IsNullOrEmpty(certificateProviders[0].serverUrl))
                return NormalizeConfiguredServerUrl(certificateProviders[0].serverUrl);
            return NormalizeConfiguredServerUrl(serverUrl);
        }

        public bool NormalizeServerConfiguration()
        {
            bool changed = false;

            string normalizedDefaultServerUrl = NormalizeConfiguredServerUrl(serverUrl);
            if (!string.Equals(serverUrl, normalizedDefaultServerUrl, StringComparison.Ordinal))
            {
                serverUrl = normalizedDefaultServerUrl;
                changed = true;
            }

            string normalizedAccountAppUrl = NormalizeAccountAppUrl(accountAppUrl);
            if (!string.Equals(accountAppUrl ?? string.Empty, normalizedAccountAppUrl ?? string.Empty, StringComparison.Ordinal))
            {
                accountAppUrl = normalizedAccountAppUrl;
                changed = true;
            }

            if (certificateProviders == null)
            {
                certificateProviders = new List<CertificateProvider>();
                return true;
            }

            foreach (var provider in certificateProviders)
            {
                if (provider == null)
                    continue;

                string normalizedProviderServerUrl = NormalizeConfiguredServerUrl(provider.serverUrl);
                if (!string.Equals(provider.serverUrl, normalizedProviderServerUrl, StringComparison.Ordinal))
                {
                    provider.serverUrl = normalizedProviderServerUrl;
                    changed = true;
                }

                string normalizedProviderAccountAppUrl = NormalizeAccountAppUrl(provider.accountAppUrl);
                if (!string.Equals(provider.accountAppUrl ?? string.Empty, normalizedProviderAccountAppUrl ?? string.Empty, StringComparison.Ordinal))
                {
                    provider.accountAppUrl = normalizedProviderAccountAppUrl;
                    changed = true;
                }
            }

            return changed;
        }

        public static string NormalizeConfiguredServerUrl(string configuredServerUrl, bool defaultIfEmpty = true)
        {
            if (string.IsNullOrWhiteSpace(configuredServerUrl))
                return defaultIfEmpty ? DefaultServerUrl : string.Empty;

            try
            {
                var uri = new Uri(configuredServerUrl.Trim(), UriKind.Absolute);
                string normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
                return string.Equals(normalized, DefaultServerUrl, StringComparison.OrdinalIgnoreCase)
                    ? DefaultServerUrl
                    : normalized;
            }
            catch
            {
                string trimmed = configuredServerUrl.Trim().TrimEnd('/');
                return trimmed;
            }
        }

        public static string NormalizeAccountAppUrl(string configuredAccountAppUrl)
        {
            if (string.IsNullOrWhiteSpace(configuredAccountAppUrl))
                return string.Empty;

            try
            {
                var uri = new Uri(configuredAccountAppUrl.Trim(), UriKind.Absolute);
                string host = uri.Host;
                int port = uri.IsDefaultPort ? -1 : uri.Port;

                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                {
                    if (port == 3001)
                        port = 3000;
                }
                else if (host.Equals("creators.yucp.club", StringComparison.OrdinalIgnoreCase) ||
                         host.Equals("api.creators.yucp.club", StringComparison.OrdinalIgnoreCase))
                {
                    host = "verify.creators.yucp.club";
                    port = -1;
                }

                var builder = new UriBuilder(uri)
                {
                    Host = host,
                    Port = port,
                    Query = string.Empty,
                    Fragment = string.Empty,
                };

                return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            }
            catch
            {
                string trimmed = configuredAccountAppUrl.Trim().TrimEnd('/');
                if (trimmed.IndexOf("creators.yucp.club", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "https://verify.creators.yucp.club";

                return trimmed;
            }
        }

        /// <summary>
        /// Returns the effective root public key: resolved from the active provider or the global field.
        /// </summary>
        public string GetEffectiveRootPublicKey()
        {
            var trustedKeys = GetEffectiveTrustedRootKeys();
            if (trustedKeys.Count > 0 && !string.IsNullOrEmpty(trustedKeys[0].publicKeyBase64))
                return trustedKeys[0].publicKeyBase64;
            if (certificateProviders != null && certificateProviders.Count > 0
                && !string.IsNullOrEmpty(certificateProviders[0].rootPublicKeyBase64))
                return certificateProviders[0].rootPublicKeyBase64;
            return yucpRootPublicKeyBase64;
        }

        public void SetTrustedRootKeysForServer(string serverUrl, IEnumerable<TrustedRootKey> trustedKeys)
        {
            var sanitized = SanitizeTrustedRootKeys(trustedKeys);
            var provider = GetProviderForServer(serverUrl);
            if (provider != null)
            {
                provider.trustedRootKeys = sanitized;
                if (sanitized.Count > 0)
                    provider.rootPublicKeyBase64 = sanitized[0].publicKeyBase64;
            }
            else
            {
                yucpTrustedRootKeys = sanitized;
                if (sanitized.Count > 0)
                    yucpRootPublicKeyBase64 = sanitized[0].publicKeyBase64;
            }
        }

        public bool TryGetTrustedRootPublicKey(string keyId, string algorithm, out string publicKeyBase64)
        {
            var trustedKeys = GetEffectiveTrustedRootKeys();
            foreach (var trustedKey in trustedKeys)
            {
                if (trustedKey == null || string.IsNullOrEmpty(trustedKey.publicKeyBase64))
                    continue;
                if (!string.Equals(trustedKey.algorithm, algorithm, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(trustedKey.keyId, keyId, StringComparison.Ordinal))
                    continue;

                publicKeyBase64 = trustedKey.publicKeyBase64;
                return true;
            }

            if (trustedKeys.Count == 0
                && string.Equals(algorithm, "Ed25519", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(GetEffectiveRootPublicKey()))
            {
                publicKeyBase64 = GetEffectiveRootPublicKey();
                return true;
            }

            publicKeyBase64 = null;
            return false;
        }

        private List<TrustedRootKey> GetEffectiveTrustedRootKeys()
        {
            if (certificateProviders != null && certificateProviders.Count > 0
                && certificateProviders[0].trustedRootKeys != null
                && certificateProviders[0].trustedRootKeys.Count > 0)
            {
                return certificateProviders[0].trustedRootKeys;
            }

            return yucpTrustedRootKeys ?? new List<TrustedRootKey>();
        }

        private CertificateProvider GetProviderForServer(string serverUrl)
        {
            if (certificateProviders == null || certificateProviders.Count == 0 || string.IsNullOrWhiteSpace(serverUrl))
                return null;

            string normalizedServerUrl = NormalizeServerUrl(serverUrl);
            foreach (var provider in certificateProviders)
            {
                if (provider == null || string.IsNullOrWhiteSpace(provider.serverUrl))
                    continue;
                if (string.Equals(NormalizeServerUrl(provider.serverUrl), normalizedServerUrl, StringComparison.OrdinalIgnoreCase))
                    return provider;
            }

            return null;
        }

        private static string NormalizeServerUrl(string serverUrl)
        {
            return NormalizeConfiguredServerUrl(serverUrl, defaultIfEmpty: false);
        }

        private static List<TrustedRootKey> SanitizeTrustedRootKeys(IEnumerable<TrustedRootKey> trustedKeys)
        {
            var sanitized = new List<TrustedRootKey>();
            if (trustedKeys == null)
                return sanitized;

            foreach (var trustedKey in trustedKeys)
            {
                if (trustedKey == null || string.IsNullOrWhiteSpace(trustedKey.publicKeyBase64))
                    continue;

                sanitized.Add(new TrustedRootKey
                {
                    keyId = trustedKey.keyId?.Trim() ?? "",
                    algorithm = string.IsNullOrWhiteSpace(trustedKey.algorithm)
                        ? "Ed25519"
                        : trustedKey.algorithm.Trim(),
                    publicKeyBase64 = trustedKey.publicKeyBase64.Trim(),
                });
            }

            return sanitized;
        }

        /// <summary>
        /// Returns the effective account certificates URL for billing and device management.
        /// </summary>
        public string GetEffectiveAccountCertificatesUrl(string serverUrlOverride = null)
        {
            string explicitBaseUrl = null;
            if (certificateProviders != null && certificateProviders.Count > 0
                && !string.IsNullOrEmpty(certificateProviders[0].accountAppUrl))
            {
                explicitBaseUrl = certificateProviders[0].accountAppUrl;
            }
            else if (!string.IsNullOrEmpty(accountAppUrl))
            {
                explicitBaseUrl = accountAppUrl;
            }

            if (!string.IsNullOrEmpty(explicitBaseUrl))
            {
                return $"{NormalizeAccountAppUrl(explicitBaseUrl).TrimEnd('/')}/dashboard/billing";
            }

            string server = !string.IsNullOrEmpty(serverUrlOverride)
                ? serverUrlOverride
                : GetEffectiveServerUrl();
            if (string.IsNullOrEmpty(server))
                return "https://verify.creators.yucp.club/dashboard/billing";

            try
            {
                var serverUri = new Uri(server);
                string scheme = serverUri.Scheme;
                string host = serverUri.Host;
                int port = serverUri.IsDefaultPort ? -1 : serverUri.Port;

                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                {
                    if (port == 3001)
                        port = 3000;
                }
                else if (host.StartsWith("api.", StringComparison.OrdinalIgnoreCase))
                {
                    string remainder = host.Substring(4);
                    host = remainder.StartsWith("verify.", StringComparison.OrdinalIgnoreCase)
                        ? remainder
                        : $"verify.{remainder}";
                    port = -1;
                }
                else if (host.StartsWith("creators.", StringComparison.OrdinalIgnoreCase))
                {
                    host = $"verify.{host}";
                    port = -1;
                }
                else if (!host.StartsWith("verify.", StringComparison.OrdinalIgnoreCase))
                {
                    return "https://verify.creators.yucp.club/dashboard/billing";
                }

                var builder = new UriBuilder(serverUri)
                {
                    Scheme = scheme,
                    Host = host,
                    Port = port,
                    Path = "/dashboard/billing",
                    Query = string.Empty,
                    Fragment = string.Empty,
                };
                return builder.Uri.ToString().TrimEnd('/');
            }
            catch
            {
                return "https://verify.creators.yucp.club/dashboard/billing";
            }
        }

        /// <summary>
        /// Store full certificate JSON
        /// </summary>
        public void StoreCertificate(string certificateJson)
        {
            _certificateJson = certificateJson;
            EditorUtility.SetDirty(this);
        }
        
        /// <summary>
        /// Get full certificate JSON
        /// </summary>
        public string GetCertificateJson()
        {
            return _certificateJson;
        }
        
        /// <summary>
        /// Check if certificate is valid
        /// </summary>
        public bool HasValidCertificate()
        {
            if (string.IsNullOrEmpty(publisherId) || string.IsNullOrEmpty(devPublicKey))
                return false;
                
            if (!string.IsNullOrEmpty(certificateExpiresAt))
            {
                if (DateTime.TryParse(certificateExpiresAt, out DateTime expiresAt))
                {
                    if (expiresAt < DateTime.UtcNow)
                        return false;
                }
            }
            
            return !string.IsNullOrEmpty(_certificateJson);
        }
        
        /// <summary>
        /// Clear certificate data
        /// </summary>
        public void ClearCertificate()
        {
            _certificateJson = null;
            publisherId = null;
            publisherName = null;
            vrchatUserId = null;
            vrchatDisplayName = null;
            devPublicKey = null;
            certificateExpiresAt = null;
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            
            // Notify Package Exporter window to refresh
            RefreshPackageExporterWindows();
        }

        /// <summary>
        /// Refresh all open Package Exporter windows after certificate changes
        /// </summary>
        private static void RefreshPackageExporterWindows()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                // Find all Package Exporter windows using reflection to avoid circular dependency
                var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                foreach (var window in windows)
                {
                    if (window != null)
                    {
                        var windowType = window.GetType();
                        // Check both by name and full name to be safe
                        if (windowType.Name == "YUCPPackageExporterWindow" || 
                            windowType.FullName == "YUCP.DevTools.Editor.PackageExporter.YUCPPackageExporterWindow")
                        {
                            var refreshMethod = windowType.GetMethod("RefreshSigningSection", 
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (refreshMethod != null)
                            {
                                refreshMethod.Invoke(window, null);
                            }
                        }
                    }
                }
            };
        }

        private void OnValidate()
        {
            if (NormalizeServerConfiguration())
                EditorUtility.SetDirty(this);
        }
    }
}
