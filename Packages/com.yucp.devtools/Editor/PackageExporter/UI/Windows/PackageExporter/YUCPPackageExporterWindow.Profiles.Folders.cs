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
        private void LoadProjectFolders()
        {
            string foldersStr = EditorPrefs.GetString(PackageFoldersKey, "");
            projectFolders = !string.IsNullOrEmpty(foldersStr) 
                ? foldersStr.Split(';').Where(s => !string.IsNullOrEmpty(s)).ToList() 
                : new List<string>();
                
            string collapsedStr = EditorPrefs.GetString(CollapsedFoldersKey, "");
            collapsedFolders = !string.IsNullOrEmpty(collapsedStr)
                ? new HashSet<string>(collapsedStr.Split(';'))
                : new HashSet<string>();
        }

        private void SaveProjectFolders()
        {
            EditorPrefs.SetString(PackageFoldersKey, string.Join(";", projectFolders));
        }

        private void SaveCollapsedFolders()
        {
             EditorPrefs.SetString(CollapsedFoldersKey, string.Join(";", collapsedFolders));
        }

        private void ShowFolderContextMenu(string folderName)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename Folder"), false, () => 
            {
                StartRenameFolder(folderName);
            });
            menu.AddItem(new GUIContent("Delete Folder"), false, () => 
            {
                if (EditorUtility.DisplayDialog("Delete Folder", 
                    $"Are you sure you want to delete folder '{folderName}'? Profiles inside will be moved to root.", "Delete", "Cancel"))
                {
                    DeleteFolder(folderName);
                }
            });
            menu.ShowAsContext();
        }

        private void RenameFolder(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName) || oldName == newName) return;
            if (projectFolders.Contains(newName))
            {
                EditorUtility.DisplayDialog("Error", "Folder already exists!", "OK");
                return;
            }
            
            // Update list
            int index = projectFolders.IndexOf(oldName);
            if (index >= 0) projectFolders[index] = newName;
            
            // Update unified order
            foreach (var item in unifiedOrder)
            {
                if (item.isFolder && item.identifier == oldName)
                {
                    item.identifier = newName;
                }
            }
            
            // Update profiles
            bool dirty = false;
            foreach (var p in allProfiles)
            {
                if (p.folderName == oldName)
                {
                    p.folderName = newName;
                    EditorUtility.SetDirty(p);
                    dirty = true;
                }
            }
            if (dirty) AssetDatabase.SaveAssets();
            
            SaveProjectFolders();
            SaveUnifiedOrder();
            UpdateProfileList();
        }

        private void DeleteFolder(string folderName)
        {
            projectFolders.Remove(folderName);
            
            // Remove folder from unified order
            unifiedOrder.RemoveAll(item => item.isFolder && item.identifier == folderName);
            
            // Move profiles to root and add them to unified order
            bool dirty = false;
            foreach (var p in allProfiles)
            {
                if (p.folderName == folderName)
                {
                    p.folderName = "";
                    EditorUtility.SetDirty(p);
                    dirty = true;
                    
                    // Add profile to unified order (at end for now)
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p));
                    if (!unifiedOrder.Any(item => !item.isFolder && item.identifier == guid))
                    {
                        unifiedOrder.Add(new UnifiedOrderItem { isFolder = false, identifier = guid });
                    }
                }
            }
            if (dirty) AssetDatabase.SaveAssets();
            
            SaveProjectFolders();
            SaveUnifiedOrder();
            UpdateProfileList();
        }

        private void CreateNewFolder()
        {
            string newName = GetUniqueNewFolderName();
            projectFolders.Add(newName);
            SaveProjectFolders();
            
            // Add folder to unified order at the end (or at insertion point if dragging)
            // For now, add at end - can be enhanced later to support insertion point
            unifiedOrder.Add(new UnifiedOrderItem { isFolder = true, identifier = newName });
            SaveUnifiedOrder();
            
            StartRenameFolder(newName);
        }

        private void MoveProfileToFolder(ExportProfile profile, string folderName)
        {
            if (profile.folderName == folderName) return;
            
            string profileGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
            
            // Remove profile from unified order (if it was there as uncategorized)
            unifiedOrder.RemoveAll(item => !item.isFolder && item.identifier == profileGuid);
            
            profile.folderName = folderName;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            
            SaveUnifiedOrder();
            UpdateProfileList();
        }

        private void GroupSelectedProfilesIntoFolder()
        {
            if (selectedProfileIndices.Count < 2) return;
            
            string newName = GetUniqueNewFolderName();
            projectFolders.Add(newName);
            SaveProjectFolders();
            
            // Add folder to unified order (at position of first selected profile if possible)
            int insertIndex = -1;
            if (selectedProfileIndices.Count > 0)
            {
                int firstIndex = selectedProfileIndices.Min();
                var firstProfile = allProfiles[firstIndex];
                string firstGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(firstProfile));
                
                // Find position in unified order
                for (int i = 0; i < unifiedOrder.Count; i++)
                {
                    if (!unifiedOrder[i].isFolder && unifiedOrder[i].identifier == firstGuid)
                    {
                        insertIndex = i;
                        break;
                    }
                }
            }
            
            var folderItem = new UnifiedOrderItem { isFolder = true, identifier = newName };
            if (insertIndex >= 0)
            {
                unifiedOrder.Insert(insertIndex, folderItem);
            }
            else
            {
                unifiedOrder.Add(folderItem);
            }
            
            // Move profiles immediately and remove from unified order
            bool dirty = false;
            foreach (int idx in selectedProfileIndices)
            {
                if (idx >= 0 && idx < allProfiles.Count)
                {
                    var p = allProfiles[idx];
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p));
                    
                    // Remove from unified order
                    unifiedOrder.RemoveAll(item => !item.isFolder && item.identifier == guid);
                    
                    p.folderName = newName;
                    EditorUtility.SetDirty(p);
                    dirty = true;
                }
            }
            if (dirty) AssetDatabase.SaveAssets();
            
            SaveUnifiedOrder();
            StartRenameFolder(newName);
        }

        private void CreateFolderWithProfiles(ExportProfile profileA, ExportProfile profileB)
        {
            string newName = GetUniqueNewFolderName();
            projectFolders.Add(newName);
            SaveProjectFolders();
            
            // Find position in unified order (between the two profiles)
            string guidA = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profileA));
            string guidB = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profileB));
            
            int insertIndex = -1;
            int indexA = -1, indexB = -1;
            for (int i = 0; i < unifiedOrder.Count; i++)
            {
                if (!unifiedOrder[i].isFolder)
                {
                    if (unifiedOrder[i].identifier == guidA) indexA = i;
                    if (unifiedOrder[i].identifier == guidB) indexB = i;
                }
            }
            
            if (indexA >= 0 && indexB >= 0)
            {
                insertIndex = Math.Min(indexA, indexB);
            }
            else if (indexA >= 0)
            {
                insertIndex = indexA;
            }
            else if (indexB >= 0)
            {
                insertIndex = indexB;
            }
            
            // Add folder to unified order
            var folderItem = new UnifiedOrderItem { isFolder = true, identifier = newName };
            if (insertIndex >= 0)
            {
                unifiedOrder.Insert(insertIndex, folderItem);
            }
            else
            {
                unifiedOrder.Add(folderItem);
            }
            
            // Remove profiles from unified order
            unifiedOrder.RemoveAll(item => !item.isFolder && (item.identifier == guidA || item.identifier == guidB));
            
            profileA.folderName = newName;
            profileB.folderName = newName;
            EditorUtility.SetDirty(profileA);
            EditorUtility.SetDirty(profileB);
            AssetDatabase.SaveAssets();
            
            SaveUnifiedOrder();
            StartRenameFolder(newName);
        }

        private string GetUniqueNewFolderName(string baseName = "New Folder")
        {
            if (!projectFolders.Contains(baseName)) return baseName;
            
            int i = 1;
            while (projectFolders.Contains($"{baseName} {i}"))
            {
                i++;
            }
            return $"{baseName} {i}";
        }

        private void StartRenameFolder(string folderName)
        {
            folderBeingRenamed = folderName;
            folderRenameStartTime = EditorApplication.timeSinceStartup;
            UpdateProfileList();
        }

        private void CancelRenameFolder()
        {
            folderBeingRenamed = null;
            UpdateProfileList();
        }

        private void EndRenameFolder(string oldName, string newName)
        {
            folderBeingRenamed = null;
            
            if (string.IsNullOrWhiteSpace(newName))
            {
                UpdateProfileList();
                return;
            }
            
            if (oldName == newName)
            {
                UpdateProfileList();
                return;
            }
            
            RenameFolder(oldName, newName);
            UpdateProfileList();
        }

    }
}
