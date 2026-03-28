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
            nameField.name = "package-name-field";
            nameField.AddToClassList("yucp-metadata-name-field");
            nameField.tooltip = "Unique identifier for your package";
            _packageNameField = nameField;
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
            
            // Collapsible Storefront section
            section.Add(CreateStorefrontSection(profile));
            
            return section;
        }

        private VisualElement CreateStorefrontSection(ExportProfile profile)
        {
            var container = new VisualElement();
            container.style.marginTop = 16;

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.paddingTop = 8;
            headerRow.style.paddingBottom = 4;

            var headerLabel = new Label("Preview & Details");
            headerLabel.AddToClassList("yucp-label");
            headerLabel.style.fontSize = 13;
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLabel.style.color = new Color(0.75f, 0.75f, 0.78f);
            headerRow.Add(headerLabel);

            var headerSpacer = new VisualElement();
            headerSpacer.style.flexGrow = 1;
            headerRow.Add(headerSpacer);

            var headerHint = new Label("Essentials stay visible; optional parts toggle below");
            headerHint.style.fontSize = 10;
            headerHint.style.color = new Color(0.45f, 0.45f, 0.5f);
            headerRow.Add(headerHint);

            container.Add(headerRow);

            var body = new VisualElement();
            body.style.marginTop = 8;

            var taglineLabel = new Label("Tagline");
            taglineLabel.AddToClassList("yucp-label");
            taglineLabel.style.marginBottom = 4;
            body.Add(taglineLabel);

            var taglineField = new TextField { value = profile.tagline ?? "" };
            taglineField.AddToClassList("yucp-input");
            taglineField.tooltip = "Short one-liner displayed under the package name";
            taglineField.RegisterValueChangedCallback(evt =>
            {
                if (profile == null) return;
                Undo.RecordObject(profile, "Change Tagline");
                profile.tagline = evt.newValue;
                EditorUtility.SetDirty(profile);
            });
            body.Add(taglineField);

            var catPlatRow = new VisualElement();
            catPlatRow.style.flexDirection = FlexDirection.Row;
            catPlatRow.style.alignItems = Align.FlexStart;
            catPlatRow.style.marginTop = 12;

            var catCol = new VisualElement();
            catCol.style.marginRight = 16;
            catCol.style.minWidth = 120;

            var catLabel = new Label("Category");
            catLabel.AddToClassList("yucp-label");
            catLabel.style.marginBottom = 4;
            catCol.Add(catLabel);

            var categoryChoices = Enum.GetNames(typeof(PackageCategory)).ToList();
            var categoryPickerSlot = new VisualElement();
            bool categoryExpanded = false;
            VisualElement categoryOverlay = null;
            VisualElement categoryBackdrop = null;
            VisualElement categoryTrigger = null;

            void RebuildCategoryPicker()
            {
                categoryPickerSlot.Clear();

                bool open = categoryExpanded;
                var normalBg = new Color(0.118f, 0.118f, 0.118f);
                var hoverBg = new Color(0.138f, 0.138f, 0.138f);
                var accentBorder = new Color(0.212f, 0.749f, 0.694f);
                var textPri = new Color(0.961f, 0.961f, 0.961f);
                var textMute = new Color(0.549f, 0.549f, 0.549f);

                var trigger = new VisualElement();
                trigger.style.flexDirection = FlexDirection.Row;
                trigger.style.alignItems = Align.Center;
                trigger.style.justifyContent = Justify.SpaceBetween;
                trigger.style.minWidth = 160;
                trigger.style.paddingLeft = 12;
                trigger.style.paddingRight = 10;
                trigger.style.paddingTop = 9;
                trigger.style.paddingBottom = 9;
                trigger.style.backgroundColor = normalBg;
                trigger.style.borderTopLeftRadius = 5;
                trigger.style.borderTopRightRadius = 5;
                trigger.style.borderBottomLeftRadius = 5;
                trigger.style.borderBottomRightRadius = 5;
                trigger.style.borderLeftWidth = trigger.style.borderRightWidth = trigger.style.borderTopWidth = trigger.style.borderBottomWidth = 1;
                trigger.style.borderLeftColor = trigger.style.borderRightColor = trigger.style.borderTopColor = trigger.style.borderBottomColor = accentBorder;

                var valueCol = new VisualElement();
                valueCol.style.flexGrow = 1;

                string currentCategory = profile.category == PackageCategory.None ? "Select category…" : profile.category.ToString();
                var primary = new Label(currentCategory);
                primary.style.fontSize = 12;
                primary.style.color = profile.category == PackageCategory.None ? textMute : textPri;
                primary.style.unityFontStyleAndWeight = profile.category == PackageCategory.None ? FontStyle.Normal : FontStyle.Bold;
                valueCol.Add(primary);

                if (profile.category != PackageCategory.None)
                {
                    var secondary = new Label("Storefront category");
                    secondary.style.fontSize = 9;
                    secondary.style.color = textMute;
                    valueCol.Add(secondary);
                }

                trigger.Add(valueCol);

                var chevron = new Label(open ? "▴" : "▾");
                chevron.style.color = textMute;
                chevron.style.fontSize = 10;
                chevron.style.marginLeft = 6;
                chevron.style.unityTextAlign = TextAnchor.MiddleCenter;
                trigger.Add(chevron);

                categoryTrigger = trigger;
                trigger.RegisterCallback<MouseEnterEvent>(_ => { if (!categoryExpanded) trigger.style.backgroundColor = hoverBg; });
                trigger.RegisterCallback<MouseLeaveEvent>(_ => { if (!categoryExpanded) trigger.style.backgroundColor = normalBg; });
                trigger.RegisterCallback<ClickEvent>(_ =>
                {
                    if (categoryExpanded)
                    {
                        categoryExpanded = false;
                        if (categoryOverlay != null && categoryOverlay.parent != null) categoryOverlay.RemoveFromHierarchy();
                        if (categoryBackdrop != null && categoryBackdrop.parent != null) categoryBackdrop.RemoveFromHierarchy();
                        categoryOverlay = null;
                        categoryBackdrop = null;
                        RebuildCategoryPicker();
                        return;
                    }

                    if (rootVisualElement == null || categoryTrigger == null)
                    {
                        categoryExpanded = true;
                        RebuildCategoryPicker();
                        return;
                    }

                    categoryExpanded = true;
                    categoryBackdrop = new VisualElement();
                    categoryBackdrop.style.position = Position.Absolute;
                    categoryBackdrop.style.left = 0;
                    categoryBackdrop.style.top = 0;
                    categoryBackdrop.style.right = 0;
                    categoryBackdrop.style.bottom = 0;
                    categoryBackdrop.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
                    categoryBackdrop.RegisterCallback<ClickEvent>(_ =>
                    {
                        categoryExpanded = false;
                        if (categoryOverlay != null && categoryOverlay.parent != null) categoryOverlay.RemoveFromHierarchy();
                        if (categoryBackdrop != null && categoryBackdrop.parent != null) categoryBackdrop.RemoveFromHierarchy();
                        categoryOverlay = null;
                        categoryBackdrop = null;
                        RebuildCategoryPicker();
                    });

                    categoryOverlay = new VisualElement();
                    categoryOverlay.style.position = Position.Absolute;
                    categoryOverlay.style.backgroundColor = new Color(0.105f, 0.105f, 0.105f);
                    categoryOverlay.style.borderLeftWidth = categoryOverlay.style.borderRightWidth = categoryOverlay.style.borderBottomWidth = categoryOverlay.style.borderTopWidth = 1;
                    categoryOverlay.style.borderLeftColor = categoryOverlay.style.borderRightColor = categoryOverlay.style.borderBottomColor = categoryOverlay.style.borderTopColor = accentBorder;
                    categoryOverlay.style.borderTopLeftRadius = 5;
                    categoryOverlay.style.borderTopRightRadius = 5;
                    categoryOverlay.style.borderBottomLeftRadius = 5;
                    categoryOverlay.style.borderBottomRightRadius = 5;
                    categoryOverlay.style.overflow = Overflow.Hidden;

                    foreach (var choice in categoryChoices)
                    {
                        var row = new VisualElement();
                        row.style.flexDirection = FlexDirection.Row;
                        row.style.alignItems = Align.Center;
                        row.style.justifyContent = Justify.SpaceBetween;
                        row.style.paddingLeft = 12;
                        row.style.paddingRight = 12;
                        row.style.paddingTop = 8;
                        row.style.paddingBottom = 8;
                        row.style.backgroundColor = choice == profile.category.ToString()
                            ? new Color(0.212f, 0.749f, 0.694f, 0.18f)
                            : new Color(0.105f, 0.105f, 0.105f);

                        var label = new Label(choice);
                        label.style.fontSize = 11;
                        label.style.color = choice == profile.category.ToString()
                            ? new Color(0.961f, 0.961f, 0.961f)
                            : new Color(0.78f, 0.78f, 0.80f);
                        label.style.unityFontStyleAndWeight = choice == profile.category.ToString() ? FontStyle.Bold : FontStyle.Normal;
                        row.Add(label);

                        if (choice == profile.category.ToString())
                        {
                            var check = new Label("✓");
                            check.style.fontSize = 9;
                            check.style.color = accentBorder;
                            check.style.unityFontStyleAndWeight = FontStyle.Bold;
                            row.Add(check);
                        }

                        row.RegisterCallback<MouseEnterEvent>(_ =>
                        {
                            if (choice != profile.category.ToString())
                                row.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);
                        });
                        row.RegisterCallback<MouseLeaveEvent>(_ =>
                        {
                            row.style.backgroundColor = choice == profile.category.ToString()
                                ? new Color(0.212f, 0.749f, 0.694f, 0.18f)
                                : new Color(0.105f, 0.105f, 0.105f);
                        });
                        row.RegisterCallback<ClickEvent>(_ =>
                        {
                            if (!Enum.TryParse(choice, out PackageCategory newCategory)) return;
                            Undo.RecordObject(profile, "Change Category");
                            profile.category = newCategory;
                            EditorUtility.SetDirty(profile);
                            categoryExpanded = false;
                            if (categoryOverlay != null && categoryOverlay.parent != null) categoryOverlay.RemoveFromHierarchy();
                            if (categoryBackdrop != null && categoryBackdrop.parent != null) categoryBackdrop.RemoveFromHierarchy();
                            categoryOverlay = null;
                            categoryBackdrop = null;
                            RebuildCategoryPicker();
                        });

                        categoryOverlay.Add(row);
                    }

                    var triggerBounds = categoryTrigger.worldBound;
                    var rootBounds = rootVisualElement.worldBound;
                    float overlayWidth = Mathf.Max(180f, triggerBounds.width);
                    float left = triggerBounds.x - rootBounds.x;
                    float topBelow = triggerBounds.yMax - rootBounds.y + 4f;
                    float estimatedHeight = Mathf.Min(260f, categoryChoices.Count * 34f + 8f);
                    float top = topBelow + estimatedHeight > rootBounds.height
                        ? Mathf.Max(0f, triggerBounds.y - rootBounds.y - estimatedHeight - 4f)
                        : topBelow;

                    categoryOverlay.style.left = left;
                    categoryOverlay.style.top = top;
                    categoryOverlay.style.width = overlayWidth;

                    rootVisualElement.Add(categoryBackdrop);
                    rootVisualElement.Add(categoryOverlay);
                    categoryBackdrop.StretchToParentSize();
                    categoryBackdrop.BringToFront();
                    categoryOverlay.BringToFront();
                    RebuildCategoryPicker();
                });

                categoryPickerSlot.Add(trigger);
            }

            RebuildCategoryPicker();
            catCol.Add(categoryPickerSlot);
            catPlatRow.Add(catCol);

            var platCol = new VisualElement();
            platCol.style.flexGrow = 1;

            var platLabel = new Label("Platforms");
            platLabel.AddToClassList("yucp-label");
            platLabel.style.marginBottom = 4;
            platCol.Add(platLabel);

            var platChipRow = new VisualElement();
            platChipRow.style.flexDirection = FlexDirection.Row;
            platChipRow.style.flexWrap = Wrap.Wrap;

            foreach (var platform in global::YUCP.DevTools.Editor.PackageExporter.ExportProfile.AvailablePlatforms)
            {
                bool isSelected = profile.supportedPlatforms != null && profile.supportedPlatforms.Contains(platform);
                var chip = CreateToggleChip(platform, isSelected, selected =>
                {
                    if (profile == null) return;
                    Undo.RecordObject(profile, "Change Platforms");
                    if (profile.supportedPlatforms == null)
                        profile.supportedPlatforms = new List<string>();
                    if (selected && !profile.supportedPlatforms.Contains(platform))
                        profile.supportedPlatforms.Add(platform);
                    else if (!selected)
                        profile.supportedPlatforms.Remove(platform);
                    EditorUtility.SetDirty(profile);
                });
                platChipRow.Add(chip);
            }
            platCol.Add(platChipRow);
            catPlatRow.Add(platCol);

            body.Add(catPlatRow);

            var optionalLabel = new Label("Optional Sections");
            optionalLabel.AddToClassList("yucp-label");
            optionalLabel.style.marginTop = 12;
            optionalLabel.style.marginBottom = 4;
            body.Add(optionalLabel);

            var optionalHint = new Label("Reveal only the sections you want to work on.");
            optionalHint.style.fontSize = 10;
            optionalHint.style.color = new Color(0.45f, 0.45f, 0.5f);
            optionalHint.style.marginBottom = 6;
            body.Add(optionalHint);

            bool galleryVisible = EditorPrefs.GetBool("YUCP_StorefrontGalleryVisible", profile.galleryImages != null && profile.galleryImages.Count > 0);
            bool creatorNoteVisible = EditorPrefs.GetBool("YUCP_StorefrontCreatorNoteVisible", !string.IsNullOrWhiteSpace(profile.creatorNote));
            bool releaseNotesVisible = EditorPrefs.GetBool("YUCP_StorefrontReleaseNotesVisible", !string.IsNullOrWhiteSpace(profile.releaseNotes));

            var optionalChipsRow = new VisualElement();
            optionalChipsRow.style.flexDirection = FlexDirection.Row;
            optionalChipsRow.style.flexWrap = Wrap.Wrap;
            body.Add(optionalChipsRow);

            void SetOptionalSectionVisible(VisualElement section, string prefsKey, bool visible)
            {
                section.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                EditorPrefs.SetBool(prefsKey, visible);
            }

            var gallerySection = new VisualElement();
            gallerySection.style.display = galleryVisible ? DisplayStyle.Flex : DisplayStyle.None;

            var galleryLabel = new Label("Gallery");
            galleryLabel.AddToClassList("yucp-label");
            galleryLabel.style.marginTop = 12;
            galleryLabel.style.marginBottom = 4;
            gallerySection.Add(galleryLabel);

            var galleryHint = new Label("Up to 8 screenshots shown in the importer carousel");
            galleryHint.style.fontSize = 10;
            galleryHint.style.color = new Color(0.45f, 0.45f, 0.5f);
            galleryHint.style.marginBottom = 6;
            gallerySection.Add(galleryHint);

            var galleryStrip = new VisualElement();
            galleryStrip.style.flexDirection = FlexDirection.Row;
            galleryStrip.style.flexWrap = Wrap.Wrap;

            void RebuildGalleryStrip()
            {
                galleryStrip.Clear();
                if (profile.galleryImages != null)
                {
                    for (int i = 0; i < profile.galleryImages.Count && i < 8; i++)
                    {
                        int idx = i;
                        var thumb = CreateGalleryThumbnail(profile.galleryImages[i], () =>
                        {
                            Undo.RecordObject(profile, "Remove Gallery Image");
                            profile.galleryImages.RemoveAt(idx);
                            EditorUtility.SetDirty(profile);
                            RebuildGalleryStrip();
                        });
                        galleryStrip.Add(thumb);
                    }
                }

                if (profile.galleryImages == null || profile.galleryImages.Count < 8)
                {
                    var addBtn = new Button(() =>
                    {
                        string path = EditorUtility.OpenFilePanel("Select Gallery Image", "", "png,jpg,jpeg,tga,psd");
                        if (!string.IsNullOrEmpty(path))
                        {
                            string galleryFolder = "Assets/YUCP/ExportProfiles/Gallery";
                            if (!AssetDatabase.IsValidFolder(galleryFolder))
                            {
                                if (!AssetDatabase.IsValidFolder("Assets/YUCP"))
                                    AssetDatabase.CreateFolder("Assets", "YUCP");
                                if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles"))
                                    AssetDatabase.CreateFolder("Assets/YUCP", "ExportProfiles");
                                AssetDatabase.CreateFolder("Assets/YUCP/ExportProfiles", "Gallery");
                            }

                            string assetPath = FileUtil.GetProjectRelativePath(path);
                            if (string.IsNullOrEmpty(assetPath))
                            {
                                string fileName = Path.GetFileName(path);
                                string targetPath = AssetDatabase.GenerateUniqueAssetPath($"{galleryFolder}/{fileName}");
                                File.Copy(path, targetPath, true);
                                assetPath = targetPath;
                                AssetDatabase.ImportAsset(assetPath);
                                AssetDatabase.Refresh();
                            }

                            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                            if (tex != null)
                            {
                                Undo.RecordObject(profile, "Add Gallery Image");
                                if (profile.galleryImages == null)
                                    profile.galleryImages = new List<Texture2D>();
                                profile.galleryImages.Add(tex);
                                EditorUtility.SetDirty(profile);
                                AssetDatabase.SaveAssets();
                                RebuildGalleryStrip();
                            }
                        }
                    });
                    addBtn.text = "+";
                    addBtn.tooltip = "Add gallery image";
                    addBtn.style.width = 64;
                    addBtn.style.height = 64;
                    addBtn.style.fontSize = 20;
                    addBtn.style.borderTopLeftRadius = 6;
                    addBtn.style.borderTopRightRadius = 6;
                    addBtn.style.borderBottomLeftRadius = 6;
                    addBtn.style.borderBottomRightRadius = 6;
                    addBtn.style.marginRight = 6;
                    addBtn.style.marginBottom = 6;
                    addBtn.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);
                    addBtn.style.color = new Color(0.55f, 0.55f, 0.6f);
                    addBtn.style.borderTopColor = new Color(1f, 1f, 1f, 0.08f);
                    addBtn.style.borderRightColor = new Color(1f, 1f, 1f, 0.08f);
                    addBtn.style.borderBottomColor = new Color(1f, 1f, 1f, 0.08f);
                    addBtn.style.borderLeftColor = new Color(1f, 1f, 1f, 0.08f);
                    addBtn.style.borderTopWidth = 1;
                    addBtn.style.borderRightWidth = 1;
                    addBtn.style.borderBottomWidth = 1;
                    addBtn.style.borderLeftWidth = 1;
                    galleryStrip.Add(addBtn);
                }
            }

            RebuildGalleryStrip();
            gallerySection.Add(galleryStrip);
            body.Add(gallerySection);

            var tagsNoteLabel = new Label("Tags & Safety");
            tagsNoteLabel.AddToClassList("yucp-label");
            tagsNoteLabel.style.marginTop = 12;
            tagsNoteLabel.style.marginBottom = 4;
            body.Add(tagsNoteLabel);

            var tagsNote = new Label("Use the existing Tags section for discovery tags. Safety badges like 'No DLLs' are computed automatically from the exported package.");
            tagsNote.style.fontSize = 10;
            tagsNote.style.color = new Color(0.45f, 0.45f, 0.5f);
            tagsNote.style.whiteSpace = WhiteSpace.Normal;
            tagsNote.style.marginBottom = 6;
            body.Add(tagsNote);

            var creatorNoteSection = new VisualElement();
            creatorNoteSection.style.display = creatorNoteVisible ? DisplayStyle.Flex : DisplayStyle.None;

            var noteLabel = new Label("Creator Note");
            noteLabel.AddToClassList("yucp-label");
            noteLabel.style.marginTop = 12;
            noteLabel.style.marginBottom = 4;
            creatorNoteSection.Add(noteLabel);

            var noteField = new TextField { value = profile.creatorNote ?? "", multiline = true };
            noteField.AddToClassList("yucp-input");
            noteField.AddToClassList("yucp-input-multiline");
            noteField.tooltip = "Short message shown to importers in the 'From the Creator' section";
            noteField.style.minHeight = 40;
            noteField.RegisterValueChangedCallback(evt =>
            {
                if (profile == null) return;
                Undo.RecordObject(profile, "Change Creator Note");
                profile.creatorNote = evt.newValue;
                EditorUtility.SetDirty(profile);
            });
            creatorNoteSection.Add(noteField);
            body.Add(creatorNoteSection);

            var releaseNotesSection = new VisualElement();
            releaseNotesSection.style.display = releaseNotesVisible ? DisplayStyle.Flex : DisplayStyle.None;

            var rnLabel = new Label("Release Notes");
            rnLabel.AddToClassList("yucp-label");
            rnLabel.style.marginTop = 12;
            rnLabel.style.marginBottom = 4;
            releaseNotesSection.Add(rnLabel);

            var rnField = new TextField { value = profile.releaseNotes ?? "", multiline = true };
            rnField.AddToClassList("yucp-input");
            rnField.AddToClassList("yucp-input-multiline");
            rnField.tooltip = "What changed in this version — shown in the importer";
            rnField.style.minHeight = 40;
            rnField.RegisterValueChangedCallback(evt =>
            {
                if (profile == null) return;
                Undo.RecordObject(profile, "Change Release Notes");
                profile.releaseNotes = evt.newValue;
                EditorUtility.SetDirty(profile);
            });
            releaseNotesSection.Add(rnField);
            body.Add(releaseNotesSection);

            optionalChipsRow.Add(CreateToggleChip("Gallery", galleryVisible, visible =>
            {
                galleryVisible = visible;
                SetOptionalSectionVisible(gallerySection, "YUCP_StorefrontGalleryVisible", visible);
            }));
            optionalChipsRow.Add(CreateToggleChip("Creator Note", creatorNoteVisible, visible =>
            {
                creatorNoteVisible = visible;
                SetOptionalSectionVisible(creatorNoteSection, "YUCP_StorefrontCreatorNoteVisible", visible);
            }));
            optionalChipsRow.Add(CreateToggleChip("Release Notes", releaseNotesVisible, visible =>
            {
                releaseNotesVisible = visible;
                SetOptionalSectionVisible(releaseNotesSection, "YUCP_StorefrontReleaseNotesVisible", visible);
            }));

            var unityRow = new VisualElement();
            unityRow.style.flexDirection = FlexDirection.Row;
            unityRow.style.alignItems = Align.Center;
            unityRow.style.marginTop = 12;

            var unityLabel = new Label("Min Unity Version");
            unityLabel.AddToClassList("yucp-label");
            unityLabel.style.marginRight = 8;
            unityRow.Add(unityLabel);

            var unityField = new TextField { value = profile.minimumUnityVersion ?? "2022.3" };
            unityField.AddToClassList("yucp-input");
            unityField.style.width = 100;
            unityField.RegisterValueChangedCallback(evt =>
            {
                if (profile == null) return;
                Undo.RecordObject(profile, "Change Min Unity Version");
                profile.minimumUnityVersion = evt.newValue;
                EditorUtility.SetDirty(profile);
            });
            unityRow.Add(unityField);
            body.Add(unityRow);

            container.Add(body);
            return container;
        }

        private VisualElement CreateToggleChip(string label, bool selected, Action<bool> onToggle)
        {
            var chip = new VisualElement();
            chip.style.flexDirection = FlexDirection.Row;
            chip.style.alignItems = Align.Center;
            chip.style.paddingLeft = 10;
            chip.style.paddingRight = 10;
            chip.style.paddingTop = 4;
            chip.style.paddingBottom = 4;
            chip.style.marginRight = 6;
            chip.style.marginBottom = 4;
            chip.style.borderTopLeftRadius = 12;
            chip.style.borderTopRightRadius = 12;
            chip.style.borderBottomLeftRadius = 12;
            chip.style.borderBottomRightRadius = 12;
            chip.style.borderTopWidth = 1;
            chip.style.borderRightWidth = 1;
            chip.style.borderBottomWidth = 1;
            chip.style.borderLeftWidth = 1;

            var chipLabel = new Label(label);
            chipLabel.style.fontSize = 11;

            void ApplyStyle(bool sel)
            {
                if (sel)
                {
                    chip.style.backgroundColor = new Color(0.21f, 0.75f, 0.69f, 0.15f);
                    chip.style.borderTopColor = new Color(0.21f, 0.75f, 0.69f, 0.4f);
                    chip.style.borderRightColor = new Color(0.21f, 0.75f, 0.69f, 0.4f);
                    chip.style.borderBottomColor = new Color(0.21f, 0.75f, 0.69f, 0.4f);
                    chip.style.borderLeftColor = new Color(0.21f, 0.75f, 0.69f, 0.4f);
                    chipLabel.style.color = new Color(0.21f, 0.75f, 0.69f, 1f);
                }
                else
                {
                    chip.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);
                    chip.style.borderTopColor = new Color(1f, 1f, 1f, 0.1f);
                    chip.style.borderRightColor = new Color(1f, 1f, 1f, 0.1f);
                    chip.style.borderBottomColor = new Color(1f, 1f, 1f, 0.1f);
                    chip.style.borderLeftColor = new Color(1f, 1f, 1f, 0.1f);
                    chipLabel.style.color = new Color(0.6f, 0.6f, 0.65f);
                }
            }

            bool isSelected = selected;
            ApplyStyle(isSelected);

            chip.Add(chipLabel);
            chip.RegisterCallback<ClickEvent>(_ =>
            {
                isSelected = !isSelected;
                ApplyStyle(isSelected);
                onToggle?.Invoke(isSelected);
            });

            return chip;
        }

        private VisualElement CreateGalleryThumbnail(Texture2D texture, Action onRemove)
        {
            var container = new VisualElement();
            container.style.width = 64;
            container.style.height = 64;
            container.style.marginRight = 6;
            container.style.marginBottom = 6;
            container.style.borderTopLeftRadius = 6;
            container.style.borderTopRightRadius = 6;
            container.style.borderBottomLeftRadius = 6;
            container.style.borderBottomRightRadius = 6;
            container.style.overflow = Overflow.Hidden;
            container.style.borderTopWidth = 1;
            container.style.borderRightWidth = 1;
            container.style.borderBottomWidth = 1;
            container.style.borderLeftWidth = 1;
            container.style.borderTopColor = new Color(1f, 1f, 1f, 0.08f);
            container.style.borderRightColor = new Color(1f, 1f, 1f, 0.08f);
            container.style.borderBottomColor = new Color(1f, 1f, 1f, 0.08f);
            container.style.borderLeftColor = new Color(1f, 1f, 1f, 0.08f);

            if (texture != null)
            {
                var img = new Image { image = texture };
                img.style.width = new Length(100, LengthUnit.Percent);
                img.style.height = new Length(100, LengthUnit.Percent);
                img.scaleMode = ScaleMode.ScaleAndCrop;
                container.Add(img);
            }
            else
            {
                container.style.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
            }

            // Hover remove overlay
            var removeBtn = new Button(() => onRemove?.Invoke());
            removeBtn.text = "✕";
            removeBtn.style.position = Position.Absolute;
            removeBtn.style.top = 2;
            removeBtn.style.right = 2;
            removeBtn.style.width = 18;
            removeBtn.style.height = 18;
            removeBtn.style.fontSize = 10;
            removeBtn.style.paddingLeft = 0;
            removeBtn.style.paddingRight = 0;
            removeBtn.style.paddingTop = 0;
            removeBtn.style.paddingBottom = 0;
            removeBtn.style.borderTopLeftRadius = 9;
            removeBtn.style.borderTopRightRadius = 9;
            removeBtn.style.borderBottomLeftRadius = 9;
            removeBtn.style.borderBottomRightRadius = 9;
            removeBtn.style.backgroundColor = new Color(0.8f, 0.2f, 0.2f, 0.85f);
            removeBtn.style.color = Color.white;
            removeBtn.style.borderTopWidth = 0;
            removeBtn.style.borderRightWidth = 0;
            removeBtn.style.borderBottomWidth = 0;
            removeBtn.style.borderLeftWidth = 0;
            removeBtn.style.display = DisplayStyle.None;
            container.Add(removeBtn);

            container.RegisterCallback<MouseEnterEvent>(_ => removeBtn.style.display = DisplayStyle.Flex);
            container.RegisterCallback<MouseLeaveEvent>(_ => removeBtn.style.display = DisplayStyle.None);

            return container;
        }

        private void CreateHoverOverlayButton(string label, Action onClick, VisualElement container)
        {
            var btn = new Button(onClick) { text = label };
            btn.AddToClassList("yucp-overlay-hover-button");
            container.Add(btn);
            container.RegisterCallback<MouseEnterEvent>(_ => btn.AddToClassList("yucp-overlay-hover-button-visible"));
            container.RegisterCallback<MouseLeaveEvent>(_ => btn.RemoveFromClassList("yucp-overlay-hover-button-visible"));
        }
    }
}
