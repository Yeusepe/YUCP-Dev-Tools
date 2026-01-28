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
            _profileDetailsContainer.style.flexBasis = StyleKeyword.Auto;
            _rightPaneScrollView.Add(_profileDetailsContainer);
            
            // Top Navigation Bar (Sticky Header)
            _topNavBar = new TopNavBar(OnTopNavClicked, ShowUnifiedMenu, ToggleOverlay);
            _topNavBar.style.display = DisplayStyle.None; // Hidden by default until profile loaded
            
            
            rightPane.Add(_topNavBar); 
            
            
            rightPane.Add(_rightPaneScrollView);
            
            _topNavBar.BringToFront();
            
            
            _rightPaneScrollView.verticalScroller.valueChanged += OnRightPaneScroll;
            
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
            
            // Detailed step log so user sees what's happening (avoids "is it stuck?" anxiety)
            _progressDetailScroll = new ScrollView(ScrollViewMode.Vertical);
            _progressDetailScroll.AddToClassList("yucp-progress-detail-scroll");
            _progressDetailScroll.style.maxHeight = 100;
            _progressDetailScroll.style.marginTop = 6;
            _progressDetail = new Label("");
            _progressDetail.AddToClassList("yucp-progress-detail");
            _progressDetail.style.whiteSpace = WhiteSpace.Normal;
            _progressDetail.style.unityTextAlign = TextAnchor.UpperLeft;
            _progressDetailScroll.Add(_progressDetail);
            _progressContainer.Add(_progressDetailScroll);
            
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

        private MotionHandle _topBarMotion;

        private void OnExportButtonClicked()
        {
            ToggleExportOptions();
        }

        private void OnTopNavClicked(string targetId)
        {
            if (_profileDetailsContainer == null) return;

            VisualElement target = null;
            switch(targetId)
            {
                case "General":
                    target = _profileDetailsContainer.Q("section-metadata");
                    break;
                case "Options":
                    target = _profileDetailsContainer.Q("section-settings");
                    break;
                case "Folders":
                    target = _profileDetailsContainer.Q("section-content");
                    break;
                case "Files":
                    target = _profileDetailsContainer.Q("section-files");
                    if (target == null) target = _profileDetailsContainer.Q("section-filters");
                    break;
                case "Dependencies":
                    target = _profileDetailsContainer.Q("section-dependencies");
                    break;
                case "Security":
                    target = _profileDetailsContainer.Q("section-advanced");
                    if (target == null) target = _profileDetailsContainer.Q("section-signing");
                    break;
                case "Actions":
                    target = _profileDetailsContainer.Q("section-actions");
                    break;
            }

            if (target != null)
            {
                ScrollToSection(target);
            }
        }

        private void OnRightPaneScroll(float offset)
        {
            if (_profileDetailsContainer == null || _topNavBar == null) return;
            // Only update if visible
            if (_profileDetailsContainer.style.display == DisplayStyle.None) return;

            var sections = new[]
            {
                ("section-metadata", "General"),
                ("section-settings", "Options"),
                ("section-content", "Folders"),
                ("section-files", "Files"),
                ("section-filters", "Files"),
                ("section-dependencies", "Dependencies"),
                ("section-advanced", "Security"),
                ("section-signing", "Security"),
                ("section-actions", "Actions")
            };

            float scrollY = _rightPaneScrollView.verticalScroller.value;
            float topPadding = GetTopNavHeight() + 12f;
            string activeTab = null;

            foreach (var (sectionId, tabId) in sections)
            {
                var section = _profileDetailsContainer.Q(sectionId);
                if (section == null) continue;

                float sectionY = GetElementYInContent(_rightPaneScrollView, section);
                if (float.IsNaN(sectionY)) continue;

                if (sectionY - topPadding <= scrollY + 1f)
                {
                    activeTab = tabId;
                }
            }

            if (!string.IsNullOrEmpty(activeTab))
            {
                _topNavBar.SetActiveTab(activeTab);
            }
        }

        private void ScrollToSection(VisualElement target)
        {
            if (_rightPaneScrollView == null || target == null) return;

            float topPadding = GetTopNavHeight() + 12f;
            float targetOffset = GetElementYInContent(_rightPaneScrollView, target) - topPadding;

            if (float.IsNaN(targetOffset)) return;

            float maxScroll = _rightPaneScrollView.verticalScroller.highValue;
            if (targetOffset < 0) targetOffset = 0;
            if (targetOffset > maxScroll) targetOffset = maxScroll;

            _rightPaneScrollView.verticalScroller.value = targetOffset;
        }

        private float GetElementYInContent(ScrollView scrollView, VisualElement element)
        {
            if (scrollView == null || element == null) return float.NaN;
            var content = scrollView.contentContainer;

            try
            {
                Vector2 elementLocalPos = content.WorldToLocal(element.worldBound.position);
                float y = elementLocalPos.y;
                if (!float.IsNaN(y)) return y;
            }
            catch { }

            float sumY = 0f;
            VisualElement current = element;
            while (current != null && current != content)
            {
                if (!float.IsNaN(current.layout.y))
                    sumY += current.layout.y;
                current = current.hierarchy.parent;
            }

            return sumY;
        }

        private float GetTopNavHeight()
        {
            if (_topNavBar == null) return 0f;
            float h = _topNavBar.resolvedStyle.height;
            if (h <= 0) h = _topNavBar.layout.height;
            return h > 0 ? h : 0f;
        }
        
        private void ShowUnifiedMenu()
        {
            var menu = new GenericMenu();

            // Export Submenu
            foreach (var item in GetExportMenuItems())
            {
                 if (item.IsSeparator) menu.AddSeparator("Export/");
                 else menu.AddItem(new GUIContent($"Export/{item.Label}"), false, () => item.Callback?.Invoke());
            }

            menu.AddSeparator("");

            // Texture Array Builder
            menu.AddItem(new GUIContent("Texture Array Builder"), false, () => {
                var windowType = System.Type.GetType("YUCP.DevTools.Editor.TextureArrayBuilder.TextureArrayBuilderWindow, yucp.devtools.Editor");
                if (windowType != null)
                {
                    var method = windowType.GetMethod("ShowWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    method?.Invoke(null, null);
                }
            });

            menu.AddSeparator("");

            // Utilities Submenu
            foreach (var item in GetUtilitiesMenuItems())
            {
                 if (item.IsSeparator) menu.AddSeparator("Utilities/");
                 else menu.AddItem(new GUIContent($"Utilities/{item.Label}"), false, () => item.Callback?.Invoke());
            }

            // Debug Submenu
            foreach (var item in GetDebugMenuItems())
            {
                 if (item.IsSeparator) menu.AddSeparator("Debug/");
                 else menu.AddItem(new GUIContent($"Debug/{item.Label}"), false, () => item.Callback?.Invoke());
            }

            menu.AddSeparator("");

            // Help Submenu
            foreach (var item in GetHelpMenuItems())
            {
                 if (item.IsSeparator) menu.AddSeparator("Help/");
                 else menu.AddItem(new GUIContent($"Help/{item.Label}"), false, () => item.Callback?.Invoke());
            }

            // Compact Mode
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Compact View"), _isCompactMode, ToggleCompactMode);

            menu.ShowAsContext();
        }
    }
}
