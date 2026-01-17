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
        private VisualElement CreateBulkFoldersSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Export Folders");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpText = new Label("Add or remove folders to/from all selected profiles");
            helpText.AddToClassList("yucp-label-secondary");
            helpText.style.marginBottom = 8;
            section.Add(helpText);
            
            // Get unique folders across all profiles
            var allFolders = selectedProfiles
                .SelectMany(p => p.foldersToExport ?? new List<string>())
                .Distinct()
                .OrderBy(f => f)
                .ToList();
            
            var folderList = new VisualElement();
            folderList.AddToClassList("yucp-folder-list");
            folderList.style.maxHeight = 200;
            folderList.style.overflow = Overflow.Hidden;
            
            var scrollView = new ScrollView();
            
            foreach (var folder in allFolders)
            {
                var folderItem = new VisualElement();
                folderItem.AddToClassList("yucp-folder-item");
                
                // Check if all profiles have this folder
                bool allHaveFolder = selectedProfiles.All(p => p.foldersToExport.Contains(folder));
                bool someHaveFolder = selectedProfiles.Any(p => p.foldersToExport.Contains(folder));
                
                var checkbox = new Toggle();
                checkbox.value = allHaveFolder;
                checkbox.AddToClassList("yucp-toggle");
                checkbox.RegisterValueChangedCallback(evt =>
                {
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Change Folder");
                        if (evt.newValue)
                        {
                            if (!profile.foldersToExport.Contains(folder))
                            {
                                profile.foldersToExport.Add(folder);
                            }
                        }
                        else
                        {
                            profile.foldersToExport.Remove(folder);
                        }
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                });
                folderItem.Add(checkbox);
                
                var pathLabel = new Label(folder);
                pathLabel.AddToClassList("yucp-folder-item-path");
                if (!allHaveFolder && someHaveFolder)
                {
                    pathLabel.style.opacity = 0.7f;
                    var mixedLabel = new Label(" (Mixed)");
                    mixedLabel.AddToClassList("yucp-label-secondary");
                    mixedLabel.style.marginLeft = 4;
                    folderItem.Add(mixedLabel);
                }
                folderItem.Add(pathLabel);
                
                scrollView.Add(folderItem);
            }
            
            folderList.Add(scrollView);
            section.Add(folderList);
            
            // Add folder button
            var addButton = new Button(() => 
            {
                string folderPath = EditorUtility.OpenFolderPanel("Select Folder to Add", Application.dataPath, "");
                if (!string.IsNullOrEmpty(folderPath))
                {
                    // Convert to relative path
                    string relativePath = GetRelativePath(folderPath);
                    if (string.IsNullOrEmpty(relativePath))
                    {
                        EditorUtility.DisplayDialog("Invalid Path", "Please select a folder within the Unity project.", "OK");
                        return;
                    }
                    
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Add Folder");
                        if (!profile.foldersToExport.Contains(relativePath))
                        {
                            profile.foldersToExport.Add(relativePath);
                        }
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                }
            }) { text = "+ Add Folder to All" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            return section;
        }

        private VisualElement CreateBulkExclusionFiltersSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Exclusion Filters");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpText = new Label("Add or remove exclusion patterns across all selected profiles");
            helpText.AddToClassList("yucp-label-secondary");
            helpText.style.marginBottom = 8;
            section.Add(helpText);
            
            // File Patterns
            var filePatternsLabel = new Label("File Patterns");
            filePatternsLabel.AddToClassList("yucp-label");
            filePatternsLabel.style.marginTop = 8;
            filePatternsLabel.style.marginBottom = 4;
            filePatternsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.Add(filePatternsLabel);
            
            var allFilePatterns = selectedProfiles
                .SelectMany(p => p.excludeFilePatterns ?? new List<string>())
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            
            var filePatternsContainer = new VisualElement();
            filePatternsContainer.style.maxHeight = 150;
            var fileScrollView = new ScrollView();
            
            foreach (var pattern in allFilePatterns)
            {
                bool allHavePattern = selectedProfiles.All(p => p.excludeFilePatterns.Contains(pattern));
                bool someHavePattern = selectedProfiles.Any(p => p.excludeFilePatterns.Contains(pattern));
                
                var patternItem = CreateBulkStringListItem(pattern, allHavePattern, someHavePattern,
                    (profile, value) =>
                    {
                        if (value)
                        {
                            if (!profile.excludeFilePatterns.Contains(pattern))
                                profile.excludeFilePatterns.Add(pattern);
                        }
                        else
                        {
                            profile.excludeFilePatterns.Remove(pattern);
                        }
                    });
                fileScrollView.Add(patternItem);
            }
            
            filePatternsContainer.Add(fileScrollView);
            section.Add(filePatternsContainer);
            
            var addFilePatternButton = new Button(() =>
            {
                // Add a default pattern - users can edit it after adding
                string pattern = "*.tmp";
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Add File Pattern");
                    if (!profile.excludeFilePatterns.Contains(pattern))
                    {
                        profile.excludeFilePatterns.Add(pattern);
                    }
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            }) { text = "+ Add Pattern to All" };
            addFilePatternButton.AddToClassList("yucp-button");
            addFilePatternButton.style.marginBottom = 12;
            section.Add(addFilePatternButton);
            
            // Folder Names
            var folderNamesLabel = new Label("Folder Names");
            folderNamesLabel.AddToClassList("yucp-label");
            folderNamesLabel.style.marginTop = 8;
            folderNamesLabel.style.marginBottom = 4;
            folderNamesLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.Add(folderNamesLabel);
            
            var allFolderNames = selectedProfiles
                .SelectMany(p => p.excludeFolderNames ?? new List<string>())
                .Distinct()
                .OrderBy(f => f)
                .ToList();
            
            var folderNamesContainer = new VisualElement();
            folderNamesContainer.style.maxHeight = 150;
            var folderScrollView = new ScrollView();
            
            foreach (var folderName in allFolderNames)
            {
                bool allHaveFolder = selectedProfiles.All(p => p.excludeFolderNames.Contains(folderName));
                bool someHaveFolder = selectedProfiles.Any(p => p.excludeFolderNames.Contains(folderName));
                
                var folderItem = CreateBulkStringListItem(folderName, allHaveFolder, someHaveFolder,
                    (profile, value) =>
                    {
                        if (value)
                        {
                            if (!profile.excludeFolderNames.Contains(folderName))
                                profile.excludeFolderNames.Add(folderName);
                        }
                        else
                        {
                            profile.excludeFolderNames.Remove(folderName);
                        }
                    });
                folderScrollView.Add(folderItem);
            }
            
            folderNamesContainer.Add(folderScrollView);
            section.Add(folderNamesContainer);
            
            var addFolderNameButton = new Button(() =>
            {
                // Add a default folder name - users can edit it after adding
                string folderName = ".git";
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Add Folder Name");
                    if (!profile.excludeFolderNames.Contains(folderName))
                    {
                        profile.excludeFolderNames.Add(folderName);
                    }
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            }) { text = "+ Add Folder Name to All" };
            addFolderNameButton.AddToClassList("yucp-button");
            section.Add(addFolderNameButton);
            
            return section;
        }

        private VisualElement CreateBulkStringListItem(string value, bool allHave, bool someHave, 
            System.Action<ExportProfile, bool> toggleAction)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-folder-item");
            
            var checkbox = new Toggle();
            checkbox.value = allHave;
            checkbox.AddToClassList("yucp-toggle");
            checkbox.RegisterValueChangedCallback(evt =>
            {
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Change Exclusion");
                    toggleAction(profile, evt.newValue);
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            });
            item.Add(checkbox);
            
            var textField = new TextField { value = value };
            textField.AddToClassList("yucp-input");
            textField.style.flexGrow = 1;
            textField.isReadOnly = true;
            if (!allHave && someHave)
            {
                textField.style.opacity = 0.7f;
                var mixedLabel = new Label(" (Mixed)");
                mixedLabel.AddToClassList("yucp-label-secondary");
                mixedLabel.style.marginLeft = 4;
                item.Add(mixedLabel);
            }
            item.Add(textField);
            
            var removeButton = new Button(() =>
            {
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Remove Exclusion");
                    toggleAction(profile, false);
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            }) { text = "×" };
            removeButton.AddToClassList("yucp-button");
            removeButton.AddToClassList("yucp-folder-item-remove");
            item.Add(removeButton);
            
            return item;
        }

    }
}
