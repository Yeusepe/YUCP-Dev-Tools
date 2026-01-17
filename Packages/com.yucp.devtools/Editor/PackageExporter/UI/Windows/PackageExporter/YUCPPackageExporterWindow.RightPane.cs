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
        private VisualElement CreateRightPane()
        {
            var rightPane = new VisualElement();
            rightPane.AddToClassList("yucp-right-pane");
            
            _rightPaneScrollView = new ScrollView();
            _rightPaneScrollView.AddToClassList("yucp-panel");
            _rightPaneScrollView.AddToClassList("yucp-scrollview");
            
            // Empty state
            _emptyState = CreateEmptyState();
            _rightPaneScrollView.Add(_emptyState);
            
            _profileDetailsContainer = new VisualElement();
            _profileDetailsContainer.style.display = DisplayStyle.None;
            _profileDetailsContainer.style.flexGrow = 0;
            _profileDetailsContainer.style.flexShrink = 1;
            _profileDetailsContainer.style.flexBasis = StyleKeyword.Auto;
            _rightPaneScrollView.Add(_profileDetailsContainer);
            
            rightPane.Add(_rightPaneScrollView);
            return rightPane;
        }

        private VisualElement CreateEmptyState()
        {
            var emptyState = new VisualElement();
            emptyState.AddToClassList("yucp-empty-state");
            
            var title = new Label("No Profile Selected");
            title.AddToClassList("yucp-empty-state-title");
            emptyState.Add(title);
            
            var description = new Label("Select a profile from the list or create a new one");
            description.AddToClassList("yucp-empty-state-description");
            emptyState.Add(description);
            
            return emptyState;
        }

        private VisualElement CreateBottomBar()
        {
            var bottomBar = new VisualElement();
            bottomBar.AddToClassList("yucp-bottom-bar");
            
            // Export buttons container
            var exportContainer = new VisualElement();
            exportContainer.AddToClassList("yucp-export-buttons");
            
            // Info section (left side)
            var infoSection = new VisualElement();
            infoSection.AddToClassList("yucp-export-info");
            
            _multiSelectInfo = new VisualElement();
            _multiSelectInfo.AddToClassList("yucp-multi-select-info");
            _multiSelectInfo.style.display = DisplayStyle.None;
            var multiSelectText = new Label();
            multiSelectText.AddToClassList("yucp-multi-select-text");
            multiSelectText.name = "multiSelectText";
            _multiSelectInfo.Add(multiSelectText);
            infoSection.Add(_multiSelectInfo);
            
            exportContainer.Add(infoSection);
            
            // Buttons (right side)
            _exportSelectedButton = new Button(ExportSelectedProfiles);
            _exportSelectedButton.AddToClassList("yucp-button");
            _exportSelectedButton.AddToClassList("yucp-button-primary");
            _exportSelectedButton.AddToClassList("yucp-button-large");
            exportContainer.Add(_exportSelectedButton);
            
            _exportAllButton = new Button(() => ExportAllProfiles()) { text = "Export All Profiles" };
            _exportAllButton.AddToClassList("yucp-button");
            _exportAllButton.AddToClassList("yucp-button-export");
            _exportAllButton.AddToClassList("yucp-button-large");
            exportContainer.Add(_exportAllButton);
            
            bottomBar.Add(exportContainer);
            
            // Progress container
            _progressContainer = new VisualElement();
            _progressContainer.AddToClassList("yucp-progress-container");
            _progressContainer.style.display = DisplayStyle.None;
            
            var progressBar = new VisualElement();
            progressBar.AddToClassList("yucp-progress-bar");
            
            _progressFill = new VisualElement();
            _progressFill.AddToClassList("yucp-progress-fill");
            _progressFill.style.width = Length.Percent(0);
            progressBar.Add(_progressFill);
            
            _progressText = new Label("0%");
            _progressText.AddToClassList("yucp-progress-text");
            progressBar.Add(_progressText);
            
            _progressContainer.Add(progressBar);
            bottomBar.Add(_progressContainer);
            
            return bottomBar;
        }

    }
}
