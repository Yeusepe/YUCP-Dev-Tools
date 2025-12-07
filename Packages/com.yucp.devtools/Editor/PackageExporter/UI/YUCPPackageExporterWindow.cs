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

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Represents a folder node in the hierarchical tree structure
    /// </summary>
    internal class FolderTreeNode
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public List<FolderTreeNode> Children { get; set; } = new List<FolderTreeNode>();
        public List<DiscoveredAsset> Assets { get; set; } = new List<DiscoveredAsset>();
        public bool IsExpanded { get; set; } = true;
        
        public FolderTreeNode(string name, string fullPath)
        {
            Name = name;
            FullPath = fullPath;
        }
    }
    
    /// <summary>
    /// Represents a menu item in the toolbar
    /// </summary>
    internal struct ToolbarMenuItem
    {
        public string Label;
        public string Tooltip;
        public Action Callback;
        public bool IsSeparator;
        
        public static ToolbarMenuItem Separator()
        {
            return new ToolbarMenuItem { IsSeparator = true };
        }
    }
    
    /// <summary>
    /// Main Package Exporter window with profile management and batch export capabilities.
    /// Modern UI Toolkit implementation matching Package Guardian design system.
    /// </summary>
    public class YUCPPackageExporterWindow : EditorWindow
    {
        [MenuItem("Tools/YUCP/Package Exporter")]
        public static void ShowWindow()
        {
            var window = GetWindow<YUCPPackageExporterWindow>();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DevTools.png");
            window.titleContent = new GUIContent("YUCP Package Exporter", icon);
            window.minSize = new Vector2(400, 500); // Reduced minimum size for responsive design
            window.Show();
        }
        
        // UI Elements
        private ScrollView _profileListScrollView;
        private ScrollView _rightPaneScrollView;
        private VisualElement _profileListContainer;
        private VisualElement _profileListContainerOverlay;
        private VisualElement _profileDetailsContainer;
        private VisualElement _emptyState;
        private VisualElement _bottomBar;
        private VisualElement _progressContainer;
        private YucpSidebar _sidebar;
        private YucpSidebar _sidebarOverlay;
        private VisualElement _progressFill;
        private VisualElement _bannerImageContainer;
        private VisualElement _bannerGradientOverlay;
        private VisualElement _metadataSection;
        private VisualElement _bannerContainer;
        private Button _changeBannerButton;
        private EventCallback<GeometryChangedEvent> _bannerButtonGeometryCallback;
        private Label _progressText;
        private VisualElement _multiSelectInfo;
        private Button _exportSelectedButton;
        private Button _exportAllButton;
        private VisualElement _supportToast;
        
        // State
        private List<ExportProfile> allProfiles = new List<ExportProfile>();
        private ExportProfile selectedProfile;
        private HashSet<int> selectedProfileIndices = new HashSet<int>();
        private int lastClickedProfileIndex = -1;
        private bool isExporting = false;
        private float currentProgress = 0f;
        private string currentStatus = "";
        
        // Delayed rename tracking
        private double lastPackageNameChangeTime = 0;
        private const double RENAME_DELAY_SECONDS = 1.5;
        private ExportProfile pendingRenameProfile = null;
        private string pendingRenamePackageName = "";
        
        // Brandfetch configuration (Logo API)
        // Client ID provided by the user for fetching official product icons
        private const string BrandfetchClientId = "1bxid64Mup7aczewSAYMX";
        
        // Export Inspector state
        private string inspectorSearchFilter = "";
        private bool showOnlyIncluded = false;
        private bool showOnlyExcluded = false;
        private bool showExportInspector = true;
		private bool showOnlyDerived = false;
        private Dictionary<string, bool> folderExpandedStates = new Dictionary<string, bool>();
        
        // Exclusion Filters state
        private bool showExclusionFilters = false;
        
        // Dependencies filter state
        private string dependenciesSearchFilter = "";
        
        // Collapsible section states
        private bool showExportOptions = true;
        private bool showFolders = true;
        private bool showDependencies = true;
        private bool showQuickActions = true;
        
        private Texture2D logoTexture;
        private Texture2D bannerGradientTexture;
        private Texture2D dottedBorderTexture;
        
        private const float BannerHeight = 410f;
        private const string DefaultGridPlaceholderPath = "Packages/com.yucp.devtools/Resources/DefaultGrid.png";
        
        // Responsive design elements
        private Button _mobileToggleButton;
        private VisualElement _leftPaneOverlay;
        private VisualElement _overlayBackdrop;
        private VisualElement _contentContainer;
        private VisualElement _leftPane;
        private bool _isOverlayOpen = false;

        // Support banner prefs (devtools scope)
        private const string SupportUrl = "http://patreon.com/Yeusepe";
        private const string SupportPrefNeverKey = "com.yucp.devtools.support.never";
        private const string SupportPrefCounterKey = "com.yucp.devtools.support.counter";
        private const string SupportPrefCadenceKey = "com.yucp.devtools.support.cadence"; // optional override
        private const string SupportSessionDismissKey = "com.yucp.devtools.support.dismissed.session";

        private void OnEnable()
        {
            LoadProfiles();
            LoadResources();
        }
        
        private void LoadResources()
        {
            logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DevTools.png");
            CreateBannerGradientTexture();
            CreateDottedBorderTexture();
        }
        
        private static bool IsDefaultGridPlaceholder(Texture2D texture)
        {
            if (texture == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(texture);
            return assetPath == DefaultGridPlaceholderPath;
        }
        
        private static Texture2D GetPlaceholderTexture()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultGridPlaceholderPath);
        }
        
        private void CreateBannerGradientTexture()
        {
            if (bannerGradientTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(bannerGradientTexture);
            }
            
            int width = 1;
            int height = Mathf.RoundToInt(BannerHeight);
            bannerGradientTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            
            // Configurable gradient parameters
            float segment1End = 0.3f;  // Top segment ends at 30% (was 20%)
            float segment2End = 0.7f;  // Middle segment ends at 70% (was 60%)
            float alpha1 = 0.2f;        // First segment alpha (was 0.3f)
            float alpha2 = 0.6f;        // Second segment alpha (was 0.8f)
            float alpha3 = 1.0f;
            
            for (int y = 0; y < height; y++)
            {
                float t = (float)y / (height - 1);
                Color color;
                
                if (t < segment1End)
                {
                    float localT = t / segment1End;
                    color = new Color(0.082f, 0.082f, 0.082f, Mathf.Lerp(0f, alpha1, localT));
                }
                else if (t < segment2End)
                {
                    float localT = (t - segment1End) / (segment2End - segment1End);
                    color = new Color(0.082f, 0.082f, 0.082f, Mathf.Lerp(alpha1, alpha2, localT));
                }
                else
                {
                    float localT = (t - segment2End) / (1.0f - segment2End);
                    color = new Color(0.082f, 0.082f, 0.082f, Mathf.Lerp(alpha2, alpha3, localT));
                }
                
                for (int x = 0; x < width; x++)
                {
                    bannerGradientTexture.SetPixel(x, height - 1 - y, color);
                }
            }
            
            bannerGradientTexture.Apply();
        }
        
        private void CreateDottedBorderTexture()
        {
            int size = 16;
            dottedBorderTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            
            Color transparent = new Color(0, 0, 0, 0);
            Color teal = new Color(54f / 255f, 191f / 255f, 177f / 255f, 0.6f);
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isBorder = (x == 0 || x == size - 1 || y == 0 || y == size - 1);
                    bool isDot = isBorder && ((x % 4 < 2 && y == 0) || (x % 4 < 2 && y == size - 1) || 
                                              (y % 4 < 2 && x == 0) || (y % 4 < 2 && x == size - 1));
                    dottedBorderTexture.SetPixel(x, y, isDot ? teal : transparent);
                }
            }
            
            dottedBorderTexture.Apply();
            dottedBorderTexture.wrapMode = TextureWrapMode.Repeat;
        }
        
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
            
            // Create left pane overlay (for mobile)
            _leftPaneOverlay = CreateLeftPane(isOverlay: true);
            _leftPaneOverlay.AddToClassList("yucp-left-pane-overlay");
            _leftPaneOverlay.style.display = DisplayStyle.None;
            _leftPaneOverlay.style.visibility = Visibility.Hidden;
            _contentContainer.Add(_leftPaneOverlay);
            
            // Create normal left pane
            _leftPane = CreateLeftPane(isOverlay: false);
            _contentContainer.Add(_leftPane);
            
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
            var centerYInBanner = metadataTop / 2f;
            
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
            
            string bannerPath = EditorUtility.OpenFilePanel("Select Banner Image", "", "png,jpg,jpeg");
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
        
        private VisualElement CreateTopBar()
        {
            var topBar = new VisualElement();
            topBar.AddToClassList("yucp-top-bar");
            
            // Mobile toggle button (hamburger menu)
            _mobileToggleButton = new Button(ToggleOverlay);
            _mobileToggleButton.text = "≡";
            _mobileToggleButton.AddToClassList("yucp-mobile-toggle");
            topBar.Add(_mobileToggleButton);
            
            
            // Logo image
            var packageLogo = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/Icons/PackageLogo.png");
            if (packageLogo != null)
            {
                var logo = new VisualElement();
                logo.AddToClassList("yucp-package-logo");
                logo.style.backgroundImage = new StyleBackground(packageLogo);
                topBar.Add(logo);
            }
            
            // Spacer to push buttons to the right
            var spacer = new VisualElement();
            spacer.AddToClassList("yucp-menu-spacer");
            topBar.Add(spacer);
            
            // Menu button groups
            topBar.Add(CreateMenuButtonGroup());
            
            return topBar;
        }
        
        private VisualElement CreateMenuButtonGroup()
        {
            var container = new VisualElement();
            container.AddToClassList("yucp-menu-button-group");
            
            // Export dropdown button
            var exportButton = CreateDropdownButton("Export", GetExportMenuItems());
            container.Add(exportButton);
            
            // Texture Array Builder
            container.Add(CreateActionButton("Texture", "Open Texture Array Builder", () => {
                var windowType = System.Type.GetType("YUCP.DevTools.Editor.TextureArrayBuilder.TextureArrayBuilderWindow, yucp.devtools.Editor");
                if (windowType != null)
                {
                    var method = windowType.GetMethod("ShowWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    method?.Invoke(null, null);
                }
            }));
            
            // Utilities dropdown
            var utilitiesButton = CreateDropdownButton("Utilities", GetUtilitiesMenuItems());
            container.Add(utilitiesButton);
            
            // Debug dropdown
            var debugButton = CreateDropdownButton("Debug", GetDebugMenuItems());
            container.Add(debugButton);
            
            return container;
        }
        
        private Button CreateActionButton(string label, string tooltip, Action callback)
        {
            var button = new Button(callback);
            button.text = label;
            button.tooltip = tooltip;
            button.AddToClassList("yucp-menu-button");
            return button;
        }
        
        private Button CreateHoverOverlayButton(string text, Action onClick, VisualElement parentContainer)
        {
            var button = new Button(onClick);
            button.text = text;
            button.AddToClassList("yucp-overlay-hover-button");
            button.style.display = DisplayStyle.Flex;
            button.pickingMode = PickingMode.Ignore;
            
            parentContainer.RegisterCallback<MouseEnterEvent>(evt =>
            {
                button.AddToClassList("yucp-overlay-hover-button-visible");
                button.pickingMode = PickingMode.Position;
            });
            parentContainer.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                button.RemoveFromClassList("yucp-overlay-hover-button-visible");
                button.pickingMode = PickingMode.Ignore;
            });
            
            parentContainer.Add(button);
            return button;
        }
        
        private Button CreateDropdownButton(string label, List<ToolbarMenuItem> items)
        {
            var button = new Button();
            button.clicked += () => ShowDropdownMenu(button, items);
            button.text = label + " ▼";
            button.AddToClassList("yucp-menu-button");
            button.AddToClassList("yucp-menu-dropdown");
            return button;
        }
        
        private void ShowDropdownMenu(VisualElement anchor, List<ToolbarMenuItem> items)
        {
            var menu = new GenericMenu();
            
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    menu.AddSeparator("");
                }
                else
                {
                    menu.AddItem(new GUIContent(item.Label), false, () => item.Callback?.Invoke());
                }
            }
            
            menu.ShowAsContext();
        }
        
        private List<ToolbarMenuItem> GetExportMenuItems()
        {
            return new List<ToolbarMenuItem>
            {
                new ToolbarMenuItem
                {
                    Label = "Export Selected",
                    Tooltip = "Export selected profile(s)",
                    Callback = () => ExportSelectedProfiles()
                },
                new ToolbarMenuItem
                {
                    Label = "Export All",
                    Tooltip = "Export all profiles",
                    Callback = () => ExportAllProfiles()
                },
                ToolbarMenuItem.Separator(),
                new ToolbarMenuItem
                {
                    Label = "Quick Export Current",
                    Tooltip = "Quick export the currently selected profile",
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
            
            leftPane.Add(container);
            return leftPane;
        }

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
            
            // Profile details container (initially empty)
            _profileDetailsContainer = new VisualElement();
            _profileDetailsContainer.style.display = DisplayStyle.None;
            _rightPaneScrollView.Add(_profileDetailsContainer);
            
            rightPane.Add(_rightPaneScrollView);
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
            _exportSelectedButton = new Button(ExportSelectedProfiles);
            _exportSelectedButton.AddToClassList("yucp-button");
            _exportSelectedButton.AddToClassList("yucp-button-primary");
            _exportSelectedButton.AddToClassList("yucp-button-large");
            exportContainer.Add(_exportSelectedButton);
            
            _exportAllButton = new Button(() => ExportAllProfiles()) { text = "Export All Profiles" };
            _exportAllButton.AddToClassList("yucp-button");
            _exportAllButton.AddToClassList("yucp-button-export");
            _exportAllButton.AddToClassList("yucp-button-large");
            exportContainer.Add(_exportAllButton);
            
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
            bottomBar.Add(_progressContainer);
            
            return bottomBar;
        }

        private void UpdateProfileList()
        {
            // Update both normal and overlay sidebars
            if (_sidebar != null)
            {
                _sidebar.UpdateList(allProfiles, 
                    profile => CreateProfileItem(profile, allProfiles.IndexOf(profile)),
                    profile => GetProfileDisplayName(profile));
            }
            
            if (_sidebarOverlay != null)
            {
                _sidebarOverlay.UpdateList(allProfiles, 
                    profile => CreateProfileItem(profile, allProfiles.IndexOf(profile)),
                    profile => GetProfileDisplayName(profile));
            }
        }

        private VisualElement CreateProfileItem(ExportProfile profile, int index)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-profile-item");
            
            bool isSelected = selectedProfileIndices.Contains(index);
            if (isSelected)
            {
                item.AddToClassList("yucp-profile-item-selected");
            }
            
            // Icon container
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("yucp-profile-item-icon-container");
            
            var iconImage = new Image();
            Texture2D displayIcon = profile.icon;
            if (displayIcon == null)
            {
                displayIcon = GetPlaceholderTexture();
            }
            iconImage.image = displayIcon;
            iconImage.AddToClassList("yucp-profile-item-icon");
            iconContainer.Add(iconImage);
            
            item.Add(iconContainer);
            
            // Content column
            var contentColumn = new VisualElement();
            contentColumn.AddToClassList("yucp-profile-item-content");
            contentColumn.style.flexGrow = 1;
            
            // Profile name
            var nameLabel = new Label(GetProfileDisplayName(profile));
            nameLabel.AddToClassList("yucp-profile-item-name");
            contentColumn.Add(nameLabel);
            
            // Profile info
            var infoText = $"v{profile.version}";
            if (profile.foldersToExport.Count > 0)
            {
                infoText += $" • {profile.foldersToExport.Count} folder(s)";
            }
            var infoLabel = new Label(infoText);
            infoLabel.AddToClassList("yucp-profile-item-info");
            contentColumn.Add(infoLabel);
            
            item.Add(contentColumn);
            
            // Click handler with multi-selection support
            item.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left click
                {
                    HandleProfileSelection(index, evt);
                    evt.StopPropagation();
                }
                else if (evt.button == 1) // Right click
                {
                    ShowProfileContextMenu(profile, index, evt);
                    evt.StopPropagation();
                }
            });
            
            return item;
        }
        
        private void ShowProfileContextMenu(ExportProfile profile, int index, MouseDownEvent evt)
        {
            // Select the profile if not already selected
            if (!selectedProfileIndices.Contains(index))
            {
                selectedProfileIndices.Clear();
                selectedProfileIndices.Add(index);
                selectedProfile = profile;
                lastClickedProfileIndex = index;
                UpdateProfileList();
                UpdateProfileDetails();
                UpdateBottomBar();
            }
            
            var menu = new GenericMenu();
            
            // Export option
            menu.AddItem(new GUIContent("Export"), false, () => 
            {
                ExportSingleProfile(profile);
            });
            
            menu.AddSeparator("");
            
            // Clone option
            menu.AddItem(new GUIContent("Clone"), false, () => 
            {
                CloneProfile(profile);
            });
            
            // Duplicate option (same as clone)
            menu.AddItem(new GUIContent("Duplicate"), false, () => 
            {
                CloneProfile(profile);
            });
            
            menu.AddSeparator("");
            
            // Rename option
            menu.AddItem(new GUIContent("Rename"), false, () => 
            {
                StartRenameProfile(profile);
            });
            
            // Delete option
            menu.AddItem(new GUIContent("Delete"), false, () => 
            {
                DeleteProfile(profile);
            });
            
            menu.AddSeparator("");
            
            // Select in Project option
            menu.AddItem(new GUIContent("Select in Project"), false, () => 
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            });
            
            if (!string.IsNullOrEmpty(profile.profileSaveLocation) && System.IO.Directory.Exists(profile.profileSaveLocation))
            {
                menu.AddItem(new GUIContent("Show in Explorer"), false, () => 
                {
                    EditorUtility.RevealInFinder(profile.profileSaveLocation);
                });
            }
            
            menu.ShowAsContext();
        }
        
        private void StartRenameProfile(ExportProfile profile)
        {
            // Focus on the package name field in the details panel if visible
            if (selectedProfile == profile && _profileDetailsContainer.style.display == DisplayStyle.Flex)
            {
                // The package name field should get focus
                EditorUtility.DisplayDialog("Rename Profile", 
                    $"Edit the 'Package Name' field in the details panel to rename this profile.\n\nCurrent name: {profile.packageName}", 
                    "OK");
            }
        }
        
        private void ExportSingleProfile(ExportProfile profile)
        {
            ExportProfile(profile);
        }

        private void HandleProfileSelection(int index, MouseDownEvent evt)
        {
            if (evt.ctrlKey || evt.commandKey)
            {
                // Ctrl/Cmd+Click: Toggle individual selection
                if (selectedProfileIndices.Contains(index))
                {
                    selectedProfileIndices.Remove(index);
                }
                else
                {
                    selectedProfileIndices.Add(index);
                }
                lastClickedProfileIndex = index;
            }
            else if (evt.shiftKey && lastClickedProfileIndex >= 0)
            {
                // Shift+Click: Range selection
                int start = Mathf.Min(lastClickedProfileIndex, index);
                int end = Mathf.Max(lastClickedProfileIndex, index);
                
                for (int i = start; i <= end; i++)
                {
                    if (i < allProfiles.Count)
                    {
                        selectedProfileIndices.Add(i);
                    }
                }
            }
            else
            {
                // Normal click: Single selection
                selectedProfileIndices.Clear();
                selectedProfileIndices.Add(index);
                lastClickedProfileIndex = index;
            }
            
            // Update selected profile
            if (selectedProfileIndices.Count > 0)
            {
                int firstIndex = selectedProfileIndices.Min();
                selectedProfile = allProfiles[firstIndex];
            }
            else
            {
                selectedProfile = null;
            }
            
            // Refresh UI
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
            
            // Close overlay when profile is selected (for mobile)
            CloseOverlay();
        }

        private void UpdateProfileDetails()
        {
            if (selectedProfile == null)
            {
                _emptyState.style.display = DisplayStyle.Flex;
                _profileDetailsContainer.style.display = DisplayStyle.None;
                return;
            }
            
            _emptyState.style.display = DisplayStyle.None;
            _profileDetailsContainer.style.display = DisplayStyle.Flex;
            _profileDetailsContainer.Clear();
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

        private VisualElement CreateMetadataSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.AddToClassList("yucp-metadata-section");
            
            // Hero-style header with icon and name
            var headerRow = new VisualElement();
            headerRow.AddToClassList("yucp-metadata-header");
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 16;
            
            // Icon preview - clickable to change
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("yucp-metadata-icon-container");
            iconContainer.tooltip = "Click to change icon";
            
            var iconImageContainer = new VisualElement();
            iconImageContainer.AddToClassList("yucp-metadata-icon-image-container");
            
            var iconImage = new Image();
            Texture2D displayIcon = profile.icon;
            if (displayIcon == null)
            {
                displayIcon = GetPlaceholderTexture();
            }
            iconImage.image = displayIcon;
            iconImage.AddToClassList("yucp-metadata-icon-image");
            iconImageContainer.Add(iconImage);
            
            // Change icon button overlay
            iconContainer.Add(iconImageContainer);
            CreateHoverOverlayButton("Change", () => BrowseForIcon(profile), iconContainer);
            headerRow.Add(iconContainer);
            
            // Name and version in a column
            var nameVersionColumn = new VisualElement();
            nameVersionColumn.style.flexGrow = 1;
            nameVersionColumn.style.marginLeft = 16;
            
            // Package Name - large, prominent
            var nameField = new TextField { value = string.IsNullOrEmpty(profile.packageName) ? "Untitled Package" : profile.packageName };
            nameField.AddToClassList("yucp-metadata-name-field");
            nameField.tooltip = "Unique identifier for your package";
            nameField.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Package Name");
                    profile.packageName = evt.newValue;
                    profile.profileName = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    
                    // Schedule delayed rename
                    lastPackageNameChangeTime = EditorApplication.timeSinceStartup;
                    pendingRenameProfile = profile;
                    pendingRenamePackageName = evt.newValue;
                    
                    UpdateProfileList();
                }
            });
            nameVersionColumn.Add(nameField);
            
            // Version badge
            var versionRow = new VisualElement();
            versionRow.style.flexDirection = FlexDirection.Row;
            versionRow.style.alignItems = Align.Center;
            versionRow.style.marginTop = 6;
            
            var versionLabel = new Label("Version:");
            versionLabel.AddToClassList("yucp-label-small");
            versionLabel.style.marginRight = 6;
            versionRow.Add(versionLabel);
            
            var versionField = new TextField { value = profile.version };
            versionField.AddToClassList("yucp-input");
            versionField.AddToClassList("yucp-metadata-version-field");
            versionField.tooltip = "Package version (X.Y.Z)";
            versionField.style.width = 120;
            versionField.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Version");
                    profile.version = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    UpdateProfileList();
                    UpdateValidationDisplay(profile);
                }
            });
            versionRow.Add(versionField);
            nameVersionColumn.Add(versionRow);
            
            headerRow.Add(nameVersionColumn);
            section.Add(headerRow);
            
            // Description - prominent, multiline
            var descLabel = new Label("Description");
            descLabel.AddToClassList("yucp-label");
            descLabel.style.marginTop = 4;
            descLabel.style.marginBottom = 6;
            section.Add(descLabel);
            
            var descField = new TextField { value = profile.description, multiline = true };
            descField.AddToClassList("yucp-input");
            descField.AddToClassList("yucp-input-multiline");
            descField.AddToClassList("yucp-metadata-description-field");
            descField.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Description");
                    profile.description = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            section.Add(descField);
            
            // Author field - compact, single-line
            var authorLabel = new Label("Author");
            authorLabel.AddToClassList("yucp-label");
            authorLabel.style.marginTop = 4;
            authorLabel.style.marginBottom = 6;
            section.Add(authorLabel);
            
            var authorField = new TextField { value = profile.author };
            authorField.AddToClassList("yucp-input");
            authorField.AddToClassList("yucp-metadata-author-field");
            authorField.tooltip = "Author(s) - separate multiple authors with commas";
            authorField.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Author");
                    profile.author = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            section.Add(authorField);
            
            // Official Product Links section
            var linksLabel = new Label("Official Product Links");
            linksLabel.AddToClassList("yucp-label");
            linksLabel.style.marginTop = 16;
            linksLabel.style.marginBottom = 6;
            section.Add(linksLabel);
            
            var linksContainer = new VisualElement();
            linksContainer.AddToClassList("yucp-product-links-container");
            
            // Add existing links
            if (profile.productLinks != null)
            {
                foreach (var link in profile.productLinks)
                {
                    linksContainer.Add(CreateProductLinkCard(profile, link));
                }
            }
            
            // Add Link button
            var addLinkButton = new Button(() => AddProductLink(profile, linksContainer));
            addLinkButton.text = "+ Add Link";
            addLinkButton.AddToClassList("yucp-button");
            addLinkButton.AddToClassList("yucp-button-secondary");
            addLinkButton.style.marginTop = 8;
            addLinkButton.style.marginBottom = 4;
            linksContainer.Add(addLinkButton);
            
            section.Add(linksContainer);
            
            return section;
        }

        private VisualElement CreateProductLinkCard(ExportProfile profile, ProductLink link)
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-product-link-card");
            
            var cardContent = new VisualElement();
            cardContent.AddToClassList("yucp-product-link-content");
            cardContent.style.flexDirection = FlexDirection.Row;
            cardContent.style.alignItems = Align.Center;
            
            // Icon container - clickable to set custom icon
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("yucp-product-link-icon");
            iconContainer.tooltip = "Click to set custom icon";
            var iconImage = new Image();
            
            Texture2D displayIcon = link.GetDisplayIcon();
            if (displayIcon != null)
            {
                iconImage.image = displayIcon;
            }
            else
            {
                iconImage.image = GetPlaceholderTexture();
                // Fetch favicon if URL is provided and no custom icon
                if (!string.IsNullOrEmpty(link.url))
                {
                    FetchFavicon(profile, link, iconImage);
                }
            }
            
            iconContainer.Add(iconImage);
            
            // Add hover overlay button to browse for custom icon
            CreateHoverOverlayButton("+", () => BrowseForProductLinkIcon(profile, link, iconImage), iconContainer);
            
            cardContent.Add(iconContainer);
            
            // Link info container
            var infoContainer = new VisualElement();
            infoContainer.style.flexGrow = 1;
            infoContainer.style.marginLeft = 12;
            
            // Label field (optional)
            var labelRow = new VisualElement();
            labelRow.style.position = Position.Relative;
            var labelField = new TextField { value = link.label };
            labelField.AddToClassList("yucp-input");
            labelField.AddToClassList("yucp-product-link-label");
            
            Label labelPlaceholder = null;
            if (string.IsNullOrEmpty(link.label))
            {
                labelPlaceholder = new Label("Link label (optional)");
                labelPlaceholder.AddToClassList("yucp-label-secondary");
                labelPlaceholder.style.position = Position.Absolute;
                labelPlaceholder.style.left = 8;
                labelPlaceholder.style.top = 4;
                labelPlaceholder.pickingMode = PickingMode.Ignore;
                labelRow.Add(labelPlaceholder);
            }
            
            labelField.RegisterValueChangedCallback(evt =>
            {
                if (labelPlaceholder != null)
                {
                    if (string.IsNullOrEmpty(evt.newValue))
                    {
                        labelPlaceholder.style.display = DisplayStyle.Flex;
                        labelField.style.opacity = 0.7f;
                    }
                    else
                    {
                        labelPlaceholder.style.display = DisplayStyle.None;
                        labelField.style.opacity = 1f;
                    }
                }
                
                if (profile != null && link != null)
                {
                    Undo.RecordObject(profile, "Change Link Label");
                    link.label = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            labelRow.Add(labelField);
            infoContainer.Add(labelRow);
            
            // URL field
            var urlRow = new VisualElement();
            urlRow.style.position = Position.Relative;
            urlRow.style.marginTop = 6;
            var urlField = new TextField { value = link.url };
            urlField.AddToClassList("yucp-input");
            urlField.AddToClassList("yucp-product-link-url");
            
            Label urlPlaceholder = null;
            if (string.IsNullOrEmpty(link.url))
            {
                urlPlaceholder = new Label("https://example.com");
                urlPlaceholder.AddToClassList("yucp-label-secondary");
                urlPlaceholder.style.position = Position.Absolute;
                urlPlaceholder.style.left = 8;
                urlPlaceholder.style.top = 4;
                urlPlaceholder.pickingMode = PickingMode.Ignore;
                urlRow.Add(urlPlaceholder);
            }
            
            urlField.RegisterValueChangedCallback(evt =>
            {
                if (urlPlaceholder != null)
                {
                    if (string.IsNullOrEmpty(evt.newValue))
                    {
                        urlPlaceholder.style.display = DisplayStyle.Flex;
                        urlField.style.opacity = 0.7f;
                    }
                    else
                    {
                        urlPlaceholder.style.display = DisplayStyle.None;
                        urlField.style.opacity = 1f;
                    }
                }
                
                if (profile != null && link != null)
                {
                    Undo.RecordObject(profile, "Change Link URL");
                    link.url = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            
            // Fetch favicon when user finishes typing (on blur)
            urlField.RegisterCallback<BlurEvent>(evt =>
            {
                Debug.Log($"[YUCP PackageExporter] URL field blur event triggered for URL: {link.url}");
                if (profile != null && link != null && !string.IsNullOrEmpty(link.url))
                {
                    Debug.Log($"[YUCP PackageExporter] Calling FetchFavicon from blur event");
                    FetchFavicon(profile, link, iconImage);
                }
                else
                {
                    Debug.LogWarning($"[YUCP PackageExporter] Blur event: profile or link is null, or URL is empty. Profile: {profile != null}, Link: {link != null}, URL: {link?.url ?? "null"}");
                }
            });
            urlRow.Add(urlField);
            infoContainer.Add(urlRow);
            
            cardContent.Add(infoContainer);
            
            // Action buttons
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.marginLeft = 8;
            
            // Clear custom icon button (only show if custom icon is set)
            if (link.customIcon != null)
            {
                var clearIconButton = new Button(() =>
                {
                    if (profile != null && link != null)
                    {
                        Undo.RecordObject(profile, "Clear Custom Link Icon");
                        link.customIcon = null;
                        Texture2D displayIcon = link.GetDisplayIcon();
                        iconImage.image = displayIcon != null ? displayIcon : GetPlaceholderTexture();
                        EditorUtility.SetDirty(profile);
                    }
                });
                clearIconButton.text = "↺";
                clearIconButton.tooltip = "Clear custom icon (use auto-fetched)";
                clearIconButton.AddToClassList("yucp-button");
                clearIconButton.AddToClassList("yucp-button-icon");
                clearIconButton.style.width = 32;
                clearIconButton.style.height = 32;
                buttonContainer.Add(clearIconButton);
            }
            
            // Open in browser button
            var openButton = new Button(() =>
            {
                if (!string.IsNullOrEmpty(link.url))
                {
                    Application.OpenURL(link.url);
                }
            });
            openButton.text = "→";
            openButton.tooltip = "Open in browser";
            openButton.AddToClassList("yucp-button");
            openButton.AddToClassList("yucp-button-icon");
            openButton.style.width = 32;
            openButton.style.height = 32;
            if (link.customIcon != null)
            {
                openButton.style.marginLeft = 4;
            }
            buttonContainer.Add(openButton);
            
            // Remove button
            var removeButton = new Button(() =>
            {
                if (profile != null && profile.productLinks != null)
                {
                    Undo.RecordObject(profile, "Remove Product Link");
                    profile.productLinks.Remove(link);
                    card.RemoveFromHierarchy();
                    EditorUtility.SetDirty(profile);
                }
            });
            removeButton.text = "×";
            removeButton.tooltip = "Remove link";
            removeButton.AddToClassList("yucp-button");
            removeButton.AddToClassList("yucp-button-icon");
            removeButton.style.width = 32;
            removeButton.style.height = 32;
            removeButton.style.marginLeft = 4;
            buttonContainer.Add(removeButton);
            
            cardContent.Add(buttonContainer);
            card.Add(cardContent);
            
            return card;
        }

        private void AddProductLink(ExportProfile profile, VisualElement container)
        {
            if (profile.productLinks == null)
            {
                profile.productLinks = new List<ProductLink>();
            }
            
            var newLink = new ProductLink();
            Undo.RecordObject(profile, "Add Product Link");
            profile.productLinks.Add(newLink);
            EditorUtility.SetDirty(profile);
            
            // Find the add button (last child)
            var addButton = container.Children().Last();
            var linkCard = CreateProductLinkCard(profile, newLink);
            container.Insert(container.childCount - 1, linkCard);
        }

        private void FetchFavicon(ExportProfile profile, ProductLink link, Image iconImage)
        {
            Debug.Log($"[YUCP PackageExporter] FetchFavicon called for URL: {link.url}");
            
            if (string.IsNullOrEmpty(link.url))
            {
                Debug.LogWarning("[YUCP PackageExporter] FetchFavicon: URL is empty, aborting");
                return;
            }
            
            // Show loading state
            iconImage.image = GetPlaceholderTexture();
            
            // Extract domain from URL
            string domain = ExtractDomain(link.url);
            Debug.Log($"[YUCP PackageExporter] Extracted domain: {domain}");
            
            // Reduce to base domain for Brandfetch and favicon (e.g. yeusepe.gumroad.com -> gumroad.com)
            string baseDomain = ExtractBaseDomain(domain);
            Debug.Log($"[YUCP PackageExporter] Base domain: {baseDomain}");
            
            if (string.IsNullOrEmpty(baseDomain))
            {
                Debug.LogWarning("[YUCP PackageExporter] FetchFavicon: Failed to extract domain, aborting");
                return;
            }
            
            // Start async fetch
            EditorApplication.update += CheckFaviconRequest;
            
            var requestData = new FaviconRequestData
            {
                profile = profile,
                link = link,
                iconImage = iconImage,
                domain = baseDomain,
                currentUrlIndex = 0,
                request = null,
                isBrandfetchSearch = false,
                brandId = null
            };
            
            faviconRequests.Add(requestData);
            Debug.Log($"[YUCP PackageExporter] Starting favicon request for domain: {domain}");
            StartFaviconRequest(requestData);
        }

        private class FaviconRequestData
        {
            public ExportProfile profile;
            public ProductLink link;
            public Image iconImage;
            public string domain;
            public int currentUrlIndex;
            public UnityWebRequest request;
            public bool isBrandfetchSearch; // True if this request is for Brandfetch search API
            public string brandId; // Brandfetch brandId after search
        }

        private List<FaviconRequestData> faviconRequests = new List<FaviconRequestData>();

        private void CheckFaviconRequest()
        {
            for (int i = faviconRequests.Count - 1; i >= 0; i--)
            {
                var requestData = faviconRequests[i];
                
                if (requestData.request == null)
                {
                    Debug.LogWarning($"[YUCP PackageExporter] CheckFaviconRequest: Request is null for domain {requestData.domain}");
                    continue;
                }
                
                if (requestData.request.isDone)
                {
                    Debug.Log($"[YUCP PackageExporter] Request completed for domain {requestData.domain}, result: {requestData.request.result}, URL index: {requestData.currentUrlIndex}");
                    bool success = false;
                    
                    if (requestData.request.result == UnityWebRequest.Result.Success)
                    {
                        // Handle Brandfetch search API response
                        if (requestData.isBrandfetchSearch)
                        {
                            string jsonResponse = requestData.request.downloadHandler.text;
                            Debug.Log($"[YUCP PackageExporter] Brandfetch search response: {jsonResponse}");
                            
                            // Parse JSON to extract brandId (simple string parsing)
                            string brandId = ExtractBrandIdFromJson(jsonResponse);
                            if (!string.IsNullOrEmpty(brandId))
                            {
                                Debug.Log($"[YUCP PackageExporter] Found Brandfetch brandId: {brandId}");
                                requestData.brandId = brandId;
                                requestData.isBrandfetchSearch = false;
                                // Now fetch the actual icon
                                StartFaviconRequest(requestData);
                                continue;
                            }
                            else
                            {
                                Debug.LogWarning("[YUCP PackageExporter] Failed to extract brandId from Brandfetch response");
                                // Fall through to try next URL
                            }
                        }
                        
                        byte[] imageData = requestData.request.downloadHandler.data;
                        if (imageData != null && imageData.Length > 0)
                        {
                            Debug.Log($"[YUCP PackageExporter] Downloaded {imageData.Length} bytes of image data");
                            
                            // Check if this is an ICO file (ICO files start with 00 00 01 00)
                            bool isIcoFile = imageData.Length >= 4 && 
                                           imageData[0] == 0x00 && imageData[1] == 0x00 && 
                                           imageData[2] == 0x01 && imageData[3] == 0x00;
                            
                            if (isIcoFile)
                            {
                                Debug.Log($"[YUCP PackageExporter] Detected ICO file format, extracting bitmap from ICO");
                                Texture2D texture = ExtractBitmapFromIco(imageData);
                                if (texture != null && texture.width > 0 && texture.height > 0)
                                {
                                    Debug.Log($"[YUCP PackageExporter] Successfully extracted bitmap from ICO: {texture.width}x{texture.height}");
                                    
                                    // Resize if needed
                                    if (texture.width != 32 || texture.height != 32)
                                    {
                                        texture = ResizeTexture(texture, 32, 32);
                                        Debug.Log("[YUCP PackageExporter] Resized favicon to 32x32");
                                    }
                                    
                                    // Save the icon as an asset so it persists across domain reloads
                                    string iconAssetPath = PackageBuilder.SaveTextureAsTemporaryAsset(texture, requestData.link.label ?? requestData.domain);
                                    if (!string.IsNullOrEmpty(iconAssetPath))
                                    {
                                        Texture2D savedIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconAssetPath);
                                        if (savedIcon != null)
                                        {
                                            requestData.link.icon = savedIcon;
                                            Debug.Log($"[YUCP PackageExporter] Saved favicon as asset: {iconAssetPath}");
                                        }
                                        else
                                        {
                                            // Fallback to in-memory texture if asset loading fails
                                            requestData.link.icon = texture;
                                            Debug.LogWarning($"[YUCP PackageExporter] Failed to load saved icon asset, using in-memory texture");
                                        }
                                    }
                                    else
                                    {
                                        // Fallback to in-memory texture if saving fails
                                        requestData.link.icon = texture;
                                        Debug.LogWarning($"[YUCP PackageExporter] Failed to save favicon as asset, using in-memory texture");
                                    }
                                    
                                    // Only set fetched icon if no custom icon is set
                                    if (requestData.link.customIcon == null)
                                    {
                                        requestData.iconImage.image = requestData.link.icon;
                                        Debug.Log($"[YUCP PackageExporter] Favicon successfully set for {requestData.domain}");
                                    }
                                    else
                                    {
                                        Debug.Log($"[YUCP PackageExporter] Favicon fetched but custom icon is displayed for {requestData.domain}");
                                    }
                                    if (requestData.profile != null)
                                    {
                                        EditorUtility.SetDirty(requestData.profile);
                                    }
                                    success = true;
                                }
                                else
                                {
                                    Debug.LogWarning($"[YUCP PackageExporter] Failed to extract bitmap from ICO file");
                                }
                            }
                            else
                            {
                                // Try to load as texture from raw bytes (PNG, JPG, etc.)
                                Texture2D texture = new Texture2D(2, 2);
                                bool loaded = texture.LoadImage(imageData);
                                
                                if (loaded && texture.width > 2 && texture.height > 2)
                                {
                                    Debug.Log($"[YUCP PackageExporter] Successfully loaded favicon: {texture.width}x{texture.height}");
                                    
                                    // Resize if needed (favicons can be various sizes)
                                    if (texture.width != 32 || texture.height != 32)
                                    {
                                        texture = ResizeTexture(texture, 32, 32);
                                        Debug.Log("[YUCP PackageExporter] Resized favicon to 32x32");
                                    }
                                    
                                    // Save the icon as an asset so it persists across domain reloads
                                    string iconAssetPath = PackageBuilder.SaveTextureAsTemporaryAsset(texture, requestData.link.label ?? requestData.domain);
                                    if (!string.IsNullOrEmpty(iconAssetPath))
                                    {
                                        Texture2D savedIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconAssetPath);
                                        if (savedIcon != null)
                                        {
                                            requestData.link.icon = savedIcon;
                                            Debug.Log($"[YUCP PackageExporter] Saved favicon as asset: {iconAssetPath}");
                                        }
                                        else
                                        {
                                            // Fallback to in-memory texture if asset loading fails
                                            requestData.link.icon = texture;
                                            Debug.LogWarning($"[YUCP PackageExporter] Failed to load saved icon asset, using in-memory texture");
                                        }
                                    }
                                    else
                                    {
                                        // Fallback to in-memory texture if saving fails
                                        requestData.link.icon = texture;
                                        Debug.LogWarning($"[YUCP PackageExporter] Failed to save favicon as asset, using in-memory texture");
                                    }
                                    
                                    // Only set fetched icon if no custom icon is set
                                    if (requestData.link.customIcon == null)
                                    {
                                        requestData.iconImage.image = requestData.link.icon;
                                        Debug.Log($"[YUCP PackageExporter] Favicon successfully set for {requestData.domain}");
                                    }
                                    else
                                    {
                                        Debug.Log($"[YUCP PackageExporter] Favicon fetched but custom icon is displayed for {requestData.domain}");
                                    }
                                    if (requestData.profile != null)
                                    {
                                        EditorUtility.SetDirty(requestData.profile);
                                    }
                                    success = true;
                                }
                                else
                                {
                                    Debug.LogWarning($"[YUCP PackageExporter] Failed to load image data as texture (width: {texture.width}, height: {texture.height}, loaded: {loaded})");
                                    UnityEngine.Object.DestroyImmediate(texture);
                                }
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[YUCP PackageExporter] Downloaded data is null or empty");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[YUCP PackageExporter] Request failed: {requestData.request.error}");
                    }
                    
                    requestData.request.Dispose();
                    requestData.request = null;
                    
                    if (!success)
                    {
                        // Try next URL
                        requestData.currentUrlIndex++;
                        Debug.Log($"[YUCP PackageExporter] Trying next favicon URL (index {requestData.currentUrlIndex})");
                        if (requestData.currentUrlIndex < 3)
                        {
                            StartFaviconRequest(requestData);
                            continue;
                        }
                        else
                        {
                            Debug.LogWarning($"[YUCP PackageExporter] All favicon URLs failed for domain: {requestData.domain}");
                        }
                    }
                    
                    faviconRequests.RemoveAt(i);
                }
            }
            
            if (faviconRequests.Count == 0)
            {
                EditorApplication.update -= CheckFaviconRequest;
                Debug.Log("[YUCP PackageExporter] All favicon requests completed, removed update callback");
            }
        }

        private void StartFaviconRequest(FaviconRequestData requestData)
        {
            // Try Brandfetch Logo API first (using base domain), then direct favicon, then DuckDuckGo fallback
            string[] faviconUrls = new string[]
            {
                $"https://cdn.brandfetch.io/{requestData.domain}/w/128/h/128/theme/dark/icon.png?c={BrandfetchClientId}",
                $"https://{requestData.domain}/favicon.ico",
                $"https://icons.duckduckgo.com/ip3/{requestData.domain}.ico"
            };
            
            if (requestData.currentUrlIndex >= faviconUrls.Length)
            {
                Debug.LogWarning($"[YUCP PackageExporter] URL index {requestData.currentUrlIndex} is out of range");
                return;
            }
            
            string faviconUrl = faviconUrls[requestData.currentUrlIndex];
            Debug.Log($"[YUCP PackageExporter] Starting request for favicon URL: {faviconUrl}");
            
            // Use regular UnityWebRequest instead of UnityWebRequestTexture to handle various formats
            requestData.request = UnityWebRequest.Get(faviconUrl);
            requestData.request.timeout = 5;
            
            // Set User-Agent to avoid blocking by some services
            requestData.request.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            var operation = requestData.request.SendWebRequest();
            
            if (operation == null)
            {
                Debug.LogError("[YUCP PackageExporter] Failed to start web request");
            }
            else
            {
                Debug.Log($"[YUCP PackageExporter] Web request started successfully");
            }
        }

        private string ExtractDomain(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning("[YUCP PackageExporter] ExtractDomain: URL is empty");
                return "";
            }
            
            try
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    url = "https://" + url;
                    Debug.Log($"[YUCP PackageExporter] ExtractDomain: Added https:// prefix, new URL: {url}");
                }
                
                Uri uri = new Uri(url);
                string domain = uri.Host;
                Debug.Log($"[YUCP PackageExporter] ExtractDomain: Extracted domain '{domain}' from URL '{url}'");
                return domain;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageExporter] ExtractDomain failed for URL '{url}': {ex.Message}");
                return "";
            }
        }

        private string ExtractBrandIdFromJson(string json)
        {
            try
            {
                // Simple JSON parsing: look for "brandId":"value"
                int brandIdIndex = json.IndexOf("\"brandId\"");
                if (brandIdIndex < 0)
                    return null;
                
                int colonIndex = json.IndexOf(':', brandIdIndex);
                if (colonIndex < 0)
                    return null;
                
                int quoteStart = json.IndexOf('"', colonIndex);
                if (quoteStart < 0)
                    return null;
                
                int quoteEnd = json.IndexOf('"', quoteStart + 1);
                if (quoteEnd < 0)
                    return null;
                
                return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageExporter] Error parsing Brandfetch JSON: {ex.Message}");
                return null;
            }
        }

        private string ExtractBaseDomain(string host)
        {
            if (string.IsNullOrEmpty(host))
                return host;
            
            var parts = host.Split('.');
            if (parts.Length <= 2)
                return host;
            
            // Join last two labels (e.g., yeusepe.gumroad.com -> gumroad.com)
            string baseDomain = parts[parts.Length - 2] + "." + parts[parts.Length - 1];
            Debug.Log($"[YUCP PackageExporter] ExtractBaseDomain: host='{host}' -> baseDomain='{baseDomain}'");
            return baseDomain;
        }

        private Texture2D ResizeTexture(Texture2D source, int width, int height)
        {
            RenderTexture rt = RenderTexture.GetTemporary(width, height);
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D resized = new Texture2D(width, height);
            resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resized.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return resized;
        }

        private Texture2D ExtractBitmapFromIco(byte[] icoData)
        {
            try
            {
                using (var ms = new MemoryStream(icoData))
                using (var reader = new BinaryReader(ms))
                {
                    // ICO header: 6 bytes
                    short reserved = reader.ReadInt16(); // Should be 0
                    short type = reader.ReadInt16();     // Should be 1 (icon)
                    short count = reader.ReadInt16();    // Number of images
                    
                    if (reserved != 0 || type != 1 || count <= 0)
                    {
                        Debug.LogWarning($"[YUCP PackageExporter] Invalid ICO header: reserved={reserved}, type={type}, count={count}");
                        return null;
                    }
                    
                    if (count > 20)
                    {
                        Debug.LogWarning($"[YUCP PackageExporter] ICO contains too many images: {count}");
                        return null;
                    }
                    
                    Debug.Log($"[YUCP PackageExporter] ICO file contains {count} images");
                    
                    // Read first directory entry (16 bytes)
                    byte width = reader.ReadByte();
                    byte height = reader.ReadByte();
                    reader.ReadByte(); // color count
                    reader.ReadByte(); // reserved
                    short planes = reader.ReadInt16();
                    short bitCount = reader.ReadInt16();
                    int imageSize = reader.ReadInt32();
                    int imageOffset = reader.ReadInt32();
                    
                    Debug.Log($"[YUCP PackageExporter] ICO entry: {Math.Max((int)width, 1)}x{Math.Max((int)height, 1)}, planes={planes}, bpp={bitCount}, size={imageSize}, offset={imageOffset}");
                    
                    if (imageOffset >= icoData.Length || imageOffset + imageSize > icoData.Length)
                    {
                        Debug.LogWarning($"[YUCP PackageExporter] Invalid ICO image offset or size");
                        return null;
                    }
                    
                    // Seek to image data
                    ms.Seek(imageOffset, SeekOrigin.Begin);
                    
                    // Check if it's PNG (PNG files start with 89 50 4E 47)
                    byte[] header = reader.ReadBytes(4);
                    ms.Seek(imageOffset, SeekOrigin.Begin); // Reset
                    
                    bool isPng = header.Length >= 4 && 
                                header[0] == 0x89 && header[1] == 0x50 && 
                                header[2] == 0x4E && header[3] == 0x47;
                    
                    if (isPng)
                    {
                        Debug.Log("[YUCP PackageExporter] ICO contains PNG data, loading directly");
                        byte[] pngData = reader.ReadBytes(imageSize);
                        Texture2D texture = new Texture2D(2, 2);
                        if (texture.LoadImage(pngData))
                        {
                            return texture;
                        }
                    }
                    else
                    {
                        // Read BITMAPINFOHEADER (DIB header)
                        int headerSize = reader.ReadInt32();     // Usually 40
                        int bmpWidth = reader.ReadInt32();
                        int bmpHeight = reader.ReadInt32();       // This is total height (image + mask)
                        reader.ReadInt16();                      // planes
                        int bpp = reader.ReadInt16();
                        int compression = reader.ReadInt32();
                        int imageSizeBytes = reader.ReadInt32();
                        reader.ReadInt32();                      // xPelsPerMeter
                        reader.ReadInt32();                      // yPelsPerMeter
                        int clrUsed = reader.ReadInt32();        // clrUsed
                        reader.ReadInt32();                      // clrImportant
                        
                        // Support palette-based bitmaps (1, 4, 8 bpp) and direct color (24, 32 bpp)
                        if (bpp != 1 && bpp != 4 && bpp != 8 && bpp != 24 && bpp != 32)
                        {
                            Debug.LogWarning($"[YUCP PackageExporter] Unsupported ICO bitmap bpp: {bpp}");
                            return null;
                        }
                        
                        bool isPaletteBased = (bpp == 1 || bpp == 4 || bpp == 8);
                        
                        int realHeight = bmpHeight / 2; // ICO stores height as image+mask
                        if (realHeight <= 0 || bmpWidth <= 0)
                        {
                            Debug.LogWarning($"[YUCP PackageExporter] Invalid ICO bitmap dimensions: {bmpWidth}x{realHeight}");
                            return null;
                        }
                        
                        if (bmpWidth > 512 || realHeight > 512)
                        {
                            Debug.LogWarning($"[YUCP PackageExporter] ICO bitmap too large: {bmpWidth}x{realHeight}");
                            return null;
                        }
                        
                        // Read color palette if palette-based
                        Color32[] palette = null;
                        int paletteSize = 0;
                        if (isPaletteBased)
                        {
                            paletteSize = (bpp == 1) ? 2 : ((bpp == 4) ? 16 : 256);
                            palette = new Color32[paletteSize];
                            
                            // Palette entries are BGR (3 bytes) or BGRA (4 bytes if clrUsed > 0)
                            int paletteBytes = (clrUsed > 0 && clrUsed <= paletteSize) ? 4 : 3;
                            for (int i = 0; i < paletteSize; i++)
                            {
                                byte b = reader.ReadByte();
                                byte g = reader.ReadByte();
                                byte r = reader.ReadByte();
                                byte a = (paletteBytes == 4) ? reader.ReadByte() : (byte)255;
                                palette[i] = new Color32(r, g, b, a);
                            }
                            Debug.Log($"[YUCP PackageExporter] Read {paletteSize}-color palette for {bpp}-bit bitmap");
                        }
                        
                        // Read the pixel data (DIB, bottom-up, BGR(A) or palette indices)
                        int bytesPerPixel = isPaletteBased ? 1 : (bpp / 8);
                        int bitsPerRow = bmpWidth * bpp;
                        int rowSize = ((bitsPerRow + 31) / 32) * 4; // Row size padded to 4-byte boundary
                        int dibSize = rowSize * realHeight;
                        
                        if (dibSize > imageSize - headerSize - (isPaletteBased ? (paletteSize * (clrUsed > 0 && clrUsed <= paletteSize ? 4 : 3)) : 0))
                        {
                            int available = imageSize - headerSize - (isPaletteBased ? (paletteSize * (clrUsed > 0 && clrUsed <= paletteSize ? 4 : 3)) : 0);
                            Debug.LogWarning($"[YUCP PackageExporter] DIB size calculation: calculated={dibSize}, available={available}");
                            dibSize = available;
                        }
                        
                        byte[] dib = reader.ReadBytes(dibSize);
                        if (dib.Length < dibSize)
                        {
                            Debug.LogWarning("[YUCP PackageExporter] DIB data truncated");
                            return null;
                        }
                        
                        // Create texture and convert BGR to RGB or palette indices to colors
                        Texture2D texture = new Texture2D(bmpWidth, realHeight, TextureFormat.RGBA32, false);
                        Color32[] pixels = new Color32[bmpWidth * realHeight];
                        
                        for (int y = 0; y < realHeight; y++)
                        {
                            int srcRow = realHeight - 1 - y; // Bottom-up
                            int rowOffset = srcRow * rowSize;
                            
                            for (int x = 0; x < bmpWidth; x++)
                            {
                                Color32 color;
                                
                                if (isPaletteBased)
                                {
                                    // Read palette index based on bit depth
                                    int bitOffset = x * bpp;
                                    int byteIndex = rowOffset + (bitOffset / 8);
                                    int bitIndex = bitOffset % 8;
                                    
                                    if (byteIndex >= dib.Length)
                                        break;
                                    
                                    int paletteIndex = 0;
                                    if (bpp == 1)
                                    {
                                        // 1-bit: each bit is a palette index
                                        paletteIndex = (dib[byteIndex] >> (7 - bitIndex)) & 0x01;
                                    }
                                    else if (bpp == 4)
                                    {
                                        // 4-bit: two palette indices per byte
                                        // For 4-bit, bitIndex will be 0, 4, 8, 12, etc.
                                        // If bitIndex is 0 or 4 mod 8, it's the first pixel in that byte
                                        if ((x % 2) == 0)
                                        {
                                            // First pixel in byte (high 4 bits)
                                            paletteIndex = (dib[byteIndex] >> 4) & 0x0F;
                                        }
                                        else
                                        {
                                            // Second pixel in byte (low 4 bits)
                                            paletteIndex = dib[byteIndex] & 0x0F;
                                        }
                                    }
                                    else // bpp == 8
                                    {
                                        // 8-bit: one byte per pixel
                                        paletteIndex = dib[byteIndex];
                                    }
                                    
                                    if (paletteIndex >= paletteSize || paletteIndex < 0)
                                        paletteIndex = 0;
                                    
                                    color = palette[paletteIndex];
                                }
                                else
                                {
                                    // Direct color (24 or 32 bpp)
                                    int srcIndex = rowOffset + x * bytesPerPixel;
                                    if (srcIndex + bytesPerPixel > dib.Length)
                                        break;
                                    
                                    byte b = dib[srcIndex + 0];
                                    byte g = dib[srcIndex + 1];
                                    byte r = dib[srcIndex + 2];
                                    byte a = (bytesPerPixel == 4) ? dib[srcIndex + 3] : (byte)255;
                                    
                                    color = new Color32(r, g, b, a);
                                }
                                
                                pixels[y * bmpWidth + x] = color;
                            }
                        }
                        
                        texture.SetPixels32(pixels);
                        texture.Apply();
                        Debug.Log($"[YUCP PackageExporter] ICO bitmap extracted as texture: {bmpWidth}x{realHeight}, bpp={bpp}");
                        return texture;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageExporter] ExtractBitmapFromIco failed: {ex.Message}\n{ex.StackTrace}");
            }
            
            return null;
        }

        private VisualElement CreateSummarySection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var title = new Label("Quick Summary");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var statsContainer = new VisualElement();
            statsContainer.AddToClassList("yucp-stats-container");
            
            AddStatItem(statsContainer, "Folders to Export", profile.foldersToExport.Count.ToString());
            
            // Dependencies
            if (profile.dependencies.Count > 0)
            {
                int bundled = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Bundle);
                int referenced = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Dependency);
                AddStatItem(statsContainer, "Dependencies", $"{bundled} bundled, {referenced} referenced");
            }
            
            // Obfuscation
            string obfuscationText = profile.enableObfuscation 
                ? $"Enabled ({profile.assembliesToObfuscate.Count(a => a.enabled)} assemblies)" 
                : "Disabled";
            AddStatItem(statsContainer, "Obfuscation", obfuscationText);
            
            // Output path
            string outputText = string.IsNullOrEmpty(profile.exportPath) ? "Desktop" : profile.exportPath;
            AddStatItem(statsContainer, "Output", outputText);
            
            // Last export
            if (!string.IsNullOrEmpty(profile.LastExportTime))
            {
                AddStatItem(statsContainer, "Last Export", profile.LastExportTime);
            }
            
            section.Add(statsContainer);
            return section;
        }

        private void AddStatItem(VisualElement container, string label, string value)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-stat-item");
            
            var labelElement = new Label(label);
            labelElement.AddToClassList("yucp-stat-label");
            item.Add(labelElement);
            
            var valueElement = new Label(value);
            valueElement.AddToClassList("yucp-stat-value");
            item.Add(valueElement);
            
            container.Add(item);
        }

        private VisualElement CreateValidationSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.name = "validation-section";
            
            if (!profile.Validate(out string errorMessage))
            {
                var errorContainer = new VisualElement();
                errorContainer.AddToClassList("yucp-validation-error");
                
                var errorText = new Label($"Validation Error: {errorMessage}");
                errorText.AddToClassList("yucp-validation-error-text");
                errorContainer.Add(errorText);
                
                section.Add(errorContainer);
            }
            else
            {
                var successContainer = new VisualElement();
                successContainer.AddToClassList("yucp-validation-success");
                
                var successText = new Label("Profile is valid and ready to export");
                successText.AddToClassList("yucp-validation-success-text");
                successContainer.Add(successText);
                
                section.Add(successContainer);
            }
            
            return section;
        }

        private void UpdateValidationDisplay(ExportProfile profile)
        {
            var validationSection = _profileDetailsContainer?.Q("validation-section");
            if (validationSection != null && profile != null)
            {
                var parent = validationSection.parent;
                var index = parent.IndexOf(validationSection);
                parent.Remove(validationSection);
                parent.Insert(index, CreateValidationSection(profile));
            }
        }

        private VisualElement CreateExportOptionsSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var header = CreateCollapsibleHeader("Export Options", 
                () => showExportOptions, 
                (value) => { showExportOptions = value; }, 
                () => UpdateProfileDetails());
            section.Add(header);
            
            if (!showExportOptions)
            {
                return section;
            }
            
            // Toggles container
            var togglesContainer = new VisualElement();
            togglesContainer.style.flexDirection = FlexDirection.Column;
            togglesContainer.style.marginBottom = 8;
            
            // Include Dependencies
            var includeDepsToggle = new Toggle("Include Dependencies") { value = profile.includeDependencies };
            includeDepsToggle.AddToClassList("yucp-toggle");
            includeDepsToggle.tooltip = "Include all dependency files directly in the exported package";
            includeDepsToggle.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Include Dependencies");
                    profile.includeDependencies = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            togglesContainer.Add(includeDepsToggle);
            
            var recurseFoldersToggle = new Toggle("Recurse Folders") { value = profile.recurseFolders };
            recurseFoldersToggle.AddToClassList("yucp-toggle");
            recurseFoldersToggle.tooltip = "Search subfolders when collecting assets to export";
            recurseFoldersToggle.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Recurse Folders");
                    profile.recurseFolders = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            togglesContainer.Add(recurseFoldersToggle);
            
            // Generate package.json
            var generateJsonToggle = new Toggle("Generate package.json") { value = profile.generatePackageJson };
            generateJsonToggle.AddToClassList("yucp-toggle");
            generateJsonToggle.tooltip = "Create a package.json file with dependency information for VPM compatibility";
            generateJsonToggle.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Generate Package Json");
                    profile.generatePackageJson = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            togglesContainer.Add(generateJsonToggle);
            
            // Auto-Increment Version
            var autoIncrementToggle = new Toggle("Auto-Increment Version") { value = profile.autoIncrementVersion };
            autoIncrementToggle.AddToClassList("yucp-toggle");
            autoIncrementToggle.tooltip = "Automatically increment the version number on each export";
            autoIncrementToggle.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Auto Increment Version");
                    profile.autoIncrementVersion = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    UpdateProfileDetails(); // Refresh to show/hide increment strategy
                }
            });
            togglesContainer.Add(autoIncrementToggle);
            
            section.Add(togglesContainer);
            
            // Increment Strategy (only if auto-increment is enabled)
            if (profile.autoIncrementVersion)
            {
                var strategyRow = CreateFormRow("What to Bump", tooltip: "Choose which number to increment");
                var strategyField = new EnumField(profile.incrementStrategy);
                strategyField.AddToClassList("yucp-form-field");
                strategyField.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Increment Strategy");
                        profile.incrementStrategy = (VersionIncrementStrategy)evt.newValue;
                        EditorUtility.SetDirty(profile);
                        UpdateProfileDetails(); // Refresh to show help
                    }
                });
                strategyRow.Add(strategyField);
                section.Add(strategyRow);
                
                // Show what each strategy does
                var strategyHelp = new Label(GetStrategyExplanation(profile.incrementStrategy));
                strategyHelp.style.fontSize = 11;
                strategyHelp.style.color = new UnityEngine.UIElements.StyleColor(new Color(0.6f, 0.8f, 1.0f));
                strategyHelp.style.marginLeft = 4;
                strategyHelp.style.marginTop = 2;
                strategyHelp.style.marginBottom = 8;
                strategyHelp.style.unityFontStyleAndWeight = FontStyle.Italic;
                section.Add(strategyHelp);
                
                // Custom Version Rule (optional)
                var customRuleRow = CreateFormRow("Custom Rule (Optional)", tooltip: "Use a custom version rule for special formats. Leave empty for standard semver.");
                var customRuleField = new ObjectField { objectType = typeof(CustomVersionRule), value = profile.customVersionRule };
                customRuleField.AddToClassList("yucp-form-field");
                customRuleField.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Custom Rule");
                        profile.customVersionRule = evt.newValue as CustomVersionRule;
                        if (profile.customVersionRule != null)
                        {
                            profile.customVersionRule.RegisterRule();
                        }
                        EditorUtility.SetDirty(profile);
                        UpdateProfileDetails(); // Refresh UI
                    }
                });
                customRuleRow.Add(customRuleField);
                
                var createRuleBtn = new Button(() => CreateCustomRule(profile)) { text = "New" };
                createRuleBtn.AddToClassList("yucp-button");
                createRuleBtn.AddToClassList("yucp-button-small");
                createRuleBtn.tooltip = "Create a new custom version rule";
                customRuleRow.Add(createRuleBtn);
                
                section.Add(customRuleRow);
                
                // Custom Rule Editor (show when a rule is assigned)
                if (profile.customVersionRule != null)
                {
                    section.Add(CreateCustomRuleEditor(profile));
                }
                
                // Bump Directives in Files toggle
                var bumpDirectivesToggle = new Toggle("Auto-bump @directives in Files") { value = profile.bumpDirectivesInFiles };
                bumpDirectivesToggle.AddToClassList("yucp-toggle");
                bumpDirectivesToggle.tooltip = "Automatically update versions in source files that have @bump directives";
                bumpDirectivesToggle.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Bump Directives");
                        profile.bumpDirectivesInFiles = evt.newValue;
                        EditorUtility.SetDirty(profile);
                        UpdateProfileDetails(); // Refresh to show/hide help text
                    }
                });
                section.Add(bumpDirectivesToggle);
                
                // Help text for smart version bumping
                if (profile.bumpDirectivesInFiles)
                {
                    var helpBox = new VisualElement();
                    helpBox.style.backgroundColor = new UnityEngine.UIElements.StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.3f));
                    helpBox.style.borderTopWidth = 1;
                    helpBox.style.borderBottomWidth = 1;
                    helpBox.style.borderLeftWidth = 1;
                    helpBox.style.borderRightWidth = 1;
                    helpBox.style.borderTopColor = new UnityEngine.UIElements.StyleColor(new Color(0.1f, 0.1f, 0.1f));
                    helpBox.style.borderBottomColor = new UnityEngine.UIElements.StyleColor(new Color(0.1f, 0.1f, 0.1f));
                    helpBox.style.borderLeftColor = new UnityEngine.UIElements.StyleColor(new Color(0.1f, 0.1f, 0.1f));
                    helpBox.style.borderRightColor = new UnityEngine.UIElements.StyleColor(new Color(0.1f, 0.1f, 0.1f));
                    helpBox.style.borderTopLeftRadius = 4;
                    helpBox.style.borderTopRightRadius = 4;
                    helpBox.style.borderBottomLeftRadius = 4;
                    helpBox.style.borderBottomRightRadius = 4;
                    helpBox.style.paddingTop = 8;
                    helpBox.style.paddingBottom = 8;
                    helpBox.style.paddingLeft = 8;
                    helpBox.style.paddingRight = 8;
                    helpBox.style.marginTop = 8;
                    helpBox.style.marginBottom = 8;
                    
                    var helpTitle = new Label("How to use:");
                    helpTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                    helpTitle.style.fontSize = 11;
                    helpTitle.style.marginBottom = 4;
                    helpBox.Add(helpTitle);
                    
                    string exampleRule = profile.customVersionRule != null 
                        ? profile.customVersionRule.ruleName 
                        : "semver";
                    
                    var helpText = new Label(
                        $"Add comment directives to your source files:\n" +
                        $"  public const string Version = \"1.0.0\"; // @bump {exampleRule}\n\n" +
                        "When you export, these versions will auto-update according to your Increment Strategy.");
                    helpText.style.fontSize = 11;
                    helpText.style.color = new UnityEngine.UIElements.StyleColor(new Color(0.7f, 0.7f, 0.7f));
                    helpText.style.whiteSpace = UnityEngine.UIElements.WhiteSpace.Normal;
                    helpBox.Add(helpText);
                    
                    section.Add(helpBox);
                }
            }
            
            // Export Path
            var pathRow = CreateFormRow("Export Path", tooltip: "Folder where the exported .unitypackage file will be saved");
            var pathField = new TextField { value = profile.exportPath };
            pathField.AddToClassList("yucp-input");
            pathField.AddToClassList("yucp-form-field");
            pathField.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Export Path");
                    profile.exportPath = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            pathRow.Add(pathField);
            var browsePathButton = new Button(() => BrowseForPath(profile)) { text = "Browse" };
            browsePathButton.AddToClassList("yucp-button");
            browsePathButton.AddToClassList("yucp-button-small");
            pathRow.Add(browsePathButton);
            section.Add(pathRow);
            
            if (string.IsNullOrEmpty(profile.exportPath))
            {
                var hintBox = new VisualElement();
                hintBox.AddToClassList("yucp-help-box");
                var hintText = new Label("Empty path = Desktop");
                hintText.AddToClassList("yucp-help-box-text");
                hintBox.Add(hintText);
                section.Add(hintBox);
            }
            
            return section;
        }

        private VisualElement CreateFoldersSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var header = CreateCollapsibleHeader("Export Folders", 
                () => showFolders, 
                (value) => { showFolders = value; }, 
                () => UpdateProfileDetails());
            section.Add(header);
            
            if (!showFolders)
            {
                return section;
            }
            
            if (profile.foldersToExport.Count == 0)
            {
                var warning = new VisualElement();
                warning.AddToClassList("yucp-validation-error");
                var warningText = new Label("No folders added. Add folders to export.");
                warningText.AddToClassList("yucp-validation-error-text");
                warning.Add(warningText);
                section.Add(warning);
            }
            else
            {
                var folderList = new VisualElement();
                folderList.AddToClassList("yucp-folder-list");
                
                for (int i = 0; i < profile.foldersToExport.Count; i++)
                {
                    int index = i; // Capture for closure
                    var folderItem = new VisualElement();
                    folderItem.AddToClassList("yucp-folder-item");
                    
                    var pathLabel = new Label(profile.foldersToExport[i]);
                    pathLabel.AddToClassList("yucp-folder-item-path");
                    folderItem.Add(pathLabel);
                    
                    var removeButton = new Button(() => RemoveFolder(profile, index)) { text = "×" };
                    removeButton.AddToClassList("yucp-button");
                    removeButton.AddToClassList("yucp-folder-item-remove");
                    folderItem.Add(removeButton);
                    
                    folderList.Add(folderItem);
                }
                
                section.Add(folderList);
            }
            
            var addButton = new Button(() => AddFolder(profile)) { text = "+ Add Folder" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            return section;
        }

        private VisualElement CreateExclusionFiltersSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var header = CreateCollapsibleHeader("Exclusion Filters", 
                () => showExclusionFilters, 
                (value) => { showExclusionFilters = value; }, 
                () => UpdateProfileDetails());
            section.Add(header);
            
            if (!showExclusionFilters)
            {
                return section;
            }
            
            var helpBox = new VisualElement();
            helpBox.AddToClassList("yucp-help-box");
            helpBox.style.marginTop = 8;
            var helpText = new Label("Exclude files and folders from export using patterns");
            helpText.AddToClassList("yucp-help-box-text");
            helpBox.Add(helpText);
            section.Add(helpBox);
            
            // File Patterns
            var filePatternsLabel = new Label("File Patterns");
            filePatternsLabel.AddToClassList("yucp-label");
            filePatternsLabel.style.marginTop = 8;
            filePatternsLabel.style.marginBottom = 4;
            filePatternsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.Add(filePatternsLabel);
            
            var filePatternsContainer = new VisualElement();
            filePatternsContainer.style.marginBottom = 8;
            
            for (int i = 0; i < profile.excludeFilePatterns.Count; i++)
            {
                int index = i;
                string originalValue = profile.excludeFilePatterns[i];
                var patternItem = CreateEditableStringListItem(
                    originalValue,
                    (newValue) =>
                    {
                        if (index < profile.excludeFilePatterns.Count)
                        {
                            Undo.RecordObject(profile, "Change Exclusion Pattern");
                            profile.excludeFilePatterns[index] = newValue;
                            EditorUtility.SetDirty(profile);
                        }
                    },
                    () =>
                    {
                        if (index < profile.excludeFilePatterns.Count)
                        {
                            Undo.RecordObject(profile, "Remove Exclusion Pattern");
                            profile.excludeFilePatterns.RemoveAt(index);
                            EditorUtility.SetDirty(profile);
                            AssetDatabase.SaveAssets();
                            UpdateProfileDetails();
                        }
                    });
                filePatternsContainer.Add(patternItem);
            }
            section.Add(filePatternsContainer);
            
            var addFilePatternButton = new Button(() =>
            {
                profile.excludeFilePatterns.Add("*.tmp");
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }) { text = "+ Add Pattern (e.g., *.tmp)" };
            addFilePatternButton.AddToClassList("yucp-button");
            addFilePatternButton.style.marginBottom = 12;
            section.Add(addFilePatternButton);
            
            // Folder Names
            var folderNamesLabel = new Label("Folder Names");
            folderNamesLabel.AddToClassList("yucp-label");
            folderNamesLabel.style.marginTop = 8;
            folderNamesLabel.style.marginBottom = 4;
            folderNamesLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.Add(folderNamesLabel);
            
            var folderNamesContainer = new VisualElement();
            
            for (int i = 0; i < profile.excludeFolderNames.Count; i++)
            {
                int index = i;
                string originalValue = profile.excludeFolderNames[i];
                var folderItem = CreateEditableStringListItem(
                    originalValue,
                    (newValue) =>
                    {
                        if (index < profile.excludeFolderNames.Count)
                        {
                            Undo.RecordObject(profile, "Change Exclusion Folder");
                            profile.excludeFolderNames[index] = newValue;
                            EditorUtility.SetDirty(profile);
                        }
                    },
                    () =>
                    {
                        if (index < profile.excludeFolderNames.Count)
                        {
                            Undo.RecordObject(profile, "Remove Exclusion Folder");
                            profile.excludeFolderNames.RemoveAt(index);
                            EditorUtility.SetDirty(profile);
                            AssetDatabase.SaveAssets();
                            UpdateProfileDetails();
                        }
                    });
                folderNamesContainer.Add(folderItem);
            }
            section.Add(folderNamesContainer);
            
            var addFolderNameButton = new Button(() =>
            {
                profile.excludeFolderNames.Add(".git");
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }) { text = "+ Add Folder Name (e.g., .git)" };
            addFolderNameButton.AddToClassList("yucp-button");
            section.Add(addFolderNameButton);
            
            return section;
        }

        private VisualElement CreateStringListItem(string value, Action onRemove)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-folder-item");
            
            var textField = new TextField { value = value };
            textField.AddToClassList("yucp-input");
            textField.style.flexGrow = 1;
            textField.isReadOnly = true;
            item.Add(textField);
            
            var removeButton = new Button(onRemove) { text = "×" };
            removeButton.AddToClassList("yucp-button");
            removeButton.AddToClassList("yucp-folder-item-remove");
            item.Add(removeButton);
            
            return item;
        }

        private VisualElement CreateEditableStringListItem(string value, Action<string> onValueChanged, Action onRemove)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-folder-item");
            
            var textField = new TextField { value = value };
            textField.AddToClassList("yucp-input");
            textField.style.flexGrow = 1;
            textField.isReadOnly = false;
            textField.RegisterValueChangedCallback(evt =>
            {
                onValueChanged?.Invoke(evt.newValue);
            });
            item.Add(textField);
            
            var removeButton = new Button(onRemove) { text = "×" };
            removeButton.AddToClassList("yucp-button");
            removeButton.AddToClassList("yucp-folder-item-remove");
            item.Add(removeButton);
            
            return item;
        }

        private VisualElement CreateExportInspectorSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var header = new VisualElement();
            header.AddToClassList("yucp-inspector-header");
            
            var title = new Label($"Export Inspector ({profile.discoveredAssets.Count} assets)");
            title.AddToClassList("yucp-section-title");
            title.style.flexGrow = 1;
            header.Add(title);
            
            var rescanButton = new Button(() => 
            {
                Undo.RecordObject(profile, "Rescan Assets");
                ScanAssetsForInspector(profile, silent: true);
            });
            rescanButton.tooltip = "Rescan Assets";
            rescanButton.AddToClassList("yucp-button");
            rescanButton.AddToClassList("yucp-button-small");
            
            var refreshIcon = EditorGUIUtility.IconContent("Refresh");
            if (refreshIcon != null && refreshIcon.image != null)
            {
                rescanButton.text = "";
                var iconImage = new Image();
                iconImage.image = refreshIcon.image as Texture2D;
                iconImage.style.width = 16;
                iconImage.style.height = 16;
                iconImage.style.alignSelf = Align.Center;
                iconImage.style.marginLeft = Length.Auto();
                iconImage.style.marginRight = Length.Auto();
                iconImage.style.marginTop = Length.Auto();
                iconImage.style.marginBottom = Length.Auto();
                rescanButton.Add(iconImage);
            }
            else
            {
                rescanButton.text = "⟳";
            }
            
            rescanButton.style.justifyContent = Justify.Center;
            rescanButton.style.alignItems = Align.Center;
            rescanButton.style.marginRight = 4;
            rescanButton.SetEnabled(profile.foldersToExport.Count > 0);
            header.Add(rescanButton);
            
            var toggleButton = new Button(() => 
            {
                bool wasOpen = showExportInspector;
                showExportInspector = !showExportInspector;
                UpdateProfileDetails();
                
                // Scan when section is opened
                if (showExportInspector && !wasOpen && profile != null && profile.foldersToExport.Count > 0)
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (profile != null)
                        {
                            ScanAssetsForInspector(profile, silent: true);
                        }
                    };
                }
            }) 
            { text = showExportInspector ? "▼" : "▶" };
            toggleButton.AddToClassList("yucp-button");
            toggleButton.AddToClassList("yucp-button-small");
            header.Add(toggleButton);
            
            section.Add(header);
            
            // Auto-scan when section is visible and needs scanning
            if (showExportInspector && profile != null && profile.foldersToExport.Count > 0)
            {
                if (!profile.HasScannedAssets)
                {
                    // Scan immediately if not scanned yet
                    EditorApplication.delayCall += () =>
                    {
                        if (profile != null)
                        {
                            ScanAssetsForInspector(profile, silent: true);
                        }
                    };
                }
            }
            
            if (showExportInspector)
            {
                // Help box
                var helpBox = new VisualElement();
                helpBox.AddToClassList("yucp-help-box");
                var helpText = new Label("The Export Inspector shows all assets discovered from your export folders. Scan to discover assets, then deselect unwanted items or add folders to the permanent ignore list.");
                helpText.AddToClassList("yucp-help-box-text");
                helpBox.Add(helpText);
                section.Add(helpBox);
                
                // Action buttons
                var actionButtons = new VisualElement();
                actionButtons.AddToClassList("yucp-inspector-action-buttons");
                actionButtons.style.flexDirection = FlexDirection.Row;
                actionButtons.style.marginTop = 8;
                actionButtons.style.marginBottom = 8;
                
                // Show scan in progress message
                if (!profile.HasScannedAssets)
                {
                    var infoBox = new VisualElement();
                    infoBox.AddToClassList("yucp-help-box");
                    var infoText = new Label("Scanning assets... This will complete automatically.");
                    infoText.AddToClassList("yucp-help-box-text");
                    infoBox.Add(infoText);
                    section.Add(infoBox);
                }
                else
                {
                    // Statistics
                    var statsLabel = new Label("Asset Statistics");
                    statsLabel.AddToClassList("yucp-label");
                    statsLabel.style.marginTop = 8;
                    statsLabel.style.marginBottom = 4;
                    section.Add(statsLabel);
                    
					var summaryBox = new VisualElement();
                    summaryBox.AddToClassList("yucp-help-box");
                    var summaryText = new Label(AssetCollector.GetAssetSummary(profile.discoveredAssets));
                    summaryText.AddToClassList("yucp-help-box-text");
                    summaryBox.Add(summaryText);
                    section.Add(summaryBox);
					
					// Derived patch summary
					int derivedCount = profile.discoveredAssets.Count(a => IsDerivedFbx(a.assetPath, out _, out _));
					if (derivedCount > 0)
					{
						var derivedBox = new VisualElement();
						derivedBox.AddToClassList("yucp-help-box");
						var derivedText = new Label($"{derivedCount} FBX asset(s) are marked to export as Derived Patch packages.");
						derivedText.AddToClassList("yucp-help-box-text");
						derivedBox.Add(derivedText);
						section.Add(derivedBox);
					}
                    
                    // Filter controls
                    var filtersLabel = new Label("Filters");
                    filtersLabel.AddToClassList("yucp-label");
                    filtersLabel.style.marginTop = 8;
                    filtersLabel.style.marginBottom = 4;
                    section.Add(filtersLabel);
                    
                    var searchRow = new VisualElement();
                    searchRow.AddToClassList("yucp-inspector-search-row");
                    searchRow.style.flexDirection = FlexDirection.Row;
                    searchRow.style.marginBottom = 4;
                    
                    var searchField = new TextField { value = inspectorSearchFilter };
                    searchField.AddToClassList("yucp-input");
                    searchField.style.flexGrow = 1;
                    searchField.style.marginRight = 4;
                    searchField.name = "inspector-search-field";
                    searchField.RegisterValueChangedCallback(evt =>
                    {
                        inspectorSearchFilter = evt.newValue;
                        // Skip UpdateProfileDetails() - it recreates the entire UI
                        // Instead, find and update the asset list container only
                        var assetListContainer = section.Q<VisualElement>("asset-list-container");
                        if (assetListContainer != null)
                        {
                            assetListContainer.Clear();
                            RebuildAssetList(profile, assetListContainer);
                        }
                    });
                    searchRow.Add(searchField);
                    
                    var clearSearchButton = new Button(() => 
                    {
                        inspectorSearchFilter = "";
                        var searchField = section.Q<TextField>("inspector-search-field");
                        if (searchField != null)
                        {
                            searchField.value = "";
                        }
                        var assetListContainer = section.Q<VisualElement>("asset-list-container");
                        if (assetListContainer != null)
                        {
                            assetListContainer.Clear();
                            RebuildAssetList(profile, assetListContainer);
                        }
                    }) { text = "Clear" };
                    clearSearchButton.AddToClassList("yucp-button");
                    clearSearchButton.AddToClassList("yucp-button-small");
                    searchRow.Add(clearSearchButton);
                    
                    section.Add(searchRow);
                    
                    var filterToggles = new VisualElement();
                    filterToggles.AddToClassList("yucp-inspector-filter-toggles");
                    filterToggles.style.flexDirection = FlexDirection.Row;
                    filterToggles.style.marginBottom = 8;
                    
                    var includedToggle = new Toggle("Show Only Included") { value = showOnlyIncluded };
                    includedToggle.AddToClassList("yucp-toggle");
                    includedToggle.RegisterValueChangedCallback(evt =>
                    {
                        showOnlyIncluded = evt.newValue;
                        if (evt.newValue) showOnlyExcluded = false;
                        var assetListContainer = section.Q<VisualElement>("asset-list-container");
                        if (assetListContainer != null)
                        {
                            assetListContainer.Clear();
                            RebuildAssetList(profile, assetListContainer);
                        }
                    });
                    filterToggles.Add(includedToggle);
                    
                    var excludedToggle = new Toggle("Show Only Excluded") { value = showOnlyExcluded };
                    excludedToggle.AddToClassList("yucp-toggle");
                    excludedToggle.RegisterValueChangedCallback(evt =>
                    {
                        showOnlyExcluded = evt.newValue;
                        if (evt.newValue) showOnlyIncluded = false;
                        var assetListContainer = section.Q<VisualElement>("asset-list-container");
                        if (assetListContainer != null)
                        {
                            assetListContainer.Clear();
                            RebuildAssetList(profile, assetListContainer);
                        }
                    });
                    filterToggles.Add(excludedToggle);
                    
					// Show Only Derived toggle
					var derivedToggle = new Toggle("Show Only Derived") { value = showOnlyDerived };
					derivedToggle.AddToClassList("yucp-toggle");
					derivedToggle.RegisterValueChangedCallback(evt =>
					{
						showOnlyDerived = evt.newValue;
						var assetListContainer = section.Q<VisualElement>("asset-list-container");
						if (assetListContainer != null)
						{
							assetListContainer.Clear();
							RebuildAssetList(profile, assetListContainer);
						}
					});
					filterToggles.Add(derivedToggle);
					
					section.Add(filterToggles);
                    
                    // Asset list header with actions
                    var listHeader = new VisualElement();
                    listHeader.AddToClassList("yucp-inspector-list-header");
                    listHeader.style.flexDirection = FlexDirection.Row;
                    listHeader.style.justifyContent = Justify.SpaceBetween;
                    listHeader.style.marginBottom = 4;
                    
                    var listTitle = new Label("Discovered Assets");
                    listTitle.AddToClassList("yucp-label");
                    listHeader.Add(listTitle);
                    
                    var listActions = new VisualElement();
                    listActions.AddToClassList("yucp-inspector-list-actions");
                    listActions.style.flexDirection = FlexDirection.Row;
                    
                    var includeAllButton = new Button(() => 
                    {
                        Undo.RecordObject(profile, "Include All Assets");
                        foreach (var asset in profile.discoveredAssets)
                            asset.included = true;
                        EditorUtility.SetDirty(profile);
                        AssetDatabase.SaveAssets();
                        UpdateProfileDetails();
                    }) { text = "Include All" };
                    includeAllButton.AddToClassList("yucp-button");
                    includeAllButton.AddToClassList("yucp-button-action");
                    includeAllButton.AddToClassList("yucp-button-small");
                    listActions.Add(includeAllButton);
                    
                    var excludeAllButton = new Button(() => 
                    {
                        Undo.RecordObject(profile, "Exclude All Assets");
                        foreach (var asset in profile.discoveredAssets)
                            asset.included = false;
                        EditorUtility.SetDirty(profile);
                        AssetDatabase.SaveAssets();
                        UpdateProfileDetails();
                    }) { text = "Exclude All" };
                    excludeAllButton.AddToClassList("yucp-button");
                    excludeAllButton.AddToClassList("yucp-button-action");
                    excludeAllButton.AddToClassList("yucp-button-small");
                    listActions.Add(excludeAllButton);
                    
                    listHeader.Add(listActions);
                    section.Add(listHeader);
                    
                    // Filter assets
                    var filteredAssets = profile.discoveredAssets.AsEnumerable();
                    
                    if (!string.IsNullOrWhiteSpace(inspectorSearchFilter))
                    {
                        filteredAssets = filteredAssets.Where(a => 
                            a.assetPath.IndexOf(inspectorSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    
                    if (showOnlyIncluded)
                        filteredAssets = filteredAssets.Where(a => a.included);
                    
                    if (showOnlyExcluded)
                        filteredAssets = filteredAssets.Where(a => !a.included);
                    
                    var filteredList = filteredAssets.ToList();
                    
                    // Asset list container (wraps the scrollview for easy rebuilding)
                    var assetListContainer = new VisualElement();
                    assetListContainer.name = "asset-list-container";
                    RebuildAssetList(profile, assetListContainer);
                    section.Add(assetListContainer);
                    
                    // Permanent ignore list
                    var ignoreLabel = new Label("Permanent Ignore List");
                    ignoreLabel.AddToClassList("yucp-label");
                    ignoreLabel.style.marginTop = 12;
                    ignoreLabel.style.marginBottom = 4;
                    section.Add(ignoreLabel);
                    
                    var ignoreHelpBox = new VisualElement();
                    ignoreHelpBox.AddToClassList("yucp-help-box");
                    var ignoreHelpText = new Label("Folders in this list will be permanently ignored from all exports (like .gitignore).");
                    ignoreHelpText.AddToClassList("yucp-help-box-text");
                    ignoreHelpBox.Add(ignoreHelpText);
                    section.Add(ignoreHelpBox);
                    
                    if (profile.PermanentIgnoreFolders == null || profile.PermanentIgnoreFolders.Count == 0)
                    {
                        var noIgnoresLabel = new Label("No folders in ignore list.");
                        noIgnoresLabel.AddToClassList("yucp-label-secondary");
                        noIgnoresLabel.style.paddingTop = 8;
                        noIgnoresLabel.style.paddingBottom = 8;
                        section.Add(noIgnoresLabel);
                    }
                    else
                    {
                        foreach (var ignoreFolder in profile.PermanentIgnoreFolders.ToList())
                        {
                            var ignoreItem = new VisualElement();
                            ignoreItem.AddToClassList("yucp-folder-item");
                            
                            var ignorePathLabel = new Label(ignoreFolder);
                            ignorePathLabel.AddToClassList("yucp-folder-item-path");
                            ignoreItem.Add(ignorePathLabel);
                            
                            var removeIgnoreButton = new Button(() => RemoveFromIgnoreList(profile, ignoreFolder)) { text = "×" };
                            removeIgnoreButton.AddToClassList("yucp-button");
                            removeIgnoreButton.AddToClassList("yucp-folder-item-remove");
                            removeIgnoreButton.tooltip = "Remove from ignore list";
                            ignoreItem.Add(removeIgnoreButton);
                            
                            section.Add(ignoreItem);
                        }
                    }
                    
                    var addIgnoreButton = new Button(() => 
                    {
                        string selectedFolder = EditorUtility.OpenFolderPanel("Select Folder to Ignore", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(selectedFolder))
                        {
                            string relativePath = GetRelativePath(selectedFolder);
                            AddFolderToIgnoreList(profile, relativePath);
                        }
                    }) { text = "+ Add Folder to Ignore List" };
                    addIgnoreButton.AddToClassList("yucp-button");
                    addIgnoreButton.style.marginTop = 8;
                    section.Add(addIgnoreButton);
                }
            }
            
            return section;
        }

        private void RebuildAssetList(ExportProfile profile, VisualElement container)
        {
            // Filter assets
            var filteredAssets = profile.discoveredAssets.AsEnumerable();
            
            if (!string.IsNullOrWhiteSpace(inspectorSearchFilter))
            {
                filteredAssets = filteredAssets.Where(a => 
                    a.assetPath.IndexOf(inspectorSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            
            if (showOnlyIncluded)
                filteredAssets = filteredAssets.Where(a => a.included);
            
            if (showOnlyExcluded)
                filteredAssets = filteredAssets.Where(a => !a.included);
			
			if (showOnlyDerived)
				filteredAssets = filteredAssets.Where(a => IsDerivedFbx(a.assetPath, out _, out _));
            
            var filteredList = filteredAssets.ToList();
            
            // Asset list scrollview
            var assetListScroll = new ScrollView();
            assetListScroll.AddToClassList("yucp-inspector-list");
            
            if (filteredList.Count == 0)
            {
                var emptyLabel = new Label("No assets match the current filters.");
                emptyLabel.AddToClassList("yucp-label-secondary");
                emptyLabel.style.paddingTop = 20;
                emptyLabel.style.paddingBottom = 20;
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                assetListScroll.Add(emptyLabel);
            }
            else
            {
                // Build hierarchical folder tree
                var rootNode = BuildFolderTree(filteredList.Where(a => !a.isFolder).ToList());
                
                // Render the tree
                RenderFolderTree(rootNode, assetListScroll, profile, 0);
            }
            
            container.Add(assetListScroll);
        }
        
        private FolderTreeNode BuildFolderTree(List<DiscoveredAsset> assets)
        {
            var root = new FolderTreeNode("Assets", "Assets");
            root.IsExpanded = true;
            
            foreach (var asset in assets)
            {
                string folderPath = asset.GetFolderPath();
                if (string.IsNullOrEmpty(folderPath) || folderPath == "Assets")
                {
                    root.Assets.Add(asset);
                    continue;
                }
                
                // Split path into segments
                string[] segments = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments[0] == "Assets")
                {
                    segments = segments.Skip(1).ToArray();
                }
                
                FolderTreeNode current = root;
                string currentPath = "Assets";
                
                foreach (string segment in segments)
                {
                    currentPath = currentPath == "Assets" ? $"Assets/{segment}" : $"{currentPath}/{segment}";
                    
                    var child = current.Children.FirstOrDefault(c => c.Name == segment);
                    if (child == null)
                    {
                        child = new FolderTreeNode(segment, currentPath);
                        // Default to expanded for better navigation, unless user has collapsed it
                        child.IsExpanded = folderExpandedStates.ContainsKey(currentPath) 
                            ? folderExpandedStates[currentPath] 
                            : true;
                        current.Children.Add(child);
                    }
                    else
                    {
                        // Ensure existing nodes respect saved state
                        if (folderExpandedStates.ContainsKey(currentPath))
                        {
                            child.IsExpanded = folderExpandedStates[currentPath];
                        }
                    }
                    
                    current = child;
                }
                
                current.Assets.Add(asset);
            }
            
            // Sort children and assets
            SortFolderTree(root);
            
            return root;
        }
        
        private void SortFolderTree(FolderTreeNode node)
        {
            node.Children = node.Children.OrderBy(c => c.Name).ToList();
            node.Assets = node.Assets.OrderBy(a => a.GetDisplayName()).ToList();
            
            foreach (var child in node.Children)
            {
                SortFolderTree(child);
            }
        }
        
        private void RenderFolderTree(FolderTreeNode node, VisualElement container, ExportProfile profile, int depth)
        {
            // Only render if node has assets or children
            if (node.Assets.Count == 0 && node.Children.Count == 0 && depth > 0)
                return;
            
            if (depth > 0 || (depth == 0 && node.Assets.Count > 0))
            {
                if (depth > 0)
                {
                    var folderHeader = CreateFolderHeader(node, profile, depth);
                    container.Add(folderHeader);
                }
            }
            
            if (node.Assets.Count > 0 && (depth == 0 || node.IsExpanded))
            {
                foreach (var asset in node.Assets)
                {
                    var assetItem = CreateAssetItem(asset, profile, depth == 0 ? 0 : depth + 1);
                    container.Add(assetItem);
                }
            }
            
            if (node.Children.Count > 0 && (depth == 0 || node.IsExpanded))
            {
                foreach (var child in node.Children)
                {
                    RenderFolderTree(child, container, profile, depth + 1);
                }
            }
        }
        
        private VisualElement CreateFolderHeader(FolderTreeNode node, ExportProfile profile, int depth)
        {
            var folderHeader = new VisualElement();
            folderHeader.AddToClassList("yucp-inspector-folder-header");
            folderHeader.style.paddingLeft = 12 + (depth * 24);
            
            var folderHeaderContent = new VisualElement();
            folderHeaderContent.AddToClassList("yucp-inspector-folder-content");
            
            // Expand/collapse button
            var expandButton = new Button(() =>
            {
                node.IsExpanded = !node.IsExpanded;
                folderExpandedStates[node.FullPath] = node.IsExpanded;
                
                VisualElement scrollView = folderHeader.parent;
                if (scrollView != null)
                {
                    VisualElement container = scrollView.parent;
                    if (container != null && container.name == "asset-list-container")
                    {
                        container.Clear();
                        RebuildAssetList(profile, container);
                    }
                }
            });
            expandButton.AddToClassList("yucp-folder-expand-button");
            expandButton.text = node.IsExpanded ? "▼" : "▶";
            folderHeaderContent.Add(expandButton);
            
            var folderLabel = new Label(node.Name);
            folderLabel.AddToClassList("yucp-inspector-folder-label");
            folderLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left click
                {
                    PingAsset(node.FullPath);
                }
            });
            folderHeaderContent.Add(folderLabel);
            
            // Asset count badge
            int totalAssets = CountTotalAssets(node);
            if (totalAssets > 0)
            {
                var countBadge = new Label($"({totalAssets})");
                countBadge.AddToClassList("yucp-folder-count-badge");
                folderHeaderContent.Add(countBadge);
            }
            
            // Actions toolbar
            var folderActions = new VisualElement();
            folderActions.AddToClassList("yucp-inspector-folder-actions");
            
            // .yucpignore Create/Edit button
            // Convert relative path (e.g., "Assets/Folder") to absolute path
            // node.FullPath should be relative like "Assets/Folder/Subfolder"
            string folderFullPath;
            
            // Get project root (parent of Assets folder)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            
            // Normalize node.FullPath - ensure it uses forward slashes
            string pathToProcess = node.FullPath.Replace('\\', '/');
            
            // Extract the relative part starting from "Assets/"
            // This handles cases where the path might already be absolute or have duplicates
            string relativePath = pathToProcess;
            
            // Find the last occurrence of "Assets/" in the path (in case of duplicates)
            int lastAssetsIndex = pathToProcess.LastIndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (lastAssetsIndex >= 0)
            {
                // Extract everything from "Assets/" onwards
                relativePath = pathToProcess.Substring(lastAssetsIndex);
            }
            else
            {
                // No "Assets/" found - check if it starts with "Assets" (without slash)
                int assetsIndex = pathToProcess.LastIndexOf("Assets", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex >= 0)
                {
                    // Extract from "Assets" and ensure it has a slash
                    relativePath = pathToProcess.Substring(assetsIndex);
                    if (!relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = "Assets/" + relativePath.Substring(6).TrimStart('/');
                    }
                }
                else
                {
                    // Doesn't contain "Assets" at all - prepend it
                    relativePath = "Assets/" + pathToProcess.TrimStart('/');
                }
            }
            
            // Final safety check: if relativePath still contains a drive letter, extract just the Assets part
            if (relativePath.Contains(":"))
            {
                int assetsIndex = relativePath.LastIndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex >= 0)
                {
                    relativePath = relativePath.Substring(assetsIndex);
                }
            }
            
            // Combine with project root to get absolute path
            // Use Path.Combine which will handle the case where relativePath is already absolute
            folderFullPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
            
            // Final validation: if the result still contains duplicates, fix it
            string expectedAssetsPath = Path.Combine(projectRoot, "Assets");
            if (folderFullPath.Contains(expectedAssetsPath + Path.DirectorySeparatorChar + expectedAssetsPath))
            {
                // Remove the duplicate
                int dupIndex = folderFullPath.IndexOf(expectedAssetsPath + Path.DirectorySeparatorChar + expectedAssetsPath);
                folderFullPath = folderFullPath.Substring(0, dupIndex + expectedAssetsPath.Length) + 
                               folderFullPath.Substring(dupIndex + expectedAssetsPath.Length + expectedAssetsPath.Length);
            }
            
            bool hasIgnoreFile = YucpIgnoreHandler.HasIgnoreFile(folderFullPath);
            
            if (hasIgnoreFile)
            {
                var editIgnoreButton = new Button(() => OpenYucpIgnoreFile(folderFullPath)) { text = "Edit .yucpignore" };
                editIgnoreButton.AddToClassList("yucp-button");
                editIgnoreButton.AddToClassList("yucp-button-small");
                folderActions.Add(editIgnoreButton);
            }
            
            var ignoreButton = new Button(() => AddFolderToIgnoreList(profile, node.FullPath)) { text = "Add to Ignore" };
            ignoreButton.AddToClassList("yucp-button");
            ignoreButton.AddToClassList("yucp-button-small");
            folderActions.Add(ignoreButton);
            
            folderHeaderContent.Add(folderActions);
            folderHeader.Add(folderHeaderContent);
            
            return folderHeader;
        }
        
        private int CountTotalAssets(FolderTreeNode node)
        {
            int count = node.Assets.Count;
            foreach (var child in node.Children)
            {
                count += CountTotalAssets(child);
            }
            return count;
        }
        
        /// <summary>
        /// Ping an asset in the Project window by its path
        /// </summary>
        private void PingAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;
            
            // Normalize the path - ensure it's relative to Assets
            string relativePath = assetPath;
            if (Path.IsPathRooted(relativePath))
            {
                // Extract relative part if absolute
                int assetsIndex = relativePath.LastIndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex >= 0)
                {
                    relativePath = relativePath.Substring(assetsIndex);
                }
                else
                {
                    // Try to convert absolute path to relative
                    string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    if (relativePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = relativePath.Substring(projectRoot.Length).Replace('\\', '/').TrimStart('/');
                    }
                }
            }
            
            // Ensure it starts with Assets/
            if (!relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                if (relativePath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = "Assets/" + relativePath.Substring(6).TrimStart('/');
                }
                else
                {
                    relativePath = "Assets/" + relativePath.TrimStart('/');
                }
            }
            
            // Load and ping the asset
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }
        
        private VisualElement CreateAssetItem(DiscoveredAsset asset, ExportProfile profile, int depth)
        {
            var assetItem = new VisualElement();
            assetItem.AddToClassList("yucp-asset-item");
            assetItem.style.paddingLeft = 12 + (depth * 24);
            
            // Checkbox
            var checkbox = new Toggle { value = asset.included };
            checkbox.AddToClassList("yucp-asset-item-checkbox");
            checkbox.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(profile, "Toggle Asset Inclusion");
                asset.included = evt.newValue;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            });
            assetItem.Add(checkbox);
            
            // Icon
            var icon = new Label(GetAssetTypeIcon(asset.assetType));
            icon.AddToClassList("yucp-asset-item-icon");
            assetItem.Add(icon);
            
            // Name - make it clickable to navigate to asset
            var nameLabel = new Label(asset.GetDisplayName());
            nameLabel.AddToClassList("yucp-asset-item-name");
            // Compute derived flag once for this row
            DerivedSettings derivedSettings;
            string derivedBasePath;
            bool isDerivedFbx = IsDerivedFbx(asset.assetPath, out derivedSettings, out derivedBasePath);
            if (asset.isDependency)
            {
                nameLabel.text += " [Dep]";
            }
            if (isDerivedFbx)
            {
                nameLabel.text += string.IsNullOrEmpty(derivedBasePath) ? " [Derived: Base Missing]" : " [Derived]";
            }
            
            // Add click handler to navigate to asset
            nameLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left click
                {
                    PingAsset(asset.assetPath);
                }
            });
            assetItem.Add(nameLabel);
            
            // Type
            string typeText = asset.assetType;
            if (isDerivedFbx && typeText.Equals("Model", StringComparison.OrdinalIgnoreCase))
            {
                typeText = "Model (Derived)";
            }
            var typeLabel = new Label(typeText);
            typeLabel.AddToClassList("yucp-asset-item-type");
            assetItem.Add(typeLabel);
            
            // Size
            if (!asset.isFolder && asset.fileSize > 0)
            {
                var sizeLabel = new Label(FormatBytes(asset.fileSize));
                sizeLabel.AddToClassList("yucp-asset-item-size");
                assetItem.Add(sizeLabel);
            }
            
            // Derived patch badge and quick actions for FBX
            if (isDerivedFbx)
            {
                var badge = new Label(string.IsNullOrEmpty(derivedBasePath) ? "[Derived Patch: Base Missing]" : "[Derived Patch]");
                badge.AddToClassList("yucp-label-secondary");
                badge.style.marginLeft = 6;
                badge.style.color = string.IsNullOrEmpty(derivedBasePath) ? new Color(0.95f, 0.75f, 0.2f) : new Color(0.2f, 0.8f, 0.8f);
                assetItem.Add(badge);
                
                var actionsRow = new VisualElement();
                actionsRow.style.flexDirection = FlexDirection.Row;
                actionsRow.style.marginLeft = 6;
                
                var optionsButton = new Button(() =>
                {
                    string relativePath = asset.assetPath;
                    if (Path.IsPathRooted(relativePath))
                    {
                        string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                        if (relativePath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                        {
                            relativePath = relativePath.Substring(projectPath.Length).Replace('\\', '/').TrimStart('/');
                        }
                    }
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }) { text = "Options" };
                optionsButton.AddToClassList("yucp-button");
                optionsButton.AddToClassList("yucp-button-small");
                actionsRow.Add(optionsButton);
                
                var clearButton = new Button(() =>
                {
                    string relativePath = asset.assetPath;
                    if (Path.IsPathRooted(relativePath))
                    {
                        string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                        if (relativePath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                        {
                            relativePath = relativePath.Substring(projectPath.Length).Replace('\\', '/').TrimStart('/');
                        }
                    }
                    var importer = AssetImporter.GetAtPath(relativePath) as ModelImporter;
                    if (importer != null)
                    {
                        try
                        {
                            var s = string.IsNullOrEmpty(importer.userData) ? new DerivedSettings() : JsonUtility.FromJson<DerivedSettings>(importer.userData) ?? new DerivedSettings();
                            s.isDerived = false;
                            importer.userData = JsonUtility.ToJson(s);
                            importer.SaveAndReimport();
                            UpdateProfileDetails();
                        }
                        catch { /* ignore */ }
                    }
                }) { text = "Clear" };
                clearButton.AddToClassList("yucp-button");
                clearButton.AddToClassList("yucp-button-small");
                actionsRow.Add(clearButton);
                
                assetItem.Add(actionsRow);
            }
            
            return assetItem;
        }
		
		[System.Serializable]
		private class DerivedSettings
		{
			public bool isDerived;
			public string baseGuid;
			public string friendlyName;
			public string category;
		}
		
		private bool IsDerivedFbx(string assetPath, out DerivedSettings settings, out string basePath)
		{
			settings = null;
			basePath = null;
			if (string.IsNullOrEmpty(assetPath)) return false;
			if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return false;
			
			// Convert to relative path if needed (for AssetDatabase/AssetImporter APIs)
			string relativePath = assetPath;
			if (Path.IsPathRooted(relativePath))
			{
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				if (relativePath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
				{
					relativePath = relativePath.Substring(projectPath.Length).Replace('\\', '/').TrimStart('/');
				}
			}
			
			// Try reading from importer first
			var importer = AssetImporter.GetAtPath(relativePath) as ModelImporter;
			if (importer != null)
			{
				try
				{
					string userDataJson = importer.userData;
					if (!string.IsNullOrEmpty(userDataJson))
					{
						settings = JsonUtility.FromJson<DerivedSettings>(userDataJson);
						if (settings != null && settings.isDerived)
						{
							basePath = string.IsNullOrEmpty(settings.baseGuid) ? null : AssetDatabase.GUIDToAssetPath(settings.baseGuid);
							return true;
						}
					}
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"[YUCP PackageExporter] Failed to parse importer userData for {assetPath}: {ex.Message}\nuserData was: '{importer.userData}'");
				}
			}
			
			// Fallback: read directly from .meta file
			try
			{
				// Use original assetPath for file system operations (can be absolute or relative)
				string metaPath = assetPath + ".meta";
				// If relative path, convert to absolute for File operations
				if (!Path.IsPathRooted(metaPath))
				{
					string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
					metaPath = Path.GetFullPath(Path.Combine(projectPath, metaPath));
				}
				if (File.Exists(metaPath))
				{
					string metaContent = File.ReadAllText(metaPath);
					// Look for userData in the meta file
					// Unity stores it as: userData: <json string>
					// But it might be on multiple lines or have special formatting
					
					// Try regex to find userData line
					// Match: userData: <value> where value can be quoted JSON or empty
					var userDataMatch = System.Text.RegularExpressions.Regex.Match(metaContent, @"userData:\s*([^\r\n]*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
					if (userDataMatch.Success)
					{
						string userDataJson = userDataMatch.Groups[1].Value.Trim();
						// Skip if empty or matches Unity's default format
						if (string.IsNullOrEmpty(userDataJson) || userDataJson == "assetBundleName:")
						{
							// Not our custom userData, skip
						}
						else
						{
							// Unity stores userData as a quoted string in YAML, so strip surrounding quotes first
							if ((userDataJson.StartsWith("'") && userDataJson.EndsWith("'")) || 
							    (userDataJson.StartsWith("\"") && userDataJson.EndsWith("\"")))
							{
								userDataJson = userDataJson.Substring(1, userDataJson.Length - 2);
							}
							
							// Now check if it's our JSON format
							if (!string.IsNullOrEmpty(userDataJson) && userDataJson.StartsWith("{"))
							{
								try
								{
									settings = JsonUtility.FromJson<DerivedSettings>(userDataJson);
									if (settings != null && settings.isDerived)
									{
										basePath = string.IsNullOrEmpty(settings.baseGuid) ? null : AssetDatabase.GUIDToAssetPath(settings.baseGuid);
										return true;
									}
								}
								catch (Exception jsonEx)
								{
									Debug.LogWarning($"[YUCP PackageExporter] Failed to parse userData JSON from .meta for {Path.GetFileName(assetPath)}: {jsonEx.Message}\nExtracted value: '{userDataJson}'");
								}
							}
						}
					}
					else
					{
						// No userData found - check if the CustomEditor is even saving
						// This is normal if the FBX hasn't been marked as derived
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[YUCP PackageExporter] Failed to read .meta file for {assetPath}: {ex.Message}\n{ex.StackTrace}");
			}
			
			return false;
		}

        private void RebuildDependencyList(ExportProfile profile, VisualElement section, VisualElement container)
        {
            // Filter dependencies using search
            var filteredDependencies = profile.dependencies.AsEnumerable();
            
            if (!string.IsNullOrWhiteSpace(dependenciesSearchFilter))
            {
                filteredDependencies = filteredDependencies.Where(d =>
                    d.packageName.IndexOf(dependenciesSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(d.displayName) && d.displayName.IndexOf(dependenciesSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            
            // Apply toggle filters if they exist
            var enabledFilter = section.Q<Toggle>("enabled-filter");
            var vpmFilter = section.Q<Toggle>("vpm-filter");
            
            if (enabledFilter?.value == true)
            {
                filteredDependencies = filteredDependencies.Where(d => d.enabled);
            }
            
            if (vpmFilter?.value == true)
            {
                filteredDependencies = filteredDependencies.Where(d => d.isVpmDependency);
            }
            
            var filteredList = filteredDependencies.ToList();
            
            // Dependencies list
            if (filteredList.Count == 0 && profile.dependencies.Count > 0)
            {
                var emptyLabel = new Label("No dependencies match the current filter.");
                emptyLabel.AddToClassList("yucp-label-secondary");
                emptyLabel.style.paddingTop = 10;
                emptyLabel.style.paddingBottom = 10;
                container.Add(emptyLabel);
            }
            else if (profile.dependencies.Count == 0)
            {
                var emptyLabel = new Label("No dependencies configured. Add manually or scan.");
                emptyLabel.AddToClassList("yucp-label-secondary");
                emptyLabel.style.paddingTop = 10;
                emptyLabel.style.paddingBottom = 10;
                container.Add(emptyLabel);
            }
            else
            {
                for (int i = 0; i < filteredList.Count; i++)
                {
                    var dep = filteredList[i];
                    var originalIndex = profile.dependencies.IndexOf(dep);
                    var depCard = CreateDependencyCard(dep, originalIndex, profile);
                    container.Add(depCard);
                }
                
                // Select all/none buttons
                var selectButtons = new VisualElement();
                selectButtons.style.flexDirection = FlexDirection.Row;
                selectButtons.style.marginTop = 8;
                selectButtons.style.marginBottom = 8;
                
                var selectAllButton = new Button(() => 
                {
                    foreach (var dep in profile.dependencies)
                    {
                        dep.enabled = true;
                    }
                    EditorUtility.SetDirty(profile);
                    container.Clear();
                    RebuildDependencyList(profile, section, container);
                }) { text = "Select All" };
                selectAllButton.AddToClassList("yucp-button");
                selectAllButton.AddToClassList("yucp-button-action");
                selectAllButton.style.flexGrow = 1;
                selectAllButton.style.marginRight = 4;
                selectButtons.Add(selectAllButton);
                
                var deselectAllButton = new Button(() => 
                {
                    foreach (var dep in profile.dependencies)
                    {
                        dep.enabled = false;
                    }
                    EditorUtility.SetDirty(profile);
                    container.Clear();
                    RebuildDependencyList(profile, section, container);
                }) { text = "Deselect All" };
                deselectAllButton.AddToClassList("yucp-button");
                deselectAllButton.AddToClassList("yucp-button-action");
                deselectAllButton.style.flexGrow = 1;
                deselectAllButton.style.marginLeft = 4;
                selectButtons.Add(deselectAllButton);
                
                container.Add(selectButtons);
            }
        }

        private VisualElement CreateDependenciesSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var header = CreateCollapsibleHeader("Package Dependencies", 
                () => showDependencies, 
                (value) => { showDependencies = value; }, 
                () => UpdateProfileDetails());
            section.Add(header);
            
            if (!showDependencies)
            {
                return section;
            }
            
            var helpBox = new VisualElement();
            helpBox.AddToClassList("yucp-help-box");
            var helpText = new Label("Bundle: Include dependency files directly in the exported package\nDependency: Add to package.json for automatic download when package is installed");
            helpText.AddToClassList("yucp-help-box-text");
            helpBox.Add(helpText);
            section.Add(helpBox);
            
            // Search/Filter for dependencies
            if (profile.dependencies.Count > 0)
            {
                var searchRow = new VisualElement();
                searchRow.style.flexDirection = FlexDirection.Row;
                searchRow.style.marginTop = 8;
                searchRow.style.marginBottom = 8;
                
                var searchField = new TextField { value = dependenciesSearchFilter };
                searchField.AddToClassList("yucp-input");
                searchField.style.flexGrow = 1;
                searchField.style.marginRight = 4;
                searchField.name = "dependencies-search-field";
                searchField.RegisterValueChangedCallback(evt =>
                {
                    dependenciesSearchFilter = evt.newValue;
                    // Update dependency list without rebuilding entire UI
                    var depListContainer = section.Q<VisualElement>("dep-list-container");
                    if (depListContainer != null)
                    {
                        depListContainer.Clear();
                        RebuildDependencyList(profile, section, depListContainer);
                    }
                });
                searchRow.Add(searchField);
                
                var clearSearchButton = new Button(() => 
                {
                    dependenciesSearchFilter = "";
                    var searchField = section.Q<TextField>("dependencies-search-field");
                    if (searchField != null)
                    {
                        searchField.value = "";
                    }
                    var depListContainer = section.Q<VisualElement>("dep-list-container");
                    if (depListContainer != null)
                    {
                        depListContainer.Clear();
                        RebuildDependencyList(profile, section, depListContainer);
                    }
                }) { text = "Clear" };
                clearSearchButton.AddToClassList("yucp-button");
                clearSearchButton.AddToClassList("yucp-button-small");
                searchRow.Add(clearSearchButton);
                
                section.Add(searchRow);
                
                // Filter toggles
                var filterRow = new VisualElement();
                filterRow.style.flexDirection = FlexDirection.Row;
                filterRow.style.marginBottom = 8;
                
                var enabledToggle = new Toggle("Enabled Only") { value = false };
                enabledToggle.name = "enabled-filter";
                enabledToggle.AddToClassList("yucp-toggle");
                enabledToggle.RegisterValueChangedCallback(evt =>
                {
                    var depListContainer = section.Q<VisualElement>("dep-list-container");
                    if (depListContainer != null)
                    {
                        depListContainer.Clear();
                        RebuildDependencyList(profile, section, depListContainer);
                    }
                });
                filterRow.Add(enabledToggle);
                
                var vpmToggle = new Toggle("VPM Only") { value = false };
                vpmToggle.name = "vpm-filter";
                vpmToggle.AddToClassList("yucp-toggle");
                vpmToggle.RegisterValueChangedCallback(evt =>
                {
                    var depListContainer = section.Q<VisualElement>("dep-list-container");
                    if (depListContainer != null)
                    {
                        depListContainer.Clear();
                        RebuildDependencyList(profile, section, depListContainer);
                    }
                });
                filterRow.Add(vpmToggle);
                
                section.Add(filterRow);
            }
            
            // Dependency list container (wraps the list for easy rebuilding)
            var depListContainer = new VisualElement();
            depListContainer.name = "dep-list-container";
            depListContainer.AddToClassList("yucp-dependency-list-container");
            depListContainer.style.width = Length.Percent(100);
            depListContainer.style.maxWidth = Length.Percent(100);
            depListContainer.style.minWidth = 0;
            depListContainer.style.overflow = Overflow.Hidden;
            RebuildDependencyList(profile, section, depListContainer);
            section.Add(depListContainer);
            
            // Action buttons
            var addButton = new Button(() => AddDependency(profile)) { text = "Add dependency manually" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.flexGrow = 1;
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            // Action buttons - row 2 (Auto-Detect)
            if (profile.dependencies.Count > 0 && profile.foldersToExport.Count > 0)
            {
                var autoDetectButton = new Button(() => AutoDetectUsedDependencies(profile)) { text = "Auto-Detect Used" };
                autoDetectButton.AddToClassList("yucp-button");
                autoDetectButton.AddToClassList("yucp-button-action");
                autoDetectButton.style.marginTop = 8;
                section.Add(autoDetectButton);
            }
            else if (profile.foldersToExport.Count == 0)
            {
                var hintBox = new VisualElement();
                hintBox.AddToClassList("yucp-help-box");
                hintBox.style.marginTop = 8;
                var hintText = new Label("Add export folders first, then use 'Auto-Detect Used' to find dependencies");
                hintText.AddToClassList("yucp-help-box-text");
                hintBox.Add(hintText);
                section.Add(hintBox);
            }
            
            return section;
        }

        private VisualElement CreateDependencyCard(PackageDependency dep, int index, ExportProfile profile)
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-dependency-card");
            card.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
            card.style.marginBottom = 8;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = dep.enabled ? new Color(0.21f, 0.75f, 0.73f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
            card.style.width = Length.Percent(100);
            card.style.maxWidth = Length.Percent(100);
            card.style.minWidth = 0;
            
            // Header row
            var headerRow = new VisualElement();
            headerRow.AddToClassList("yucp-dependency-card-header");
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = dep.enabled ? 8 : 0;
            headerRow.style.width = Length.Percent(100);
            headerRow.style.maxWidth = Length.Percent(100);
            headerRow.style.minWidth = 0;
            
            // Enable checkbox
            var enableToggle = new Toggle { value = dep.enabled };
            enableToggle.AddToClassList("yucp-toggle");
            enableToggle.style.marginRight = 8;
            enableToggle.style.flexShrink = 0;
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                dep.enabled = evt.newValue;
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            });
            headerRow.Add(enableToggle);
            
            // Package name label
            string label = dep.isVpmDependency ? "[VPM] " : "";
            label += string.IsNullOrEmpty(dep.displayName) ? dep.packageName : dep.displayName;
            
            var nameLabel = new Label(label);
            nameLabel.AddToClassList("yucp-label");
            nameLabel.AddToClassList("yucp-dependency-card-name");
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.flexGrow = 1;
            nameLabel.style.minWidth = 0;
            headerRow.Add(nameLabel);
            
            // Remove button
            var removeButton = new Button(() => 
            {
                profile.dependencies.RemoveAt(index);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }) { text = "×" };
            removeButton.AddToClassList("yucp-button");
            removeButton.AddToClassList("yucp-button-danger");
            removeButton.AddToClassList("yucp-button-small");
            removeButton.style.width = 25;
            removeButton.style.flexShrink = 0;
            headerRow.Add(removeButton);
            
            card.Add(headerRow);
            
            // Content area - only show if enabled
            if (dep.enabled)
            {
                // Package Name field
                var packageNameRow = CreateFormRow("Package Name", tooltip: "Unique identifier for the package");
                var packageNameField = new TextField { value = dep.packageName };
                packageNameField.AddToClassList("yucp-input");
                packageNameField.AddToClassList("yucp-form-field");
                packageNameField.RegisterValueChangedCallback(evt =>
                {
                    dep.packageName = evt.newValue;
                    EditorUtility.SetDirty(profile);
                });
                packageNameRow.Add(packageNameField);
                card.Add(packageNameRow);
                
                // Version field
                var versionRow = CreateFormRow("Version", tooltip: "Package version");
                var versionField = new TextField { value = dep.packageVersion };
                versionField.AddToClassList("yucp-input");
                versionField.AddToClassList("yucp-form-field");
                versionField.RegisterValueChangedCallback(evt =>
                {
                    dep.packageVersion = evt.newValue;
                    EditorUtility.SetDirty(profile);
                });
                versionRow.Add(versionField);
                card.Add(versionRow);
                
                // Display Name field
                var displayNameRow = CreateFormRow("Display Name", tooltip: "Human-readable name");
                var displayNameField = new TextField { value = dep.displayName };
                displayNameField.AddToClassList("yucp-input");
                displayNameField.AddToClassList("yucp-form-field");
                displayNameField.RegisterValueChangedCallback(evt =>
                {
                    dep.displayName = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    UpdateProfileDetails(); // Refresh to update card title
                });
                displayNameRow.Add(displayNameField);
                card.Add(displayNameRow);
                
                // Export Mode dropdown
                var exportModeRow = CreateFormRow("Export Mode", tooltip: "How this dependency should be handled");
                var exportModeField = new EnumField(dep.exportMode);
                exportModeField.AddToClassList("yucp-form-field");
                exportModeField.RegisterValueChangedCallback(evt =>
                {
                    dep.exportMode = (DependencyExportMode)evt.newValue;
                    EditorUtility.SetDirty(profile);
                });
                exportModeRow.Add(exportModeField);
                card.Add(exportModeRow);
                
                // VPM Package toggle
                var vpmToggle = new Toggle("VPM Package") { value = dep.isVpmDependency };
                vpmToggle.AddToClassList("yucp-toggle");
                vpmToggle.tooltip = "Is this a VRChat Package Manager dependency?";
                vpmToggle.RegisterValueChangedCallback(evt =>
                {
                    dep.isVpmDependency = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    UpdateProfileDetails(); // Refresh to update card title
                });
                card.Add(vpmToggle);
            }
            
            return card;
        }

        private VisualElement CreateObfuscationSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var title = new Label("Assembly Obfuscation");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var enableToggle = new Toggle("Enable Obfuscation") { value = profile.enableObfuscation };
            enableToggle.AddToClassList("yucp-toggle");
            enableToggle.tooltip = "Protect compiled assemblies using ConfuserEx";
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Change Enable Obfuscation");
                    profile.enableObfuscation = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    UpdateProfileDetails(); // Refresh to show/hide obfuscation options
                }
            });
            section.Add(enableToggle);
            
            if (profile.enableObfuscation)
            {
                // Protection Level
                var presetRow = CreateFormRow("Protection Level", tooltip: "Choose how aggressively to obfuscate");
                var presetField = new EnumField(profile.obfuscationPreset);
                presetField.AddToClassList("yucp-form-field");
                presetField.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Obfuscation Preset");
                        profile.obfuscationPreset = (ConfuserExPreset)evt.newValue;
                        EditorUtility.SetDirty(profile);
                    }
                });
                presetRow.Add(presetField);
                section.Add(presetRow);
                
                // Strip Debug Symbols
                var stripToggle = new Toggle("Strip Debug Symbols") { value = profile.stripDebugSymbols };
                stripToggle.AddToClassList("yucp-toggle");
                stripToggle.RegisterValueChangedCallback(evt =>
                {
                    if (profile != null)
                    {
                        Undo.RecordObject(profile, "Change Strip Debug Symbols");
                        profile.stripDebugSymbols = evt.newValue;
                        EditorUtility.SetDirty(profile);
                    }
                });
                section.Add(stripToggle);
                
                // Scan Assemblies button
                var scanButton = new Button(() => ScanAllAssemblies(profile)) { text = "Scan Assemblies" };
                scanButton.AddToClassList("yucp-button");
                scanButton.AddToClassList("yucp-button-action");
                scanButton.style.marginTop = 8;
                section.Add(scanButton);
                
                // Assembly selection list
                if (profile.assembliesToObfuscate.Count > 0)
                {
                    int enabledCount = profile.assembliesToObfuscate.Count(a => a.enabled);
                    var countLabel = new Label($"Found {profile.assembliesToObfuscate.Count} assemblies ({enabledCount} selected)");
                    countLabel.AddToClassList("yucp-label-secondary");
                    countLabel.style.marginTop = 8;
                    section.Add(countLabel);
                    
                    var scrollView = new ScrollView();
                    scrollView.style.maxHeight = 200;
                    scrollView.style.marginTop = 8;
                    
                    for (int i = 0; i < profile.assembliesToObfuscate.Count; i++)
                    {
                        int index = i;
                        var assembly = profile.assembliesToObfuscate[i];
                        
                        var assemblyItem = new VisualElement();
                        assemblyItem.AddToClassList("yucp-folder-item");
                        
                        var checkbox = new Toggle { value = assembly.enabled };
                        checkbox.AddToClassList("yucp-toggle");
                        checkbox.RegisterValueChangedCallback(evt =>
                        {
                            if (profile != null && index < profile.assembliesToObfuscate.Count)
                            {
                                Undo.RecordObject(profile, "Change Assembly Obfuscation");
                                profile.assembliesToObfuscate[index].enabled = evt.newValue;
                                EditorUtility.SetDirty(profile);
                                UpdateProfileDetails();
                            }
                        });
                        assemblyItem.Add(checkbox);
                        
                        var assemblyLabel = new Label(assembly.assemblyName);
                        assemblyLabel.AddToClassList("yucp-folder-item-path");
                        assemblyLabel.style.marginLeft = 8;
                        assemblyItem.Add(assemblyLabel);
                        
                        scrollView.Add(assemblyItem);
                    }
                    
                    section.Add(scrollView);
                }
            }
            
            return section;
        }

        private VisualElement _signingSectionElement;

        private VisualElement CreatePackageSigningSection(ExportProfile profile)
        {
            var signingTab = new YUCP.DevTools.Editor.PackageSigning.UI.PackageSigningTab(profile);
            _signingSectionElement = signingTab.CreateUI();
            return _signingSectionElement;
        }

        /// <summary>
        /// Refresh the signing section when certificate changes
        /// </summary>
        public void RefreshSigningSection()
        {
            // If a profile is selected, refresh the entire details view to ensure signing section updates
            if (selectedProfile != null)
            {
                UpdateProfileDetails();
            }
            // If no profile selected but signing section exists, just refresh it
            else if (_signingSectionElement != null)
            {
                var parent = _signingSectionElement.parent;
                if (parent != null)
                {
                    var index = parent.IndexOf(_signingSectionElement);
                    _signingSectionElement.RemoveFromHierarchy();
                    
                    var newSigningSection = CreatePackageSigningSection(selectedProfile);
                    parent.Insert(index, newSigningSection);
                }
            }
        }
        
        private VisualElement CreateQuickActionsSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var header = CreateCollapsibleHeader("Quick Actions", 
                () => showQuickActions, 
                (value) => { showQuickActions = value; }, 
                () => UpdateProfileDetails());
            section.Add(header);
            
            if (!showQuickActions)
            {
                return section;
            }
            
            var actionsContainer = new VisualElement();
            actionsContainer.style.flexDirection = FlexDirection.Row;
            actionsContainer.style.marginTop = 8;
            
            var inspectorButton = new Button(() => 
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            }) 
            { text = "Open in Inspector" };
            inspectorButton.AddToClassList("yucp-button");
            inspectorButton.AddToClassList("yucp-button-action");
            inspectorButton.style.flexGrow = 1;
            actionsContainer.Add(inspectorButton);
            
            var saveButton = new Button(() => 
            {
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }) 
            { text = "Save Changes" };
            saveButton.AddToClassList("yucp-button");
            saveButton.AddToClassList("yucp-button-action");
            saveButton.style.flexGrow = 1;
            actionsContainer.Add(saveButton);
            
            section.Add(actionsContainer);
            
            return section;
        }

        private VisualElement CreateFormRow(string labelText, string tooltip = "")
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-form-row");
            
            var label = new Label(labelText);
            label.AddToClassList("yucp-form-label");
            if (!string.IsNullOrEmpty(tooltip))
            {
                label.tooltip = tooltip;
            }
            row.Add(label);
            
            return row;
        }

        private VisualElement CreateCollapsibleHeader(string title, Func<bool> getExpanded, Action<bool> setExpanded, Action onToggle)
        {
            var header = new VisualElement();
            header.AddToClassList("yucp-inspector-header");
            
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("yucp-section-title");
            header.Add(titleLabel);
            
            var toggleButton = new Button();
            
            void UpdateButtonText()
            {
                toggleButton.text = getExpanded() ? "▼" : "▶";
            }
            
            toggleButton.clicked += () =>
            {
                bool current = getExpanded();
                setExpanded(!current);
                UpdateButtonText();
                onToggle?.Invoke();
            };
            
            UpdateButtonText();
            toggleButton.AddToClassList("yucp-button");
            toggleButton.AddToClassList("yucp-button-small");
            header.Add(toggleButton);
            
            return header;
        }

        private void UpdateBottomBar()
        {
            // Update multi-select info
            if (selectedProfileIndices.Count > 1)
            {
                _multiSelectInfo.style.display = DisplayStyle.Flex;
                var textLabel = _multiSelectInfo.Q<Label>("multiSelectText");
                if (textLabel != null)
                {
                    textLabel.text = $"{selectedProfileIndices.Count} profiles selected";
                }
            }
            else
            {
                _multiSelectInfo.style.display = DisplayStyle.None;
            }
            
            // Update export selected button
            _exportSelectedButton.SetEnabled(selectedProfileIndices.Count > 0 && !isExporting);
            if (selectedProfileIndices.Count == 1)
            {
                _exportSelectedButton.text = "Export Selected Profile";
            }
            else if (selectedProfileIndices.Count > 1)
            {
                _exportSelectedButton.text = $"Export Selected Profiles ({selectedProfileIndices.Count})";
            }
            else
            {
                _exportSelectedButton.text = "Export Selected";
            }
            
            // Update export all button
            _exportAllButton.SetEnabled(allProfiles.Count > 0 && !isExporting);
        }

        private void UpdateProgress(float progress, string status)
        {
            currentProgress = progress;
            currentStatus = status;
            
            _progressFill.style.width = Length.Percent(progress * 100);
            _progressText.text = $"{(progress * 100):F0}% - {status}";
        }

        // Helper methods
        private string GetProfileDisplayName(ExportProfile profile)
        {
            return string.IsNullOrEmpty(profile.packageName) ? profile.name : profile.packageName;
        }

        private void CheckDelayedRename()
        {
            if (pendingRenameProfile != null && !string.IsNullOrEmpty(pendingRenamePackageName))
            {
                double timeSinceChange = EditorApplication.timeSinceStartup - lastPackageNameChangeTime;
                
                if (timeSinceChange >= RENAME_DELAY_SECONDS)
                {
                    PerformDelayedRename(pendingRenameProfile, pendingRenamePackageName);
                    pendingRenameProfile = null;
                    pendingRenamePackageName = "";
                }
            }
        }

        private void PerformDelayedRename(ExportProfile profile, string newPackageName)
        {
            if (profile == null) return;
            
            string currentPath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(currentPath)) return;
            
            string directory = Path.GetDirectoryName(currentPath);
            string extension = Path.GetExtension(currentPath);
            string newFileName = SanitizeFileName(newPackageName) + extension;
            string newPath = Path.Combine(directory, newFileName).Replace('\\', '/');
            
            if (newPath != currentPath)
            {
                string result = AssetDatabase.MoveAsset(currentPath, newPath);
                if (string.IsNullOrEmpty(result))
                {
                    profile.profileName = profile.packageName;
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssets();
                    
                    LoadProfiles();
                    
                    var updatedProfile = AssetDatabase.LoadAssetAtPath<ExportProfile>(newPath);
                    if (updatedProfile != null)
                    {
                        selectedProfile = updatedProfile;
                        UpdateProfileList();
                        UpdateProfileDetails();
                    }
                }
                else
                {
                    Debug.LogWarning($"[Package Exporter] Failed to rename asset: {result}");
                }
            }
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "NewPackage";
            
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }
            
            return fileName;
        }

        private void BrowseForIcon(ExportProfile profile)
        {
            string iconPath = EditorUtility.OpenFilePanel("Select Package Icon", "", "png,jpg,jpeg");
            if (!string.IsNullOrEmpty(iconPath))
            {
                string projectPath = "Assets/YUCP/ExportProfiles/Icons/";
                if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles/Icons"))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/YUCP"))
                        AssetDatabase.CreateFolder("Assets", "YUCP");
                    if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles"))
                        AssetDatabase.CreateFolder("Assets/YUCP", "ExportProfiles");
                    AssetDatabase.CreateFolder("Assets/YUCP/ExportProfiles", "Icons");
                }
                
                string fileName = Path.GetFileName(iconPath);
                string targetPath = projectPath + fileName;
                
                File.Copy(iconPath, targetPath, true);
                AssetDatabase.ImportAsset(targetPath);
                AssetDatabase.Refresh();
                
                profile.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                
                UpdateProfileDetails();
            }
        }

        private void BrowseForProductLinkIcon(ExportProfile profile, ProductLink link, Image iconImage)
        {
            string iconPath = EditorUtility.OpenFilePanel("Select Custom Icon for Link", "", "png,jpg,jpeg");
            if (!string.IsNullOrEmpty(iconPath))
            {
                string projectPath = "Assets/YUCP/ExportProfiles/LinkIcons/";
                if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles/LinkIcons"))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/YUCP"))
                        AssetDatabase.CreateFolder("Assets", "YUCP");
                    if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles"))
                        AssetDatabase.CreateFolder("Assets/YUCP", "ExportProfiles");
                    AssetDatabase.CreateFolder("Assets/YUCP/ExportProfiles", "LinkIcons");
                }
                
                string fileName = Path.GetFileName(iconPath);
                string targetPath = projectPath + fileName;
                
                File.Copy(iconPath, targetPath, true);
                AssetDatabase.ImportAsset(targetPath);
                AssetDatabase.Refresh();
                
                Undo.RecordObject(profile, "Set Custom Link Icon");
                link.customIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
                iconImage.image = link.customIcon;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
        }

        private void BrowseForPath(ExportProfile profile)
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select Export Folder", "", "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                Undo.RecordObject(profile, "Change Export Path");
                profile.exportPath = selectedPath;
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            }
        }

        private void AddFolder(ExportProfile profile)
        {
            string selectedFolder = EditorUtility.OpenFolderPanel("Select Folder to Export", Application.dataPath, "");
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                string relativePath = GetRelativePath(selectedFolder);
                Undo.RecordObject(profile, "Add Export Folder");
                profile.foldersToExport.Add(relativePath);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }
        }

        private void RemoveFolder(ExportProfile profile, int index)
        {
            if (index >= 0 && index < profile.foldersToExport.Count)
            {
                Undo.RecordObject(profile, "Remove Export Folder");
                profile.foldersToExport.RemoveAt(index);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }
        }

        private string GetRelativePath(string absolutePath)
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            
            if (absolutePath.StartsWith(projectPath))
            {
                string relative = absolutePath.Substring(projectPath.Length);
                if (relative.StartsWith("\\") || relative.StartsWith("/"))
                {
                    relative = relative.Substring(1);
                }
                return relative;
            }
            
            return absolutePath;
        }

        private void AddDependency(ExportProfile profile)
        {
            var newDep = new PackageDependency("com.example.package", "1.0.0", "Example Package", false);
            Undo.RecordObject(profile, "Add Dependency");
            profile.dependencies.Add(newDep);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            UpdateProfileDetails();
        }

        // Profile CRUD operations
        private void LoadProfiles()
        {
            allProfiles.Clear();
            
            string[] guids = AssetDatabase.FindAssets("t:ExportProfile");
            
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<ExportProfile>(path);
                
                if (profile != null)
                {
                    allProfiles.Add(profile);
                }
            }
            
            allProfiles = allProfiles.OrderBy(p => p.packageName).ToList();
            
            // Reselect if we had a selection
            if (selectedProfile != null)
            {
                int index = allProfiles.IndexOf(selectedProfile);
                if (index < 0)
                {
                    selectedProfile = null;
                    selectedProfileIndices.Clear();
                }
            }
        }

        private void RefreshProfiles()
        {
            LoadProfiles();
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

        private void CreateNewProfile()
        {
            string profilesDir = "Assets/YUCP/ExportProfiles";
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
            }
            
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            profile.packageName = "NewPackage";
            profile.profileName = profile.packageName;
            profile.version = "1.0.0";
            
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(profilesDir, "NewExportProfile.asset"));
            
            AssetDatabase.CreateAsset(profile, assetPath);
            AssetDatabase.SaveAssets();
            
            // Update profile count for milestones
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
                    var updateMethod = milestoneTrackerType.GetMethod("UpdateProfileCountFromAssets", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    updateMethod?.Invoke(null, null);
                }
            }
            catch
            {
                // Silently fail if milestone tracker is not available
            }
            
            LoadProfiles();
            
            selectedProfile = profile;
            int index = allProfiles.IndexOf(profile);
            selectedProfileIndices.Clear();
            selectedProfileIndices.Add(index);
            lastClickedProfileIndex = index;
            
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
            
            EditorGUIUtility.PingObject(profile);
        }

        private void CloneProfile(ExportProfile source)
        {
            if (source == null)
                return;
            
            var clone = Instantiate(source);
            clone.name = source.name + " (Clone)";
            
            string profilesDir = "Assets/YUCP/ExportProfiles";
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
            }
            
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(profilesDir, clone.name + ".asset"));
            
            AssetDatabase.CreateAsset(clone, assetPath);
            AssetDatabase.SaveAssets();
            
            LoadProfiles();
            
            selectedProfile = clone;
            int index = allProfiles.IndexOf(clone);
            selectedProfileIndices.Clear();
            selectedProfileIndices.Add(index);
            lastClickedProfileIndex = index;
            
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

        private void DeleteProfile(ExportProfile profile)
        {
            if (profile == null)
                return;
            
            bool confirm = EditorUtility.DisplayDialog(
                "Delete Export Profile",
                $"Are you sure you want to delete the profile '{profile.name}'?\n\nThis cannot be undone.",
                "Delete",
                "Cancel"
            );
            
            if (!confirm)
                return;
            
            string assetPath = AssetDatabase.GetAssetPath(profile);
            
            if (selectedProfile == profile)
            {
                selectedProfile = null;
                selectedProfileIndices.Clear();
            }
            
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.SaveAssets();
            
            // Update profile count for milestones
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
                    var updateMethod = milestoneTrackerType.GetMethod("UpdateProfileCountFromAssets", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    updateMethod?.Invoke(null, null);
                }
            }
            catch
            {
                // Silently fail if milestone tracker is not available
            }
            
            LoadProfiles();
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

        // Export operations
        private void ExportSelectedProfiles()
        {
            if (selectedProfileIndices.Count == 0)
            {
                EditorUtility.DisplayDialog("No Selection", "No profiles are selected.", "OK");
                return;
            }
            
            if (selectedProfileIndices.Count == 1)
            {
                ExportProfile(selectedProfile);
            }
            else
            {
                var selectedProfiles = selectedProfileIndices.OrderBy(i => i).Select(i => allProfiles[i]).ToList();
                ExportAllProfiles(selectedProfiles);
            }
        }

        private void ExportProfile(ExportProfile profile)
        {
            if (profile == null)
                return;
            
            if (!profile.Validate(out string errorMessage))
            {
                EditorUtility.DisplayDialog("Validation Error", errorMessage, "OK");
                return;
            }
            
            string foldersList = profile.foldersToExport.Count > 0 
                ? string.Join("\n", profile.foldersToExport.Take(5)) + (profile.foldersToExport.Count > 5 ? $"\n... and {profile.foldersToExport.Count - 5} more" : "")
                : "None configured";
            
            int bundledDeps = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Bundle);
            int refDeps = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Dependency);
            
            bool confirm = EditorUtility.DisplayDialog(
                "Export Package",
                $"Export package: {profile.packageName} v{profile.version}\n\n" +
                $"Export Folders ({profile.foldersToExport.Count}):\n{foldersList}\n\n" +
                $"Dependencies:\n" +
                $"  Bundled: {bundledDeps}\n" +
                $"  Referenced: {refDeps}\n\n" +
                $"Obfuscation: {(profile.enableObfuscation ? $"Enabled ({profile.obfuscationPreset}, {profile.assembliesToObfuscate.Count(a => a.enabled)} assemblies)" : "Disabled")}\n\n" +
                $"Output: {profile.GetOutputFilePath()}",
                "Export",
                "Cancel"
            );
            
            if (!confirm)
                return;
            
            isExporting = true;
            _progressContainer.style.display = DisplayStyle.Flex;
            UpdateProgress(0f, "Starting export...");
            UpdateBottomBar();
            
            try
            {
                var result = PackageBuilder.ExportPackage(profile, (progress, status) =>
                {
                    UpdateProgress(progress, status);
                });
                
                isExporting = false;
                _progressContainer.style.display = DisplayStyle.None;
                UpdateBottomBar();
                
                if (result.success)
                {
                    bool openFolder = EditorUtility.DisplayDialog(
                        "Export Successful",
                        $"Package exported successfully!\n\n" +
                        $"Package: {profile.packageName} v{profile.version}\n" +
                        $"Output: {result.outputPath}\n" +
                        $"Files: {result.filesExported}\n" +
                        $"Assemblies Obfuscated: {result.assembliesObfuscated}\n" +
                        $"Build Time: {result.buildTimeSeconds:F2}s",
                        "Open Folder",
                        "OK"
                    );
                    
                    if (openFolder)
                    {
                        EditorUtility.RevealInFinder(result.outputPath);
                    }
                    
                    LoadProfiles();
                    UpdateProfileDetails();
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Export Failed",
                        $"Export failed: {result.errorMessage}\n\n" +
                        "Check the console for more details.",
                        "OK"
                    );
                }
            }
            catch (Exception ex)
            {
                isExporting = false;
                _progressContainer.style.display = DisplayStyle.None;
                UpdateBottomBar();
                
                Debug.LogError($"[Package Exporter] Export failed: {ex.Message}");
                EditorUtility.DisplayDialog("Export Failed", $"An error occurred: {ex.Message}", "OK");
            }
        }

        private void ExportAllProfiles(List<ExportProfile> profilesToExport = null)
        {
            var profiles = profilesToExport ?? allProfiles;
            if (profiles.Count == 0)
                return;
            
            var invalidProfiles = new List<string>();
            foreach (var profile in profiles)
            {
                if (!profile.Validate(out string error))
                {
                    invalidProfiles.Add($"{profile.name}: {error}");
                }
            }
            
            if (invalidProfiles.Count > 0)
            {
                string message = "The following profiles have validation errors:\n\n" + string.Join("\n", invalidProfiles);
                EditorUtility.DisplayDialog("Validation Errors", message, "OK");
                return;
            }
            
            bool confirm = EditorUtility.DisplayDialog(
                "Export Profiles",
                $"This will export {profiles.Count} package(s):\n\n" +
                string.Join("\n", profiles.Select(p => $"• {p.packageName} v{p.version}")) +
                "\n\nThis may take several minutes.",
                "Export All",
                "Cancel"
            );
            
            if (!confirm)
                return;
            
            isExporting = true;
            _progressContainer.style.display = DisplayStyle.Flex;
            UpdateBottomBar();
            
            try
            {
                var results = PackageBuilder.ExportMultiple(profiles, (index, total, progress, status) =>
                {
                    float overallProgress = (index + progress) / total;
                    UpdateProgress(overallProgress, $"[{index + 1}/{total}] {status}");
                });
                
                isExporting = false;
                _progressContainer.style.display = DisplayStyle.None;
                UpdateBottomBar();
                
                int successCount = results.Count(r => r.success);
                int failCount = results.Count - successCount;
                
                string summaryMessage = $"Batch export complete!\n\n" +
                                      $"Successful: {successCount}\n" +
                                      $"Failed: {failCount}\n\n";
                
                if (failCount > 0)
                {
                    var failures = results.Where(r => !r.success).ToList();
                    summaryMessage += "Failed profiles:\n" + string.Join("\n", failures.Select(r => $"• {r.errorMessage}"));
                }
                
                EditorUtility.DisplayDialog("Batch Export Complete", summaryMessage, "OK");
                
                LoadProfiles();
                UpdateProfileDetails();
            }
            catch (Exception ex)
            {
                isExporting = false;
                _progressContainer.style.display = DisplayStyle.None;
                UpdateBottomBar();
                
                Debug.LogError($"[Package Exporter] Batch export failed: {ex.Message}");
                EditorUtility.DisplayDialog("Batch Export Failed", $"An error occurred: {ex.Message}", "OK");
            }
        }

        // Scanning operations (simplified implementations)
        private void ScanAssetsForInspector(ExportProfile profile, bool silent = false)
        {
            EditorUtility.DisplayProgressBar("Scanning Assets", "Discovering assets from export folders...", 0f);
            
            try
            {
                profile.discoveredAssets = AssetCollector.ScanExportFolders(profile, profile.includeDependencies);
                profile.MarkScanned();
                EditorUtility.SetDirty(profile);
                
                if (!silent)
                {
                    EditorUtility.DisplayDialog(
                        "Scan Complete",
                        $"Discovered {profile.discoveredAssets.Count} assets.\n\n" +
                        AssetCollector.GetAssetSummary(profile.discoveredAssets),
                        "OK");
                }
                
                UpdateProfileDetails();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Exporter] Asset scan failed: {ex.Message}");
                if (!silent)
                {
                    EditorUtility.DisplayDialog("Scan Failed", $"Failed to scan assets:\n{ex.Message}", "OK");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ScanProfileDependencies(ExportProfile profile, bool silent = false)
        {
            if (!silent)
            {
                EditorUtility.DisplayProgressBar("Scanning Dependencies", "Finding installed packages...", 0.3f);
            }
            
            try
            {
                var foundPackages = DependencyScanner.ScanInstalledPackages();
                
                if (foundPackages.Count == 0)
                {
                    if (!silent)
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog("No Packages Found", 
                            "No installed packages were found in the project.", 
                            "OK");
                    }
                    return;
                }
                
                if (!silent)
                {
                    EditorUtility.DisplayProgressBar("Scanning Dependencies", "Processing packages...", 0.6f);
                }
                
                profile.dependencies.Clear();
                
                var dependencies = DependencyScanner.ConvertToPackageDependencies(foundPackages);
                foreach (var dep in dependencies)
                {
                    profile.dependencies.Add(dep);
                }
                
                if (!silent)
                {
                    EditorUtility.DisplayProgressBar("Scanning Dependencies", "Auto-detecting usage...", 0.8f);
                }
                
                if (profile.foldersToExport.Count > 0)
                {
                    DependencyScanner.AutoDetectUsedDependencies(profile);
                }
                
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                
                if (!silent)
                {
                    int vpmCount = dependencies.Count(d => d.isVpmDependency);
                    int autoEnabled = dependencies.Count(d => d.enabled);
                    
                    string message = $"Found {dependencies.Count} packages:\n\n" +
                                   $"• {vpmCount} VRChat (VPM) packages\n" +
                                   $"• {dependencies.Count - vpmCount} Unity packages\n" +
                                   $"• {autoEnabled} auto-enabled (detected in use)\n\n" +
                                   "Dependencies detected in your export folders have been automatically enabled.";
                    
                    EditorUtility.DisplayDialog("Scan Complete", message, "OK");
                }
                
                UpdateProfileDetails();
            }
            finally
            {
                if (!silent)
                {
                    EditorUtility.ClearProgressBar();
                }
            }
        }

        private void ScanAllAssemblies(ExportProfile profile)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Scanning Assemblies", "Initializing...", 0f);
                
                var foundAssemblies = new List<AssemblyScanner.AssemblyInfo>();
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Scanning {profile.foldersToExport.Count} export folders...", 0.2f);
                var folderAssemblies = AssemblyScanner.ScanFolders(profile.foldersToExport);
                foundAssemblies.AddRange(folderAssemblies);
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Found {folderAssemblies.Count} assemblies in export folders", 0.5f);
                
                int bundledDepsCount = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Bundle);
                
                if (bundledDepsCount > 0)
                {
                    EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Scanning {bundledDepsCount} bundled dependencies...", 0.6f);
                }
                
                var dependencyAssemblies = AssemblyScanner.ScanVpmPackages(profile.dependencies);
                foundAssemblies.AddRange(dependencyAssemblies);
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Found {dependencyAssemblies.Count} assemblies in bundled dependencies", 0.8f);
                
                if (foundAssemblies.Count == 0)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("No Assemblies Found", 
                        "No .asmdef files were found in export folders or enabled dependencies.", 
                        "OK");
                    return;
                }
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", "Processing assembly list...", 0.9f);
                
                profile.assembliesToObfuscate.Clear();
                
                foreach (var assemblyInfo in foundAssemblies)
                {
                    var settings = new AssemblyObfuscationSettings(assemblyInfo.assemblyName, assemblyInfo.asmdefPath);
                    settings.enabled = assemblyInfo.exists;
                    profile.assembliesToObfuscate.Add(settings);
                }
                
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                
                int existingCount = foundAssemblies.Count(a => a.exists);
                EditorUtility.DisplayDialog("Scan Complete", 
                    $"Found {foundAssemblies.Count} assemblies ({existingCount} compiled)\n\nFrom export folders: {folderAssemblies.Count}\nFrom bundled dependencies: {dependencyAssemblies.Count}", 
                    "OK");
                
                UpdateProfileDetails();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // Helper methods for Export Inspector
        private string GetAssetTypeIcon(string assetType)
        {
            return assetType switch
            {
                "Script" => "C#",
                "Prefab" => "P",
                "Material" => "M",
                "Texture" => "T",
                "Scene" => "S",
                "Shader" => "SH",
                "Model" => "3D",
                "Animation" => "A",
                "Animator" => "AC",
                "Assembly" => "DLL",
                "Audio" => "AU",
                "Font" => "F",
                _ => "F"
            };
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }

        private void ClearAssetScan(ExportProfile profile)
        {
            if (EditorUtility.DisplayDialog("Clear Scan", 
                "Clear all discovered assets and rescan later?", "Clear", "Cancel"))
            {
                profile.ClearScan();
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            }
        }

        private void AddFolderToIgnoreList(ExportProfile profile, string folderPath)
        {
            var ignoreFolders = profile.PermanentIgnoreFolders;
            if (!ignoreFolders.Contains(folderPath))
            {
                ignoreFolders.Add(folderPath);
                EditorUtility.SetDirty(profile);
                
                if (EditorUtility.DisplayDialog(
                    "Added to Ignore List",
                    $"Added '{folderPath}' to ignore list.\n\nRescan assets now to apply changes?",
                    "Rescan",
                    "Later"))
                {
                    ScanAssetsForInspector(profile);
                }
                else
                {
                    UpdateProfileDetails();
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Already Ignored", $"'{folderPath}' is already in the ignore list.", "OK");
            }
        }

        private void RemoveFromIgnoreList(ExportProfile profile, string folderPath)
        {
            var ignoreFolders = profile.PermanentIgnoreFolders;
            if (ignoreFolders != null && ignoreFolders.Contains(folderPath))
            {
                ignoreFolders.Remove(folderPath);
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            }
        }

        private void AutoDetectUsedDependencies(ExportProfile profile)
        {
            if (profile.dependencies.Count == 0)
            {
                EditorUtility.DisplayDialog("No Dependencies", 
                    "Scan for installed packages first before auto-detecting.", 
                    "OK");
                return;
            }
            
            EditorUtility.DisplayProgressBar("Auto-Detecting Dependencies", "Scanning assets...", 0.5f);
            
            try
            {
                DependencyScanner.AutoDetectUsedDependencies(profile);
                
                EditorUtility.ClearProgressBar();
                
                int enabledCount = profile.dependencies.Count(d => d.enabled);
                int disabledCount = profile.dependencies.Count - enabledCount;
                
                string message = $"Auto-detection complete!\n\n" +
                               $"• {enabledCount} dependencies enabled (used in export)\n" +
                               $"• {disabledCount} dependencies disabled (not used)\n\n" +
                               "Review the dependency list and adjust as needed.";
                
                EditorUtility.DisplayDialog("Auto-Detection Complete", message, "OK");
                
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                UpdateProfileDetails();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void CreateYucpIgnoreFile(ExportProfile profile, string folderPath)
        {
            if (YucpIgnoreHandler.CreateIgnoreFile(folderPath))
            {
                AssetDatabase.Refresh();
                
                if (EditorUtility.DisplayDialog(
                    "Created .yucpignore",
                    $"Created .yucpignore file in:\n{folderPath}\n\nOpen the file to edit ignore patterns?",
                    "Open",
                    "Later"))
                {
                    OpenYucpIgnoreFile(folderPath);
                }
                
                if (EditorUtility.DisplayDialog(
                    "Rescan Assets",
                    "Rescan assets now to apply the new ignore file?",
                    "Rescan",
                    "Later"))
                {
                    ScanAssetsForInspector(profile);
                }
                else
                {
                    UpdateProfileDetails();
                }
            }
        }

        private void OpenYucpIgnoreFile(string folderPath)
        {
            string ignoreFilePath = YucpIgnoreHandler.GetIgnoreFilePath(folderPath);
            
            if (File.Exists(ignoreFilePath))
            {
                System.Diagnostics.Process.Start(ignoreFilePath);
            }
            else
            {
                EditorUtility.DisplayDialog("File Not Found", $".yucpignore file not found:\n{ignoreFilePath}", "OK");
            }
        }

        private void ScanAndBumpVersionsInProfile(ExportProfile profile)
        {
            if (profile == null || profile.foldersToExport == null || profile.foldersToExport.Count == 0)
            {
                EditorUtility.DisplayDialog("No Folders", 
                    "No folders to scan. Add folders to export first.", 
                    "OK");
                return;
            }
            
            // Preview first
            EditorUtility.DisplayProgressBar("Scanning for Version Directives", "Finding files...", 0.3f);
            
            var previewResults = ProjectVersionScanner.PreviewBumpInProfile(profile);
            
            EditorUtility.ClearProgressBar();
            
            if (previewResults.Count == 0)
            {
                EditorUtility.DisplayDialog("No Directives Found", 
                    "No version bump directives (@bump) found in export folders.\n\n" +
                    "Add directives like:\n" +
                    "  // @bump semver:patch\n" +
                    "  // @bump dotted_tail\n" +
                    "  // @bump wordnum\n\n" +
                    "to your source files.", 
                    "OK");
                return;
            }
            
            // Show preview dialog
            string previewMessage = SmartVersionBumper.GetBumpSummary(previewResults);
            bool confirmed = EditorUtility.DisplayDialog(
                "Version Bump Preview",
                $"Found {previewResults.Count} version(s) to bump:\n\n{previewMessage}\n\nApply these changes?",
                "Apply",
                "Cancel"
            );
            
            if (!confirmed)
                return;
            
            // Apply the bumps
            EditorUtility.DisplayProgressBar("Bumping Versions", "Updating files...", 0.7f);
            
            var results = ProjectVersionScanner.BumpVersionsInProfile(profile, writeBack: true);
            
            EditorUtility.ClearProgressBar();
            
            int successful = results.Count(r => r.Success);
            int failed = results.Count(r => !r.Success);
            
            string resultMessage = $"Version bump complete!\n\n" +
                                 $"Successful: {successful}\n" +
                                 $"Failed: {failed}";
            
            if (failed > 0)
            {
                resultMessage += "\n\nCheck the console for details on failures.";
                foreach (var failure in results.Where(r => !r.Success))
                {
                    Debug.LogWarning($"[YUCP] Version bump failed: {failure}");
                }
            }
            
            EditorUtility.DisplayDialog("Version Bump Complete", resultMessage, "OK");
            
            // Log successful bumps
            
            AssetDatabase.Refresh();
        }

        private void CreateCustomRule(ExportProfile profile)
        {
            string savePath = EditorUtility.SaveFilePanelInProject(
                "Create Custom Version Rule",
                "CustomVersionRule",
                "asset",
                "Create a new custom version rule asset"
            );
            
            if (string.IsNullOrEmpty(savePath))
                return;
            
            var customRule = ScriptableObject.CreateInstance<CustomVersionRule>();
            customRule.ruleName = "my_custom_rule";
            customRule.displayName = "My Custom Rule";
            customRule.description = "Custom version bumping rule";
            customRule.regexPattern = @"\b(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\b";
            customRule.ruleType = CustomVersionRule.RuleType.Semver;
            customRule.exampleInput = "1.0.0";
            customRule.exampleOutput = "1.0.1";
            
            AssetDatabase.CreateAsset(customRule, savePath);
            AssetDatabase.SaveAssets();
            
            Selection.activeObject = customRule;
            EditorGUIUtility.PingObject(customRule);
            
            // Auto-assign to profile
            if (profile != null)
            {
                Undo.RecordObject(profile, "Assign Custom Rule");
                profile.customVersionRule = customRule;
                customRule.RegisterRule();
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            }
            
            EditorUtility.DisplayDialog(
                "Custom Rule Created",
                $"Custom version rule created at:\n{savePath}\n\n" +
                "Edit the asset to configure:\n" +
                "• Rule name\n" +
                "• Regex pattern\n" +
                "• Rule type\n" +
                "• Test with examples",
                "OK"
            );
        }

        private void TestCustomRule(CustomVersionRule rule)
        {
            if (rule == null)
                return;
            
            string result = rule.TestRule();
            
            EditorUtility.DisplayDialog(
                $"Test Rule: {rule.displayName}",
                $"Rule Name: {rule.ruleName}\n" +
                $"Type: {rule.ruleType}\n" +
                $"Pattern: {rule.regexPattern}\n\n" +
                $"Example Input: {rule.exampleInput}\n" +
                $"Expected Output: {rule.exampleOutput}\n" +
                $"Actual Output: {result}\n\n" +
                (result == rule.exampleOutput ? "[OK] Test PASSED" : "[X] Test FAILED"),
                "OK"
            );
        }

        private string GetStrategyExplanation(VersionIncrementStrategy strategy)
        {
            return strategy switch
            {
                VersionIncrementStrategy.Major => "Breaking changes: 1.0.0 → 2.0.0",
                VersionIncrementStrategy.Minor => "New features: 1.0.0 → 1.1.0",
                VersionIncrementStrategy.Patch => "Bug fixes: 1.0.0 → 1.0.1",
                VersionIncrementStrategy.Build => "Build number: 1.0.0.0 → 1.0.0.1",
                _ => ""
            };
        }

        private VisualElement CreateCustomRuleEditor(ExportProfile profile)
        {
            var rule = profile.customVersionRule;
            if (rule == null) return new VisualElement();
            
            var editorContainer = new VisualElement();
            editorContainer.style.backgroundColor = new UnityEngine.UIElements.StyleColor(new Color(0.25f, 0.3f, 0.35f, 0.3f));
            editorContainer.style.borderTopWidth = 1;
            editorContainer.style.borderBottomWidth = 1;
            editorContainer.style.borderLeftWidth = 1;
            editorContainer.style.borderRightWidth = 1;
            editorContainer.style.borderTopColor = new UnityEngine.UIElements.StyleColor(new Color(0.3f, 0.4f, 0.5f));
            editorContainer.style.borderBottomColor = new UnityEngine.UIElements.StyleColor(new Color(0.3f, 0.4f, 0.5f));
            editorContainer.style.borderLeftColor = new UnityEngine.UIElements.StyleColor(new Color(0.3f, 0.4f, 0.5f));
            editorContainer.style.borderRightColor = new UnityEngine.UIElements.StyleColor(new Color(0.3f, 0.4f, 0.5f));
            editorContainer.style.borderTopLeftRadius = 4;
            editorContainer.style.borderTopRightRadius = 4;
            editorContainer.style.borderBottomLeftRadius = 4;
            editorContainer.style.borderBottomRightRadius = 4;
            editorContainer.style.paddingTop = 10;
            editorContainer.style.paddingBottom = 10;
            editorContainer.style.paddingLeft = 10;
            editorContainer.style.paddingRight = 10;
            editorContainer.style.marginTop = 8;
            editorContainer.style.marginBottom = 8;
            
            var title = new Label($"Editing: {rule.displayName}");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 12;
            title.style.marginBottom = 8;
            editorContainer.Add(title);
            
            // Rule Name
            var ruleNameRow = CreateFormRow("Rule Name", tooltip: "Identifier used in @bump directives (lowercase, no spaces)");
            var ruleNameField = new TextField { value = rule.ruleName };
            ruleNameField.AddToClassList("yucp-input");
            ruleNameField.AddToClassList("yucp-form-field");
            ruleNameField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Rule Name");
                rule.ruleName = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            ruleNameRow.Add(ruleNameField);
            editorContainer.Add(ruleNameRow);
            
            // Display Name
            var displayNameRow = CreateFormRow("Display Name", tooltip: "Human-readable name");
            var displayNameField = new TextField { value = rule.displayName };
            displayNameField.AddToClassList("yucp-input");
            displayNameField.AddToClassList("yucp-form-field");
            displayNameField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Display Name");
                rule.displayName = evt.newValue;
                EditorUtility.SetDirty(rule);
                UpdateProfileDetails(); // Refresh title
            });
            displayNameRow.Add(displayNameField);
            editorContainer.Add(displayNameRow);
            
            // Description
            var descLabel = new Label("Description");
            descLabel.AddToClassList("yucp-label");
            descLabel.style.marginTop = 8;
            descLabel.style.marginBottom = 4;
            editorContainer.Add(descLabel);
            
            var descField = new TextField { value = rule.description, multiline = true };
            descField.AddToClassList("yucp-input");
            descField.AddToClassList("yucp-input-multiline");
            descField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Description");
                rule.description = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            editorContainer.Add(descField);
            
            // Regex Pattern
            var patternLabel = new Label("Regex Pattern");
            patternLabel.AddToClassList("yucp-label");
            patternLabel.style.marginTop = 8;
            patternLabel.style.marginBottom = 4;
            editorContainer.Add(patternLabel);
            
            var patternField = new TextField { value = rule.regexPattern, multiline = true };
            patternField.AddToClassList("yucp-input");
            patternField.AddToClassList("yucp-input-multiline");
            patternField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Regex Pattern");
                rule.regexPattern = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            editorContainer.Add(patternField);
            
            // Rule Type
            var ruleTypeRow = CreateFormRow("Rule Type", tooltip: "Base behavior for this rule");
            var ruleTypeField = new EnumField(rule.ruleType);
            ruleTypeField.AddToClassList("yucp-form-field");
            ruleTypeField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Rule Type");
                rule.ruleType = (CustomVersionRule.RuleType)evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            ruleTypeRow.Add(ruleTypeField);
            editorContainer.Add(ruleTypeRow);
            
            // Options row
            var optionsRow = new VisualElement();
            optionsRow.style.flexDirection = FlexDirection.Row;
            optionsRow.style.marginTop = 8;
            
            var supportsPartsToggle = new Toggle("Supports Parts") { value = rule.supportsParts };
            supportsPartsToggle.AddToClassList("yucp-toggle");
            supportsPartsToggle.tooltip = "Whether the rule understands major/minor/patch";
            supportsPartsToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Supports Parts");
                rule.supportsParts = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            optionsRow.Add(supportsPartsToggle);
            
            var preservePaddingToggle = new Toggle("Preserve Padding") { value = rule.preservePadding };
            preservePaddingToggle.AddToClassList("yucp-toggle");
            preservePaddingToggle.tooltip = "Keep zero padding in numbers (007 → 008)";
            preservePaddingToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Preserve Padding");
                rule.preservePadding = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            optionsRow.Add(preservePaddingToggle);
            
            editorContainer.Add(optionsRow);
            
            // Test section
            var testLabel = new Label("Test");
            testLabel.AddToClassList("yucp-label");
            testLabel.style.marginTop = 12;
            testLabel.style.marginBottom = 4;
            editorContainer.Add(testLabel);
            
            var testRow = new VisualElement();
            testRow.style.flexDirection = FlexDirection.Row;
            testRow.style.marginBottom = 4;
            
            var inputLabel = new Label("Input:");
            inputLabel.style.width = 60;
            inputLabel.style.marginRight = 4;
            testRow.Add(inputLabel);
            
            var exampleInputField = new TextField { value = rule.exampleInput };
            exampleInputField.AddToClassList("yucp-input");
            exampleInputField.style.flexGrow = 1;
            exampleInputField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Example Input");
                rule.exampleInput = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            testRow.Add(exampleInputField);
            
            editorContainer.Add(testRow);
            
            var expectedRow = new VisualElement();
            expectedRow.style.flexDirection = FlexDirection.Row;
            expectedRow.style.marginBottom = 8;
            
            var expectedLabel = new Label("Expected:");
            expectedLabel.style.width = 60;
            expectedLabel.style.marginRight = 4;
            expectedRow.Add(expectedLabel);
            
            var exampleOutputField = new TextField { value = rule.exampleOutput };
            exampleOutputField.AddToClassList("yucp-input");
            exampleOutputField.style.flexGrow = 1;
            exampleOutputField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Example Output");
                rule.exampleOutput = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            expectedRow.Add(exampleOutputField);
            
            editorContainer.Add(expectedRow);
            
            // Action buttons
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.FlexEnd;
            buttonRow.style.marginTop = 8;
            
            var testBtn = new Button(() => TestCustomRule(rule)) { text = "Test Rule" };
            testBtn.AddToClassList("yucp-button");
            testBtn.tooltip = "Test the rule with the example input";
            buttonRow.Add(testBtn);
            
            var registerBtn = new Button(() => 
            {
                rule.RegisterRule();
                EditorUtility.DisplayDialog("Rule Registered", $"Rule '{rule.ruleName}' has been registered and is ready to use.", "OK");
            }) { text = "Save & Register" };
            registerBtn.AddToClassList("yucp-button");
            registerBtn.tooltip = "Register this rule so it can be used";
            buttonRow.Add(registerBtn);
            
            var selectBtn = new Button(() => 
            {
                Selection.activeObject = rule;
                EditorGUIUtility.PingObject(rule);
            }) { text = "Select Asset" };
            selectBtn.AddToClassList("yucp-button");
            selectBtn.tooltip = "Select the rule asset in the project";
            buttonRow.Add(selectBtn);
            
            editorContainer.Add(buttonRow);
            
            return editorContainer;
        }
        
        // ============================================================================
        // RESPONSIVE DESIGN METHODS
        // ============================================================================
        
        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateResponsiveLayout(evt.newRect.width);
        }
        
        private void UpdateResponsiveLayout(float width)
        {
            var root = rootVisualElement;
            
            // Remove all responsive classes first
            root.RemoveFromClassList("yucp-window-narrow");
            root.RemoveFromClassList("yucp-window-medium");
            root.RemoveFromClassList("yucp-window-wide");
            
            // Apply appropriate class
            if (width < 700f)
            {
                root.AddToClassList("yucp-window-narrow");
            }
            else if (width < 900f)
            {
                root.AddToClassList("yucp-window-medium");
            }
            else
            {
                root.AddToClassList("yucp-window-wide");
            }
            
            // Close overlay if window is wide enough
            if (width >= 700f && _isOverlayOpen)
            {
                CloseOverlay();
            }
        }
        
        private void ToggleOverlay()
        {
            if (_isOverlayOpen)
            {
                CloseOverlay();
            }
            else
            {
                OpenOverlay();
            }
        }
        
        private void OpenOverlay()
        {
            _isOverlayOpen = true;
            
            // Show backdrop first
            if (_overlayBackdrop != null)
            {
                _overlayBackdrop.style.display = DisplayStyle.Flex;
                _overlayBackdrop.style.visibility = Visibility.Visible;
                _overlayBackdrop.style.position = Position.Absolute;
                _overlayBackdrop.style.left = 0;
                _overlayBackdrop.style.right = 0;
                _overlayBackdrop.style.top = 0;
                _overlayBackdrop.style.bottom = 0;
                _overlayBackdrop.style.opacity = 0;
                _overlayBackdrop.BringToFront();
                
                // Fade in backdrop
                _overlayBackdrop.schedule.Execute(() => 
                {
                    if (_overlayBackdrop != null)
                    {
                        _overlayBackdrop.style.opacity = 1;
                    }
                }).StartingIn(10);
            }
            
            // Show overlay
            if (_leftPaneOverlay != null)
            {
                // Force dimensions and positioning with inline styles
                _leftPaneOverlay.style.display = DisplayStyle.Flex;
                _leftPaneOverlay.style.visibility = Visibility.Visible;
                _leftPaneOverlay.style.position = Position.Absolute;
                _leftPaneOverlay.style.width = 270;
                _leftPaneOverlay.style.top = 0;
                _leftPaneOverlay.style.bottom = 0;
                _leftPaneOverlay.style.left = -270;
                _leftPaneOverlay.style.opacity = 0;
                
                // Ensure it's in front
                _leftPaneOverlay.BringToFront();
                
                // Animate to visible position
                _leftPaneOverlay.schedule.Execute(() => 
                {
                    if (_leftPaneOverlay != null)
                    {
                        _leftPaneOverlay.style.left = 0;
                        _leftPaneOverlay.style.opacity = 1;
                    }
                }).StartingIn(10);
            }
        }
        
        // ============================================================================
        // BULK EDIT METHODS
        // ============================================================================
        
        private VisualElement CreateBulkEditorSection()
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var title = new Label($"Bulk Edit ({selectedProfileIndices.Count} profiles selected)");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpBox = new VisualElement();
            helpBox.AddToClassList("yucp-help-box");
            var helpText = new Label("Changes made here will be applied to all selected profiles. Package names remain unique to each profile.");
            helpText.AddToClassList("yucp-help-box-text");
            helpBox.Add(helpText);
            section.Add(helpBox);
            
            // Get selected profiles
            var selectedProfiles = selectedProfileIndices
                .Select(i => allProfiles[i])
                .Where(p => p != null)
                .ToList();
            
            // Version field
            var versionRow = CreateFormRow("Version", tooltip: "Set version for all selected profiles");
            var versionField = new TextField();
            versionField.AddToClassList("yucp-input");
            versionField.AddToClassList("yucp-form-field");
            
            // Show current version if all are the same, otherwise show placeholder
            var versions = selectedProfiles.Select(p => p.version).Distinct().ToList();
            Label versionPlaceholder = null;
            if (versions.Count == 1)
            {
                versionField.value = versions[0];
            }
            else
            {
                versionField.value = "";
                versionField.style.opacity = 0.7f;
                // Add placeholder label
                versionPlaceholder = new Label("Mixed values - enter new version");
                versionPlaceholder.AddToClassList("yucp-label-secondary");
                versionPlaceholder.style.position = Position.Absolute;
                versionPlaceholder.style.left = 8;
                versionPlaceholder.style.top = 4;
                versionPlaceholder.pickingMode = PickingMode.Ignore;
                versionRow.style.position = Position.Relative;
                versionRow.Add(versionPlaceholder);
            }
            
            versionField.RegisterValueChangedCallback(evt =>
            {
                if (versionPlaceholder != null)
                {
                    if (string.IsNullOrEmpty(evt.newValue))
                    {
                        versionPlaceholder.style.display = DisplayStyle.Flex;
                        versionField.style.opacity = 0.7f;
                    }
                    else
                    {
                        versionPlaceholder.style.display = DisplayStyle.None;
                        versionField.style.opacity = 1f;
                    }
                }
                
                // Apply changes to all selected profiles
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    ApplyToAllSelected(profile => 
                    {
                        Undo.RecordObject(profile, "Bulk Change Version");
                        profile.version = evt.newValue;
                        EditorUtility.SetDirty(profile);
                    });
                    // Refresh to show updated value
                    UpdateProfileDetails();
                }
            });
            versionRow.Add(versionField);
            section.Add(versionRow);
            
            // Author field
            var authorRow = CreateFormRow("Author", tooltip: "Set author for all selected profiles");
            var authorField = new TextField();
            authorField.AddToClassList("yucp-input");
            authorField.AddToClassList("yucp-form-field");
            
            var authors = selectedProfiles.Select(p => p.author ?? "").Distinct().ToList();
            Label authorPlaceholder = null;
            if (authors.Count == 1)
            {
                authorField.value = authors[0];
            }
            else
            {
                authorField.value = "";
                authorField.style.opacity = 0.7f;
                authorPlaceholder = new Label("Mixed values - enter new author");
                authorPlaceholder.AddToClassList("yucp-label-secondary");
                authorPlaceholder.style.position = Position.Absolute;
                authorPlaceholder.style.left = 8;
                authorPlaceholder.style.top = 4;
                authorPlaceholder.pickingMode = PickingMode.Ignore;
                authorRow.style.position = Position.Relative;
                authorRow.Add(authorPlaceholder);
            }
            
            authorField.RegisterValueChangedCallback(evt =>
            {
                if (authorPlaceholder != null)
                {
                    if (string.IsNullOrEmpty(evt.newValue))
                    {
                        authorPlaceholder.style.display = DisplayStyle.Flex;
                        authorField.style.opacity = 0.7f;
                    }
                    else
                    {
                        authorPlaceholder.style.display = DisplayStyle.None;
                        authorField.style.opacity = 1f;
                    }
                }
                
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    ApplyToAllSelected(profile => 
                    {
                        Undo.RecordObject(profile, "Bulk Change Author");
                        profile.author = evt.newValue;
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                }
            });
            authorRow.Add(authorField);
            section.Add(authorRow);
            
            // Description field
            var descriptionRow = CreateFormRow("Description", tooltip: "Set description for all selected profiles");
            var descriptionField = new TextField();
            descriptionField.AddToClassList("yucp-input");
            descriptionField.AddToClassList("yucp-form-field");
            descriptionField.multiline = true;
            descriptionField.style.height = 60;
            
            var descriptions = selectedProfiles.Select(p => p.description ?? "").Distinct().ToList();
            Label descriptionPlaceholder = null;
            if (descriptions.Count == 1)
            {
                descriptionField.value = descriptions[0];
            }
            else
            {
                descriptionField.value = "";
                descriptionField.style.opacity = 0.7f;
                descriptionPlaceholder = new Label("Mixed values - enter new description");
                descriptionPlaceholder.AddToClassList("yucp-label-secondary");
                descriptionPlaceholder.style.position = Position.Absolute;
                descriptionPlaceholder.style.left = 8;
                descriptionPlaceholder.style.top = 4;
                descriptionPlaceholder.pickingMode = PickingMode.Ignore;
                descriptionRow.style.position = Position.Relative;
                descriptionRow.Add(descriptionPlaceholder);
            }
            
            descriptionField.RegisterValueChangedCallback(evt =>
            {
                if (descriptionPlaceholder != null)
                {
                    if (string.IsNullOrEmpty(evt.newValue))
                    {
                        descriptionPlaceholder.style.display = DisplayStyle.Flex;
                        descriptionField.style.opacity = 0.7f;
                    }
                    else
                    {
                        descriptionPlaceholder.style.display = DisplayStyle.None;
                        descriptionField.style.opacity = 1f;
                    }
                }
                
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    ApplyToAllSelected(profile => 
                    {
                        Undo.RecordObject(profile, "Bulk Change Description");
                        profile.description = evt.newValue;
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                }
            });
            descriptionRow.Add(descriptionField);
            section.Add(descriptionRow);
            
            // Icon field
            var iconRow = CreateFormRow("Icon", tooltip: "Set icon for all selected profiles");
            var icons = selectedProfiles.Select(p => p.icon).Distinct().ToList();
            var iconField = new ObjectField();
            iconField.objectType = typeof(Texture2D);
            iconField.AddToClassList("yucp-form-field");
            if (icons.Count == 1)
            {
                iconField.value = icons[0];
            }
            else
            {
                iconField.value = null;
            }
            iconField.RegisterValueChangedCallback(evt =>
            {
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Change Icon");
                    profile.icon = evt.newValue as Texture2D;
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            });
            iconRow.Add(iconField);
            section.Add(iconRow);
            
            // Export Path
            var pathRow = CreateFormRow("Export Path", tooltip: "Set export path for all selected profiles");
            var pathField = new TextField();
            pathField.AddToClassList("yucp-input");
            pathField.AddToClassList("yucp-form-field");
            
            var paths = selectedProfiles.Select(p => p.exportPath ?? "").Distinct().ToList();
            Label pathPlaceholder = null;
            if (paths.Count == 1)
            {
                pathField.value = paths[0];
            }
            else
            {
                pathField.value = "";
                pathField.style.opacity = 0.7f;
                // Add placeholder label
                pathPlaceholder = new Label("Mixed values - use Browse to set");
                pathPlaceholder.AddToClassList("yucp-label-secondary");
                pathPlaceholder.style.position = Position.Absolute;
                pathPlaceholder.style.left = 8;
                pathPlaceholder.style.top = 4;
                pathPlaceholder.pickingMode = PickingMode.Ignore;
                pathField.RegisterValueChangedCallback(evt => 
                {
                    if (string.IsNullOrEmpty(evt.newValue) && pathPlaceholder != null)
                    {
                        pathPlaceholder.style.display = DisplayStyle.Flex;
                        pathField.style.opacity = 0.7f;
                    }
                    else
                    {
                        if (pathPlaceholder != null)
                            pathPlaceholder.style.display = DisplayStyle.None;
                        pathField.style.opacity = 1f;
                    }
                });
                pathRow.style.position = Position.Relative;
                pathRow.Add(pathPlaceholder);
            }
            
            var browseButton = new Button(() => 
            {
                string currentPath = pathField.value;
                if (string.IsNullOrEmpty(currentPath))
                {
                    currentPath = "";
                }
                string newPath = EditorUtility.OpenFolderPanel("Select Export Path", currentPath, "");
                if (!string.IsNullOrEmpty(newPath))
                {
                    pathField.value = newPath;
                    // Hide placeholder if it exists
                    if (pathPlaceholder != null)
                    {
                        pathPlaceholder.style.display = DisplayStyle.None;
                        pathField.style.opacity = 1f;
                    }
                    ApplyToAllSelected(profile => 
                    {
                        Undo.RecordObject(profile, "Bulk Change Export Path");
                        profile.exportPath = newPath;
                        EditorUtility.SetDirty(profile);
                    });
                    // Refresh to show updated value
                    UpdateProfileDetails();
                }
            }) { text = "Browse" };
            browseButton.AddToClassList("yucp-button");
            browseButton.AddToClassList("yucp-button-action");
            pathRow.Add(pathField);
            pathRow.Add(browseButton);
            section.Add(pathRow);
            
            // Profile Save Location
            var profileSaveRow = CreateFormRow("Profile Save Location", tooltip: "Custom location to save profiles");
            var profileSaveField = new TextField();
            profileSaveField.AddToClassList("yucp-input");
            profileSaveField.AddToClassList("yucp-form-field");
            
            var saveLocs = selectedProfiles.Select(p => p.profileSaveLocation ?? "").Distinct().ToList();
            Label savePlaceholder = null;
            if (saveLocs.Count == 1)
            {
                profileSaveField.value = saveLocs[0];
            }
            else
            {
                profileSaveField.value = "";
                profileSaveField.style.opacity = 0.7f;
                savePlaceholder = new Label("Mixed values - use Browse to set");
                savePlaceholder.AddToClassList("yucp-label-secondary");
                savePlaceholder.style.position = Position.Absolute;
                savePlaceholder.style.left = 8;
                savePlaceholder.style.top = 4;
                savePlaceholder.pickingMode = PickingMode.Ignore;
                profileSaveField.RegisterValueChangedCallback(evt => 
                {
                    if (string.IsNullOrEmpty(evt.newValue) && savePlaceholder != null)
                    {
                        savePlaceholder.style.display = DisplayStyle.Flex;
                        profileSaveField.style.opacity = 0.7f;
                    }
                    else
                    {
                        if (savePlaceholder != null)
                            savePlaceholder.style.display = DisplayStyle.None;
                        profileSaveField.style.opacity = 1f;
                    }
                });
                profileSaveRow.style.position = Position.Relative;
                profileSaveRow.Add(savePlaceholder);
            }
            
            var browseSaveButton = new Button(() => 
            {
                string currentPath = profileSaveField.value;
                if (string.IsNullOrEmpty(currentPath))
                {
                    currentPath = "";
                }
                string newPath = EditorUtility.OpenFolderPanel("Select Profile Save Location", currentPath, "");
                if (!string.IsNullOrEmpty(newPath))
                {
                    profileSaveField.value = newPath;
                    if (savePlaceholder != null)
                    {
                        savePlaceholder.style.display = DisplayStyle.None;
                        profileSaveField.style.opacity = 1f;
                    }
                    ApplyToAllSelected(profile => 
                    {
                        Undo.RecordObject(profile, "Bulk Change Profile Save Location");
                        profile.profileSaveLocation = newPath;
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                }
            }) { text = "Browse" };
            browseSaveButton.AddToClassList("yucp-button");
            browseSaveButton.AddToClassList("yucp-button-action");
            profileSaveRow.Add(profileSaveField);
            profileSaveRow.Add(browseSaveButton);
            section.Add(profileSaveRow);
            
            // Export Options Toggles
            var optionsTitle = new Label("Export Options");
            optionsTitle.AddToClassList("yucp-section-title");
            optionsTitle.style.marginTop = 16;
            optionsTitle.style.marginBottom = 8;
            section.Add(optionsTitle);
            
            // Get common values for toggles
            bool allIncludeDeps = selectedProfiles.All(p => p.includeDependencies);
            bool allRecurse = selectedProfiles.All(p => p.recurseFolders);
            bool allGenerateJson = selectedProfiles.All(p => p.generatePackageJson);
            
            // Include Dependencies
            var includeDepsToggle = CreateBulkToggle("Include Dependencies", allIncludeDeps, 
                profile => profile.includeDependencies,
                (profile, value) => profile.includeDependencies = value);
            section.Add(includeDepsToggle);
            
            // Recurse Folders
            var recurseToggle = CreateBulkToggle("Recurse Folders", allRecurse,
                profile => profile.recurseFolders,
                (profile, value) => profile.recurseFolders = value);
            section.Add(recurseToggle);
            
            // Generate Package JSON
            var generateJsonToggle = CreateBulkToggle("Generate package.json", allGenerateJson,
                profile => profile.generatePackageJson,
                (profile, value) => profile.generatePackageJson = value);
            section.Add(generateJsonToggle);
            
            // Version Management Section
            var versionMgmtTitle = new Label("Version Management");
            versionMgmtTitle.AddToClassList("yucp-section-title");
            versionMgmtTitle.style.marginTop = 16;
            versionMgmtTitle.style.marginBottom = 8;
            section.Add(versionMgmtTitle);
            
            bool allAutoIncrement = selectedProfiles.All(p => p.autoIncrementVersion);
            bool allBumpDirectives = selectedProfiles.All(p => p.bumpDirectivesInFiles);
            
            var autoIncrementToggle = CreateBulkToggle("Auto-Increment Version", allAutoIncrement,
                profile => profile.autoIncrementVersion,
                (profile, value) => profile.autoIncrementVersion = value);
            section.Add(autoIncrementToggle);
            
            // Increment Strategy (only if all have same value)
            var strategies = selectedProfiles.Select(p => p.incrementStrategy).Distinct().ToList();
            if (strategies.Count == 1)
            {
                var strategyRow = CreateFormRow("Increment Strategy");
                var strategyField = new EnumField(strategies[0]);
                strategyField.AddToClassList("yucp-form-field");
                strategyField.RegisterValueChangedCallback(evt =>
                {
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Change Increment Strategy");
                        profile.incrementStrategy = (VersionIncrementStrategy)evt.newValue;
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                });
                strategyRow.Add(strategyField);
                section.Add(strategyRow);
            }
            
            var bumpDirectivesToggle = CreateBulkToggle("Bump @bump Directives in Files", allBumpDirectives,
                profile => profile.bumpDirectivesInFiles,
                (profile, value) => profile.bumpDirectivesInFiles = value);
            section.Add(bumpDirectivesToggle);
            
            // Custom Version Rule
            var customRules = selectedProfiles.Select(p => p.customVersionRule).Distinct().ToList();
            var ruleRow = CreateFormRow("Custom Version Rule");
            var ruleField = new ObjectField();
            ruleField.objectType = typeof(CustomVersionRule);
            ruleField.AddToClassList("yucp-form-field");
            if (customRules.Count == 1)
            {
                ruleField.value = customRules[0];
            }
            else
            {
                ruleField.value = null;
                // Add mixed label
                var mixedLabel = new Label(" (Mixed values)");
                mixedLabel.AddToClassList("yucp-label-secondary");
                mixedLabel.style.marginLeft = 4;
                ruleRow.Add(mixedLabel);
            }
            ruleField.RegisterValueChangedCallback(evt =>
            {
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Change Custom Version Rule");
                    profile.customVersionRule = evt.newValue as CustomVersionRule;
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            });
            ruleRow.Add(ruleField);
            section.Add(ruleRow);
            
            // Add Folders Section
            var foldersSection = CreateBulkFoldersSection(selectedProfiles);
            section.Add(foldersSection);
            
            // Add Dependencies Section
            var dependenciesSection = CreateBulkDependenciesSection(selectedProfiles);
            section.Add(dependenciesSection);
            
            // Add Exclusion Filters Section
            var exclusionSection = CreateBulkExclusionFiltersSection(selectedProfiles);
            section.Add(exclusionSection);
            
            // Add Permanent Ignore Folders Section
            var ignoreSection = CreateBulkPermanentIgnoreFoldersSection(selectedProfiles);
            section.Add(ignoreSection);
            
            // Add Obfuscation Section
            var obfuscationSection = CreateBulkObfuscationSection(selectedProfiles);
            section.Add(obfuscationSection);
            
            // Add Assembly Obfuscation Section
            var assemblySection = CreateBulkAssemblyObfuscationSection(selectedProfiles);
            section.Add(assemblySection);
            
            return section;
        }
        
        private VisualElement CreateBulkPermanentIgnoreFoldersSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Permanent Ignore Folders");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpText = new Label("Folders permanently excluded from all exports");
            helpText.AddToClassList("yucp-label-secondary");
            helpText.style.marginBottom = 8;
            section.Add(helpText);
            
            var allIgnoreFolders = selectedProfiles
                .SelectMany(p => p.PermanentIgnoreFolders ?? new List<string>())
                .Distinct()
                .OrderBy(f => f)
                .ToList();
            
            var folderList = new VisualElement();
            folderList.style.maxHeight = 150;
            var scrollView = new ScrollView();
            
            foreach (var folder in allIgnoreFolders)
            {
                bool allHaveFolder = selectedProfiles.All(p => p.PermanentIgnoreFolders != null && p.PermanentIgnoreFolders.Contains(folder));
                bool someHaveFolder = selectedProfiles.Any(p => p.PermanentIgnoreFolders != null && p.PermanentIgnoreFolders.Contains(folder));
                
                var folderItem = CreateBulkStringListItem(folder, allHaveFolder, someHaveFolder,
                    (profile, value) =>
                    {
                        var ignoreFolders = profile.PermanentIgnoreFolders;
                        
                        if (value)
                        {
                            if (!ignoreFolders.Contains(folder))
                                ignoreFolders.Add(folder);
                        }
                        else
                        {
                            ignoreFolders.Remove(folder);
                        }
                    });
                scrollView.Add(folderItem);
            }
            
            folderList.Add(scrollView);
            section.Add(folderList);
            
            var addButton = new Button(() =>
            {
                string folderPath = EditorUtility.OpenFolderPanel("Select Folder to Ignore", Application.dataPath, "");
                if (!string.IsNullOrEmpty(folderPath))
                {
                    string relativePath = GetRelativePath(folderPath);
                    if (string.IsNullOrEmpty(relativePath))
                    {
                        EditorUtility.DisplayDialog("Invalid Path", "Please select a folder within the Unity project.", "OK");
                        return;
                    }
                    
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Add Ignore Folder");
                        var ignoreFolders = profile.PermanentIgnoreFolders;
                        if (!ignoreFolders.Contains(relativePath))
                        {
                            ignoreFolders.Add(relativePath);
                        }
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                }
            }) { text = "+ Add Ignore Folder to All" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            return section;
        }
        
        private VisualElement CreateBulkAssemblyObfuscationSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Assembly Obfuscation Settings");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpText = new Label("Configure which assemblies to obfuscate");
            helpText.AddToClassList("yucp-label-secondary");
            helpText.style.marginBottom = 8;
            section.Add(helpText);
            
            var allAssemblies = selectedProfiles
                .SelectMany(p => p.assembliesToObfuscate ?? new List<AssemblyObfuscationSettings>())
                .GroupBy(a => a.assemblyName)
                .Select(g => g.First())
                .OrderBy(a => a.assemblyName)
                .ToList();
            
            var assemblyList = new VisualElement();
            assemblyList.style.maxHeight = 200;
            var scrollView = new ScrollView();
            
            foreach (var assembly in allAssemblies)
            {
                var assemblyItem = new VisualElement();
                assemblyItem.AddToClassList("yucp-folder-item");
                
                bool allHaveAssembly = selectedProfiles.All(p => p.assembliesToObfuscate.Any(a => a.assemblyName == assembly.assemblyName));
                bool someHaveAssembly = selectedProfiles.Any(p => p.assembliesToObfuscate.Any(a => a.assemblyName == assembly.assemblyName));
                
                var checkbox = new Toggle();
                checkbox.value = allHaveAssembly;
                checkbox.AddToClassList("yucp-toggle");
                checkbox.RegisterValueChangedCallback(evt =>
                {
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Change Assembly Obfuscation");
                        var existingAssembly = profile.assembliesToObfuscate.FirstOrDefault(a => a.assemblyName == assembly.assemblyName);
                        if (evt.newValue)
                        {
                            if (existingAssembly == null)
                            {
                                var newAssembly = new AssemblyObfuscationSettings
                                {
                                    assemblyName = assembly.assemblyName,
                                    enabled = assembly.enabled,
                                    asmdefPath = assembly.asmdefPath
                                };
                                profile.assembliesToObfuscate.Add(newAssembly);
                            }
                        }
                        else
                        {
                            if (existingAssembly != null)
                            {
                                profile.assembliesToObfuscate.Remove(existingAssembly);
                            }
                        }
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                });
                assemblyItem.Add(checkbox);
                
                var assemblyLabel = new Label($"{assembly.assemblyName} ({(assembly.enabled ? "Enabled" : "Disabled")})");
                assemblyLabel.AddToClassList("yucp-folder-item-path");
                if (!allHaveAssembly && someHaveAssembly)
                {
                    assemblyLabel.style.opacity = 0.7f;
                    var mixedLabel = new Label(" (Mixed)");
                    mixedLabel.AddToClassList("yucp-label-secondary");
                    mixedLabel.style.marginLeft = 4;
                    assemblyItem.Add(mixedLabel);
                }
                assemblyItem.Add(assemblyLabel);
                
                scrollView.Add(assemblyItem);
            }
            
            assemblyList.Add(scrollView);
            section.Add(assemblyList);
            
            var addButton = new Button(() =>
            {
                var newAssembly = new AssemblyObfuscationSettings
                {
                    assemblyName = "Assembly-CSharp",
                    enabled = true,
                    asmdefPath = ""
                };
                
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Add Assembly");
                    if (!profile.assembliesToObfuscate.Any(a => a.assemblyName == newAssembly.assemblyName))
                    {
                        var clonedAssembly = new AssemblyObfuscationSettings
                        {
                            assemblyName = newAssembly.assemblyName,
                            enabled = newAssembly.enabled,
                            asmdefPath = newAssembly.asmdefPath
                        };
                        profile.assembliesToObfuscate.Add(clonedAssembly);
                    }
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            }) { text = "+ Add Assembly to All" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            return section;
        }
        
        private VisualElement CreateBulkFoldersSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Export Folders");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpText = new Label("Add or remove folders to/from all selected profiles");
            helpText.AddToClassList("yucp-label-secondary");
            helpText.style.marginBottom = 8;
            section.Add(helpText);
            
            // Get unique folders across all profiles
            var allFolders = selectedProfiles
                .SelectMany(p => p.foldersToExport ?? new List<string>())
                .Distinct()
                .OrderBy(f => f)
                .ToList();
            
            var folderList = new VisualElement();
            folderList.AddToClassList("yucp-folder-list");
            folderList.style.maxHeight = 200;
            folderList.style.overflow = Overflow.Hidden;
            
            var scrollView = new ScrollView();
            
            foreach (var folder in allFolders)
            {
                var folderItem = new VisualElement();
                folderItem.AddToClassList("yucp-folder-item");
                
                // Check if all profiles have this folder
                bool allHaveFolder = selectedProfiles.All(p => p.foldersToExport.Contains(folder));
                bool someHaveFolder = selectedProfiles.Any(p => p.foldersToExport.Contains(folder));
                
                var checkbox = new Toggle();
                checkbox.value = allHaveFolder;
                checkbox.AddToClassList("yucp-toggle");
                checkbox.RegisterValueChangedCallback(evt =>
                {
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Change Folder");
                        if (evt.newValue)
                        {
                            if (!profile.foldersToExport.Contains(folder))
                            {
                                profile.foldersToExport.Add(folder);
                            }
                        }
                        else
                        {
                            profile.foldersToExport.Remove(folder);
                        }
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                });
                folderItem.Add(checkbox);
                
                var pathLabel = new Label(folder);
                pathLabel.AddToClassList("yucp-folder-item-path");
                if (!allHaveFolder && someHaveFolder)
                {
                    pathLabel.style.opacity = 0.7f;
                    var mixedLabel = new Label(" (Mixed)");
                    mixedLabel.AddToClassList("yucp-label-secondary");
                    mixedLabel.style.marginLeft = 4;
                    folderItem.Add(mixedLabel);
                }
                folderItem.Add(pathLabel);
                
                scrollView.Add(folderItem);
            }
            
            folderList.Add(scrollView);
            section.Add(folderList);
            
            // Add folder button
            var addButton = new Button(() => 
            {
                string folderPath = EditorUtility.OpenFolderPanel("Select Folder to Add", Application.dataPath, "");
                if (!string.IsNullOrEmpty(folderPath))
                {
                    // Convert to relative path
                    string relativePath = GetRelativePath(folderPath);
                    if (string.IsNullOrEmpty(relativePath))
                    {
                        EditorUtility.DisplayDialog("Invalid Path", "Please select a folder within the Unity project.", "OK");
                        return;
                    }
                    
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Add Folder");
                        if (!profile.foldersToExport.Contains(relativePath))
                        {
                            profile.foldersToExport.Add(relativePath);
                        }
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                }
            }) { text = "+ Add Folder to All" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            return section;
        }
        
        private VisualElement CreateBulkDependenciesSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Package Dependencies");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpText = new Label("Add or remove dependencies across all selected profiles");
            helpText.AddToClassList("yucp-label-secondary");
            helpText.style.marginBottom = 8;
            section.Add(helpText);
            
            // Get unique dependencies across all profiles
            var allDependencies = selectedProfiles
                .SelectMany(p => p.dependencies ?? new List<PackageDependency>())
                .GroupBy(d => d.packageName)
                .Select(g => g.First())
                .OrderBy(d => d.packageName)
                .ToList();
            
            var depsList = new VisualElement();
            depsList.AddToClassList("yucp-folder-list");
            depsList.style.maxHeight = 200;
            
            var scrollView = new ScrollView();
            
            foreach (var dep in allDependencies)
            {
                var depItem = new VisualElement();
                depItem.AddToClassList("yucp-folder-item");
                
                // Check if all profiles have this dependency and if it's enabled
                bool allHaveDep = selectedProfiles.All(p => p.dependencies.Any(d => d.packageName == dep.packageName));
                bool someHaveDep = selectedProfiles.Any(p => p.dependencies.Any(d => d.packageName == dep.packageName));
                int enabledCount = selectedProfiles.Count(p => p.dependencies.Any(d => d.packageName == dep.packageName && d.enabled));
                int totalCount = selectedProfiles.Count;
                
                var checkbox = new Toggle();
                checkbox.value = allHaveDep;
                checkbox.AddToClassList("yucp-toggle");
                checkbox.RegisterValueChangedCallback(evt =>
                {
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Change Dependency");
                        var existingDep = profile.dependencies.FirstOrDefault(d => d.packageName == dep.packageName);
                        if (evt.newValue)
                        {
                            if (existingDep == null)
                            {
                                // Clone the dependency to add to this profile
                                var newDep = new PackageDependency(dep.packageName, dep.packageVersion, dep.displayName, dep.isVpmDependency);
                                newDep.enabled = dep.enabled;
                                newDep.exportMode = dep.exportMode;
                                profile.dependencies.Add(newDep);
                            }
                        }
                        else
                        {
                            if (existingDep != null)
                            {
                                profile.dependencies.Remove(existingDep);
                            }
                        }
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                });
                depItem.Add(checkbox);
                
                // Create container for label and status
                var labelContainer = new VisualElement();
                labelContainer.style.flexDirection = FlexDirection.Row;
                labelContainer.style.flexGrow = 1;
                labelContainer.style.alignItems = Align.Center;
                
                var depLabel = new Label($"{dep.displayName} ({dep.packageName}@{dep.packageVersion})");
                depLabel.AddToClassList("yucp-folder-item-path");
                depLabel.style.flexGrow = 1;
                if (!allHaveDep && someHaveDep)
                {
                    depLabel.style.opacity = 0.7f;
                }
                labelContainer.Add(depLabel);
                
                // Add status indicator showing how many profiles have this dependency enabled
                if (someHaveDep)
                {
                    var statusLabel = new Label();
                    statusLabel.AddToClassList("yucp-label-secondary");
                    statusLabel.style.marginLeft = 8;
                    statusLabel.style.fontSize = 10;
                    
                    if (allHaveDep)
                    {
                        if (enabledCount == totalCount)
                        {
                            statusLabel.text = $"[OK] All ({enabledCount}/{totalCount} enabled)";
                            statusLabel.style.color = new Color(0.3f, 0.8f, 0.3f);
                        }
                        else if (enabledCount == 0)
                        {
                            statusLabel.text = $"All ({enabledCount}/{totalCount} enabled)";
                            statusLabel.style.color = new Color(0.8f, 0.5f, 0.3f);
                        }
                        else
                        {
                            statusLabel.text = $"All ({enabledCount}/{totalCount} enabled)";
                            statusLabel.style.color = new Color(0.8f, 0.8f, 0.3f);
                        }
                    }
                    else
                    {
                        int haveCount = selectedProfiles.Count(p => p.dependencies.Any(d => d.packageName == dep.packageName));
                        statusLabel.text = $"Mixed ({haveCount}/{totalCount} have it, {enabledCount} enabled)";
                        statusLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                    }
                    labelContainer.Add(statusLabel);
                }
                
                depItem.Add(labelContainer);
                
                scrollView.Add(depItem);
            }
            
            depsList.Add(scrollView);
            section.Add(depsList);
            
            // Add dependency button
            var addButton = new Button(() => 
            {
                // Create a default dependency - users can edit it after adding
                var newDep = new PackageDependency("com.example.package", "1.0.0", "Example Package", false);
                
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Add Dependency");
                    // Check if a dependency with this name already exists
                    if (!profile.dependencies.Any(d => d.packageName == newDep.packageName))
                    {
                        // Clone the dependency for each profile
                        var clonedDep = new PackageDependency(newDep.packageName, newDep.packageVersion, newDep.displayName, newDep.isVpmDependency);
                        clonedDep.enabled = newDep.enabled;
                        clonedDep.exportMode = newDep.exportMode;
                        profile.dependencies.Add(clonedDep);
                    }
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            }) { text = "+ Add Dependency to All" };
            addButton.AddToClassList("yucp-button");
            addButton.AddToClassList("yucp-button-action");
            addButton.style.marginTop = 8;
            section.Add(addButton);
            
            return section;
        }
        
        private VisualElement CreateBulkExclusionFiltersSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Exclusion Filters");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var helpText = new Label("Add or remove exclusion patterns across all selected profiles");
            helpText.AddToClassList("yucp-label-secondary");
            helpText.style.marginBottom = 8;
            section.Add(helpText);
            
            // File Patterns
            var filePatternsLabel = new Label("File Patterns");
            filePatternsLabel.AddToClassList("yucp-label");
            filePatternsLabel.style.marginTop = 8;
            filePatternsLabel.style.marginBottom = 4;
            filePatternsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.Add(filePatternsLabel);
            
            var allFilePatterns = selectedProfiles
                .SelectMany(p => p.excludeFilePatterns ?? new List<string>())
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            
            var filePatternsContainer = new VisualElement();
            filePatternsContainer.style.maxHeight = 150;
            var fileScrollView = new ScrollView();
            
            foreach (var pattern in allFilePatterns)
            {
                bool allHavePattern = selectedProfiles.All(p => p.excludeFilePatterns.Contains(pattern));
                bool someHavePattern = selectedProfiles.Any(p => p.excludeFilePatterns.Contains(pattern));
                
                var patternItem = CreateBulkStringListItem(pattern, allHavePattern, someHavePattern,
                    (profile, value) =>
                    {
                        if (value)
                        {
                            if (!profile.excludeFilePatterns.Contains(pattern))
                                profile.excludeFilePatterns.Add(pattern);
                        }
                        else
                        {
                            profile.excludeFilePatterns.Remove(pattern);
                        }
                    });
                fileScrollView.Add(patternItem);
            }
            
            filePatternsContainer.Add(fileScrollView);
            section.Add(filePatternsContainer);
            
            var addFilePatternButton = new Button(() =>
            {
                // Add a default pattern - users can edit it after adding
                string pattern = "*.tmp";
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Add File Pattern");
                    if (!profile.excludeFilePatterns.Contains(pattern))
                    {
                        profile.excludeFilePatterns.Add(pattern);
                    }
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            }) { text = "+ Add Pattern to All" };
            addFilePatternButton.AddToClassList("yucp-button");
            addFilePatternButton.style.marginBottom = 12;
            section.Add(addFilePatternButton);
            
            // Folder Names
            var folderNamesLabel = new Label("Folder Names");
            folderNamesLabel.AddToClassList("yucp-label");
            folderNamesLabel.style.marginTop = 8;
            folderNamesLabel.style.marginBottom = 4;
            folderNamesLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.Add(folderNamesLabel);
            
            var allFolderNames = selectedProfiles
                .SelectMany(p => p.excludeFolderNames ?? new List<string>())
                .Distinct()
                .OrderBy(f => f)
                .ToList();
            
            var folderNamesContainer = new VisualElement();
            folderNamesContainer.style.maxHeight = 150;
            var folderScrollView = new ScrollView();
            
            foreach (var folderName in allFolderNames)
            {
                bool allHaveFolder = selectedProfiles.All(p => p.excludeFolderNames.Contains(folderName));
                bool someHaveFolder = selectedProfiles.Any(p => p.excludeFolderNames.Contains(folderName));
                
                var folderItem = CreateBulkStringListItem(folderName, allHaveFolder, someHaveFolder,
                    (profile, value) =>
                    {
                        if (value)
                        {
                            if (!profile.excludeFolderNames.Contains(folderName))
                                profile.excludeFolderNames.Add(folderName);
                        }
                        else
                        {
                            profile.excludeFolderNames.Remove(folderName);
                        }
                    });
                folderScrollView.Add(folderItem);
            }
            
            folderNamesContainer.Add(folderScrollView);
            section.Add(folderNamesContainer);
            
            var addFolderNameButton = new Button(() =>
            {
                // Add a default folder name - users can edit it after adding
                string folderName = ".git";
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Add Folder Name");
                    if (!profile.excludeFolderNames.Contains(folderName))
                    {
                        profile.excludeFolderNames.Add(folderName);
                    }
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            }) { text = "+ Add Folder Name to All" };
            addFolderNameButton.AddToClassList("yucp-button");
            section.Add(addFolderNameButton);
            
            return section;
        }
        
        private VisualElement CreateBulkStringListItem(string value, bool allHave, bool someHave, 
            System.Action<ExportProfile, bool> toggleAction)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-folder-item");
            
            var checkbox = new Toggle();
            checkbox.value = allHave;
            checkbox.AddToClassList("yucp-toggle");
            checkbox.RegisterValueChangedCallback(evt =>
            {
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Change Exclusion");
                    toggleAction(profile, evt.newValue);
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            });
            item.Add(checkbox);
            
            var textField = new TextField { value = value };
            textField.AddToClassList("yucp-input");
            textField.style.flexGrow = 1;
            textField.isReadOnly = true;
            if (!allHave && someHave)
            {
                textField.style.opacity = 0.7f;
                var mixedLabel = new Label(" (Mixed)");
                mixedLabel.AddToClassList("yucp-label-secondary");
                mixedLabel.style.marginLeft = 4;
                item.Add(mixedLabel);
            }
            item.Add(textField);
            
            var removeButton = new Button(() =>
            {
                ApplyToAllSelected(profile =>
                {
                    Undo.RecordObject(profile, "Bulk Remove Exclusion");
                    toggleAction(profile, false);
                    EditorUtility.SetDirty(profile);
                });
                UpdateProfileDetails();
            }) { text = "×" };
            removeButton.AddToClassList("yucp-button");
            removeButton.AddToClassList("yucp-folder-item-remove");
            item.Add(removeButton);
            
            return item;
        }
        
        private VisualElement CreateBulkObfuscationSection(List<ExportProfile> selectedProfiles)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 16;
            
            var title = new Label("Obfuscation Settings");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            bool allObfuscation = selectedProfiles.All(p => p.enableObfuscation);
            bool allStripDebug = selectedProfiles.All(p => p.stripDebugSymbols);
            
            var obfuscationToggle = CreateBulkToggle("Enable Obfuscation", allObfuscation,
                profile => profile.enableObfuscation,
                (profile, value) => profile.enableObfuscation = value);
            section.Add(obfuscationToggle);
            
            var stripDebugToggle = CreateBulkToggle("Strip Debug Symbols", allStripDebug,
                profile => profile.stripDebugSymbols,
                (profile, value) => profile.stripDebugSymbols = value);
            section.Add(stripDebugToggle);
            
            // Obfuscation Preset (only if all have same value)
            var presets = selectedProfiles.Select(p => p.obfuscationPreset).Distinct().ToList();
            if (presets.Count == 1)
            {
                var presetRow = CreateFormRow("Obfuscation Preset");
                var presetField = new EnumField(presets[0]);
                presetField.AddToClassList("yucp-form-field");
                presetField.RegisterValueChangedCallback(evt =>
                {
                    ApplyToAllSelected(profile =>
                    {
                        Undo.RecordObject(profile, "Bulk Change Obfuscation Preset");
                        profile.obfuscationPreset = (ConfuserExPreset)evt.newValue;
                        EditorUtility.SetDirty(profile);
                    });
                    UpdateProfileDetails();
                });
                presetRow.Add(presetField);
                section.Add(presetRow);
            }
            
            return section;
        }
        
        private VisualElement CreateBulkToggle(string label, bool allSame, 
            System.Func<ExportProfile, bool> getValue, 
            System.Action<ExportProfile, bool> setValue)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.marginBottom = 4;
            
            var toggle = new Toggle(label);
            toggle.AddToClassList("yucp-toggle");
            toggle.value = allSame;
            
            if (!allSame)
            {
                // When mixed, show indeterminate state but allow toggling
                var mixedLabel = new Label(" (Mixed - click to set all)");
                mixedLabel.AddToClassList("yucp-label-secondary");
                mixedLabel.style.marginLeft = 4;
                container.Add(mixedLabel);
            }
            
            toggle.RegisterValueChangedCallback(evt =>
            {
                // Apply the new value to all selected profiles
                bool newValue = evt.newValue;
                ApplyToAllSelected(profile => 
                {
                    Undo.RecordObject(profile, $"Bulk Toggle {label}");
                    setValue(profile, newValue);
                    EditorUtility.SetDirty(profile);
                });
                // Refresh to update UI
                UpdateProfileDetails();
            });
            
            container.Add(toggle);
            return container;
        }
        
        private void ApplyToAllSelected(System.Action<ExportProfile> action)
        {
            foreach (int index in selectedProfileIndices)
            {
                if (index >= 0 && index < allProfiles.Count)
                {
                    var profile = allProfiles[index];
                    if (profile != null)
                    {
                        action(profile);
                    }
                }
            }
            UpdateProfileDetails(); // Refresh to show changes
        }
        
        private VisualElement CreateMultiProfileSummarySection()
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            
            var title = new Label("Selected Profiles");
            title.AddToClassList("yucp-section-title");
            section.Add(title);
            
            var list = new ScrollView();
            list.style.maxHeight = 300;
            
            foreach (int index in selectedProfileIndices.OrderBy(i => i))
            {
                if (index >= 0 && index < allProfiles.Count)
                {
                    var profile = allProfiles[index];
                    if (profile != null)
                    {
                        var item = new VisualElement();
                        item.style.flexDirection = FlexDirection.Row;
                        item.style.alignItems = Align.Center;
                        item.style.paddingTop = 4;
                        item.style.paddingBottom = 4;
                        item.style.paddingLeft = 8;
                        item.style.paddingRight = 8;
                        item.style.marginBottom = 2;
                        item.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
                        
                        var nameLabel = new Label(GetProfileDisplayName(profile));
                        nameLabel.AddToClassList("yucp-label");
                        nameLabel.style.flexGrow = 1;
                        item.Add(nameLabel);
                        
                        var versionLabel = new Label($"v{profile.version}");
                        versionLabel.AddToClassList("yucp-label-secondary");
                        versionLabel.style.marginLeft = 8;
                        item.Add(versionLabel);
                        
                        list.Add(item);
                    }
                }
            }
            
            section.Add(list);
            return section;
        }
        
        private void CloseOverlay()
        {
            _isOverlayOpen = false;
            
            // Animate overlay out
            if (_leftPaneOverlay != null)
            {
                _leftPaneOverlay.style.left = -270;
                _leftPaneOverlay.style.opacity = 0;
            }
            
            // Fade out backdrop
            if (_overlayBackdrop != null)
            {
                _overlayBackdrop.style.opacity = 0;
            }
            
            // Hide after animation completes (300ms)
            rootVisualElement.schedule.Execute(() => 
            {
                if (_leftPaneOverlay != null && !_isOverlayOpen)
                {
                    _leftPaneOverlay.style.display = DisplayStyle.None;
                    _leftPaneOverlay.style.visibility = Visibility.Hidden;
                }
                if (_overlayBackdrop != null && !_isOverlayOpen)
                {
                    _overlayBackdrop.style.display = DisplayStyle.None;
                    _overlayBackdrop.style.visibility = Visibility.Hidden;
                }
            }).StartingIn(300);
        }
    }
}
