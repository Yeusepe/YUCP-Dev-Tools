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
                        OnClick = () => { DeleteProfile(selectedProfile); CloseOverlay(); }
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
            
            leftPane.Add(container);
            return leftPane;
        }

    }
}
