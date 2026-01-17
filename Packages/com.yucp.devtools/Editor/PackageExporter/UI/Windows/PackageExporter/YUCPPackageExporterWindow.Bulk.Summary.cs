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
        private VisualElement CreateMultiProfileSummarySection()
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var title = new Label("Selected Profiles");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var list = new ScrollView();
            list.style.maxHeight = 300;
            
            foreach (int index in selectedProfileIndices.OrderBy(i => i))
            {
                if (index >= 0 && index < allProfiles.Count)
                {
                    var profile = allProfiles[index];
                    if (profile != null)
                    {
                        var item = new VisualElement();
                        item.style.flexDirection = FlexDirection.Row;
                        item.style.alignItems = Align.Center;
                        item.style.paddingTop = 4;
                        item.style.paddingBottom = 4;
                        item.style.paddingLeft = 8;
                        item.style.paddingRight = 8;
                        item.style.marginBottom = 2;
                        item.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
                        
                        var nameLabel = new Label(GetProfileDisplayName(profile));
                        nameLabel.AddToClassList("yucp-label");
                        nameLabel.style.flexGrow = 1;
                        item.Add(nameLabel);
                        
                        var versionLabel = new Label($"v{profile.version}");
                        versionLabel.AddToClassList("yucp-label-secondary");
                        versionLabel.style.marginLeft = 8;
                        item.Add(versionLabel);
                        
                        list.Add(item);
                    }
                }
            }
            
            section.Add(list);
            return section;
        }

    }
}
