using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// UI section for managing bundled profiles in composite export profiles
    /// </summary>
    public static class BundledProfilesSection
    {
        private static bool showBundledProfiles = true;
        private static VisualElement currentOverlay;
        
        /// <summary>
        /// Create the bundled profiles section UI
        /// </summary>
        public static VisualElement CreateBundledProfilesSection(ExportProfile profile, Action onUpdateDetails)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.name = "bundled-profiles-section";
            
            // Get bundled profiles count
            var bundledProfiles = profile.GetIncludedProfiles();
            int count = bundledProfiles.Count;
            
            // Create collapsible header matching YUCP design
            var header = CreateCollapsibleHeader("Bundled Profiles", 
                () => showBundledProfiles, 
                (value) => { showBundledProfiles = value; }, 
                onUpdateDetails);
            section.Add(header);
            
            if (!showBundledProfiles)
            {
                return section;
            }
            
            // Help text explaining what bundled profiles do
            var helpBox = new VisualElement();
            helpBox.AddToClassList("yucp-help-box");
            helpBox.AddToClassList("yucp-help-box-info");
            helpBox.style.marginTop = 8;
            var helpText = new Label("Bundled profiles allow you to combine multiple packages into one. When you export this profile, all assets from the bundled profiles will be included in the exported package.");
            helpText.AddToClassList("yucp-help-box-text");
            helpText.style.whiteSpace = WhiteSpace.Normal;
            helpBox.Add(helpText);
            section.Add(helpBox);
            
            // Profile cards container
            if (count > 0)
            {
                var cardsContainer = new VisualElement();
                cardsContainer.style.flexDirection = FlexDirection.Column;
                cardsContainer.style.marginTop = 12;
                cardsContainer.style.marginBottom = 8;
                
                // Add profile cards
                foreach (var bundledProfile in bundledProfiles)
                {
                    if (bundledProfile != null)
                    {
                        var card = CreateProfileCard(profile, bundledProfile, onUpdateDetails);
                        cardsContainer.Add(card);
                    }
                }
                
                section.Add(cardsContainer);
            }
            else
            {
                var emptyState = new VisualElement();
                emptyState.style.marginTop = 12;
                emptyState.style.marginBottom = 8;
                var emptyLabel = new Label("No bundled profiles. Click 'Add Bundled Profile' to add one.");
                emptyLabel.AddToClassList("yucp-label-secondary");
                emptyState.Add(emptyLabel);
                section.Add(emptyState);
            }
            
            // Add Profile button matching YUCP design
            var addButton = new Button(() => ShowProfilePickerOverlay(profile, onUpdateDetails)) { text = "+ Add Bundled Profile" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.marginTop = 8;
            addButton.tooltip = "Select profiles to bundle. Their assets will be included when exporting this profile.";
            section.Add(addButton);
            
            // Export options (only show if there are bundled profiles)
            if (count > 0)
            {
                var optionsContainer = new VisualElement();
                optionsContainer.style.flexDirection = FlexDirection.Column;
                optionsContainer.style.marginTop = 12;
                optionsContainer.style.paddingTop = 12;
                optionsContainer.style.borderTopWidth = 1;
                optionsContainer.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f, 1f);
                
                var optionsLabel = new Label("Export Options");
                optionsLabel.AddToClassList("yucp-label");
                optionsLabel.style.marginBottom = 8;
                optionsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                optionsContainer.Add(optionsLabel);
                
                // Bundle option
                var bundleToggle = new Toggle("Bundle all assets into one package")
                {
                    value = profile.bundleIncludedProfiles
                };
                bundleToggle.AddToClassList("yucp-toggle");
                bundleToggle.tooltip = "Include all assets from bundled profiles in the main package";
                bundleToggle.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Bundle Profiles");
                        profile.bundleIncludedProfiles = evt.newValue;
                        EditorUtility.SetDirty(profile);
                        onUpdateDetails?.Invoke();
                    }
                });
                optionsContainer.Add(bundleToggle);

                // Also export separately option
                var separateToggle = new Toggle("Also export bundled profiles separately")
                {
                    value = profile.alsoExportIncludedSeparately
                };
                separateToggle.AddToClassList("yucp-toggle");
                separateToggle.tooltip = "Export each bundled profile as a separate package in addition to the composite package";
                separateToggle.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Export Separately");
                        profile.alsoExportIncludedSeparately = evt.newValue;
                        EditorUtility.SetDirty(profile);
                        onUpdateDetails?.Invoke();
                    }
                });
                optionsContainer.Add(separateToggle);

                // Summary line: make it explicit whether export is one package or multiple
                var summaryLabel = new Label(GetBundledExportSummary(profile));
                summaryLabel.AddToClassList("yucp-label-secondary");
                summaryLabel.style.marginTop = 6;
                summaryLabel.style.whiteSpace = WhiteSpace.Normal;
                summaryLabel.style.unityFontStyleAndWeight = FontStyle.Normal;
                optionsContainer.Add(summaryLabel);

                section.Add(optionsContainer);
            }
            
            return section;
        }
        
        /// <summary>
        /// Create collapsible header matching YUCP design
        /// </summary>
        private static VisualElement CreateCollapsibleHeader(string title, Func<bool> getExpanded, Action<bool> setExpanded, Action onToggle)
        {
            var header = new VisualElement();
            header.AddToClassList("yucp-inspector-header");
            
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("yucp-section-title");
            header.Add(titleLabel);
            
            var toggleButton = new Button();
            
            void UpdateButtonText()
            {
                toggleButton.text = getExpanded() ? "▼" : "▶";
            }
            
            toggleButton.clicked += () =>
            {
                bool current = getExpanded();
                setExpanded(!current);
                UpdateButtonText();
                onToggle?.Invoke();
            };
            
            toggleButton.AddToClassList("yucp-button");
            toggleButton.AddToClassList("yucp-button-small");
            UpdateButtonText();
            header.Add(toggleButton);
            
            return header;
        }
        
        /// <summary>
        /// Create a profile card for a bundled profile matching YUCP design
        /// </summary>
        /// <summary>
        /// Create a profile card for a bundled profile matching YUCP design
        /// </summary>
        private static VisualElement CreateProfileCard(ExportProfile parent, ExportProfile bundled, Action onUpdateDetails)
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-bundled-profile-card");
            
            // 1. Icon (Rounded container)
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("yucp-bundled-profile-icon-container");
            
            var iconImage = new Image();
            Texture2D profileIcon = bundled.icon;
            if (profileIcon == null)
            {
                // Use Unity's default script icon or default asset
                profileIcon = EditorGUIUtility.FindTexture("ScriptableObject Icon");
                if (profileIcon == null)
                {
                    profileIcon = EditorGUIUtility.FindTexture("DefaultAsset Icon");
                }
            }
            iconImage.image = profileIcon;
            iconImage.AddToClassList("yucp-bundled-profile-icon");
            iconContainer.Add(iconImage);
            card.Add(iconContainer);

            // 2. Content Column (Name + Details)
            var contentCol = new VisualElement();
            contentCol.AddToClassList("yucp-bundled-profile-content");

            // Name Row (Name + Status)
            var nameRow = new VisualElement();
            nameRow.AddToClassList("yucp-bundled-profile-name-row");
            
            var nameLabel = new Label(bundled.packageName ?? "Unnamed Profile");
            nameLabel.AddToClassList("yucp-bundled-profile-name");
            nameRow.Add(nameLabel);

            // Status Badge
            var status = CompositeProfileResolver.GetProfileStatus(bundled);
            var statusBadge = CreateStatusBadge(status);
            // Adjust badge margins for this context if needed, but default is left-margin 8 which is fine
            nameRow.Add(statusBadge);
            
            contentCol.Add(nameRow);

            // Details Row (Version | Folder)
            var detailsRow = new VisualElement();
            detailsRow.AddToClassList("yucp-bundled-profile-details-row");

            if (!string.IsNullOrEmpty(bundled.version))
            {
                var versionLabel = new Label($"v{bundled.version}");
                versionLabel.AddToClassList("yucp-bundled-profile-detail-text");
                detailsRow.Add(versionLabel);
            }

            if (bundled.foldersToExport != null && bundled.foldersToExport.Count > 0)
            {
                // Separator if we have version
                if (!string.IsNullOrEmpty(bundled.version)) 
                {
                    var sep = new Label("•");
                    sep.AddToClassList("yucp-bundled-profile-detail-text");
                    sep.style.marginRight = 8;
                    sep.style.fontSize = 8; // Smaller separator
                    detailsRow.Add(sep);
                }

                string firstFolder = bundled.foldersToExport[0];
                if (firstFolder.Length > 40)
                {
                    firstFolder = firstFolder.Substring(0, 15) + "..." + firstFolder.Substring(firstFolder.Length - 15);
                }
                
                var folderLabel = new Label(firstFolder);
                folderLabel.AddToClassList("yucp-bundled-profile-detail-text");
                detailsRow.Add(folderLabel);
            }
            
            contentCol.Add(detailsRow);
            card.Add(contentCol);

            // 3. Remove Button
            var removeButton = new Button(() =>
            {
                if (EditorUtility.DisplayDialog(
                    "Remove Bundled Profile",
                    $"Remove '{bundled.packageName}' from bundled profiles?",
                    "Remove",
                    "Cancel"))
                {
                    Undo.RecordObject(parent, "Remove Bundled Profile");
                    parent.RemoveIncludedProfile(bundled);
                    EditorUtility.SetDirty(parent);
                    AssetDatabase.SaveAssets();
                    onUpdateDetails?.Invoke();
                }
            });
            removeButton.text = "×";
            removeButton.AddToClassList("yucp-bundled-profile-remove-btn");
            card.Add(removeButton);
            
            // Click to Ping
            card.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left click
                {
                    Selection.activeObject = bundled;
                    EditorGUIUtility.PingObject(bundled);
                }
            });
            
            return card;
        }
        
        /// <summary>
        /// Returns a short summary of how bundled profiles will be exported (one package vs multiple).
        /// </summary>
        private static string GetBundledExportSummary(ExportProfile profile)
        {
            if (profile == null) return "";
            bool onePackage = profile.bundleIncludedProfiles;
            bool alsoSeparate = profile.alsoExportIncludedSeparately;
            if (onePackage && alsoSeparate)
                return "Export: One composite package containing all assets, plus each bundled profile as a separate package.";
            if (onePackage)
                return "Export: All assets from bundled profiles will be merged into one package.";
            if (alsoSeparate)
                return "Export: Each bundled profile will be exported as a separate package (no composite merge).";
            return "Export: Enable an option above to include bundled profile assets.";
        }

        /// <summary>
        /// Create a status badge for a profile
        /// </summary>
        private static VisualElement CreateStatusBadge(CompositeProfileResolver.ProfileStatus status)
        {
            var badge = new Label();
            badge.style.marginLeft = 8;
            badge.style.paddingLeft = 6;
            badge.style.paddingRight = 6;
            badge.style.paddingTop = 2;
            badge.style.paddingBottom = 2;
            badge.style.borderTopLeftRadius = 4;
            badge.style.borderTopRightRadius = 4;
            badge.style.borderBottomLeftRadius = 4;
            badge.style.borderBottomRightRadius = 4;
            
            switch (status)
            {
                case CompositeProfileResolver.ProfileStatus.Valid:
                    badge.text = "✓";
                    badge.style.color = new Color(0.2f, 0.8f, 0.2f);
                    badge.tooltip = "Profile is valid";
                    break;
                case CompositeProfileResolver.ProfileStatus.HasErrors:
                    badge.text = "⚠";
                    badge.style.color = new Color(0.9f, 0.7f, 0.2f);
                    badge.tooltip = "Profile has validation errors";
                    break;
                case CompositeProfileResolver.ProfileStatus.Missing:
                    badge.text = "✗";
                    badge.style.color = new Color(0.9f, 0.3f, 0.3f);
                    badge.tooltip = "Profile is missing or deleted";
                    break;
                case CompositeProfileResolver.ProfileStatus.Cycle:
                    badge.text = "↻";
                    badge.style.color = new Color(0.9f, 0.3f, 0.3f);
                    badge.tooltip = "Cycle detected";
                    break;
            }
            
            return badge;
        }
        
        /// <summary>
        /// Show profile picker overlay using UI Toolkit matching YUCP design
        /// </summary>
        private static void ShowProfilePickerOverlay(ExportProfile parent, Action onUpdateDetails)
        {
            var window = EditorWindow.GetWindow<YUCPPackageExporterWindow>();
            if (window == null)
                return;
            
            // Close existing overlay if any
            CloseOverlay();
            
            // Get already bundled profiles to pre-select them
            var alreadyBundled = new HashSet<ExportProfile>(parent.GetIncludedProfiles());
            
            // Get all profiles
            var allProfiles = CompositeProfileResolver.GetAllExportProfiles();
            
            // Separate into already bundled and available to add
            var alreadyBundledList = allProfiles.Where(p => p != null && alreadyBundled.Contains(p)).OrderBy(p => p.packageName).ToList();
            var availableProfiles = allProfiles.Where(p =>
            {
                if (p == null || p == parent)
                    return false;
                
                // Check if adding would create a cycle
                if (CompositeProfileResolver.WouldCreateCycle(parent, p))
                    return false;
                
                return true; // Include already bundled ones so they show as checked
            }).OrderBy(p => p.packageName).ToList();
            
            if (availableProfiles.Count == 0 && alreadyBundledList.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No Available Profiles",
                    "No profiles available to bundle. All profiles are either already bundled, would create a cycle, or are invalid.",
                    "OK");
                return;
            }
            
            // Create overlay backdrop
            var backdrop = new VisualElement();
            backdrop.AddToClassList("yucp-overlay-backdrop");
            backdrop.RegisterCallback<ClickEvent>(evt => CloseOverlay());
            
            // Create overlay panel matching YUCP design - use same background as main window
            var overlay = new VisualElement();
            overlay.style.width = 500;
            overlay.style.maxHeight = 600;
            // Match main window background color from YucpDesignSystem.uss: #151515
            overlay.style.backgroundColor = new Color(0.082f, 0.082f, 0.082f, 1f); // #151515
            overlay.style.borderTopLeftRadius = 8;
            overlay.style.borderTopRightRadius = 8;
            overlay.style.borderBottomLeftRadius = 8;
            overlay.style.borderBottomRightRadius = 8;
            overlay.style.borderTopWidth = 1;
            overlay.style.borderBottomWidth = 1;
            overlay.style.borderLeftWidth = 1;
            overlay.style.borderRightWidth = 1;
            overlay.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            overlay.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            overlay.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            overlay.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            overlay.style.paddingTop = 16;
            overlay.style.paddingBottom = 16;
            overlay.style.paddingLeft = 16;
            overlay.style.paddingRight = 16;
            overlay.style.flexShrink = 0;
            overlay.RegisterCallback<ClickEvent>(evt => evt.StopPropagation()); // Prevent closing when clicking inside
            
            // Container to center the panel
            var overlayContainer = new VisualElement();
            overlayContainer.style.flexDirection = FlexDirection.Row;
            overlayContainer.style.justifyContent = Justify.Center;
            overlayContainer.style.alignItems = Align.Center;
            overlayContainer.style.width = Length.Percent(100);
            overlayContainer.style.height = Length.Percent(100);
            overlayContainer.Add(overlay);
            
            // Header
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 16;
            
            var title = new Label("Select Profiles to Bundle");
            title.AddToClassList("yucp-section-title");
            title.style.flexGrow = 1;
            header.Add(title);
            
            var closeButton = new Button(CloseOverlay) { text = "×" };
            closeButton.AddToClassList("yucp-button");
            closeButton.AddToClassList("yucp-button-icon");
            closeButton.style.width = 32;
            closeButton.style.height = 32;
            header.Add(closeButton);
            
            overlay.Add(header);
            
            // Help text
            var helpBox = new VisualElement();
            helpBox.AddToClassList("yucp-help-box");
            helpBox.style.marginBottom = 12;
            var helpText = new Label("Select one or more profiles to bundle. Already bundled profiles are pre-selected. Their assets will be included when exporting this profile.");
            helpText.AddToClassList("yucp-help-box-text");
            helpText.style.whiteSpace = WhiteSpace.Normal;
            helpBox.Add(helpText);
            overlay.Add(helpBox);
            
            // Search field
            var searchRow = new VisualElement();
            searchRow.style.flexDirection = FlexDirection.Row;
            searchRow.style.marginBottom = 12;
            
            var searchField = new TextField { value = "" };
            searchField.AddToClassList("yucp-input");
            searchField.style.flexGrow = 1;
            searchRow.Add(searchField);
            
            overlay.Add(searchRow);
            
            // Profile list with checkboxes
            var scrollView = new ScrollView();
            scrollView.style.flexGrow = 1;
            scrollView.style.maxHeight = 400;
            
            var profileList = new VisualElement();
            profileList.name = "profile-list";
            
            string searchFilter = "";
            // Pre-populate with already bundled profiles
            HashSet<ExportProfile> selectedProfiles = new HashSet<ExportProfile>(alreadyBundled);
            
            // Declare RebuildList first
            Action RebuildList = null;
            
            // Then assign it
            RebuildList = () =>
            {
                profileList.Clear();
                
                var filtered = availableProfiles.Where(p =>
                {
                    if (string.IsNullOrEmpty(searchFilter))
                        return true;
                    
                    string search = searchFilter.ToLower();
                    return (p.packageName?.ToLower().Contains(search) ?? false) ||
                           (p.version?.ToLower().Contains(search) ?? false) ||
                           (p.foldersToExport?.Any(f => f.ToLower().Contains(search)) ?? false);
                }).ToList();
                
                if (filtered.Count == 0)
                {
                    var emptyLabel = new Label("No profiles match your search.");
                    emptyLabel.AddToClassList("yucp-label-secondary");
                    emptyLabel.style.paddingTop = 20;
                    emptyLabel.style.paddingBottom = 20;
                    profileList.Add(emptyLabel);
                }
                else
                {
                    foreach (var profile in filtered)
                    {
                        var profileItem = CreateProfileListItem(profile, selectedProfiles, RebuildList);
                        profileList.Add(profileItem);
                    }
                }
            };
            
            searchField.RegisterValueChangedCallback(evt =>
            {
                searchFilter = evt.newValue;
                RebuildList();
            });
            
            RebuildList();
            scrollView.Add(profileList);
            overlay.Add(scrollView);
            
            // Action buttons
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.FlexEnd;
            buttonRow.style.marginTop = 16;
            
            var cancelButton = new Button(CloseOverlay) { text = "Cancel" };
            cancelButton.AddToClassList("yucp-button");
            cancelButton.style.marginRight = 8;
            buttonRow.Add(cancelButton);
            
            var addButton = new Button(() =>
            {
                if (selectedProfiles.Count > 0)
                {
                    Undo.RecordObject(parent, "Update Bundled Profiles");

                    // Remove profiles that were unselected
                    var currentBundled = new HashSet<ExportProfile>(parent.GetIncludedProfiles());
                    foreach (var profile in currentBundled)
                    {
                        if (!selectedProfiles.Contains(profile))
                        {
                            parent.RemoveIncludedProfile(profile);
                        }
                    }

                    // Add newly selected profiles
                    foreach (var profile in selectedProfiles)
                    {
                        if (!currentBundled.Contains(profile))
                        {
                            parent.AddIncludedProfile(profile);
                        }
                    }

                    EditorUtility.SetDirty(parent);
                    AssetDatabase.SaveAssets();
                    CloseOverlay();
                    // Defer refresh so the overlay is fully closed and the details pane can rebuild
                    UnityEditor.EditorApplication.delayCall += () =>
                    {
                        onUpdateDetails?.Invoke();
                    };
                }
            })
            {
                text = $"Add ({selectedProfiles.Count})"
            };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-primary");
            
            // Update button text when selection changes
            Action UpdateAddButton = () =>
            {
                addButton.text = selectedProfiles.Count > 0 ? $"Add ({selectedProfiles.Count})" : "Add";
                addButton.SetEnabled(selectedProfiles.Count > 0);
            };
            
            // Wrap RebuildList to also update button
            var originalRebuild = RebuildList;
            RebuildList = () =>
            {
                if (originalRebuild != null)
                {
                    originalRebuild();
                }
                UpdateAddButton();
            };
            
            // Initial build
            RebuildList();
            UpdateAddButton();
            buttonRow.Add(addButton);
            overlay.Add(buttonRow);
            
            backdrop.Add(overlayContainer);
            
            // Add to window root
            var root = window.rootVisualElement;
            root.Add(backdrop);
            
            currentOverlay = backdrop;
            
            // Animate in with fade and scale
            backdrop.style.opacity = 0;
            overlay.style.scale = new Scale(new Vector3(0.95f, 0.95f, 1f));
            backdrop.schedule.Execute(() =>
            {
                backdrop.style.opacity = 1;
                overlay.style.scale = new Scale(Vector3.one);
            }).StartingIn(10);
        }
        
        /// <summary>
        /// Create a profile list item with checkbox - clickable anywhere
        /// </summary>
        private static VisualElement CreateProfileListItem(ExportProfile profile, HashSet<ExportProfile> selectedProfiles, Action onSelectionChanged)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-card");
            item.style.marginBottom = 8;
            item.style.paddingLeft = 12;
            item.style.paddingRight = 12;
            item.style.paddingTop = 12;
            item.style.paddingBottom = 12;
            // Add rounded corners
            item.style.borderTopLeftRadius = 6;
            item.style.borderTopRightRadius = 6;
            item.style.borderBottomLeftRadius = 6;
            item.style.borderBottomRightRadius = 6;
            
            // Make entire card clickable
            bool isSelected = selectedProfiles.Contains(profile);
            
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            
            // Checkbox
            var checkbox = new Toggle { value = isSelected };
            checkbox.AddToClassList("yucp-toggle");
            checkbox.style.marginRight = 12;
            checkbox.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    selectedProfiles.Add(profile);
                }
                else
                {
                    selectedProfiles.Remove(profile);
                }
                onSelectionChanged?.Invoke();
            });
            // Prevent checkbox click from triggering card click
            checkbox.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
            row.Add(checkbox);
            
            // Add hover effect with smooth transition
            item.RegisterCallback<MouseEnterEvent>(evt =>
            {
                item.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 1f);
                item.style.transitionDuration = new List<TimeValue> { new TimeValue(150, TimeUnit.Millisecond) };
            });
            item.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                item.style.backgroundColor = StyleKeyword.Null;
            });
            
            // Click handler for entire card - toggle selection
            item.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left click
                {
                    isSelected = !isSelected;
                    checkbox.value = isSelected; // Update checkbox
                    if (isSelected)
                    {
                        selectedProfiles.Add(profile);
                    }
                    else
                    {
                        selectedProfiles.Remove(profile);
                    }
                    onSelectionChanged?.Invoke();
                }
            });
            
            // Icon
            var iconContainer = new VisualElement();
            iconContainer.style.width = 32;
            iconContainer.style.height = 32;
            iconContainer.style.marginRight = 12;
            iconContainer.style.flexShrink = 0;
            
            var iconImage = new Image();
            Texture2D profileIcon = profile.icon;
            if (profileIcon == null)
            {
                profileIcon = EditorGUIUtility.FindTexture("ScriptableObject Icon");
                if (profileIcon == null)
                {
                    profileIcon = EditorGUIUtility.FindTexture("DefaultAsset Icon");
                }
            }
            iconImage.image = profileIcon;
            iconImage.style.width = 32;
            iconImage.style.height = 32;
            iconImage.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            iconContainer.Add(iconImage);
            row.Add(iconContainer);
            
            // Info
            var infoColumn = new VisualElement();
            infoColumn.style.flexGrow = 1;
            
            var nameLabel = new Label(profile.packageName ?? "Unnamed Profile");
            nameLabel.AddToClassList("yucp-label");
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            infoColumn.Add(nameLabel);
            
            var detailsRow = new VisualElement();
            detailsRow.style.flexDirection = FlexDirection.Row;
            detailsRow.style.marginTop = 4;
            
            if (!string.IsNullOrEmpty(profile.version))
            {
                var versionLabel = new Label($"v{profile.version}");
                versionLabel.AddToClassList("yucp-label-secondary");
                versionLabel.style.marginRight = 8;
                detailsRow.Add(versionLabel);
            }
            
            if (profile.foldersToExport != null && profile.foldersToExport.Count > 0)
            {
                string folder = profile.foldersToExport[0];
                if (folder.Length > 40)
                    folder = folder.Substring(0, 37) + "...";
                
                var folderLabel = new Label(folder);
                folderLabel.AddToClassList("yucp-label-secondary");
                detailsRow.Add(folderLabel);
            }
            
            infoColumn.Add(detailsRow);
            row.Add(infoColumn);
            
            item.Add(row);
            return item;
        }
        
        /// <summary>
        /// Close the overlay
        /// </summary>
        private static void CloseOverlay()
        {
            if (currentOverlay != null)
            {
                // Find the overlay panel inside the container
                var overlayContainer = currentOverlay.Q<VisualElement>();
                var overlay = overlayContainer?.Q<VisualElement>();
                
                // Animate out
                if (overlay != null)
                {
                    overlay.style.scale = new Scale(new Vector3(0.95f, 0.95f, 1f));
                }
                currentOverlay.style.opacity = 0;
                
                // Remove after animation
                currentOverlay.schedule.Execute(() =>
                {
                    if (currentOverlay != null)
                    {
                        currentOverlay.RemoveFromHierarchy();
                        currentOverlay = null;
                    }
                }).StartingIn(150);
            }
        }
    }
}
