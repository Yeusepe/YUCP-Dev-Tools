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
        private void CreateYucpIgnoreFile(ExportProfile profile, string folderPath)
        {
            if (YucpIgnoreHandler.CreateIgnoreFile(folderPath))
            {
                AssetDatabase.Refresh();
                
                if (EditorUtility.DisplayDialog(
                    "Created .yucpignore",
                    $"Created .yucpignore file in:\n{folderPath}\n\nOpen the file to edit ignore patterns?",
                    "Open",
                    "Later"))
                {
                    OpenYucpIgnoreFile(folderPath);
                }
                
                if (EditorUtility.DisplayDialog(
                    "Rescan Assets",
                    "Rescan assets now to apply the new ignore file?",
                    "Rescan",
                    "Later"))
                {
                    ScanAssetsForInspector(profile);
                }
                else
                {
                    UpdateProfileDetails();
                }
            }
        }

        private void OpenYucpIgnoreFile(string folderPath)
        {
            string ignoreFilePath = YucpIgnoreHandler.GetIgnoreFilePath(folderPath);
            
            if (File.Exists(ignoreFilePath))
            {
                System.Diagnostics.Process.Start(ignoreFilePath);
            }
            else
            {
                EditorUtility.DisplayDialog("File Not Found", $".yucpignore file not found:\n{ignoreFilePath}", "OK");
            }
        }

    }
}
