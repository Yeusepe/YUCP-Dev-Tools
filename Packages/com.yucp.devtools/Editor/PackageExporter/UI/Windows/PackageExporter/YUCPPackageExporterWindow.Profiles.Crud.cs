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
            // Focus on the package name field in the details panel if visible
            if (selectedProfile == profile && _profileDetailsContainer.style.display == DisplayStyle.Flex)
            {
                // The package name field should get focus
                EditorUtility.DisplayDialog("Rename Profile", 
                    $"Edit the 'Package Name' field in the details panel to rename this profile.\n\nCurrent name: {profile.packageName}", 
                    "OK");
            }
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

        private void CloneProfile(ExportProfile source)
        {
            if (source == null)
                return;
            
            var clone = Instantiate(source);
            clone.name = source.name + " (Clone)";
            
            string profilesDir = "Assets/YUCP/ExportProfiles";
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
            }
            
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(profilesDir, clone.name + ".asset"));
            
            AssetDatabase.CreateAsset(clone, assetPath);
            AssetDatabase.SaveAssets();
            
            LoadProfiles();
            
            // Add cloned profile to unified order if not in a folder
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clone));
            if (string.IsNullOrEmpty(clone.folderName))
            {
                if (!unifiedOrder.Any(item => !item.isFolder && item.identifier == guid))
                {
                    // Add after the source profile if possible
                    string sourceGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                    int insertIndex = -1;
                    for (int i = 0; i < unifiedOrder.Count; i++)
                    {
                        if (!unifiedOrder[i].isFolder && unifiedOrder[i].identifier == sourceGuid)
                        {
                            insertIndex = i + 1;
                            break;
                        }
                    }
                    
                    var newItem = new UnifiedOrderItem { isFolder = false, identifier = guid };
                    if (insertIndex >= 0 && insertIndex <= unifiedOrder.Count)
                    {
                        unifiedOrder.Insert(insertIndex, newItem);
                    }
                    else
                    {
                        unifiedOrder.Add(newItem);
                    }
                    SaveUnifiedOrder();
                }
            }
            
            selectedProfile = clone;
            int index = allProfiles.IndexOf(clone);
            selectedProfileIndices.Clear();
            selectedProfileIndices.Add(index);
            lastClickedProfileIndex = index;
            
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
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
            
            string assetPath = AssetDatabase.GetAssetPath(profile);
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

    }
}
