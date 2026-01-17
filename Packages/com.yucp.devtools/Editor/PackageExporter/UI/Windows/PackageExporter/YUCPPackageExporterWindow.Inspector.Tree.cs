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


        private VisualElement CreateFolderHeader(FolderTreeNode node, ExportProfile profile, int depth)
        {
            var folderHeader = new VisualElement();
            folderHeader.AddToClassList("yucp-inspector-folder-header");
            folderHeader.style.paddingLeft = 12 + (depth * 24);
            
            var folderHeaderContent = new VisualElement();
            folderHeaderContent.AddToClassList("yucp-inspector-folder-content");
            
            // Expand/collapse button
            var expandButton = new Button(() =>
            {
                node.IsExpanded = !node.IsExpanded;
                folderExpandedStates[node.FullPath] = node.IsExpanded;
                
                VisualElement scrollView = folderHeader.parent;
                if (scrollView != null && scrollView is ScrollView)
                {
                    // Store current scroll position
                    float scrollValue = (scrollView as ScrollView).verticalScroller.value;
                    
                    VisualElement container = scrollView.parent;
                    if (container != null && container.name == "asset-list-container")
                    {
                        container.Clear();
                        RebuildAssetList(profile, container);
                        
                        // Restore scroll position after layout update
                        EditorApplication.delayCall += () =>
                        {
                            var newScrollView = container.Q<ScrollView>();
                            if (newScrollView != null)
                            {
                                newScrollView.verticalScroller.value = scrollValue;
                            }
                        };
                    }
                }
            });
            expandButton.AddToClassList("yucp-folder-expand-button");
            expandButton.text = node.IsExpanded ? "▼" : "▶";
            folderHeaderContent.Add(expandButton);
            
            var folderLabel = new Label(node.Name);
            folderLabel.AddToClassList("yucp-inspector-folder-label");
            folderLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left click
                {
                    PingAsset(node.FullPath);
                }
            });
            folderHeaderContent.Add(folderLabel);
            
            // Asset count badge
            int totalAssets = CountTotalAssets(node);
            if (totalAssets > 0)
            {
                var countBadge = new Label($"({totalAssets})");
                countBadge.AddToClassList("yucp-folder-count-badge");
                folderHeaderContent.Add(countBadge);
            }
            
            // Actions toolbar
            var folderActions = new VisualElement();
            folderActions.AddToClassList("yucp-inspector-folder-actions");
            
            // .yucpignore Create/Edit button
            // Convert relative path (e.g., "Assets/Folder") to absolute path
            // node.FullPath should be relative like "Assets/Folder/Subfolder"
            string folderFullPath;
            
            // Get project root (parent of Assets folder)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            
            // Normalize node.FullPath - ensure it uses forward slashes
            string pathToProcess = node.FullPath.Replace('\\', '/');
            
            // Extract the relative part starting from "Assets/"
            // This handles cases where the path might already be absolute or have duplicates
            string relativePath = pathToProcess;
            
            // Find the last occurrence of "Assets/" in the path (in case of duplicates)
            int lastAssetsIndex = pathToProcess.LastIndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (lastAssetsIndex >= 0)
            {
                // Extract everything from "Assets/" onwards
                relativePath = pathToProcess.Substring(lastAssetsIndex);
            }
            else
            {
                // No "Assets/" found - check if it starts with "Assets" (without slash)
                int assetsIndex = pathToProcess.LastIndexOf("Assets", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex >= 0)
                {
                    // Extract from "Assets" and ensure it has a slash
                    relativePath = pathToProcess.Substring(assetsIndex);
                    if (!relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = "Assets/" + relativePath.Substring(6).TrimStart('/');
                    }
                }
                else
                {
                    // Doesn't contain "Assets" at all - prepend it
                    relativePath = "Assets/" + pathToProcess.TrimStart('/');
                }
            }
            
            // Final safety check: if relativePath still contains a drive letter, extract just the Assets part
            if (relativePath.Contains(":"))
            {
                int assetsIndex = relativePath.LastIndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex >= 0)
                {
                    relativePath = relativePath.Substring(assetsIndex);
                }
            }
            
            // Combine with project root to get absolute path
            // Use Path.Combine which will handle the case where relativePath is already absolute
            folderFullPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
            
            // Final validation: if the result still contains duplicates, fix it
            string expectedAssetsPath = Path.Combine(projectRoot, "Assets");
            if (folderFullPath.Contains(expectedAssetsPath + Path.DirectorySeparatorChar + expectedAssetsPath))
            {
                // Remove the duplicate
                int dupIndex = folderFullPath.IndexOf(expectedAssetsPath + Path.DirectorySeparatorChar + expectedAssetsPath);
                folderFullPath = folderFullPath.Substring(0, dupIndex + expectedAssetsPath.Length) + 
                               folderFullPath.Substring(dupIndex + expectedAssetsPath.Length + expectedAssetsPath.Length);
            }
            
            bool hasIgnoreFile = YucpIgnoreHandler.HasIgnoreFile(folderFullPath);
            
            if (hasIgnoreFile)
            {
                var editIgnoreButton = new Button(() => OpenYucpIgnoreFile(folderFullPath)) { text = "Edit .yucpignore" };
                editIgnoreButton.AddToClassList("yucp-button");
                editIgnoreButton.AddToClassList("yucp-button-small");
                folderActions.Add(editIgnoreButton);
            }
            
            var ignoreButton = new Button(() => AddFolderToIgnoreList(profile, node.FullPath)) { text = "Add to Ignore" };
            ignoreButton.AddToClassList("yucp-button");
            ignoreButton.AddToClassList("yucp-button-small");
            folderActions.Add(ignoreButton);
            
            folderHeaderContent.Add(folderActions);
            folderHeader.Add(folderHeaderContent);
            
            return folderHeader;
        }

        private FolderTreeNode BuildFolderTree(List<DiscoveredAsset> assets)
        {
            var root = new FolderTreeNode("Assets", "Assets");
            root.IsExpanded = true;
            
            foreach (var asset in assets)
            {
                string folderPath = asset.GetFolderPath();
                if (string.IsNullOrEmpty(folderPath) || folderPath == "Assets")
                {
                    root.Assets.Add(asset);
                    continue;
                }
                
                // Split path into segments
                string[] segments = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments[0] == "Assets")
                {
                    segments = segments.Skip(1).ToArray();
                }
                
                FolderTreeNode current = root;
                string currentPath = "Assets";
                
                foreach (string segment in segments)
                {
                    currentPath = currentPath == "Assets" ? $"Assets/{segment}" : $"{currentPath}/{segment}";
                    
                    var child = current.Children.FirstOrDefault(c => c.Name == segment);
                    if (child == null)
                    {
                        child = new FolderTreeNode(segment, currentPath);
                        // Default to expanded for better navigation, unless user has collapsed it
                        child.IsExpanded = folderExpandedStates.ContainsKey(currentPath) 
                            ? folderExpandedStates[currentPath] 
                            : true;
                        current.Children.Add(child);
                    }
                    else
                    {
                        // Ensure existing nodes respect saved state
                        if (folderExpandedStates.ContainsKey(currentPath))
                        {
                            child.IsExpanded = folderExpandedStates[currentPath];
                        }
                    }
                    
                    current = child;
                }
                
                current.Assets.Add(asset);
            }
            
            // Sort children and assets
            SortFolderTree(root);
            
            return root;
        }

        private void SortFolderTree(FolderTreeNode node)
        {
            node.Children = node.Children.OrderBy(c => c.Name).ToList();
            node.Assets = node.Assets.OrderBy(a => a.GetDisplayName()).ToList();
            
            foreach (var child in node.Children)
            {
                SortFolderTree(child);
            }
        }

        private void RenderFolderTree(FolderTreeNode node, VisualElement container, ExportProfile profile, int depth)
        {
            // Only render if node has assets or children
            if (node.Assets.Count == 0 && node.Children.Count == 0 && depth > 0)
                return;
            
            if (depth > 0 || (depth == 0 && node.Assets.Count > 0))
            {
                if (depth > 0)
                {
                    var folderHeader = CreateFolderHeader(node, profile, depth);
                    container.Add(folderHeader);
                }
            }
            
            if (node.Assets.Count > 0 && (depth == 0 || node.IsExpanded))
            {
                foreach (var asset in node.Assets)
                {
                    var assetItem = CreateAssetItem(asset, profile, depth == 0 ? 0 : depth + 1);
                    container.Add(assetItem);
                }
            }
            
            if (node.Children.Count > 0 && (depth == 0 || node.IsExpanded))
            {
                foreach (var child in node.Children)
                {
                    RenderFolderTree(child, container, profile, depth + 1);
                }
            }
        }

        private int CountTotalAssets(FolderTreeNode node)
        {
            int count = node.Assets.Count;
            foreach (var child in node.Children)
            {
                count += CountTotalAssets(child);
            }
            return count;
        }

        private VisualElement CreateAssetItem(DiscoveredAsset asset, ExportProfile profile, int depth)
        {
            var assetItem = new VisualElement();
            assetItem.AddToClassList("yucp-asset-item");
            assetItem.style.paddingLeft = 12 + (depth * 24);
            
            // Checkbox
            var checkbox = new Toggle { value = asset.included };
            checkbox.AddToClassList("yucp-asset-item-checkbox");
            checkbox.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(profile, "Toggle Asset Inclusion");
                asset.included = evt.newValue;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            });
            assetItem.Add(checkbox);
            
            // Icon
            var icon = new Label(GetAssetTypeIcon(asset.assetType));
            icon.AddToClassList("yucp-asset-item-icon");
            assetItem.Add(icon);
            
            // Name - make it clickable to navigate to asset
            var nameLabel = new Label(asset.GetDisplayName());
            nameLabel.AddToClassList("yucp-asset-item-name");
            // Compute derived flag once for this row
            DerivedSettings derivedSettings;
            string derivedBasePath;
            bool isDerivedFbx = IsDerivedFbx(asset.assetPath, out derivedSettings, out derivedBasePath);
            if (asset.isDependency)
            {
                nameLabel.text += " [Dep]";
            }
            if (isDerivedFbx)
            {
                nameLabel.text += string.IsNullOrEmpty(derivedBasePath) ? " [Derived: Base Missing]" : " [Derived]";
            }
            
            // Add click handler to navigate to asset
            nameLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left click
                {
                    PingAsset(asset.assetPath);
                }
            });
            assetItem.Add(nameLabel);
            
            // Type
            string typeText = asset.assetType;
            if (isDerivedFbx && typeText.Equals("Model", StringComparison.OrdinalIgnoreCase))
            {
                typeText = "Model (Derived)";
            }
            var typeLabel = new Label(typeText);
            typeLabel.AddToClassList("yucp-asset-item-type");
            assetItem.Add(typeLabel);
            
            // Size
            if (!asset.isFolder && asset.fileSize > 0)
            {
                var sizeLabel = new Label(FormatBytes(asset.fileSize));
                sizeLabel.AddToClassList("yucp-asset-item-size");
                assetItem.Add(sizeLabel);
            }
            
            // Source profile badge (for composite profiles)
            if (!string.IsNullOrEmpty(asset.sourceProfileName))
            {
                var sourceBadge = new Label($"[{asset.sourceProfileName}]");
                sourceBadge.AddToClassList("yucp-label-secondary");
                sourceBadge.style.marginLeft = 6;
                sourceBadge.style.fontSize = 10;
                sourceBadge.style.color = new Color(0.6f, 0.8f, 0.9f);
                assetItem.Add(sourceBadge);
            }
            
            // Derived patch badge and quick actions for FBX
            if (isDerivedFbx)
            {
                var badge = new Label(string.IsNullOrEmpty(derivedBasePath) ? "[Derived Patch: Base Missing]" : "[Derived Patch]");
                badge.AddToClassList("yucp-label-secondary");
                badge.style.marginLeft = 6;
                badge.style.color = string.IsNullOrEmpty(derivedBasePath) ? new Color(0.95f, 0.75f, 0.2f) : new Color(0.2f, 0.8f, 0.8f);
                assetItem.Add(badge);
                
                var actionsRow = new VisualElement();
                actionsRow.style.flexDirection = FlexDirection.Row;
                actionsRow.style.marginLeft = 6;
                
                var optionsButton = new Button(() =>
                {
                    string relativePath = asset.assetPath;
                    if (Path.IsPathRooted(relativePath))
                    {
                        string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                        if (relativePath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                        {
                            relativePath = relativePath.Substring(projectPath.Length).Replace('\\', '/').TrimStart('/');
                        }
                    }
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }) { text = "Options" };
                optionsButton.AddToClassList("yucp-button");
                optionsButton.AddToClassList("yucp-button-small");
                actionsRow.Add(optionsButton);
                
                var clearButton = new Button(() =>
                {
                    string relativePath = asset.assetPath;
                    if (Path.IsPathRooted(relativePath))
                    {
                        string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                        if (relativePath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                        {
                            relativePath = relativePath.Substring(projectPath.Length).Replace('\\', '/').TrimStart('/');
                        }
                    }
                    var importer = AssetImporter.GetAtPath(relativePath) as ModelImporter;
                    if (importer != null)
                    {
                        try
                        {
                            var s = string.IsNullOrEmpty(importer.userData) ? new DerivedSettings() : JsonUtility.FromJson<DerivedSettings>(importer.userData) ?? new DerivedSettings();
                            s.isDerived = false;
                            importer.userData = JsonUtility.ToJson(s);
                            importer.SaveAndReimport();
                            UpdateProfileDetails();
                        }
                        catch { /* ignore */ }
                    }
                }) { text = "Clear" };
                clearButton.AddToClassList("yucp-button");
                clearButton.AddToClassList("yucp-button-small");
                actionsRow.Add(clearButton);
                
                assetItem.Add(actionsRow);
            }
            
            return assetItem;
        }

    }
}
