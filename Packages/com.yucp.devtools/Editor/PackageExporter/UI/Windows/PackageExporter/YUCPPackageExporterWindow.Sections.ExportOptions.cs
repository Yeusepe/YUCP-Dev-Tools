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
        private VisualElement CreateExportOptionsSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.name = "versioning-section";
            
            var header = CreateCollapsibleHeader("Export Options", 
                () => showExportOptions, 
                (value) => { showExportOptions = value; }, 
                () => UpdateProfileDetails());
            section.Add(header);
            
            if (!showExportOptions)
            {
                return section;
            }
            
            // Toggles container
            var togglesContainer = new VisualElement();
            togglesContainer.style.flexDirection = FlexDirection.Column;
            togglesContainer.style.marginBottom = 8;
            
            // Include Dependencies
            var includeDepsToggle = new Toggle("Include Dependencies") { value = profile.includeDependencies };
            includeDepsToggle.AddToClassList("yucp-toggle");
            includeDepsToggle.tooltip = "Include all dependency files directly in the exported package";
            includeDepsToggle.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Include Dependencies");
                    profile.includeDependencies = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    // Update the asset list to reflect the toggle change
                    UpdateProfileDetails();
                }
            });
            togglesContainer.Add(includeDepsToggle);
            
            var recurseFoldersToggle = new Toggle("Recurse Folders") { value = profile.recurseFolders };
            recurseFoldersToggle.AddToClassList("yucp-toggle");
            recurseFoldersToggle.tooltip = "Search subfolders when collecting assets to export";
            recurseFoldersToggle.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Recurse Folders");
                    profile.recurseFolders = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            togglesContainer.Add(recurseFoldersToggle);
            
            // Generate package.json
            var generateJsonToggle = new Toggle("Generate package.json") { value = profile.generatePackageJson };
            generateJsonToggle.AddToClassList("yucp-toggle");
            generateJsonToggle.tooltip = "Create a package.json file with dependency information for VPM compatibility";
            generateJsonToggle.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Generate Package Json");
                    profile.generatePackageJson = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            togglesContainer.Add(generateJsonToggle);
            
            // Auto-Increment Version
            var autoIncrementToggle = new Toggle("Auto-Increment Version") { value = profile.autoIncrementVersion };
            autoIncrementToggle.AddToClassList("yucp-toggle");
            autoIncrementToggle.tooltip = "Automatically increment the version number on each export";
            autoIncrementToggle.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Auto Increment Version");
                    profile.autoIncrementVersion = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    UpdateProfileDetails(); // Refresh to show/hide increment strategy
                }
            });
            togglesContainer.Add(autoIncrementToggle);
            
            section.Add(togglesContainer);
            
            // Increment Strategy (only if auto-increment is enabled)
            if (profile.autoIncrementVersion)
            {
                var strategyRow = CreateFormRow("What to Bump", tooltip: "Choose which number to increment");
                var strategyField = new EnumField(profile.incrementStrategy);
                strategyField.AddToClassList("yucp-dropdown");
                strategyField.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Increment Strategy");
                        profile.incrementStrategy = (VersionIncrementStrategy)evt.newValue;
                        EditorUtility.SetDirty(profile);
                        UpdateProfileDetails(); // Refresh to show help
                    }
                });
                strategyRow.Add(strategyField);
                section.Add(strategyRow);
                
                // Show what each strategy does
                var strategyHelp = new Label(GetStrategyExplanation(profile.incrementStrategy));
                strategyHelp.style.fontSize = 11;
                strategyHelp.style.color = new UnityEngine.UIElements.StyleColor(new Color(0.6f, 0.8f, 1.0f));
                strategyHelp.style.marginLeft = 4;
                strategyHelp.style.marginTop = 2;
                strategyHelp.style.marginBottom = 8;
                strategyHelp.style.unityFontStyleAndWeight = FontStyle.Italic;
                section.Add(strategyHelp);
                
                // Custom Version Rule (optional)
                var customRuleRow = CreateFormRow("Custom Rule (Optional)", tooltip: "Use a custom version rule for special formats. Leave empty for standard semver.");
                var customRuleField = new ObjectField { objectType = typeof(CustomVersionRule), value = profile.customVersionRule };
                customRuleField.AddToClassList("yucp-form-field");
                customRuleField.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Custom Rule");
                        profile.customVersionRule = evt.newValue as CustomVersionRule;
                        if (profile.customVersionRule != null)
                        {
                            profile.customVersionRule.RegisterRule();
                        }
                        EditorUtility.SetDirty(profile);
                        UpdateProfileDetails(); // Refresh UI
                    }
                });
                customRuleRow.Add(customRuleField);
                
                var createRuleBtn = new Button(() => CreateCustomRule(profile)) { text = "New" };
                createRuleBtn.AddToClassList("yucp-button");
                createRuleBtn.AddToClassList("yucp-button-small");
                createRuleBtn.tooltip = "Create a new custom version rule";
                customRuleRow.Add(createRuleBtn);
                
                section.Add(customRuleRow);
                
                // Custom Rule Editor (show when a rule is assigned)
                if (profile.customVersionRule != null)
                {
                    section.Add(CreateCustomRuleEditor(profile));
                }
                
                // Bump Directives in Files toggle
                var bumpDirectivesToggle = new Toggle("Auto-bump @directives in Files") { value = profile.bumpDirectivesInFiles };
                bumpDirectivesToggle.AddToClassList("yucp-toggle");
                bumpDirectivesToggle.tooltip = "Automatically update versions in source files that have @bump directives";
                bumpDirectivesToggle.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Bump Directives");
                        profile.bumpDirectivesInFiles = evt.newValue;
                        EditorUtility.SetDirty(profile);
                        UpdateProfileDetails(); // Refresh to show/hide help text
                    }
                });
                section.Add(bumpDirectivesToggle);
                
                // Help text for smart version bumping
                if (profile.bumpDirectivesInFiles)
                {
                    var helpBox = new VisualElement();
                    helpBox.style.backgroundColor = new UnityEngine.UIElements.StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.3f));
                    helpBox.style.borderTopWidth = 1;
                    helpBox.style.borderBottomWidth = 1;
                    helpBox.style.borderLeftWidth = 1;
                    helpBox.style.borderRightWidth = 1;
                    helpBox.style.borderTopColor = new UnityEngine.UIElements.StyleColor(new Color(0.1f, 0.1f, 0.1f));
                    helpBox.style.borderBottomColor = new UnityEngine.UIElements.StyleColor(new Color(0.1f, 0.1f, 0.1f));
                    helpBox.style.borderLeftColor = new UnityEngine.UIElements.StyleColor(new Color(0.1f, 0.1f, 0.1f));
                    helpBox.style.borderRightColor = new UnityEngine.UIElements.StyleColor(new Color(0.1f, 0.1f, 0.1f));
                    helpBox.style.borderTopLeftRadius = 4;
                    helpBox.style.borderTopRightRadius = 4;
                    helpBox.style.borderBottomLeftRadius = 4;
                    helpBox.style.borderBottomRightRadius = 4;
                    helpBox.style.paddingTop = 8;
                    helpBox.style.paddingBottom = 8;
                    helpBox.style.paddingLeft = 8;
                    helpBox.style.paddingRight = 8;
                    helpBox.style.marginTop = 8;
                    helpBox.style.marginBottom = 8;
                    
                    var helpTitle = new Label("How to use:");
                    helpTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                    helpTitle.style.fontSize = 11;
                    helpTitle.style.marginBottom = 4;
                    helpBox.Add(helpTitle);
                    
                    string exampleRule = profile.customVersionRule != null 
                        ? profile.customVersionRule.ruleName 
                        : "semver";
                    
                    var helpText = new Label(
                        $"Add comment directives to your source files:\n" +
                        $"  public const string Version = \"1.0.0\"; // @bump {exampleRule}\n\n" +
                        "When you export, these versions will auto-update according to your Increment Strategy.");
                    helpText.style.fontSize = 11;
                    helpText.style.color = new UnityEngine.UIElements.StyleColor(new Color(0.7f, 0.7f, 0.7f));
                    helpText.style.whiteSpace = UnityEngine.UIElements.WhiteSpace.Normal;
                    helpBox.Add(helpText);
                    
                    section.Add(helpBox);
                }
            }
            
            // Export Path
            var pathRow = CreateFormRow("Export Path", tooltip: "Folder where the exported .unitypackage file will be saved");
            var pathField = new TextField { value = profile.exportPath };
            pathField.AddToClassList("yucp-input");
            pathField.AddToClassList("yucp-form-field");
            pathField.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Export Path");
                    profile.exportPath = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            pathRow.Add(pathField);
            var browsePathButton = new Button(() => BrowseForPath(profile)) { text = "Browse" };
            browsePathButton.AddToClassList("yucp-button");
            browsePathButton.AddToClassList("yucp-button-small");
            pathRow.Add(browsePathButton);
            section.Add(pathRow);
            
            if (string.IsNullOrEmpty(profile.exportPath))
            {
                var hintBox = new VisualElement();
                hintBox.AddToClassList("yucp-help-box");
                var hintText = new Label("Empty path = Desktop");
                hintText.AddToClassList("yucp-help-box-text");
                hintBox.Add(hintText);
                section.Add(hintBox);
            }
            
            return section;
        }

    }
}
