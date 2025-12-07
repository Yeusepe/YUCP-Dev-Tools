using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace YUCP.DevTools.Editor.PackageSigning.Data
{
    [CreateAssetMenu(fileName = "SigningSettings", menuName = "YUCP/Package Signing Settings")]
    public class SigningSettings : ScriptableObject
    {
        [Header("Server Configuration")]
        public string serverUrl = "https://signing.yucp.club";
        
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
        public string yucpRootPublicKeyBase64 = ""; // Will be set from server/plan
        
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
