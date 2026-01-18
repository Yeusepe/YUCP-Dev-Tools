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
            bottomBar.name = "yucp-bottom-bar";
            
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
            // We now have a single "Export" button that triggers an overlay
            _exportButton = new Button(OnExportButtonClicked);
            _exportButton.text = "Export";
            _exportButton.AddToClassList("yucp-button");
            _exportButton.AddToClassList("yucp-button-primary");
            _exportButton.AddToClassList("yucp-button-large");
            exportContainer.Add(_exportButton);
            
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

        private VisualElement _exportOptionsOverlay;
        private Button _exportButton;

        private void CreateExportOptionsOverlay()
        {
            if (_exportOptionsOverlay != null) return;

            // Create the overlay container ( Teaching Tip style )
            _exportOptionsOverlay = new VisualElement();
            _exportOptionsOverlay.AddToClassList("yucp-teaching-tip-overlay");
            _exportOptionsOverlay.style.position = Position.Absolute;
            _exportOptionsOverlay.style.bottom = 60; // Just above the bottom bar
            _exportOptionsOverlay.style.right = 20;
            _exportOptionsOverlay.style.display = DisplayStyle.None;
            // Add to root so it floats above everything
            rootVisualElement.Add(_exportOptionsOverlay);
            
            var container = new VisualElement();
            container.AddToClassList("yucp-teaching-tip-container");
            _exportOptionsOverlay.Add(container);

            var title = new Label("Export Options");
            title.AddToClassList("yucp-teaching-tip-title");
            container.Add(title);

            var description = new Label("Choose how you want to export your packages.");
            description.AddToClassList("yucp-teaching-tip-description");
            container.Add(description);
            
            var divider = new VisualElement();
            divider.AddToClassList("yucp-teaching-tip-divider");
            container.Add(divider);

            // Export Selected Button (in overlay)
            var exportSelectedBtn = new Button(() => {
                ExportSelectedProfiles();
                ToggleExportOptions(false);
            });
            exportSelectedBtn.text = "Export Selected Profile";
            exportSelectedBtn.name = "exportSelectedBtnOverlay"; // ID for finding it later maybe
            exportSelectedBtn.AddToClassList("yucp-button");
            exportSelectedBtn.AddToClassList("yucp-button-primary");
            exportSelectedBtn.style.marginBottom = 5;
            container.Add(exportSelectedBtn);

            // Export All Button (in overlay)
            var exportAllBtn = new Button(() => {
                ExportAllProfiles();
                ToggleExportOptions(false);
            });
            exportAllBtn.text = "Export All Profiles";
            exportAllBtn.AddToClassList("yucp-button");
            exportAllBtn.style.marginBottom = 5;
            container.Add(exportAllBtn);

            // Click outside to close (backdrop)
            // Since we can't easily do a full screen backdrop without blocking clicks to other things potentially, 
            // or we do want to block. 
            // Let's add a transparent backdrop behind the tip.
            var backdrop = new VisualElement();
            backdrop.style.position = Position.Absolute;
            backdrop.style.top = -10000; // Big coverage
            backdrop.style.left = -10000;
            backdrop.style.width = 20000;
            backdrop.style.height = 20000;
            backdrop.style.backgroundColor = new StyleColor(Color.clear); 
            // backdrop.RegisterCallback<ClickEvent>(evt => ToggleExportOptions(false)); // This might block interaction with the button itself if not careful
            // Actually, best is to register click on root to close if not clicking overlay?
            // Or just use a backdrop behind the container but inside the overlay element.
            
            // Re-structure: Overlay is full screen, invisible. Container is positioned.
            _exportOptionsOverlay.style.top = 0;
            _exportOptionsOverlay.style.left = 0;
            _exportOptionsOverlay.style.right = 0;
            _exportOptionsOverlay.style.bottom = 0;
            _exportOptionsOverlay.style.backgroundColor = new StyleColor(Color.clear);
            _exportOptionsOverlay.RegisterCallback<MouseDownEvent>(evt => {
                if (evt.target == _exportOptionsOverlay) ToggleExportOptions(false);
            });

            container.style.position = Position.Absolute;
            container.style.bottom = 60;
            container.style.right = 10;
        }

        private void ToggleExportOptions(bool? show = null)
        {
            CreateExportOptionsOverlay();
            
            bool isVisible = _exportOptionsOverlay.style.display == DisplayStyle.Flex;
            bool targetState = show ?? !isVisible;
            
            if (targetState)
            {
                _exportOptionsOverlay.style.display = DisplayStyle.Flex;
                
                // Update button texts dynamically based on selection
                var exportSelectedBtn = _exportOptionsOverlay.Q<Button>("exportSelectedBtnOverlay");
                if (exportSelectedBtn != null)
                {
                    if (selectedProfileIndices.Count <= 1)
                        exportSelectedBtn.text = "Export Selected Profile";
                    else
                        exportSelectedBtn.text = $"Export Selected Profiles ({selectedProfileIndices.Count})";
                        
                    exportSelectedBtn.SetEnabled(selectedProfileIndices.Count > 0);
                }
            }
            else
            {
                _exportOptionsOverlay.style.display = DisplayStyle.None;
            }
        }

        private void OnExportButtonClicked()
        {
            ToggleExportOptions();
        }
    }
}
