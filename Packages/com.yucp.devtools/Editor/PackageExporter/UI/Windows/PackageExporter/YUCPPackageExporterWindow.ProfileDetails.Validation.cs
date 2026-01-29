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
        private VisualElement CreateValidationSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.name = "validation-section";
            
            if (!profile.Validate(out string errorMessage))
            {
                var errorContainer = new VisualElement();
                errorContainer.AddToClassList("yucp-validation-error");
                
                var errorText = new Label($"Validation Error: {errorMessage}");
                errorText.AddToClassList("yucp-validation-error-text");
                errorContainer.Add(errorText);
                
                section.Add(errorContainer);
            }
            else
            {
                var successContainer = new VisualElement();
                successContainer.AddToClassList("yucp-validation-success");
                
                var successText = new Label("Profile is valid and ready to export");
                successText.AddToClassList("yucp-validation-success-text");
                successContainer.Add(successText);
                
                section.Add(successContainer);
            }
            
            // Add warnings section (non-critical issues)
            var warnings = CollectWarnings(profile);
            if (warnings.Count > 0)
            {
                var warningsContainer = new VisualElement();
                warningsContainer.AddToClassList("yucp-help-box");
                warningsContainer.style.backgroundColor = new Color(0.7f, 0.65f, 0.1f, 0.3f); // Yellow warning color
                warningsContainer.style.marginTop = 8;
                warningsContainer.style.borderLeftWidth = 3;
                warningsContainer.style.borderLeftColor = new Color(0.9f, 0.85f, 0.2f, 1f); // Yellow border
                
                var warningsLabel = new Label("Warnings:");
                warningsLabel.AddToClassList("yucp-label");
                warningsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                warningsLabel.style.marginBottom = 4;
                warningsContainer.Add(warningsLabel);
                
                foreach (var warning in warnings)
                {
                    var warningText = new Label($"• {warning}");
                    warningText.AddToClassList("yucp-help-box-text");
                    warningText.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.9f, 1f));
                    warningsContainer.Add(warningText);
                }
                
                section.Add(warningsContainer);
            }
            
            return section;
        }

        private List<string> CollectWarnings(ExportProfile profile)
        {
            var warnings = new List<string>();
            
            // Check for derived FBXs with missing origin files
            if (profile.discoveredAssets != null)
            {
                foreach (var asset in profile.discoveredAssets)
                {
                    if (IsDerivedFbx(asset.assetPath, out DerivedSettings settings, out string basePath))
                    {
                        string assetName = Path.GetFileName(asset.assetPath);
                        
                        if (string.IsNullOrEmpty(basePath))
                        {
                            warnings.Add($"Derived FBX '{assetName}' is missing its origin file");
                        }
                        else
                        {
                            // Check if the base file exists (basePath from AssetDatabase is relative)
                            string baseAssetPath = basePath;
                            if (!Path.IsPathRooted(baseAssetPath))
                            {
                                string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                                baseAssetPath = Path.GetFullPath(Path.Combine(projectPath, baseAssetPath));
                            }
                            
                            if (!File.Exists(baseAssetPath))
                            {
                                warnings.Add($"Derived FBX '{assetName}' origin file not found: {basePath}");
                            }
                        }
                    }
                }
            }
            
            // Check for problematic dependencies
            if (profile.dependencies != null)
            {
                foreach (var dep in profile.dependencies)
                {
                    if (dep.enabled)
                    {
                        string packageName = dep.packageName ?? "";
                        string displayName = dep.displayName ?? "";
                        string combinedName = (packageName + " " + displayName).ToLower();
                        
                        // Check for Temp/Temporary (but not template)
                        bool hasTemp = (combinedName.Contains(" temp ") || combinedName.StartsWith("temp ") || 
                                       combinedName.EndsWith(" temp") || combinedName == "temp" || 
                                       combinedName.Contains("temporary")) && !combinedName.Contains("template");
                        
                        // Check for YUCP Dev tools
                        bool hasDevTools = combinedName.Contains("yucp dev tools") || combinedName.Contains("yucp.devtools");
                        
                        if (hasTemp)
                        {
                            string depName = string.IsNullOrEmpty(displayName) ? packageName : displayName;
                            warnings.Add($"Dependency '{depName}' contains 'Temp' or 'Temporary' - not recommended for general distribution");
                        }
                        
                        if (hasDevTools)
                        {
                            string depName = string.IsNullOrEmpty(displayName) ? packageName : displayName;
                            warnings.Add($"Dependency '{depName}' contains 'YUCP Dev tools' - not recommended for general distribution");
                        }
                    }
                }
            }
            
            // Check for composite profile issues
            if (profile.HasIncludedProfiles())
            {
                List<ExportProfile> resolved;
                List<string> cycles;
                CompositeProfileResolver.ResolveIncludedProfiles(profile, out resolved, out cycles);
                
                // Cycle warnings
                if (cycles.Count > 0)
                {
                    foreach (string cycle in cycles)
                    {
                        warnings.Add($"Cycle detected in bundled profiles: {cycle}");
                    }
                }
                
                // Missing profile warnings
                var bundledProfiles = profile.GetIncludedProfiles();
                var allGuids = new List<string>();
                var profileType = typeof(ExportProfile);
                var guidField = profileType.GetField("includedProfileGuids", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (guidField != null)
                {
                    var guids = guidField.GetValue(profile) as List<string>;
                    if (guids != null)
                    {
                        allGuids = guids;
                    }
                }
                
                var resolvedGuids = new HashSet<string>();
                foreach (var resolvedProfile in resolved)
                {
                    if (resolvedProfile != null)
                    {
                        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(resolvedProfile));
                        if (!string.IsNullOrEmpty(guid))
                        {
                            resolvedGuids.Add(guid);
                        }
                    }
                }
                
                int missingCount = 0;
                foreach (string guid in allGuids)
                {
                    if (!string.IsNullOrEmpty(guid) && !resolvedGuids.Contains(guid))
                    {
                        missingCount++;
                    }
                }
                
                if (missingCount > 0)
                {
                    warnings.Add($"{missingCount} bundled profile(s) are missing or deleted");
                }
                
                // Bundled profile validation errors
                foreach (var bundledProfile in bundledProfiles)
                {
                    if (bundledProfile != null)
                    {
                        if (!bundledProfile.Validate(out string validationError))
                        {
                            warnings.Add($"Bundled profile '{bundledProfile.packageName}' has errors: {validationError}");
                        }
                    }
                }
                
                // Asset conflict warnings (if we have discovered assets)
                if (profile.discoveredAssets != null && profile.discoveredAssets.Count > 0)
                {
                    var assetSourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var assetCounts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    
                    foreach (var asset in profile.discoveredAssets)
                    {
                        if (!string.IsNullOrEmpty(asset.sourceProfileName))
                        {
                            string normalizedPath = asset.assetPath.Replace('\\', '/');
                            if (!assetCounts.ContainsKey(normalizedPath))
                            {
                                assetCounts[normalizedPath] = new List<string>();
                            }
                            assetCounts[normalizedPath].Add(asset.sourceProfileName);
                        }
                    }
                    
                    foreach (var kvp in assetCounts)
                    {
                        if (kvp.Value.Count > 1)
                        {
                            var uniqueSources = kvp.Value.Distinct().ToList();
                            if (uniqueSources.Count > 1)
                            {
                                string assetName = Path.GetFileName(kvp.Key);
                                warnings.Add($"Asset '{assetName}' appears in multiple bundled profiles: {string.Join(", ", uniqueSources)}");
                            }
                        }
                    }
                }
            }
            
            return warnings;
        }

        private void UpdateValidationDisplay(ExportProfile profile)
        {
            var validationSection = _profileDetailsContainer?.Q("validation-section");
            if (validationSection != null && profile != null)
            {
                var parent = validationSection.parent;
                var index = parent.IndexOf(validationSection);
                parent.Remove(validationSection);
                parent.Insert(index, CreateValidationSection(profile));
            }
        }
        
        private static int GetTriangleCountForOwnershipTarget(string assetPath, string targetMeshName)
        {
            try
            {
                string unityPath = assetPath.Replace('\\', '/');
                if (Path.IsPathRooted(unityPath))
                {
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
                    if (unityPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        unityPath = unityPath.Substring(projectPath.Length).TrimStart('/');
                    }
                }
                
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(unityPath);
                if (prefab == null) return 0;
                
                Mesh target = FindTargetMeshForOwnership(prefab, targetMeshName);
                if (target == null) return 0;
                
                int count = 0;
                for (int s = 0; s < target.subMeshCount; s++)
                {
                    count += target.GetTriangles(s).Length / 3;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }
        
        private static Mesh FindTargetMeshForOwnership(GameObject prefab, string targetMeshName)
        {
            if (prefab == null) return null;
            
            if (!string.IsNullOrEmpty(targetMeshName))
            {
                foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh != null && smr.sharedMesh.name == targetMeshName)
                        return smr.sharedMesh;
                }
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh != null && mf.sharedMesh.name == targetMeshName)
                        return mf.sharedMesh;
                }
            }
            
            var firstSmr = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (firstSmr != null && firstSmr.sharedMesh != null)
                return firstSmr.sharedMesh;
            
            var firstMf = prefab.GetComponentInChildren<MeshFilter>(true);
            return firstMf != null ? firstMf.sharedMesh : null;
        }

    }
}
