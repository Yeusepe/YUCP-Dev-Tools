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
        private void ScanAssetsForInspector(ExportProfile profile, bool silent = false)
        {
            EditorUtility.DisplayProgressBar("Scanning Assets", "Discovering assets from export folders...", 0f);
            
            try
            {
                var allAssets = new List<DiscoveredAsset>();
                
                // Scan parent profile assets
                var parentAssets = AssetCollector.ScanExportFolders(profile, profile.includeDependencies, profile.packageName);
                allAssets.AddRange(parentAssets);
                
                // Scan bundled profiles if this is a composite profile
                if (profile.HasIncludedProfiles())
                {
                    List<ExportProfile> resolved;
                    List<string> cycles;
                    CompositeProfileResolver.ResolveIncludedProfiles(profile, out resolved, out cycles);
                    
                    foreach (var bundledProfile in resolved)
                    {
                        if (bundledProfile != null)
                        {
                            EditorUtility.DisplayProgressBar("Scanning Assets", $"Scanning bundled profile: {bundledProfile.packageName}...", 0.5f);
                            var bundledAssets = AssetCollector.ScanExportFolders(bundledProfile, bundledProfile.includeDependencies, bundledProfile.packageName);
                            allAssets.AddRange(bundledAssets);
                        }
                    }
                }
                
                profile.discoveredAssets = allAssets;
                profile.MarkScanned();
                EditorUtility.SetDirty(profile);
                
                if (!silent)
                {
                    EditorUtility.DisplayDialog(
                        "Scan Complete",
                        $"Discovered {profile.discoveredAssets.Count} assets.\n\n" +
                        AssetCollector.GetAssetSummary(profile.discoveredAssets),
                        "OK");
                }
                
                UpdateProfileDetails();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Exporter] Asset scan failed: {ex.Message}");
                if (!silent)
                {
                    EditorUtility.DisplayDialog("Scan Failed", $"Failed to scan assets:\n{ex.Message}", "OK");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private string GetAssetTypeIcon(string assetType)
        {
            return assetType switch
            {
                "Script" => "C#",
                "Prefab" => "P",
                "Material" => "M",
                "Texture" => "T",
                "Scene" => "S",
                "Shader" => "SH",
                "Model" => "3D",
                "Animation" => "A",
                "Animator" => "AC",
                "Assembly" => "DLL",
                "Audio" => "AU",
                "Font" => "F",
                _ => "F"
            };
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }

        private void ClearAssetScan(ExportProfile profile)
        {
            if (EditorUtility.DisplayDialog("Clear Scan", 
                "Clear all discovered assets and rescan later?", "Clear", "Cancel"))
            {
                profile.ClearScan();
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            }
        }

    }
}
