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
        private void LoadProfiles()
        {
            allProfiles.Clear();
            
            string[] guids = AssetDatabase.FindAssets("t:ExportProfile");
            
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<ExportProfile>(path);
                
                if (profile != null)
                {
                    allProfiles.Add(profile);
                }
            }
            
            // Apply custom order if available, otherwise sort alphabetically
            allProfiles = ApplyCustomOrder(allProfiles);
            
            // Reselect if we had a selection
            if (selectedProfile != null)
            {
                int index = allProfiles.IndexOf(selectedProfile);
                if (index < 0)
                {
                    selectedProfile = null;
                    selectedProfileIndices.Clear();
                }
            }
        }

        private List<ExportProfile> ApplyCustomOrder(List<ExportProfile> profiles)
        {
            string orderJson = EditorPrefs.GetString(PackageOrderKey, "");
            
            if (string.IsNullOrEmpty(orderJson))
            {
                // No custom order, sort alphabetically
                return profiles.OrderBy(p => p.packageName).ToList();
            }
            
            try
            {
                // Parse the stored order (list of GUIDs)
                var orderedGuids = JsonUtility.FromJson<SerializableStringList>(orderJson);
                if (orderedGuids == null || orderedGuids.items == null || orderedGuids.items.Count == 0)
                {
                    return profiles.OrderBy(p => p.packageName).ToList();
                }
                
                // Create a dictionary for quick lookup
                var profileDict = profiles.ToDictionary(p => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p)), p => p);
                
                // Build ordered list
                var ordered = new List<ExportProfile>();
                var usedGuids = new HashSet<string>();
                
                // Add profiles in stored order
                foreach (string guid in orderedGuids.items)
                {
                    if (profileDict.TryGetValue(guid, out var profile))
                    {
                        ordered.Add(profile);
                        usedGuids.Add(guid);
                    }
                }
                
                // Add any new profiles that weren't in the stored order (alphabetically)
                var newProfiles = profiles.Where(p => 
                {
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p));
                    return !usedGuids.Contains(guid);
                }).OrderBy(p => p.packageName).ToList();
                
                ordered.AddRange(newProfiles);
                
                return ordered;
            }
            catch
            {
                // If parsing fails, fall back to alphabetical
                return profiles.OrderBy(p => p.packageName).ToList();
            }
        }

        private void SaveCustomOrder()
        {
            var guids = allProfiles.Select(p => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p))).ToList();
            var serializable = new SerializableStringList { items = guids };
            string json = JsonUtility.ToJson(serializable);
            EditorPrefs.SetString(PackageOrderKey, json);
            
            // Also save unified order
            SaveUnifiedOrder();
        }

        private void MigrateToUnifiedOrder()
        {
            // Check if already migrated
            if (EditorPrefs.GetBool(UnifiedOrderMigratedKey, false))
                return;
            
            unifiedOrder.Clear();
            
            // Load existing order
            string orderJson = EditorPrefs.GetString(PackageOrderKey, "");
            List<string> orderedGuids = new List<string>();
            
            if (!string.IsNullOrEmpty(orderJson))
            {
                try
                {
                    var orderedGuidsObj = JsonUtility.FromJson<SerializableStringList>(orderJson);
                    if (orderedGuidsObj != null && orderedGuidsObj.items != null)
                    {
                        orderedGuids = orderedGuidsObj.items;
                    }
                }
                catch
                {
                    // If parsing fails, start fresh
                }
            }
            
            // Build unified order from existing data
            // First, add folders in their current order (alphabetically sorted)
            var folders = new List<string>(projectFolders);
            folders.Sort();
            
            foreach (var folderName in folders)
            {
                unifiedOrder.Add(new UnifiedOrderItem { isFolder = true, identifier = folderName });
            }
            
            // Then add profiles in their stored order (only those not in folders)
            foreach (string guid in orderedGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<ExportProfile>(path);
                if (profile != null && string.IsNullOrEmpty(profile.folderName))
                {
                    unifiedOrder.Add(new UnifiedOrderItem { isFolder = false, identifier = guid });
                }
            }
            
            // Add any new profiles not in order (alphabetically)
            var allProfileGuids = allProfiles.Select(p => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p))).ToHashSet();
            var orderedProfileGuids = orderedGuids.ToHashSet();
            var newProfiles = allProfiles.Where(p =>
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p));
                return !orderedProfileGuids.Contains(guid) && string.IsNullOrEmpty(p.folderName);
            }).OrderBy(p => p.packageName).ToList();
            
            foreach (var profile in newProfiles)
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
                unifiedOrder.Add(new UnifiedOrderItem { isFolder = false, identifier = guid });
            }
            
            // Save unified order
            SaveUnifiedOrder();
            
            // Mark as migrated
            EditorPrefs.SetBool(UnifiedOrderMigratedKey, true);
        }

        private void LoadUnifiedOrder()
        {
            string orderJson = EditorPrefs.GetString(UnifiedOrderKey, "");
            
            if (string.IsNullOrEmpty(orderJson))
            {
                // If no unified order exists, build it from current state
                BuildUnifiedOrderFromCurrentState();
                return;
            }
            
            try
            {
                var orderList = JsonUtility.FromJson<UnifiedOrderList>(orderJson);
                if (orderList != null && orderList.items != null)
                {
                    unifiedOrder = orderList.items;
                    // Validate and clean up order (remove invalid items)
                    ValidateAndCleanUnifiedOrder();
                }
                else
                {
                    BuildUnifiedOrderFromCurrentState();
                }
            }
            catch
            {
                // If parsing fails, rebuild from current state
                BuildUnifiedOrderFromCurrentState();
            }
        }

        private void BuildUnifiedOrderFromCurrentState()
        {
            unifiedOrder.Clear();
            
            // Add folders first (sorted)
            var folders = new List<string>(projectFolders);
            folders.Sort();
            foreach (var folderName in folders)
            {
                unifiedOrder.Add(new UnifiedOrderItem { isFolder = true, identifier = folderName });
            }
            
            // Add uncategorized profiles (sorted alphabetically)
            var uncategorizedProfiles = allProfiles
                .Where(p => string.IsNullOrEmpty(p.folderName) || !projectFolders.Contains(p.folderName))
                .OrderBy(p => p.packageName)
                .ToList();
            
            foreach (var profile in uncategorizedProfiles)
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
                unifiedOrder.Add(new UnifiedOrderItem { isFolder = false, identifier = guid });
            }
        }

        private void ValidateAndCleanUnifiedOrder()
        {
            var validOrder = new List<UnifiedOrderItem>();
            var seenFolders = new HashSet<string>();
            var seenProfiles = new HashSet<string>();
            
            // Get all valid folders and profiles
            var validFolders = projectFolders.ToHashSet();
            var validProfileGuids = allProfiles.ToDictionary(
                p => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p)),
                p => p
            );
            
            foreach (var item in unifiedOrder)
            {
                if (item.isFolder)
                {
                    // Validate folder exists
                    if (validFolders.Contains(item.identifier) && !seenFolders.Contains(item.identifier))
                    {
                        validOrder.Add(item);
                        seenFolders.Add(item.identifier);
                    }
                }
                else
                {
                    // Validate profile exists and is not in a folder
                    if (validProfileGuids.TryGetValue(item.identifier, out var profile))
                    {
                        // Only add if not in a folder (folders handle their own profiles)
                        if (string.IsNullOrEmpty(profile.folderName) && !seenProfiles.Contains(item.identifier))
                        {
                            validOrder.Add(item);
                            seenProfiles.Add(item.identifier);
                        }
                    }
                }
            }
            
            // Add any missing folders
            foreach (var folder in validFolders)
            {
                if (!seenFolders.Contains(folder))
                {
                    validOrder.Add(new UnifiedOrderItem { isFolder = true, identifier = folder });
                }
            }
            
            // Add any missing uncategorized profiles
            foreach (var profile in allProfiles)
            {
                if (string.IsNullOrEmpty(profile.folderName))
                {
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
                    if (!seenProfiles.Contains(guid))
                    {
                        validOrder.Add(new UnifiedOrderItem { isFolder = false, identifier = guid });
                    }
                }
            }
            
            unifiedOrder = validOrder;
        }

        private void SaveUnifiedOrder()
        {
            var orderList = new UnifiedOrderList { items = unifiedOrder };
            string json = JsonUtility.ToJson(orderList);
            EditorPrefs.SetString(UnifiedOrderKey, json);
        }

    }
}
