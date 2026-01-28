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
        private void ExportSingleProfile(ExportProfile profile)
        {
            ExportProfile(profile);
        }

        private void UpdateBottomBar()
        {
            // Update multi-select info
            if (selectedProfileIndices.Count > 1)
            {
                _multiSelectInfo.style.display = DisplayStyle.Flex;
                var textLabel = _multiSelectInfo.Q<Label>("multiSelectText");
                if (textLabel != null)
                {
                    textLabel.text = $"{selectedProfileIndices.Count} profiles selected";
                }
            }
            else
            {
                _multiSelectInfo.style.display = DisplayStyle.None;
            }
            
            // Update export button
            // If exporting, disable it
            // If no profiles exist at all, disable it
            // Otherwise enable (overlay handles specific validations)
            _exportButton.SetEnabled(allProfiles.Count > 0 && !isExporting);
            
            // Text is static "Export" now, but let's double check if we want to change it?
            // Requirement: "Change the 'Export Selected Profile' to Export"
            _exportButton.text = "Export";
        }

        private void UpdateProgress(float progress, string status)
        {
            currentProgress = progress;
            currentStatus = status;
            
            _progressFill.style.width = Length.Percent(progress * 100);
            _progressText.text = $"{(progress * 100):F0}% - {status}";
            
            // Append to step log (avoid duplicates for same status)
            if (!string.IsNullOrEmpty(status) && (_exportStepLog.Count == 0 || _exportStepLog[_exportStepLog.Count - 1] != status))
            {
                _exportStepLog.Add(status);
                while (_exportStepLog.Count > MaxExportStepLogEntries)
                    _exportStepLog.RemoveAt(0);
                _progressDetail.text = string.Join("\n", _exportStepLog);
                // Scroll to bottom so latest step is visible
                if (_progressDetailScroll != null)
                    _progressDetailScroll.verticalScroller.value = _progressDetailScroll.verticalScroller.highValue;
            }
            
            // Force repaint so UI updates during blocking export (otherwise it looks stuck)
            Repaint();
        }
        
        private void ClearExportStepLog()
        {
            _exportStepLog.Clear();
            _progressDetail.text = "";
        }

        private void ExportSelectedProfiles()
        {
            if (selectedProfileIndices.Count == 0)
            {
                EditorUtility.DisplayDialog("No Selection", "No profiles are selected.", "OK");
                return;
            }
            
            if (selectedProfileIndices.Count == 1)
            {
                ExportProfile(selectedProfile);
            }
            else
            {
                var selectedProfiles = selectedProfileIndices.OrderBy(i => i).Select(i => allProfiles[i]).ToList();
                ExportAllProfiles(selectedProfiles);
            }
        }

        private static string BuildExportConfirmationMessage(ExportProfile profile)
        {
            var lines = new List<string>();
            lines.Add($"Export package: {profile.packageName} v{profile.version}");
            lines.Add("");

            // Content: folders + bundled profiles
            lines.Add("Content:");
            lines.Add($"  Export Folders ({profile.foldersToExport.Count}):");
            if (profile.foldersToExport.Count > 0)
            {
                foreach (var f in profile.foldersToExport.Take(5))
                    lines.Add($"    {f}");
                if (profile.foldersToExport.Count > 5)
                    lines.Add($"    ... and {profile.foldersToExport.Count - 5} more");
            }
            else
                lines.Add("    None configured");

            if (profile.HasIncludedProfiles())
            {
                var included = profile.GetIncludedProfiles().Where(p => p != null).ToList();
                if (included.Count > 0)
                {
                    var names = included.Select(p => p.packageName).Where(n => !string.IsNullOrEmpty(n)).ToList();
                    string bundleList = names.Count <= 4
                        ? string.Join(", ", names)
                        : string.Join(", ", names.Take(3)) + $", ... +{names.Count - 3} more";
                    lines.Add($"  Bundled Profiles ({included.Count}): {bundleList}");
                }
            }
            lines.Add("");

            // Assets (if scanned)
            if (profile.HasScannedAssets && profile.discoveredAssets != null && profile.discoveredAssets.Count > 0)
            {
                int includedCount = profile.discoveredAssets.Count(a => !a.isFolder && a.included);
                int totalCount = profile.discoveredAssets.Count(a => !a.isFolder);
                lines.Add($"Assets: {includedCount} included ({totalCount} discovered)");
                lines.Add("");
            }

            // Dependencies (package deps: bundled into package vs referenced in package.json)
            var deps = profile.dependencies ?? new List<PackageDependency>();
            var bundledPkgs = deps.Where(d => d != null && d.enabled && d.exportMode == DependencyExportMode.Bundle).ToList();
            var refPkgs = deps.Where(d => d != null && d.enabled && d.exportMode == DependencyExportMode.Dependency).ToList();
            lines.Add("Package Dependencies:");
            if (bundledPkgs.Count > 0)
            {
                var names = bundledPkgs.Select(d => d.packageName).Where(n => !string.IsNullOrEmpty(n)).ToList();
                string bl = names.Count <= 3 ? string.Join(", ", names) : string.Join(", ", names.Take(2)) + $", ... +{names.Count - 2} more";
                lines.Add($"  Bundled (into package): {bundledPkgs.Count} — {bl}");
            }
            else
                lines.Add("  Bundled (into package): 0");
            if (refPkgs.Count > 0)
            {
                var names = refPkgs.Select(d => d.packageName).Where(n => !string.IsNullOrEmpty(n)).ToList();
                string rl = names.Count <= 3 ? string.Join(", ", names) : string.Join(", ", names.Take(2)) + $", ... +{names.Count - 2} more";
                lines.Add($"  Referenced (package.json): {refPkgs.Count} — {rl}");
            }
            else
                lines.Add("  Referenced (package.json): 0");
            lines.Add("");

            int obfuscAssemblies = profile.assembliesToObfuscate != null ? profile.assembliesToObfuscate.Count(a => a != null && a.enabled) : 0;
            lines.Add($"Obfuscation: {(profile.enableObfuscation ? $"Enabled ({profile.obfuscationPreset}, {obfuscAssemblies} assemblies)" : "Disabled")}");
            lines.Add("");
            lines.Add($"Output: {profile.GetOutputFilePath()}");
            return string.Join("\n", lines);
        }

        private void ExportProfile(ExportProfile profile)
        {
            if (profile == null)
                return;
            
            if (!profile.Validate(out string errorMessage))
            {
                EditorUtility.DisplayDialog("Validation Error", errorMessage, "OK");
                return;
            }
            
            bool confirm = EditorUtility.DisplayDialog(
                "Export Package",
                BuildExportConfirmationMessage(profile),
                "Export",
                "Cancel"
            );
            
            if (!confirm)
                return;
            
            isExporting = true;
            _progressContainer.style.display = DisplayStyle.Flex;
            ClearExportStepLog();
            UpdateProgress(0f, "Starting export...");
            UpdateBottomBar();
            
            try
            {
                var result = PackageBuilder.ExportPackage(profile, (progress, status) =>
                {
                    UpdateProgress(progress, status);
                });
                
                isExporting = false;
                _progressContainer.style.display = DisplayStyle.None;
                UpdateBottomBar();
                
                if (result.success)
                {
                    bool openFolder = EditorUtility.DisplayDialog(
                        "Export Successful",
                        $"Package exported successfully!\n\n" +
                        $"Package: {profile.packageName} v{profile.version}\n" +
                        $"Output: {result.outputPath}\n" +
                        $"Files: {result.filesExported}\n" +
                        $"Assemblies Obfuscated: {result.assembliesObfuscated}\n" +
                        $"Build Time: {result.buildTimeSeconds:F2}s",
                        "Open Folder",
                        "OK"
                    );
                    
                    if (openFolder)
                    {
                        EditorUtility.RevealInFinder(result.outputPath);
                    }
                    
                    LoadProfiles();
                    UpdateProfileDetails();
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Export Failed",
                        $"Export failed: {result.errorMessage}\n\n" +
                        "Check the console for more details.",
                        "OK"
                    );
                }
            }
            catch (Exception ex)
            {
                isExporting = false;
                _progressContainer.style.display = DisplayStyle.None;
                UpdateBottomBar();
                
                Debug.LogError($"[Package Exporter] Export failed: {ex.Message}");
                EditorUtility.DisplayDialog("Export Failed", $"An error occurred: {ex.Message}", "OK");
            }
        }

        private void ExportAllProfiles(List<ExportProfile> profilesToExport = null)
        {
            var profiles = profilesToExport ?? allProfiles;
            if (profiles.Count == 0)
                return;
            
            var invalidProfiles = new List<string>();
            foreach (var profile in profiles)
            {
                if (!profile.Validate(out string error))
                {
                    invalidProfiles.Add($"{profile.name}: {error}");
                }
            }
            
            if (invalidProfiles.Count > 0)
            {
                string message = "The following profiles have validation errors:\n\n" + string.Join("\n", invalidProfiles);
                EditorUtility.DisplayDialog("Validation Errors", message, "OK");
                return;
            }
            
            bool confirm = EditorUtility.DisplayDialog(
                "Export Profiles",
                $"This will export {profiles.Count} package(s):\n\n" +
                string.Join("\n", profiles.Select(p => $"• {p.packageName} v{p.version}")) +
                "\n\nThis may take several minutes.",
                "Export All",
                "Cancel"
            );
            
            if (!confirm)
                return;
            
            isExporting = true;
            _progressContainer.style.display = DisplayStyle.Flex;
            ClearExportStepLog();
            UpdateProgress(0f, $"Starting batch export ({profiles.Count} profile(s))...");
            UpdateBottomBar();
            
            try
            {
                var results = PackageBuilder.ExportMultiple(profiles, (index, total, progress, status) =>
                {
                    float overallProgress = (index + progress) / total;
                    UpdateProgress(overallProgress, $"[{index + 1}/{total}] {status}");
                });
                
                isExporting = false;
                _progressContainer.style.display = DisplayStyle.None;
                UpdateBottomBar();
                
                int successCount = results.Count(r => r.success);
                int failCount = results.Count - successCount;
                
                string summaryMessage = $"Batch export complete!\n\n" +
                                      $"Successful: {successCount}\n" +
                                      $"Failed: {failCount}\n\n";
                
                if (failCount > 0)
                {
                    var failures = results.Where(r => !r.success).ToList();
                    summaryMessage += "Failed profiles:\n" + string.Join("\n", failures.Select(r => $"• {r.errorMessage}"));
                }
                
                EditorUtility.DisplayDialog("Batch Export Complete", summaryMessage, "OK");
                
                LoadProfiles();
                UpdateProfileDetails();
            }
            catch (Exception ex)
            {
                isExporting = false;
                _progressContainer.style.display = DisplayStyle.None;
                UpdateBottomBar();
                
                Debug.LogError($"[Package Exporter] Batch export failed: {ex.Message}");
                EditorUtility.DisplayDialog("Batch Export Failed", $"An error occurred: {ex.Message}", "OK");
            }
        }

    }
}
