using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using YUCP.DevTools.Editor.PackageSigning.Core;
using YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageExporter.Settings
{
    /// <summary>
    /// Settings provider for Package Exporter, including package signing configuration
    /// Accessible via Edit > Preferences > YUCP Package Exporter (Project settings)
    /// </summary>
    public class PackageExporterSettingsProvider : SettingsProvider
    {
        private SigningSettings _settings;
        private string _devPublicKeyDisplay = "";
        private Vector2 _scrollPosition;
        private bool _showServerConfig = true;
        private bool _showDevKey = true;
        private bool _showCertificate = true;
        private bool _showRootKey = true;

        public PackageExporterSettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
            : base(path, scope) { }

        public override void OnGUI(string searchContext)
        {
            LoadSettings();
            RefreshDevKey();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.Space(10);

            // Server Configuration Section
            _showServerConfig = EditorGUILayout.BeginFoldoutHeaderGroup(_showServerConfig, "Server Configuration");
            if (_showServerConfig)
            {
                EditorGUI.indentLevel++;
                if (_settings != null)
                {
                    EditorGUI.BeginChangeCheck();
                    _settings.serverUrl = EditorGUILayout.TextField("Server URL", _settings.serverUrl);
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(_settings);
                    }

                    EditorGUILayout.Space(5);
                    if (GUILayout.Button("Save Settings", GUILayout.Width(120)))
                    {
                        EditorUtility.SetDirty(_settings);
                        AssetDatabase.SaveAssets();
                        Debug.Log("Signing settings saved");
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("SigningSettings asset not found. It will be created automatically when needed.", MessageType.Info);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(5);

            // Developer Key Section
            _showDevKey = EditorGUILayout.BeginFoldoutHeaderGroup(_showDevKey, "Developer Key");
            if (_showDevKey)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Share this public key with admins to receive a signing certificate.", MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.TextField("Public Key", _devPublicKeyDisplay, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Copy", GUILayout.Width(60)))
                {
                    EditorGUIUtility.systemCopyBuffer = _devPublicKeyDisplay;
                    Debug.Log("Dev public key copied to clipboard");
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Refresh", GUILayout.Width(100)))
                {
                    RefreshDevKey();
                }
                if (GUILayout.Button("Generate New Key", GUILayout.Width(140)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Generate New Key",
                        "This will replace your current developer key. You will need a new certificate.\n\nContinue?",
                        "Yes", "Cancel"))
                    {
                        RegenerateDevKey();
                        RefreshDevKey();
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(5);

            // Certificate Section
            _showCertificate = EditorGUILayout.BeginFoldoutHeaderGroup(_showCertificate, "Certificate");
            if (_showCertificate)
            {
                EditorGUI.indentLevel++;
                LoadSettings();

                if (_settings != null && _settings.HasValidCertificate())
                {
                    EditorGUILayout.HelpBox("Certificate is valid and active.", MessageType.Info);

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Publisher ID:", _settings.publisherId);
                    EditorGUILayout.LabelField("Publisher Name:", _settings.publisherName);
                    EditorGUILayout.LabelField("VRChat User ID:", _settings.vrchatUserId);
                    EditorGUILayout.LabelField("VRChat Display Name:", _settings.vrchatDisplayName);

                    if (!string.IsNullOrEmpty(_settings.certificateExpiresAt))
                    {
                        if (DateTime.TryParse(_settings.certificateExpiresAt, out DateTime expiresAt))
                        {
                            EditorGUILayout.LabelField("Expires:", expiresAt.ToString("yyyy-MM-dd HH:mm:ss UTC"));
                        }
                    }

                    EditorGUILayout.Space(5);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Import New Certificate", GUILayout.Width(160)))
                    {
                        ImportCertificate();
                    }
                    if (GUILayout.Button("Remove Certificate", GUILayout.Width(160)))
                    {
                        if (EditorUtility.DisplayDialog(
                            "Remove Certificate",
                            "Are you sure you want to remove the current certificate? Packages will no longer be signed until you import a new certificate.",
                            "Remove", "Cancel"))
                        {
                            _settings.ClearCertificate();
                            EditorUtility.SetDirty(_settings);
                            AssetDatabase.SaveAssets();
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.HelpBox("No valid certificate found. Import a .yucp_cert file to enable signing.", MessageType.Warning);
                    EditorGUILayout.Space(5);
                    if (GUILayout.Button("Import YUCP Certificate", GUILayout.Width(160)))
                    {
                        ImportCertificate();
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(5);

            // Root Public Key Section
            if (_settings != null)
            {
                _showRootKey = EditorGUILayout.BeginFoldoutHeaderGroup(_showRootKey, "YUCP Root Public Key");
                if (_showRootKey)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.HelpBox("The root public key for verifying certificates. Set this from server configuration.", MessageType.Info);

                    EditorGUI.BeginChangeCheck();
                    _settings.yucpRootPublicKeyBase64 = EditorGUILayout.TextArea(
                        _settings.yucpRootPublicKeyBase64,
                        GUILayout.Height(80),
                        GUILayout.ExpandWidth(true));
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(_settings);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            EditorGUILayout.EndScrollView();
        }

        private void LoadSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _settings = AssetDatabase.LoadAssetAtPath<SigningSettings>(path);
            }
            else
            {
                // Create settings if they don't exist
                _settings = ScriptableObject.CreateInstance<SigningSettings>();
                string settingsPath = "Assets/YUCP/SigningSettings.asset";
                string dir = Path.GetDirectoryName(settingsPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                AssetDatabase.CreateAsset(_settings, settingsPath);
                AssetDatabase.SaveAssets();
            }
        }

        private void RefreshDevKey()
        {
            try
            {
                _devPublicKeyDisplay = DevKeyManager.GetPublicKeyBase64();
            }
            catch
            {
                _devPublicKeyDisplay = "Error loading dev key. Generate a new key.";
            }
        }

        private void RegenerateDevKey()
        {
            try
            {
                // Delete existing key file
                string keyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".unitysign",
                    "devkey.json"
                );
                if (File.Exists(keyPath))
                {
                    File.Delete(keyPath);
                }

                // Clear cache by using reflection to reset the cached keypair
                var devKeyManagerType = typeof(DevKeyManager);
                var cachedField = devKeyManagerType.GetField("_cachedKeyPair",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (cachedField != null)
                {
                    cachedField.SetValue(null, null);
                }

                // Generate new key (will be created on next GetOrCreateDevKey call)
                DevKeyManager.GetOrCreateDevKey();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to regenerate dev key: {ex.Message}");
            }
        }

        private void ImportCertificate()
        {
            string path = EditorUtility.OpenFilePanel("Import YUCP Certificate", "", "yucp_cert");
            if (string.IsNullOrEmpty(path))
                return;

            var result = CertificateManager.ImportAndVerify(path);

            if (result.valid)
            {
                EditorUtility.DisplayDialog("Certificate Imported",
                    $"Certificate imported successfully!\n\n" +
                    $"Publisher: {result.publisherName}\n" +
                    $"VRChat User: {result.vrchatDisplayName}\n" +
                    $"Expires: {result.expiresAt:yyyy-MM-dd HH:mm:ss UTC}",
                    "OK");
                LoadSettings();
            }
            else
            {
                EditorUtility.DisplayDialog("Certificate Import Failed",
                    $"Failed to import certificate:\n\n{result.error}",
                    "OK");
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new PackageExporterSettingsProvider("Project/YUCP Package Exporter", SettingsScope.Project);
        }
    }
}













