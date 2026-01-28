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
        private VisualElement CreateLeftPane(bool isOverlay)
        {
            var leftPane = new VisualElement();
            leftPane.AddToClassList("yucp-left-pane");
            leftPane.name = "yucp-left-pane";
            
            // Create sidebar configuration
            var config = new YucpSidebar.SidebarConfig
            {
                HeaderText = "Export Profiles",
                ShowSearch = true,
                OnSearchChanged = (searchText) => UpdateProfileList(),
                ActionButtons = new List<YucpSidebar.SidebarActionButton>
                {
                    new YucpSidebar.SidebarActionButton
                    {
                        Text = "+ New Profile",
                        IsPrimary = true,
                        OnClick = () => { CreateNewProfile(); CloseOverlay(); }
                    },
                    new YucpSidebar.SidebarActionButton
                    {
                        Text = "Clone",
                        OnClick = () => { CloneProfile(selectedProfile); CloseOverlay(); }
                    },
                    new YucpSidebar.SidebarActionButton
                    {
                        Text = "Delete",
                        IsDanger = true,
                        // Bulk-delete aware: one confirm, deletes all selected
                        OnClick = () => { HandleDelete(); CloseOverlay(); }
                    }
                }
            };
            
            // Create sidebar - use separate instances for overlay and normal
            YucpSidebar sidebar;
            if (isOverlay)
            {
                _sidebarOverlay = new YucpSidebar(config);
                sidebar = _sidebarOverlay;
            }
            else
            {
                _sidebar = new YucpSidebar(config);
                sidebar = _sidebar;
            }
            
            var container = sidebar.CreateSidebar(isOverlay);
            
            // Store references
            if (isOverlay)
            {
                _profileListContainerOverlay = sidebar.ListContainer;
            }
            else
            {
                _profileListScrollView = sidebar.ScrollView;
                _profileListContainer = sidebar.ListContainer;
            }
            
            // Replace the default header with our custom header that includes Sort/Filter + Search
            ReplaceSidebarHeader(container, sidebar, isOverlay);
            
            // Inject Support Banner above actions
            // The actions section has class "yucp-profile-actions-section"
            var actionsSection = container.Q(className: "yucp-profile-actions-section");
            if (actionsSection != null) 
            {
                var supportBanner = CreateSidebarSupportBanner();
                if (supportBanner != null)
                {
                    // Insert before actions section
                    var parent = actionsSection.parent;
                    int index = parent.IndexOf(actionsSection);
                    parent.Insert(index, supportBanner);
                }
            }

            leftPane.Add(container);
            return leftPane;
        }

        private VisualElement CreateSidebarSupportBanner()
        {
            if (EditorPrefs.GetBool(SupportPrefNeverKey, false))
            {
                return null;
            }
            
            if (SessionState.GetBool(SupportSessionDismissKey, false))
            {
                return null;
            }

            // Check if there's a milestone - if so, always show (bypass cadence)
            object milestone = null;
            bool hasMilestone = false;
            try
            {
                System.Type milestoneTrackerType = null;
                
                // Try to find the type by searching through all loaded assemblies
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    milestoneTrackerType = assembly.GetType("YUCP.Components.Editor.SupportBanner.MilestoneTracker");
                    if (milestoneTrackerType != null)
                        break;
                }
                
                if (milestoneTrackerType != null)
                {
                    var getMilestoneMethod = milestoneTrackerType.GetMethod("GetCurrentMilestone", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (getMilestoneMethod != null)
                    {
                        milestone = getMilestoneMethod.Invoke(null, null);
                        hasMilestone = milestone != null;
                    }
                }
            }
            catch (System.Exception)
            {
                // Silently fail milestone check
            }

            // Note: The original CreateSupportToast incremented the counter. 
             int currentCount = EditorPrefs.GetInt(SupportPrefCounterKey, 0) + 1;
             EditorPrefs.SetInt(SupportPrefCounterKey, currentCount);

            if (!hasMilestone)
            {
                int cadence = Math.Max(1, EditorPrefs.GetInt(SupportPrefCadenceKey, 1000));
                if (currentCount % cadence != 0)
                {
                    return null;
                }
            }

            // Banner Container
            var banner = new VisualElement();
            banner.AddToClassList("yucp-sidebar-support-banner");
            
            // Header with Icon and Title
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6;
            
            var iconImage = new Image();
            var heartIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.components/Resources/Icons/NucleoArcade/heart.png");
            if (heartIcon != null)
            {
                iconImage.image = heartIcon;
            }
            iconImage.AddToClassList("yucp-sidebar-support-icon");
            header.Add(iconImage);
            
            // Milestone Title
            string titleText = "Support This Tool";
            if (milestone != null)
            {
                try
                {
                    var titleProperty = milestone.GetType().GetProperty("Title");
                    titleText = titleProperty?.GetValue(milestone)?.ToString() ?? titleText;
                }
                catch {}
            }
            
            var title = new Label(titleText);
            title.AddToClassList("yucp-sidebar-support-title");
            header.Add(title);
            banner.Add(header);

            // Subtitle
            string subtitleText = "Your support helps keep these tools free and updated!";
            if (milestone != null)
            {
                 try
                {
                    var subtitleProperty = milestone.GetType().GetProperty("Subtitle");
                    subtitleText = subtitleProperty?.GetValue(milestone)?.ToString() ?? subtitleText;
                }
                catch {}
            }

            var subtitle = new Label(subtitleText);
            subtitle.AddToClassList("yucp-sidebar-support-subtitle");
            banner.Add(subtitle);

            // Action Row
            var actionRow = new VisualElement();
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.marginTop = 10;
            actionRow.style.justifyContent = Justify.SpaceBetween;
            actionRow.style.alignItems = Align.Center;

            // Left side actions (Later / Never)
            var leftActions = new VisualElement();
            leftActions.style.flexDirection = FlexDirection.Row;
            leftActions.style.alignItems = Align.Center;

            // Dismiss (Later)
            var dismissBtn = new Button(() => {
                SessionState.SetBool(SupportSessionDismissKey, true);
                banner.style.display = DisplayStyle.None;
            }) { text = "Later" };
            dismissBtn.AddToClassList("yucp-sidebar-support-dismiss");
            leftActions.Add(dismissBtn);
            
            // Never Show
            var neverBtn = new Button(() => {
                EditorPrefs.SetBool(SupportPrefNeverKey, true);
                banner.style.display = DisplayStyle.None;
            }) { text = "Never" };
            neverBtn.AddToClassList("yucp-sidebar-support-dismiss");
            leftActions.Add(neverBtn);
            
            actionRow.Add(leftActions);

            // Support (Primary button style but gold)
            var supportBtn = new Button(() => Application.OpenURL(SupportUrl)) { text = "Support" };
            supportBtn.AddToClassList("yucp-sidebar-support-button");
            actionRow.Add(supportBtn);

            banner.Add(actionRow);
            
            return banner;
        }

    }
}
