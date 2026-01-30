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
        private void RebuildAssetList(ExportProfile profile, VisualElement container)
        {
            // Filter assets
            var filteredAssets = profile.discoveredAssets.AsEnumerable();
            
            // Respect the Include Dependencies toggle - filter out dependencies if toggle is off
            if (!profile.includeDependencies)
            {
                filteredAssets = filteredAssets.Where(a => !a.isDependency);
            }
            
            if (!string.IsNullOrWhiteSpace(inspectorSearchFilter))
            {
                filteredAssets = filteredAssets.Where(a => 
                    a.assetPath.IndexOf(inspectorSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            
            if (showOnlyIncluded)
                filteredAssets = filteredAssets.Where(a => a.included);
            
            if (showOnlyExcluded)
                filteredAssets = filteredAssets.Where(a => !a.included);
			
			if (showOnlyDerived)
				filteredAssets = filteredAssets.Where(a => IsDerivedFbx(a.assetPath, out _, out _));
            
            // Filter by source profile (for composite profiles)
            if (!string.IsNullOrEmpty(sourceProfileFilter) && sourceProfileFilter != "All")
            {
                filteredAssets = filteredAssets.Where(a =>
                {
                    // If asset has source profile, match it
                    if (!string.IsNullOrEmpty(a.sourceProfileName))
                    {
                        return a.sourceProfileName == sourceProfileFilter;
                    }
                    // If no source profile, it's from parent
                    return sourceProfileFilter == profile.packageName;
                });
            }
            
            var filteredList = filteredAssets.ToList();
            
            // Asset list scrollview - make it fill the container
            var assetListScroll = new ScrollView();
            assetListScroll.AddToClassList("yucp-inspector-list");
            assetListScroll.style.flexGrow = 1; // Fill available space
            assetListScroll.style.flexShrink = 1; // Allow shrinking
            assetListScroll.style.width = Length.Percent(100); // Full width
            assetListScroll.style.height = Length.Percent(100); // Full height of container
            assetListScroll.style.overflow = Overflow.Hidden; // Prevent horizontal scrolling
            assetListScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden; // Hide horizontal scrollbar
            // Override CSS max-height constraint to allow unlimited resizing
            assetListScroll.style.maxHeight = new StyleLength(StyleKeyword.None);
            
            if (filteredList.Count == 0)
            {
                var emptyLabel = new Label("No assets match the current filters.");
                emptyLabel.AddToClassList("yucp-label-secondary");
                emptyLabel.style.paddingTop = 20;
                emptyLabel.style.paddingBottom = 20;
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                assetListScroll.Add(emptyLabel);
            }
            else
            {
                // Build hierarchical folder tree
                var rootNode = BuildFolderTree(filteredList.Where(a => !a.isFolder).ToList());
                
                // Render the tree
                RenderFolderTree(rootNode, assetListScroll, profile, 0);
            }
            
            container.Add(assetListScroll);
        }

        private void PingAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            // Normalize the path - ensure it's relative to Assets
            string relativePath = assetPath.Replace('\\', '/');
            if (Path.IsPathRooted(relativePath))
            {
                int assetsIndex = relativePath.LastIndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex >= 0)
                {
                    relativePath = relativePath.Substring(assetsIndex);
                }
                else
                {
                    string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    if (relativePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = relativePath.Substring(projectRoot.Length).Replace('\\', '/').TrimStart('/');
                    }
                }
            }

            if (!relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                if (relativePath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                    relativePath = "Assets/" + relativePath.Substring(6).TrimStart('/');
                else
                    relativePath = "Assets/" + relativePath.TrimStart('/');
            }

            // Folders and files: LoadAssetAtPath works for both
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }

    }
}
