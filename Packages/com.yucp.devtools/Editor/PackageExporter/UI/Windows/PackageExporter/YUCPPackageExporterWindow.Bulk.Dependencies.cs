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
        private VisualElement CreateBulkDependenciesSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Package Dependencies");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpText = new Label("Add or remove dependencies across all selected profiles");
            helpText.AddToClassList("yucp-label-secondary");
            helpText.style.marginBottom = 8;
            section.Add(helpText);
            
            // Get unique dependencies across all profiles
            var allDependencies = selectedProfiles
                .SelectMany(p => p.dependencies ?? new List<PackageDependency>())
                .GroupBy(d => d.packageName)
                .Select(g => g.First())
                .OrderBy(d => d.packageName)
                .ToList();
            
            var depsList = new VisualElement();
            depsList.AddToClassList("yucp-folder-list");
            depsList.style.maxHeight = 200;
            
            var scrollView = new ScrollView();
            
            foreach (var dep in allDependencies)
            {
                var depItem = new VisualElement();
                depItem.AddToClassList("yucp-folder-item");
                
                // Check if all profiles have this dependency and if it's enabled
                bool allHaveDep = selectedProfiles.All(p => p.dependencies.Any(d => d.packageName == dep.packageName));
                bool someHaveDep = selectedProfiles.Any(p => p.dependencies.Any(d => d.packageName == dep.packageName));
                int enabledCount = selectedProfiles.Count(p => p.dependencies.Any(d => d.packageName == dep.packageName && d.enabled));
                int totalCount = selectedProfiles.Count;
                
                var checkbox = new Toggle();
                checkbox.value = allHaveDep;
                checkbox.AddToClassList("yucp-toggle");
                checkbox.RegisterValueChangedCallback(evt =>
                {
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Change Dependency");
                        var existingDep = profile.dependencies.FirstOrDefault(d => d.packageName == dep.packageName);
                        if (evt.newValue)
                        {
                            if (existingDep == null)
                            {
                                // Clone the dependency to add to this profile
                                var newDep = new PackageDependency(dep.packageName, dep.packageVersion, dep.displayName, dep.isVpmDependency);
                                newDep.enabled = dep.enabled;
                                newDep.exportMode = dep.exportMode;
                                profile.dependencies.Add(newDep);
                            }
                        }
                        else
                        {
                            if (existingDep != null)
                            {
                                profile.dependencies.Remove(existingDep);
                            }
                        }
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                });
                depItem.Add(checkbox);
                
                // Create container for label and status
                var labelContainer = new VisualElement();
                labelContainer.style.flexDirection = FlexDirection.Row;
                labelContainer.style.flexGrow = 1;
                labelContainer.style.alignItems = Align.Center;
                
                var depLabel = new Label($"{dep.displayName} ({dep.packageName}@{dep.packageVersion})");
                depLabel.AddToClassList("yucp-folder-item-path");
                depLabel.style.flexGrow = 1;
                if (!allHaveDep && someHaveDep)
                {
                    depLabel.style.opacity = 0.7f;
                }
                labelContainer.Add(depLabel);
                
                // Add status indicator showing how many profiles have this dependency enabled
                if (someHaveDep)
                {
                    var statusLabel = new Label();
                    statusLabel.AddToClassList("yucp-label-secondary");
                    statusLabel.style.marginLeft = 8;
                    statusLabel.style.fontSize = 10;
                    
                    if (allHaveDep)
                    {
                        if (enabledCount == totalCount)
                        {
                            statusLabel.text = $"[OK] All ({enabledCount}/{totalCount} enabled)";
                            statusLabel.style.color = new Color(0.3f, 0.8f, 0.3f);
                        }
                        else if (enabledCount == 0)
                        {
                            statusLabel.text = $"All ({enabledCount}/{totalCount} enabled)";
                            statusLabel.style.color = new Color(0.8f, 0.5f, 0.3f);
                        }
                        else
                        {
                            statusLabel.text = $"All ({enabledCount}/{totalCount} enabled)";
                            statusLabel.style.color = new Color(0.8f, 0.8f, 0.3f);
                        }
                    }
                    else
                    {
                        int haveCount = selectedProfiles.Count(p => p.dependencies.Any(d => d.packageName == dep.packageName));
                        statusLabel.text = $"Mixed ({haveCount}/{totalCount} have it, {enabledCount} enabled)";
                        statusLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                    }
                    labelContainer.Add(statusLabel);
                }
                
                depItem.Add(labelContainer);
                
                scrollView.Add(depItem);
            }
            
            depsList.Add(scrollView);
            section.Add(depsList);
            
            // Add dependency button
            var addButton = new Button(() => 
            {
                // Create a default dependency - users can edit it after adding
                var newDep = new PackageDependency("com.example.package", "1.0.0", "Example Package", false);
                
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Add Dependency");
                    // Check if a dependency with this name already exists
                    if (!profile.dependencies.Any(d => d.packageName == newDep.packageName))
                    {
                        // Clone the dependency for each profile
                        var clonedDep = new PackageDependency(newDep.packageName, newDep.packageVersion, newDep.displayName, newDep.isVpmDependency);
                        clonedDep.enabled = newDep.enabled;
                        clonedDep.exportMode = newDep.exportMode;
                        profile.dependencies.Add(clonedDep);
                    }
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            }) { text = "+ Add Dependency to All" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            return section;
        }

    }
}
