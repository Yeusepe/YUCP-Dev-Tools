using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using YUCP.DevTools.Components;
using YUCP.DevTools.Editor.PackageExporter.UI.Components;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
        // Package name for the YUCP Package Importer — required when license verification is on.
        private const string YucpImporterPackageName = "com.yucp.importer";

        private void RebuildDependencyList(ExportProfile profile, VisualElement section, VisualElement container)
        {
            // Auto-add the YUCP Importer dependency when license verification is enabled.
            if (profile.requiresLicenseVerification)
            {
                bool hasImporter = profile.dependencies.Any(d =>
                    string.Equals(d.packageName, YucpImporterPackageName, StringComparison.OrdinalIgnoreCase));

                if (!hasImporter)
                {
                    var importerDep = new PackageDependency
                    {
                        packageName       = YucpImporterPackageName,
                        displayName       = "YUCP Package Importer",
                        packageVersion    = "1.0.0",
                        enabled           = true,
                        isVpmDependency   = true,
                        exportMode        = DependencyExportMode.Dependency,
                    };
                    profile.dependencies.Insert(0, importerDep);
                    EditorUtility.SetDirty(profile);
                }
                else
                {
                    // Make sure it's enabled — it can't be off while license gate is on
                    var importerDep = profile.dependencies.First(d =>
                        string.Equals(d.packageName, YucpImporterPackageName, StringComparison.OrdinalIgnoreCase));
                    if (!importerDep.enabled)
                    {
                        importerDep.enabled = true;
                        EditorUtility.SetDirty(profile);
                    }
                }
            }

            var scrollView = container.GetFirstAncestorOfType<ScrollView>();
            float? sectionTopRelativeToViewport = null;
            
            if (scrollView != null && section != null)
            {
                var sectionWorldBounds = section.worldBound;
                var scrollViewWorldBounds = scrollView.worldBound;
                
                if (sectionWorldBounds.height > 0 && scrollViewWorldBounds.height > 0)
                {
                    float sectionTopInViewport = sectionWorldBounds.y - scrollViewWorldBounds.y;
                    sectionTopRelativeToViewport = sectionTopInViewport;
                }
            }
            
            var filteredDependencies = profile.dependencies.AsEnumerable();
            
            if (!string.IsNullOrWhiteSpace(dependenciesSearchFilter))
            {
                filteredDependencies = filteredDependencies.Where(d =>
                    d.packageName.IndexOf(dependenciesSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(d.displayName) && d.displayName.IndexOf(dependenciesSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            
            // Apply toggle filters if they exist
            var enabledFilter = section.Q<Toggle>("enabled-filter");
            var vpmFilter = section.Q<Toggle>("vpm-filter");
            
            if (enabledFilter?.value == true)
            {
                filteredDependencies = filteredDependencies.Where(d => d.enabled);
            }
            
            if (vpmFilter?.value == true)
            {
                filteredDependencies = filteredDependencies.Where(d => d.isVpmDependency);
            }
            
            var filteredList = filteredDependencies.ToList();
            
            // Dependencies list
            if (filteredList.Count == 0 && profile.dependencies.Count > 0)
            {
                var emptyLabel = new Label("No dependencies match the current filter.");
                emptyLabel.AddToClassList("yucp-label-secondary");
                emptyLabel.style.paddingTop = 10;
                emptyLabel.style.paddingBottom = 10;
                container.Add(emptyLabel);
            }
            else if (profile.dependencies.Count == 0)
            {
                var emptyLabel = new Label("No dependencies configured. Add manually or scan.");
                emptyLabel.AddToClassList("yucp-label-secondary");
                emptyLabel.style.paddingTop = 10;
                emptyLabel.style.paddingBottom = 10;
                container.Add(emptyLabel);
            }
            else
            {
                for (int i = 0; i < filteredList.Count; i++)
                {
                    var dep = filteredList[i];
                    var originalIndex = profile.dependencies.IndexOf(dep);
                    var depCard = CreateDependencyCard(dep, originalIndex, profile);
                    container.Add(depCard);
                }
                
                // Select all/none buttons
                var selectButtons = new VisualElement();
                selectButtons.style.flexDirection = FlexDirection.Row;
                selectButtons.style.marginTop = 8;
                selectButtons.style.marginBottom = 8;
                
                var selectAllButton = new Button(() => 
                {
                    foreach (var dep in profile.dependencies)
                    {
                        dep.enabled = true;
                    }
                    EditorUtility.SetDirty(profile);
                    container.Clear();
                    RebuildDependencyList(profile, section, container);
                }) { text = "Select All" };
                selectAllButton.AddToClassList("yucp-button");
                selectAllButton.AddToClassList("yucp-button-action");
                selectAllButton.style.flexGrow = 1;
                selectAllButton.style.marginRight = 4;
                selectButtons.Add(selectAllButton);
                
                var deselectAllButton = new Button(() => 
                {
                    foreach (var dep in profile.dependencies)
                    {
                        // Never disable the YUCP Importer while license verification is on
                        bool isLockedImporter = profile.requiresLicenseVerification &&
                            string.Equals(dep.packageName, YucpImporterPackageName, StringComparison.OrdinalIgnoreCase);
                        if (!isLockedImporter)
                            dep.enabled = false;
                    }
                    EditorUtility.SetDirty(profile);
                    container.Clear();
                    RebuildDependencyList(profile, section, container);
                }) { text = "Deselect All" };
                deselectAllButton.AddToClassList("yucp-button");
                deselectAllButton.AddToClassList("yucp-button-action");
                deselectAllButton.style.flexGrow = 1;
                deselectAllButton.style.marginLeft = 4;
                selectButtons.Add(deselectAllButton);
                
                container.Add(selectButtons);
            }
            
            if (scrollView != null && sectionTopRelativeToViewport.HasValue)
            {
                scrollView.schedule.Execute(() =>
                {
                    if (scrollView != null && section != null)
                    {
                        var sectionWorldBounds = section.worldBound;
                        var scrollViewWorldBounds = scrollView.worldBound;
                        
                        if (sectionWorldBounds.height > 0)
                        {
                            float currentSectionTop = sectionWorldBounds.y - scrollViewWorldBounds.y;
                            float targetSectionTop = sectionTopRelativeToViewport.Value;
                            float scrollAdjustment = currentSectionTop - targetSectionTop;
                            
                            if (Mathf.Abs(scrollAdjustment) > 1f)
                            {
                                float newScrollValue = Mathf.Max(0, scrollView.verticalScroller.value + scrollAdjustment);
                                scrollView.verticalScroller.value = newScrollValue;
                            }
                        }
                    }
                });
            }
        }

        private VisualElement CreateDependenciesSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.name = "dependencies-section";
            
            var header = CreateCollapsibleHeader("Package Dependencies", 
                () => showDependencies, 
                (value) => { showDependencies = value; }, 
                () => UpdateProfileDetails());
            section.Add(header);
            
            if (!showDependencies)
            {
                return section;
            }
            
            var helpBox = new VisualElement();
            helpBox.AddToClassList("yucp-help-box");
            helpBox.AddToClassList("yucp-help-box-info");
            var helpText = new Label("Bundle: Include dependency files directly in the exported package\nDependency: Add to package.json for automatic download when package is installed");
            helpText.AddToClassList("yucp-help-box-text");
            helpBox.Add(helpText);
            section.Add(helpBox);

            // Show a notice when license protection is active — YUCP Importer is mandatory
            if (profile.requiresLicenseVerification)
            {
                var licenseNotice = new VisualElement();
                licenseNotice.AddToClassList("yucp-help-box");
                licenseNotice.style.marginTop = 8;
                licenseNotice.style.backgroundColor = new Color(0.1f, 0.45f, 0.1f, 0.35f);
                licenseNotice.style.borderLeftWidth = 3;
                licenseNotice.style.borderLeftColor = new StyleColor(new Color(0.3f, 0.85f, 0.3f, 1f));

                // Header row: warning icon + title
                var noticeHeader = new VisualElement();
                noticeHeader.style.flexDirection = FlexDirection.Row;
                noticeHeader.style.alignItems = Align.Center;
                noticeHeader.style.marginBottom = 4;

                var warnIcon = new Image
                {
                    image = EditorGUIUtility.IconContent("console.warnicon.sml").image,
                };
                warnIcon.style.width  = 16;
                warnIcon.style.height = 16;
                warnIcon.style.flexShrink = 0;
                warnIcon.style.marginRight = 5;
                noticeHeader.Add(warnIcon);

                var noticeTitleLabel = new Label("License Protection is ON");
                noticeTitleLabel.AddToClassList("yucp-help-box-text");
                noticeTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                noticeHeader.Add(noticeTitleLabel);
                licenseNotice.Add(noticeHeader);

                var licenseNoticeText = new Label(
                    "YUCP Package Importer (com.yucp.importer) is required and cannot be disabled " +
                    "while license verification is enabled.");
                licenseNoticeText.AddToClassList("yucp-help-box-text");
                licenseNoticeText.style.whiteSpace = WhiteSpace.Normal;
                licenseNotice.Add(licenseNoticeText);
                section.Add(licenseNotice);
            }
            
            // Disclaimer about automatic installation
            var disclaimerBox = new VisualElement();
            disclaimerBox.AddToClassList("yucp-help-box");
            disclaimerBox.AddToClassList("yucp-help-box-info");
            disclaimerBox.style.marginTop = 8;
            disclaimerBox.style.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.2f);
            var disclaimerText = new Label("Everything you select here will be asked for the user to install when importing the package, and will be installed automatically if clicked \"Install\" on their end");
            disclaimerText.AddToClassList("yucp-help-box-text");
            disclaimerText.style.unityFontStyleAndWeight = FontStyle.Bold;
            disclaimerBox.Add(disclaimerText);
            section.Add(disclaimerBox);
            
            // Search/Filter for dependencies
            if (profile.dependencies.Count > 0)
            {
                var searchRow = new VisualElement();
                searchRow.style.flexDirection = FlexDirection.Row;
                searchRow.style.marginTop = 8;
                searchRow.style.marginBottom = 8;
                
                var searchField = new TextField { value = dependenciesSearchFilter };
                searchField.AddToClassList("yucp-input");
                searchField.style.flexGrow = 1;
                searchField.style.marginRight = 4;
                searchField.name = "dependencies-search-field";
                searchField.RegisterValueChangedCallback(evt =>
                {
                    dependenciesSearchFilter = evt.newValue;
                    // Update dependency list without rebuilding entire UI
                    var depListContainer = section.Q<VisualElement>("dep-list-container");
                    if (depListContainer != null)
                    {
                        depListContainer.Clear();
                        RebuildDependencyList(profile, section, depListContainer);
                    }
                });
                searchRow.Add(searchField);
                
                var clearSearchButton = new Button(() => 
                {
                    dependenciesSearchFilter = "";
                    var searchField = section.Q<TextField>("dependencies-search-field");
                    if (searchField != null)
                    {
                        searchField.value = "";
                    }
                    var depListContainer = section.Q<VisualElement>("dep-list-container");
                    if (depListContainer != null)
                    {
                        depListContainer.Clear();
                        RebuildDependencyList(profile, section, depListContainer);
                    }
                }) { text = "Clear" };
                clearSearchButton.AddToClassList("yucp-button");
                clearSearchButton.AddToClassList("yucp-button-small");
                searchRow.Add(clearSearchButton);
                
                section.Add(searchRow);
                
                // Filter toggles
                var filterRow = new VisualElement();
                filterRow.style.flexDirection = FlexDirection.Row;
                filterRow.style.marginBottom = 8;
                
                var enabledToggle = new Toggle("Enabled Only") { value = false };
                enabledToggle.name = "enabled-filter";
                enabledToggle.AddToClassList("yucp-toggle");
                enabledToggle.RegisterValueChangedCallback(evt =>
                {
                    var depListContainer = section.Q<VisualElement>("dep-list-container");
                    if (depListContainer != null)
                    {
                        depListContainer.Clear();
                        RebuildDependencyList(profile, section, depListContainer);
                    }
                });
                filterRow.Add(enabledToggle);
                
                var vpmToggle = new Toggle("VPM Only") { value = false };
                vpmToggle.name = "vpm-filter";
                vpmToggle.AddToClassList("yucp-toggle");
                vpmToggle.RegisterValueChangedCallback(evt =>
                {
                    var depListContainer = section.Q<VisualElement>("dep-list-container");
                    if (depListContainer != null)
                    {
                        depListContainer.Clear();
                        RebuildDependencyList(profile, section, depListContainer);
                    }
                });
                filterRow.Add(vpmToggle);
                
                section.Add(filterRow);
            }
            
            // Dependency list container (wraps the list for easy rebuilding)
            var depListContainer = new VisualElement();
            depListContainer.name = "dep-list-container";
            depListContainer.AddToClassList("yucp-dependency-list-container");
            depListContainer.style.width = Length.Percent(100);
            depListContainer.style.maxWidth = Length.Percent(100);
            depListContainer.style.minWidth = 0;
            depListContainer.style.overflow = Overflow.Hidden;
            RebuildDependencyList(profile, section, depListContainer);
            section.Add(depListContainer);
            
            // Action buttons
            var addButton = new Button(() => AddDependency(profile)) { text = "Add dependency manually" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.flexGrow = 1;
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            // Action buttons - row 2 (Auto-Detect)
            if (profile.dependencies.Count > 0 && profile.foldersToExport.Count > 0)
            {
                var autoDetectButton = new Button(() => AutoDetectUsedDependencies(profile)) { text = "Auto-Detect Used" };
                autoDetectButton.AddToClassList("yucp-button");
                autoDetectButton.AddToClassList("yucp-button-action");
                autoDetectButton.style.marginTop = 8;
                section.Add(autoDetectButton);
            }
            else if (profile.foldersToExport.Count == 0)
            {
                var hintBox = new VisualElement();
                hintBox.AddToClassList("yucp-help-box");
                hintBox.AddToClassList("yucp-help-box-info");
                hintBox.style.marginTop = 8;
                var hintText = new Label("Add export folders first, then use 'Auto-Detect Used' to find dependencies");
                hintText.AddToClassList("yucp-help-box-text");
                hintBox.Add(hintText);
                section.Add(hintBox);
            }
            
            return section;
        }

        private VisualElement CreateDependencyCard(PackageDependency dep, int index, ExportProfile profile)
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-dependency-card");
            card.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
            card.style.marginBottom = 8;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.borderLeftWidth = 0;
            card.style.width = Length.Percent(100);
            card.style.maxWidth = Length.Percent(100);
            card.style.minWidth = 0;
            
            // Header row
            var headerRow = new VisualElement();
            headerRow.AddToClassList("yucp-dependency-card-header");
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = dep.enabled ? 8 : 0;
            headerRow.style.width = Length.Percent(100);
            headerRow.style.maxWidth = Length.Percent(100);
            headerRow.style.minWidth = 0;
            
            // Enable checkbox
            var enableToggle = new Toggle { value = dep.enabled };
            enableToggle.AddToClassList("yucp-toggle");
            enableToggle.style.marginRight = 8;
            enableToggle.style.flexShrink = 0;
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                // Prevent disabling the YUCP Importer while license protection is on
                if (profile.requiresLicenseVerification &&
                    string.Equals(dep.packageName, YucpImporterPackageName, StringComparison.OrdinalIgnoreCase))
                {
                    enableToggle.SetValueWithoutNotify(true);
                    return;
                }
                dep.enabled = evt.newValue;
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            });
            headerRow.Add(enableToggle);
            
            // Package name label
            string label = dep.isVpmDependency ? "[VPM] " : "";
            label += string.IsNullOrEmpty(dep.displayName) ? dep.packageName : dep.displayName;
            
            var nameLabel = new Label(label);
            nameLabel.AddToClassList("yucp-label");
            nameLabel.AddToClassList("yucp-dependency-card-name");
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.flexGrow = 1;
            nameLabel.style.minWidth = 0;
            headerRow.Add(nameLabel);
            
            // Remove button — hidden for license-locked dependencies
            var removeButton = new Button(() => 
            {
                profile.dependencies.RemoveAt(index);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }) { text = "×" };
            removeButton.AddToClassList("yucp-button");
            removeButton.AddToClassList("yucp-button-danger");
            removeButton.AddToClassList("yucp-button-small");
            removeButton.style.width = 25;
            removeButton.style.flexShrink = 0;
            // Hide remove button for the YUCP Importer while license protection is on
            removeButton.style.display = (profile.requiresLicenseVerification &&
                string.Equals(dep.packageName, YucpImporterPackageName, StringComparison.OrdinalIgnoreCase))
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            headerRow.Add(removeButton);
            
            // Is this the YUCP Importer locked by license protection?
            bool isLicenseLocked = profile.requiresLicenseVerification &&
                string.Equals(dep.packageName, YucpImporterPackageName, StringComparison.OrdinalIgnoreCase);

            // Tint the card and show a lock badge for the mandatory importer
            if (isLicenseLocked)
            {
                card.style.backgroundColor = new Color(0.05f, 0.22f, 0.05f, 1f);
                card.style.borderLeftWidth = 3;
                card.style.borderLeftColor = new StyleColor(new Color(0.3f, 0.85f, 0.3f, 1f));

                // Lock badge: Unity built-in LockIcon + "Required" text
                var lockBadge = new VisualElement();
                lockBadge.style.flexDirection = FlexDirection.Row;
                lockBadge.style.alignItems = Align.Center;
                lockBadge.style.marginRight = 6;
                lockBadge.style.flexShrink = 0;

                var lockIcon = new Image
                {
                    image = EditorGUIUtility.IconContent("LockIcon").image,
                };
                lockIcon.style.width  = 12;
                lockIcon.style.height = 12;
                lockIcon.style.flexShrink = 0;
                lockIcon.style.marginRight = 3;
                lockBadge.Add(lockIcon);

                var lockLabel = new Label("Required");
                lockLabel.AddToClassList("yucp-label-secondary");
                lockLabel.style.fontSize = 10;
                lockLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                lockLabel.style.color = new StyleColor(new Color(0.4f, 0.9f, 0.4f, 1f));
                lockBadge.Add(lockLabel);

                // Insert before the (hidden) remove button — after the name label
                headerRow.Insert(headerRow.IndexOf(removeButton), lockBadge);
            }

            card.Add(headerRow);
            
            // Warning box for problematic dependencies
            string packageName = dep.packageName ?? "";
            string displayName = dep.displayName ?? "";
            string combinedName = (packageName + " " + displayName).ToLower();
            
            // Check for "temp" or "temporary" but NOT "template"
            bool hasTempWarning = (combinedName.Contains(" temp ") || combinedName.StartsWith("temp ") || combinedName.EndsWith(" temp") || 
                                   combinedName == "temp" || combinedName.Contains("temporary")) && 
                                   !combinedName.Contains("template");
            bool hasDevToolsWarning = combinedName.Contains("yucp dev tools") || combinedName.Contains("yucp.devtools");
            
            if (hasTempWarning || hasDevToolsWarning)
            {
                string warningMessage = "";
                if (hasTempWarning && hasDevToolsWarning)
                {
                    warningMessage = "'Temp' or 'Temporary' folders are usually unique to your project and are not recommended to be included for the general public.\n\n" +
                                   "'YUCP Dev tools' is for creators and is not recommended to be included for the general public.";
                }
                else if (hasTempWarning)
                {
                    warningMessage = "'Temp' or 'Temporary' folders are usually unique to your project and are not recommended to be included for the general public.";
                }
                else if (hasDevToolsWarning)
                {
                    warningMessage = "'YUCP Dev tools' is for creators and is not recommended to be included for the general public.";
                }
                
                var warningBox = new VisualElement();
                warningBox.name = "dependency-warning-box";
                warningBox.AddToClassList("yucp-help-box");
                warningBox.style.marginTop = 8;
                warningBox.style.marginBottom = 8;
                warningBox.style.backgroundColor = new Color(0.7f, 0.65f, 0.1f, 0.3f); // Yellow warning color (more yellow, less green)
                
                var warningText = new Label(warningMessage);
                warningText.AddToClassList("yucp-help-box-text");
                warningText.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.9f, 1f)); // Light gray/white text to match design
                warningBox.Add(warningText);
                
                card.Add(warningBox);
            }
            
            // Warning for VPM dependencies without a resolvable download URL
            bool showVpmMissingWarning = false;
            bool vpmLookupPerformed = false;
            bool vpmLookupOk = false;
            string vpmLookupError = "";
            string vpmRepoName = "";
            string vpmRepoUrl = "";
            bool vpmHasDownloadUrl = false;
            List<string> vpmCheckedUrls = new List<string>();
            
            if (dep.enabled && dep.exportMode == DependencyExportMode.Dependency && dep.isVpmDependency)
            {
                vpmLookupOk = DependencyScanner.TryGetVpmDependencyRepoInfo(
                    dep,
                    out vpmRepoName,
                    out vpmRepoUrl,
                    out vpmHasDownloadUrl,
                    out vpmCheckedUrls,
                    out vpmLookupError
                );
                vpmLookupPerformed = true;
                
                if (vpmLookupOk && !vpmHasDownloadUrl)
                {
                    showVpmMissingWarning = true;
                }
            }
            
            if (showVpmMissingWarning)
            {
                var warningBox = new VisualElement();
                warningBox.name = "dependency-warning-box-vpm";
                warningBox.AddToClassList("yucp-help-box");
                warningBox.style.marginTop = 8;
                warningBox.style.marginBottom = 8;
                warningBox.style.backgroundColor = new Color(0.7f, 0.25f, 0.25f, 0.25f);
                
                var warningText = new Label("This dependency is set to 'Dependency' but no download URL was found in any VPM repository. It will be skipped when generating package.json.");
                warningText.AddToClassList("yucp-help-box-text");
                warningText.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.9f, 1f));
                warningBox.Add(warningText);
                
                if (vpmCheckedUrls != null && vpmCheckedUrls.Count > 0)
                {
                    var checkedLabel = new Label($"Checked: {string.Join(", ", vpmCheckedUrls)}");
                    checkedLabel.AddToClassList("yucp-help-box-text");
                    checkedLabel.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f, 1f));
                    warningBox.Add(checkedLabel);
                }
                
                card.Add(warningBox);
            }
            
            // Content area - only show if enabled
            if (dep.enabled)
            {
                // Package Name field
                var packageNameRow = CreateFormRow("Package Name", tooltip: "Unique identifier for the package");
                var packageNameField = new TextField { value = dep.packageName };
                packageNameField.AddToClassList("yucp-input");
                packageNameField.AddToClassList("yucp-form-field");
                packageNameField.RegisterValueChangedCallback(evt =>
                {
                    dep.packageName = evt.newValue;
                    EditorUtility.SetDirty(profile);
                });
                packageNameRow.Add(packageNameField);
                card.Add(packageNameRow);
                
                // Version field
                var versionRow = CreateFormRow("Version", tooltip: "Package version");
                var versionField = new TextField { value = dep.packageVersion };
                versionField.AddToClassList("yucp-input");
                versionField.AddToClassList("yucp-form-field");
                versionField.RegisterValueChangedCallback(evt =>
                {
                    dep.packageVersion = evt.newValue;
                    EditorUtility.SetDirty(profile);
                });
                versionRow.Add(versionField);
                card.Add(versionRow);
                
                // Display Name field
                var displayNameRow = CreateFormRow("Display Name", tooltip: "Human-readable name");
                var displayNameField = new TextField { value = dep.displayName };
                displayNameField.AddToClassList("yucp-input");
                displayNameField.AddToClassList("yucp-form-field");
                displayNameField.RegisterValueChangedCallback(evt =>
                {
                    dep.displayName = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    UpdateProfileDetails(); // Refresh to update card title
                });
                displayNameRow.Add(displayNameField);
                card.Add(displayNameRow);

                
                // Export Mode dropdown
                var exportModeRow = CreateFormRow("Export Mode", tooltip: "How this dependency should be handled");
                var exportModeField = new EnumField(dep.exportMode);
                exportModeField.AddToClassList("yucp-dropdown");
                exportModeField.RegisterValueChangedCallback(evt =>
                {
                    dep.exportMode = (DependencyExportMode)evt.newValue;
                    EditorUtility.SetDirty(profile);
                });
                exportModeRow.Add(exportModeField);
                card.Add(exportModeRow);
                
                // VPM Package toggle
                var vpmToggle = new Toggle("VPM Package") { value = dep.isVpmDependency };
                vpmToggle.AddToClassList("yucp-toggle");
                if (!_isCompactMode) vpmToggle.tooltip = "Is this a VRChat Package Manager dependency?";
                vpmToggle.RegisterValueChangedCallback(evt =>
                {
                    dep.isVpmDependency = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    UpdateProfileDetails(); // Refresh to update card title
                });
                card.Add(vpmToggle);
                
                if (dep.isVpmDependency)
                {
                    var customRepoRow = CreateFormRow("Custom VPM Index URL", tooltip: "Optional repository index URL for this dependency");
                    var customRepoField = new TextField { value = dep.vpmRepositoryUrl };
                    customRepoField.AddToClassList("yucp-input");
                    customRepoField.AddToClassList("yucp-form-field");
                    customRepoField.RegisterValueChangedCallback(evt =>
                    {
                        dep.vpmRepositoryUrl = evt.newValue;
                        EditorUtility.SetDirty(profile);
                        UpdateProfileDetails();
                    });
                    customRepoRow.Add(customRepoField);
                    card.Add(customRepoRow);
                    
                    string repoInfoText = "Lookup unavailable";
                    
                    if (!vpmLookupPerformed)
                    {
                        vpmLookupOk = DependencyScanner.TryGetVpmDependencyRepoInfo(
                            dep,
                            out vpmRepoName,
                            out vpmRepoUrl,
                            out vpmHasDownloadUrl,
                            out vpmCheckedUrls,
                            out vpmLookupError
                        );
                        vpmLookupPerformed = true;
                    }
                    
                    if (vpmLookupOk)
                    {
                        if (vpmHasDownloadUrl && !string.IsNullOrEmpty(vpmRepoUrl))
                        {
                            repoInfoText = vpmRepoUrl;
                        }
                        else if (!string.IsNullOrEmpty(vpmLookupError))
                        {
                            repoInfoText = vpmLookupError;
                        }
                        else
                        {
                            repoInfoText = "No download URL found in any repository";
                        }
                    }
                    else if (!string.IsNullOrEmpty(vpmLookupError))
                    {
                        repoInfoText = vpmLookupError;
                    }
                    
                    var repoRow = CreateFormRow("Resolved VPM Index URL", tooltip: "Repository index URL used to resolve this dependency");
                    var repoLabel = new Label(repoInfoText);
                    repoLabel.AddToClassList("yucp-label-secondary");
                    repoLabel.AddToClassList("yucp-form-field");
                    repoLabel.style.whiteSpace = WhiteSpace.Normal;
                    repoLabel.style.flexGrow = 1;
                    repoLabel.style.flexShrink = 1;
                    repoLabel.style.minWidth = 0;
                    repoRow.Add(repoLabel);
                    card.Add(repoRow);
                }
            }
            
            return card;
        }

    }
}
