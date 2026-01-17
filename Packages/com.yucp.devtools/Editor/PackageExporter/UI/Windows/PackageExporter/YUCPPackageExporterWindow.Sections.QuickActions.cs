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
        private VisualElement CreateQuickActionsSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var header = CreateCollapsibleHeader("Quick Actions", 
                () => showQuickActions, 
                (value) => { showQuickActions = value; }, 
                () => UpdateProfileDetails());
            section.Add(header);
            
            if (!showQuickActions)
            {
                return section;
            }
            
            var actionsContainer = new VisualElement();
            actionsContainer.style.flexDirection = FlexDirection.Row;
            actionsContainer.style.marginTop = 8;
            
            var inspectorButton = new Button(() => 
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            }) 
            { text = "Open in Inspector" };
            inspectorButton.AddToClassList("yucp-button");
            inspectorButton.AddToClassList("yucp-button-action");
            inspectorButton.style.flexGrow = 1;
            actionsContainer.Add(inspectorButton);
            
            var saveButton = new Button(() => 
            {
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }) 
            { text = "Save Changes" };
            saveButton.AddToClassList("yucp-button");
            saveButton.AddToClassList("yucp-button-action");
            saveButton.style.flexGrow = 1;
            actionsContainer.Add(saveButton);
            
            section.Add(actionsContainer);
            
            return section;
        }

    }
}
