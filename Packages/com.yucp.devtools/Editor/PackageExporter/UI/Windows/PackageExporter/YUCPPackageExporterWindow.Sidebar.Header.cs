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
        private void ReplaceSidebarHeader(VisualElement sidebarContainer, YucpSidebar sidebar, bool isOverlay)
        {
            // Find the existing header/search area - try common class names
            var existingHeader = sidebarContainer.Q<VisualElement>(className: "yucp-sidebar-header");
            if (existingHeader == null)
            {
                // Try finding by structure - look for search field parent
                var searchField = sidebarContainer.Q<TextField>();
                if (searchField != null)
                {
                    existingHeader = searchField.parent;
                }
            }
            
            // Create new custom header
            var customHeader = CreateProfileListHeader(sidebar, isOverlay);
            
            if (existingHeader != null)
            {
                // Replace existing header
                var parent = existingHeader.parent;
                if (parent != null)
                {
                    int index = parent.IndexOf(existingHeader);
                    existingHeader.RemoveFromHierarchy();
                    parent.Insert(index, customHeader);
                }
                else
                {
                    // Fallback: add at top
                    sidebarContainer.Insert(0, customHeader);
                }
            }
            else
            {
                // No existing header found, add at top
                sidebarContainer.Insert(0, customHeader);
            }
        }

        private VisualElement CreateProfileListHeader(YucpSidebar sidebar, bool isOverlay)
        {
            var headerRow = new VisualElement();
            headerRow.AddToClassList("yucp-toolbar");
            headerRow.style.backgroundColor = new StyleColor(Color.clear); // Remove grey background
            // headerRow.style.zIndex = 100; // Removed: Not supported in this Unity version
            headerRow.style.overflow = Overflow.Visible; // Allow dropdown to overflow
            
            // Ensure toolbar allows overlay to flow out if needed. 
            // Note: UI Toolkit 'absolute' is relative to parent. 
            // If z-index issues occur, might need to append overlay to root, but let's try this first.
            
            // Left group: Sort button only (Filter button removed)
            var leftGroup = new VisualElement();
            leftGroup.AddToClassList("yucp-toolbar-left");


            
            // Sort button with icon
            string sortLabel = GetSortLabel(currentSortOption);
            Button sortButton = null;
            sortButton = new Button(() => ShowSortPopover(sortButton, isOverlay));
            sortButton.AddToClassList("yucp-btn-outline");
            if (currentSortOption != ProfileSortOption.Custom)
                sortButton.AddToClassList("active");
            
            // Add icon (using text as icon placeholder - can be replaced with actual icon texture)
            var sortIcon = new Label("⇅");
            sortIcon.AddToClassList("yucp-btn-icon");
            sortButton.Add(sortIcon);
            
            var sortLabelElement = new Label(sortLabel);
            sortLabelElement.AddToClassList("yucp-sort-label"); // Added class for easier finding
            sortButton.Add(sortLabelElement);
            leftGroup.Add(sortButton);
            
            headerRow.Add(leftGroup);
            
            // Right group: Search field (Tokenized)
            var rightGroup = new VisualElement();
            rightGroup.AddToClassList("yucp-toolbar-right");
            rightGroup.style.flexGrow = 1;
            // Ensure search container doesn't clip the dropdown
            rightGroup.style.overflow = Overflow.Visible;
            headerRow.style.overflow = Overflow.Visible;
            
            var searchField = new TokenizedSearchField();
            searchField.name = "global-search-field";
            if (isOverlay) _overlaySearchField = searchField;
            else _mainSearchField = searchField;
            
            RefreshSearchOptions(searchField);
            
            // Initialize with current search text
            string currentSearch = sidebar != null ? sidebar.GetSearchText() : "";
            searchField.value = currentSearch;
            
            // Initialize with current tags
            var currentTags = new List<string>(selectedFilterTags);
            if (!string.IsNullOrEmpty(selectedFilterFolder))
            {
                currentTags.Add($"Folder: {selectedFilterFolder}");
            }
            searchField.SetActiveTags(currentTags);
            
            // Hook up events
            searchField.OnSearchValueChanged += (newValue) => 
            {
                _currentSearchText = newValue ?? ""; // Store locally for reliable access
                var setSearchMethod = sidebar?.GetType().GetMethod("SetSearchText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (setSearchMethod != null)
                {
                    setSearchMethod.Invoke(sidebar, new object[] { newValue });
                }
                UpdateProfileList();
            };
            
            searchField.OnTagAdded += (tag) => 
            {
                if (tag.StartsWith("Folder: "))
                {
                    string folderName = tag.Substring("Folder: ".Length);
                    selectedFilterFolder = folderName;
                }
                else
                {
                    if (!selectedFilterTags.Contains(tag))
                        selectedFilterTags.Add(tag);
                }
                UpdateProfileList();
            };
            
            // Connect Color Provider
            searchField.TagColorProvider = (tag) => GetTagColor(tag);
            
            searchField.OnTagRemoved += (tag) => 
            {
                if (tag.StartsWith("Folder: "))
                {
                    selectedFilterFolder = null;
                }
                else
                {
                    selectedFilterTags.Remove(tag);
                }
                UpdateProfileList();
            };
            
            rightGroup.Add(searchField);
            
            // Attach overlay to root to fix layering (dropdown appearing behind items)
            searchField.AttachOverlayToRoot(rootVisualElement);
            
            headerRow.Add(rightGroup);
            
            headerRow.name = "profile-header-row";
            
            // Responsive layout: Collapse sort button text on small widths
            headerRow.RegisterCallback<GeometryChangedEvent>(evt => 
            {
                if (sortLabelElement == null) return;
                
                // Threshold width for collapsing (approximate width of sidebar when minimal)
                float collapseThreshold = 320f;
                bool shouldCollapse = evt.newRect.width < collapseThreshold;
                
                if (shouldCollapse)
                {
                    sortLabelElement.style.display = DisplayStyle.None;
                    // Ensure button didn't shrink too much
                    sortButton.tooltip = sortLabel; // Move label to tooltip
                }
                else
                {
                    sortLabelElement.style.display = DisplayStyle.Flex;
                    sortButton.tooltip = ""; 
                }
            });
            
            return headerRow;
        }

        private string GetSortLabel(ProfileSortOption option)
        {
            return option switch
            {
                ProfileSortOption.Custom => "Sort: Custom",
                ProfileSortOption.Name => "Sort: Name",
                ProfileSortOption.ExportDate => "Sort: Export Date",
                ProfileSortOption.Version => "Sort: Version",
                ProfileSortOption.ExportCount => "Sort: Export Count",
                _ => "Sort"
            };
        }

        private void ShowSortPopover(Button anchorButton, bool isOverlay)
        {
            var popoverContent = CreateSortPopoverContent(isOverlay);
            
            // Get button position for popover
            var buttonRect = anchorButton.worldBound;
            var popoverRect = new Rect(buttonRect.x, buttonRect.yMax + 4, 300, 200);
            
            ShowPopover(popoverRect, popoverContent);
        }

        private VisualElement CreateSortPopoverContent(bool isOverlay)
        {
            // The container here was causing "double container" issues because ShowPopover adds "yucp-popover" class too.
            // We'll keep the container for layout separation but remove the visual class.
            var container = new VisualElement();
            // container.AddToClassList("yucp-popover"); // Removed to avoid double border
            
            // Allow content to size itself or fit within parent
            container.style.overflow = Overflow.Hidden;
            container.style.minWidth = 240; 
            container.style.maxWidth = 260;
            
            var content = new VisualElement();
            content.AddToClassList("yucp-popover-content");
            content.style.overflow = Overflow.Hidden;
            content.style.minWidth = 0;
            
            // Section Title
            var sectionTitle = new Label("SORT BY");
            sectionTitle.AddToClassList("yucp-popover-section-title");
            content.Add(sectionTitle);
            
            // Sort options container
            var optionsContainer = new VisualElement();
            optionsContainer.AddToClassList("yucp-popover-section");
            
            var customOption = CreateSortOption("Custom (Default)", ProfileSortOption.Custom, currentSortOption == ProfileSortOption.Custom, () =>
            {
                currentSortOption = ProfileSortOption.Custom;
                UpdateProfileList();
                ClosePopover();
            });
            optionsContainer.Add(customOption);
            
            var nameOption = CreateSortOption("Name", ProfileSortOption.Name, currentSortOption == ProfileSortOption.Name, () =>
            {
                currentSortOption = ProfileSortOption.Name;
                UpdateProfileList();
                ClosePopover();
            });
            optionsContainer.Add(nameOption);
            
            var dateOption = CreateSortOption("Export Date", ProfileSortOption.ExportDate, currentSortOption == ProfileSortOption.ExportDate, () =>
            {
                currentSortOption = ProfileSortOption.ExportDate;
                UpdateProfileList();
                ClosePopover();
            });
            optionsContainer.Add(dateOption);
            
            var versionOption = CreateSortOption("Version", ProfileSortOption.Version, currentSortOption == ProfileSortOption.Version, () =>
            {
                currentSortOption = ProfileSortOption.Version;
                UpdateProfileList();
                ClosePopover();
            });
            optionsContainer.Add(versionOption);
            
            var countOption = CreateSortOption("Export Count", ProfileSortOption.ExportCount, currentSortOption == ProfileSortOption.ExportCount, () =>
            {
                currentSortOption = ProfileSortOption.ExportCount;
                UpdateProfileList();
                ClosePopover();
            });
            optionsContainer.Add(countOption);
            
            content.Add(optionsContainer);
            
            // Close Action
            var actionsContainer = new VisualElement();
            actionsContainer.AddToClassList("yucp-popover-actions");
            
            var closeButton = new Button(ClosePopover) { text = "Close" };
            closeButton.AddToClassList("yucp-button-text");
            actionsContainer.Add(closeButton);
            
            content.Add(actionsContainer);
            
            container.Add(content);
            return container;
        }

        private VisualElement CreateSortOption(string label, ProfileSortOption option, bool isSelected, Action onClick)
        {
            var optionRow = new VisualElement();
            optionRow.AddToClassList("yucp-sort-option");
            if (isSelected)
                optionRow.AddToClassList("selected");
            
            // Label comes first now
            var labelElement = new Label(label);
            labelElement.AddToClassList("yucp-sort-option-label");
            optionRow.Add(labelElement);
            
            // Indicator (Right aligned via CSS)
            var indicator = new VisualElement();
            indicator.AddToClassList("yucp-sort-indicator");
            
            // Checkmark
            var check = new Label("✓");
            check.AddToClassList("yucp-sort-indicator-check");
            indicator.Add(check);
            
            optionRow.Add(indicator);
            
            optionRow.AddManipulator(new Clickable(() =>
            {
                onClick?.Invoke();
            }));
            
            return optionRow;
        }

        private VisualElement BuildCheckboxRow(string id, string label, bool initial, Action<bool> onChanged)
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-row-item");
            row.name = id;
            if (initial)
                row.AddToClassList("selected");
            
            var toggle = new Toggle { value = initial };
            toggle.AddToClassList("yucp-toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                    row.AddToClassList("selected");
                else
                    row.RemoveFromClassList("selected");
                onChanged?.Invoke(evt.newValue);
            });
            row.Add(toggle);
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("yucp-row-item-label");
            row.Add(labelElement);
            
            // Make entire row clickable
            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    toggle.value = !toggle.value;
                    evt.StopPropagation();
                }
            });
            
            return row;
        }

    }
}
