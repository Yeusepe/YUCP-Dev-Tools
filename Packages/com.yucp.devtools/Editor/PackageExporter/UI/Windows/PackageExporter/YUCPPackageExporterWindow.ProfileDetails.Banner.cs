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

            _bannerGradientOverlay.pickingMode = PickingMode.Position;

            bannerContainer.Add(_bannerGradientOverlay);




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


            var bannerBounds = _bannerContainer.worldBound;
            var metadataBounds = _metadataSection.worldBound;
            var scrollViewBounds = _rightPaneScrollView.worldBound;




            var metadataTop = 10f;

            if (bannerBounds.y >= 0 && metadataBounds.y >= 0)
            {

                var relativeTop = metadataBounds.y - bannerBounds.y;
                if (relativeTop > 0 && relativeTop < BannerHeight)
                {
                    metadataTop = relativeTop;
                }
            }


            if (metadataTop <= 0)
                metadataTop = 10f;


            var centerYInBanner = metadataTop * 0.70f;


            var buttonY = bannerBounds.y - scrollViewBounds.y + centerYInBanner - (buttonHeight / 2f);
            var buttonX = bannerBounds.x - scrollViewBounds.x + (bannerWidth - buttonWidth) / 2f;

            _changeBannerButton.style.left = buttonX;
            _changeBannerButton.style.top = buttonY;
        }

        private void SetupParallaxScrolling()
        {
            var scroller = _rightPaneScrollView.verticalScroller;
            scroller.valueChanged -= UpdateBannerParallax;
            scroller.valueChanged += UpdateBannerParallax;
            UpdateBannerParallax(scroller.value);
        }

        private void UpdateBannerParallax(float scrollY)
        {
            if (_bannerImageContainer != null)
                _bannerImageContainer.style.translate = new Translate(0, scrollY * 0.93f);
            if (_bannerGradientOverlay != null)
                _bannerGradientOverlay.style.translate = new Translate(0, scrollY * 0.931f);
        }

        private void OnBannerGeometryChanged(GeometryChangedEvent evt) => UpdateBannerButtonPosition();

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
