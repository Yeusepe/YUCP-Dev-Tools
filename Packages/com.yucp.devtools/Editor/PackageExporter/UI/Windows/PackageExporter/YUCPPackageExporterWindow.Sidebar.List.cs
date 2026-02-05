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
            // Find and update sort button
            var sortButton = rootVisualElement.Q<Button>(className: "yucp-btn-outline");
            if (sortButton != null)
            {
                var label = sortButton.Q<Label>(className: "yucp-sort-label");
                if (label != null)
                {
                    label.text = GetSortLabel(currentSortOption);
                }
                
                if (currentSortOption != ProfileSortOption.Custom)
                    sortButton.AddToClassList("active");
                else
                    sortButton.RemoveFromClassList("active");
            }
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
        }

        private void RenderProfileListWithFolders(VisualElement container, string searchText)
        {
            if (container == null) return;
            container.Clear();
            
            bool hasSearchFilter = !string.IsNullOrWhiteSpace(searchText);
            
            // Ensure unified order is up to date
            ValidateAndCleanUnifiedOrder();
            
            // Apply filters and sorting to profiles
            var filteredProfiles = allProfiles.AsEnumerable();
            
            // Apply search filter
            if (hasSearchFilter)
            {
                filteredProfiles = filteredProfiles.Where(p => 
                    GetProfileDisplayName(p).ToLowerInvariant().Contains(searchText.ToLowerInvariant()));
            }
            
            // Apply tag filter
            if (selectedFilterTags.Count > 0)
            {
                filteredProfiles = filteredProfiles.Where(p => 
                {
                    var profileTags = p.GetAllTags();
                    return selectedFilterTags.Any(tag => profileTags.Contains(tag));
                });
            }
            
            // Apply folder filter
            if (!string.IsNullOrEmpty(selectedFilterFolder))
            {
                filteredProfiles = filteredProfiles.Where(p => p.folderName == selectedFilterFolder);
            }
            
            // Apply sorting
            filteredProfiles = SortProfiles(filteredProfiles.ToList(), currentSortOption);
            
            // Rebuild folder structure with filtered and sorted profiles
            var profileGuidToProfile = filteredProfiles.ToDictionary(
                p => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p)),
                p => p
            );
            
            // If we are sorting by anything other than default Custom/Manual, 
            // OR if we have a search query, we should probably render a Flat List to respect the sort order/search results.
            // UnifiedOrder enforces manual positioning which conflicts with dynamic sorting.
            
            bool isCustomSort = currentSortOption != ProfileSortOption.Custom;
            
            if (isCustomSort)
            {
                // RENDER FLAT LIST (Respects Sort)
                // Just render all filtered profiles in order
                int index = 0;
                foreach (var profile in filteredProfiles)
                {
                    var item = CreateProfileItem(profile, index); 
                    if (item != null)
                    {
                        container.Add(item);
                        
                        // Add gap for drag and drop (though drag drop might be disabled in this mode)
                        var gap = new VisualElement();
                        gap.AddToClassList("yucp-list-gap");
                        container.Add(gap);
                    }
                    index++;
                }
                
                // If empty
                if (!filteredProfiles.Any())
                {
                   // Container empty, maybe show 'No results' but empty state is handled by parent if needed, 
                   // or we just leave it empty.
                }
                
                return; // Done
            }

            // DEFAULT: Render Unified Order (Folders + Manual Sort)
            
            var folderToProfiles = filteredProfiles
                .Where(p => !string.IsNullOrEmpty(p.folderName) && projectFolders.Contains(p.folderName))
                .GroupBy(p => p.folderName)
                .ToDictionary(g => g.Key, g => g.ToList());
            
            // Build filtered unified order
            var filteredUnifiedOrder = new List<UnifiedOrderItem>();
            foreach (var orderItem in unifiedOrder)
            {
                if (orderItem.isFolder)
                {
                    // Only include folder if it has filtered profiles
                    if (folderToProfiles.ContainsKey(orderItem.identifier) && 
                        folderToProfiles[orderItem.identifier].Count > 0)
                    {
                        filteredUnifiedOrder.Add(orderItem);
                    }
                }
                else
                {
                    // Only include profile if it's in the filtered list
                    if (profileGuidToProfile.ContainsKey(orderItem.identifier))
                    {
                        filteredUnifiedOrder.Add(orderItem);
                    }
                }
            }
            
            // Track what we've rendered to avoid duplicates
            var renderedProfiles = new HashSet<ExportProfile>();
            var renderedFolders = new HashSet<string>();
            
            // Render items in filtered unified order
            foreach (var orderItem in filteredUnifiedOrder)
            {
                if (orderItem.isFolder)
                {
                    // Render folder
                    string folderName = orderItem.identifier;
                    
                    if (!projectFolders.Contains(folderName))
                        continue; // Skip invalid folders
                    
                    // Get profiles in this folder (already filtered and sorted)
                    var folderProfiles = folderToProfiles.TryGetValue(folderName, out var profiles) 
                        ? profiles 
                        : new List<ExportProfile>();
                    
                    // Skip if no matching profiles
                    if (folderProfiles.Count == 0)
                        continue;
                    
                    // Create folder header
                    var folderHeader = CreateFolderHeader(folderName, folderProfiles.Count);
                    container.Add(folderHeader);
                    renderedFolders.Add(folderName);
                    
                    // Render profiles in folder if not collapsed (or if searching)
                    if (!collapsedFolders.Contains(folderName) || hasSearchFilter)
                    {
                        var folderContent = new VisualElement();
                        folderContent.AddToClassList("yucp-folder-content");
                        
                        foreach (var profile in folderProfiles)
                        {
                            var item = CreateProfileItem(profile, allProfiles.IndexOf(profile));
                            folderContent.Add(item);
                            renderedProfiles.Add(profile);
                        }
                        container.Add(folderContent);
                    }
                }
                else
                {
                    // Render profile (only if not in a folder that's already rendered)
                    if (profileGuidToProfile.TryGetValue(orderItem.identifier, out var profile))
                    {
                        // Skip if already rendered (in a folder)
                        if (renderedProfiles.Contains(profile))
                            continue;
                        
                        // Skip if in a folder (folders handle their own profiles)
                        if (!string.IsNullOrEmpty(profile.folderName) && projectFolders.Contains(profile.folderName))
                            continue;
                        
                        // Profile is already filtered, no need to check again
                        
                        var item = CreateProfileItem(profile, allProfiles.IndexOf(profile));
                        container.Add(item);
                        renderedProfiles.Add(profile);
                    }
                }
            }
            
            // Check if we have any items to show
            if (container.childCount == 0)
            {
                string emptyMessage = "No profiles found";
                if (hasSearchFilter || selectedFilterTags.Count > 0 || !string.IsNullOrEmpty(selectedFilterFolder))
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
