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
        private VisualElement CreateBulkAssemblyObfuscationSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Assembly Obfuscation Settings");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpText = new Label("Configure which assemblies to obfuscate");
            helpText.AddToClassList("yucp-label-secondary");
            helpText.style.marginBottom = 8;
            section.Add(helpText);
            
            var allAssemblies = selectedProfiles
                .SelectMany(p => p.assembliesToObfuscate ?? new List<AssemblyObfuscationSettings>())
                .GroupBy(a => a.assemblyName)
                .Select(g => g.First())
                .OrderBy(a => a.assemblyName)
                .ToList();
            
            var assemblyList = new VisualElement();
            assemblyList.style.maxHeight = 200;
            var scrollView = new ScrollView();
            
            foreach (var assembly in allAssemblies)
            {
                var assemblyItem = new VisualElement();
                assemblyItem.AddToClassList("yucp-folder-item");
                
                bool allHaveAssembly = selectedProfiles.All(p => p.assembliesToObfuscate.Any(a => a.assemblyName == assembly.assemblyName));
                bool someHaveAssembly = selectedProfiles.Any(p => p.assembliesToObfuscate.Any(a => a.assemblyName == assembly.assemblyName));
                
                var checkbox = new Toggle();
                checkbox.value = allHaveAssembly;
                checkbox.AddToClassList("yucp-toggle");
                checkbox.RegisterValueChangedCallback(evt =>
                {
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Change Assembly Obfuscation");
                        var existingAssembly = profile.assembliesToObfuscate.FirstOrDefault(a => a.assemblyName == assembly.assemblyName);
                        if (evt.newValue)
                        {
                            if (existingAssembly == null)
                            {
                                var newAssembly = new AssemblyObfuscationSettings
                                {
                                    assemblyName = assembly.assemblyName,
                                    enabled = assembly.enabled,
                                    asmdefPath = assembly.asmdefPath
                                };
                                profile.assembliesToObfuscate.Add(newAssembly);
                            }
                        }
                        else
                        {
                            if (existingAssembly != null)
                            {
                                profile.assembliesToObfuscate.Remove(existingAssembly);
                            }
                        }
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                });
                assemblyItem.Add(checkbox);
                
                var assemblyLabel = new Label($"{assembly.assemblyName} ({(assembly.enabled ? "Enabled" : "Disabled")})");
                assemblyLabel.AddToClassList("yucp-folder-item-path");
                if (!allHaveAssembly && someHaveAssembly)
                {
                    assemblyLabel.style.opacity = 0.7f;
                    var mixedLabel = new Label(" (Mixed)");
                    mixedLabel.AddToClassList("yucp-label-secondary");
                    mixedLabel.style.marginLeft = 4;
                    assemblyItem.Add(mixedLabel);
                }
                assemblyItem.Add(assemblyLabel);
                
                scrollView.Add(assemblyItem);
            }
            
            assemblyList.Add(scrollView);
            section.Add(assemblyList);
            
            var addButton = new Button(() =>
            {
                var newAssembly = new AssemblyObfuscationSettings
                {
                    assemblyName = "Assembly-CSharp",
                    enabled = true,
                    asmdefPath = ""
                };
                
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Add Assembly");
                    if (!profile.assembliesToObfuscate.Any(a => a.assemblyName == newAssembly.assemblyName))
                    {
                        var clonedAssembly = new AssemblyObfuscationSettings
                        {
                            assemblyName = newAssembly.assemblyName,
                            enabled = newAssembly.enabled,
                            asmdefPath = newAssembly.asmdefPath
                        };
                        profile.assembliesToObfuscate.Add(clonedAssembly);
                    }
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            }) { text = "+ Add Assembly to All" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            return section;
        }

        private VisualElement CreateBulkObfuscationSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Obfuscation Settings");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            bool allObfuscation = selectedProfiles.All(p => p.enableObfuscation);
            bool allStripDebug = selectedProfiles.All(p => p.stripDebugSymbols);
            
            var obfuscationToggle = CreateBulkToggle("Enable Obfuscation", allObfuscation,
                profile => profile.enableObfuscation,
                (profile, value) => profile.enableObfuscation = value);
            section.Add(obfuscationToggle);
            
            var stripDebugToggle = CreateBulkToggle("Strip Debug Symbols", allStripDebug,
                profile => profile.stripDebugSymbols,
                (profile, value) => profile.stripDebugSymbols = value);
            section.Add(stripDebugToggle);
            
            // Obfuscation Preset (only if all have same value)
            var presets = selectedProfiles.Select(p => p.obfuscationPreset).Distinct().ToList();
            if (presets.Count == 1)
            {
                var presetRow = CreateFormRow("Obfuscation Preset");
                var presetField = new EnumField(presets[0]);
                presetField.AddToClassList("yucp-dropdown");
                presetField.RegisterValueChangedCallback(evt =>
                {
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Change Obfuscation Preset");
                        profile.obfuscationPreset = (ConfuserExPreset)evt.newValue;
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                });
                presetRow.Add(presetField);
                section.Add(presetRow);
            }
            
            return section;
        }

        private VisualElement CreateBulkToggle(string label, bool allSame, 
            System.Func<ExportProfile, bool> getValue, 
            System.Action<ExportProfile, bool> setValue)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.marginBottom = 4;
            
            var toggle = new Toggle(label);
            toggle.AddToClassList("yucp-toggle");
            toggle.value = allSame;
            
            if (!allSame)
            {
                // When mixed, show indeterminate state but allow toggling
                var mixedLabel = new Label(" (Mixed - click to set all)");
                mixedLabel.AddToClassList("yucp-label-secondary");
                mixedLabel.style.marginLeft = 4;
                container.Add(mixedLabel);
            }
            
            toggle.RegisterValueChangedCallback(evt =>
            {
                // Apply the new value to all selected profiles
                bool newValue = evt.newValue;
                ApplyToAllSelected(profile => 
                {
                    Undo.RecordObject(profile, $"Bulk Toggle {label}");
                    setValue(profile, newValue);
                    EditorUtility.SetDirty(profile);
                });
                // Refresh to update UI
                UpdateProfileDetails();
            });
            
            container.Add(toggle);
            return container;
        }

    }
}
