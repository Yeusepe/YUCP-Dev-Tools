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
        private void ScanProfileDependencies(ExportProfile profile, bool silent = false)
        {
            if (!silent)
            {
                EditorUtility.DisplayProgressBar("Scanning Dependencies", "Finding installed packages...", 0.3f);
            }
            
            try
            {
                var foundPackages = DependencyScanner.ScanInstalledPackages();
                
                if (foundPackages.Count == 0)
                {
                    if (!silent)
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog("No Packages Found", 
                            "No installed packages were found in the project.", 
                            "OK");
                    }
                    return;
                }
                
                if (!silent)
                {
                    EditorUtility.DisplayProgressBar("Scanning Dependencies", "Processing packages...", 0.6f);
                }
                
                // Preserve existing dependency settings before clearing (including custom/manual entries)
                var existingSettings = new Dictionary<string, PackageDependency>(StringComparer.OrdinalIgnoreCase);
                foreach (var existingDep in profile.dependencies)
                {
                    if (!string.IsNullOrEmpty(existingDep.packageName))
                    {
                        existingSettings[existingDep.packageName] = existingDep;
                    }
                }
                
                profile.dependencies.Clear();
                
                // Normalize package name for comparison (same normalization as in GeneratePackageJson)
                string normalizedPackageName = profile.packageName.ToLower().Replace(" ", ".");
                
                var dependencies = DependencyScanner.ConvertToPackageDependencies(foundPackages);
                foreach (var dep in dependencies)
                {
                    // Skip the package itself - prevent self-referential dependencies
                    if (string.Equals(dep.packageName, normalizedPackageName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    if (existingSettings.TryGetValue(dep.packageName, out var settings))
                    {
                        dep.enabled = settings.enabled;
                        dep.exportMode = settings.exportMode;
                        dep.isVpmDependency = settings.isVpmDependency;
                        dep.packageVersion = string.IsNullOrEmpty(dep.packageVersion) ? settings.packageVersion : dep.packageVersion;
                        dep.displayName = string.IsNullOrEmpty(dep.displayName) ? settings.displayName : dep.displayName;
                        dep.vpmRepositoryUrl = settings.vpmRepositoryUrl;
                    }
                    profile.dependencies.Add(dep);
                }
                
                // Re-add any manual/custom dependencies not found in installed packages
                foreach (var existing in existingSettings.Values)
                {
                    if (string.IsNullOrEmpty(existing.packageName))
                        continue;
                    
                    bool exists = profile.dependencies.Any(d =>
                        string.Equals(d.packageName, existing.packageName, StringComparison.OrdinalIgnoreCase));
                    
                    if (!exists)
                    {
                        profile.dependencies.Add(existing);
                    }
                }
                
                if (!profile.dependencies.Any(d => d.packageName == "com.yucp.devtools"))
                {
                    var devtoolsDep = new PackageDependency("com.yucp.devtools", "*", "YUCP Dev Tools", false);
                    profile.dependencies.Add(devtoolsDep);
                }
                
                if (!silent)
                {
                    EditorUtility.DisplayProgressBar("Scanning Dependencies", "Auto-detecting usage...", 0.8f);
                }
                
                if (profile.foldersToExport.Count > 0)
                {
                    DependencyScanner.AutoDetectUsedDependencies(profile);
                }
                
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                
                if (!silent)
                {
                    int vpmCount = dependencies.Count(d => d.isVpmDependency);
                    int autoEnabled = dependencies.Count(d => d.enabled);
                    
                    string message = $"Found {dependencies.Count} packages:\n\n" +
                                   $"• {vpmCount} VRChat (VPM) packages\n" +
                                   $"• {dependencies.Count - vpmCount} Unity packages\n" +
                                   $"• {autoEnabled} auto-enabled (detected in use)\n\n" +
                                   "Dependencies detected in your export folders have been automatically enabled.";
                    
                    EditorUtility.DisplayDialog("Scan Complete", message, "OK");
                }
                
                UpdateProfileDetails();
            }
            finally
            {
                if (!silent)
                {
                    EditorUtility.ClearProgressBar();
                }
            }
        }

        private void AddFolderToIgnoreList(ExportProfile profile, string folderPath)
        {
            if (profile == null || string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            string unityPath = NormalizeUnityPathForIgnore(folderPath);
            string storedPath = !string.IsNullOrEmpty(unityPath) ? unityPath : Path.GetFullPath(folderPath);
            string folderGuid = !string.IsNullOrEmpty(unityPath) ? AssetDatabase.AssetPathToGUID(unityPath) : null;

            var ignoreFolders = profile.PermanentIgnoreFolders;
            bool alreadyIgnored = false;

            for (int i = 0; i < ignoreFolders.Count; i++)
            {
                if (ignoreFolders[i].Equals(storedPath, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyIgnored = true;
                    break;
                }

                if (i < profile.PermanentIgnoreFolderGuids.Count &&
                    !string.IsNullOrEmpty(profile.PermanentIgnoreFolderGuids[i]) &&
                    !string.IsNullOrEmpty(folderGuid) &&
                    profile.PermanentIgnoreFolderGuids[i].Equals(folderGuid, StringComparison.OrdinalIgnoreCase))
                {
                    ignoreFolders[i] = storedPath;
                    alreadyIgnored = true;
                    break;
                }
            }

            if (!alreadyIgnored)
            {
                ignoreFolders.Add(storedPath);
                profile.PermanentIgnoreFolderGuids.Add(folderGuid ?? "");
                EditorUtility.SetDirty(profile);

                // Automatically rescan without popup
                ScanAssetsForInspector(profile, silent: true);
            }
            else
            {
                EditorUtility.DisplayDialog("Already Ignored", $"'{storedPath}' is already in the ignore list.", "OK");
            }
        }

        private static string NormalizeUnityPathForIgnore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string normalized = path.Replace('\\', '/').Trim();

            if (normalized.Equals("Assets/Packages", StringComparison.OrdinalIgnoreCase))
            {
                return "Packages";
            }

            if (normalized.StartsWith("Assets/Packages/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "Packages/" + normalized.Substring("Assets/Packages/".Length);
            }

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Packages", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            if (Path.IsPathRooted(normalized))
            {
                string unityPath = GetUnityPathFromFullPath(normalized);
                if (!string.IsNullOrEmpty(unityPath))
                {
                    return unityPath;
                }
            }

            return null;
        }

        private static string GetUnityPathFromFullPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return null;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            fullPath = Path.GetFullPath(fullPath);

            if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = fullPath.Substring(projectRoot.Length).TrimStart('\\', '/');
                return relativePath.Replace("\\", "/");
            }

            string packageCacheRoot = Path.Combine(projectRoot, "Library", "PackageCache");
            if (fullPath.StartsWith(packageCacheRoot, StringComparison.OrdinalIgnoreCase))
            {
                string relative = fullPath.Substring(packageCacheRoot.Length).TrimStart('\\', '/');
                string[] parts = relative.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    return null;
                }

                string packageFolder = parts[0];
                int atIndex = packageFolder.IndexOf('@');
                string packageName = atIndex > 0 ? packageFolder.Substring(0, atIndex) : packageFolder;

                string remainder = relative.Substring(packageFolder.Length).TrimStart('\\', '/');
                if (string.IsNullOrEmpty(remainder))
                {
                    return $"Packages/{packageName}";
                }

                return $"Packages/{packageName}/{remainder.Replace("\\", "/")}";
            }

            return null;
        }

        private void RemoveFromIgnoreList(ExportProfile profile, string folderPath)
        {
            var ignoreFolders = profile.PermanentIgnoreFolders;
            if (ignoreFolders != null && ignoreFolders.Contains(folderPath))
            {
                ignoreFolders.Remove(folderPath);
                EditorUtility.SetDirty(profile);
                                
                ScanAssetsForInspector(profile, silent: true);
            }
        }

        private void AutoDetectUsedDependencies(ExportProfile profile)
        {
            if (profile.dependencies.Count == 0)
            {
                EditorUtility.DisplayDialog("No Dependencies", 
                    "Scan for installed packages first before auto-detecting.", 
                    "OK");
                return;
            }
            
            EditorUtility.DisplayProgressBar("Auto-Detecting Dependencies", "Scanning assets...", 0.5f);
            
            try
            {
                DependencyScanner.AutoDetectUsedDependencies(profile);
                
                EditorUtility.ClearProgressBar();
                
                int enabledCount = profile.dependencies.Count(d => d.enabled);
                int disabledCount = profile.dependencies.Count - enabledCount;
                
                string message = $"Auto-detection complete!\n\n" +
                               $"• {enabledCount} dependencies enabled (used in export)\n" +
                               $"• {disabledCount} dependencies disabled (not used)\n\n" +
                               "Review the dependency list and adjust as needed.";
                
                EditorUtility.DisplayDialog("Auto-Detection Complete", message, "OK");
                
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

    }
}
