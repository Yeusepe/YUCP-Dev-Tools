using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal class UpdateArchiveRestoreWindow : EditorWindow
    {
        private TextField _suffixField;
        private Label _resultLabel;

        public static void ShowWindow(string defaultSuffix = "_old")
        {
            var window = GetWindow<UpdateArchiveRestoreWindow>(true, "Restore Archived Assets", true);
            window.minSize = new Vector2(520, 260);
            window.ShowUtility();
            window.SetSuffix(defaultSuffix);
        }

        private void OnEnable()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 16;
            rootVisualElement.style.paddingRight = 16;
            rootVisualElement.style.paddingTop = 12;
            rootVisualElement.style.paddingBottom = 12;

            var title = new Label("Restore Archived Assets");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14;
            title.style.marginBottom = 6;
            rootVisualElement.Add(title);

            var info = new Label(
                "This restores assets from folders that were archived during update (e.g., *_old). " +
                "If the destination already exists, it will be moved to a *_conflict folder before restoration.");
            info.style.whiteSpace = WhiteSpace.Normal;
            info.style.opacity = 0.8f;
            info.style.marginBottom = 10;
            rootVisualElement.Add(info);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 12;

            var label = new Label("Archive Suffix");
            label.style.minWidth = 110;
            label.style.fontSize = 11;
            label.style.opacity = 0.7f;
            row.Add(label);

            _suffixField = new TextField();
            _suffixField.isDelayed = true;
            _suffixField.AddToClassList("yucp-input");
            _suffixField.style.flexGrow = 1;
            row.Add(_suffixField);

            rootVisualElement.Add(row);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.FlexEnd;

            var restoreButton = new Button(() =>
            {
                string suffix = string.IsNullOrWhiteSpace(_suffixField.value) ? "_old" : _suffixField.value.Trim();
                string message = RestoreArchivedAssets(suffix);
                _resultLabel.text = message;
                AssetDatabase.Refresh();
            })
            { text = "Restore" };
            restoreButton.AddToClassList("yucp-button");
            restoreButton.AddToClassList("yucp-button-primary");
            buttonRow.Add(restoreButton);

            var cancelButton = new Button(Close) { text = "Cancel" };
            cancelButton.AddToClassList("yucp-button");
            cancelButton.style.marginLeft = 6;
            buttonRow.Add(cancelButton);

            rootVisualElement.Add(buttonRow);

            _resultLabel = new Label("");
            _resultLabel.style.whiteSpace = WhiteSpace.Normal;
            _resultLabel.style.opacity = 0.7f;
            _resultLabel.style.marginTop = 10;
            rootVisualElement.Add(_resultLabel);
        }

        private void SetSuffix(string value)
        {
            if (_suffixField != null)
            {
                _suffixField.value = string.IsNullOrWhiteSpace(value) ? "_old" : value;
            }
        }

        private static string RestoreArchivedAssets(string suffix)
        {
            try
            {
                int restored = 0;
                int conflicts = 0;

                foreach (var root in new[] { "Assets", "Packages" })
                {
                    string rootPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", root));
                    if (!Directory.Exists(rootPath))
                        continue;

                    foreach (var dir in Directory.GetDirectories(rootPath))
                    {
                        string name = Path.GetFileName(dir);
                        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string targetName = name.Substring(0, name.Length - suffix.Length);
                        if (string.IsNullOrEmpty(targetName))
                            continue;

                        string targetPath = Path.Combine(rootPath, targetName);

                        if (Directory.Exists(targetPath))
                        {
                            string conflictPath = targetPath + "_conflict_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            Directory.Move(targetPath, conflictPath);
                            conflicts++;
                        }

                        Directory.Move(dir, targetPath);
                        restored++;
                    }
                }

                if (restored == 0)
                    return "No archived folders were found for the specified suffix.";

                return $"Restored {restored} folder(s). Conflicts moved: {conflicts}.";
            }
            catch (Exception ex)
            {
                return $"Restore failed: {ex.Message}";
            }
        }
    }
}

