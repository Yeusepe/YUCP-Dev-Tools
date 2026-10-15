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


            int index = projectFolders.IndexOf(oldName);
            if (index >= 0) projectFolders[index] = newName;


            foreach (var item in unifiedOrder)
            {
                if (item.isFolder && item.identifier == oldName)
                {
                    item.identifier = newName;
                }
            }


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


            unifiedOrder.RemoveAll(item => item.isFolder && item.identifier == folderName);


            bool dirty = false;
            foreach (var p in allProfiles)
            {
                if (p.folderName == folderName)
                {
                    p.folderName = "";
                    EditorUtility.SetDirty(p);
                    dirty = true;


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


        private void MoveProfileToFolder(ExportProfile profile, string folderName)
        {
            if (profile.folderName == folderName) return;

            string profileGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));


            unifiedOrder.RemoveAll(item => !item.isFolder && item.identifier == profileGuid);

            profile.folderName = folderName;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            SaveUnifiedOrder();
            UpdateProfileList();
        }

        private void GroupSelectedProfilesIntoFolder()
        {
            CreateFolder(selectedProfileIndices.Where(i => i >= 0 && i < allProfiles.Count).Select(i => allProfiles[i]));
        }

        private void CreateFolderWithProfiles(ExportProfile profileA, ExportProfile profileB)
        {
            CreateFolder(new[] { profileA, profileB });
        }

        /// <summary>
        /// Creates a folder holding the given profiles (none is fine: an empty folder), placed where the
        /// first of them sat in the list, and starts renaming it.
        /// </summary>
        private void CreateFolder(IEnumerable<ExportProfile> profiles)
        {
            var members = profiles.Where(p => p != null).Distinct().ToList();
            var memberGuids = new HashSet<string>(members.Select(GetProfileGuid));

            string newName = GetUniqueNewFolderName();
            projectFolders.Add(newName);
            collapsedFolders.Remove(newName);
            SaveProjectFolders();

            int insertIndex = unifiedOrder.FindIndex(item => !item.isFolder && memberGuids.Contains(item.identifier));
            unifiedOrder.RemoveAll(item => !item.isFolder && memberGuids.Contains(item.identifier));
            unifiedOrder.Insert(insertIndex >= 0 ? insertIndex : unifiedOrder.Count,
                new UnifiedOrderItem { isFolder = true, identifier = newName });

            foreach (var p in members)
            {
                p.folderName = newName;
                EditorUtility.SetDirty(p);
            }
            if (members.Count > 0) AssetDatabase.SaveAssets();

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
