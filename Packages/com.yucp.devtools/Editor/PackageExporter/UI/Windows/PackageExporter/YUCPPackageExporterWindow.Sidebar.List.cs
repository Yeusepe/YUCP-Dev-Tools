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
        private void UpdateProfileList()
        {
            // Update header buttons to reflect current state
            UpdateHeaderButtons();
            
            // Refresh options in case tags changed
            RefreshSearchOptions(_mainSearchField);
            RefreshSearchOptions(_overlaySearchField);
            
            // Update both normal and overlay sidebars using local search text
            if (_sidebar != null)
            {
                // Custom rendering for folders - use local _currentSearchText for reliable filtering
                RenderProfileListWithFolders(_sidebar.ListContainer, _currentSearchText);
            }
            
            if (_sidebarOverlay != null)
            {
                RenderProfileListWithFolders(_sidebarOverlay.ListContainer, _currentSearchText);
            }
        }

        private void UpdateHeaderButtons()
        {
            // Main and overlay sidebars each have one
            rootVisualElement.Query<Button>("profile-sort-button").ForEach(UpdateSortButton);
        }

        private void RefreshSearchOptions(YUCP.DevTools.Editor.PackageExporter.UI.Components.TokenizedSearchField field)
        {
            if (field == null) return;

            var options = new List<YUCP.DevTools.Editor.PackageExporter.UI.Components.TokenizedSearchField.SearchProposal>();
            var seenTags = new HashSet<string>();
            
            // 1. Preset tags
            var presetTagsType = typeof(YUCP.DevTools.Editor.PackageExporter.ExportProfile);
            var presetTagsField = presetTagsType.GetField("AvailablePresetTags", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var presetTagsList = presetTagsField?.GetValue(null) as List<string>;
            if (presetTagsList != null)
            {
                foreach(var t in presetTagsList) 
                {
                    if(seenTags.Add(t))
                        options.Add(new YUCP.DevTools.Editor.PackageExporter.UI.Components.TokenizedSearchField.SearchProposal { Label = t, Value = t, IsTag = true });
                }
            }
            
            // 2. Global Custom Tags (EditorPrefs)
            string rawGlobalTags = EditorPrefs.GetString("YUCP_GlobalCustomTags", "");
            if (!string.IsNullOrEmpty(rawGlobalTags))
            {
                var splits = rawGlobalTags.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach(var t in splits)
                {
                     if(seenTags.Add(t))
                        options.Add(new YUCP.DevTools.Editor.PackageExporter.UI.Components.TokenizedSearchField.SearchProposal { Label = t, Value = t, IsTag = true });
                }
            }
            
            // 3. Custom tags from Profiles
            foreach (var profile in allProfiles)
            {
                if (profile.customTags != null)
                {
                    foreach (var tag in profile.customTags)
                    {
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            var tr = tag.Trim();
                            if(seenTags.Add(tr))
                                options.Add(new YUCP.DevTools.Editor.PackageExporter.UI.Components.TokenizedSearchField.SearchProposal { Label = tr, Value = tr, IsTag = true });
                        }
                    }
                }
                
                // 4. Profile Names (as non-tags)
                if (!string.IsNullOrWhiteSpace(profile.profileName))
                {
                    options.Add(new YUCP.DevTools.Editor.PackageExporter.UI.Components.TokenizedSearchField.SearchProposal { Label = profile.profileName, Value = profile.profileName, IsTag = false });
                }
            }
            
            // 5. Folders as tags
             if (projectFolders != null)
            {
                foreach(var f in projectFolders) 
                {
                    string label = $"Folder: {f}";
                    if(seenTags.Add(label))
                        options.Add(new YUCP.DevTools.Editor.PackageExporter.UI.Components.TokenizedSearchField.SearchProposal { Label = label, Value = label, IsTag = true });
                }
            }
            
            field.SetAvailableOptions(options);
            field.RefreshChips(); // picks up tag recolors
        }

        /// <summary>
        /// allProfiles indices in the order they are on screen (collapsed folders excluded).
        /// Keyboard navigation and range selection walk this, not allProfiles.
        /// </summary>
        private List<int> _displayedProfileIndices = new List<int>();

        /// <summary>Search text, tag and folder filters. "-text" and "-Tag" exclude instead of include.</summary>
        private bool ProfileMatchesFilters(ExportProfile profile)
        {
            string text = (_currentSearchText ?? "").Trim();
            bool excludeText = text.StartsWith("-");
            if (excludeText) text = text.Substring(1);
            if (text.Length > 0 &&
                (GetProfileDisplayName(profile).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) == excludeText)
                return false;

            var profileTags = profile.GetAllTags();
            var includeTags = selectedFilterTags.Where(t => !t.StartsWith("-")).ToList();
            if (includeTags.Count > 0 && !includeTags.Any(profileTags.Contains))
                return false;
            if (selectedFilterTags.Any(t => t.StartsWith("-") && profileTags.Contains(t.Substring(1))))
                return false;

            return string.IsNullOrEmpty(selectedFilterFolder) || profile.folderName == selectedFilterFolder;
        }

        private bool IsInProjectFolder(ExportProfile profile)
        {
            return !string.IsNullOrEmpty(profile.folderName) && projectFolders.Contains(profile.folderName);
        }

        private static string GetProfileGuid(ExportProfile profile)
        {
            return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
        }

        private void RenderProfileListWithFolders(VisualElement container, string searchText)
        {
            if (container == null) return;
            container.Clear();

            bool hasSearchFilter = !string.IsNullOrWhiteSpace(searchText);
            bool hasAnyFilter = hasSearchFilter || selectedFilterTags.Count > 0 || !string.IsNullOrEmpty(selectedFilterFolder);
            var displayed = new List<int>();

            void AddProfileItem(VisualElement parent, ExportProfile profile)
            {
                int index = allProfiles.IndexOf(profile);
                parent.Add(CreateProfileItem(profile, index));
                displayed.Add(index);
            }

            // Ensure unified order is up to date
            ValidateAndCleanUnifiedOrder();

            // Apply filters and sorting to profiles
            var filteredProfiles = SortProfiles(allProfiles.Where(ProfileMatchesFilters).ToList(), currentSortOption);

            // Any sort other than Custom renders a flat list, since unifiedOrder's manual
            // positioning (and folders) would conflict with it.
            if (currentSortOption != ProfileSortOption.Custom)
            {
                foreach (var profile in filteredProfiles)
                {
                    AddProfileItem(container, profile);

                    var gap = new VisualElement();
                    gap.AddToClassList("yucp-list-gap");
                    container.Add(gap);
                }
            }
            else
            {
                // DEFAULT: Render Unified Order (Folders + Manual Sort)
                var profileGuidToProfile = filteredProfiles.ToDictionary(GetProfileGuid, p => p);
                var folderToProfiles = filteredProfiles
                    .Where(IsInProjectFolder)
                    .GroupBy(p => p.folderName)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var orderItem in unifiedOrder)
                {
                    if (orderItem.isFolder)
                    {
                        string folderName = orderItem.identifier;
                        if (!projectFolders.Contains(folderName))
                            continue;

                        var folderProfiles = folderToProfiles.TryGetValue(folderName, out var profiles)
                            ? profiles
                            : new List<ExportProfile>();

                        // Empty folders stay visible (so new folders show up), unless a filter hides their contents
                        if (folderProfiles.Count == 0 && hasAnyFilter)
                            continue;

                        container.Add(CreateFolderHeader(folderName, folderProfiles.Count));

                        // Render profiles in folder if not collapsed (or if searching)
                        if (!collapsedFolders.Contains(folderName) || hasSearchFilter)
                        {
                            var folderContent = new VisualElement();
                            folderContent.AddToClassList("yucp-folder-content");
                            foreach (var profile in folderProfiles)
                            {
                                AddProfileItem(folderContent, profile);
                            }
                            container.Add(folderContent);
                        }
                    }
                    else if (profileGuidToProfile.TryGetValue(orderItem.identifier, out var profile) && !IsInProjectFolder(profile))
                    {
                        AddProfileItem(container, profile);
                    }
                }
            }

            _displayedProfileIndices = displayed;

            // Check if we have any items to show
            if (container.childCount == 0)
            {
                string emptyMessage = "No profiles found";
                if (hasAnyFilter)
                    emptyMessage = "No profiles match your filters";

                var emptyLabel = new Label(emptyMessage);
                emptyLabel.AddToClassList("yucp-label-secondary");
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyLabel.style.paddingTop = 20;
                container.Add(emptyLabel);
            }
        }

        private List<ExportProfile> SortProfiles(List<ExportProfile> profiles, ProfileSortOption sortOption)
        {
            switch (sortOption)
            {
                case ProfileSortOption.Custom:
                    return profiles;
                    
                case ProfileSortOption.Name:
                    return profiles
                        .OrderBy(p => GetProfileDisplayNameKey(p))
                        .ThenByDescending(p => GetExportDateValue(p))
                        .ThenByDescending(p => GetVersionSortValue(p))
                        .ThenBy(p => p.name)
                        .ToList();
                    
                case ProfileSortOption.ExportDate:
                    return profiles
                        .OrderByDescending(p => GetExportDateValue(p))
                        .ThenBy(p => GetProfileDisplayNameKey(p))
                        .ThenBy(p => p.name)
                        .ToList();
                    
                case ProfileSortOption.Version:
                    return profiles
                        .OrderByDescending(p => GetVersionSortValue(p))
                        .ThenBy(p => GetProfileDisplayNameKey(p))
                        .ThenByDescending(p => GetExportDateValue(p))
                        .ThenBy(p => p.name)
                        .ToList();
                    
                case ProfileSortOption.ExportCount:
                    return profiles
                        .OrderByDescending(p => p.ExportCount)
                        .ThenByDescending(p => GetExportDateValue(p))
                        .ThenBy(p => GetProfileDisplayNameKey(p))
                        .ThenBy(p => p.name)
                        .ToList();
                    
                default:
                    return profiles;
            }
        }

        private string GetProfileDisplayNameKey(ExportProfile profile)
        {
            return (GetProfileDisplayName(profile) ?? string.Empty).ToLowerInvariant();
        }

        private DateTime GetExportDateValue(ExportProfile profile)
        {
            if (string.IsNullOrEmpty(profile.LastExportTime))
                return DateTime.MinValue;
            if (DateTime.TryParse(profile.LastExportTime, out var date))
                return date;
            return DateTime.MinValue;
        }

        private long GetVersionSortValue(ExportProfile profile)
        {
            // Try to parse version for proper semver sorting
            if (VersionUtility.TryParseVersion(profile.version, out int major, out int minor, out int patch, out int? build, out string prerelease, out string metadata))
            {
                // Create a comparable value: major * 1000000 + minor * 1000 + patch + (build ?? 0)
                long versionValue = (long)major * 1000000L + (long)minor * 1000L + (long)patch;
                if (build.HasValue)
                    versionValue += build.Value;
                return versionValue;
            }
            return 0L;
        }

    }
}
