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
            var container = new VisualElement();
            container.AddToClassList("yucp-summary-bar");
            

            
            // 2. Version
            string ver = string.IsNullOrEmpty(profile.version) ? "—" : profile.version;
            AddHorizontalStat(container, "VER", ver);
            
            AddSummarySeparator(container);

            // 2b. Bundled Profiles (Restored)
            if (profile.HasIncludedProfiles())
            {
                int bundledCount = profile.GetIncludedProfiles().Count;
                if (bundledCount > 0)
                {
                    string bundleLabel = bundledCount == 1 ? "Profile" : "Profiles";
                    AddHorizontalStat(container, "BUNDLED", $"{bundledCount} {bundleLabel}");
                    AddSummarySeparator(container);
                }
            }

            // 2c. Dependencies (Restored)
            if (profile.dependencies != null && profile.dependencies.Count > 0)
            {
                int enabledDeps = profile.dependencies.Count(d => d.enabled);
                if (enabledDeps > 0)
                {
                    AddHorizontalStat(container, "DEPS", $"{enabledDeps} Linked");
                    AddSummarySeparator(container);
                }
            }

            // 3. Package Size (New)
            long totalBytes = 0;
            if (profile.discoveredAssets != null)
            {
                // Calculate size of all included files (excluding folders)
                // Note: We need to access fileSize property of DiscoveredAsset. 
                // Using reflection or dynamic if DiscoveredAsset definition is not visible, 
                // but usually it is public in the same assembly. 
                // Assuming DiscoveredAsset has 'fileSize' and 'isFolder' based on AssetCollector code.
                foreach (var asset in profile.discoveredAssets)
                {
                    if (!asset.isFolder && asset.included)
                    {
                        totalBytes += asset.fileSize;
                    }
                }
            }
            AddHorizontalStat(container, "SIZE", FormatBytes(totalBytes));
            
            AddSummarySeparator(container);

            // 4. Export Folder (Fixed)
            string outPath = "Desktop"; // Default per AssetCollector logic
            if (!string.IsNullOrEmpty(profile.exportPath))
            {
                try {
                    outPath = new System.IO.DirectoryInfo(profile.exportPath).Name;
                    if (outPath.Length > 15) outPath = outPath.Substring(0, 14) + "…";
                } catch {
                    outPath = "Custom";
                }
            }
            AddHorizontalStat(container, "FOLDER", outPath);
            
            // 5. Last Export
            AddSummarySeparator(container);
            string lastExport = string.IsNullOrEmpty(profile.LastExportTime) ? "Never" : profile.LastExportTime;
            AddHorizontalStat(container, "LAST BUILD", lastExport);

            return container;
        }

        private VisualElement AddHorizontalStat(VisualElement container, string label, string value, bool isPrimary = false)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-summary-stat");
            
            var keyLabel = new Label(label);
            keyLabel.AddToClassList("yucp-summary-stat-label");
            item.Add(keyLabel);
            
            var valLabel = new Label(value);
            valLabel.AddToClassList("yucp-summary-stat-value");
            if (isPrimary) valLabel.AddToClassList("yucp-summary-accent");
            item.Add(valLabel);
            
            container.Add(item);
            return item;
        }

        private void AddSummarySeparator(VisualElement container)
        {
            var sep = new VisualElement();
            sep.AddToClassList("yucp-summary-sep");
            container.Add(sep);
        }
    }
}
