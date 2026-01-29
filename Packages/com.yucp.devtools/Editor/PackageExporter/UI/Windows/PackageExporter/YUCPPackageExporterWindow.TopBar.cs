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
        // Legacy TopBar UI creation methods removed.
        // Menu item providers are kept for the new context menu.

        private List<ToolbarMenuItem> GetExportMenuItems()
        {
            #if UNITY_EDITOR_WIN
                string modKey = "Ctrl";
            #else
                string modKey = "Cmd";
            #endif
            
            return new List<ToolbarMenuItem>
            {
                new ToolbarMenuItem
                {
                    Label = "Export Selected",
                    Tooltip = $"Export selected profile(s) ({modKey}+E)",
                    Callback = () => ExportSelectedProfiles()
                },
                new ToolbarMenuItem
                {
                    Label = "Export All",
                    Tooltip = $"Export all profiles ({modKey}+Shift+E)",
                    Callback = () => ExportAllProfiles()
                },
                ToolbarMenuItem.Separator(),
                new ToolbarMenuItem
                {
                    Label = "Quick Export Current",
                    Tooltip = $"Quick export the currently selected profile ({modKey}+Enter)",
                    Callback = () => {
                        if (selectedProfile != null)
                        {
                            ExportProfile(selectedProfile);
                        }
                    }
                }
            };
        }

        private List<ToolbarMenuItem> GetUtilitiesMenuItems()
        {
            return new List<ToolbarMenuItem>
            {
                new ToolbarMenuItem
                {
                    Label = "Create Export Profile",
                    Tooltip = "Create a new export profile",
                    Callback = () => MenuItems.CreateExportProfileFromMenu()
                },
                new ToolbarMenuItem
                {
                    Label = "Open Profiles Folder",
                    Tooltip = "Open the export profiles folder",
                    Callback = () => MenuItems.OpenExportProfilesFolder()
                },
                ToolbarMenuItem.Separator(),
                new ToolbarMenuItem
                {
                    Label = "Check ConfuserEx Installation",
                    Tooltip = "Check if ConfuserEx is installed",
                    Callback = () => MenuItems.CheckConfuserExInstallation()
                },
                new ToolbarMenuItem
                {
                    Label = "Scan for @bump Directives",
                    Tooltip = "Scan project for version bump directives",
                    Callback = () => MenuItems.ScanProjectForVersionDirectives()
                },
                ToolbarMenuItem.Separator(),
                new ToolbarMenuItem
                {
                    Label = "Refresh",
                    Tooltip = $"Reload profiles from disk (F5)",
                    Callback = () => {
                        LoadProfiles();
                        UpdateProfileList();
                        UpdateProfileDetails();
                        UpdateBottomBar();
                    }
                }
            };
        }

        private List<ToolbarMenuItem> GetDebugMenuItems()
        {
            return new List<ToolbarMenuItem>
            {
                new ToolbarMenuItem
                {
                    Label = "Derived FBX Debug",
                    Tooltip = "Open Derived FBX Debug window",
                    Callback = () => {
                        var windowType = System.Type.GetType("YUCP.DevTools.Editor.PackageExporter.DerivedFbxDebugWindow, yucp.devtools.Editor");
                        if (windowType != null)
                        {
                            var method = windowType.GetMethod("ShowWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            method?.Invoke(null, null);
                        }
                    }
                },
                ToolbarMenuItem.Separator(),
                new ToolbarMenuItem
                {
                    Label = "Validate Install",
                    Tooltip = "Validate YUCP installation",
                    Callback = () => {
                        var menuItemsType = System.Type.GetType("YUCP.DevTools.Editor.PackageExporter.Templates.InstallerHealthTools, yucp.devtools.Editor");
                        if (menuItemsType != null)
                        {
                            var method = menuItemsType.GetMethod("ValidateInstall", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            method?.Invoke(null, null);
                        }
                    }
                },
                new ToolbarMenuItem
                {
                    Label = "Repair Install",
                    Tooltip = "Repair YUCP installation",
                    Callback = () => {
                        var menuItemsType = System.Type.GetType("YUCP.DevTools.Editor.PackageExporter.Templates.InstallerHealthTools, yucp.devtools.Editor");
                        if (menuItemsType != null)
                        {
                            var method = menuItemsType.GetMethod("RepairInstall", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            method?.Invoke(null, null);
                        }

                    }
                }
            };
        }

        private List<ToolbarMenuItem> GetHelpMenuItems()
        {
            return new List<ToolbarMenuItem>
            {
                new ToolbarMenuItem
                {
                    Label = "Restart Onboarding",
                    Tooltip = "Restart the onboarding tour",
                    Callback = () => {
                         EditorPrefs.SetBool(OnboardingPrefKey, false);
                         StartOnboarding();
                    }
                },
                new ToolbarMenuItem
                {
                    Label = "Documentation",
                    Tooltip = "Open online documentation",
                    Callback = () => Application.OpenURL(PackageExporterWikiUrl)
                }
            };
        }

        private VisualElement CreateSupportToast()
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

            int count = EditorPrefs.GetInt(SupportPrefCounterKey, 0) + 1;
            EditorPrefs.SetInt(SupportPrefCounterKey, count);

            if (!hasMilestone)
            {
                int cadence = Math.Max(1, EditorPrefs.GetInt(SupportPrefCadenceKey, 1000));
                if (count % cadence != 0)
                {
                    return null;
                }
            }

            // Toast container - positioned at top-right, yellow theme matching components
            var toast = new VisualElement();
            toast.AddToClassList("yucp-support-toast");
            toast.style.position = Position.Absolute;
            toast.style.backgroundColor = new Color(0.6f, 0.45f, 0.2f, 0.95f); // Darker yellow/brown

            // Content container with horizontal layout
            var content = new VisualElement();
            content.AddToClassList("yucp-support-toast-content");
            toast.Add(content);

            // Left: Icon
            var iconImage = new Image();
            var heartIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.components/Resources/Icons/NucleoArcade/heart.png");
            if (heartIcon != null)
            {
                iconImage.image = heartIcon;
            }
            iconImage.style.width = 32;
            iconImage.style.height = 32;
            iconImage.style.marginRight = 10;
            content.Add(iconImage);

            // Middle: Title and Subtitle
            var textContainer = new VisualElement();
            textContainer.AddToClassList("yucp-support-text-container");
            textContainer.name = "yucp-support-text-container";
            textContainer.style.flexDirection = FlexDirection.Column;
            textContainer.style.flexGrow = 1;
            textContainer.style.flexShrink = 1;
            textContainer.style.minWidth = 0; // Allow shrinking below content size

            // Get milestone title and subtitle
            string titleText = "Your Support Keeps This Free";
            string subtitleText = "Enjoying these tools? Your support keeps them free and helps create more amazing features!";
            
            if (milestone != null)
            {
                try
                {
                    var titleProperty = milestone.GetType().GetProperty("Title");
                    var subtitleProperty = milestone.GetType().GetProperty("Subtitle");
                    titleText = titleProperty?.GetValue(milestone)?.ToString() ?? titleText;
                    subtitleText = subtitleProperty?.GetValue(milestone)?.ToString() ?? subtitleText;
                }
                catch (System.Exception)
                {
                    // Silently fail milestone text retrieval
                }
            }

            var title = new Label(titleText);
            title.AddToClassList("yucp-support-title");
            title.style.fontSize = 13;
            title.style.color = new Color(1f, 1f, 1f, 1f); // White text
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 2;
            textContainer.Add(title);

            var subtitle = new Label(subtitleText);
            subtitle.AddToClassList("yucp-support-subtitle");
            subtitle.style.fontSize = 10;
            subtitle.style.color = new Color(0.95f, 0.95f, 0.95f, 1f); // Light gray text
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            textContainer.Add(subtitle);

            content.Add(textContainer);

            // Right: Action buttons
            var actionsContainer = new VisualElement();
            actionsContainer.AddToClassList("yucp-support-actions-container");
            actionsContainer.name = "yucp-support-actions-container";
            actionsContainer.style.flexDirection = FlexDirection.Column;
            actionsContainer.style.alignItems = Align.FlexEnd;
            actionsContainer.style.flexShrink = 0;
            actionsContainer.style.minWidth = 90;

            var supportButton = new Button(() => Application.OpenURL(SupportUrl)) { text = "Support" };
            supportButton.AddToClassList("yucp-support-button");
            supportButton.style.minWidth = 85;
            supportButton.style.height = 24;
            supportButton.style.fontSize = 11;
            supportButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            supportButton.style.backgroundColor = new Color(0.886f, 0.647f, 0.290f, 1f); // Yellow
            supportButton.style.color = new Color(0.1f, 0.1f, 0.1f, 1f); // Dark text
            supportButton.style.borderTopWidth = 0;
            supportButton.style.borderBottomWidth = 0;
            supportButton.style.borderLeftWidth = 0;
            supportButton.style.borderRightWidth = 0;
            supportButton.style.paddingLeft = 12;
            supportButton.style.paddingRight = 12;
            supportButton.style.marginBottom = 4;
            supportButton.RegisterCallback<MouseEnterEvent>(evt => {
                supportButton.style.backgroundColor = new Color(0.9f, 0.7f, 0.4f, 1f); // Lighter on hover
            });
            supportButton.RegisterCallback<MouseLeaveEvent>(evt => {
                supportButton.style.backgroundColor = new Color(0.8f, 0.6f, 0.3f, 1f);
            });
            actionsContainer.Add(supportButton);

            var linksContainer = new VisualElement();
            linksContainer.style.flexDirection = FlexDirection.Row;

            var dismissButton = new Button(() =>
            {
                SessionState.SetBool(SupportSessionDismissKey, true);
                toast.RemoveFromHierarchy();
            }) { text = "Dismiss" };
            dismissButton.style.backgroundColor = new StyleColor(StyleKeyword.None);
            dismissButton.style.borderTopWidth = 0;
            dismissButton.style.borderBottomWidth = 0;
            dismissButton.style.borderLeftWidth = 0;
            dismissButton.style.borderRightWidth = 0;
            dismissButton.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            dismissButton.style.fontSize = 9;
            dismissButton.style.paddingLeft = 2;
            dismissButton.style.paddingRight = 2;
            dismissButton.style.minHeight = 16;
            dismissButton.style.marginRight = 6;
            dismissButton.RegisterCallback<MouseEnterEvent>(evt => {
                dismissButton.style.color = new Color(1f, 1f, 1f, 1f);
            });
            dismissButton.RegisterCallback<MouseLeaveEvent>(evt => {
                dismissButton.style.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            });
            linksContainer.Add(dismissButton);

            var neverButton = new Button(() =>
            {
                EditorPrefs.SetBool(SupportPrefNeverKey, true);
                
                // Animate out before removing
                toast.style.opacity = 0;
                toast.style.translate = new Translate(0, -20);
                
                toast.schedule.Execute(() => {
                    toast.RemoveFromHierarchy();
                }).StartingIn(300);
            }) { text = "Never show" };
            neverButton.style.backgroundColor = new StyleColor(StyleKeyword.None);
            neverButton.style.borderTopWidth = 0;
            neverButton.style.borderBottomWidth = 0;
            neverButton.style.borderLeftWidth = 0;
            neverButton.style.borderRightWidth = 0;
            neverButton.style.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            neverButton.style.fontSize = 9;
            neverButton.style.paddingLeft = 2;
            neverButton.style.paddingRight = 2;
            neverButton.style.minHeight = 16;
            neverButton.RegisterCallback<MouseEnterEvent>(evt => {
                neverButton.style.color = new Color(1f, 1f, 1f, 1f);
            });
            neverButton.RegisterCallback<MouseLeaveEvent>(evt => {
                neverButton.style.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            });
            linksContainer.Add(neverButton);

            actionsContainer.Add(linksContainer);
            content.Add(actionsContainer);

            return toast;
        }

        private VisualElement BuildChip(string text, Action onRemove = null, bool isSelected = false)
        {
            var chip = new VisualElement();
            chip.AddToClassList("yucp-chip");
            if (isSelected)
                chip.AddToClassList("yucp-chip-selected");
            
            var label = new Label(text);
            label.AddToClassList("yucp-chip-label");
            chip.Add(label);
            
            if (onRemove != null)
            {
                var removeButton = new Button(onRemove) { text = "×" };
                removeButton.AddToClassList("yucp-chip-remove");
                chip.Add(removeButton);
            }
            
            return chip;
        }

        private Button BuildIconButton(string label, Texture2D icon, Action onClick, string variant = "outline", int badgeCount = 0)
        {
            var button = new Button(onClick);
            button.AddToClassList("yucp-button");
            button.AddToClassList("yucp-button-outline");
            button.AddToClassList("yucp-button-icon-left");
            
            if (icon != null)
            {
                var iconImage = new Image { image = icon };
                button.Add(iconImage);
            }
            
            var labelElement = new Label(label);
            button.Add(labelElement);
            
            if (badgeCount > 0)
            {
                var badge = new Label(badgeCount.ToString());
                badge.AddToClassList("yucp-button-badge");
                button.Add(badge);
            }
            
            return button;
        }

        private void ShowPopover(Rect position, VisualElement content, Action onClose = null)
        {
            // Close existing popover
            ClosePopover();
            
            // Create backdrop
            _popoverBackdrop = new VisualElement();
            _popoverBackdrop.style.position = Position.Absolute;
            _popoverBackdrop.style.left = 0;
            _popoverBackdrop.style.top = 0;
            _popoverBackdrop.style.right = 0;
            _popoverBackdrop.style.bottom = 0;
            _popoverBackdrop.style.backgroundColor = new Color(0, 0, 0, 0.1f);
            _popoverBackdrop.RegisterCallback<MouseDownEvent>(evt =>
            {
                ClosePopover();
                onClose?.Invoke();
            });
            rootVisualElement.Add(_popoverBackdrop);
            
            // Create popover panel
            _currentPopover = new VisualElement();
            _currentPopover.AddToClassList("yucp-popover");
            _currentPopover.style.position = Position.Absolute;
            _currentPopover.style.left = position.x;
            _currentPopover.style.top = position.y;
            _currentPopover.style.width = position.width;
            _currentPopover.style.minHeight = position.height;
            _currentPopover.style.maxHeight = 500;
            _currentPopover.style.overflow = Overflow.Hidden;
            
            // Add content directly (content should handle its own scrolling if needed)
            _currentPopover.Add(content);
            
            rootVisualElement.Add(_currentPopover);
        }

        private void ClosePopover()
        {
            if (_currentPopover != null)
            {
                _currentPopover.RemoveFromHierarchy();
                _currentPopover = null;
            }
            if (_popoverBackdrop != null)
            {
                _popoverBackdrop.RemoveFromHierarchy();
                _popoverBackdrop = null;
            }
        }

        private VisualElement CreateTopBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("yucp-top-bar");
            return bar;
        }

        private void ToggleCompactMode()
        {
            _isCompactMode = !_isCompactMode;
            EditorPrefs.SetBool(CompactModeKey, _isCompactMode);
            
            // Update UI class
            if (_isCompactMode)
                rootVisualElement.AddToClassList("yucp-compact-mode");
            else
                rootVisualElement.RemoveFromClassList("yucp-compact-mode");

            // Rebuild top bar to update button text (if a top bar exists in the layout)
            var topBar = rootVisualElement.Q(className: "yucp-top-bar");
            if (topBar != null)
            {
                var parent = topBar.parent;
                var index = parent.IndexOf(topBar);
                topBar.RemoveFromHierarchy();
                parent.Insert(index, CreateTopBar());
            }
        }
    }
}
