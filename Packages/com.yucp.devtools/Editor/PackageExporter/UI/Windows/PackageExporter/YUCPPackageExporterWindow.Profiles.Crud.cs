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
        private void StartRenameProfile(ExportProfile profile)
        {
            if (profile == null)
                return;
            
            // Ensure this profile is selected + details are built, then focus the Package Name field.
            if (selectedProfile != profile)
            {
                selectedProfile = profile;
                selectedProfileIndices.Clear();
                
                int idx = allProfiles.IndexOf(profile);
                if (idx >= 0)
                {
                    selectedProfileIndices.Add(idx);
                    lastClickedProfileIndex = idx;
                }
                
                UpdateProfileList();
                UpdateProfileDetails();
                UpdateBottomBar();
            }
            else
            {
                // If details weren't visible/built for some reason, rebuild them.
                if (_profileDetailsContainer != null && _profileDetailsContainer.style.display != DisplayStyle.Flex)
                {
                    UpdateProfileDetails();
                }
            }
            
            // UI Toolkit focus can fail if we try immediately (context menu closing focus churn).
            rootVisualElement?.schedule.Execute(() =>
            {
                if (_packageNameField == null)
                {
                    // Attempt to re-find it if the field reference was cleared by a rebuild.
                    _packageNameField = _profileDetailsContainer?.Q<TextField>("package-name-field");
                }
                
                if (_packageNameField != null && _packageNameField.panel != null)
                {
                    _packageNameField.Focus();
                    _packageNameField.SelectAll();
                }
            }).StartingIn(1);
        }

        private void RefreshProfiles()
        {
            LoadProfiles();
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

        private void CreateNewProfile()
        {
            string profilesDir = "Assets/YUCP/ExportProfiles";
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
            }
            
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            profile.packageName = "NewPackage";
            profile.profileName = profile.packageName;
            profile.version = "1.0.0";
            
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(profilesDir, "NewExportProfile.asset"));
            
            AssetDatabase.CreateAsset(profile, assetPath);
            AssetDatabase.SaveAssets();
            
            // Update profile count for milestones
            try
            {
                System.Type milestoneTrackerType = null;
                
                // Try to find the type by searching through all loaded assemblies
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    milestoneTrackerType = assembly.GetType("YUCP.Components.Editor.SupportBanner.MilestoneTracker");
                    if (milestoneTrackerType != null)
                        break;
                }
                
                if (milestoneTrackerType != null)
                {
                    var updateMethod = milestoneTrackerType.GetMethod("UpdateProfileCountFromAssets", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    updateMethod?.Invoke(null, null);
                }
            }
            catch
            {
                // Silently fail if milestone tracker is not available
            }
            
            LoadProfiles();
            
            // Add new profile to unified order if not in a folder
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
            if (string.IsNullOrEmpty(profile.folderName))
            {
                if (!unifiedOrder.Any(item => !item.isFolder && item.identifier == guid))
                {
                    unifiedOrder.Add(new UnifiedOrderItem { isFolder = false, identifier = guid });
                    SaveUnifiedOrder();
                }
            }
            
            selectedProfile = profile;
            int index = allProfiles.IndexOf(profile);
            selectedProfileIndices.Clear();
            selectedProfileIndices.Add(index);
            lastClickedProfileIndex = index;
            
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
            
            EditorGUIUtility.PingObject(profile);
        }

        /// <summary>
        /// Duplicate: exact copy. Keeps all fields including version, export stats, product IDs, links, etc.
        /// </summary>
        private void DuplicateProfile(ExportProfile source)
        {
            if (source == null)
                return;
            
            var dup = Instantiate(source);
            dup.name = source.name + " (Duplicate)";
            
            string profilesDir = "Assets/YUCP/ExportProfiles";
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
            }
            
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(profilesDir, dup.name + ".asset"));
            AssetDatabase.CreateAsset(dup, assetPath);
            AssetDatabase.SaveAssets();
            
            LoadProfiles();
            AddNewProfileToUnifiedOrder(dup, source, placeAfterSource: true);
            
            selectedProfile = dup;
            int index = allProfiles.IndexOf(dup);
            selectedProfileIndices.Clear();
            selectedProfileIndices.Add(index);
            lastClickedProfileIndex = index;
            
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

        /// <summary>
        /// Clone: copy structure but reset distribution-related fields (version, stats, product IDs, links, etc.)
        /// to create a "fresh template" from the source.
        /// </summary>
        private void CloneProfile(ExportProfile source)
        {
            if (source == null)
                return;
            
            var clone = Instantiate(source);
            clone.name = source.name + " (Clone)";
            
            // Reset distribution-ish fields for template use
            clone.version = "1.0.0";
            clone.UnlinkPackageId();
            clone.gumroadProductId = "";
            clone.jinxxyProductId = "";
            clone.profileSaveLocation = "";
            clone.productLinks = new List<ProductLink>();
            clone.ResetExportStats();
            clone.ClearScan();
            
            string profilesDir = "Assets/YUCP/ExportProfiles";
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
            }
            
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(profilesDir, clone.name + ".asset"));
            AssetDatabase.CreateAsset(clone, assetPath);
            AssetDatabase.SaveAssets();
            
            LoadProfiles();
            AddNewProfileToUnifiedOrder(clone, source, placeAfterSource: true);
            
            selectedProfile = clone;
            int index = allProfiles.IndexOf(clone);
            selectedProfileIndices.Clear();
            selectedProfileIndices.Add(index);
            lastClickedProfileIndex = index;
            
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

        private void AddNewProfileToUnifiedOrder(ExportProfile newProfile, ExportProfile sourceProfile, bool placeAfterSource)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(newProfile));
            if (!string.IsNullOrEmpty(newProfile.folderName))
                return;
            if (unifiedOrder.Any(item => !item.isFolder && item.identifier == guid))
                return;
            
            int insertIndex = -1;
            if (placeAfterSource && sourceProfile != null)
            {
                string sourceGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sourceProfile));
                for (int i = 0; i < unifiedOrder.Count; i++)
                {
                    if (!unifiedOrder[i].isFolder && unifiedOrder[i].identifier == sourceGuid)
                    {
                        insertIndex = i + 1;
                        break;
                    }
                }
            }
            
            var newItem = new UnifiedOrderItem { isFolder = false, identifier = guid };
            if (insertIndex >= 0 && insertIndex <= unifiedOrder.Count)
                unifiedOrder.Insert(insertIndex, newItem);
            else
                unifiedOrder.Add(newItem);
            SaveUnifiedOrder();
        }

        private void DeleteProfile(ExportProfile profile)
        {
            if (profile == null)
                return;
            
            bool confirm = EditorUtility.DisplayDialog(
                "Delete Export Profile",
                $"Are you sure you want to delete the profile '{profile.name}'?\n\nThis cannot be undone.",
                "Delete",
                "Cancel"
            );
            
            if (!confirm)
                return;
           
            DeleteProfileInternal(profile);
        }

        private void DeleteProfileInternal(ExportProfile profile)
        {
            if (profile == null)
                return;

            string assetPath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(assetPath))
                return;
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            
            // Remove from unified order
            unifiedOrder.RemoveAll(item => !item.isFolder && item.identifier == guid);
            
            if (selectedProfile == profile)
            {
                selectedProfile = null;
                selectedProfileIndices.Clear();
            }
            
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.SaveAssets();
            
            SaveUnifiedOrder();
            
            // Update profile count for milestones
            try
            {
                System.Type milestoneTrackerType = null;
                
                // Try to find the type by searching through all loaded assemblies
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    milestoneTrackerType = assembly.GetType("YUCP.Components.Editor.SupportBanner.MilestoneTracker");
                    if (milestoneTrackerType != null)
                        break;
                }
                
                if (milestoneTrackerType != null)
                {
                    var updateMethod = milestoneTrackerType.GetMethod("UpdateProfileCountFromAssets", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    updateMethod?.Invoke(null, null);
                }
            }
            catch
            {
                // Silently fail if milestone tracker is not available
            }
            
            LoadProfiles();
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

        private void BulkDeleteProfilesInternal(List<ExportProfile> profilesToDelete)
        {
            if (profilesToDelete == null || profilesToDelete.Count == 0)
                return;

            // Clear selection first to avoid UI trying to render deleted indices/assets mid-loop.
            selectedProfileIndices.Clear();
            selectedProfile = null;

            // Remove from unified order before deleting assets.
            foreach (var p in profilesToDelete)
            {
                if (p == null) continue;
                string path = AssetDatabase.GetAssetPath(p);
                if (string.IsNullOrEmpty(path)) continue;
                string guid = AssetDatabase.AssetPathToGUID(path);
                unifiedOrder.RemoveAll(item => !item.isFolder && item.identifier == guid);
            }

            // Delete assets without refreshing UI each time.
            foreach (var p in profilesToDelete)
            {
                if (p == null) continue;
                string path = AssetDatabase.GetAssetPath(p);
                if (string.IsNullOrEmpty(path)) continue;
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.SaveAssets();
            SaveUnifiedOrder();

            // Refresh once.
            LoadProfiles();
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

    }
}
