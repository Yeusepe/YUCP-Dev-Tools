using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.DevTools.Editor.PackageSigning.Core;
using YUCP.DevTools.Editor.PackageSigning.Data;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.DevTools.Editor.PackageSigning.UI
{
    /// <summary>
    /// Window for managing package signing settings and certificates
    /// Modern UIToolkit implementation with YUCP design system
    /// </summary>
    public class SigningSettingsWindow : EditorWindow
    {
        private Data.SigningSettings _settings;
        private string _devPublicKeyDisplay = "";
        private ScrollView _scrollView;
        private VisualElement _certificateCard;
        private VisualElement _rootKeyCard;

        [MenuItem("Tools/YUCP/Others/Development/Package Signing Settings", false, 100)]
        public static void ShowWindow()
        {
            // Redirect to Unity Preferences
            SettingsService.OpenProjectSettings("Project/YUCP Package Exporter");
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            
            // Load design system styles
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            
            // Load devtools stylesheet
            var devtoolsStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.devtools/Editor/Styles/YucpDesignSystem.uss");
            if (devtoolsStyle != null)
            {
                root.styleSheets.Add(devtoolsStyle);
            }

            root.style.backgroundColor = new Color(0.082f, 0.082f, 0.082f);

            // Main scroll view
            _scrollView = new ScrollView();
            _scrollView.AddToClassList("yucp-scrollview");
            root.Add(_scrollView);

            LoadSettings();
            RefreshDevKey();
            BuildUI();
        }

        private void OnEnable()
        {
            CreateGUI();
        }

        private void BuildUI()
        {
            _scrollView.Clear();

            // Header
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 20;
            header.style.paddingBottom = 12;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);

            var title = new Label("Package Signing Configuration");
            title.AddToClassList("yucp-section-title");
            title.style.marginBottom = 0;
            header.Add(title);

            _scrollView.Add(header);

            // Server Configuration Card
            if (_settings != null)
            {
                var serverCard = CreateServerConfigCard();
                _scrollView.Add(serverCard);
            }

            // Developer Key Card
            var devKeyCard = CreateDevKeyCard();
            _scrollView.Add(devKeyCard);

            // Certificate Card
            _certificateCard = CreateCertificateCard();
            _scrollView.Add(_certificateCard);

            // Root Public Key Card
            if (_settings != null)
            {
                _rootKeyCard = CreateRootKeyCard();
                _scrollView.Add(_rootKeyCard);
            }
        }

        private VisualElement CreateServerConfigCard()
        {
            var card = YUCPUIToolkitHelper.CreateCard("Server Configuration", "Signing authority server settings");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            var serverUrlField = new TextField("Server URL");
            serverUrlField.value = _settings.serverUrl;
            serverUrlField.AddToClassList("yucp-input");
            serverUrlField.RegisterValueChangedCallback(evt =>
            {
                _settings.serverUrl = evt.newValue;
                EditorUtility.SetDirty(_settings);
            });
            content.Add(serverUrlField);

            YUCPUIToolkitHelper.AddSpacing(content, 12);

            var saveButton = YUCPUIToolkitHelper.CreateButton(
                "Save Settings",
                () => {
                    EditorUtility.SetDirty(_settings);
                    AssetDatabase.SaveAssets();
                    Debug.Log("Signing settings saved");
                },
                YUCPUIToolkitHelper.ButtonVariant.Primary
            );
            content.Add(saveButton);

            return card;
        }

        private VisualElement CreateDevKeyCard()
        {
            var card = YUCPUIToolkitHelper.CreateCard("Developer Key", "Your Ed25519 public key");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            var infoAlert = YUCPUIToolkitHelper.CreateHelpBox(
                "Share this public key with admins to receive a signing certificate.",
                YUCPUIToolkitHelper.MessageType.Info
            );
            content.Add(infoAlert);

            YUCPUIToolkitHelper.AddSpacing(content, 12);

            // Key display row
            var keyRow = new VisualElement();
            keyRow.style.flexDirection = FlexDirection.Row;
            keyRow.style.alignItems = Align.FlexStart;

            var keyField = new TextField();
            keyField.value = _devPublicKeyDisplay;
            keyField.isReadOnly = true;
            keyField.AddToClassList("yucp-input");
            keyField.style.flexGrow = 1;
            keyField.style.fontSize = 11;
            keyRow.Add(keyField);

            var copyButton = YUCPUIToolkitHelper.CreateButton(
                "Copy",
                () => {
                    EditorGUIUtility.systemCopyBuffer = _devPublicKeyDisplay;
                    Debug.Log("Dev public key copied to clipboard");
                },
                YUCPUIToolkitHelper.ButtonVariant.Secondary
            );
            copyButton.style.marginLeft = 8;
            keyRow.Add(copyButton);

            content.Add(keyRow);

            YUCPUIToolkitHelper.AddSpacing(content, 8);

            // Action buttons row
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.flexWrap = Wrap.Wrap;

            var refreshButton = YUCPUIToolkitHelper.CreateButton(
                "Refresh",
                RefreshDevKey,
                YUCPUIToolkitHelper.ButtonVariant.Ghost
            );
            buttonRow.Add(refreshButton);

            var generateButton = YUCPUIToolkitHelper.CreateButton(
                "Generate New Key",
                () => {
                    if (EditorUtility.DisplayDialog(
                        "Generate New Key",
                        "This will replace your current developer key. You will need a new certificate.\n\nContinue?",
                        "Yes", "Cancel"))
                    {
                        RegenerateDevKey();
                        RefreshDevKey();
                    }
                },
                YUCPUIToolkitHelper.ButtonVariant.Ghost
            );
            buttonRow.Add(generateButton);

            content.Add(buttonRow);

            return card;
        }

        private VisualElement CreateCertificateCard()
        {
            var card = YUCPUIToolkitHelper.CreateCard("Certificate", "Package signing authorization");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            LoadSettings();

            if (_settings != null && _settings.HasValidCertificate())
            {
                // Valid certificate
                var successAlert = YUCPUIToolkitHelper.CreateHelpBox(
                    "Certificate is valid and active.",
                    YUCPUIToolkitHelper.MessageType.Success
                );
                content.Add(successAlert);

                YUCPUIToolkitHelper.AddSpacing(content, 12);

                // Certificate details
                var detailsContainer = new VisualElement();
                detailsContainer.style.flexDirection = FlexDirection.Column;

                AddDetailRow(detailsContainer, "Publisher ID", _settings.publisherId);
                AddDetailRow(detailsContainer, "Publisher Name", _settings.publisherName);
                AddDetailRow(detailsContainer, "VRChat User ID", _settings.vrchatUserId);
                AddDetailRow(detailsContainer, "VRChat Display Name", _settings.vrchatDisplayName);

                if (!string.IsNullOrEmpty(_settings.certificateExpiresAt))
                {
                    if (System.DateTime.TryParse(_settings.certificateExpiresAt, out System.DateTime expiresAt))
                    {
                        AddDetailRow(detailsContainer, "Expires", expiresAt.ToString("yyyy-MM-dd HH:mm:ss UTC"));
                    }
                }

                content.Add(detailsContainer);

                YUCPUIToolkitHelper.AddSpacing(content, 12);

                // Action buttons
                var buttonRow = new VisualElement();
                buttonRow.style.flexDirection = FlexDirection.Row;
                buttonRow.style.flexWrap = Wrap.Wrap;

                var importButton = YUCPUIToolkitHelper.CreateButton(
                    "Import New Certificate",
                    ImportCertificate,
                    YUCPUIToolkitHelper.ButtonVariant.Secondary
                );
                buttonRow.Add(importButton);

                var removeButton = YUCPUIToolkitHelper.CreateButton(
                    "Remove Certificate",
                    () => {
                        if (EditorUtility.DisplayDialog(
                            "Remove Certificate",
                            "Are you sure you want to remove the current certificate? Packages will no longer be signed until you import a new certificate.",
                            "Remove", "Cancel"))
                        {
                            if (_settings != null)
                            {
                                _settings.ClearCertificate();
                                EditorUtility.SetDirty(_settings);
                                AssetDatabase.SaveAssets();
                                BuildUI(); // Refresh UI
                            }
                        }
                    },
                    YUCPUIToolkitHelper.ButtonVariant.Ghost
                );
                removeButton.style.marginLeft = 8;
                buttonRow.Add(removeButton);

                content.Add(buttonRow);
            }
            else
            {
                // No certificate
                var warningAlert = YUCPUIToolkitHelper.CreateHelpBox(
                    "No valid certificate found. Import a .yucp_cert file to enable signing.",
                    YUCPUIToolkitHelper.MessageType.Warning
                );
                content.Add(warningAlert);

                YUCPUIToolkitHelper.AddSpacing(content, 12);

                var importButton = YUCPUIToolkitHelper.CreateButton(
                    "Import YUCP Certificate",
                    ImportCertificate,
                    YUCPUIToolkitHelper.ButtonVariant.Primary
                );
                content.Add(importButton);
            }

            return card;
        }

        private VisualElement CreateRootKeyCard()
        {
            var card = YUCPUIToolkitHelper.CreateCard("YUCP Root Public Key", "Authority verification key");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            var infoAlert = YUCPUIToolkitHelper.CreateHelpBox(
                "The root public key for verifying certificates. Set this from server configuration.",
                YUCPUIToolkitHelper.MessageType.Info
            );
            content.Add(infoAlert);

            YUCPUIToolkitHelper.AddSpacing(content, 12);

            var rootKeyField = new TextField();
            rootKeyField.multiline = true;
            rootKeyField.value = _settings.yucpRootPublicKeyBase64;
            rootKeyField.AddToClassList("yucp-input");
            rootKeyField.AddToClassList("yucp-input-multiline");
            rootKeyField.style.minHeight = 80;
            rootKeyField.style.fontSize = 11;
            rootKeyField.RegisterValueChangedCallback(evt =>
            {
                _settings.yucpRootPublicKeyBase64 = evt.newValue;
                EditorUtility.SetDirty(_settings);
            });
            content.Add(rootKeyField);

            return card;
        }

        private void AddDetailRow(VisualElement container, string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 6;

            var labelElement = new Label(label + ":");
            labelElement.style.width = 140;
            labelElement.AddToClassList("yucp-label-secondary");
            row.Add(labelElement);

            var valueElement = new Label(value);
            valueElement.style.flexGrow = 1;
            valueElement.AddToClassList("yucp-label");
            row.Add(valueElement);

            container.Add(row);
        }

        private void LoadSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _settings = AssetDatabase.LoadAssetAtPath<Data.SigningSettings>(path);
            }
            else
            {
                // Create settings if they don't exist
                _settings = ScriptableObject.CreateInstance<Data.SigningSettings>();
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
                BuildUI(); // Rebuild UI to show new certificate
            }
            else
            {
                EditorUtility.DisplayDialog("Certificate Import Failed", 
                    $"Failed to import certificate:\n\n{result.error}", 
                    "OK");
            }
        }
    }
}
