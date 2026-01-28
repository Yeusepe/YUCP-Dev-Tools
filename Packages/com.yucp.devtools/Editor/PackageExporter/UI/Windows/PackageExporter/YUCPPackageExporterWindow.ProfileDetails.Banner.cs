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
        private VisualElement CreateBannerSection(ExportProfile profile)
        {
            var bannerContainer = new VisualElement();
            bannerContainer.AddToClassList("yucp-banner-container");
            _bannerContainer = bannerContainer;
            
            bannerContainer.style.position = Position.Relative;
            bannerContainer.style.height = BannerHeight;
            bannerContainer.style.marginBottom = 0;
            bannerContainer.style.width = Length.Percent(100);
            bannerContainer.style.paddingLeft = 0;
            bannerContainer.style.paddingRight = 0;
            bannerContainer.style.paddingTop = 0;
            bannerContainer.style.paddingBottom = 0;
            bannerContainer.style.flexShrink = 0;
            bannerContainer.style.overflow = Overflow.Visible;
            
            _bannerImageContainer = new VisualElement();
            _bannerImageContainer.AddToClassList("yucp-banner-image-container");
            _bannerImageContainer.style.position = Position.Absolute;
            _bannerImageContainer.style.top = 0;
            _bannerImageContainer.style.left = 0;
            _bannerImageContainer.style.right = 0;
            _bannerImageContainer.style.bottom = 0;
            Texture2D displayBanner = profile?.banner;
            if (displayBanner == null)
            {
                displayBanner = GetPlaceholderTexture();
            }
            if (displayBanner != null)
            {
                _bannerImageContainer.style.backgroundImage = new StyleBackground(displayBanner);
                
                // Check if banner is a GIF and animate it
                string bannerPath = AssetDatabase.GetAssetPath(displayBanner);
                if (!string.IsNullOrEmpty(bannerPath) && bannerPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    StartGifAnimation(_bannerImageContainer, bannerPath);
                }
            }
            bannerContainer.Add(_bannerImageContainer);
            
            _bannerGradientOverlay = new VisualElement();
            _bannerGradientOverlay.AddToClassList("yucp-banner-gradient-overlay");
            _bannerGradientOverlay.style.position = Position.Absolute;
            _bannerGradientOverlay.style.top = 0;
            _bannerGradientOverlay.style.left = 0;
            _bannerGradientOverlay.style.right = 0;
            _bannerGradientOverlay.style.bottom = -10;
            if (bannerGradientTexture != null)
            {
                _bannerGradientOverlay.style.backgroundImage = new StyleBackground(bannerGradientTexture);
            }
            // Gradient overlay needs to receive pointer events for hover detection but allow button clicks to pass through
            _bannerGradientOverlay.pickingMode = PickingMode.Position;
            // Add as sibling so it can use its own parallax factor
            bannerContainer.Add(_bannerGradientOverlay);
            
            // Button will be added to _profileDetailsContainer after contentWrapper
            // so it's on top of everything
            
            return bannerContainer;
        }

        private void UpdateBannerButtonPosition()
        {
            if (_changeBannerButton == null || _bannerContainer == null || _metadataSection == null || _rightPaneScrollView == null)
                return;
            
            var bannerWidth = _bannerContainer.resolvedStyle.width;
            var buttonWidth = _changeBannerButton.resolvedStyle.width;
            var buttonHeight = 24f;
            
            if (bannerWidth <= 0 || buttonWidth <= 0)
                return;
            
            // Get world positions to calculate button position relative to ScrollView
            var bannerBounds = _bannerContainer.worldBound;
            var metadataBounds = _metadataSection.worldBound;
            var scrollViewBounds = _rightPaneScrollView.worldBound;
            
            // Calculate metadata section position relative to banner
            // contentWrapper has marginTop = -400, banner height is 410
            // Metadata section is the first element in contentWrapper
            var metadataTop = 10f;
            
            if (bannerBounds.y >= 0 && metadataBounds.y >= 0)
            {
                // Calculate the difference in world Y coordinates
                var relativeTop = metadataBounds.y - bannerBounds.y;
                if (relativeTop > 0 && relativeTop < BannerHeight)
                {
                    metadataTop = relativeTop;
                }
            }
            
            // Ensure metadataTop is not negative or zero
            if (metadataTop <= 0)
                metadataTop = 10f;
            
            // Center vertically between top of banner and metadata section top            
            var centerYInBanner = metadataTop * 0.70f;
            
            // Calculate button position relative to ScrollView
            var buttonY = bannerBounds.y - scrollViewBounds.y + centerYInBanner - (buttonHeight / 2f);
            var buttonX = bannerBounds.x - scrollViewBounds.x + (bannerWidth - buttonWidth) / 2f;
            
            _changeBannerButton.style.left = buttonX;
            _changeBannerButton.style.top = buttonY;
        }

        private void SetupParallaxScrolling()
        {
            if (_rightPaneScrollView == null) return;
            if (_bannerImageContainer == null && _bannerGradientOverlay == null) return;
            
            // Separate parallax speeds so image and gradient can move differently than the content
            // and differently from each other.
            const float imageParallaxSpeed = 0.07f;
            const float gradientParallaxSpeed = 0.069f;
            
            Action<float> updateParallax = (scrollY) =>
            {
                // Image offset: container moves at (1 - speed) of scroll
                if (_bannerImageContainer != null)
                {
                    var imageOffset = scrollY * (1.0f - imageParallaxSpeed);
                    _bannerImageContainer.style.translate = new StyleTranslate(new Translate(0, imageOffset));
                }
                
                // Gradient offset: independent speed so it can drift differently
                if (_bannerGradientOverlay != null)
                {
                    var gradientOffset = scrollY * (1.0f - gradientParallaxSpeed);
                    _bannerGradientOverlay.style.translate = new StyleTranslate(new Translate(0, gradientOffset));
                }
            };
            
            var verticalScroller = _rightPaneScrollView.verticalScroller;
            if (verticalScroller != null)
            {
                verticalScroller.valueChanged += (value) =>
                {
                    updateParallax(value);
                };
                
                // Set initial position
                updateParallax(verticalScroller.value);
            }
            
            // Also use scheduled callback as backup for continuous updates
            _rightPaneScrollView.schedule.Execute(() =>
            {
                if (_rightPaneScrollView == null) return;
                if (_bannerImageContainer == null && _bannerGradientOverlay == null) return;
                
                // Get current scroll position
                var scrollOffset = _rightPaneScrollView.scrollOffset;
                var scrollY = scrollOffset.y;
                
                updateParallax(scrollY);
            }).Every(16); // Update approximately every frame (60fps = ~16ms per frame)
        }

        private void ForceBannerFullWidth()
        {
            if (_rightPaneScrollView == null || _profileDetailsContainer == null) return;
            
            var bannerWrapper = _profileDetailsContainer.Q("banner-wrapper-fullwidth");
            if (bannerWrapper == null) return;
            
            // Access ScrollView's content viewport to allow overflow
            var contentViewport = _rightPaneScrollView.Q(className: "unity-scroll-view__content-viewport");
            if (contentViewport != null)
            {
                contentViewport.style.overflow = Overflow.Visible;
            }
            
            // Get the right pane (parent of ScrollView) for full width
            var rightPane = _rightPaneScrollView?.parent;
            if (rightPane != null)
            {
                var paneWidth = rightPane.resolvedStyle.width;
                if (paneWidth > 0)
                {
                    // Set wrapper to full pane width and use negative margins to break out
                    bannerWrapper.style.position = Position.Relative;
                    bannerWrapper.style.width = paneWidth;
                    bannerWrapper.style.minWidth = paneWidth;
                    bannerWrapper.style.maxWidth = paneWidth;
                    bannerWrapper.style.marginLeft = -20;
                    bannerWrapper.style.marginRight = -24;
                    bannerWrapper.style.marginTop = -20;
                    bannerWrapper.style.marginBottom = 0;
                    bannerWrapper.style.paddingLeft = 0;
                    bannerWrapper.style.paddingRight = 0;
                    bannerWrapper.style.paddingTop = 0;
                    bannerWrapper.style.paddingBottom = 0;
                    bannerWrapper.style.left = 0;
                    bannerWrapper.style.flexShrink = 0;
                    
                    // Set banner container to match wrapper width
                    var bannerContainer = bannerWrapper.Q(className: "yucp-banner-container");
                    if (bannerContainer != null)
                    {
                        bannerContainer.style.width = paneWidth;
                        bannerContainer.style.minWidth = paneWidth;
                        bannerContainer.style.maxWidth = paneWidth;
                        bannerContainer.style.marginLeft = 0;
                        bannerContainer.style.marginRight = 0;
                        bannerContainer.style.marginTop = 0;
                    }
                }
            }
        }

        private void OnChangeBannerClicked(ExportProfile profile)
        {
            if (profile == null)
            {
                EditorUtility.DisplayDialog("No Profile", "Cannot change banner for this profile.", "OK");
                return;
            }
            
            string bannerPath = EditorUtility.OpenFilePanel("Select Banner Image", "", "png,jpg,jpeg,gif");
            if (!string.IsNullOrEmpty(bannerPath))
            {
                string projectPath = "Assets/YUCP/ExportProfiles/Banners/";
                if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles/Banners"))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/YUCP"))
                        AssetDatabase.CreateFolder("Assets", "YUCP");
                    if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles"))
                        AssetDatabase.CreateFolder("Assets/YUCP", "ExportProfiles");
                    AssetDatabase.CreateFolder("Assets/YUCP/ExportProfiles", "Banners");
                }
                
                string fileName = Path.GetFileName(bannerPath);
                string targetPath = projectPath + fileName;
                
                File.Copy(bannerPath, targetPath, true);
                AssetDatabase.ImportAsset(targetPath);
                AssetDatabase.Refresh();
                
                profile.banner = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                
                UpdateProfileDetails();
            }
        }

    }
}
