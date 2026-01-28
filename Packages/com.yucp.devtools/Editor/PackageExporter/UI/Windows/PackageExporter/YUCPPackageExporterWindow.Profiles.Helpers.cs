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
        private void HandleProfileSelection(int index, MouseDownEvent evt)
        {
            if (evt.ctrlKey || evt.commandKey)
            {
                // Ctrl/Cmd+Click: Toggle individual selection
                if (selectedProfileIndices.Contains(index))
                {
                    selectedProfileIndices.Remove(index);
                }
                else
                {
                    selectedProfileIndices.Add(index);
                }
                lastClickedProfileIndex = index;
            }
            else if (evt.shiftKey && lastClickedProfileIndex >= 0)
            {
                // Shift+Click: Range selection
                int start = Mathf.Min(lastClickedProfileIndex, index);
                int end = Mathf.Max(lastClickedProfileIndex, index);
                
                for (int i = start; i <= end; i++)
                {
                    if (i < allProfiles.Count)
                    {
                        selectedProfileIndices.Add(i);
                    }
                }
            }
            else
            {
                // Normal click: Single selection
                selectedProfileIndices.Clear();
                selectedProfileIndices.Add(index);
                lastClickedProfileIndex = index;
            }
            
            // Update selected profile
            if (selectedProfileIndices.Count > 0)
            {
                int firstIndex = selectedProfileIndices.Min();
                selectedProfile = allProfiles[firstIndex];
            }
            else
            {
                selectedProfile = null;
            }
            
            // Refresh UI
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
            
            // Close overlay when profile is selected (for mobile)
            // Handle overlay visibility based on selection and responsive state
            float currentWidth = rootVisualElement.resolvedStyle.width;
            if (currentWidth <= 0) currentWidth = rootVisualElement.layout.width;
            
            // If in narrow mode and no profile is selected (i.e. we just deselected), 
            // we want the sidebar to be visible so the user can select another profile.
            if (currentWidth > 0 && currentWidth < 700f && selectedProfile == null)
            {
                if (!_isOverlayOpen) OpenOverlay();
            }
            else
            {
                // Otherwise (profile selected OR wide mode), close the overlay
                if (_isOverlayOpen) CloseOverlay();
            }
        }

        private string GetProfileDisplayName(ExportProfile profile)
        {
            return string.IsNullOrEmpty(profile.packageName) ? profile.name : profile.packageName;
        }

        private void CheckDelayedRename()
        {
            if (pendingRenameProfile != null && !string.IsNullOrEmpty(pendingRenamePackageName))
            {
                double timeSinceChange = EditorApplication.timeSinceStartup - lastPackageNameChangeTime;
                
                if (timeSinceChange >= RENAME_DELAY_SECONDS)
                {
                    PerformDelayedRename(pendingRenameProfile, pendingRenamePackageName);
                    pendingRenameProfile = null;
                    pendingRenamePackageName = "";
                }
            }
        }

        private void PerformDelayedRename(ExportProfile profile, string newPackageName)
        {
            if (profile == null) return;
            
            string currentPath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(currentPath)) return;
            
            string directory = Path.GetDirectoryName(currentPath);
            string extension = Path.GetExtension(currentPath);
            string newFileName = SanitizeFileName(newPackageName) + extension;
            string newPath = Path.Combine(directory, newFileName).Replace('\\', '/');
            
            if (newPath != currentPath)
            {
                string result = AssetDatabase.MoveAsset(currentPath, newPath);
                if (string.IsNullOrEmpty(result))
                {
                    profile.profileName = profile.packageName;
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssets();
                    
                    LoadProfiles();
                    
                    var updatedProfile = AssetDatabase.LoadAssetAtPath<ExportProfile>(newPath);
                    if (updatedProfile != null)
                    {
                        selectedProfile = updatedProfile;
                        UpdateProfileList();
                        UpdateProfileDetails();
                    }
                }
                else
                {
                    Debug.LogWarning($"[Package Exporter] Failed to rename asset: {result}");
                }
            }
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "NewPackage";
            
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }
            
            return fileName;
        }

        private void BrowseForIcon(ExportProfile profile)
        {
            string iconPath = EditorUtility.OpenFilePanel("Select Package Icon", "", "png,jpg,jpeg,gif");
            if (!string.IsNullOrEmpty(iconPath))
            {
                string projectPath = "Assets/YUCP/ExportProfiles/Icons/";
                if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles/Icons"))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/YUCP"))
                        AssetDatabase.CreateFolder("Assets", "YUCP");
                    if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles"))
                        AssetDatabase.CreateFolder("Assets/YUCP", "ExportProfiles");
                    AssetDatabase.CreateFolder("Assets/YUCP/ExportProfiles", "Icons");
                }
                
                string fileName = Path.GetFileName(iconPath);
                string targetPath = projectPath + fileName;
                
                File.Copy(iconPath, targetPath, true);
                AssetDatabase.ImportAsset(targetPath);
                AssetDatabase.Refresh();
                
                profile.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                
                UpdateProfileDetails();
            }
        }

        private void BrowseForProductLinkIcon(ExportProfile profile, ProductLink link, Image iconImage)
        {
            string iconPath = EditorUtility.OpenFilePanel("Select Custom Icon for Link", "", "png,jpg,jpeg,gif");
            if (!string.IsNullOrEmpty(iconPath))
            {
                string projectPath = "Assets/YUCP/ExportProfiles/LinkIcons/";
                if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles/LinkIcons"))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/YUCP"))
                        AssetDatabase.CreateFolder("Assets", "YUCP");
                    if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles"))
                        AssetDatabase.CreateFolder("Assets/YUCP", "ExportProfiles");
                    AssetDatabase.CreateFolder("Assets/YUCP/ExportProfiles", "LinkIcons");
                }
                
                string fileName = Path.GetFileName(iconPath);
                string targetPath = projectPath + fileName;
                
                File.Copy(iconPath, targetPath, true);
                AssetDatabase.ImportAsset(targetPath);
                AssetDatabase.Refresh();
                
                Undo.RecordObject(profile, "Set Custom Link Icon");
                link.customIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
                iconImage.image = link.customIcon;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
        }

        private void BrowseForPath(ExportProfile profile)
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select Export Folder", "", "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                Undo.RecordObject(profile, "Change Export Path");
                profile.exportPath = selectedPath;
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            }
        }

        private void AddFolder(ExportProfile profile)
        {
            string selectedFolder = EditorUtility.OpenFolderPanel("Select Folder to Export", Application.dataPath, "");
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                string relativePath = GetRelativePath(selectedFolder);
                Undo.RecordObject(profile, "Add Export Folder");
                profile.foldersToExport.Add(relativePath);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }
        }

        private void RemoveFolder(ExportProfile profile, int index)
        {
            if (index >= 0 && index < profile.foldersToExport.Count)
            {
                Undo.RecordObject(profile, "Remove Export Folder");
                profile.foldersToExport.RemoveAt(index);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }
        }

        private string GetRelativePath(string absolutePath)
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            
            if (absolutePath.StartsWith(projectPath))
            {
                string relative = absolutePath.Substring(projectPath.Length);
                if (relative.StartsWith("\\") || relative.StartsWith("/"))
                {
                    relative = relative.Substring(1);
                }
                return relative;
            }
            
            return absolutePath;
        }

        private void AddDependency(ExportProfile profile)
        {
            var newDep = new PackageDependency("com.example.package", "1.0.0", "Example Package", false);
            Undo.RecordObject(profile, "Add Dependency");
            profile.dependencies.Add(newDep);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            UpdateProfileDetails();
        }

    }
}
