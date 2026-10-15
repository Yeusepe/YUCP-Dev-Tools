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
                RescanProfileAssets(profile);

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

        /// <summary>Re-discovers a profile's assets (including bundled profiles). No UI besides a progress bar.</summary>
        private static void RescanProfileAssets(ExportProfile profile)
        {
            var allAssets = new List<DiscoveredAsset>();


            var parentAssets = AssetCollector.ScanExportFolders(profile, profile.includeDependencies, profile.packageName);
            allAssets.AddRange(parentAssets);


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

            AssetCollector.PreserveIncludedState(profile.discoveredAssets, allAssets);
            profile.discoveredAssets = allAssets;
            profile.ApplyExcludedPathsToDiscoveredAssets();
            profile.MarkScanned();
            EditorUtility.SetDirty(profile);
        }

        /// <summary>Rescans the assets of every given profile that has something to scan.</summary>
        private void RescanProfiles(IEnumerable<ExportProfile> profiles)
        {
            var targets = profiles.Where(ProfileHasContentToScan).ToList();
            if (targets.Count == 0)
            {
                _toast?.ShowInfo("No profiles have folders or bundled profiles to scan.", "Rescan");
                return;
            }

            int scanned = 0;
            var failed = new List<string>();
            try
            {
                foreach (var profile in targets)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Rescanning Profiles",
                            $"{GetProfileDisplayName(profile)} ({scanned + failed.Count + 1}/{targets.Count})",
                            (float)(scanned + failed.Count) / targets.Count))
                        break;

                    try
                    {
                        RescanProfileAssets(profile);
                        scanned++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(GetProfileDisplayName(profile));
                        Debug.LogError($"[Package Exporter] Asset scan failed for '{GetProfileDisplayName(profile)}': {ex.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            UpdateProfileList();
            UpdateProfileDetails();

            if (failed.Count > 0)
                _toast?.ShowWarning($"Rescanned {scanned} profile(s); {failed.Count} failed: {string.Join(", ", failed)}. See Console.", "Rescan");
            else
                _toast?.ShowSuccess($"Rescanned {scanned} of {targets.Count} profile(s).", "Rescan Complete");
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


    }
}
