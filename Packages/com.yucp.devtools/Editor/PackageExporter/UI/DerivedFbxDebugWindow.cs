using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public class DerivedFbxDebugWindow : EditorWindow
    {
        private Vector2 scrollPosition;
        private string logText = "";

        private Object baseFbxObject;
        private Object modifiedFbxObject;
        private string fbxOutputPath = "Assets/DerivedFBX_Output.fbx";
        private string targetGuid = "";
        private bool saveIntermediateAsset = false;
        private string intermediateAssetPath = "Assets/DerivedFbxAsset_Temp.asset";

        [MenuItem("Tools/YUCP/Debug/Derived FBX Builder")]
        public static void ShowWindow()
        {
            GetWindow<DerivedFbxDebugWindow>("Derived FBX Builder");
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            EditorGUILayout.LabelField("Derived FBX Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            EditorGUILayout.HelpBox(
                "Builds a DerivedFbxAsset from base + modified FBX, then reconstructs the FBX file.\n" +
                "All in one workflow - just select your files and click Build.",
                MessageType.Info
            );
            
            EditorGUILayout.Space();
            
            baseFbxObject = EditorGUILayout.ObjectField(
                "Base FBX",
                baseFbxObject,
                typeof(GameObject),
                false
            );
            
            modifiedFbxObject = EditorGUILayout.ObjectField(
                "Modified FBX",
                modifiedFbxObject,
                typeof(GameObject),
                false
            );
            
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);
            
            fbxOutputPath = EditorGUILayout.TextField("FBX Output Path", fbxOutputPath);
            
            if (GUILayout.Button("Browse FBX Output Path"))
            {
                string initialDir = Path.GetDirectoryName(fbxOutputPath);
                if (string.IsNullOrEmpty(initialDir) || !Path.IsPathRooted(initialDir))
                {
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    initialDir = Path.Combine(projectPath, "Assets");
                }
                
                string selectedPath = EditorUtility.SaveFilePanel(
                    "Save Derived FBX (can be in another Unity project)",
                    initialDir,
                    Path.GetFileNameWithoutExtension(fbxOutputPath),
                    "fbx"
                );
                
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    if (selectedPath.StartsWith(projectPath))
                    {
                        fbxOutputPath = "Assets" + selectedPath.Substring(projectPath.Length).Replace('\\', '/');
                    }
                    else
                    {
                        fbxOutputPath = selectedPath;
                    }
                }
            }
            
            if (!string.IsNullOrEmpty(fbxOutputPath) && Path.IsPathRooted(fbxOutputPath) && !fbxOutputPath.StartsWith("Assets"))
            {
                EditorGUILayout.HelpBox(
                    $"External path selected. FBX and .meta will be copied to:\n{fbxOutputPath}",
                    MessageType.Info
                );
            }
            
            targetGuid = EditorGUILayout.TextField("Target GUID (optional - for meta preservation)", targetGuid);
            
            EditorGUILayout.Space();
            
            saveIntermediateAsset = EditorGUILayout.Toggle("Save Intermediate Asset", saveIntermediateAsset);
            
            if (saveIntermediateAsset)
            {
                intermediateAssetPath = EditorGUILayout.TextField("Intermediate Asset Path", intermediateAssetPath);
                
                if (GUILayout.Button("Browse Intermediate Asset Path"))
                {
                    string selectedPath = EditorUtility.SaveFilePanelInProject(
                        "Save Intermediate DerivedFbxAsset",
                        Path.GetFileNameWithoutExtension(intermediateAssetPath),
                        "asset",
                        "Select where to save the intermediate asset"
                    );
                    
                    if (!string.IsNullOrEmpty(selectedPath))
                    {
                        intermediateAssetPath = selectedPath;
                    }
                }
            }
            
            EditorGUILayout.Space();
            
            GUI.enabled = baseFbxObject != null && modifiedFbxObject != null && !string.IsNullOrEmpty(fbxOutputPath);
            
            if (GUILayout.Button("Build Derived FBX", GUILayout.Height(40)))
            {
                BuildDerivedFbx();
            }
            
            GUI.enabled = true;
            
            EditorGUILayout.Space();
            
            if (!string.IsNullOrEmpty(logText))
            {
                EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(logText, GUILayout.Height(200));
                
                if (GUILayout.Button("Clear Log"))
                {
                    logText = "";
                }
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void BuildDerivedFbx()
        {
            if (baseFbxObject == null)
            {
                LogError("Base FBX is not set.");
                return;
            }

            if (modifiedFbxObject == null)
            {
                LogError("Modified FBX is not set.");
                return;
            }

            if (string.IsNullOrEmpty(fbxOutputPath))
            {
                LogError("FBX output path is not set.");
                return;
            }

            string baseFbxPath = AssetDatabase.GetAssetPath(baseFbxObject);
            string modifiedFbxPath = AssetDatabase.GetAssetPath(modifiedFbxObject);

            if (string.IsNullOrEmpty(baseFbxPath) || !baseFbxPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                LogError($"Invalid base FBX path: {baseFbxPath}");
                return;
            }

            if (string.IsNullOrEmpty(modifiedFbxPath) || !modifiedFbxPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                LogError($"Invalid modified FBX path: {modifiedFbxPath}");
                return;
            }

            Log("=== Starting Derived FBX Build Workflow ===");
            Log($"Base FBX: {baseFbxPath}");
            Log($"Modified FBX: {modifiedFbxPath}");
            Log($"Output Path: {fbxOutputPath}");

            try
            {
                EditorUtility.DisplayProgressBar("Building Derived FBX", "Building DerivedFbxAsset...", 0.3f);

                var policy = new DerivedFbxAsset.Policy();

                var hints = new DerivedFbxAsset.UIHints
                {
                    friendlyName = Path.GetFileNameWithoutExtension(modifiedFbxPath),
                    thumbnail = null,
                    category = ""
                };

                var seeds = new DerivedFbxAsset.SeedMaps();

                Log("Step 1: Building DerivedFbxAsset...");
                DerivedFbxAsset asset = PatchBuilder.BuildDerivedFbxAsset(
                    baseFbxPath,
                    modifiedFbxPath,
                    policy,
                    hints,
                    seeds
                );

                if (asset == null)
                {
                    EditorUtility.ClearProgressBar();
                    LogError("BuildDerivedFbxAsset returned null. Check console for errors.");
                    return;
                }

                asset.baseFbxGuid = AssetDatabase.AssetPathToGUID(baseFbxPath);
                asset.originalDerivedFbxPath = modifiedFbxPath;

                if (saveIntermediateAsset)
                {
                    string directory = Path.GetDirectoryName(intermediateAssetPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    AssetDatabase.CreateAsset(asset, intermediateAssetPath);
                    AssetDatabase.SaveAssets();
                    Log($"  Saved intermediate asset: {intermediateAssetPath}");
                }

                Log($"  [OK] DerivedFbxAsset created with .hdiff file: {asset.hdiffFilePath ?? "none"}");

                EditorUtility.DisplayProgressBar("Building Derived FBX", "Reconstructing FBX...", 0.7f);

                string targetGuidToUse = targetGuid;
                if (string.IsNullOrEmpty(targetGuidToUse))
                {
                    string modifiedFbxGuid = AssetDatabase.AssetPathToGUID(modifiedFbxPath);
                    if (!string.IsNullOrEmpty(modifiedFbxGuid))
                    {
                        targetGuidToUse = modifiedFbxGuid;
                        asset.derivedFbxGuid = modifiedFbxGuid;
                    }
                }
                else
                {
                    asset.derivedFbxGuid = targetGuidToUse;
                }

                Log("Step 2: Reconstructing FBX from DerivedFbxAsset...");
                
                bool isExternalPath = Path.IsPathRooted(fbxOutputPath) && !fbxOutputPath.StartsWith("Assets");
                string result;
                
                if (isExternalPath)
                {
                    result = BuildDerivedFbxExternal(
                        baseFbxPath,
                        asset,
                        fbxOutputPath,
                        modifiedFbxPath,
                        targetGuidToUse
                    );
                }
                else
                {
                    result = DerivedFbxBuilder.BuildDerivedFbx(
                        baseFbxPath,
                        asset,
                        fbxOutputPath,
                        targetGuidToUse
                    );
                }

                EditorUtility.ClearProgressBar();

                if (!string.IsNullOrEmpty(result))
                {
                    Log($"[OK] Successfully created derived FBX: {result}");
                    
                    if (!isExternalPath)
                    {
                        AssetDatabase.Refresh();
                    }

                    EditorUtility.DisplayDialog(
                        "Success",
                        $"Derived FBX created successfully:\n{result}\n\n" +
                        $"Binary patch applied from: {asset.hdiffFilePath ?? "unknown"}",
                        "OK"
                    );

                    if (!isExternalPath)
                    {
                        Object createdObject = AssetDatabase.LoadAssetAtPath<GameObject>(result);
                        if (createdObject != null)
                        {
                            Selection.activeObject = createdObject;
                            EditorGUIUtility.PingObject(createdObject);
                        }
                    }
                }
                else
                {
                    LogError("BuildDerivedFbx returned null. Check console for errors.");
                }
            }
            catch (System.Exception ex)
            {
                EditorUtility.ClearProgressBar();
                LogError($"Exception: {ex.GetType().Name}: {ex.Message}");
                LogError($"Stack Trace: {ex.StackTrace}");
                Debug.LogError($"[DerivedFbxDebugWindow] Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void Log(string message)
        {
            logText += $"[{System.DateTime.Now:HH:mm:ss}] {message}\n";
            Debug.Log($"[DerivedFbxDebugWindow] {message}");
        }

        private string BuildDerivedFbxExternal(
            string baseFbxPath,
            DerivedFbxAsset asset,
            string externalOutputPath,
            string modifiedFbxPath,
            string targetGuid)
        {
            try
            {
                string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string tempOutputPath = "Assets/Temp_DerivedFBX.fbx";
                
                Log($"  Building to temp location first: {tempOutputPath}");
                
                string tempResult = DerivedFbxBuilder.BuildDerivedFbx(
                    baseFbxPath,
                    asset,
                    tempOutputPath,
                    targetGuid
                );
                
                if (string.IsNullOrEmpty(tempResult))
                {
                    LogError("Failed to build FBX in temp location");
                    return null;
                }
                
                AssetDatabase.Refresh();
                
                string tempPhysicalPath = Path.Combine(projectPath, tempResult.Replace('/', Path.DirectorySeparatorChar));
                string externalDir = Path.GetDirectoryName(externalOutputPath);
                
                if (!Directory.Exists(externalDir))
                {
                    Directory.CreateDirectory(externalDir);
                    Log($"  Created directory: {externalDir}");
                }
                
                if (File.Exists(externalOutputPath))
                {
                    File.Delete(externalOutputPath);
                }
                
                File.Copy(tempPhysicalPath, externalOutputPath, true);
                Log($"  Copied FBX to: {externalOutputPath}");
                
                string modifiedFbxPhysicalPath = Path.Combine(projectPath, modifiedFbxPath.Replace('/', Path.DirectorySeparatorChar));
                string modifiedMetaPath = modifiedFbxPhysicalPath + ".meta";
                string externalMetaPath = externalOutputPath + ".meta";
                
                if (File.Exists(modifiedMetaPath))
                {
                    string metaContent = File.ReadAllText(modifiedMetaPath);
                    
                    if (!string.IsNullOrEmpty(targetGuid))
                    {
                        metaContent = Regex.Replace(
                            metaContent,
                            @"guid:\s*[a-f0-9]{32}",
                            $"guid: {targetGuid}",
                            RegexOptions.IgnoreCase
                        );
                        Log($"  Updated GUID in .meta to: {targetGuid}");
                    }
                    
                    File.WriteAllText(externalMetaPath, metaContent);
                    Log($"  Copied .meta to: {externalMetaPath}");
                }
                else
                {
                    LogError($"  Warning: Could not find .meta file at: {modifiedMetaPath}");
                }
                
                AssetDatabase.DeleteAsset(tempResult);
                AssetDatabase.Refresh();
                
                string finalFbxPath = externalOutputPath;
                if (finalFbxPath.StartsWith(projectPath))
                {
                    finalFbxPath = "Assets" + finalFbxPath.Substring(projectPath.Length).Replace(Path.DirectorySeparatorChar, '/');
                    if (File.Exists(Path.Combine(projectPath, finalFbxPath.Replace("Assets/", ""))))
                    {
                        AssetDatabase.Refresh();
                        // Note: Binary patching preserves bone structure, so no need to update prefab references
                    }
                }
                
                return externalOutputPath;
            }
            catch (System.Exception ex)
            {
                LogError($"Exception in BuildDerivedFbxExternal: {ex.GetType().Name}: {ex.Message}");
                LogError($"Stack Trace: {ex.StackTrace}");
                return null;
            }
        }

        private void LogError(string message)
        {
            logText += $"[{System.DateTime.Now:HH:mm:ss}] ERROR: {message}\n";
            Debug.LogError($"[DerivedFbxDebugWindow] {message}");
        }
    }
}

