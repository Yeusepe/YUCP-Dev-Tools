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
        private VisualElement CreateMetadataSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.AddToClassList("yucp-metadata-section");
            section.name = "package-identity-section";
            
            // Hero-style header with icon and name
            var headerRow = new VisualElement();
            headerRow.AddToClassList("yucp-metadata-header");
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 16;
            
            // Icon preview - clickable to change
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("yucp-metadata-icon-container");
            iconContainer.tooltip = "Click to change icon";
            
            var iconImageContainer = new VisualElement();
            iconImageContainer.AddToClassList("yucp-metadata-icon-image-container");
            
            var iconImage = new Image();
            Texture2D displayIcon = profile.icon;
            if (displayIcon == null)
            {
                displayIcon = GetPlaceholderTexture();
            }
            iconImage.image = displayIcon;
            iconImage.AddToClassList("yucp-metadata-icon-image");
            iconImageContainer.Add(iconImage);
            
            // Change icon button overlay
            iconContainer.Add(iconImageContainer);
            CreateHoverOverlayButton("Change", () => BrowseForIcon(profile), iconContainer);
            headerRow.Add(iconContainer);
            
            // Name and version in a column
            var nameVersionColumn = new VisualElement();
            nameVersionColumn.style.flexGrow = 1;
            nameVersionColumn.style.marginLeft = 16;
            
            // Package Name - large, prominent
            var nameField = new TextField { value = string.IsNullOrEmpty(profile.packageName) ? "Untitled Package" : profile.packageName };
            nameField.AddToClassList("yucp-metadata-name-field");
            nameField.tooltip = "Unique identifier for your package";
            nameField.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Package Name");
                    profile.packageName = evt.newValue;
                    profile.profileName = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    
                    // Schedule delayed rename
                    lastPackageNameChangeTime = EditorApplication.timeSinceStartup;
                    pendingRenameProfile = profile;
                    pendingRenamePackageName = evt.newValue;
                    
                    UpdateProfileList();
                }
            });
            nameVersionColumn.Add(nameField);
            
            // Version badge
            var versionRow = new VisualElement();
            versionRow.style.flexDirection = FlexDirection.Row;
            versionRow.style.alignItems = Align.Center;
            versionRow.style.marginTop = 6;
            
            var versionLabel = new Label("Version:");
            versionLabel.AddToClassList("yucp-label-small");
            versionLabel.style.marginRight = 6;
            versionRow.Add(versionLabel);
            
            var versionField = new TextField { value = profile.version };
            versionField.AddToClassList("yucp-input");
            versionField.AddToClassList("yucp-metadata-version-field");
            versionField.tooltip = "Package version (X.Y.Z)";
            versionField.style.width = 120;
            versionField.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Version");
                    profile.version = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    UpdateProfileList();
                    UpdateValidationDisplay(profile);
                }
            });
            versionRow.Add(versionField);
            nameVersionColumn.Add(versionRow);
            
            headerRow.Add(nameVersionColumn);
            section.Add(headerRow);
            
            // Description - prominent, multiline
            var descLabel = new Label("Description");
            descLabel.AddToClassList("yucp-label");
            descLabel.style.marginTop = 4;
            descLabel.style.marginBottom = 6;
            section.Add(descLabel);
            
            var descField = new TextField { value = profile.description, multiline = true };
            descField.AddToClassList("yucp-input");
            descField.AddToClassList("yucp-input-multiline");
            descField.AddToClassList("yucp-metadata-description-field");
            descField.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Description");
                    profile.description = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            section.Add(descField);
            
            // Author field - compact, single-line
            var authorLabel = new Label("Author");
            authorLabel.AddToClassList("yucp-label");
            authorLabel.style.marginTop = 4;
            authorLabel.style.marginBottom = 6;
            section.Add(authorLabel);
            
            var authorField = new TextField { value = profile.author };
            authorField.AddToClassList("yucp-input");
            authorField.AddToClassList("yucp-metadata-author-field");
            authorField.tooltip = "Author(s) - separate multiple authors with commas";
            authorField.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Author");
                    profile.author = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            section.Add(authorField);
            
            // Tags section - Modern design
            var tagsSection = new VisualElement();
            tagsSection.AddToClassList("yucp-tags-section");
            
            var tagsLabel = new Label("Tags");
            tagsLabel.AddToClassList("yucp-label");
            tagsLabel.style.marginTop = 16;
            tagsLabel.style.marginBottom = 8;
            tagsSection.Add(tagsLabel);
            
            // Tokenized Tag Input
            var tokenizedTags = new YUCP.DevTools.Editor.PackageExporter.UI.Components.TokenizedTagInput();
            
            // Collect available tags (Presets + Global Custom from EditorPrefs)
            var presetTagsType2 = typeof(YUCP.DevTools.Editor.PackageExporter.ExportProfile);
            var presetTagsField2 = presetTagsType2.GetField("AvailablePresetTags", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var availablePresets = presetTagsField2?.GetValue(null) as List<string> ?? new List<string>();
            
            string rawGlobalTags = EditorPrefs.GetString("YUCP_GlobalCustomTags", "");
            var globalTags = !string.IsNullOrEmpty(rawGlobalTags) 
                ? rawGlobalTags.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList() 
                : new List<string>();

            var allAvailable = availablePresets.Union(globalTags).Distinct().ToList();
            
            // Color Provider (Must be set BEFORE SetSelectedTags to apply colors initially)
            tokenizedTags.TagColorProvider = (tag) => GetTagColor(tag);
            
            tokenizedTags.SetAvailableTags(allAvailable);
            tokenizedTags.SetSelectedTags(profile.GetAllTags());
            
            // Tag Color Changed Handler
            tokenizedTags.OnTagColorChanged += (tag, color) => 
            {
                SetTagColor(tag, color);
                UpdateProfileList();
                UpdateProfileDetails(); // Refresh details to show new color in input immediately
            };
            
            // Tag Deleted Handler
            tokenizedTags.OnTagDeleted += (tag) => 
            {
                DeleteGlobalTag(tag);
                
                // Update available options
                if (globalTags.Contains(tag)) globalTags.Remove(tag);
                allAvailable = availablePresets.Union(globalTags).Distinct().ToList();
                tokenizedTags.SetAvailableTags(allAvailable);
                
                UpdateProfileList();
            };
            
            tokenizedTags.OnTagsChanged += (newTags) => {
                 if (profile != null) 
                 {
                    Undo.RecordObject(profile, "Change Tags");
                    
                    var newPresets = new List<string>();
                    var newCustoms = new List<string>();
                    var newlyCreated = new List<string>();
                    
                    foreach(var tag in newTags)
                    {
                        if (availablePresets.Contains(tag))
                        {
                            newPresets.Add(tag);
                        }
                        else
                        {
                            newCustoms.Add(tag);
                            if (!globalTags.Contains(tag))
                            {
                                newlyCreated.Add(tag);
                            }
                        }
                    }
                    
                    profile.presetTags = newPresets;
                    profile.customTags = newCustoms;
                    EditorUtility.SetDirty(profile);
                    
                    // Persist new tags globally
                    if (newlyCreated.Count > 0)
                    {
                        globalTags.AddRange(newlyCreated);
                        EditorPrefs.SetString("YUCP_GlobalCustomTags", string.Join("|", globalTags));
                        
                        // Update available options
                        allAvailable = availablePresets.Union(globalTags).Distinct().ToList();
                        tokenizedTags.SetAvailableTags(allAvailable);
                    }
                    
                    UpdateProfileList(); // Refresh sidebar filters
                 }
            };
            
            tagsSection.Add(tokenizedTags);
            
            // Allow layout pass then attach overlay
            tagsSection.schedule.Execute(() => tokenizedTags.AttachOverlayToRoot(rootVisualElement));
            section.Add(tagsSection);
            
            // Official Product Links section
            var linksLabel = new Label("Official Product Links");
            linksLabel.AddToClassList("yucp-label");
            linksLabel.style.marginTop = 16;
            linksLabel.style.marginBottom = 6;
            section.Add(linksLabel);
            
            var linksContainer = new VisualElement();
            linksContainer.AddToClassList("yucp-product-links-container");
            
            // Add existing links
            if (profile.productLinks != null)
            {
                foreach (var link in profile.productLinks)
                {
                    linksContainer.Add(CreateProductLinkCard(profile, link));
                }
            }
            
            // Add Link button
            var addLinkButton = new Button(() => AddProductLink(profile, linksContainer));
            addLinkButton.text = "+ Add Link";
            addLinkButton.AddToClassList("yucp-button");
            addLinkButton.AddToClassList("yucp-button-secondary");
            addLinkButton.style.marginTop = 8;
            addLinkButton.style.marginBottom = 4;
            linksContainer.Add(addLinkButton);
            
            section.Add(linksContainer);
            
            return section;
        }





    }
}
