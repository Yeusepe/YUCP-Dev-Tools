using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Linq;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Menu items for quick access to Package Exporter features.
    /// </summary>
    public static class MenuItems
    {
        [MenuItem("Assets/Create/YUCP/Export Profile", priority = 100)]
        public static void CreateExportProfile()
        {
            CreateExportProfileInternal();
        }
        
        [MenuItem("Assets/Create/YUCP/Export Profile Here", priority = 101)]
        public static void CreateExportProfileHere()
        {
            CreateExportProfileInternal(useCurrentFolder: true);
        }
        
        [MenuItem("Assets/Create/YUCP/Custom Version Rule", priority = 102)]
        public static void CreateCustomVersionRule()
        {
            CreateCustomVersionRuleInternal();
        }
        
        private static void CreateExportProfileInternal(bool useCurrentFolder = false)
        {
            string profilesDir;
            string defaultExportFolder = "Assets/";
            
            if (useCurrentFolder)
            {
                // Get currently selected folder in Project window
                string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                
                if (string.IsNullOrEmpty(selectedPath))
                {
                    profilesDir = "Assets";
                }
                else if (Directory.Exists(selectedPath))
                {
                    profilesDir = selectedPath;
                    defaultExportFolder = selectedPath;
                }
                else
                {
                    // Selected a file, use its directory
                    profilesDir = Path.GetDirectoryName(selectedPath);
                    defaultExportFolder = profilesDir;
                }
                
                Debug.Log($"[YUCP] Creating export profile in: {profilesDir}");
            }
            else
            {
                // Use default profiles directory
                profilesDir = "Assets/YUCP/ExportProfiles";
                if (!Directory.Exists(profilesDir))
                {
                    Directory.CreateDirectory(profilesDir);
                    AssetDatabase.Refresh();
                }
            }
            
            // Create profile
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            profile.packageName = "NewPackage";
            profile.version = "1.0.0";
            
            // Add sensible defaults
            profile.foldersToExport.Add(defaultExportFolder);
            profile.includeDependencies = true;
            profile.recurseFolders = true;
            profile.generatePackageJson = true;
            
            // Store the profile save location for future reference
            if (useCurrentFolder)
            {
                profile.profileSaveLocation = profilesDir;
            }
            
            // Generate unique path
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(profilesDir, "ExportProfile.asset"));
            
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
            
            // Select and ping
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
            
            Debug.Log($"[YUCP] Created export profile: {assetPath}");
        }
        
        [MenuItem("Tools/YUCP/Others/Package Exporter/Create Export Profile")]
        public static void CreateExportProfileFromMenu()
        {
            CreateExportProfileInternal();
        }
        
        [MenuItem("Tools/YUCP/Others/Package Exporter/Open Export Profiles Folder")]
        public static void OpenExportProfilesFolder()
        {
            string profilesDir = "Assets/YUCP/ExportProfiles";
            
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
                AssetDatabase.Refresh();
            }
            
            // Select the folder in Unity
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(profilesDir);
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }
        
        [MenuItem("Tools/YUCP/Others/Package Exporter/Check ConfuserEx Installation")]
        public static void CheckConfuserExInstallation()
        {
            string status = ConfuserExManager.GetStatusInfo();
            bool isInstalled = ConfuserExManager.IsInstalled();
            
            if (isInstalled)
            {
                EditorUtility.DisplayDialog("ConfuserEx Status", status, "OK");
            }
            else
            {
                bool download = EditorUtility.DisplayDialog(
                    "ConfuserEx Not Installed",
                    status + "\n\nWould you like to download and install ConfuserEx now?",
                    "Download",
                    "Cancel"
                );
                
                if (download)
                {
                    EditorUtility.DisplayProgressBar("Installing ConfuserEx", "Downloading...", 0f);
                    
                    try
                    {
                        bool success = ConfuserExManager.EnsureInstalled((progress, statusText) =>
                        {
                            EditorUtility.DisplayProgressBar("Installing ConfuserEx", statusText, progress);
                        });
                        
                        EditorUtility.ClearProgressBar();
                        
                        if (success)
                        {
                            EditorUtility.DisplayDialog(
                                "Installation Complete",
                                "ConfuserEx has been successfully installed!\n\n" +
                                "You can now use obfuscation in your package exports.",
                                "OK"
                            );
                        }
                        else
                        {
                            EditorUtility.DisplayDialog(
                                "Installation Failed",
                                "Failed to install ConfuserEx. Check the console for details.",
                                "OK"
                            );
                        }
                    }
                    finally
                    {
                        EditorUtility.ClearProgressBar();
                    }
                }
            }
        }
        
        [MenuItem("Tools/YUCP/Others/Package Exporter/Smart Version Bump/Scan for @bump Directives")]
        public static void ScanProjectForVersionDirectives()
        {
            var stats = ProjectVersionScanner.GetProjectStats();
            
            string message = stats.TotalFilesWithDirectives > 0
                ? stats.ToString() + "\n\nUse 'Auto-Increment Version' in your export profiles to automatically bump these versions on export."
                : "No @bump directives found in the project.\n\n" +
                  "Add directives like:\n" +
                  "  // @bump semver:patch\n" +
                  "  // @bump dotted_tail\n" +
                  "  // @bump wordnum\n\n" +
                  "to your source files, then enable 'Auto-Increment Version' in your export profile.";
            
            EditorUtility.DisplayDialog(
                "Version Directive Statistics",
                message,
                "OK"
            );
        }

        [MenuItem("Tools/YUCP/Others/Installation/Revert Last Package Update")]
        public static void RevertLastPackageUpdate()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Revert Last Update",
                "This will restore files from the last update backup. Continue?",
                "Revert",
                "Cancel"
            );

            if (!confirmed)
                return;

            if (UpdateTransaction.TryRollbackLast(out string message))
            {
                EditorUtility.DisplayDialog("Update Reverted", message, "OK");
                AssetDatabase.Refresh();
            }
            else
            {
                EditorUtility.DisplayDialog("Revert Failed", message, "OK");
            }
        }
        
        private static void CreateCustomVersionRuleInternal()
        {
            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            string directory = "Assets";
            
            if (!string.IsNullOrEmpty(selectedPath))
            {
                if (Directory.Exists(selectedPath))
                {
                    directory = selectedPath;
                }
                else
                {
                    directory = Path.GetDirectoryName(selectedPath);
                }
            }
            
            var customRule = ScriptableObject.CreateInstance<CustomVersionRule>();
            customRule.ruleName = "my_custom_rule";
            customRule.displayName = "My Custom Rule";
            customRule.description = "Custom version bumping rule";
            customRule.regexPattern = @"\b(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\b";
            customRule.ruleType = CustomVersionRule.RuleType.Semver;
            customRule.exampleInput = "1.0.0";
            customRule.exampleOutput = "1.0.1";
            
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(directory, "CustomVersionRule.asset"));
            
            AssetDatabase.CreateAsset(customRule, assetPath);
            AssetDatabase.SaveAssets();
            
            Selection.activeObject = customRule;
            EditorGUIUtility.PingObject(customRule);
            
            Debug.Log($"[YUCP] Created custom version rule: {assetPath}");
        }
    }
}
