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
        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("yucp-window");
            
            // Load shared design system stylesheet first
            var sharedStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.devtools/Editor/Styles/YucpDesignSystem.uss");
            if (sharedStyleSheet != null)
            {
                root.styleSheets.Add(sharedStyleSheet);
            }
            
            // Load component-specific stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.devtools/Editor/PackageExporter/Styles/PackageExporter.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
            
            // Main container
            var mainContainer = new VisualElement();
            mainContainer.AddToClassList("yucp-main-container");
            
            // Top Bar
            mainContainer.Add(CreateTopBar());

            // Content Container (Left + Right Panes)
            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("yucp-content-container");
            
            // Create overlay backdrop (for mobile menu)
            _overlayBackdrop = new VisualElement();
            _overlayBackdrop.AddToClassList("yucp-overlay-backdrop");
            _overlayBackdrop.RegisterCallback<ClickEvent>(evt => CloseOverlay());
            _overlayBackdrop.style.display = DisplayStyle.None;
            _overlayBackdrop.style.visibility = Visibility.Hidden;
            _contentContainer.Add(_overlayBackdrop);
            
            // Load saved width or use default (shared between overlay and normal pane)
            float savedWidth = EditorPrefs.GetFloat(LeftPaneWidthKey, DefaultLeftPaneWidth);
            savedWidth = Mathf.Clamp(savedWidth, MinLeftPaneWidth, MaxLeftPaneWidth);
            
            // Create left pane overlay (for mobile)
            _leftPaneOverlay = CreateLeftPane(isOverlay: true);
            _leftPaneOverlay.AddToClassList("yucp-left-pane-overlay");
            // Use the same width as the normal left pane
            _leftPaneOverlay.style.width = new Length(savedWidth, LengthUnit.Pixel);
            _leftPaneOverlay.style.minWidth = MinLeftPaneWidth;
            _leftPaneOverlay.style.maxWidth = MaxLeftPaneWidth;
            _leftPaneOverlay.style.display = DisplayStyle.None;
            _leftPaneOverlay.style.visibility = Visibility.Hidden;
            _contentContainer.Add(_leftPaneOverlay);
            
            // Create normal left pane
            _leftPane = CreateLeftPane(isOverlay: false);
            _leftPane.style.width = new Length(savedWidth, LengthUnit.Pixel);
            _leftPane.style.minWidth = MinLeftPaneWidth;
            _leftPane.style.maxWidth = MaxLeftPaneWidth;
            _leftPane.style.flexShrink = 0;
            _leftPane.style.flexGrow = 0;
            _contentContainer.Add(_leftPane);
            
            // Create resize handle between left and right panes
            _resizeHandle = new VisualElement();
            _resizeHandle.AddToClassList("yucp-resize-handle");
            _resizeHandle.style.width = 4f;
            _resizeHandle.style.flexShrink = 0;
            _resizeHandle.style.cursor = new StyleCursor(StyleKeyword.None);
            _resizeHandle.pickingMode = PickingMode.Position;
            
            // Setup resize handle mouse events
            SetupLeftPaneResizeHandle();
            
            _contentContainer.Add(_resizeHandle);
            
            _contentContainer.Add(CreateRightPane());
            mainContainer.Add(_contentContainer);
            
            // Bottom Bar
            _bottomBar = CreateBottomBar();
            mainContainer.Add(_bottomBar);
            
            root.Add(mainContainer);
            
            // Optional non-intrusive support toast (appears rarely)
            _supportToast = CreateSupportToast();
            if (_supportToast != null)
            {
                // Start hidden for fade-in animation
                _supportToast.style.opacity = 0;
                _supportToast.style.translate = new Translate(0, -20);
                
                root.Add(_supportToast);
                
                // Animate in
                root.schedule.Execute(() => {
                    if (_supportToast != null)
                    {
                        _supportToast.style.opacity = 1;
                        _supportToast.style.translate = new Translate(0, 0);
                    }
                }).StartingIn(100);
            }
            
            // Schedule delayed rename checks
            root.schedule.Execute(CheckDelayedRename).Every(100);
            
            // Initial UI update
            UpdateProfileList();
            UpdateBottomBar();
            
            // Register for geometry changes to handle responsive layout
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            
            // Schedule initial responsive check after layout is ready
            root.schedule.Execute(() => 
            {
                UpdateResponsiveLayout(rootVisualElement.resolvedStyle.width);
            }).StartingIn(100);
        }

    }
}
