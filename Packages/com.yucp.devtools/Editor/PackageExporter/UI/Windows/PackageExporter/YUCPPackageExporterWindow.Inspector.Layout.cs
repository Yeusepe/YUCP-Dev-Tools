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
        private VisualElement CreateExportInspectorSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.name = "export-inspector-section";
            section.AddToClassList("yucp-section");
            section.style.flexGrow = 0;
            section.style.flexShrink = 1;
            section.style.minWidth = 0;
            section.style.maxWidth = Length.Percent(100);
            section.style.width = Length.Percent(100);
            section.style.overflow = Overflow.Hidden;
            
            var header = new VisualElement();
            header.AddToClassList("yucp-inspector-header");
            
            var title = new Label($"Export Inspector ({profile.discoveredAssets.Count} assets)");
            title.AddToClassList("yucp-section-title");
            title.style.flexGrow = 1;
            header.Add(title);
            
            var rescanButton = new Button(() => 
            {
                Undo.RecordObject(profile, "Rescan Assets");
                ScanAssetsForInspector(profile, silent: true);
            });
            rescanButton.tooltip = "Rescan Assets";
            rescanButton.AddToClassList("yucp-button");
            rescanButton.AddToClassList("yucp-button-small");
            
            var refreshIcon = EditorGUIUtility.IconContent("Refresh");
            if (refreshIcon != null && refreshIcon.image != null)
            {
                rescanButton.text = "";
                var iconImage = new Image();
                iconImage.image = refreshIcon.image as Texture2D;
                iconImage.style.width = 16;
                iconImage.style.height = 16;
                iconImage.style.alignSelf = Align.Center;
                iconImage.style.marginLeft = Length.Auto();
                iconImage.style.marginRight = Length.Auto();
                iconImage.style.marginTop = Length.Auto();
                iconImage.style.marginBottom = Length.Auto();
                rescanButton.Add(iconImage);
            }
            else
            {
                rescanButton.text = "⟳";
            }
            
            rescanButton.style.justifyContent = Justify.Center;
            rescanButton.style.alignItems = Align.Center;
            rescanButton.style.marginRight = 4;
            rescanButton.SetEnabled(ProfileHasContentToScan(profile));
            header.Add(rescanButton);
            
            var toggleButton = new Button(() => 
            {
                bool wasOpen = showExportInspector;
                showExportInspector = !showExportInspector;
                UpdateProfileDetails();
                
                // Scan when section is opened
                if (showExportInspector && !wasOpen && ProfileHasContentToScan(profile))
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (profile != null)
                        {
                            ScanAssetsForInspector(profile, silent: true);
                        }
                    };
                }
            }) 
            { text = showExportInspector ? "▼" : "▶" };
            toggleButton.AddToClassList("yucp-button");
            toggleButton.AddToClassList("yucp-button-small");
            header.Add(toggleButton);
            
            section.Add(header);
            
            // Auto-scan when section is visible and needs scanning
            if (showExportInspector && ProfileHasContentToScan(profile))
            {
                if (!profile.HasScannedAssets)
                {
                    // Scan immediately if not scanned yet
                    EditorApplication.delayCall += () =>
                    {
                        if (profile != null)
                        {
                            ScanAssetsForInspector(profile, silent: true);
                        }
                    };
                }
            }
            
            if (showExportInspector)
            {
                // Help box
                var helpBox = new VisualElement();
                helpBox.AddToClassList("yucp-help-box");
                var helpText = new Label("The Export Inspector shows all assets discovered from your export folders. Scan to discover assets, then deselect unwanted items or add folders to the permanent ignore list.");
                helpText.AddToClassList("yucp-help-box-text");
                helpBox.Add(helpText);
                section.Add(helpBox);
                
                // Action buttons
                var actionButtons = new VisualElement();
                actionButtons.AddToClassList("yucp-inspector-action-buttons");
                actionButtons.style.flexDirection = FlexDirection.Row;
                actionButtons.style.marginTop = 8;
                actionButtons.style.marginBottom = 8;
                
                // Show scan in progress message
                if (!profile.HasScannedAssets)
                {
                    var infoBox = new VisualElement();
                    infoBox.AddToClassList("yucp-help-box");
                    var infoText = new Label("Scanning assets... This will complete automatically.");
                    infoText.AddToClassList("yucp-help-box-text");
                    infoBox.Add(infoText);
                    section.Add(infoBox);
                }
                else
                {
                    // Statistics
                    var statsLabel = new Label("Asset Statistics");
                    statsLabel.AddToClassList("yucp-label");
                    statsLabel.style.marginTop = 8;
                    statsLabel.style.marginBottom = 4;
                    section.Add(statsLabel);
                    
					var summaryBox = new VisualElement();
                    summaryBox.AddToClassList("yucp-help-box");
                    var summaryText = new Label(AssetCollector.GetAssetSummary(profile.discoveredAssets));
                    summaryText.AddToClassList("yucp-help-box-text");
                    summaryBox.Add(summaryText);
                    section.Add(summaryBox);
					
					// Derived patch summary
					int derivedCount = profile.discoveredAssets.Count(a => IsDerivedFbx(a.assetPath, out _, out _));
					if (derivedCount > 0)
					{
						var derivedBox = new VisualElement();
						derivedBox.AddToClassList("yucp-help-box");
						var derivedText = new Label($"{derivedCount} FBX asset(s) are marked to export as Derived Patch packages.");
						derivedText.AddToClassList("yucp-help-box-text");
						derivedBox.Add(derivedText);
						section.Add(derivedBox);
					}
                    
                    // Filter controls
                    var filtersLabel = new Label("Filters");
                    filtersLabel.AddToClassList("yucp-label");
                    filtersLabel.style.marginTop = 8;
                    filtersLabel.style.marginBottom = 4;
                    section.Add(filtersLabel);
                    
                    var searchRow = new VisualElement();
                    searchRow.AddToClassList("yucp-inspector-search-row");
                    searchRow.style.marginBottom = 4;
                    
                    var searchField = new TextField { value = inspectorSearchFilter };
                    searchField.AddToClassList("yucp-input");
                    searchField.style.flexGrow = 1;
                    searchField.style.marginRight = 4;
                    searchField.name = "inspector-search-field";
                    searchField.RegisterValueChangedCallback(evt =>
                    {
                        inspectorSearchFilter = evt.newValue;
                        var assetListContainer = section.Q<VisualElement>("asset-list-container");
                        if (assetListContainer != null)
                        {
                            assetListContainer.Clear();
                            RebuildAssetList(profile, assetListContainer);
                        }
                    });
                    searchRow.Add(searchField);
                    
                    var clearSearchButton = new Button(() => 
                    {
                        inspectorSearchFilter = "";
                        var searchField = section.Q<TextField>("inspector-search-field");
                        if (searchField != null)
                        {
                            searchField.value = "";
                        }
                        var assetListContainer = section.Q<VisualElement>("asset-list-container");
                        if (assetListContainer != null)
                        {
                            assetListContainer.Clear();
                            RebuildAssetList(profile, assetListContainer);
                        }
                    }) { text = "Clear" };
                    clearSearchButton.AddToClassList("yucp-button");
                    clearSearchButton.AddToClassList("yucp-button-small");
                    searchRow.Add(clearSearchButton);
                    
                    section.Add(searchRow);
                    
                    // Source profile filter (for composite profiles)
                    if (profile.HasIncludedProfiles())
                    {
                        var sourceFilterRow = new VisualElement();
                        sourceFilterRow.AddToClassList("yucp-filter-row");
                        
                        var sourceFilterLabel = new Label("Source:");
                        sourceFilterLabel.AddToClassList("yucp-filter-label");
                        sourceFilterRow.Add(sourceFilterLabel);
                        
                        // Get unique source profiles
                        var sourceProfiles = new List<string> { "All" };
                        sourceProfiles.Add(profile.packageName); // Parent
                        var includedProfiles = profile.GetIncludedProfiles();
                        foreach (var included in includedProfiles)
                        {
                            if (included != null && !sourceProfiles.Contains(included.packageName))
                            {
                                sourceProfiles.Add(included.packageName);
                            }
                        }
                        
                        var sourceFilterDropdown = new DropdownField(sourceProfiles, 0);
                        sourceFilterDropdown.AddToClassList("yucp-input");
                        sourceFilterDropdown.AddToClassList("yucp-filter-dropdown");
                        sourceFilterDropdown.name = "source-filter-dropdown";
                        sourceFilterDropdown.SetValueWithoutNotify(sourceProfileFilter);
                        sourceFilterDropdown.RegisterValueChangedCallback(evt =>
                        {
                            sourceProfileFilter = evt.newValue;
                            var assetListContainer = section.Q<VisualElement>("asset-list-container");
                            if (assetListContainer != null)
                            {
                                assetListContainer.Clear();
                                RebuildAssetList(profile, assetListContainer);
                            }
                        });
                        sourceFilterRow.Add(sourceFilterDropdown);
                        section.Add(sourceFilterRow);
                    }
                    
                    var filterToggles = new VisualElement();
                    filterToggles.AddToClassList("yucp-inspector-filter-toggles");
                    filterToggles.style.marginBottom = 8;
                    
                    var includedToggle = new Toggle("Show Only Included") { value = showOnlyIncluded };
                    includedToggle.AddToClassList("yucp-toggle");
                    includedToggle.RegisterValueChangedCallback(evt =>
                    {
                        showOnlyIncluded = evt.newValue;
                        if (evt.newValue) showOnlyExcluded = false;
                        var assetListContainer = section.Q<VisualElement>("asset-list-container");
                        if (assetListContainer != null)
                        {
                            assetListContainer.Clear();
                            RebuildAssetList(profile, assetListContainer);
                        }
                    });
                    filterToggles.Add(includedToggle);
                    
                    var excludedToggle = new Toggle("Show Only Excluded") { value = showOnlyExcluded };
                    excludedToggle.AddToClassList("yucp-toggle");
                    excludedToggle.RegisterValueChangedCallback(evt =>
                    {
                        showOnlyExcluded = evt.newValue;
                        if (evt.newValue) showOnlyIncluded = false;
                        var assetListContainer = section.Q<VisualElement>("asset-list-container");
                        if (assetListContainer != null)
                        {
                            assetListContainer.Clear();
                            RebuildAssetList(profile, assetListContainer);
                        }
                    });
                    filterToggles.Add(excludedToggle);
                    
					// Show Only Derived toggle
					var derivedToggle = new Toggle("Show Only Derived") { value = showOnlyDerived };
					derivedToggle.AddToClassList("yucp-toggle");
                    derivedToggle.name = "derived-fbx-section";
					derivedToggle.RegisterValueChangedCallback(evt =>
					{
						showOnlyDerived = evt.newValue;
						var assetListContainer = section.Q<VisualElement>("asset-list-container");
						if (assetListContainer != null)
						{
							assetListContainer.Clear();
							RebuildAssetList(profile, assetListContainer);
						}
					});
					filterToggles.Add(derivedToggle);
					
					section.Add(filterToggles);
                    
                    // Asset list header with actions
                    var listHeader = new VisualElement();
                    listHeader.AddToClassList("yucp-inspector-list-header");
                    listHeader.style.marginBottom = 4;
                    
                    var listTitle = new Label("Discovered Assets");
                    listTitle.AddToClassList("yucp-label");
                    listHeader.Add(listTitle);
                    
                    var listActions = new VisualElement();
                    listActions.AddToClassList("yucp-inspector-list-actions");
                    
                    var includeAllButton = new Button(() => 
                    {
                        Undo.RecordObject(profile, "Include All Assets");
                        foreach (var asset in profile.discoveredAssets)
                            asset.included = true;
                        EditorUtility.SetDirty(profile);
                        AssetDatabase.SaveAssets();
                        UpdateProfileDetails();
                    }) { text = "Include All" };
                    includeAllButton.AddToClassList("yucp-button");
                    includeAllButton.AddToClassList("yucp-button-action");
                    includeAllButton.AddToClassList("yucp-button-small");
                    listActions.Add(includeAllButton);
                    
                    var excludeAllButton = new Button(() => 
                    {
                        Undo.RecordObject(profile, "Exclude All Assets");
                        foreach (var asset in profile.discoveredAssets)
                            asset.included = false;
                        EditorUtility.SetDirty(profile);
                        AssetDatabase.SaveAssets();
                        UpdateProfileDetails();
                    }) { text = "Exclude All" };
                    excludeAllButton.AddToClassList("yucp-button");
                    excludeAllButton.AddToClassList("yucp-button-action");
                    excludeAllButton.AddToClassList("yucp-button-small");
                    listActions.Add(excludeAllButton);
                    
                    listHeader.Add(listActions);
                    section.Add(listHeader);
                    
                    // Filter assets
                    var filteredAssets = profile.discoveredAssets.AsEnumerable();
                    
                    if (!string.IsNullOrWhiteSpace(inspectorSearchFilter))
                    {
                        filteredAssets = filteredAssets.Where(a => 
                            a.assetPath.IndexOf(inspectorSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    
                    if (showOnlyIncluded)
                        filteredAssets = filteredAssets.Where(a => a.included);
                    
                    if (showOnlyExcluded)
                        filteredAssets = filteredAssets.Where(a => !a.included);
                    
                    // Filter by source profile (for composite profiles)
                    if (!string.IsNullOrEmpty(sourceProfileFilter) && sourceProfileFilter != "All")
                    {
                        filteredAssets = filteredAssets.Where(a =>
                        {
                            // If asset has source profile, match it
                            if (!string.IsNullOrEmpty(a.sourceProfileName))
                            {
                                return a.sourceProfileName == sourceProfileFilter;
                            }
                            // If no source profile, it's from parent
                            return sourceProfileFilter == profile.packageName;
                        });
                    }
                    
                    var filteredList = filteredAssets.ToList();
                    
                    // Removed top resize handle - only using bottom edge resizing
                    
                    // Asset list container (wraps the scrollview for easy rebuilding)
                    var assetListContainer = new VisualElement();
                    assetListContainer.name = "asset-list-container";
                    // Load inspector height from profile, or use default
                    float profileHeight = profile != null ? profile.InspectorHeight : 500f;
                    assetListContainer.style.height = new Length(profileHeight, LengthUnit.Pixel);
                    assetListContainer.style.minHeight = 200;
                    // Don't set maxHeight - let it grow unlimited
                    assetListContainer.style.flexDirection = FlexDirection.Column; // Ensure children stack vertically
                    assetListContainer.style.overflow = Overflow.Visible; // Allow content to extend
                    assetListContainer.style.position = Position.Relative; // Ensure proper positioning
                    assetListContainer.pickingMode = PickingMode.Position;
                    
                    // Helper method for updating inspector resize height - similar to left pane resize
                    System.Action<Vector2> updateInspectorResizeHeight = (mousePosition) =>
                    {
                        if (!isResizingInspector || !assetListContainer.HasMouseCapture()) return;
                        
                        // Dragging down (mousePosition.y increases) should increase height
                        float deltaY = mousePosition.y - resizeStartY;
                        float newHeight = resizeStartHeight + deltaY;
                        // Allow very large sizes - no upper limit, only enforce minimum
                        newHeight = Mathf.Max(newHeight, 200f);
                        
                        // Update height immediately
                        assetListContainer.style.height = new Length(newHeight, LengthUnit.Pixel);
                        assetListContainer.style.maxHeight = new StyleLength(StyleKeyword.None);
                        assetListContainer.style.flexGrow = 0;
                        assetListContainer.style.flexShrink = 0;
                        assetListContainer.style.overflow = Overflow.Visible;
                        
                        // Force immediate repaint updates
                        assetListContainer.MarkDirtyRepaint();
                        
                        // Update resize rect for cursor
                        var worldBounds = assetListContainer.worldBound;
                        if (!worldBounds.Equals(Rect.zero))
                        {
                            currentResizeRect = new Rect(worldBounds.x, worldBounds.yMax - 10, worldBounds.width, 10);
                        }
                        
                        // Force immediate repaint of the window
                        Repaint();
                    };
                    
                    // Make the bottom edge of the container resizable
                    assetListContainer.RegisterCallback<MouseMoveEvent>(evt =>
                    {
                        if (!isResizingInspector)
                        {
                            // Check if mouse is near the bottom edge (within 10 pixels)
                            var localPos = evt.localMousePosition;
                            float containerHeight = assetListContainer.resolvedStyle.height;
                            if (containerHeight <= 0)
                            {
                                containerHeight = assetListContainer.layout.height;
                            }
                            if (localPos.y >= containerHeight - 10 && localPos.y <= containerHeight + 5)
                            {
                                assetListContainer.style.borderBottomWidth = 2;
                                assetListContainer.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                                currentResizableContainer = assetListContainer;
                                
                                // Calculate the resize rect in window coordinates for cursor
                                var worldBounds = assetListContainer.worldBound;
                                if (!worldBounds.Equals(Rect.zero))
                                {
                                    currentResizeRect = new Rect(worldBounds.x, worldBounds.yMax - 10, worldBounds.width, 10);
                                }
                            }
                            else
                            {
                                assetListContainer.style.borderBottomWidth = 0;
                                if (currentResizableContainer == assetListContainer)
                                {
                                    currentResizableContainer = null;
                                    currentResizeRect = Rect.zero;
                                }
                            }
                        }
                    });
                    
                    assetListContainer.RegisterCallback<MouseLeaveEvent>(evt =>
                    {
                        if (!isResizingInspector)
                        {
                            assetListContainer.style.borderBottomWidth = 0;
                        }
                    });
                    
                    // Allow resizing from the bottom edge of the container
                    assetListContainer.RegisterCallback<MouseDownEvent>(evt =>
                    {
                        if (evt.button == 0)
                        {
                            var localPos = evt.localMousePosition;
                            float containerHeight = assetListContainer.resolvedStyle.height;
                            if (containerHeight <= 0)
                            {
                                containerHeight = assetListContainer.layout.height;
                            }
                            // Check if click is near bottom edge (within 10 pixels)
                            if (localPos.y >= containerHeight - 10 && localPos.y <= containerHeight + 5)
                            {
                                isResizingInspector = true;
                                resizeStartY = evt.mousePosition.y;
                                float currentHeight = assetListContainer.resolvedStyle.height;
                                if (currentHeight <= 0)
                                {
                                    currentHeight = assetListContainer.layout.height;
                                }
                                resizeStartHeight = currentHeight;
                                assetListContainer.CaptureMouse();
                                evt.StopPropagation();
                            }
                        }
                    });
                    
                    // Handle mouse move during resize
                    assetListContainer.RegisterCallback<MouseMoveEvent>(evt =>
                    {
                        if (isResizingInspector && assetListContainer.HasMouseCapture())
                        {
                            updateInspectorResizeHeight(evt.mousePosition);
                            evt.StopPropagation();
                        }
                    });
                    
                    // Also register on root for better tracking when mouse leaves the container
                    if (rootVisualElement != null)
                    {
                        rootVisualElement.RegisterCallback<MouseMoveEvent>(evt =>
                        {
                            if (isResizingInspector && assetListContainer.HasMouseCapture())
                            {
                                updateInspectorResizeHeight(evt.mousePosition);
                                evt.StopPropagation();
                            }
                        }, TrickleDown.TrickleDown);
                    }
                    
                    assetListContainer.RegisterCallback<MouseUpEvent>(evt =>
                    {
                        if (evt.button == 0 && isResizingInspector)
                        {
                            isResizingInspector = false;
                            assetListContainer.ReleaseMouse();
                            assetListContainer.style.borderBottomWidth = 0;
                            
                            // Save the new height to the profile
                            if (profile != null)
                            {
                                float finalHeight = assetListContainer.resolvedStyle.height;
                                if (finalHeight <= 0)
                                {
                                    finalHeight = assetListContainer.layout.height;
                                }
                                if (finalHeight > 0)
                                {
                                    Undo.RecordObject(profile, "Resize Inspector");
                                    profile.InspectorHeight = finalHeight;
                                    EditorUtility.SetDirty(profile);
                                }
                            }
                            
                            currentResizeRect = Rect.zero;
                            evt.StopPropagation();
                        }
                    });
                    
                    // Also handle mouse up on root to ensure we release mouse capture
                    if (rootVisualElement != null)
                    {
                        rootVisualElement.RegisterCallback<MouseUpEvent>(evt =>
                        {
                            if (evt.button == 0 && isResizingInspector)
                            {
                                isResizingInspector = false;
                                if (assetListContainer.HasMouseCapture())
                                {
                                    assetListContainer.ReleaseMouse();
                                }
                                assetListContainer.style.borderBottomWidth = 0;
                                
                                // Save the new height to the profile
                                if (profile != null)
                                {
                                    float finalHeight = assetListContainer.resolvedStyle.height;
                                    if (finalHeight <= 0)
                                    {
                                        finalHeight = assetListContainer.layout.height;
                                    }
                                    if (finalHeight > 0)
                                    {
                                        Undo.RecordObject(profile, "Resize Inspector");
                                        profile.InspectorHeight = finalHeight;
                                        EditorUtility.SetDirty(profile);
                                    }
                                }
                                
                                currentResizeRect = Rect.zero;
                            }
                        });
                    }
                    
                    RebuildAssetList(profile, assetListContainer);
                    
                    // Store reference to container for resize updates
                    assetListContainer.userData = "asset-list-container-ref";
                    
                    section.Add(assetListContainer);
                    
                    // After adding to section, ensure ScrollView max-height is overridden
                    EditorApplication.delayCall += () =>
                    {
                        var scrollView = assetListContainer.Q<ScrollView>(className: "yucp-inspector-list");
                        if (scrollView != null)
                        {
                            scrollView.style.maxHeight = new StyleLength(StyleKeyword.None);
                        }
                    };
                    
                    // Permanent ignore list
                    var ignoreLabel = new Label("Permanent Ignore List");
                    ignoreLabel.AddToClassList("yucp-label");
                    ignoreLabel.style.marginTop = 12;
                    ignoreLabel.style.marginBottom = 4;
                    section.Add(ignoreLabel);
                    
                    var ignoreHelpBox = new VisualElement();
                    ignoreHelpBox.AddToClassList("yucp-help-box");
                    var ignoreHelpText = new Label("Folders in this list will be permanently ignored from all exports (like .gitignore).");
                    ignoreHelpText.AddToClassList("yucp-help-box-text");
                    ignoreHelpBox.Add(ignoreHelpText);
                    section.Add(ignoreHelpBox);
                    
                    if (profile.PermanentIgnoreFolders == null || profile.PermanentIgnoreFolders.Count == 0)
                    {
                        var noIgnoresLabel = new Label("No folders in ignore list.");
                        noIgnoresLabel.AddToClassList("yucp-label-secondary");
                        noIgnoresLabel.style.paddingTop = 8;
                        noIgnoresLabel.style.paddingBottom = 8;
                        section.Add(noIgnoresLabel);
                    }
                    else
                    {
                        foreach (var ignoreFolder in profile.PermanentIgnoreFolders.ToList())
                        {
                            var ignoreItem = new VisualElement();
                            ignoreItem.AddToClassList("yucp-folder-item");
                            
                            var ignorePathLabel = new Label(ignoreFolder);
                            ignorePathLabel.AddToClassList("yucp-folder-item-path");
                            ignoreItem.Add(ignorePathLabel);
                            
                            var removeIgnoreButton = new Button(() => RemoveFromIgnoreList(profile, ignoreFolder)) { text = "×" };
                            removeIgnoreButton.AddToClassList("yucp-button");
                            removeIgnoreButton.AddToClassList("yucp-folder-item-remove");
                            removeIgnoreButton.tooltip = "Remove from ignore list";
                            ignoreItem.Add(removeIgnoreButton);
                            
                            section.Add(ignoreItem);
                        }
                    }
                    
                    var addIgnoreButton = new Button(() => 
                    {
                        string selectedFolder = EditorUtility.OpenFolderPanel("Select Folder to Ignore", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(selectedFolder))
                        {
                            // Use full absolute path instead of relative path
                            AddFolderToIgnoreList(profile, selectedFolder);
                        }
                    }) { text = "+ Add Folder to Ignore List" };
                    addIgnoreButton.AddToClassList("yucp-button");
                    addIgnoreButton.style.marginTop = 8;
                    section.Add(addIgnoreButton);
                }
            }
            
            return section;
        }

    }
}
