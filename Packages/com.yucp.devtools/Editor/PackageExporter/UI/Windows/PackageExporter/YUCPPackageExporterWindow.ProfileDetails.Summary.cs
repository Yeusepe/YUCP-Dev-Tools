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
        private VisualElement CreateSummarySection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var title = new Label("Quick Summary");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var statsContainer = new VisualElement();
            statsContainer.AddToClassList("yucp-stats-container");
            
            AddStatItem(statsContainer, "Folders to Export", profile.foldersToExport.Count.ToString());
            
            // Bundled Profiles
            if (profile.HasIncludedProfiles())
            {
                var bundledProfiles = profile.GetIncludedProfiles();
                AddStatItem(statsContainer, "Bundled Profiles", bundledProfiles.Count.ToString());
                
                // Show breakdown if we have discovered assets with source info
                if (profile.discoveredAssets != null && profile.discoveredAssets.Count > 0)
                {
                    var sourceBreakdown = profile.discoveredAssets
                        .Where(a => !string.IsNullOrEmpty(a.sourceProfileName))
                        .GroupBy(a => a.sourceProfileName)
                        .Select(g => $"{g.Key}: {g.Count()}")
                        .ToList();
                    
                    if (sourceBreakdown.Count > 0)
                    {
                        // Add parent assets count
                        int parentAssets = profile.discoveredAssets.Count(a => 
                            string.IsNullOrEmpty(a.sourceProfileName) || a.sourceProfileName == profile.packageName);
                        if (parentAssets > 0)
                        {
                            sourceBreakdown.Insert(0, $"{profile.packageName}: {parentAssets}");
                        }
                        
                        string breakdownText = string.Join(", ", sourceBreakdown);
                        if (breakdownText.Length > 60)
                        {
                            breakdownText = breakdownText.Substring(0, 57) + "...";
                        }
                        AddStatItem(statsContainer, "Assets by Source", breakdownText);
                    }
                }
            }
            
            // Dependencies
            if (profile.dependencies.Count > 0)
            {
                int bundled = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Bundle);
                int referenced = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Dependency);
                AddStatItem(statsContainer, "Dependencies", $"{bundled} bundled, {referenced} referenced");
            }
            
            // Obfuscation
            string obfuscationText = profile.enableObfuscation 
                ? $"Enabled ({profile.assembliesToObfuscate.Count(a => a.enabled)} assemblies)" 
                : "Disabled";
            AddStatItem(statsContainer, "Obfuscation", obfuscationText);
            
            // Output path
            string outputText = string.IsNullOrEmpty(profile.exportPath) ? "Desktop" : profile.exportPath;
            AddStatItem(statsContainer, "Output", outputText);
            
            // Last export
            if (!string.IsNullOrEmpty(profile.LastExportTime))
            {
                AddStatItem(statsContainer, "Last Export", profile.LastExportTime);
            }
            
            section.Add(statsContainer);
            return section;
        }

        private void AddStatItem(VisualElement container, string label, string value)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-stat-item");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("yucp-stat-label");
            item.Add(labelElement);
            
            var valueElement = new Label(value);
            valueElement.AddToClassList("yucp-stat-value");
            item.Add(valueElement);
            
            container.Add(item);
        }

    }
}
