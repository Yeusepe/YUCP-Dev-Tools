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
        public void UpdateProfileDetails()
        {
            if (selectedProfile == null)
            {
                _emptyState.style.display = DisplayStyle.Flex;
                _profileDetailsContainer.style.display = DisplayStyle.None;
                return;
            }
            
            // Save current inspector height to profile before switching
            if (selectedProfile != null && currentResizableContainer != null)
            {
                float currentHeight = currentResizableContainer.resolvedStyle.height;
                if (currentHeight <= 0)
                {
                    currentHeight = currentResizableContainer.layout.height;
                }
                if (currentHeight > 0)
                {
                    Undo.RecordObject(selectedProfile, "Save Inspector Height");
                    selectedProfile.InspectorHeight = currentHeight;
                    EditorUtility.SetDirty(selectedProfile);
                }
            }
            
            _emptyState.style.display = DisplayStyle.None;
            _profileDetailsContainer.style.display = DisplayStyle.Flex;
            
            // Reset scroll position when switching profiles to prevent scroll position bug
            var oldScrollView = _profileDetailsContainer.Q<ScrollView>(className: "yucp-inspector-list");
            if (oldScrollView != null)
            {
                oldScrollView.verticalScroller.value = 0;
            }
            
            _profileDetailsContainer.Clear();
            
            // Reset resize tracking
            currentResizableContainer = null;
            currentResizeRect = Rect.zero;
            _bannerImageContainer = null;
            _bannerGradientOverlay = null;
            _metadataSection = null;
            _bannerContainer = null;
            
            if (_rightPaneScrollView != null)
            {
                var existingButtons = _rightPaneScrollView.Query<Button>(className: "yucp-overlay-hover-button").ToList();
                var oldButtons = _rightPaneScrollView.Query<Button>(className: "yucp-banner-change-button").ToList();
                existingButtons.AddRange(oldButtons);
                foreach (var btn in existingButtons)
                {
                    btn.RemoveFromHierarchy();
                }
                _changeBannerButton = null;
            }
            
            // Check if multiple profiles are selected
            if (selectedProfileIndices.Count > 1)
            {
                // Show bulk editor for multiple profiles
                var bulkEditorSection = CreateBulkEditorSection();
                _profileDetailsContainer.Add(bulkEditorSection);
                
                // Show summary for selected profiles
                var summarySection = CreateMultiProfileSummarySection();
                _profileDetailsContainer.Add(summarySection);
            }
            else
            {
                // Single profile editor
                // Remove ScrollView padding so banner can span full width
                _rightPaneScrollView.style.paddingLeft = 0;
                _rightPaneScrollView.style.paddingRight = 0;
                _rightPaneScrollView.style.paddingTop = 0;
                _rightPaneScrollView.style.paddingBottom = 0;
                
                // Banner Section (inside ScrollView so it scrolls with content) - spans full width
                var bannerSection = CreateBannerSection(selectedProfile);
                _profileDetailsContainer.Add(bannerSection);
                
                // Setup parallax scrolling for banner
                SetupParallaxScrolling();
                
                // Wrap all other sections in a container with padding
                // Position it over the banner with negative margin
                var contentWrapper = new VisualElement();
                contentWrapper.style.position = Position.Relative;
                contentWrapper.style.paddingLeft = 20;
                contentWrapper.style.paddingRight = 24;
                contentWrapper.style.paddingTop = 0;
                contentWrapper.style.paddingBottom = 20;
                contentWrapper.style.marginTop = -400;
                contentWrapper.style.flexGrow = 0;
                contentWrapper.style.flexShrink = 1;
                contentWrapper.style.flexBasis = StyleKeyword.Auto;
                
                // Make contentWrapper ignore pointer events in the banner area so hover works
                // But we can't easily do this, so we'll handle hover on the banner section itself
                
                // Package Metadata Section
                _metadataSection = CreateMetadataSection(selectedProfile);
                contentWrapper.Add(_metadataSection);
                
                // Register callback to update button position when metadata section geometry changes
                _metadataSection.RegisterCallback<GeometryChangedEvent>(evt => UpdateBannerButtonPosition());
                
                // Quick Summary Section
                var summarySection = CreateSummarySection(selectedProfile);
                contentWrapper.Add(summarySection);
                
                // Validation Section
                var validationSection = CreateValidationSection(selectedProfile);
                contentWrapper.Add(validationSection);
                
                // Export Options Section
                var optionsSection = CreateExportOptionsSection(selectedProfile);
                contentWrapper.Add(optionsSection);
                
                var foldersSection = CreateFoldersSection(selectedProfile);
                contentWrapper.Add(foldersSection);
                
                // Bundled Profiles Section (for composite profiles)
                var bundledProfilesSection = BundledProfilesSection.CreateBundledProfilesSection(selectedProfile, () => UpdateProfileDetails());
                contentWrapper.Add(bundledProfilesSection);
                
                // Export Inspector Section
                var inspectorSection = CreateExportInspectorSection(selectedProfile);
                contentWrapper.Add(inspectorSection);
                
                // Exclusion Filters Section
                var exclusionSection = CreateExclusionFiltersSection(selectedProfile);
                contentWrapper.Add(exclusionSection);
                
                // Dependencies Section
                var dependenciesSection = CreateDependenciesSection(selectedProfile);
                contentWrapper.Add(dependenciesSection);
                
                // Obfuscation Section
                var obfuscationSection = CreateObfuscationSection(selectedProfile);
                contentWrapper.Add(obfuscationSection);
                
                // Quick Actions
                var actionsSection = CreateQuickActionsSection(selectedProfile);
                contentWrapper.Add(actionsSection);
                
                // Package Signing Section
                var signingSection = CreatePackageSigningSection(selectedProfile);
                contentWrapper.Add(signingSection);
                
                _profileDetailsContainer.Add(contentWrapper);
                
                // Add banner button directly to ScrollView so it's definitely on top and clickable
                _changeBannerButton = new Button(() => OnChangeBannerClicked(selectedProfile));
                _changeBannerButton.text = "Change";
                _changeBannerButton.AddToClassList("yucp-overlay-hover-button");
                _changeBannerButton.style.position = Position.Absolute;
                _changeBannerButton.style.bottom = StyleKeyword.Auto;
                _changeBannerButton.style.left = StyleKeyword.Auto;
                _changeBannerButton.style.right = StyleKeyword.Auto;
                _changeBannerButton.style.opacity = 0f;
                _changeBannerButton.pickingMode = PickingMode.Ignore;
                
                // Register hover events on banner section to show/hide button
                Action showButton = () =>
                {
                    if (_changeBannerButton != null)
                    {
                        _changeBannerButton.style.opacity = 1f;
                        _changeBannerButton.pickingMode = PickingMode.Position;
                    }
                };
                Action hideButton = () =>
                {
                    if (_changeBannerButton != null)
                    {
                        _changeBannerButton.style.opacity = 0f;
                        _changeBannerButton.pickingMode = PickingMode.Ignore;
                    }
                };
                
                // Track mouse position to detect when hovering between top bar bottom and metadata top
                _rightPaneScrollView.RegisterCallback<MouseMoveEvent>(evt =>
                {
                    if (_changeBannerButton == null || _metadataSection == null) return;
                    
                    // Find top bar element
                    var topBar = rootVisualElement.Q(className: "yucp-top-bar");
                    if (topBar == null) return;
                    
                    var topBarBounds = topBar.worldBound;
                    var metadataBounds = _metadataSection.worldBound;
                    var scrollViewBounds = _rightPaneScrollView.worldBound;
                    
                    // Convert mouse position to world coordinates
                    var mouseY = scrollViewBounds.y + evt.localMousePosition.y;
                    
                    // Detection window: from bottom of top bar to top of metadata section
                    var detectionTop = topBarBounds.y + topBarBounds.height;
                    var detectionBottom = metadataBounds.y;
                    
                    bool isInDetectionWindow = mouseY >= detectionTop && mouseY <= detectionBottom;
                    
                    if (isInDetectionWindow && _changeBannerButton.style.opacity.value < 0.5f)
                    {
                        showButton();
                    }
                    else if (!isInDetectionWindow && _changeBannerButton.style.opacity.value > 0.5f)
                    {
                        hideButton();
                    }
                });
                
                
                // Update button position when geometry changes
                _rightPaneScrollView.RegisterCallback<GeometryChangedEvent>(evt => UpdateBannerButtonPosition());
                _rightPaneScrollView.Add(_changeBannerButton);
                
                if (selectedProfile != null && selectedProfile.foldersToExport.Count > 0)
                {
                    if (!selectedProfile.HasScannedAssets)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (selectedProfile != null)
                            {
                                ScanAssetsForInspector(selectedProfile, silent: true);
                            }
                        };
                    }
                }
                
                if (selectedProfile.dependencies.Count == 0 && selectedProfile.foldersToExport.Count > 0)
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (selectedProfile != null && selectedProfile.dependencies.Count == 0)
                        {
                            ScanProfileDependencies(selectedProfile, silent: true);
                        }
                    };
                }
            }
        }

    }
}
