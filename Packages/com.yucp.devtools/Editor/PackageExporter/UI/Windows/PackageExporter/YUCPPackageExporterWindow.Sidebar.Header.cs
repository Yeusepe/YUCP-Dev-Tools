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

            var existingHeader = sidebarContainer.Q<VisualElement>(className: "yucp-sidebar-header");
            if (existingHeader == null)
            {

                var searchField = sidebarContainer.Q<TextField>();
                if (searchField != null)
                {
                    existingHeader = searchField.parent;
                }
            }


            var customHeader = CreateProfileListHeader(sidebar, isOverlay);

            if (existingHeader != null)
            {

                var parent = existingHeader.parent;
                if (parent != null)
                {
                    int index = parent.IndexOf(existingHeader);
                    existingHeader.RemoveFromHierarchy();
                    parent.Insert(index, customHeader);
                }
                else
                {

                    sidebarContainer.Insert(0, customHeader);
                }
            }
            else
            {

                sidebarContainer.Insert(0, customHeader);
            }

            // The sidebar's title becomes the list's section header, leading the row that holds the list actions
            var title = sidebarContainer.Q<Label>(className: "yucp-profile-list-header");
            if (title != null)
            {
                customHeader.Q(className: "yucp-sidebar-section-row")?.Insert(0, title);
            }
        }

        /// <summary>
        /// Sidebar header, laid out per Apple's HIG: the search field comes first because it filters the
        /// sidebar, then the list's section header with its actions as borderless symbol buttons on the
        /// trailing edge (symbols, not text; a pull-down menu for sort).
        /// </summary>
        private VisualElement CreateProfileListHeader(YucpSidebar sidebar, bool isOverlay)
        {
            var header = new VisualElement();
            header.name = "profile-header-row";
            header.AddToClassList("yucp-sidebar-toolbar");

            var searchField = new TokenizedSearchField();
            searchField.name = "global-search-field";
            searchField.Placeholder = "Search profiles and tags";
            if (isOverlay) _overlaySearchField = searchField;
            else _mainSearchField = searchField;

            RefreshSearchOptions(searchField);


            string currentSearch = sidebar != null ? sidebar.GetSearchText() : "";
            searchField.value = currentSearch;


            var currentTags = new List<string>(selectedFilterTags);
            if (!string.IsNullOrEmpty(selectedFilterFolder))
            {
                currentTags.Add($"Folder: {selectedFilterFolder}");
            }
            searchField.SetActiveTags(currentTags);


            searchField.OnSearchValueChanged += (newValue) =>
            {
                _currentSearchText = newValue ?? "";
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

            header.Add(searchField);


            searchField.AttachOverlayToRoot(rootVisualElement);


            var sectionRow = new VisualElement();
            sectionRow.AddToClassList("yucp-sidebar-section-row");
            header.Add(sectionRow);

            var actions = new VisualElement();
            actions.AddToClassList("yucp-sidebar-section-actions");
            sectionRow.Add(actions);

            // Sort: a pull-down button (symbol + chevron) whose menu checks the current order
            Button sortButton = null;
            sortButton = CreateSymbolButton("swap_vert", () => ShowSortMenu(sortButton));
            sortButton.name = "profile-sort-button";
            var sortChevron = CreateSymbolImage("expand_more");
            sortChevron.AddToClassList("yucp-symbol-chevron");
            sortButton.Add(sortChevron);
            UpdateSortButton(sortButton);
            actions.Add(sortButton);

            var newFolderButton = CreateSymbolButton("create_new_folder", () => CreateFolder(Array.Empty<ExportProfile>()));
            newFolderButton.tooltip = $"New Folder ({ModifierKeyName}+Shift+N)";
            actions.Add(newFolderButton);

            return header;
        }

#if UNITY_EDITOR_OSX
        private const string ModifierKeyName = "Cmd";
#else
        private const string ModifierKeyName = "Ctrl";
#endif

        private const string MaterialSymbolsPath = "Packages/com.yucp.devtools/Editor/PackageExporter/UI/Resources/MaterialSymbols/";

        /// <summary>A Material Symbols glyph (Outlined, 20px optical size). The PNGs are white so USS can tint them.</summary>
        private static Image CreateSymbolImage(string symbol)
        {
            var image = new Image { image = AssetDatabase.LoadAssetAtPath<Texture2D>(MaterialSymbolsPath + symbol + ".png") };
            image.AddToClassList("yucp-symbol");
            image.pickingMode = PickingMode.Ignore;
            return image;
        }

        /// <summary>Borderless symbol-only button, like a macOS toolbar item.</summary>
        private static Button CreateSymbolButton(string symbol, Action onClick)
        {
            var button = new Button(onClick);
            button.AddToClassList("yucp-symbol-button");
            button.Add(CreateSymbolImage(symbol));
            return button;
        }

        private void UpdateSortButton(Button sortButton)
        {
            sortButton.tooltip = $"Sort by {GetSortLabel(currentSortOption)}";
            // A non-default order tints the symbol with the accent color, like a selected toolbar item
            sortButton.EnableInClassList("active", currentSortOption != ProfileSortOption.Custom);
        }

        private string GetSortLabel(ProfileSortOption option)
        {
            return option switch
            {
                ProfileSortOption.Custom => "Custom",
                ProfileSortOption.Name => "Name",
                ProfileSortOption.ExportDate => "Export Date",
                ProfileSortOption.Version => "Version",
                ProfileSortOption.ExportCount => "Export Count",
                _ => "Sort"
            };
        }

        /// <summary>Adds the sort orders to a menu, checking the current one. Used by the sort button and the window menu.</summary>
        private void AddSortMenuItems(GenericMenu menu, string prefix)
        {
            foreach (ProfileSortOption option in Enum.GetValues(typeof(ProfileSortOption)))
            {
                var selected = option;
                menu.AddItem(new GUIContent(prefix + GetSortLabel(option)), currentSortOption == option, () =>
                {
                    currentSortOption = selected;
                    UpdateProfileList();
                });
            }
        }

        private void ShowSortMenu(VisualElement anchor)
        {
            var menu = new GenericMenu();
            AddSortMenuItems(menu, "");
            menu.DropDown(anchor.worldBound);
        }
    }
}
