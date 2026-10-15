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




























        private void ShowProfileContextMenu(ExportProfile profile, int index, MouseDownEvent evt)
        {
            // Select the profile if not already selected
            if (!selectedProfileIndices.Contains(index))
            {
                selectedProfileIndices.Clear();
                selectedProfileIndices.Add(index);
                selectedProfile = profile;
                lastClickedProfileIndex = index;
                UpdateProfileList();
                UpdateProfileDetails();
                UpdateBottomBar();
            }

            var menu = new GenericMenu();

            // Multi-select: Group into Folder option
            if (selectedProfileIndices.Count >= 2)
            {
                menu.AddItem(new GUIContent("Group into Folder..."), false, () =>
                {
                    GroupSelectedProfilesIntoFolder();
                });
                menu.AddSeparator("");
            }

            // Export option
            menu.AddItem(new GUIContent("Export"), false, () =>
            {
                ExportSingleProfile(profile);
            });

            // Rescan the whole selection when the clicked profile is part of it
            var selectedProfiles = selectedProfileIndices.Where(i => i >= 0 && i < allProfiles.Count).Select(i => allProfiles[i]).ToList();
            menu.AddItem(new GUIContent(selectedProfiles.Count > 1 ? $"Rescan Assets ({selectedProfiles.Count} profiles)" : "Rescan Assets"), false, () =>
            {
                RescanProfiles(selectedProfiles);
            });

            menu.AddSeparator("");

            // Clone option (copy structure, reset distribution fields → fresh template)
            menu.AddItem(new GUIContent("Clone"), false, () =>
            {
                CloneProfile(profile);
            });

            // Duplicate option (exact copy, keep everything)
            menu.AddItem(new GUIContent("Duplicate"), false, () =>
            {
                DuplicateProfile(profile);
            });

            menu.AddSeparator("");

            // Rename option
            menu.AddItem(new GUIContent("Rename"), false, () =>
            {
                StartRenameProfile(profile);
            });

            // Delete option
            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                DeleteProfile(profile);
            });

            menu.AddSeparator("");

            // Select in Project option
            menu.AddItem(new GUIContent("Select in Project"), false, () =>
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            });

            if (!string.IsNullOrEmpty(profile.profileSaveLocation) && System.IO.Directory.Exists(profile.profileSaveLocation))
            {
                menu.AddItem(new GUIContent("Show in Explorer"), false, () =>
                {
                    EditorUtility.RevealInFinder(profile.profileSaveLocation);
                });
            }

            menu.AddSeparator("");

            // Move to Folder option
            menu.AddItem(new GUIContent("Move to Folder/New Folder..."), false, GroupSelectedProfilesIntoFolder);
            if (projectFolders.Count > 0)
            {
                menu.AddSeparator("Move to Folder/");

                // Root folder option (if currently in a folder)
                if (!string.IsNullOrEmpty(profile.folderName))
                {
                    menu.AddItem(new GUIContent("Move to Folder/_Root"), false, () =>
                    {
                        MoveProfileToFolder(profile, "");
                    });
                     menu.AddSeparator("Move to Folder/");
                }

                foreach (var folder in projectFolders)
                {
                    // Skip current folder
                    if (folder == profile.folderName) continue;

                    menu.AddItem(new GUIContent($"Move to Folder/{folder}"), false, () =>
                    {
                        MoveProfileToFolder(profile, folder);
                    });
                }
            }

            menu.ShowAsContext();
        }

        /// <summary>Right-click on empty space in the profile list.</summary>
        private void ShowProfileListContextMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("New Folder"), false, () => CreateFolder(Array.Empty<ExportProfile>()));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Rescan All Profiles"), false, () => RescanProfiles(allProfiles));
            menu.ShowAsContext();
        }

























































    }
}
