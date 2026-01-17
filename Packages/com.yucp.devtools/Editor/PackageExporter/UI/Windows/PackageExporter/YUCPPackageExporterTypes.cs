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
    internal class FolderTreeNode
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public List<FolderTreeNode> Children { get; set; } = new List<FolderTreeNode>();
        public List<DiscoveredAsset> Assets { get; set; } = new List<DiscoveredAsset>();
        public bool IsExpanded { get; set; } = true;
        
        public FolderTreeNode(string name, string fullPath)
        {
            Name = name;
            FullPath = fullPath;
        }
    }

    internal struct ToolbarMenuItem
    {
        public string Label;
        public string Tooltip;
        public Action Callback;
        public bool IsSeparator;
        
        public static ToolbarMenuItem Separator()
        {
            return new ToolbarMenuItem { IsSeparator = true };
        }
    }

}
