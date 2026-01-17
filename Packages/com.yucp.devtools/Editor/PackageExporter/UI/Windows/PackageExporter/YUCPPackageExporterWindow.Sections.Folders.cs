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
        private VisualElement CreateFoldersSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var header = CreateCollapsibleHeader("Export Folders", 
                () => showFolders, 
                (value) => { showFolders = value; }, 
                () => UpdateProfileDetails());
            section.Add(header);
            
            if (!showFolders)
            {
                return section;
            }
            
            if (profile.foldersToExport.Count == 0)
            {
                var warning = new VisualElement();
                warning.AddToClassList("yucp-validation-error");
                var warningText = new Label("No folders added. Add folders to export.");
                warningText.AddToClassList("yucp-validation-error-text");
                warning.Add(warningText);
                section.Add(warning);
            }
            else
            {
                var folderList = new VisualElement();
                folderList.AddToClassList("yucp-folder-list");
                
                for (int i = 0; i < profile.foldersToExport.Count; i++)
                {
                    int index = i; // Capture for closure
                    var folderItem = new VisualElement();
                    folderItem.AddToClassList("yucp-folder-item");
                    
                    var pathLabel = new Label(profile.foldersToExport[i]);
                    pathLabel.AddToClassList("yucp-folder-item-path");
                    folderItem.Add(pathLabel);
                    
                    var removeButton = new Button(() => RemoveFolder(profile, index)) { text = "×" };
                    removeButton.AddToClassList("yucp-button");
                    removeButton.AddToClassList("yucp-folder-item-remove");
                    folderItem.Add(removeButton);
                    
                    folderList.Add(folderItem);
                }
                
                section.Add(folderList);
            }
            
            var addButton = new Button(() => AddFolder(profile)) { text = "+ Add Folder" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            return section;
        }

        private VisualElement CreateExclusionFiltersSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var header = CreateCollapsibleHeader("Exclusion Filters", 
                () => showExclusionFilters, 
                (value) => { showExclusionFilters = value; }, 
                () => UpdateProfileDetails());
            section.Add(header);
            
            if (!showExclusionFilters)
            {
                return section;
            }
            
            var helpBox = new VisualElement();
            helpBox.AddToClassList("yucp-help-box");
            helpBox.style.marginTop = 8;
            var helpText = new Label("Exclude files and folders from export using patterns");
            helpText.AddToClassList("yucp-help-box-text");
            helpBox.Add(helpText);
            section.Add(helpBox);
            
            // File Patterns
            var filePatternsLabel = new Label("File Patterns");
            filePatternsLabel.AddToClassList("yucp-label");
            filePatternsLabel.style.marginTop = 8;
            filePatternsLabel.style.marginBottom = 4;
            filePatternsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.Add(filePatternsLabel);
            
            var filePatternsContainer = new VisualElement();
            filePatternsContainer.style.marginBottom = 8;
            
            for (int i = 0; i < profile.excludeFilePatterns.Count; i++)
            {
                int index = i;
                string originalValue = profile.excludeFilePatterns[i];
                var patternItem = CreateEditableStringListItem(
                    originalValue,
                    (newValue) =>
                    {
                        if (index < profile.excludeFilePatterns.Count)
                        {
                            Undo.RecordObject(profile, "Change Exclusion Pattern");
                            profile.excludeFilePatterns[index] = newValue;
                            EditorUtility.SetDirty(profile);
                        }
                    },
                    () =>
                    {
                        if (index < profile.excludeFilePatterns.Count)
                        {
                            Undo.RecordObject(profile, "Remove Exclusion Pattern");
                            profile.excludeFilePatterns.RemoveAt(index);
                            EditorUtility.SetDirty(profile);
                            AssetDatabase.SaveAssets();
                            UpdateProfileDetails();
                        }
                    });
                filePatternsContainer.Add(patternItem);
            }
            section.Add(filePatternsContainer);
            
            var addFilePatternButton = new Button(() =>
            {
                profile.excludeFilePatterns.Add("*.tmp");
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }) { text = "+ Add Pattern (e.g., *.tmp)" };
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
            
            var folderNamesContainer = new VisualElement();
            
            for (int i = 0; i < profile.excludeFolderNames.Count; i++)
            {
                int index = i;
                string originalValue = profile.excludeFolderNames[i];
                var folderItem = CreateEditableStringListItem(
                    originalValue,
                    (newValue) =>
                    {
                        if (index < profile.excludeFolderNames.Count)
                        {
                            Undo.RecordObject(profile, "Change Exclusion Folder");
                            profile.excludeFolderNames[index] = newValue;
                            EditorUtility.SetDirty(profile);
                        }
                    },
                    () =>
                    {
                        if (index < profile.excludeFolderNames.Count)
                        {
                            Undo.RecordObject(profile, "Remove Exclusion Folder");
                            profile.excludeFolderNames.RemoveAt(index);
                            EditorUtility.SetDirty(profile);
                            AssetDatabase.SaveAssets();
                            UpdateProfileDetails();
                        }
                    });
                folderNamesContainer.Add(folderItem);
            }
            section.Add(folderNamesContainer);
            
            var addFolderNameButton = new Button(() =>
            {
                profile.excludeFolderNames.Add(".git");
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }) { text = "+ Add Folder Name (e.g., .git)" };
            addFolderNameButton.AddToClassList("yucp-button");
            section.Add(addFolderNameButton);
            
            return section;
        }

        private VisualElement CreateStringListItem(string value, Action onRemove)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-folder-item");
            
            var textField = new TextField { value = value };
            textField.AddToClassList("yucp-input");
            textField.style.flexGrow = 1;
            textField.isReadOnly = true;
            item.Add(textField);
            
            var removeButton = new Button(onRemove) { text = "×" };
            removeButton.AddToClassList("yucp-button");
            removeButton.AddToClassList("yucp-folder-item-remove");
            item.Add(removeButton);
            
            return item;
        }

        private VisualElement CreateEditableStringListItem(string value, Action<string> onValueChanged, Action onRemove)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-folder-item");
            
            var textField = new TextField { value = value };
            textField.AddToClassList("yucp-input");
            textField.style.flexGrow = 1;
            textField.isReadOnly = false;
            textField.RegisterValueChangedCallback(evt =>
            {
                onValueChanged?.Invoke(evt.newValue);
            });
            item.Add(textField);
            
            var removeButton = new Button(onRemove) { text = "×" };
            removeButton.AddToClassList("yucp-button");
            removeButton.AddToClassList("yucp-folder-item-remove");
            item.Add(removeButton);
            
            return item;
        }

    }
}
