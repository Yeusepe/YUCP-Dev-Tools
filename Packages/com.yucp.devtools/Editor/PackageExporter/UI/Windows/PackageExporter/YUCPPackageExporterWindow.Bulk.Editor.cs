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
        private VisualElement CreateBulkEditorSection()
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var title = new Label($"Bulk Edit ({selectedProfileIndices.Count} profiles selected)");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpBox = new VisualElement();
            helpBox.AddToClassList("yucp-help-box");
            var helpText = new Label("Changes made here will be applied to all selected profiles. Package names remain unique to each profile.");
            helpText.AddToClassList("yucp-help-box-text");
            helpBox.Add(helpText);
            section.Add(helpBox);
            
            // Get selected profiles
            var selectedProfiles = selectedProfileIndices
                .Select(i => allProfiles[i])
                .Where(p => p != null)
                .ToList();
            
            // Version field
            var versionRow = CreateFormRow("Version", tooltip: "Set version for all selected profiles");
            var versionField = new TextField();
            versionField.AddToClassList("yucp-input");
            versionField.AddToClassList("yucp-form-field");
            
            // Show current version if all are the same, otherwise show placeholder
            var versions = selectedProfiles.Select(p => p.version).Distinct().ToList();
            Label versionPlaceholder = null;
            if (versions.Count == 1)
            {
                versionField.value = versions[0];
            }
            else
            {
                versionField.value = "";
                versionField.style.opacity = 0.7f;
                // Add placeholder label
                versionPlaceholder = new Label("Mixed values - enter new version");
                versionPlaceholder.AddToClassList("yucp-label-secondary");
                versionPlaceholder.style.position = Position.Absolute;
                versionPlaceholder.style.left = 8;
                versionPlaceholder.style.top = 4;
                versionPlaceholder.pickingMode = PickingMode.Ignore;
                versionRow.style.position = Position.Relative;
                versionRow.Add(versionPlaceholder);
            }
            
            versionField.RegisterValueChangedCallback(evt =>
            {
                if (versionPlaceholder != null)
                {
                    if (string.IsNullOrEmpty(evt.newValue))
                    {
                        versionPlaceholder.style.display = DisplayStyle.Flex;
                        versionField.style.opacity = 0.7f;
                    }
                    else
                    {
                        versionPlaceholder.style.display = DisplayStyle.None;
                        versionField.style.opacity = 1f;
                    }
                }
                
                // Apply changes to all selected profiles
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    ApplyToAllSelected(profile => 
                    {
                        Undo.RecordObject(profile, "Bulk Change Version");
                        profile.version = evt.newValue;
                        EditorUtility.SetDirty(profile);
                    });
                    // Refresh to show updated value
                    UpdateProfileDetails();
                }
            });
            versionRow.Add(versionField);
            section.Add(versionRow);
            
            // Author field
            var authorRow = CreateFormRow("Author", tooltip: "Set author for all selected profiles");
            var authorField = new TextField();
            authorField.AddToClassList("yucp-input");
            authorField.AddToClassList("yucp-form-field");
            
            var authors = selectedProfiles.Select(p => p.author ?? "").Distinct().ToList();
            Label authorPlaceholder = null;
            if (authors.Count == 1)
            {
                authorField.value = authors[0];
            }
            else
            {
                authorField.value = "";
                authorField.style.opacity = 0.7f;
                authorPlaceholder = new Label("Mixed values - enter new author");
                authorPlaceholder.AddToClassList("yucp-label-secondary");
                authorPlaceholder.style.position = Position.Absolute;
                authorPlaceholder.style.left = 8;
                authorPlaceholder.style.top = 4;
                authorPlaceholder.pickingMode = PickingMode.Ignore;
                authorRow.style.position = Position.Relative;
                authorRow.Add(authorPlaceholder);
            }
            
            authorField.RegisterValueChangedCallback(evt =>
            {
                if (authorPlaceholder != null)
                {
                    if (string.IsNullOrEmpty(evt.newValue))
                    {
                        authorPlaceholder.style.display = DisplayStyle.Flex;
                        authorField.style.opacity = 0.7f;
                    }
                    else
                    {
                        authorPlaceholder.style.display = DisplayStyle.None;
                        authorField.style.opacity = 1f;
                    }
                }
                
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    ApplyToAllSelected(profile => 
                    {
                        Undo.RecordObject(profile, "Bulk Change Author");
                        profile.author = evt.newValue;
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                }
            });
            authorRow.Add(authorField);
            section.Add(authorRow);
            
            // Description field
            var descriptionRow = CreateFormRow("Description", tooltip: "Set description for all selected profiles");
            var descriptionField = new TextField();
            descriptionField.AddToClassList("yucp-input");
            descriptionField.AddToClassList("yucp-form-field");
            descriptionField.multiline = true;
            descriptionField.style.height = 60;
            
            var descriptions = selectedProfiles.Select(p => p.description ?? "").Distinct().ToList();
            Label descriptionPlaceholder = null;
            if (descriptions.Count == 1)
            {
                descriptionField.value = descriptions[0];
            }
            else
            {
                descriptionField.value = "";
                descriptionField.style.opacity = 0.7f;
                descriptionPlaceholder = new Label("Mixed values - enter new description");
                descriptionPlaceholder.AddToClassList("yucp-label-secondary");
                descriptionPlaceholder.style.position = Position.Absolute;
                descriptionPlaceholder.style.left = 8;
                descriptionPlaceholder.style.top = 4;
                descriptionPlaceholder.pickingMode = PickingMode.Ignore;
                descriptionRow.style.position = Position.Relative;
                descriptionRow.Add(descriptionPlaceholder);
            }
            
            descriptionField.RegisterValueChangedCallback(evt =>
            {
                if (descriptionPlaceholder != null)
                {
                    if (string.IsNullOrEmpty(evt.newValue))
                    {
                        descriptionPlaceholder.style.display = DisplayStyle.Flex;
                        descriptionField.style.opacity = 0.7f;
                    }
                    else
                    {
                        descriptionPlaceholder.style.display = DisplayStyle.None;
                        descriptionField.style.opacity = 1f;
                    }
                }
                
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    ApplyToAllSelected(profile => 
                    {
                        Undo.RecordObject(profile, "Bulk Change Description");
                        profile.description = evt.newValue;
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                }
            });
            descriptionRow.Add(descriptionField);
            section.Add(descriptionRow);
            
            // Icon field
            var iconRow = CreateFormRow("Icon", tooltip: "Set icon for all selected profiles");
            var icons = selectedProfiles.Select(p => p.icon).Distinct().ToList();
            var iconField = new ObjectField();
            iconField.objectType = typeof(Texture2D);
            iconField.AddToClassList("yucp-form-field");
            if (icons.Count == 1)
            {
                iconField.value = icons[0];
            }
            else
            {
                iconField.value = null;
            }
            iconField.RegisterValueChangedCallback(evt =>
            {
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Change Icon");
                    profile.icon = evt.newValue as Texture2D;
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            });
            iconRow.Add(iconField);
            section.Add(iconRow);
            
            // Export Path
            var pathRow = CreateFormRow("Export Path", tooltip: "Set export path for all selected profiles");
            var pathField = new TextField();
            pathField.AddToClassList("yucp-input");
            pathField.AddToClassList("yucp-form-field");
            
            var paths = selectedProfiles.Select(p => p.exportPath ?? "").Distinct().ToList();
            Label pathPlaceholder = null;
            if (paths.Count == 1)
            {
                pathField.value = paths[0];
            }
            else
            {
                pathField.value = "";
                pathField.style.opacity = 0.7f;
                // Add placeholder label
                pathPlaceholder = new Label("Mixed values - use Browse to set");
                pathPlaceholder.AddToClassList("yucp-label-secondary");
                pathPlaceholder.style.position = Position.Absolute;
                pathPlaceholder.style.left = 8;
                pathPlaceholder.style.top = 4;
                pathPlaceholder.pickingMode = PickingMode.Ignore;
                pathField.RegisterValueChangedCallback(evt => 
                {
                    if (string.IsNullOrEmpty(evt.newValue) && pathPlaceholder != null)
                    {
                        pathPlaceholder.style.display = DisplayStyle.Flex;
                        pathField.style.opacity = 0.7f;
                    }
                    else
                    {
                        if (pathPlaceholder != null)
                            pathPlaceholder.style.display = DisplayStyle.None;
                        pathField.style.opacity = 1f;
                    }
                });
                pathRow.style.position = Position.Relative;
                pathRow.Add(pathPlaceholder);
            }
            
            var browseButton = new Button(() => 
            {
                string currentPath = pathField.value;
                if (string.IsNullOrEmpty(currentPath))
                {
                    currentPath = "";
                }
                string newPath = EditorUtility.OpenFolderPanel("Select Export Path", currentPath, "");
                if (!string.IsNullOrEmpty(newPath))
                {
                    pathField.value = newPath;
                    // Hide placeholder if it exists
                    if (pathPlaceholder != null)
                    {
                        pathPlaceholder.style.display = DisplayStyle.None;
                        pathField.style.opacity = 1f;
                    }
                    ApplyToAllSelected(profile => 
                    {
                        Undo.RecordObject(profile, "Bulk Change Export Path");
                        profile.exportPath = newPath;
                        EditorUtility.SetDirty(profile);
                    });
                    // Refresh to show updated value
                    UpdateProfileDetails();
                }
            }) { text = "Browse" };
            browseButton.AddToClassList("yucp-button");
            browseButton.AddToClassList("yucp-button-action");
            pathRow.Add(pathField);
            pathRow.Add(browseButton);
            section.Add(pathRow);
            
            // Profile Save Location
            var profileSaveRow = CreateFormRow("Profile Save Location", tooltip: "Custom location to save profiles");
            var profileSaveField = new TextField();
            profileSaveField.AddToClassList("yucp-input");
            profileSaveField.AddToClassList("yucp-form-field");
            
            var saveLocs = selectedProfiles.Select(p => p.profileSaveLocation ?? "").Distinct().ToList();
            Label savePlaceholder = null;
            if (saveLocs.Count == 1)
            {
                profileSaveField.value = saveLocs[0];
            }
            else
            {
                profileSaveField.value = "";
                profileSaveField.style.opacity = 0.7f;
                savePlaceholder = new Label("Mixed values - use Browse to set");
                savePlaceholder.AddToClassList("yucp-label-secondary");
                savePlaceholder.style.position = Position.Absolute;
                savePlaceholder.style.left = 8;
                savePlaceholder.style.top = 4;
                savePlaceholder.pickingMode = PickingMode.Ignore;
                profileSaveField.RegisterValueChangedCallback(evt => 
                {
                    if (string.IsNullOrEmpty(evt.newValue) && savePlaceholder != null)
                    {
                        savePlaceholder.style.display = DisplayStyle.Flex;
                        profileSaveField.style.opacity = 0.7f;
                    }
                    else
                    {
                        if (savePlaceholder != null)
                            savePlaceholder.style.display = DisplayStyle.None;
                        profileSaveField.style.opacity = 1f;
                    }
                });
                profileSaveRow.style.position = Position.Relative;
                profileSaveRow.Add(savePlaceholder);
            }
            
            var browseSaveButton = new Button(() => 
            {
                string currentPath = profileSaveField.value;
                if (string.IsNullOrEmpty(currentPath))
                {
                    currentPath = "";
                }
                string newPath = EditorUtility.OpenFolderPanel("Select Profile Save Location", currentPath, "");
                if (!string.IsNullOrEmpty(newPath))
                {
                    profileSaveField.value = newPath;
                    if (savePlaceholder != null)
                    {
                        savePlaceholder.style.display = DisplayStyle.None;
                        profileSaveField.style.opacity = 1f;
                    }
                    ApplyToAllSelected(profile => 
                    {
                        Undo.RecordObject(profile, "Bulk Change Profile Save Location");
                        profile.profileSaveLocation = newPath;
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                }
            }) { text = "Browse" };
            browseSaveButton.AddToClassList("yucp-button");
            browseSaveButton.AddToClassList("yucp-button-action");
            profileSaveRow.Add(profileSaveField);
            profileSaveRow.Add(browseSaveButton);
            section.Add(profileSaveRow);
            
            // Export Options Toggles
            var optionsTitle = new Label("Export Options");
            optionsTitle.AddToClassList("yucp-section-title");
            optionsTitle.style.marginTop = 16;
            optionsTitle.style.marginBottom = 8;
            section.Add(optionsTitle);
            
            // Get common values for toggles
            bool allIncludeDeps = selectedProfiles.All(p => p.includeDependencies);
            bool allRecurse = selectedProfiles.All(p => p.recurseFolders);
            bool allGenerateJson = selectedProfiles.All(p => p.generatePackageJson);
            
            // Include Dependencies
            var includeDepsToggle = CreateBulkToggle("Include Dependencies", allIncludeDeps, 
                profile => profile.includeDependencies,
                (profile, value) => profile.includeDependencies = value);
            section.Add(includeDepsToggle);
            
            // Recurse Folders
            var recurseToggle = CreateBulkToggle("Recurse Folders", allRecurse,
                profile => profile.recurseFolders,
                (profile, value) => profile.recurseFolders = value);
            section.Add(recurseToggle);
            
            // Generate Package JSON
            var generateJsonToggle = CreateBulkToggle("Generate package.json", allGenerateJson,
                profile => profile.generatePackageJson,
                (profile, value) => profile.generatePackageJson = value);
            section.Add(generateJsonToggle);
            
            // Version Management Section
            var versionMgmtTitle = new Label("Version Management");
            versionMgmtTitle.AddToClassList("yucp-section-title");
            versionMgmtTitle.style.marginTop = 16;
            versionMgmtTitle.style.marginBottom = 8;
            section.Add(versionMgmtTitle);
            
            bool allAutoIncrement = selectedProfiles.All(p => p.autoIncrementVersion);
            bool allBumpDirectives = selectedProfiles.All(p => p.bumpDirectivesInFiles);
            
            var autoIncrementToggle = CreateBulkToggle("Auto-Increment Version", allAutoIncrement,
                profile => profile.autoIncrementVersion,
                (profile, value) => profile.autoIncrementVersion = value);
            section.Add(autoIncrementToggle);
            
            // Increment Strategy (only if all have same value)
            var strategies = selectedProfiles.Select(p => p.incrementStrategy).Distinct().ToList();
            if (strategies.Count == 1)
            {
                var strategyRow = CreateFormRow("Increment Strategy");
                var strategyField = new EnumField(strategies[0]);
                strategyField.AddToClassList("yucp-dropdown");
                strategyField.RegisterValueChangedCallback(evt =>
                {
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Change Increment Strategy");
                        profile.incrementStrategy = (VersionIncrementStrategy)evt.newValue;
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                });
                strategyRow.Add(strategyField);
                section.Add(strategyRow);
            }
            
            var bumpDirectivesToggle = CreateBulkToggle("Bump @bump Directives in Files", allBumpDirectives,
                profile => profile.bumpDirectivesInFiles,
                (profile, value) => profile.bumpDirectivesInFiles = value);
            section.Add(bumpDirectivesToggle);
            
            // Custom Version Rule
            var customRules = selectedProfiles.Select(p => p.customVersionRule).Distinct().ToList();
            var ruleRow = CreateFormRow("Custom Version Rule");
            var ruleField = new ObjectField();
            ruleField.objectType = typeof(CustomVersionRule);
            ruleField.AddToClassList("yucp-form-field");
            if (customRules.Count == 1)
            {
                ruleField.value = customRules[0];
            }
            else
            {
                ruleField.value = null;
                // Add mixed label
                var mixedLabel = new Label(" (Mixed values)");
                mixedLabel.AddToClassList("yucp-label-secondary");
                mixedLabel.style.marginLeft = 4;
                ruleRow.Add(mixedLabel);
            }
            ruleField.RegisterValueChangedCallback(evt =>
            {
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Change Custom Version Rule");
                    profile.customVersionRule = evt.newValue as CustomVersionRule;
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            });
            ruleRow.Add(ruleField);
            section.Add(ruleRow);
            
            // Add Folders Section
            var foldersSection = CreateBulkFoldersSection(selectedProfiles);
            section.Add(foldersSection);
            
            // Add Dependencies Section
            var dependenciesSection = CreateBulkDependenciesSection(selectedProfiles);
            section.Add(dependenciesSection);
            
            // Add Exclusion Filters Section
            var exclusionSection = CreateBulkExclusionFiltersSection(selectedProfiles);
            section.Add(exclusionSection);
            
            // Add Permanent Ignore Folders Section
            var ignoreSection = CreateBulkPermanentIgnoreFoldersSection(selectedProfiles);
            section.Add(ignoreSection);
            
            // Add Obfuscation Section
            var obfuscationSection = CreateBulkObfuscationSection(selectedProfiles);
            section.Add(obfuscationSection);
            
            // Add Assembly Obfuscation Section
            var assemblySection = CreateBulkAssemblyObfuscationSection(selectedProfiles);
            section.Add(assemblySection);
            
            return section;
        }

    }
}
