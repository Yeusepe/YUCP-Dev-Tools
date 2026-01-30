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
        private VisualElement CreateObfuscationSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var title = new Label("Assembly Obfuscation");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var enableToggle = new Toggle("Enable Obfuscation") { value = profile.enableObfuscation };
            enableToggle.AddToClassList("yucp-toggle");
            enableToggle.tooltip = "Protect compiled assemblies using ConfuserEx";
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Enable Obfuscation");
                    profile.enableObfuscation = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    UpdateProfileDetails(); // Refresh to show/hide obfuscation options
                }
            });
            section.Add(enableToggle);
            
            if (profile.enableObfuscation)
            {
                // Protection Level
                var presetRow = CreateFormRow("Protection Level", tooltip: "Choose how aggressively to obfuscate");
                var presetField = new EnumField(profile.obfuscationPreset);
                presetField.AddToClassList("yucp-dropdown");
                presetField.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Obfuscation Preset");
                        profile.obfuscationPreset = (ConfuserExPreset)evt.newValue;
                        EditorUtility.SetDirty(profile);
                    }
                });
                presetRow.Add(presetField);
                section.Add(presetRow);
                
                // Strip Debug Symbols
                var stripToggle = new Toggle("Strip Debug Symbols") { value = profile.stripDebugSymbols };
                stripToggle.AddToClassList("yucp-toggle");
                stripToggle.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Strip Debug Symbols");
                        profile.stripDebugSymbols = evt.newValue;
                        EditorUtility.SetDirty(profile);
                    }
                });
                section.Add(stripToggle);
                
                // Scan Assemblies button
                var scanButton = new Button(() => ScanAllAssemblies(profile)) { text = "Scan Assemblies" };
                scanButton.AddToClassList("yucp-button");
                scanButton.AddToClassList("yucp-button-action");
                scanButton.style.marginTop = 8;
                section.Add(scanButton);
                
                // Assembly selection list
                if (profile.assembliesToObfuscate.Count > 0)
                {
                    int enabledCount = profile.assembliesToObfuscate.Count(a => a.enabled);
                    var countLabel = new Label($"Found {profile.assembliesToObfuscate.Count} assemblies ({enabledCount} selected)");
                    countLabel.AddToClassList("yucp-label-secondary");
                    countLabel.style.marginTop = 8;
                    section.Add(countLabel);
                    
                    var scrollView = new ScrollView();
                    scrollView.style.maxHeight = 200;
                    scrollView.style.marginTop = 8;
                    
                    for (int i = 0; i < profile.assembliesToObfuscate.Count; i++)
                    {
                        int index = i;
                        var assembly = profile.assembliesToObfuscate[i];
                        
                        var assemblyItem = new VisualElement();
                        assemblyItem.AddToClassList("yucp-folder-item");
                        
                        var checkbox = new Toggle { value = assembly.enabled };
                        checkbox.AddToClassList("yucp-toggle");
                        checkbox.RegisterValueChangedCallback(evt =>
                        {
                            if (profile != null && index < profile.assembliesToObfuscate.Count)
                            {
                                Undo.RecordObject(profile, "Change Assembly Obfuscation");
                                profile.assembliesToObfuscate[index].enabled = evt.newValue;
                                EditorUtility.SetDirty(profile);
                                UpdateProfileDetails();
                            }
                        });
                        assemblyItem.Add(checkbox);
                        
                        var assemblyLabel = new Label(assembly.assemblyName);
                        assemblyLabel.AddToClassList("yucp-folder-item-path");
                        assemblyLabel.style.marginLeft = 8;
                        assemblyItem.Add(assemblyLabel);
                        
                        scrollView.Add(assemblyItem);
                    }
                    
                    section.Add(scrollView);
                }
            }
            
            return section;
        }

    }
}
