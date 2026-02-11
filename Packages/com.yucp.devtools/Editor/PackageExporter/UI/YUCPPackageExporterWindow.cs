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
    /// <summary>
    /// Represents a folder node in the hierarchical tree structure
    /// </summary>

    
    /// <summary>
    /// Represents a menu item in the toolbar
    /// </summary>

    
    /// <summary>
    /// Main Package Exporter window with profile management and batch export capabilities.
    /// Modern UI Toolkit implementation matching Package Guardian design system.
    /// </summary>
    public partial class YUCPPackageExporterWindow : EditorWindow
    {
        // UI Elements
        private ScrollView _profileListScrollView;
        private ScrollView _rightPaneScrollView;
        private VisualElement _profileListContainer;
        private VisualElement _profileListContainerOverlay;
        private VisualElement _profileDetailsContainer;
        private TopNavBar _topNavBar;
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
        private TextField _packageNameField;
        private Button _changeBannerButton;
        private EventCallback<GeometryChangedEvent> _bannerButtonGeometryCallback;
        private Label _progressText;
        private Label _progressDetail;
        private ScrollView _progressDetailScroll;
        private List<string> _exportStepLog = new List<string>();
        private const int MaxExportStepLogEntries = 14;
        private VisualElement _multiSelectInfo;
        private Button _exportSelectedButton;
        private Button _exportAllButton;
        private VisualElement _supportToast;
        
        // Animated GIF support
        private Dictionary<string, AnimatedGifData> animatedGifs = new Dictionary<string, AnimatedGifData>();
        
        // Resizable inspector state
        private bool isResizingInspector = false;
        private float resizeStartY = 0f;
        private float resizeStartHeight = 0f;
        private VisualElement currentResizableContainer = null; // Track which container is being resized
        private Rect currentResizeRect = Rect.zero; // Store the resize rect for cursor
        
        // Resizable left pane state
        private bool isResizingLeftPane = false;
        private float resizeStartX = 0f;
        private float resizeStartWidth = 0f;
        private VisualElement _resizeHandle;
        private Rect currentLeftPaneResizeRect = Rect.zero; // Store the resize rect for cursor
        private const string LeftPaneWidthKey = "com.yucp.devtools.packageexporter.leftpanewidth";
        private const float DefaultLeftPaneWidth = 270f;
        private const float MinLeftPaneWidth = 150f;
        private const float MaxLeftPaneWidth = 600f;
        
        // State
        private List<ExportProfile> allProfiles = new List<ExportProfile>();
        private ExportProfile selectedProfile;
        private HashSet<int> selectedProfileIndices = new HashSet<int>();
        private int lastClickedProfileIndex = -1;
        private bool isExporting = false;
        private float currentProgress = 0f;
        private string currentStatus = "";
        
        // Drag-and-drop state
        private int draggingIndex = -1;
        private VisualElement draggingElement = null;
        private Vector2 dragOffset = Vector2.zero;
        private Vector2 dragStartPosition = Vector2.zero;
        private bool hasDragged = false;
        private const float DRAG_THRESHOLD = 5f; // Pixels to move before considering it a drag
        private int potentialDropIndex = -1; // Where the item would be dropped
        private Dictionary<VisualElement, MotionHandle> motionHandles = new Dictionary<VisualElement, MotionHandle>(); // Motion handles for smooth animations
        private Dictionary<int, float> rowGaps = new Dictionary<int, float>(); // Track gaps for smooth spacing animation (vFavorites approach)
        private float draggedItemY = 0f; // Y position of dragged item (absolute)
        private VisualElement draggedItemContainer = null; // Container for absolutely positioned dragged item
        private int draggingItemFromPageAtIndex = -1; // Original index of dragged item (vFavorites approach)
        private int insertDraggedItemAtIndex = -1; // Where item will be inserted (calculated from mouse position)
        // private float draggedItemHoldOffset = 0f; // Offset from mouse to item center when drag started (vFavorites approach) - kept for potential future use
        private int draggingVisualIndex = -1; // Visual index of dragged item in list
        /// <summary>List container where the current drag started (main sidebar or overlay). Used so overlay uses same folder/profile logic as main.</summary>
        private VisualElement _draggingListContainer = null;
        
        // Stack-to-create folder state
        private VisualElement stackTargetElement = null;
        private int stackTargetIndex = -1;
        private double stackHoverStartTime = 0;
        private const double STACK_HOVER_THRESHOLD = 0.5; // 500ms
        private Vector2 stackHoverPosition = Vector2.zero;
        private const float STACK_POSITION_TOLERANCE = 15f; // pixels
        private bool isStackReady = false;
        
        // Delayed rename tracking
        private double lastPackageNameChangeTime = 0;
        private const double RENAME_DELAY_SECONDS = 1.5;
        private ExportProfile pendingRenameProfile = null;
        private string pendingRenamePackageName = "";
        
        // Folder renaming state
        private string folderBeingRenamed = null;
        private double folderRenameStartTime = -1;
        // Client ID provided by the user for fetching official product icons
        private const string BrandfetchClientId = "1bxid64Mup7aczewSAYMX";
        
        // Export Inspector state
        private string inspectorSearchFilter = "";
        private bool showOnlyIncluded = false;
        private bool showOnlyExcluded = false;
        private bool showExportInspector = true;
		private bool showOnlyDerived = false;
        private string sourceProfileFilter = "All";
        private Dictionary<string, bool> folderExpandedStates = new Dictionary<string, bool>();
        
        // Exclusion Filters state
        private bool showExclusionFilters = false;
        
        // Compact View Mode
        private bool _isCompactMode = false;
        private const string CompactModeKey = "com.yucp.devtools.packageexporter.compactmode";

        // Show Info Help Boxes (can be turned off from menu, independent of compact mode)
        private bool _showInfoHelpBoxes = true;
        private const string ShowInfoHelpBoxesKey = "com.yucp.devtools.packageexporter.showinfohelpboxes";

        
        // Dependencies filter state
        private string dependenciesSearchFilter = "";
        
        // Profile list sort and filter state
        private enum ProfileSortOption
        {
            Custom,
            Name,
            ExportDate,
            Version,
            ExportCount
        }
        
        private ProfileSortOption currentSortOption = ProfileSortOption.Custom;
        private List<string> selectedFilterTags = new List<string>();
        private string selectedFilterFolder = null; // null means "All Folders"
        private string _currentSearchText = "";
        private YUCP.DevTools.Editor.PackageExporter.UI.Components.TokenizedSearchField _mainSearchField;
        private YUCP.DevTools.Editor.PackageExporter.UI.Components.TokenizedSearchField _overlaySearchField;
        
        // Collapsible section states
        private bool showExportOptions = true;
        private bool showFolders = true;
        private bool showDependencies = true;
        private bool showQuickActions = true;
        private bool showUpdateSteps = true;
        
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
        
        // Package reordering prefs
        private const string PackageOrderKey = "com.yucp.devtools.packageexporter.order";
        private const string PackageFoldersKey = "com.yucp.devtools.packageexporter.folders";
        private const string UnifiedOrderKey = "com.yucp.devtools.packageexporter.unifiedorder";
        private const string UnifiedOrderMigratedKey = "com.yucp.devtools.packageexporter.unifiedorder.migrated";
        
        // Folder state
        private List<string> projectFolders = new List<string>();
        private HashSet<string> collapsedFolders = new HashSet<string>();
        private const string CollapsedFoldersKey = "com.yucp.devtools.packageexporter.collapsedfolders";
        
        // Unified order (profiles and folders mixed)
        private List<UnifiedOrderItem> unifiedOrder = new List<UnifiedOrderItem>();

        private VisualElement _currentPopover = null;
        private VisualElement _popoverBackdrop = null;
        private double lastGapUpdateTime = 0;
        private List<FaviconRequestData> faviconRequests = new List<FaviconRequestData>();
        private VisualElement _signingSectionElement;
        private float lastGifUpdateTime = 0f;
        private Dictionary<string, bool> _updateStepValidationFoldouts = new Dictionary<string, bool>();
        /// <summary>Main Package Exporter wiki (Help / Documentation).</summary>
        private const string PackageExporterWikiUrl = "https://github.com/Yeusepe/YUCP-Dev-Tools/wiki/Package-Exporter";
        /// <summary>Derived FBXs section (onboarding "Learn more" link).</summary>
        private const string DerivedFbxWikiUrl = "https://github.com/Yeusepe/YUCP-Dev-Tools/wiki/Package-Exporter#derived-fbxs";
    }
}
