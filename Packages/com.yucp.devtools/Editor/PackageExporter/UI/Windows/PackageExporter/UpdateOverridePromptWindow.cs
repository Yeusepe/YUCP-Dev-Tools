using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal class UpdateOverridePromptWindow : EditorWindow
    {
        private static Action _onDone;
        private static List<string> _items;
        private static string _reason;
        private static List<string> _changedItems;

        public static void ShowWindow(List<string> items, string reason, List<string> changedItems, Action onDone)
        {
            _items = items ?? new List<string>();
            _reason = reason ?? "";
            _changedItems = changedItems ?? new List<string>();
            _onDone = onDone;

            var window = GetWindow<UpdateOverridePromptWindow>(true, "Save Overrides", true);
            window.minSize = new Vector2(520, 360);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 16;
            rootVisualElement.style.paddingRight = 16;
            rootVisualElement.style.paddingTop = 12;
            rootVisualElement.style.paddingBottom = 12;

            var title = new Label("Save Your Overrides Before Updating");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14;
            title.style.marginBottom = 6;
            rootVisualElement.Add(title);

            var info = new Label(
                "Before continuing, please save any custom materials/textures or changes you want to keep " +
                "by moving or duplicating them to a safe folder.\n\n" +
                "When you’re done, click OK to continue.");
            info.style.whiteSpace = WhiteSpace.Normal;
            info.style.opacity = 0.8f;
            info.style.marginBottom = 10;
            rootVisualElement.Add(info);

            if (!string.IsNullOrEmpty(_reason))
            {
                var reasonLabel = new Label("Reason: " + _reason);
                reasonLabel.style.fontSize = 11;
                reasonLabel.style.opacity = 0.7f;
                reasonLabel.style.whiteSpace = WhiteSpace.Normal;
                reasonLabel.style.marginBottom = 8;
                rootVisualElement.Add(reasonLabel);
            }

            var listLabel = new Label("Prefab overrides detected:");
            listLabel.style.fontSize = 11;
            listLabel.style.opacity = 0.7f;
            listLabel.style.marginBottom = 4;
            rootVisualElement.Add(listLabel);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.marginBottom = 10;

            if (_items != null && _items.Count > 0)
            {
                foreach (var item in _items)
                {
                    var row = new Label("• " + item);
                    row.style.whiteSpace = WhiteSpace.Normal;
                    row.style.fontSize = 11;
                    row.style.marginBottom = 2;
                    scroll.Add(row);
                }
            }
            else
            {
                var none = new Label("No overrides detected.");
                none.style.opacity = 0.6f;
                scroll.Add(none);
            }

            rootVisualElement.Add(scroll);

            if (_changedItems != null && _changedItems.Count > 0)
            {
                var changedLabel = new Label("Changed assets detected:");
                changedLabel.style.fontSize = 11;
                changedLabel.style.opacity = 0.7f;
                changedLabel.style.marginBottom = 4;
                rootVisualElement.Add(changedLabel);

                var changedScroll = new ScrollView(ScrollViewMode.Vertical);
                changedScroll.style.flexGrow = 1;
                changedScroll.style.marginBottom = 10;
                foreach (var item in _changedItems)
                {
                    var row = new Label("• " + item);
                    row.style.whiteSpace = WhiteSpace.Normal;
                    row.style.fontSize = 11;
                    row.style.marginBottom = 2;
                    changedScroll.Add(row);
                }
                rootVisualElement.Add(changedScroll);
            }

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.FlexEnd;

            var okButton = new Button(() =>
            {
                Close();
                _onDone?.Invoke();
                _onDone = null;
                _items = null;
                _changedItems = null;
                _reason = null;
            })
            { text = "OK, Continue" };
            okButton.AddToClassList("yucp-button");
            okButton.AddToClassList("yucp-button-primary");
            buttonRow.Add(okButton);

            rootVisualElement.Add(buttonRow);
        }
    }
}
