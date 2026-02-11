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
        private VisualElement CreateFormRow(string labelText, string tooltip = "", bool isWarningOrError = false)
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-form-row");
            
            var label = new Label(labelText);
            label.AddToClassList("yucp-form-label");
            if (!string.IsNullOrEmpty(tooltip) && (!_isCompactMode || isWarningOrError))
            {
                label.tooltip = tooltip;
            }
            row.Add(label);
            
            return row;
        }

        private VisualElement CreateCollapsibleHeader(string title, Func<bool> getExpanded, Action<bool> setExpanded, Action onToggle)
        {
            var header = new VisualElement();
            header.AddToClassList("yucp-inspector-header");
            
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("yucp-section-title");
            header.Add(titleLabel);
            
            var toggleButton = new Button();
            
            void UpdateButtonText()
            {
                toggleButton.text = getExpanded() ? "▼" : "▶";
            }
            
            toggleButton.clicked += () =>
            {
                bool current = getExpanded();
                setExpanded(!current);
                UpdateButtonText();
                onToggle?.Invoke();
            };
            
            UpdateButtonText();
            toggleButton.AddToClassList("yucp-button");
            toggleButton.AddToClassList("yucp-button-small");
            header.Add(toggleButton);
            
            return header;
        }

    }
}
