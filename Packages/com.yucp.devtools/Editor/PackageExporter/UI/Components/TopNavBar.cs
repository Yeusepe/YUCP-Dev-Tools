using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.DevTools.Editor.PackageExporter.UI.Components
{
    public class TopNavBar : VisualElement
    {
        private class NavGroup
        {
            public string Name;
            public VisualElement Root;
            public VisualElement Content;
            public Label Label;
            public MotionHandle ContentMotion;
            public List<Button> Tabs = new List<Button>();
            public bool IsExpanded;
            private int _animationToken;
            private const float OpenDuration = 0.3f;
            private const float CloseDuration = 0.3f;

            private static readonly MotionTargets VisibleTargets = new MotionTargets
            {
                HasOpacity = true, Opacity = 1f,
                HasX = true, X = 0f,
                HasScaleX = true, ScaleX = 1f,
                HasScaleY = true, ScaleY = 1f
            };
            
            private static readonly MotionTargets HiddenTargets = new MotionTargets
            {
                HasOpacity = true, Opacity = 0f,
                HasX = true, X = -2f,
                HasScaleX = true, ScaleX = 0.99f,
                HasScaleY = true, ScaleY = 0.99f
            };
            
            public void SetExpanded(bool expanded, bool animate = true)
            {
                if (IsExpanded == expanded && animate)
                {
                    float opacity = Content.resolvedStyle.opacity;
                    if (expanded && opacity > 0.98f)
                        return;
                    if (!expanded && opacity < 0.02f)
                        return;
                }

                _animationToken++;
                int token = _animationToken;
                
                if (expanded)
                {
                    IsExpanded = true;
                    Root.AddToClassList("expanded");
                    if (animate)
                    {
                        ContentMotion.Animate(VisibleTargets, new Transition(OpenDuration, EasingType.EaseInOutCubic));
                    }
                    else
                    {
                         ContentMotion.Animate(VisibleTargets, new Transition(0f));
                    }
                }
                else
                {
                    IsExpanded = false;
                    if (!animate)
                    {
                        Root.RemoveFromClassList("expanded");
                        ContentMotion.Animate(HiddenTargets, new Transition(0f));
                        return;
                    }

                    // Smooth close animation synchronized with width collapse.
                    Root.RemoveFromClassList("expanded");
                    ContentMotion.Animate(HiddenTargets, new Transition(CloseDuration, EasingType.EaseInOutCubic));
                    Root.schedule.Execute(() =>
                    {
                        if (!IsExpanded && _animationToken == token)
                        {
                            ContentMotion.Animate(HiddenTargets, new Transition(0f));
                        }
                    }).StartingIn(Mathf.RoundToInt(CloseDuration * 1000f));
                }
            }
            
            public void SetContentVisibleImmediate()
            {
                ContentMotion.Animate(VisibleTargets, new Transition(0f));
            }

            public void ForceVisibleStyleImmediate()
            {
                Content.style.opacity = 1f;
                Content.style.translate = new Translate(0f, 0f);
                Content.style.scale = new Scale(Vector3.one);
                Content.style.visibility = Visibility.Visible;
            }
            
            public void CancelPendingAnimation()
            {
                _animationToken++;
            }
        }

        private List<NavGroup> _groups = new List<NavGroup>();
        private Action<string> _onTabClicked;
        private Action _onMenuClicked;
        private string _activeTabId;
        private VisualElement _innerContainer;
        private NavGroup _hoveredGroup;
        private NavGroup _pendingHoverGroup;
        private bool _hoverUpdateQueued;
        private bool _wasNarrowMode;

        // Configuration mapping: Group Name -> List of (TabID, TabLabel)
        private readonly Dictionary<string, List<(string Id, string Label)>> _config = new Dictionary<string, List<(string, string)>>
        {
            { "Setup", new List<(string, string)> { ("General", "General"), ("Options", "Options") } },
            { "Content", new List<(string, string)> { ("Folders", "Folders"), ("Files", "Files"), ("Dependencies", "Dependencies"), ("Updates", "Updates") } },
            { "System", new List<(string, string)> { ("Security", "Security"), ("Actions", "Actions") } }
        };

        private Action _onSidebarToggle;

        public TopNavBar(Action<string> onTabClicked, Action onMenuClicked = null, Action onSidebarToggle = null)
        {
            _onTabClicked = onTabClicked;
            _onMenuClicked = onMenuClicked;
            _onSidebarToggle = onSidebarToggle;
            
            AddToClassList("yucp-top-navbar");
            
            // Motion Init
            YUCP.Motion.Motion.Initialize();

            // Load Styles
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.devtools/Editor/PackageExporter/Styles/PackageExporter.uss");
            if (styleSheet != null) styleSheets.Add(styleSheet);
            
            _innerContainer = new VisualElement();
            _innerContainer.AddToClassList("yucp-top-navbar-container");
            Add(_innerContainer);
            
            // Track hover group from pointer movement to avoid jitter between elements
            _innerContainer.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!IsNarrowMode())
                    return;
                
                var hovered = GetGroupFromElement(evt.target as VisualElement);
                if (hovered != null && hovered != _hoveredGroup)
                {
                    QueueHoverUpdate(hovered);
                }
            });

            // Sidebar Toggle (Mobile/Narrow only)
            if (_onSidebarToggle != null)
            {
                var sidebarBtn = new Button(_onSidebarToggle);
                sidebarBtn.AddToClassList("yucp-sidebar-toggle-button");
                // Use a hamburger icon or similar
                sidebarBtn.text = "|||"; 
                sidebarBtn.tooltip = "Toggle Sidebar";
                _innerContainer.Add(sidebarBtn);
            }

            // Create Groups
            foreach (var kvp in _config)
            {
                CreateGroup(_innerContainer, kvp.Key, kvp.Value);
            }



            // Actions Container (Right side - Split Action Header)
            var rightZone = new VisualElement();
            rightZone.AddToClassList("yucp-top-navbar-right-zone");
            Add(rightZone);

            // Separator
            var separator = new VisualElement();
            separator.AddToClassList("yucp-navbar-separator");
            rightZone.Add(separator);

            // Tools Button (Structural)
            if (_onMenuClicked != null)
            {
                var toolsBtn = new Button(_onMenuClicked);
                toolsBtn.AddToClassList("yucp-tools-action-button");
                toolsBtn.tooltip = "Tools & Options";
                
                // Icon (Visible always)
                var icon = new Label("⋮"); 
                icon.AddToClassList("yucp-tools-action-icon");
                toolsBtn.Add(icon);
                
                // Label (Revealed on hover)
                var label = new Label("Tools");
                label.AddToClassList("yucp-tools-action-label");
                toolsBtn.Add(label);

                rightZone.Add(toolsBtn);
            }
            
            // Restore selection logic on mouse leave
            RegisterCallback<MouseLeaveEvent>(evt => {
                // Safeguard against layout thrashing during event processing
                schedule.Execute(() =>
                {
                    QueueHoverUpdate(null);
                });
            });
            
            // Check for mode changes - use local width mainly
            RegisterCallback<GeometryChangedEvent>(evt => 
            {
                UpdateResponsiveState(evt.newRect.width);
                ApplyGroupStates(animate: false);
            });
            
            // Initial update
            schedule.Execute(() => {
                UpdateResponsiveState(resolvedStyle.width);
                ApplyGroupStates(animate: false);
            }).StartingIn(50);
        }

        private void UpdateResponsiveState(float width)
        {
            // Threshold for collapsing tabs: approx 650px available width
            // If width is 0 (first frame), check hierarchy or default to wide? 
            // Better to default to false until we know.
            if (width <= 0) return;

            bool shouldBeNarrow = width < 650f;
            
            // Also force narrow if the global window is actually small (mobile check),
            // though usually width check covers this.
            
            if (shouldBeNarrow)
            {
                AddToClassList("yucp-navbar-narrow");
            }
            else
            {
                RemoveFromClassList("yucp-navbar-narrow");
            }

            if (_wasNarrowMode != shouldBeNarrow)
            {
                _wasNarrowMode = shouldBeNarrow;
                OnNarrowModeChanged(shouldBeNarrow);
            }
            else if (!shouldBeNarrow)
            {
                // Ensure visibility when resizing within wide range
                EnsureVisibleAfterResize();
            }
        }

        private void CreateGroup(VisualElement parent, string groupName, List<(string Id, string Label)> tabs)
        {
            var group = new NavGroup { Name = groupName };
            
            group.Root = new VisualElement();
            group.Root.AddToClassList("yucp-nav-group");
            
            // Label (collapsed state)
            group.Label = new Label(groupName);
            group.Label.AddToClassList("yucp-nav-group-label");
            group.Root.Add(group.Label);
            
            // Content (expanded state)
            group.Content = new VisualElement();
            group.Content.AddToClassList("yucp-nav-group-content");
            group.Root.Add(group.Content);
            
            group.ContentMotion = YUCP.Motion.Motion.Attach(group.Content);
            
            // Tabs
            foreach (var tabInfo in tabs)
            {
                var btn = new Button { text = tabInfo.Label };
                btn.AddToClassList("yucp-top-navbar-tab");
                btn.userData = tabInfo.Id;
                btn.clicked += () => 
                {
                    SetActiveTab(tabInfo.Id);
                    _onTabClicked?.Invoke(tabInfo.Id);
                };
                group.Content.Add(btn);
                group.Tabs.Add(btn);
            }

            // Hover Logic
            group.Root.RegisterCallback<MouseEnterEvent>(evt => {
                // Defer execution to prevent "Assertion failed" in ProcessEvent during rapid mouse movement
                group.Root.schedule.Execute(() => {
                    if (IsNarrowMode())
                    {
                        QueueHoverUpdate(group);
                    }
                });
            });
            
            parent.Add(group.Root);
            _groups.Add(group);
        }

        private bool IsNarrowMode()
        {
            if (ClassListContains("yucp-navbar-narrow")) return true;
            
            // Also check global window state which enforces CSS collapse
            VisualElement current = this;
            while (current != null)
            {
                if (current.ClassListContains("yucp-window-narrow") || 
                    current.ClassListContains("yucp-window-medium"))
                    return true;
                current = current.parent;
            }
            return false;
        }

        private void ApplyGroupStates(bool animate)
        {
            bool isNarrow = IsNarrowMode();
            
            if (!isNarrow) {
                ForceVisibleAll();
                return;
            }

            // In Narrow mode, if mouse is not over navbar, expand the active group
            var activeGroup = _groups.FirstOrDefault(g => g.Tabs.Any(t => (string)t.userData == _activeTabId));
            if (activeGroup == null && _groups.Count > 0) activeGroup = _groups[0]; // Fallback
            
            var targetGroup = _hoveredGroup ?? activeGroup;
            
            if (targetGroup != null)
            {
                foreach(var g in _groups) g.SetExpanded(g == targetGroup, animate: animate);
            }
        }

        private void OnNarrowModeChanged(bool isNowNarrow)
        {
            if (isNowNarrow)
                return;
            
            _hoveredGroup = null;
            _pendingHoverGroup = null;

            EnsureVisibleAfterResize();
        }

        private void ForceVisibleAll()
        {
            foreach (var g in _groups)
            {
                g.CancelPendingAnimation();
                g.IsExpanded = false;
                g.Root.RemoveFromClassList("expanded");
                g.SetContentVisibleImmediate();
                g.ForceVisibleStyleImmediate();
            }
        }

        private void EnsureVisibleAfterResize()
        {
            // Apply more than once to survive class updates and layout timing.
            schedule.Execute(() => { if (!IsNarrowMode()) ForceVisibleAll(); }).StartingIn(1);
            schedule.Execute(() => { if (!IsNarrowMode()) ForceVisibleAll(); }).StartingIn(60);
        }
        
        private NavGroup GetGroupFromElement(VisualElement element)
        {
            var current = element;
            while (current != null)
            {
                var match = _groups.FirstOrDefault(g => g.Root == current);
                if (match != null)
                    return match;
                current = current.parent;
            }

            return null;
        }

        private void QueueHoverUpdate(NavGroup group)
        {
            _pendingHoverGroup = group;
            if (_hoverUpdateQueued)
                return;
            
            _hoverUpdateQueued = true;
            schedule.Execute(() =>
            {
                _hoverUpdateQueued = false;
                if (!IsNarrowMode())
                    return;
                
                if (_pendingHoverGroup == _hoveredGroup)
                    return;
                
                _hoveredGroup = _pendingHoverGroup;
                ApplyGroupStates(animate: true);
            });
        }

        public void SetActiveTab(string id)
        {
            if (_activeTabId == id) return;
            _activeTabId = id;
            
            foreach (var group in _groups)
            {
                foreach (var tab in group.Tabs)
                {
                    if ((string)tab.userData == id)
                        tab.AddToClassList("selected");
                    else
                        tab.RemoveFromClassList("selected");
                }
            }
            
            ApplyGroupStates(animate: true);
        }
    }
}
