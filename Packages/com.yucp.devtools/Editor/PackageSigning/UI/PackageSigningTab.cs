using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.DevTools.Editor.PackageSigning.Core;
using YUCP.DevTools.Editor.PackageSigning.Data;
using YUCP.DevTools.Editor.PackageExporter;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.DevTools.Editor.PackageSigning.UI
{
    /// <summary>
    /// Package Signing section for Package Exporter window
    /// Integrated with YUCP design system
    /// </summary>
    public class PackageSigningTab
    {
        private VisualElement _root;
        private Data.SigningSettings _settings;
        private ExportProfile _profile;

        public PackageSigningTab(ExportProfile profile = null)
        {
            _profile = profile;
        }

        public VisualElement CreateUI()
        {
            _root = new VisualElement();
            _root.name = "PackageSigningTab";
            _root.AddToClassList("yucp-section");

            LoadSettings();

            // Single unified card following Hick's Law (one clear section)
            var mainCard = CreateMainCard();
            _root.Add(mainCard);

            return _root;
        }

        private VisualElement CreateMainCard()
        {
            LoadSettings();
            bool hasCertificate = _settings != null && _settings.HasValidCertificate();

            // Get proper title - use profile name if available
            string cardTitle = "Package Signing";
            if (_profile != null && !string.IsNullOrEmpty(_profile.packageName))
            {
                cardTitle = $"{_profile.packageName} - Signing";
            }

            var card = YUCPUIToolkitHelper.CreateCard(cardTitle, null);
            
            // Get the card title and apply proper section title styling for hierarchy
            var titleElement = card.Q<Label>(null, "yucp-card-title");
            if (titleElement != null)
            {
                titleElement.RemoveFromClassList("yucp-card-title");
                titleElement.AddToClassList("yucp-section-title");
            }
            
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            if (!hasCertificate)
            {
                // No certificate - simple, clear call to action (Hick's Law)
                var warningAlert = YUCPUIToolkitHelper.CreateHelpBox(
                    "Packages will not be signed. Import a certificate to enable automatic signing.",
                    YUCPUIToolkitHelper.MessageType.Warning
                );
                content.Add(warningAlert);

                YUCPUIToolkitHelper.AddSpacing(content, 12);

                var importButton = YUCPUIToolkitHelper.CreateButton(
                    "Import Certificate",
                    () => SigningSettingsWindow.ShowWindow(),
                    YUCPUIToolkitHelper.ButtonVariant.Primary
                );
                importButton.style.width = Length.Percent(100);
                content.Add(importButton);
                return card;
            }

            // Has certificate - show status and current package info
            // Status indicator (Aesthetic-Usability Effect - visual feedback)
            var statusRow = new VisualElement();
            statusRow.style.flexDirection = FlexDirection.Row;
            statusRow.style.alignItems = Align.Center;
            statusRow.style.marginBottom = 16;
            statusRow.style.paddingLeft = 12;
            statusRow.style.paddingRight = 12;
            statusRow.style.paddingTop = 10;
            statusRow.style.paddingBottom = 10;
            statusRow.style.backgroundColor = new Color(0.15f, 0.4f, 0.25f, 0.2f);
            statusRow.style.borderTopLeftRadius = 6;
            statusRow.style.borderTopRightRadius = 6;
            statusRow.style.borderBottomLeftRadius = 6;
            statusRow.style.borderBottomRightRadius = 6;

            var statusIcon = new Label("✓");
            statusIcon.style.fontSize = 16;
            statusIcon.style.color = new Color(0.3f, 0.9f, 0.5f);
            statusIcon.style.marginRight = 10;
            statusRow.Add(statusIcon);

            var statusText = new Label("This package will be signed");
            statusText.style.fontSize = 13;
            statusText.style.unityFontStyleAndWeight = FontStyle.Bold;
            statusRow.Add(statusText);

            content.Add(statusRow);

            // Certificate info - minimal, essential only (Miller's Law - chunking)
            var infoSection = new VisualElement();
            infoSection.style.marginTop = 8;

            if (!string.IsNullOrEmpty(_settings.publisherName))
            {
                var publisherRow = CreateCompactInfoRow("Publisher", _settings.publisherName);
                infoSection.Add(publisherRow);
            }

            if (!string.IsNullOrEmpty(_settings.certificateExpiresAt))
            {
                if (DateTime.TryParse(_settings.certificateExpiresAt, out DateTime expiresAt))
                {
                    TimeSpan timeUntilExpiry = expiresAt - DateTime.UtcNow;
                    string expirationText;
                    Color expirationColor = new Color(0.7f, 0.7f, 0.7f);
                    
                    if (timeUntilExpiry.TotalDays < 0)
                    {
                        expirationText = "Expired";
                        expirationColor = new Color(0.9f, 0.3f, 0.3f);
                    }
                    else if (timeUntilExpiry.TotalDays < 30)
                    {
                        expirationText = $"Expires in {Math.Ceiling(timeUntilExpiry.TotalDays)} days";
                        expirationColor = new Color(0.9f, 0.7f, 0.3f);
                    }
                    else
                    {
                        expirationText = $"Valid until {expiresAt:MMM dd, yyyy}";
                    }
                    
                    var expirationRow = CreateCompactInfoRow("Certificate", expirationText);
                    expirationRow.Q<Label>(className: "yucp-info-value").style.color = expirationColor;
                    infoSection.Add(expirationRow);
                }
            }

            content.Add(infoSection);

            // Current package status - ALWAYS show if profile exists (even if not signed yet)
            if (_profile != null)
            {
                YUCPUIToolkitHelper.AddSpacing(content, 16);
                
                var packageStatusSection = CreateCurrentPackageStatus();
                content.Add(packageStatusSection);
            }
            else
            {
                // No profile selected
                YUCPUIToolkitHelper.AddSpacing(content, 16);
                var noProfileAlert = YUCPUIToolkitHelper.CreateHelpBox(
                    "No export profile selected. Select a profile to manage its package signing.",
                    YUCPUIToolkitHelper.MessageType.Info
                );
                content.Add(noProfileAlert);
            }

            // Actions - minimal, clear (Hick's Law)
            YUCPUIToolkitHelper.AddSpacing(content, 12);
            var actionsRow = new VisualElement();
            actionsRow.style.flexDirection = FlexDirection.Row;

            var manageButton = YUCPUIToolkitHelper.CreateButton(
                "Manage Certificate",
                () => SigningSettingsWindow.ShowWindow(),
                YUCPUIToolkitHelper.ButtonVariant.Ghost
            );
            actionsRow.Add(manageButton);

            content.Add(actionsRow);

            return card;
        }

        private VisualElement CreateCompactInfoRow(string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 6;

            var labelElement = new Label(label + ":");
            labelElement.style.fontSize = 11;
            labelElement.AddToClassList("yucp-label-secondary");
            labelElement.style.width = 90;
            labelElement.style.flexShrink = 0;
            row.Add(labelElement);

            var valueElement = new Label(value);
            valueElement.style.fontSize = 12;
            valueElement.AddToClassList("yucp-info-value");
            valueElement.style.flexGrow = 1;
            valueElement.style.whiteSpace = WhiteSpace.Normal;
            row.Add(valueElement);

            return row;
        }

        private VisualElement CreateCurrentPackageStatus()
        {
            var section = new VisualElement();
            section.style.paddingLeft = 12;
            section.style.paddingRight = 12;
            section.style.paddingTop = 12;
            section.style.paddingBottom = 12;
            section.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.3f);
            section.style.borderTopLeftRadius = 6;
            section.style.borderTopRightRadius = 6;
            section.style.borderBottomLeftRadius = 6;
            section.style.borderBottomRightRadius = 6;

            var title = new Label("Current Package");
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            section.Add(title);

            // Show package info from profile
            var packageInfoRow = new VisualElement();
            packageInfoRow.style.flexDirection = FlexDirection.Column;
            packageInfoRow.style.marginBottom = 8;

            if (!string.IsNullOrEmpty(_profile.packageName))
            {
                var nameLabel = new Label(_profile.packageName);
                nameLabel.style.fontSize = 13;
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                packageInfoRow.Add(nameLabel);
            }

            if (!string.IsNullOrEmpty(_profile.version))
            {
                var versionLabel = new Label($"Version {_profile.version}");
                versionLabel.style.fontSize = 11;
                versionLabel.AddToClassList("yucp-label-secondary");
                versionLabel.style.marginTop = 4;
                packageInfoRow.Add(versionLabel);
            }

            section.Add(packageInfoRow);
            
            // Product ID fields
            YUCPUIToolkitHelper.AddSpacing(section, 12);
            
            var productIdsSection = new VisualElement();
            productIdsSection.style.marginTop = 8;
            
            var productIdsTitle = new Label("Product IDs");
            productIdsTitle.style.fontSize = 11;
            productIdsTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            productIdsTitle.style.marginBottom = 6;
            productIdsSection.Add(productIdsTitle);
            
            // Gumroad Product ID
            var gumroadRow = new VisualElement();
            gumroadRow.style.flexDirection = FlexDirection.Row;
            gumroadRow.style.alignItems = Align.Center;
            gumroadRow.style.marginBottom = 6;
            
            var gumroadLabel = new Label("Gumroad:");
            gumroadLabel.style.fontSize = 10;
            gumroadLabel.AddToClassList("yucp-label-secondary");
            gumroadLabel.style.width = 80;
            gumroadLabel.style.flexShrink = 0;
            gumroadRow.Add(gumroadLabel);
            
            var gumroadField = new TextField { value = _profile.gumroadProductId ?? "" };
            gumroadField.AddToClassList("yucp-input");
            gumroadField.style.fontSize = 11;
            gumroadField.style.flexGrow = 1;
            gumroadField.tooltip = "Enter your Gumroad Product ID";
            gumroadField.RegisterValueChangedCallback(evt =>
            {
                if (_profile != null)
                {
                    UnityEditor.Undo.RecordObject(_profile, "Change Gumroad Product ID");
                    _profile.gumroadProductId = evt.newValue;
                    UnityEditor.EditorUtility.SetDirty(_profile);
                }
            });
            gumroadRow.Add(gumroadField);
            productIdsSection.Add(gumroadRow);
            
            // Jinxxy Product ID
            var jinxxyRow = new VisualElement();
            jinxxyRow.style.flexDirection = FlexDirection.Row;
            jinxxyRow.style.alignItems = Align.Center;
            jinxxyRow.style.marginBottom = 6;
            
            var jinxxyLabel = new Label("Jinxxy:");
            jinxxyLabel.style.fontSize = 10;
            jinxxyLabel.AddToClassList("yucp-label-secondary");
            jinxxyLabel.style.width = 80;
            jinxxyLabel.style.flexShrink = 0;
            jinxxyRow.Add(jinxxyLabel);
            
            var jinxxyField = new TextField { value = _profile.jinxxyProductId ?? "" };
            jinxxyField.AddToClassList("yucp-input");
            jinxxyField.style.fontSize = 11;
            jinxxyField.style.flexGrow = 1;
            jinxxyField.tooltip = "Enter your Jinxxy Product ID";
            jinxxyField.RegisterValueChangedCallback(evt =>
            {
                if (_profile != null)
                {
                    UnityEditor.Undo.RecordObject(_profile, "Change Jinxxy Product ID");
                    _profile.jinxxyProductId = evt.newValue;
                    UnityEditor.EditorUtility.SetDirty(_profile);
                }
            });
            jinxxyRow.Add(jinxxyField);
            productIdsSection.Add(jinxxyRow);
            
            section.Add(productIdsSection);

            // If package has been signed, show status from server
            if (!string.IsNullOrEmpty(_profile.packageId))
            {
                // Loading state
                var loadingRow = new VisualElement();
                loadingRow.style.flexDirection = FlexDirection.Row;
                loadingRow.style.alignItems = Align.Center;

                var loadingLabel = new Label("Checking package status...");
                loadingLabel.style.fontSize = 11;
                loadingLabel.AddToClassList("yucp-label-secondary");
                loadingRow.Add(loadingLabel);
                section.Add(loadingRow);

                // Fetch package status
                PackageInfoService.GetPublisherPackages(
                    (packages) => {
                        EditorApplication.delayCall += () => {
                            if (_root == null || !section.parent.Contains(section)) return;
                            
                            loadingRow.RemoveFromHierarchy();
                            
                            // Find package matching this profile's packageId
                            var currentPackage = packages?.FirstOrDefault(p => 
                                !string.IsNullOrEmpty(p.packageId) && 
                                p.packageId == _profile.packageId);

                            if (currentPackage == null)
                            {
                                var notFoundLabel = new Label("Package not found in signing database");
                                notFoundLabel.style.fontSize = 11;
                                notFoundLabel.AddToClassList("yucp-label-secondary");
                                section.Add(notFoundLabel);
                                return;
                            }

                            // Show status badge
                            var statusRow = new VisualElement();
                            statusRow.style.flexDirection = FlexDirection.Row;
                            statusRow.style.alignItems = Align.Center;
                            statusRow.style.justifyContent = Justify.SpaceBetween;
                            statusRow.style.marginBottom = 8;

                            var statusBadge = CreateStatusBadge(currentPackage.status);
                            statusRow.Add(statusBadge);

                            section.Add(statusRow);

                            // Show signed date
                            if (!string.IsNullOrEmpty(currentPackage.createdAt) && 
                                DateTime.TryParse(currentPackage.createdAt, out DateTime created))
                            {
                                var dateLabel = new Label($"Signed on {created:MMM dd, yyyy}");
                                dateLabel.style.fontSize = 11;
                                dateLabel.AddToClassList("yucp-label-secondary");
                                section.Add(dateLabel);
                            }

                            // Revoke action (only if active)
                            if (currentPackage.status == "active")
                            {
                                YUCPUIToolkitHelper.AddSpacing(section, 8);
                                
                                var revokeButton = YUCPUIToolkitHelper.CreateButton(
                                    "Revoke Package",
                                    () => {
                                        if (EditorUtility.DisplayDialog(
                                            "Revoke Package",
                                            $"Are you sure you want to revoke this package?\n\nThis will mark it as revoked and it will fail verification.",
                                            "Revoke", "Cancel"))
                                        {
                                            PackageInfoService.RevokePackage(
                                                currentPackage.packageId,
                                                "Revoked by publisher",
                                                () => {
                                                    EditorApplication.delayCall += () => {
                                                        RefreshUI();
                                                    };
                                                },
                                                (error) => {
                                                    EditorUtility.DisplayDialog("Revoke Failed", $"Failed to revoke package: {error}", "OK");
                                                }
                                            );
                                        }
                                    },
                                    YUCPUIToolkitHelper.ButtonVariant.Ghost
                                );
                                revokeButton.style.width = Length.Percent(100);
                                section.Add(revokeButton);
                            }
                            else if (currentPackage.status == "revoked" && !string.IsNullOrEmpty(currentPackage.reason))
                            {
                                var revokedLabel = new Label($"Revoked: {currentPackage.reason}");
                                revokedLabel.style.fontSize = 11;
                                revokedLabel.style.color = new Color(0.9f, 0.5f, 0.3f);
                                revokedLabel.style.marginTop = 8;
                                section.Add(revokedLabel);
                            }
                        };
                    },
                    (error) => {
                        EditorApplication.delayCall += () => {
                            if (_root == null || !section.parent.Contains(section)) return;
                            loadingRow.RemoveFromHierarchy();
                            
                            var errorLabel = new Label($"Failed to check status: {error}");
                            errorLabel.style.fontSize = 11;
                            errorLabel.style.color = new Color(0.9f, 0.4f, 0.4f);
                            section.Add(errorLabel);
                        };
                    }
                );
            }
            else
            {
                // Package hasn't been signed yet
                var notSignedLabel = new Label("This package has not been signed yet. It will be signed when exported.");
                notSignedLabel.style.fontSize = 11;
                notSignedLabel.AddToClassList("yucp-label-secondary");
                notSignedLabel.style.marginTop = 8;
                section.Add(notSignedLabel);
            }

            return section;
        }

        private VisualElement CreateStatusBadge(string status)
        {
            var badge = new VisualElement();
            badge.style.paddingLeft = 8;
            badge.style.paddingRight = 8;
            badge.style.paddingTop = 4;
            badge.style.paddingBottom = 4;
            badge.style.borderTopLeftRadius = 4;
            badge.style.borderTopRightRadius = 4;
            badge.style.borderBottomLeftRadius = 4;
            badge.style.borderBottomRightRadius = 4;

            var label = new Label(status.ToUpper());
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;

            if (status == "active")
            {
                badge.style.backgroundColor = new Color(0.2f, 0.6f, 0.3f, 0.3f);
                label.style.color = new Color(0.4f, 0.9f, 0.5f);
            }
            else if (status == "revoked")
            {
                badge.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f, 0.3f);
                label.style.color = new Color(0.9f, 0.4f, 0.4f);
            }
            else
            {
                badge.style.backgroundColor = new Color(0.5f, 0.5f, 0.2f, 0.3f);
                label.style.color = new Color(0.9f, 0.8f, 0.4f);
            }

            badge.Add(label);
            return badge;
        }

        public void RefreshUI()
        {
            if (_root == null) return;

            _root.Clear();
            var mainCard = CreateMainCard();
            _root.Add(mainCard);
        }

        private void LoadSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _settings = AssetDatabase.LoadAssetAtPath<Data.SigningSettings>(path);
            }
        }

        public bool CanSign()
        {
            LoadSettings();
            return _settings != null && _settings.HasValidCertificate();
        }
    }
}
