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
        private void AddProductLink(ExportProfile profile, VisualElement container)
        {
            if (profile.productLinks == null)
            {
                profile.productLinks = new List<ProductLink>();
            }
            
            var newLink = new ProductLink();
            Undo.RecordObject(profile, "Add Product Link");
            profile.productLinks.Add(newLink);
            EditorUtility.SetDirty(profile);
            
            // Find the add button (last child)
            var addButton = container.Children().Last();
            var linkCard = CreateProductLinkCard(profile, newLink);
            container.Insert(container.childCount - 1, linkCard);
        }

    }
}
