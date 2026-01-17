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
        private VisualElement CreateBulkPermanentIgnoreFoldersSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Permanent Ignore Folders");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpText = new Label("Folders permanently excluded from all exports");
            helpText.AddToClassList("yucp-label-secondary");
            helpText.style.marginBottom = 8;
            section.Add(helpText);
            
            var allIgnoreFolders = selectedProfiles
                .SelectMany(p => p.PermanentIgnoreFolders ?? new List<string>())
                .Distinct()
                .OrderBy(f => f)
                .ToList();
            
            var folderList = new VisualElement();
            folderList.style.maxHeight = 150;
            var scrollView = new ScrollView();
            
            foreach (var folder in allIgnoreFolders)
            {
                bool allHaveFolder = selectedProfiles.All(p => p.PermanentIgnoreFolders != null && p.PermanentIgnoreFolders.Contains(folder));
                bool someHaveFolder = selectedProfiles.Any(p => p.PermanentIgnoreFolders != null && p.PermanentIgnoreFolders.Contains(folder));
                
                var folderItem = CreateBulkStringListItem(folder, allHaveFolder, someHaveFolder,
                    (profile, value) =>
                    {
                        var ignoreFolders = profile.PermanentIgnoreFolders;
                        
                        if (value)
                        {
                            if (!ignoreFolders.Contains(folder))
                                ignoreFolders.Add(folder);
                        }
                        else
                        {
                            ignoreFolders.Remove(folder);
                        }
                    });
                scrollView.Add(folderItem);
            }
            
            folderList.Add(scrollView);
            section.Add(folderList);
            
            var addButton = new Button(() =>
            {
                string folderPath = EditorUtility.OpenFolderPanel("Select Folder to Ignore", Application.dataPath, "");
                if (!string.IsNullOrEmpty(folderPath))
                {
                    string relativePath = GetRelativePath(folderPath);
                    if (string.IsNullOrEmpty(relativePath))
                    {
                        EditorUtility.DisplayDialog("Invalid Path", "Please select a folder within the Unity project.", "OK");
                        return;
                    }
                    
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Add Ignore Folder");
                        var ignoreFolders = profile.PermanentIgnoreFolders;
                        if (!ignoreFolders.Contains(relativePath))
                        {
                            ignoreFolders.Add(relativePath);
                        }
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                }
            }) { text = "+ Add Ignore Folder to All" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            return section;
        }

    }
}
