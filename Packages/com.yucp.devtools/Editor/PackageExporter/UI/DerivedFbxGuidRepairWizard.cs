using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal class DerivedFbxGuidRepairWizard : EditorWindow
    {
        private const int StepSelectPackage = 0;
        private const int StepSelectFbx = 1;
        private const int StepConfirm = 2;

        private int _stepIndex;
        private string _packagePath;
        private string _derivedAssetPath;
        private string _derivedAssetName;
        private string _statusMessage;
        private bool _isBusy;

        private Array _importItems;
        private readonly List<PackageFbxItem> _fbxItems = new List<PackageFbxItem>();
        private int _selectedIndex = -1;

        private bool _waitingForImport;
        private string _pendingPackageName;
        private PackageFbxItem _pendingItem;
        private GuidRepairUtility.RepairResult _repairResult;

        private static readonly Type ImportPackageItemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
        private static readonly FieldInfo DestinationAssetPathField = ImportPackageItemType?.GetField("destinationAssetPath");
        private static readonly FieldInfo SourceFolderField = ImportPackageItemType?.GetField("sourceFolder");
        private static readonly FieldInfo ExportedAssetPathField = ImportPackageItemType?.GetField("exportedAssetPath");
        private static readonly FieldInfo EnabledStatusField = ImportPackageItemType?.GetField("enabledStatus");
        private static readonly FieldInfo IsFolderField = ImportPackageItemType?.GetField("isFolder");
        private static readonly PropertyInfo EnabledStatusProperty = ImportPackageItemType?.GetProperty("enabledStatus");

        internal static void Open(ModelImporter importer)
        {
            if (importer == null)
            {
                EditorUtility.DisplayDialog("Repair Derived FBX", "Select a derived FBX asset first.", "OK");
                return;
            }

            var window = GetWindow<DerivedFbxGuidRepairWizard>(true, "Repair Derived FBX");
            window.Initialize(importer.assetPath);
            window.ShowUtility();
        }

        private void Initialize(string derivedAssetPath)
        {
            _derivedAssetPath = derivedAssetPath;
            _derivedAssetName = Path.GetFileName(derivedAssetPath);
            _stepIndex = StepSelectPackage;
            _packagePath = null;
            _statusMessage = null;
            _fbxItems.Clear();
            _selectedIndex = -1;
            _importItems = null;
            _waitingForImport = false;
            _pendingPackageName = null;
            _pendingItem = null;
        }

        private void OnDisable()
        {
            UnregisterImportCallbacks();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);

            DrawHeader();

            EditorGUILayout.Space(6);

            switch (_stepIndex)
            {
                case StepSelectPackage:
                    DrawSelectPackageStep();
                    break;
                case StepSelectFbx:
                    DrawSelectFbxStep();
                    break;
                case StepConfirm:
                    DrawConfirmStep();
                    break;
            }

            EditorGUILayout.Space(8);
            DrawFooterButtons();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Repair Derived FBX", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Use this only if you replaced the original FBX and want to bring the original back.",
                MessageType.Info);
        }

        private void DrawSelectPackageStep()
        {
            EditorGUILayout.LabelField("Step 1 of 3: Choose the package", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Pick the .unitypackage that contains the original FBX you want to restore.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField("Unity Package", _packagePath ?? "(none)");
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("Browse...", GUILayout.Width(90)))
                {
                    string path = EditorUtility.OpenFilePanel("Select Unity Package", "", "unitypackage");
                    if (!string.IsNullOrEmpty(path))
                    {
                        LoadPackage(path);
                    }
                }
            }

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Warning);
            }
        }

        private void DrawSelectFbxStep()
        {
            EditorGUILayout.LabelField("Step 2 of 3: Select the original FBX", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Choose the FBX that should become the base for this derived file.",
                MessageType.None);

            if (_fbxItems.Count == 0)
            {
                EditorGUILayout.HelpBox("No FBX files found. Go back and choose another package.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < _fbxItems.Count; i++)
                {
                    var item = _fbxItems[i];
                    string label = $"{item.DisplayName}  ({item.DestinationAssetPath})";
                    bool selected = _selectedIndex == i;
                    if (GUILayout.Toggle(selected, label, "Radio"))
                    {
                        _selectedIndex = i;
                    }
                }
            }

            if (TryGetSelectionIssue(out string issueMessage, out MessageType issueType))
            {
                EditorGUILayout.HelpBox(issueMessage, issueType);
            }
        }

        private void DrawConfirmStep()
        {
            EditorGUILayout.LabelField("Step 3 of 3: Confirm and restore", EditorStyles.miniBoldLabel);

            if (_selectedIndex < 0 || _selectedIndex >= _fbxItems.Count)
            {
                EditorGUILayout.HelpBox("No FBX selected. Go back and choose one.", MessageType.Warning);
                return;
            }

            var selectedItem = _fbxItems[_selectedIndex];

            EditorGUILayout.HelpBox(
                "Here is exactly what will happen:",
                MessageType.Info);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("1) Fix the derived file ID (GUID)");
                EditorGUILayout.LabelField("2) Import only the selected original FBX");
                EditorGUILayout.LabelField("3) Set the original FBX as the base");
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Derived FBX", _derivedAssetName);
                EditorGUILayout.LabelField("Package FBX", selectedItem.DisplayName);
                EditorGUILayout.LabelField("Import Path", selectedItem.DestinationAssetPath);
            }

            EditorGUILayout.HelpBox("This changes asset IDs. Make a backup first.", MessageType.Warning);
        }

        private void DrawFooterButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel", GUILayout.Width(90)))
                {
                    Close();
                    return;
                }

                GUILayout.FlexibleSpace();

                if (_stepIndex > StepSelectPackage && GUILayout.Button("Back", GUILayout.Width(80)))
                {
                    _stepIndex--;
                    return;
                }

                if (_stepIndex < StepConfirm)
                {
                    using (new EditorGUI.DisabledGroupScope(!CanGoNext()))
                    {
                        if (GUILayout.Button("Continue", GUILayout.Width(90)))
                        {
                            _stepIndex++;
                        }
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledGroupScope(_isBusy || _waitingForImport))
                    {
                        if (GUILayout.Button("Restore Original Now", GUILayout.Width(150)))
                        {
                            RunRepairAndImport();
                        }
                    }
                }
            }
        }

        private bool CanGoNext()
        {
            if (_stepIndex == StepSelectPackage)
            {
                return !string.IsNullOrEmpty(_packagePath) && _fbxItems.Count > 0;
            }

            if (_stepIndex == StepSelectFbx)
            {
                if (!(_selectedIndex >= 0 && _selectedIndex < _fbxItems.Count))
                {
                    return false;
                }

                return !IsSelectionBlocked();
            }

            return false;
        }

        private bool IsSelectionBlocked()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _fbxItems.Count) return false;

            var item = _fbxItems[_selectedIndex];
            if (string.Equals(item.DestinationAssetPath, _derivedAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private bool TryGetSelectionIssue(out string message, out MessageType type)
        {
            message = null;
            type = MessageType.None;

            if (_selectedIndex < 0 || _selectedIndex >= _fbxItems.Count)
            {
                return false;
            }

            var item = _fbxItems[_selectedIndex];
            if (string.Equals(item.DestinationAssetPath, _derivedAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                message = "The selected FBX would import over the derived FBX. Move the derived FBX to a different folder, or choose a different package FBX.";
                type = MessageType.Warning;
                return true;
            }

            string existingGuid = AssetDatabase.AssetPathToGUID(item.DestinationAssetPath);
            if (!string.IsNullOrEmpty(existingGuid))
            {
                message = "A file already exists at the import path. Importing will replace it.";
                type = MessageType.Info;
                return true;
            }

            return false;
        }

        private void LoadPackage(string path)
        {
            _packagePath = path;
            _statusMessage = null;
            _fbxItems.Clear();
            _selectedIndex = -1;

            if (!File.Exists(path))
            {
                _statusMessage = "File not found.";
                return;
            }

            _importItems = ExtractImportItems(path, out string error);
            if (_importItems == null || _importItems.Length == 0)
            {
                _statusMessage = string.IsNullOrEmpty(error) ? "Could not read package contents." : error;
                return;
            }

            BuildFbxItemList(_importItems, _fbxItems);
            if (_fbxItems.Count == 0)
            {
                _statusMessage = "No FBX files found in that package.";
            }
        }

        private void RunRepairAndImport()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _fbxItems.Count)
            {
                EditorUtility.DisplayDialog("Repair Derived FBX", "Select an FBX first.", "OK");
                return;
            }

            _isBusy = true;

            _repairResult = GuidRepairUtility.RepairDerivedGuid(_derivedAssetPath, true);
            if (!_repairResult.success)
            {
                _isBusy = false;
                EditorUtility.DisplayDialog("Repair Derived FBX", _repairResult.errorMessage, "OK");
                return;
            }

            if (!string.IsNullOrEmpty(_repairResult.warningMessage))
            {
                Debug.LogWarning($"[YUCP] {_repairResult.warningMessage}");
            }

            _pendingItem = _fbxItems[_selectedIndex];
            if (!StartImportSelectedFbx(_packagePath, _importItems, _pendingItem, out string importError))
            {
                _isBusy = false;
                EditorUtility.DisplayDialog("Repair Derived FBX", importError, "OK");
                return;
            }
        }

        private bool StartImportSelectedFbx(string packagePath, Array importItems, PackageFbxItem selectedItem, out string error)
        {
            error = null;
            if (importItems == null || importItems.Length == 0)
            {
                error = "Package contents were not loaded.";
                return false;
            }

            if (ImportPackageItemType == null || DestinationAssetPathField == null)
            {
                error = "Unity package import APIs are not available in this Unity version.";
                return false;
            }

            string selectedPath = selectedItem.DestinationAssetPath;
            foreach (var item in importItems)
            {
                if (item == null) continue;
                string destinationPath = GetDestinationPath(item);
                bool isFolder = GetIsFolder(item);

                bool shouldEnable = item == selectedItem.ImportItem;
                if (!shouldEnable && isFolder && IsParentPath(destinationPath, selectedPath))
                {
                    shouldEnable = true;
                }

                SetEnabledStatus(item, shouldEnable ? 1 : -1);
            }

            RegisterImportCallbacks();
            _waitingForImport = true;
            _pendingPackageName = Path.GetFileName(packagePath);
            if (!TryImportPackageAssets(packagePath, importItems, out error))
            {
                _waitingForImport = false;
                UnregisterImportCallbacks();
                return false;
            }
            return true;
        }

        private void RegisterImportCallbacks()
        {
            AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
            AssetDatabase.importPackageFailed -= OnImportPackageFailed;
            AssetDatabase.importPackageCancelled -= OnImportPackageCancelled;

            AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
            AssetDatabase.importPackageFailed += OnImportPackageFailed;
            AssetDatabase.importPackageCancelled += OnImportPackageCancelled;
        }

        private void UnregisterImportCallbacks()
        {
            AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
            AssetDatabase.importPackageFailed -= OnImportPackageFailed;
            AssetDatabase.importPackageCancelled -= OnImportPackageCancelled;
        }

        private void OnImportPackageCompleted(string packageName)
        {
            if (!_waitingForImport || !IsExpectedPackage(packageName)) return;

            _waitingForImport = false;
            UnregisterImportCallbacks();

            FinalizeBaseAssignment();
        }

        private void OnImportPackageFailed(string packageName, string error)
        {
            if (!_waitingForImport || !IsExpectedPackage(packageName)) return;

            _waitingForImport = false;
            _isBusy = false;
            UnregisterImportCallbacks();

            EditorUtility.DisplayDialog("Repair Derived FBX", $"Package import failed:\n{error}", "OK");
        }

        private void OnImportPackageCancelled(string packageName)
        {
            if (!_waitingForImport || !IsExpectedPackage(packageName)) return;

            _waitingForImport = false;
            _isBusy = false;
            UnregisterImportCallbacks();

            EditorUtility.DisplayDialog("Repair Derived FBX", "Package import was cancelled.", "OK");
        }

        private bool IsExpectedPackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(_packagePath))
            {
                return false;
            }

            if (string.Equals(packageName, _packagePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(_pendingPackageName) &&
                string.Equals(packageName, _pendingPackageName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private void FinalizeBaseAssignment()
        {
            try
            {
                AssetDatabase.Refresh();

                string importedGuid = AssetDatabase.AssetPathToGUID(_pendingItem.DestinationAssetPath);
                if (string.IsNullOrEmpty(importedGuid))
                {
                    _isBusy = false;
                    EditorUtility.DisplayDialog(
                        "Repair Derived FBX",
                        "The FBX imported, but Unity did not return a GUID for it. Try reimporting or restarting Unity.",
                        "OK");
                    return;
                }

                var importer = AssetImporter.GetAtPath(_derivedAssetPath) as ModelImporter;
                DerivedSettings settings;
                if (!DerivedSettingsUtility.TryRead(importer, out settings))
                {
                    settings = new DerivedSettings();
                }

                if (settings.baseGuids == null)
                {
                    settings.baseGuids = new List<string>();
                }

                if (settings.baseGuids.Count == 0)
                {
                    settings.baseGuids.Add(importedGuid);
                }
                else
                {
                    settings.baseGuids[0] = importedGuid;
                }

                importer.userData = JsonUtility.ToJson(settings);
                EditorUtility.SetDirty(importer);
                AssetDatabase.SaveAssets();

                _isBusy = false;

                string resultMessage =
                    $"Done!\n\n" +
                    $"Derived FBX: {_derivedAssetName}\n" +
                    $"Base FBX: {_pendingItem.DisplayName}\n" +
                    $"Files updated: {_repairResult.updatedCount}\n\n" +
                    "Your derived FBX now has a new ID, and the original FBX is set as the base.";

                EditorUtility.DisplayDialog("Repair Complete", resultMessage, "OK");
                Close();
            }
            catch (Exception ex)
            {
                _isBusy = false;
                EditorUtility.DisplayDialog("Repair Derived FBX", $"Failed to finish setup: {ex.Message}", "OK");
            }
        }

        private static Array ExtractImportItems(string packagePath, out string error)
        {
            error = null;
            try
            {
                var packageUtilityType = Type.GetType("UnityEditor.PackageUtility, UnityEditor.CoreModule");
                if (packageUtilityType == null)
                {
                    error = "Unity package utilities are not available in this Unity version.";
                    return null;
                }

                var extractMethods = packageUtilityType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                foreach (var method in extractMethods)
                {
                    if (!string.Equals(method.Name, "ExtractAndPrepareAssetList", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!method.ReturnType.IsArray)
                    {
                        continue;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length == 0 || parameters[0].ParameterType != typeof(string))
                    {
                        continue;
                    }

                    object[] args = new object[parameters.Length];
                    args[0] = packagePath;
                    for (int i = 1; i < parameters.Length; i++)
                    {
                        var paramType = parameters[i].ParameterType;
                        if (paramType == typeof(bool))
                        {
                            args[i] = false;
                        }
                        else if (paramType.IsByRef && paramType.GetElementType() == typeof(string))
                        {
                            args[i] = null;
                        }
                        else if (parameters[i].HasDefaultValue)
                        {
                            args[i] = parameters[i].DefaultValue;
                        }
                        else
                        {
                            args[i] = null;
                        }
                    }

                    var result = method.Invoke(null, args);
                    if (result is Array array && array.Length > 0)
                    {
                        return array;
                    }
                }

                error = "Unity package extraction method was not found.";
                return null;
            }
            catch (Exception ex)
            {
                error = $"Failed to read package contents: {ex.Message}";
                return null;
            }
        }

        private static bool TryImportPackageAssets(string packagePath, Array importItems, out string error)
        {
            error = null;
            try
            {
                var packageUtilityType = Type.GetType("UnityEditor.PackageUtility, UnityEditor.CoreModule");
                if (packageUtilityType == null)
                {
                    error = "Unity package utilities are not available in this Unity version.";
                    return false;
                }

                var importMethod = packageUtilityType.GetMethod("ImportPackageAssets", BindingFlags.Public | BindingFlags.Static);
                if (importMethod == null)
                {
                    error = "Unity package import method was not found.";
                    return false;
                }

                importMethod.Invoke(null, new object[] { packagePath, importItems });
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to start import: {ex.Message}";
                return false;
            }
        }

        private static void BuildFbxItemList(Array importItems, List<PackageFbxItem> output)
        {
            if (importItems == null || output == null) return;

            foreach (var item in importItems)
            {
                if (item == null) continue;
                string destinationPath = GetDestinationPath(item);
                if (string.IsNullOrEmpty(destinationPath)) continue;

                if (!destinationPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string displayName = Path.GetFileName(destinationPath);
                string guid = TryReadGuidFromItem(item);

                output.Add(new PackageFbxItem
                {
                    ImportItem = item,
                    DestinationAssetPath = destinationPath,
                    DisplayName = displayName,
                    Guid = guid
                });
            }
        }

        private static string TryReadGuidFromItem(object item)
        {
            if (item == null || SourceFolderField == null) return null;

            try
            {
                string sourceFolder = SourceFolderField.GetValue(item) as string;
                if (string.IsNullOrEmpty(sourceFolder))
                {
                    return null;
                }

                string metaPath = Path.Combine(sourceFolder, "asset.meta");
                if (!File.Exists(metaPath))
                {
                    string exportedPath = ExportedAssetPathField?.GetValue(item) as string;
                    if (!string.IsNullOrEmpty(exportedPath))
                    {
                        metaPath = Path.Combine(sourceFolder, exportedPath + ".meta");
                    }
                }

                if (!File.Exists(metaPath))
                {
                    return null;
                }

                string metaContent = File.ReadAllText(metaPath);
                return MetaFileManager.ExtractGuidFromMetaContent(metaContent);
            }
            catch
            {
                return null;
            }
        }

        private static string GetDestinationPath(object item)
        {
            if (DestinationAssetPathField == null || item == null) return null;
            return DestinationAssetPathField.GetValue(item) as string;
        }

        private static bool GetIsFolder(object item)
        {
            if (IsFolderField == null || item == null) return false;
            try
            {
                object value = IsFolderField.GetValue(item);
                return value is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsParentPath(string parentPath, string childPath)
        {
            if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(childPath)) return false;
            if (string.Equals(parentPath, "Assets", StringComparison.OrdinalIgnoreCase)) return true;
            if (!childPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase)) return false;
            return childPath.Length == parentPath.Length || childPath[parentPath.Length] == '/' || childPath[parentPath.Length] == '\\';
        }

        private static void SetEnabledStatus(object item, int status)
        {
            if (item == null) return;

            if (EnabledStatusField != null)
            {
                EnabledStatusField.SetValue(item, status);
                return;
            }

            if (EnabledStatusProperty != null && EnabledStatusProperty.CanWrite)
            {
                EnabledStatusProperty.SetValue(item, status, null);
            }
        }

        private class PackageFbxItem
        {
            public object ImportItem;
            public string DestinationAssetPath;
            public string DisplayName;
            public string Guid;
        }
    }
}
