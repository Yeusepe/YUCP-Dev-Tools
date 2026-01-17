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
            
            // Update export selected button
            _exportSelectedButton.SetEnabled(selectedProfileIndices.Count > 0 && !isExporting);
            if (selectedProfileIndices.Count == 1)
            {
                _exportSelectedButton.text = "Export Selected Profile";
            }
            else if (selectedProfileIndices.Count > 1)
            {
                _exportSelectedButton.text = $"Export Selected Profiles ({selectedProfileIndices.Count})";
            }
            else
            {
                _exportSelectedButton.text = "Export Selected";
            }
            
            // Update export all button
            _exportAllButton.SetEnabled(allProfiles.Count > 0 && !isExporting);
        }

        private void UpdateProgress(float progress, string status)
        {
            currentProgress = progress;
            currentStatus = status;
            
            _progressFill.style.width = Length.Percent(progress * 100);
            _progressText.text = $"{(progress * 100):F0}% - {status}";
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

        private void ExportProfile(ExportProfile profile)
        {
            if (profile == null)
                return;
            
            if (!profile.Validate(out string errorMessage))
            {
                EditorUtility.DisplayDialog("Validation Error", errorMessage, "OK");
                return;
            }
            
            string foldersList = profile.foldersToExport.Count > 0 
                ? string.Join("\n", profile.foldersToExport.Take(5)) + (profile.foldersToExport.Count > 5 ? $"\n... and {profile.foldersToExport.Count - 5} more" : "")
                : "None configured";
            
            int bundledDeps = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Bundle);
            int refDeps = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Dependency);
            
            bool confirm = EditorUtility.DisplayDialog(
                "Export Package",
                $"Export package: {profile.packageName} v{profile.version}\n\n" +
                $"Export Folders ({profile.foldersToExport.Count}):\n{foldersList}\n\n" +
                $"Dependencies:\n" +
                $"  Bundled: {bundledDeps}\n" +
                $"  Referenced: {refDeps}\n\n" +
                $"Obfuscation: {(profile.enableObfuscation ? $"Enabled ({profile.obfuscationPreset}, {profile.assembliesToObfuscate.Count(a => a.enabled)} assemblies)" : "Disabled")}\n\n" +
                $"Output: {profile.GetOutputFilePath()}",
                "Export",
                "Cancel"
            );
            
            if (!confirm)
                return;
            
            isExporting = true;
            _progressContainer.style.display = DisplayStyle.Flex;
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
