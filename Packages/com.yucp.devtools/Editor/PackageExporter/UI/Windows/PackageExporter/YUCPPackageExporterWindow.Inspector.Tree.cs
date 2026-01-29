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
        private const int TREE_INDENT_WIDTH = 16;
        private const int TREE_MAX_DEPTH_COLORS = 16;

        private VisualElement CreateFolderHeader(FolderTreeNode node, ExportProfile profile, int depth, bool isLastChild = false)
        {
            // Main row container
            var row = new VisualElement();
            row.AddToClassList("yucp-tree-row");
            row.AddToClassList("yucp-tree-folder-row");
            row.AddToClassList($"yucp-tree-depth-{Mathf.Min(depth, TREE_MAX_DEPTH_COLORS)}");
            if (isLastChild)
                row.AddToClassList("yucp-tree-last-child");





            // Content container
            var content = new VisualElement();
            content.AddToClassList("yucp-tree-content");

            // Expand/collapse button
            var expandButton = new Button(() =>
            {
                node.IsExpanded = !node.IsExpanded;
                folderExpandedStates[node.FullPath] = node.IsExpanded;

                VisualElement container = FindAssetListContainer(row);
                if (container != null)
                {
                    float scrollValue = 0f;
                    var scrollView = container.Q<ScrollView>();
                    if (scrollView != null)
                        scrollValue = scrollView.verticalScroller.value;

                    container.Clear();
                    RebuildAssetList(profile, container);

                    EditorApplication.delayCall += () =>
                    {
                        var newScrollView = container.Q<ScrollView>();
                        if (newScrollView != null)
                        {
                            newScrollView.verticalScroller.value = scrollValue;
                        }
                    };
                }
            });
            expandButton.AddToClassList("yucp-tree-expand-btn");
            expandButton.text = node.IsExpanded ? "▾" : "▸";
            content.Add(expandButton);

            // Folder icon using Unity's built-in icons
            var folderIcon = new Image();
            folderIcon.AddToClassList("yucp-tree-icon");
            string iconName = node.IsExpanded ? "d_FolderOpened Icon" : "d_Folder Icon";
            folderIcon.image = EditorGUIUtility.IconContent(iconName).image;
            content.Add(folderIcon);

            // Folder name label
            var folderLabel = new Label(node.Name);
            folderLabel.AddToClassList("yucp-tree-label");
            folderLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    evt.StopPropagation();
                    evt.PreventDefault();
                    PingAsset(node.FullPath);
                }
            });
            folderLabel.pickingMode = PickingMode.Position;
            content.Add(folderLabel);

            // Asset count badge
            int totalAssets = CountTotalAssets(node);
            if (totalAssets > 0)
            {
                var countBadge = new Label($"{totalAssets}");
                countBadge.AddToClassList("yucp-tree-badge");
                content.Add(countBadge);
            }

            // Spacer to push actions to the right
            var spacer = new VisualElement();
            spacer.AddToClassList("yucp-tree-spacer");
            content.Add(spacer);

            // Actions container
            var actions = new VisualElement();
            actions.AddToClassList("yucp-tree-actions");

            // Get folder path for ignore file check
            string folderFullPath = GetFolderFullPath(node.FullPath);
            bool hasIgnoreFile = YucpIgnoreHandler.HasIgnoreFile(folderFullPath);

            if (hasIgnoreFile)
            {
                var editIgnoreButton = new Button(() => OpenYucpIgnoreFile(folderFullPath)) { text = "Edit" };
                editIgnoreButton.AddToClassList("yucp-tree-action-btn");
                editIgnoreButton.tooltip = "Edit .yucpignore";
                actions.Add(editIgnoreButton);
            }

            var ignoreButton = new Button(() => AddFolderToIgnoreList(profile, folderFullPath)) { text = "Ignore" };
            ignoreButton.AddToClassList("yucp-tree-action-btn");
            ignoreButton.tooltip = "Add to Ignore";
            actions.Add(ignoreButton);

            content.Add(actions);
            row.Add(content);

            return row;
        }

        private string GetFolderFullPath(string nodePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string pathToProcess = nodePath.Replace('\\', '/');
            string relativePath = pathToProcess;

            int lastAssetsIndex = pathToProcess.LastIndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (lastAssetsIndex >= 0)
            {
                relativePath = pathToProcess.Substring(lastAssetsIndex);
            }
            else
            {
                int assetsIndex = pathToProcess.LastIndexOf("Assets", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex >= 0)
                {
                    relativePath = pathToProcess.Substring(assetsIndex);
                    if (!relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = "Assets/" + relativePath.Substring(6).TrimStart('/');
                    }
                }
                else
                {
                    relativePath = "Assets/" + pathToProcess.TrimStart('/');
                }
            }

            if (relativePath.Contains(":"))
            {
                int assetsIndex = relativePath.LastIndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex >= 0)
                {
                    relativePath = relativePath.Substring(assetsIndex);
                }
            }

            string folderFullPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));

            string expectedAssetsPath = Path.Combine(projectRoot, "Assets");
            if (folderFullPath.Contains(expectedAssetsPath + Path.DirectorySeparatorChar + expectedAssetsPath))
            {
                int dupIndex = folderFullPath.IndexOf(expectedAssetsPath + Path.DirectorySeparatorChar + expectedAssetsPath);
                folderFullPath = folderFullPath.Substring(0, dupIndex + expectedAssetsPath.Length) +
                               folderFullPath.Substring(dupIndex + expectedAssetsPath.Length + expectedAssetsPath.Length);
            }

            return folderFullPath;
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
                        child.IsExpanded = folderExpandedStates.ContainsKey(currentPath)
                            ? folderExpandedStates[currentPath]
                            : true;
                        current.Children.Add(child);
                    }
                    else
                    {
                        if (folderExpandedStates.ContainsKey(currentPath))
                        {
                            child.IsExpanded = folderExpandedStates[currentPath];
                        }
                    }

                    current = child;
                }

                current.Assets.Add(asset);
            }

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

        private void RenderFolderTree(FolderTreeNode node, VisualElement container, ExportProfile profile, int depth, bool isLastChild = false)
        {
            if (node.Assets.Count == 0 && node.Children.Count == 0 && depth > 0)
                return;

            // Create folder header
            if (depth > 0)
            {
                var folderHeader = CreateFolderHeader(node, profile, depth, isLastChild && node.Children.Count == 0 && node.Assets.Count == 0);
                container.Add(folderHeader);
            }

            // Create content group for folder contents (assets + subfolders)
            bool hasContent = (node.Assets.Count > 0 || node.Children.Count > 0) && (depth == 0 || node.IsExpanded);
            VisualElement contentGroup = null;
            
            if (hasContent && depth > 0)
            {
                contentGroup = new VisualElement();
                contentGroup.AddToClassList("yucp-tree-group");
                contentGroup.AddToClassList($"yucp-tree-group-depth-{Mathf.Min(depth, TREE_MAX_DEPTH_COLORS)}");
                container.Add(contentGroup);
            }
            else
            {
                contentGroup = container;
            }

            // Render assets
            if (node.Assets.Count > 0 && (depth == 0 || node.IsExpanded))
            {
                bool hasChildren = node.Children.Count > 0;
                for (int i = 0; i < node.Assets.Count; i++)
                {
                    var asset = node.Assets[i];
                    bool isLastAsset = (i == node.Assets.Count - 1) && !hasChildren;
                    var assetItem = CreateAssetItem(asset, profile, depth == 0 ? 0 : depth + 1, isLastAsset);
                    contentGroup.Add(assetItem);
                }
            }

            // Render child folders
            if (node.Children.Count > 0 && (depth == 0 || node.IsExpanded))
            {
                for (int i = 0; i < node.Children.Count; i++)
                {
                    var child = node.Children[i];
                    bool isLast = (i == node.Children.Count - 1);
                    RenderFolderTree(child, contentGroup, profile, depth + 1, isLast);
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

        private VisualElement CreateAssetItem(DiscoveredAsset asset, ExportProfile profile, int depth, bool isLastChild = false)
        {
            // Main row container
            var row = new VisualElement();
            row.AddToClassList("yucp-tree-row");
            row.AddToClassList("yucp-tree-asset-row");
            row.AddToClassList($"yucp-tree-depth-{Mathf.Min(depth, TREE_MAX_DEPTH_COLORS)}");
            if (isLastChild)
                row.AddToClassList("yucp-tree-last-child");





            // Content container
            var content = new VisualElement();
            content.AddToClassList("yucp-tree-content");

            // Checkbox
            var checkbox = new Toggle { value = asset.included };
            checkbox.AddToClassList("yucp-tree-checkbox");
            checkbox.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(profile, "Toggle Asset Inclusion");
                asset.included = evt.newValue;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            });
            content.Add(checkbox);

            // Asset icon
            var icon = new Label(GetAssetTypeIcon(asset.assetType));
            icon.AddToClassList("yucp-tree-icon-text");
            content.Add(icon);

            // Asset name
            var nameLabel = new Label(asset.GetDisplayName());
            nameLabel.AddToClassList("yucp-tree-label");

            DerivedSettings derivedSettings;
            string derivedBasePath;
            bool isDerivedFbx = IsDerivedFbx(asset.assetPath, out derivedSettings, out derivedBasePath);
            
            if (asset.isDependency)
                nameLabel.AddToClassList("yucp-tree-label-dep");
            if (isDerivedFbx)
                nameLabel.AddToClassList("yucp-tree-label-derived");

            nameLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    evt.StopPropagation();
                    evt.PreventDefault();
                    PingAsset(asset.assetPath);
                }
            });
            nameLabel.pickingMode = PickingMode.Position;
            content.Add(nameLabel);

            // Type badge
            string typeText = asset.assetType;
            if (isDerivedFbx && typeText.Equals("Model", StringComparison.OrdinalIgnoreCase))
                typeText = "Derived";
            var typeLabel = new Label(typeText);
            typeLabel.AddToClassList("yucp-tree-type");
            content.Add(typeLabel);

            // Size
            if (!asset.isFolder && asset.fileSize > 0)
            {
                var sizeLabel = new Label(FormatBytes(asset.fileSize));
                sizeLabel.AddToClassList("yucp-tree-size");
                content.Add(sizeLabel);
            }

            // Spacer
            var spacer = new VisualElement();
            spacer.AddToClassList("yucp-tree-spacer");
            content.Add(spacer);

            // Status badges
            var badges = new VisualElement();
            badges.AddToClassList("yucp-tree-badges");

            if (asset.isDependency)
            {
                var depBadge = new Label("DEP");
                depBadge.AddToClassList("yucp-tree-status-badge");
                depBadge.AddToClassList("yucp-tree-badge-dep");
                depBadge.tooltip = "Dependency";
                badges.Add(depBadge);
            }

            if (isDerivedFbx)
            {
                bool baseMissing = string.IsNullOrEmpty(derivedBasePath);
                var derivedBadge = new Label(baseMissing ? "MISSING" : "DERIVED");
                derivedBadge.AddToClassList("yucp-tree-status-badge");
                derivedBadge.AddToClassList(baseMissing ? "yucp-tree-badge-warning" : "yucp-tree-badge-derived");
                derivedBadge.tooltip = baseMissing ? "Derived: Base Missing" : "Derived FBX";
                badges.Add(derivedBadge);
            }

            if (!string.IsNullOrEmpty(asset.sourceProfileName))
            {
                var sourceBadge = new Label(asset.sourceProfileName);
                sourceBadge.AddToClassList("yucp-tree-status-badge");
                sourceBadge.AddToClassList("yucp-tree-badge-source");
                sourceBadge.tooltip = "Source Profile";
                badges.Add(sourceBadge);
            }

            content.Add(badges);

            // Actions for derived FBX
            if (isDerivedFbx)
            {
                var actions = new VisualElement();
                actions.AddToClassList("yucp-tree-actions");

                var optionsButton = new Button(() =>
                {
                    string relativePath = GetRelativeAssetPath(asset.assetPath);
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }) { text = "Opt" };
                optionsButton.AddToClassList("yucp-tree-action-btn");
                optionsButton.tooltip = "Options";
                actions.Add(optionsButton);

                var clearButton = new Button(() =>
                {
                    string relativePath = GetRelativeAssetPath(asset.assetPath);
                    var importer = AssetImporter.GetAtPath(relativePath) as ModelImporter;
                    if (importer != null)
                    {
                        try
                        {
                            var s = DerivedSettingsUtility.TryRead(importer, out var parsed) ? parsed : new DerivedSettings();
                            s.isDerived = false;
                            importer.userData = JsonUtility.ToJson(s);
                            importer.SaveAndReimport();
                            UpdateProfileDetails();
                        }
                        catch { }
                    }
                }) { text = "Clr" };
                clearButton.AddToClassList("yucp-tree-action-btn");
                clearButton.tooltip = "Clear Derived";
                actions.Add(clearButton);

                content.Add(actions);
            }

            row.Add(content);
            return row;
        }

        private string GetRelativeAssetPath(string assetPath)
        {
            string relativePath = assetPath;
            if (Path.IsPathRooted(relativePath))
            {
                string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                if (relativePath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = relativePath.Substring(projectPath.Length).Replace('\\', '/').TrimStart('/');
                }
            }
            return relativePath;
        }

        /// <summary>
        /// Walk up the visual tree to find the asset-list-container (row may be inside a group).
        /// </summary>
        private static VisualElement FindAssetListContainer(VisualElement from)
        {
            VisualElement p = from;
            while (p != null)
            {
                if (p.name == "asset-list-container")
                    return p;
                p = p.parent;
            }
            return null;
        }
    }
}
