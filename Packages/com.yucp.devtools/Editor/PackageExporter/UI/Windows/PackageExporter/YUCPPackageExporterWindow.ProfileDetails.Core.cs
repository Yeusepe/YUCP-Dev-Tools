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
        /// <summary>
        /// True if the profile has folders and/or bundled profiles to scan for the export inspector.
        /// </summary>
        private static bool ProfileHasContentToScan(ExportProfile profile)
        {
            return profile != null
                && (profile.foldersToExport.Count > 0 || profile.HasIncludedProfiles());
        }

        private EventCallback<MouseMoveEvent> _bannerHoverMove;

        public void UpdateProfileDetails()
        {
            if (_emptyState == null || _profileDetailsContainer == null || _rightPaneScrollView == null)
            {
                return;
            }

            if (selectedProfile == null)
            {
                _emptyState.style.display = DisplayStyle.Flex;
                _profileDetailsContainer.style.display = DisplayStyle.None;
                if (_topNavBar != null) _topNavBar.style.display = DisplayStyle.None;
                return;
            }


            if (selectedProfile != null && currentResizableContainer != null)
            {
                float currentHeight = currentResizableContainer.resolvedStyle.height;
                if (currentHeight <= 0)
                {
                    currentHeight = currentResizableContainer.layout.height;
                }
                if (currentHeight > 0 && !Mathf.Approximately(currentHeight, selectedProfile.InspectorHeight))
                {
                    Undo.RecordObject(selectedProfile, "Save Inspector Height");
                    selectedProfile.InspectorHeight = currentHeight;
                    EditorUtility.SetDirty(selectedProfile);
                }
            }

            _emptyState.style.display = DisplayStyle.None;
            _profileDetailsContainer.style.display = DisplayStyle.Flex;


            if (_topNavBar != null)
            {
                _topNavBar.style.display = DisplayStyle.Flex;
                _topNavBar.SetActiveTab("General");
            }


            var oldScrollView = _profileDetailsContainer.Q<ScrollView>(className: "yucp-inspector-list");
            if (oldScrollView != null)
            {
                oldScrollView.verticalScroller.value = 0;
            }

            _profileDetailsContainer.Clear();


            currentResizableContainer = null;
            currentResizeRect = Rect.zero;
            _bannerImageContainer = null;
            _bannerGradientOverlay = null;
            _metadataSection = null;
            _bannerContainer = null;
            _packageNameField = null;

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


            if (selectedProfileIndices.Count > 1)
            {

                var bulkEditorSection = CreateBulkEditorSection();
                _profileDetailsContainer.Add(bulkEditorSection);


                var summarySection = CreateMultiProfileSummarySection();
                _profileDetailsContainer.Add(summarySection);
            }
            else
            {


                _rightPaneScrollView.style.paddingLeft = 0;
                _rightPaneScrollView.style.paddingRight = 0;
                _rightPaneScrollView.style.paddingTop = 0;
                _rightPaneScrollView.style.paddingBottom = 0;


                var bannerSection = CreateBannerSection(selectedProfile);
                _profileDetailsContainer.Add(bannerSection);


                SetupParallaxScrolling();



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





                _metadataSection = CreateMetadataSection(selectedProfile);
                _metadataSection.name = "section-metadata";
                contentWrapper.Add(_metadataSection);


                _metadataSection.RegisterCallback<GeometryChangedEvent>(evt => UpdateBannerButtonPosition());


                var summarySection = CreateSummarySection(selectedProfile);
                contentWrapper.Add(summarySection);


                var validationSection = CreateValidationSection(selectedProfile);
                contentWrapper.Add(validationSection);


                var optionsSection = CreateExportOptionsSection(selectedProfile);
                optionsSection.name = "section-settings";
                contentWrapper.Add(optionsSection);

                var foldersSection = CreateFoldersSection(selectedProfile);
                foldersSection.name = "section-content";
                contentWrapper.Add(foldersSection);


                var bundledProfilesSection = BundledProfilesSection.CreateBundledProfilesSection(selectedProfile, () => UpdateProfileDetails());
                contentWrapper.Add(bundledProfilesSection);


                var inspectorSection = CreateExportInspectorSection(selectedProfile);
                inspectorSection.name = "section-files";
                contentWrapper.Add(inspectorSection);


                var exclusionSection = CreateExclusionFiltersSection(selectedProfile);
                exclusionSection.name = "section-filters";
                contentWrapper.Add(exclusionSection);


                var dependenciesSection = CreateDependenciesSection(selectedProfile);
                dependenciesSection.name = "section-dependencies";
                contentWrapper.Add(dependenciesSection);


                var obfuscationSection = CreateObfuscationSection(selectedProfile);
                obfuscationSection.name = "section-advanced";
                contentWrapper.Add(obfuscationSection);


                var actionsSection = CreateQuickActionsSection(selectedProfile);
                actionsSection.name = "section-actions";
                contentWrapper.Add(actionsSection);

                _profileDetailsContainer.Add(contentWrapper);


                _changeBannerButton = new Button(() => OnChangeBannerClicked(selectedProfile));
                _changeBannerButton.text = "Change";
                _changeBannerButton.AddToClassList("yucp-overlay-hover-button");
                _changeBannerButton.style.position = Position.Absolute;
                _changeBannerButton.style.bottom = StyleKeyword.Auto;
                _changeBannerButton.style.left = StyleKeyword.Auto;
                _changeBannerButton.style.right = StyleKeyword.Auto;
                _changeBannerButton.style.opacity = 0f;
                _changeBannerButton.pickingMode = PickingMode.Ignore;


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


                // The scroll view outlives each rebuild: replace the previous hover handler, don't add another.
                if (_bannerHoverMove != null) _rightPaneScrollView.UnregisterCallback(_bannerHoverMove);
                _rightPaneScrollView.RegisterCallback(_bannerHoverMove = evt =>
                {
                    if (_changeBannerButton == null) return;





                    float localY = evt.localMousePosition.y;


                    float bottomBound = 300f;
                    if (_metadataSection != null)
                    {
                        bottomBound = _metadataSection.layout.y;
                        if (bottomBound < 50) bottomBound = 250f;
                    }

                    bool isHoveringBanner = localY < bottomBound;

                    if (isHoveringBanner && _changeBannerButton.style.opacity.value < 0.5f)
                    {
                        showButton();
                    }
                    else if (!isHoveringBanner && _changeBannerButton.style.opacity.value > 0.5f)
                    {


                        hideButton();
                    }
                });


                _changeBannerButton.RegisterCallback<MouseEnterEvent>(e => showButton());
                _changeBannerButton.RegisterCallback<MouseLeaveEvent>(e => {

                });



                _rightPaneScrollView.UnregisterCallback<GeometryChangedEvent>(OnBannerGeometryChanged);
                _rightPaneScrollView.RegisterCallback<GeometryChangedEvent>(OnBannerGeometryChanged);
                _rightPaneScrollView.Add(_changeBannerButton);

                if (ProfileHasContentToScan(selectedProfile))
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

                if (selectedProfile.dependencies.Count == 0 && ProfileHasContentToScan(selectedProfile))
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
