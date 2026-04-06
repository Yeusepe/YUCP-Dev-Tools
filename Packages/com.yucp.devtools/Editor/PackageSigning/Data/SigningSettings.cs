using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace YUCP.DevTools.Editor.PackageSigning.Data
{
    /// <summary>
    /// A named certificate provider (signing authority) with its own server URL and root public key.
    /// </summary>
    [Serializable]
    public class CertificateProvider
    {
        [Tooltip("Display name for this certificate provider")]
        public string name = "";

        [Tooltip("API server URL for certificate issuance and package signing")]
        public string serverUrl = "https://api.creators.yucp.club";

        [Tooltip("Optional web account base URL for certificate billing and device management. Leave empty to derive from the server URL.")]
        public string accountAppUrl = "";

        [Tooltip("Root public key (Ed25519, base64) used to verify certificates issued by this provider. Leave empty to use the global YUCP root key.")]
        [TextArea(1, 3)]
        public string rootPublicKeyBase64 = "";
    }

    [CreateAssetMenu(fileName = "SigningSettings", menuName = "YUCP/Package Signing Settings")]
    public class SigningSettings : ScriptableObject
    {
        [Header("Server Configuration")]
        [Tooltip("Default signing server URL. Used when no per-profile or per-provider URL is configured.")]
        public string serverUrl = "https://api.creators.yucp.club";

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

        /// <summary>
        /// Returns the effective server URL: first provider's URL if providers are configured,
        /// otherwise the legacy <see cref="serverUrl"/> field.
        /// </summary>
        public string GetEffectiveServerUrl()
        {
            if (certificateProviders != null && certificateProviders.Count > 0
                && !string.IsNullOrEmpty(certificateProviders[0].serverUrl))
                return certificateProviders[0].serverUrl;
            return string.IsNullOrEmpty(serverUrl) ? "https://api.creators.yucp.club" : serverUrl;
        }

        /// <summary>
        /// Returns the effective root public key: resolved from the active provider or the global field.
        /// </summary>
        public string GetEffectiveRootPublicKey()
        {
            if (certificateProviders != null && certificateProviders.Count > 0
                && !string.IsNullOrEmpty(certificateProviders[0].rootPublicKeyBase64))
                return certificateProviders[0].rootPublicKeyBase64;
            return yucpRootPublicKeyBase64;
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
                return $"{explicitBaseUrl.TrimEnd('/')}/dashboard/billing";
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
                    host = remainder.StartsWith("creators.", StringComparison.OrdinalIgnoreCase)
                        ? remainder
                        : $"creators.{remainder}";
                }
                else if (!host.StartsWith("creators.", StringComparison.OrdinalIgnoreCase))
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
    }
}
