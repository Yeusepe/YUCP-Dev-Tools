using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VRC.Core;
using VRC.Editor;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3A.Editor;
using VRC.SDK3A.Editor.Elements;
using VRC.SDKBase;
using VRC.SDKBase.Editor.Api;
using VRC.SDKBase.Editor.Elements;
using YUCP.DevTools.Editor.AvatarUploader.UI.Components;
using YUCP.DevTools.Editor.AvatarUploader.UI;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	public class AvatarToolsWindow : EditorWindow
	{
		private const string WindowUxmlPath = "Packages/com.yucp.devtools/Editor/AvatarUploader/UI/AvatarUploaderWindow.uxml";
		private const string WindowUssPath = "Packages/com.yucp.devtools/Editor/AvatarUploader/UI/Styles/AvatarUploader.uss";

		[MenuItem("Tools/YUCP/Avatar Tools")]
		public static void ShowWindow()
		{
			var window = GetWindow<AvatarToolsWindow>();
			var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DevTools.png");
			window.titleContent = new GUIContent("YUCP Avatar Tools", icon);
			window.minSize = new Vector2(400, 500);
			window.Show();
			
			// Ensure control panel is open (required for sign-in and data retrieval)
			ControlPanelBridge.EnsureBuilder(null, focusPanel: false);
		}
		
		private void ShowBridgeStatus(string message)
		{
			// Don't show "Control Panel Ready" toasts - they're not actionable and clutter the UI
			// Only show errors/warnings
			if (message.Contains("unavailable") || message.Contains("error") || message.Contains("Error"))
			{
				_toast?.ShowWarning(message, "Control Panel", 3f);
			}
		}

		// UI Elements
		private VisualElement _profileListContainer;
		private VisualElement _profileToolbarHost;
		private VisualElement _profileDetailsContainer;
		private VisualElement _heroSectionHost;
		private VisualElement _heroContentContainer;
		private VisualElement _thumbnailHost;
		private VisualElement _metadataSectionHost;
		private VisualElement _performanceSectionHost;
		private VisualElement _visibilitySectionHost;
		private VisualElement _validationSectionHost;
		private VisualElement _buildActionsHost;
		private VisualElement _advancedSectionHost;
		private VisualElement _collectionHeaderHost;
		private VisualElement _avatarGridHost;
		private VisualElement _avatarDetailsHost;
		private VisualElement _iconSidebarHost;
		private VisualElement _gallerySectionHost;
		private ToastNotification _toast;
		private IVRCSdkControlPanelBuilder _controlPanelBuilder;
		private bool _controlPanelInitialized;
		private EventHandler _cpContentChangedHandler;
		private EventHandler _cpRevalidateHandler;
		private bool _isSyncingToControlPanel = false; // Flag to prevent event feedback loops
		private bool _isUserTyping = false; // Flag to prevent UI rebuilds while user is typing
		private GameObject _lastSelectedAvatarRoot = null; // Track last selected avatar to prevent loops
		private bool _isSelectingAvatar = false; // Flag to prevent recursive SelectAvatar calls
		private TextField _nameFieldRef; // Reference to name field to update without rebuilding
		private TextField _descFieldRef; // Reference to description field to update without rebuilding
		private ControlPanelUiBinder _cpUiBinder;
		private VisualElement _emptyState;
		private VisualElement _progressContainer;
		private VisualElement _progressFill;
		private Label _progressText;
		private ToolbarSearchField _profileSearchField;
		private Toggle _filterHasAvatars;
		private Toggle _filterHasBuilds;
		private Button _buildButton;
		private Button _testButton;
		private Button _settingsButton;

		// State
		private List<AvatarCollection> _profiles = new List<AvatarCollection>();
		private AvatarCollection _selectedProfile;
		private int _selectedIndex = -1;
		private string _profileSearchFilter = string.Empty;
		private bool _filterHasAvatarsValue;
		private bool _filterHasBuildsValue;
		private ScrollView _avatarGridScroll;
		private VisualElement _avatarGridContent;
		private int _selectedAvatarIndex = -1;
		private HashSet<int> _selectedAvatarIndices = new HashSet<int>(); // Multi-select support
		private VisualElement _bulkEditPanel;
		private VisualElement _heroPanel;
		private Image _heroImageDisplay;
		private VisualElement _heroOverlay;
		private VisualElement _heroInfoContainer;
		private Button _heroPrevButton;
		private Button _heroAddButton;
		private Button _heroSetIconButton;
		private Button _heroDeleteButton;
		private Button _heroNextButton;
		private Label _heroSlideLabel;
		private readonly List<HeroSlide> _heroSlides = new();
		private int _activeHeroSlideIndex;
		private readonly HashSet<AvatarAsset> _loadingGalleries = new();
		private AvatarAsset _currentHeroConfig;

		// Avatar position tracking during upload (to remember which avatars are where)
		private readonly Dictionary<AvatarAsset, (AvatarCollection collection, int index)> _avatarPositions = new();

		// Build state
		private bool _isBuilding;
		private float _progress;
		private string _status = string.Empty;

		// Resources
		private Texture2D _logoTexture;

		// Responsive design elements
		private Button _mobileToggleButton;
		private VisualElement _leftPaneOverlay;
		private VisualElement _overlayBackdrop;
		private VisualElement _contentContainer;
		private VisualElement _leftPane;
		private bool _isOverlayOpen = false;

		private enum BuildWorkflow
		{
			BuildOnly,
			TestOnly,
			Publish
		}

	private void OnEnable()
	{
		ReloadProfiles();
		LoadResources();
		// Don't restore selected avatar here - wait for CreateGUI to initialize UI elements
		// RestoreSelectedAvatar will be called after CreateGUI completes
		ControlPanelBridge.EnsureBuilder(null);
		
		// Subscribe to scene changes to clean up temporary instances from previous scenes
		UnityEditor.SceneManagement.EditorSceneManager.activeSceneChanged += OnActiveSceneChanged;
	}

		private void RestoreSelectedAvatar()
		{
			// Restore selected profile index
			var savedProfileIndex = EditorPrefs.GetInt("YUCP.AvatarTools.SelectedProfileIndex", -1);
			if (savedProfileIndex >= 0 && savedProfileIndex < _profiles.Count)
			{
				_selectedIndex = savedProfileIndex;
				_selectedProfile = _profiles[_selectedIndex];
				UpdateProfileList();
				UpdateProfileDetails();
			}

			// Restore selected avatar index within profile
			if (_selectedProfile != null)
			{
				var savedAvatarIndex = EditorPrefs.GetInt($"YUCP.AvatarTools.SelectedAvatarIndex.{_selectedProfile.GetInstanceID()}", -1);
				if (savedAvatarIndex >= 0 && savedAvatarIndex < _selectedProfile.avatars.Count)
				{
					_selectedAvatarIndex = savedAvatarIndex;
					_currentHeroConfig = _selectedProfile.avatars[_selectedAvatarIndex];
					UpdateHeroPanel(_selectedProfile, _currentHeroConfig, _selectedAvatarIndex);
					BuildAvatarGridView(_selectedProfile);
				}
			}
		}

		private void SaveSelectedAvatar()
		{
			// Save selected profile index
			if (_selectedIndex >= 0 && _selectedIndex < _profiles.Count)
			{
				EditorPrefs.SetInt("YUCP.AvatarTools.SelectedProfileIndex", _selectedIndex);
			}

			// Save selected avatar index within profile
			if (_selectedProfile != null && _selectedAvatarIndex >= 0 && _selectedAvatarIndex < _selectedProfile.avatars.Count)
			{
				EditorPrefs.SetInt($"YUCP.AvatarTools.SelectedAvatarIndex.{_selectedProfile.GetInstanceID()}", _selectedAvatarIndex);
			}
		}

	private void OnDisable()
	{
		TearDownControlPanelProxy();
		
		// Unsubscribe from scene changes
		UnityEditor.SceneManagement.EditorSceneManager.activeSceneChanged -= OnActiveSceneChanged;
		
		// Clean up temporary instances from scenes other than the active one
		CleanupTemporaryInstances(onlyFromOtherScenes: true);
	}

	private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene)
	{
		// Clean up temporary instances from the old scene
		CleanupTemporaryInstances(onlyFromOtherScenes: true);
	}

		private void LoadResources()
		{
			_logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DevTools.png");
		}

		private void CreateGUI()
		{
			var root = rootVisualElement;
			root.Clear();
			root.AddToClassList("yucp-window");
			ShowStartupDisclaimerIfNeeded();

			var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
			if (visualTree == null)
			{
				Debug.LogWarning("[AvatarUploader] AvatarUploaderWindow.uxml not found.");
				return;
			}

			visualTree.CloneTree(root);

			var builderStyles = Resources.Load<StyleSheet>("VRCSdkBuilderStyles");
			if (builderStyles != null && !root.styleSheets.Contains(builderStyles))
			{
				root.styleSheets.Add(builderStyles);
			}

			// Load shared design system stylesheet first
			var sharedStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.yucp.devtools/Editor/Styles/YucpDesignSystem.uss");
			if (sharedStyleSheet != null && !root.styleSheets.Contains(sharedStyleSheet))
			{
				root.styleSheets.Add(sharedStyleSheet);
			}

			// Load component-specific stylesheet
			var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(WindowUssPath);
			if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
			{
				root.styleSheets.Add(styleSheet);
			}

			BindUiElements(root);

			UpdateProfileList();
			UpdateProfileDetails();
			UpdateBottomBar();

			root.RegisterCallback<KeyDownEvent>(OnKeyDown);

			// Register global mouse move for 3D preview tracking throughout window
			root.RegisterCallback<MouseMoveEvent>(OnGlobalMouseMove);

			// Register for geometry changes to handle responsive layout
			root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

			// Schedule initial responsive check after layout is ready
			root.schedule.Execute(() => 
			{
				UpdateResponsiveLayout(rootVisualElement.resolvedStyle.width);
			}).StartingIn(100);

			// Restore selected avatar after UI is initialized
			RestoreSelectedAvatar();
		}

		private void OnGlobalMouseMove(MouseMoveEvent evt)
		{
			// Update shared mouse position for all tile preview renderers
			AvatarTilePreviewRenderer.UpdateGlobalMousePosition(evt.mousePosition);
		}

		private void BindUiElements(VisualElement root)
		{
			var topBarHost = root.Q<VisualElement>("top-bar-host");
			topBarHost?.Clear();
			topBarHost?.Add(CreateTopBar());

			// Initialize toast notification system
			_toast = new ToastNotification(root);

			// Get content container and left pane for responsive layout
			_contentContainer = root.Q<VisualElement>(className: "yucp-content-container");
			_leftPane = root.Q<VisualElement>(className: "yucp-left-pane");
			
			// Create overlay backdrop (for mobile menu)
			_overlayBackdrop = new VisualElement();
			_overlayBackdrop.AddToClassList("yucp-overlay-backdrop");
			_overlayBackdrop.RegisterCallback<ClickEvent>(evt => CloseOverlay());
			_overlayBackdrop.style.display = DisplayStyle.None;
			_overlayBackdrop.style.visibility = Visibility.Hidden;
			if (_contentContainer != null)
			{
				_contentContainer.Add(_overlayBackdrop);
			}
			
			// Create left pane overlay (for mobile)
			_leftPaneOverlay = CreateLeftPaneOverlay();
			if (_leftPaneOverlay != null && _contentContainer != null)
			{
				_contentContainer.Add(_leftPaneOverlay);
			}

			_profileToolbarHost = root.Q<VisualElement>("profile-toolbar");
			_profileListContainer = root.Q<VisualElement>("profile-list-host");

			// Collection Header (batch operations)
			_collectionHeaderHost = root.Q<VisualElement>("collection-header-host");
			_collectionHeaderHost?.Clear();

			// Bulk Edit Panel (appears when multiple avatars selected)
			_bulkEditPanel = root.Q<VisualElement>("bulk-edit-host");
			_bulkEditPanel?.Clear();
			if (_bulkEditPanel != null)
			{
				_bulkEditPanel.style.display = DisplayStyle.None;
			}

			// Avatar Grid Host (primary view)
			_avatarGridHost = root.Q<VisualElement>("avatar-grid-host");
			_avatarGridHost?.Clear();

			// Avatar Details Host (expandable panel, hidden by default)
			_avatarDetailsHost = root.Q<VisualElement>("avatar-details-host");
			if (_avatarDetailsHost != null)
			{
				_avatarDetailsHost.style.display = DisplayStyle.None;
			}

			// Icon Sidebar Host
			_iconSidebarHost = root.Q<VisualElement>("icon-sidebar-host");
			_iconSidebarHost?.Clear();

			// Gallery Section Host
			_gallerySectionHost = root.Q<VisualElement>("gallery-section-host");
			_gallerySectionHost?.Clear();

			_heroSectionHost = root.Q<VisualElement>("hero-section-host");
			_heroSectionHost?.Clear();
			_emptyState = CreateEmptyState();
			_heroContentContainer = new VisualElement();
			_heroContentContainer.AddToClassList("yucp-hero-section");
			_heroSectionHost?.Add(_emptyState);
			_heroSectionHost?.Add(_heroContentContainer);
			_thumbnailHost = new VisualElement();
			_thumbnailHost.AddToClassList("yucp-hero-thumbnail-host");
			_heroContentContainer?.Add(_thumbnailHost);

			// Build Actions Section (for individual avatar)
			_buildActionsHost = root.Q<VisualElement>("build-actions-host");
			_buildActionsHost?.Clear();

			_metadataSectionHost = root.Q<VisualElement>("metadata-section-host");
			_metadataSectionHost?.Clear();
			_profileDetailsContainer = new VisualElement();
			_profileDetailsContainer.AddToClassList("yucp-profile-details");
			_metadataSectionHost?.Add(_profileDetailsContainer);

			_performanceSectionHost = root.Q<VisualElement>("performance-section-host");
			_performanceSectionHost?.Clear();

			_validationSectionHost = root.Q<VisualElement>("validation-section-host");
			_validationSectionHost?.Clear();

			_visibilitySectionHost = root.Q<VisualElement>("visibility-section-host");
			_visibilitySectionHost?.Clear();

			// Advanced Settings Section (collapsible)
			_advancedSectionHost = root.Q<VisualElement>("advanced-section-host");
			_advancedSectionHost?.Clear();

			var bottomBarHost = root.Q<VisualElement>("bottom-bar-host");
			_progressContainer = bottomBarHost?.Q<VisualElement>("progress-container");
			_progressFill = bottomBarHost?.Q<VisualElement>("progress-fill");
			_progressText = bottomBarHost?.Q<Label>("progress-text");
			
			// Hide bottom bar by default (only shows during builds)
			if (bottomBarHost != null)
			{
				bottomBarHost.style.display = DisplayStyle.None;
			}

			ConfigureProfileToolbar();

			InitializeControlPanelProxy();

			ControlPanelBridge.EnsureBuilder(_ =>
			{
				ShowBridgeStatus("Control Panel Ready");
				InitializeControlPanelProxy();
			});
		}

		private void InitializeControlPanelProxy(bool forceRebuild = false)
		{
			if (_metadataSectionHost == null || _visibilitySectionHost == null || _validationSectionHost == null || _buildActionsHost == null)
				return;

			ControlPanelBridge.EnsureBuilder(_ =>
			{
				if (!ControlPanelBridge.TryGetControlPanelBuilder(out var builder))
				{
					ShowBridgeStatus("Control Panel builder unavailable.");
					return;
				}

				if (_cpUiBinder == null || forceRebuild || !_cpUiBinder.IsFor(builder))
				{
					TearDownControlPanelProxy();
					try
					{
						builder.Initialize();
					}
					catch (Exception ex)
					{
						Debug.LogException(ex);
					}
					_cpUiBinder = new ControlPanelUiBinder(this);
				}

				_cpUiBinder = new ControlPanelUiBinder(this);
				_cpUiBinder.Initialize(builder);
				_controlPanelBuilder = builder;
				AttachControlPanelEventHandlers(builder);
				_controlPanelInitialized = true;
				ShowBridgeStatus("Control Panel Ready");
				RefreshControlPanelValidations();
				BuildMetadataSection();
				BuildVisibilitySection();
				BuildBuildActionsSection();
				BuildAdvancedSection();
			});
		}

		private void AttachControlPanelEventHandlers(IVRCSdkControlPanelBuilder builder)
		{
			if (builder == null)
				return;

			_cpContentChangedHandler ??= (_, __) =>
			{
				// Only refresh validations, NOT metadata section, to prevent UI rebuilds while typing
				if (!_isSyncingToControlPanel)
					RefreshControlPanelValidations();
			};
			_cpRevalidateHandler ??= (_, __) =>
			{
				if (!_isSyncingToControlPanel)
					RefreshControlPanelValidations();
			};

			builder.OnContentChanged -= _cpContentChangedHandler;
			builder.OnShouldRevalidate -= _cpRevalidateHandler;

			builder.OnContentChanged += _cpContentChangedHandler;
			builder.OnShouldRevalidate += _cpRevalidateHandler;
		}

		private void RefreshControlPanelValidations()
		{
			if (_validationSectionHost == null || _isSyncingToControlPanel)
				return;

			try
			{
				BuildValidationSection();
				// Also rebuild build actions to update button states based on validation
				BuildBuildActionsSection();
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
		}

		private void TearDownControlPanelProxy()
		{
			if (_controlPanelBuilder != null)
			{
				if (_cpContentChangedHandler != null)
					_controlPanelBuilder.OnContentChanged -= _cpContentChangedHandler;
				if (_cpRevalidateHandler != null)
					_controlPanelBuilder.OnShouldRevalidate -= _cpRevalidateHandler;
			}

			_cpUiBinder?.Dispose();
			_cpUiBinder = null;
			_controlPanelBuilder = null;
			_controlPanelInitialized = false;
		}

		/// <summary>
		/// Sync blueprint ID from PipelineManager component to AvatarAsset.
		/// Called when the user manually changes the blueprint ID on the component.
		/// </summary>
		public void SyncBlueprintIdFromComponent(AvatarAsset asset)
		{
			if (asset == null || asset.avatarPrefab == null)
				return;

			asset.SyncBlueprintIdFromComponent();
			
			// Refresh UI if this is the current hero config
			if (_currentHeroConfig == asset)
			{
				UpdateHeroPanel(_selectedProfile, asset, _selectedAvatarIndex);
			}
		}

		/// <summary>
		/// Populate avatar data from VRChat API if blueprint ID exists.
		/// This fetches name, description, tags, etc. from the API.
		/// </summary>
		private async void PopulateAvatarFromAPI(AvatarAsset config)
		{
			if (config == null || config.avatarPrefab == null)
				return;

			// Don't call PopulateFromPipelineManager here - it's already called in ShowAvatarDetails/UpdateHeroPanel
			// Try to get blueprint ID from the asset
			var blueprintId = config.GetBlueprintId(PlatformSwitcher.BuildPlatform.PC);
			if (string.IsNullOrEmpty(blueprintId))
			{
				blueprintId = config.GetBlueprintId(PlatformSwitcher.BuildPlatform.Quest);
			}

			if (string.IsNullOrEmpty(blueprintId))
				return;

			try
			{
				// Use reflection to call VRCApi.GetAvatar (like the SDK does)
				var apiType = System.Type.GetType("VRC.SDKBase.Editor.Api.VRCApi, VRCSDKBase");
				if (apiType == null)
					return;

				var getAvatarMethod = apiType.GetMethod("GetAvatar", new[] { typeof(string), typeof(bool) });
				if (getAvatarMethod == null)
					return;

				var avatarTask = getAvatarMethod.Invoke(null, new object[] { blueprintId, true }) as System.Threading.Tasks.Task;
				if (avatarTask == null)
					return;

				await avatarTask;
				
				// Get the result using reflection
				var resultProperty = avatarTask.GetType().GetProperty("Result");
				if (resultProperty == null)
					return;

				var avatar = resultProperty.GetValue(avatarTask);
				if (avatar == null)
					return;

				// Populate fields from API response
				var nameProperty = avatar.GetType().GetProperty("Name");
				var descProperty = avatar.GetType().GetProperty("Description");
				var tagsProperty = avatar.GetType().GetProperty("Tags");
				var releaseStatusProperty = avatar.GetType().GetProperty("ReleaseStatus");

				Undo.RecordObject(config, "Populate from API");
				
				if (nameProperty != null)
				{
					var name = nameProperty.GetValue(avatar) as string;
					if (!string.IsNullOrEmpty(name) && string.IsNullOrEmpty(config.avatarName))
					{
						config.avatarName = name;
					}
				}

				if (descProperty != null)
				{
					var desc = descProperty.GetValue(avatar) as string;
					if (!string.IsNullOrEmpty(desc) && string.IsNullOrEmpty(config.description))
					{
						config.description = desc;
					}
				}

				if (tagsProperty != null)
				{
					var tags = tagsProperty.GetValue(avatar) as System.Collections.Generic.List<string>;
					if (tags != null && tags.Count > 0 && (config.tags == null || config.tags.Count == 0))
					{
						config.tags = new List<string>(tags);
					}
				}

				if (releaseStatusProperty != null)
				{
					var status = releaseStatusProperty.GetValue(avatar) as string;
					if (!string.IsNullOrEmpty(status))
					{
						config.releaseStatus = status == "public" ? ReleaseStatus.Public : ReleaseStatus.Private;
					}
				}

				// Try to get thumbnail/image URL from API response
				var imageUrlProperty = avatar.GetType().GetProperty("ImageUrl");
				if (imageUrlProperty == null)
					imageUrlProperty = avatar.GetType().GetProperty("ThumbnailImageUrl");
				if (imageUrlProperty == null)
					imageUrlProperty = avatar.GetType().GetProperty("Image");
				if (imageUrlProperty == null)
					imageUrlProperty = avatar.GetType().GetProperty("ThumbnailUrl");

				if (imageUrlProperty != null && config.avatarIcon == null)
				{
					var imageUrl = imageUrlProperty.GetValue(avatar) as string;
					if (!string.IsNullOrEmpty(imageUrl))
					{
						try
						{
							var thumbnail = await AvatarGalleryClient.DownloadImageAsync(imageUrl);
							if (thumbnail != null)
							{
								config.avatarIcon = thumbnail;
							}
						}
						catch (Exception ex)
						{
							Debug.LogWarning($"[AvatarUploader] Failed to download thumbnail from API: {ex.Message}");
						}
					}
				}

				EditorUtility.SetDirty(config);
				
				// Refresh UI if this is the current hero config
				if (_currentHeroConfig == config)
				{
					UpdateHeroPanel(_selectedProfile, config, _selectedAvatarIndex);
				}
			}
			catch (Exception ex)
			{
				// Silently fail - API might not be available or avatar might not exist
				Debug.LogWarning($"[AvatarUploader] Failed to populate from API: {ex.Message}");
			}
		}

		private void UpdateActiveAvatarName(string value)
		{
			if (_currentHeroConfig == null)
				return;

			Undo.RecordObject(_currentHeroConfig, "Change Avatar Name");
			_currentHeroConfig.avatarName = value;
			EditorUtility.SetDirty(_currentHeroConfig);
			SaveAvatarAsset(_currentHeroConfig);
			// Don't rebuild UI - just update specific elements if needed
		}

		private void UpdateActiveAvatarDescription(string value)
		{
			if (_currentHeroConfig == null)
				return;

			Undo.RecordObject(_currentHeroConfig, "Change Description");
			_currentHeroConfig.description = value;
			EditorUtility.SetDirty(_currentHeroConfig);
			SaveAvatarAsset(_currentHeroConfig);
		}

		/// <summary>
		/// Explicitly save an AvatarAsset to ensure it persists between Unity sessions.
		/// </summary>
		private void SaveAvatarAsset(AvatarAsset asset)
		{
			if (asset == null)
				return;

			EditorUtility.SetDirty(asset);
			var assetPath = AssetDatabase.GetAssetPath(asset);
			if (!string.IsNullOrEmpty(assetPath))
			{
				// Mark the asset as dirty and save it
				AssetDatabase.SaveAssetIfDirty(asset);
			}
		}

		private void AddActiveAvatarTag(string tag)
		{
			if (_currentHeroConfig == null)
				return;
			if (string.IsNullOrWhiteSpace(tag))
				return;

			_currentHeroConfig.tags ??= new List<string>();
			if (_currentHeroConfig.tags.Contains(tag))
				return;

			Undo.RecordObject(_currentHeroConfig, "Add Avatar Tag");
			_currentHeroConfig.tags.Add(tag);
			EditorUtility.SetDirty(_currentHeroConfig);
			SaveAvatarAsset(_currentHeroConfig);
			// Don't rebuild UI - just update specific elements if needed
		}

		private void RemoveActiveAvatarTag(string tag)
		{
			if (_currentHeroConfig == null || _currentHeroConfig.tags == null)
				return;

			if (!_currentHeroConfig.tags.Remove(tag))
				return;

			Undo.RecordObject(_currentHeroConfig, "Remove Avatar Tag");
			EditorUtility.SetDirty(_currentHeroConfig);
			SaveAvatarAsset(_currentHeroConfig);
			// Don't rebuild UI - just update specific elements if needed
		}

		private void UpdateActiveAvatarVisibility(string value)
		{
			if (_selectedProfile == null || _currentHeroConfig == null)
				return;

			var newStatus = string.Equals(value, "public", StringComparison.OrdinalIgnoreCase)
				? ReleaseStatus.Public
				: ReleaseStatus.Private;

			if (_currentHeroConfig.releaseStatus == newStatus)
				return;

			Undo.RecordObject(_selectedProfile, "Change Release Status");
			_currentHeroConfig.releaseStatus = newStatus;
			EditorUtility.SetDirty(_selectedProfile);
		}

		private void ConfigureProfileToolbar()
		{
			if (_profileToolbarHost == null)
				return;

			_profileToolbarHost.Clear();

			var searchRow = new VisualElement();
			searchRow.AddToClassList("yucp-search-row");
			_profileSearchField = new ToolbarSearchField();
			_profileSearchField.RegisterValueChangedCallback(evt =>
			{
				_profileSearchFilter = string.IsNullOrEmpty(evt.newValue)
					? string.Empty
					: evt.newValue.ToLowerInvariant();
			UpdateProfileList();
			});
			searchRow.Add(_profileSearchField);
			_profileToolbarHost.Add(searchRow);

			var filterRow = new VisualElement();
			filterRow.AddToClassList("yucp-filter-row");
			_filterHasAvatars = new Toggle("Has Avatars") { value = _filterHasAvatarsValue };
			_filterHasAvatars.AddToClassList("yucp-toggle");
			_filterHasAvatars.RegisterValueChangedCallback(evt =>
			{
				_filterHasAvatarsValue = evt.newValue;
				UpdateProfileList();
			});
			filterRow.Add(_filterHasAvatars);

			_filterHasBuilds = new Toggle("Has Builds") { value = _filterHasBuildsValue };
			_filterHasBuilds.AddToClassList("yucp-toggle");
			_filterHasBuilds.RegisterValueChangedCallback(evt =>
			{
				_filterHasBuildsValue = evt.newValue;
				UpdateProfileList();
			});
			filterRow.Add(_filterHasBuilds);
			_profileToolbarHost.Add(filterRow);

			var buttonRow = new VisualElement();
			buttonRow.AddToClassList("yucp-profile-buttons");

			var newButton = new Button(CreateNewProfile) { text = "+ New" };
			newButton.AddToClassList("yucp-button");
			newButton.AddToClassList("yucp-button-action");
			buttonRow.Add(newButton);

			var cloneButton = new Button(CloneSelectedProfile) { text = "Clone" };
			cloneButton.AddToClassList("yucp-button");
			buttonRow.Add(cloneButton);

			var deleteButton = new Button(DeleteSelectedProfile) { text = "Delete" };
			deleteButton.AddToClassList("yucp-button");
			deleteButton.AddToClassList("yucp-button-danger");
			buttonRow.Add(deleteButton);

			_profileToolbarHost.Add(buttonRow);
		}

		private void OnKeyDown(KeyDownEvent evt)
		{
			// Ctrl/Cmd + N: New profile
			if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.N)
			{
				CreateNewProfile();
				evt.StopPropagation();
			}
			// Ctrl/Cmd + D: Duplicate profile
			else if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.D)
			{
				CloneSelectedProfile();
				evt.StopPropagation();
			}
			// Delete: Delete profile
			else if (evt.keyCode == KeyCode.Delete && _selectedProfile != null)
			{
				DeleteSelectedProfile();
				evt.StopPropagation();
			}
			// F5: Refresh
			else if (evt.keyCode == KeyCode.F5)
			{
				ReloadProfiles();
				UpdateProfileList();
				UpdateProfileDetails();
				UpdateBottomBar();
				evt.StopPropagation();
			}
			// Ctrl/Cmd + B: Build
			else if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.B && !_isBuilding)
			{
				BuildSelectedProfile();
				evt.StopPropagation();
			}
		}

		private VisualElement CreateTopBar()
		{
			var topBar = new VisualElement();
			topBar.AddToClassList("yucp-top-bar");
			topBar.AddToClassList("yucp-top-bar");

			// Mobile toggle button (hamburger menu)
			_mobileToggleButton = new Button(ToggleOverlay);
			_mobileToggleButton.text = "☰";
			_mobileToggleButton.AddToClassList("yucp-mobile-toggle");
			topBar.Add(_mobileToggleButton);

			// Logo
			if (_logoTexture != null)
			{
				var logo = new Image
				{
					scaleMode = ScaleMode.ScaleToFit,
					image = _logoTexture
				};
				logo.AddToClassList("yucp-logo");
				logo.AddToClassList("yucp-logo");

				float textureAspect = (float)_logoTexture.width / _logoTexture.height;
				float maxHeight = 50f;
				float calculatedWidth = maxHeight * textureAspect;

				logo.style.width = calculatedWidth;
				logo.style.height = maxHeight;

				topBar.Add(logo);
			}

			// Title
			var title = new Label("Avatar Tools");
			title.AddToClassList("yucp-title");
			title.AddToClassList("yucp-title");
			topBar.Add(title);

			_settingsButton = new Button(OpenSettings)
			{
				tooltip = "Open Avatar Tools settings"
			};
			_settingsButton.AddToClassList("yucp-settings-button");
			
			// Use Unity's built-in settings icon
			var settingsIcon = EditorGUIUtility.IconContent("Settings").image as Texture2D;
			if (settingsIcon != null)
			{
				var iconImage = new Image { image = settingsIcon };
				iconImage.style.width = 16;
				iconImage.style.height = 16;
				_settingsButton.Add(iconImage);
			}
			else
			{
				// Fallback to text if icon not available
				_settingsButton.text = "⚙";
			}
			
			topBar.Add(_settingsButton);

			return topBar;
		}

		private void OpenSettings()
		{
			SettingsService.OpenProjectSettings("Project/YUCP Avatar Tools");
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


		private void UpdateProfileList()
		{
			UpdateProfileListContainer(_profileListContainer);
		}

		private void UpdateProfileListContainer(VisualElement container)
		{
			if (container == null) return;

			container.Clear();

			// Filter profiles
			var filteredProfiles = _profiles.Where(p =>
			{
				if (p == null) return false;

				// Search filter
				if (!string.IsNullOrEmpty(_profileSearchFilter))
				{
					var displayName = GetProfileDisplayName(p)?.ToLowerInvariant() ?? string.Empty;
					if (!displayName.Contains(_profileSearchFilter))
						return false;
				}

				// Has avatars filter
				if (_filterHasAvatarsValue)
				{
					if (p.avatars == null || p.avatars.Count == 0)
						return false;
				}

				// Has builds filter
				if (_filterHasBuildsValue)
				{
					if (p.BuildCount == 0)
						return false;
				}

				return true;
			}).ToList();

			if (filteredProfiles.Count == 0)
			{
				var emptyLabel = new Label(_profiles.Count == 0 ? "No profiles found" : "No profiles match filters");
				emptyLabel.AddToClassList("yucp-label-secondary");
				emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
				emptyLabel.style.paddingTop = 20;
				emptyLabel.style.paddingBottom = 10;
				container.Add(emptyLabel);

				var hintLabel = new Label(_profiles.Count == 0 ? "Create one using the button below" : "Try adjusting your search or filters");
				hintLabel.AddToClassList("yucp-label-small");
				hintLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
				container.Add(hintLabel);
				return;
			}

				for (int i = 0; i < _profiles.Count; i++)
				{
				var profile = _profiles[i];
				if (profile == null) continue;

				// Check if this profile passes filters
				if (!filteredProfiles.Contains(profile))
					continue;

				var profileItem = CreateProfileItem(profile, i);
				container.Add(profileItem);
			}
		}

		private VisualElement CreateProfileItem(AvatarCollection profile, int index)
		{
			var item = new VisualElement();
			item.AddToClassList("yucp-profile-item");

			bool isSelected = index == _selectedIndex;
			if (isSelected)
			{
				item.AddToClassList("yucp-profile-item-selected");
			}

			// Profile name with status indicator
			var nameRow = new VisualElement();
			nameRow.style.flexDirection = FlexDirection.Row;
			nameRow.style.alignItems = Align.Center;

			var nameLabel = new Label(GetProfileDisplayName(profile));
			nameLabel.AddToClassList("yucp-profile-item-name");
			nameLabel.style.flexGrow = 1;
			nameRow.Add(nameLabel);

			// Status indicators
			var avatarCount = profile.avatars?.Count ?? 0;
			if (avatarCount > 0)
			{
				var avatarBadge = new Label($"●");
				avatarBadge.style.fontSize = 8;
				avatarBadge.style.color = new Color(0.212f, 0.749f, 0.694f); // YUCP Teal
				avatarBadge.style.marginLeft = 4;
				avatarBadge.tooltip = $"{avatarCount} avatar(s)";
				nameRow.Add(avatarBadge);
			}

			if (profile.BuildCount > 0)
			{
				var buildBadge = new Label("✓");
				buildBadge.style.fontSize = 10;
				buildBadge.style.color = new Color(0.212f, 0.749f, 0.694f); // YUCP Teal
				buildBadge.style.marginLeft = 4;
				buildBadge.tooltip = $"Built {profile.BuildCount} time(s)";
				nameRow.Add(buildBadge);
			}

			item.Add(nameRow);

			// Profile info
			var infoText = $"{avatarCount} avatar(s)";
			if (!string.IsNullOrEmpty(profile.LastBuildTime))
			{
				infoText += $" • Built {profile.LastBuildTime}";
			}
			var infoLabel = new Label(infoText);
			infoLabel.AddToClassList("yucp-profile-item-info");
			item.Add(infoLabel);

			// Click handler
			item.RegisterCallback<MouseDownEvent>(evt =>
			{
				if (evt.button == 0) // Left click
				{
					_selectedIndex = index;
					_selectedProfile = profile;
					UpdateProfileList();
					UpdateProfileDetails();
					SaveSelectedAvatar();
					UpdateBottomBar();
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

		private void UpdateProfileDetails()
		{
			if (_selectedProfile == null)
			{
				if (_collectionHeaderHost != null)
					_collectionHeaderHost.style.display = DisplayStyle.None;
				if (_avatarGridHost != null)
					_avatarGridHost.style.display = DisplayStyle.None;
				if (_avatarDetailsHost != null)
					_avatarDetailsHost.style.display = DisplayStyle.None;
				return;
			}

			var profile = _selectedProfile;

			// Populate all avatars from PipelineManager when collection is loaded
			if (profile.avatars != null)
			{
				// Don't populate from PipelineManager here - it's expensive and causes lag
				// Only populate when avatar is selected (in ShowAvatarDetails)
			}

			// Build collection header with batch operations
			BuildCollectionHeader(profile);

			// Build avatar grid as primary view
			BuildAvatarGridView(profile);

			// Update bulk edit panel
			UpdateBulkEditPanel(profile);

			// Hide avatar details panel by default (shown when avatar is clicked)
			if (_avatarDetailsHost != null)
			{
				_avatarDetailsHost.style.display = DisplayStyle.None;
			}
		}

		private void BuildCollectionHeader(AvatarCollection profile)
		{
			if (_collectionHeaderHost == null)
				return;

			_collectionHeaderHost.Clear();

			// Main card container
			var headerCard = new VisualElement();
			headerCard.AddToClassList("yucp-collection-header-card");

			// Header section with name and quick actions
			var headerSection = new VisualElement();
			headerSection.AddToClassList("yucp-collection-header-section");

			// Left: Collection name and info
			var headerLeft = new VisualElement();
			headerLeft.AddToClassList("yucp-collection-header-left");

			// Collection name with edit capability
			var nameContainer = new VisualElement();
			nameContainer.AddToClassList("yucp-collection-name-container");

			var nameField = new TextField { value = profile.collectionName ?? profile.name };
			nameField.AddToClassList("yucp-collection-name-field");
			nameField.RegisterValueChangedCallback(evt =>
			{
				if (profile != null)
				{
					Undo.RecordObject(profile, "Change Collection Name");
					profile.collectionName = evt.newValue;
					EditorUtility.SetDirty(profile);
					UpdateProfileList();
				}
			});
			nameContainer.Add(nameField);

			headerLeft.Add(nameContainer);

			// Stats badges row
			int totalAvatars = profile.avatars?.Count ?? 0;
			int selectedCount = _selectedAvatarIndices.Count;
			int builtCount = 0;
			int publicCount = 0;
			int pcCount = 0;
			int questCount = 0;

			foreach (var avatar in profile.avatars ?? new List<AvatarAsset>())
			{
				if (avatar == null) continue;
				if (!string.IsNullOrEmpty(avatar.blueprintIdPC) || !string.IsNullOrEmpty(avatar.blueprintIdQuest))
					builtCount++;
				if (avatar.releaseStatus == ReleaseStatus.Public)
					publicCount++;
				if (avatar.buildPC)
					pcCount++;
				if (avatar.buildQuest)
					questCount++;
			}

			var statsContainer = new VisualElement();
			statsContainer.AddToClassList("yucp-collection-stats");

			CreateStatBadge(statsContainer, totalAvatars.ToString(), "Avatars", totalAvatars > 0);
			if (selectedCount > 0)
				CreateStatBadge(statsContainer, selectedCount.ToString(), "Selected", true, true);
			CreateStatBadge(statsContainer, builtCount.ToString(), "Built", builtCount > 0);
			CreateStatBadge(statsContainer, publicCount.ToString(), "Public", publicCount > 0);
			CreateStatBadge(statsContainer, $"{pcCount}/{questCount}", "PC/Quest", pcCount > 0 || questCount > 0);

			headerLeft.Add(statsContainer);
			headerSection.Add(headerLeft);
			headerCard.Add(headerSection);

			// Build actions section
			var actionsCard = new VisualElement();
			actionsCard.AddToClassList("yucp-collection-actions-card");

			var actionsHeader = new VisualElement();
			actionsHeader.AddToClassList("yucp-collection-actions-header");

			bool hasSelection = selectedCount > 0;
			bool hasAvatars = totalAvatars > 0;

			var actionsTitle = new Label(hasSelection 
				? $"Build Actions ({selectedCount} selected)"
				: hasAvatars 
					? $"Build Actions ({totalAvatars} total)"
					: "Build Actions");
			actionsTitle.AddToClassList("yucp-collection-actions-title");
			actionsHeader.Add(actionsTitle);

			var actionsDescription = new Label(hasSelection
				? "Build, test, or publish the selected avatars"
				: hasAvatars
					? "Build, test, or publish all avatars in this collection"
					: "Add avatars to this collection to enable build actions");
			actionsDescription.AddToClassList("yucp-collection-actions-description");
			actionsHeader.Add(actionsDescription);

			actionsCard.Add(actionsHeader);

			// Action buttons
			var actionButtons = new VisualElement();
			actionButtons.AddToClassList("yucp-collection-action-buttons");

			var buildButton = new Button(() =>
			{
				if (hasSelection)
					BuildSelectedAvatars(profile, BuildWorkflow.BuildOnly);
				else
					BuildAllAvatars(profile);
			})
			{
				text = "Build"
			};
			buildButton.AddToClassList("yucp-button");
			buildButton.AddToClassList("yucp-button-large");
			buildButton.tooltip = "Build avatars without uploading to VRChat";
			buildButton.SetEnabled(!_isBuilding && (hasSelection || hasAvatars));
			actionButtons.Add(buildButton);

			var testButton = new Button(() =>
			{
				if (hasSelection)
					BuildSelectedAvatars(profile, BuildWorkflow.TestOnly);
				else
					TestAllAvatars(profile);
			})
			{
				text = "Test"
			};
			testButton.AddToClassList("yucp-button");
			testButton.AddToClassList("yucp-button-large");
			testButton.tooltip = "Build and upload as test (visible only to you)";
			testButton.SetEnabled(!_isBuilding && (hasSelection || hasAvatars));
			actionButtons.Add(testButton);

			var publishButton = new Button(() =>
			{
				if (hasSelection)
					BuildSelectedAvatars(profile, BuildWorkflow.Publish);
				else
					PublishAllAvatars(profile);
			})
			{
				text = "Publish"
			};
			publishButton.AddToClassList("yucp-button");
			publishButton.AddToClassList("yucp-button-primary");
			publishButton.AddToClassList("yucp-button-large");
			publishButton.tooltip = "Build and publish to VRChat (public)";
			publishButton.SetEnabled(!_isBuilding && (hasSelection || hasAvatars));
			actionButtons.Add(publishButton);

			actionsCard.Add(actionButtons);
			headerCard.Add(actionsCard);

			_collectionHeaderHost.Add(headerCard);
		}

		private void CreateStatBadge(VisualElement container, string value, string label, bool isActive, bool isSelected = false)
		{
			var badge = new VisualElement();
			badge.AddToClassList("yucp-collection-stat-badge");
			if (isSelected)
				badge.AddToClassList("yucp-collection-stat-badge-selected");
			else if (!isActive)
				badge.AddToClassList("yucp-collection-stat-badge-inactive");

			var valueLabel = new Label(value);
			valueLabel.AddToClassList("yucp-collection-stat-value");
			badge.Add(valueLabel);

			var labelLabel = new Label(label);
			labelLabel.AddToClassList("yucp-collection-stat-label");
			badge.Add(labelLabel);

			container.Add(badge);
		}

		private void UpdateBulkEditPanel(AvatarCollection profile)
		{
			if (_bulkEditPanel == null || profile == null)
				return;

			_bulkEditPanel.Clear();

			if (_selectedAvatarIndices.Count < 2)
			{
				_bulkEditPanel.style.display = DisplayStyle.None;
				_bulkEditPanel.style.opacity = 0;
				return;
			}

			_bulkEditPanel.style.display = DisplayStyle.Flex;
			_bulkEditPanel.style.opacity = 1;

			var panel = new VisualElement();
			panel.AddToClassList("yucp-bulk-edit-panel");
			panel.style.opacity = 1;

			// Header with icon and count badge
			var header = new VisualElement();
			header.AddToClassList("yucp-bulk-edit-header");
			header.style.flexDirection = FlexDirection.Row;
			header.style.alignItems = Align.Center;
			header.style.justifyContent = Justify.SpaceBetween;
			header.style.marginBottom = 20;

			var headerLeft = new VisualElement();
			headerLeft.style.flexDirection = FlexDirection.Column;

			var title = new Label("Bulk Edit");
			title.AddToClassList("yucp-bulk-edit-title");
			headerLeft.Add(title);

			var subtitle = new Label($"Editing {_selectedAvatarIndices.Count} avatar{(_selectedAvatarIndices.Count > 1 ? "s" : "")}");
			subtitle.AddToClassList("yucp-bulk-edit-subtitle");
			headerLeft.Add(subtitle);

			header.Add(headerLeft);

			var closeButton = new Button(() =>
			{
				// Animate out before clearing
				_bulkEditPanel.style.opacity = 0;
				_bulkEditPanel.schedule.Execute(() =>
				{
					_selectedAvatarIndices.Clear();
					UpdateBulkEditPanel(profile);
					BuildCollectionHeader(profile);
					BuildAvatarGridView(profile);
				}).StartingIn(200);
			})
			{ text = "×" };
			closeButton.AddToClassList("yucp-bulk-edit-close");
			header.Add(closeButton);

			panel.Add(header);

			// Get selected avatars
			var selectedAvatars = new List<AvatarAsset>();
			foreach (var index in _selectedAvatarIndices)
			{
				if (index >= 0 && index < profile.avatars.Count && profile.avatars[index] != null)
				{
					selectedAvatars.Add(profile.avatars[index]);
				}
			}

			if (selectedAvatars.Count == 0)
			{
				_bulkEditPanel.style.display = DisplayStyle.None;
				return;
			}

			// Calculate current states
			int pcEnabledCount = 0;
			int questEnabledCount = 0;
			int publicCount = 0;
			int privateCount = 0;
			
			foreach (var avatar in selectedAvatars)
			{
				if (avatar.buildPC) pcEnabledCount++;
				if (avatar.buildQuest) questEnabledCount++;
				if (avatar.releaseStatus == ReleaseStatus.Public) publicCount++;
				else privateCount++;
			}
			
			bool allPCEnabled = pcEnabledCount == selectedAvatars.Count;
			bool allPCDisabled = pcEnabledCount == 0;
			bool allQuestEnabled = questEnabledCount == selectedAvatars.Count;
			bool allQuestDisabled = questEnabledCount == 0;
			bool allPublic = publicCount == selectedAvatars.Count;
			bool allPrivate = privateCount == selectedAvatars.Count;

			// Vertical layout to prevent overflow
			var contentContainer = new VisualElement();
			contentContainer.style.flexDirection = FlexDirection.Column;
			contentContainer.style.width = Length.Percent(100);

			// Platform settings section
			var platformSection = CreateBulkEditSection("Platform Selection", $"Configure build platforms for {selectedAvatars.Count} avatar(s)");
			
			var pcToggle = new Toggle("PC");
			pcToggle.value = allPCEnabled;
			pcToggle.AddToClassList("yucp-bulk-toggle");
			if (!allPCEnabled && !allPCDisabled)
			{
				pcToggle.AddToClassList("yucp-bulk-toggle-mixed");
			}
			pcToggle.RegisterValueChangedCallback(evt =>
			{
				BulkSetPlatform(selectedAvatars, PlatformSwitcher.BuildPlatform.PC, evt.newValue);
				BuildAvatarGridView(profile);
				UpdateBulkEditPanel(profile);
			});
			
			var pcStatusLabel = new Label(allPCEnabled ? "All enabled" : allPCDisabled ? "All disabled" : $"{pcEnabledCount}/{selectedAvatars.Count} enabled");
			pcStatusLabel.AddToClassList("yucp-bulk-status-label");
			pcStatusLabel.style.marginLeft = 8;
			
			var pcRow = new VisualElement();
			pcRow.style.flexDirection = FlexDirection.Row;
			pcRow.style.alignItems = Align.Center;
			pcRow.style.marginBottom = 12;
			pcRow.style.width = Length.Percent(100);
			pcRow.style.minWidth = 0;
			pcRow.style.overflow = Overflow.Hidden;
			pcRow.Add(pcToggle);
			pcStatusLabel.style.flexShrink = 1;
			pcStatusLabel.style.minWidth = 0;
			pcRow.Add(pcStatusLabel);
			platformSection.Add(pcRow);

			var questToggle = new Toggle("Quest");
			questToggle.value = allQuestEnabled;
			questToggle.AddToClassList("yucp-bulk-toggle");
			if (!allQuestEnabled && !allQuestDisabled)
			{
				questToggle.AddToClassList("yucp-bulk-toggle-mixed");
			}
			questToggle.RegisterValueChangedCallback(evt =>
			{
				BulkSetPlatform(selectedAvatars, PlatformSwitcher.BuildPlatform.Quest, evt.newValue);
				BuildAvatarGridView(profile);
				UpdateBulkEditPanel(profile);
			});
			
			var questStatusLabel = new Label(allQuestEnabled ? "All enabled" : allQuestDisabled ? "All disabled" : $"{questEnabledCount}/{selectedAvatars.Count} enabled");
			questStatusLabel.AddToClassList("yucp-bulk-status-label");
			questStatusLabel.style.marginLeft = 8;
			
			var questRow = new VisualElement();
			questRow.style.flexDirection = FlexDirection.Row;
			questRow.style.alignItems = Align.Center;
			questRow.style.width = Length.Percent(100);
			questRow.style.minWidth = 0;
			questRow.style.overflow = Overflow.Hidden;
			questRow.Add(questToggle);
			questStatusLabel.style.flexShrink = 1;
			questStatusLabel.style.minWidth = 0;
			questRow.Add(questStatusLabel);
			platformSection.Add(questRow);
			
			contentContainer.Add(platformSection);

			// Release status section
			var visibilitySection = CreateBulkEditSection("Release Status", $"Set visibility for {selectedAvatars.Count} avatar(s)");
			
			var publicToggle = new Toggle("Public");
			publicToggle.value = allPublic;
			publicToggle.AddToClassList("yucp-bulk-toggle");
			if (!allPublic && !allPrivate)
			{
				publicToggle.AddToClassList("yucp-bulk-toggle-mixed");
			}
			publicToggle.RegisterValueChangedCallback(evt =>
			{
				if (evt.newValue)
				{
					BulkSetReleaseStatus(selectedAvatars, ReleaseStatus.Public);
				}
				else
				{
					BulkSetReleaseStatus(selectedAvatars, ReleaseStatus.Private);
				}
				BuildAvatarGridView(profile);
				UpdateBulkEditPanel(profile);
			});
			
			var visibilityStatusLabel = new Label(allPublic ? "All public" : allPrivate ? "All private" : $"{publicCount} public, {privateCount} private");
			visibilityStatusLabel.AddToClassList("yucp-bulk-status-label");
			visibilityStatusLabel.style.marginLeft = 8;
			
			var visibilityRow = new VisualElement();
			visibilityRow.style.flexDirection = FlexDirection.Row;
			visibilityRow.style.alignItems = Align.Center;
			visibilityRow.style.width = Length.Percent(100);
			visibilityRow.style.minWidth = 0;
			visibilityRow.style.overflow = Overflow.Hidden;
			visibilityRow.Add(publicToggle);
			visibilityStatusLabel.style.flexShrink = 1;
			visibilityStatusLabel.style.minWidth = 0;
			visibilityRow.Add(visibilityStatusLabel);
			visibilitySection.Add(visibilityRow);
			
			contentContainer.Add(visibilitySection);

			// Tags section
			var tagsSection = CreateBulkEditSection("Tags", "Add or remove tags from all selected avatars");
			
			var tagsInputRow = new VisualElement();
			tagsInputRow.style.flexDirection = FlexDirection.Row;
			tagsInputRow.style.alignItems = Align.Center;
			tagsInputRow.style.marginTop = 8;
			tagsInputRow.style.width = Length.Percent(100);
			tagsInputRow.style.minWidth = 0;
			tagsInputRow.style.overflow = Overflow.Hidden;

			var tagInput = new TextField();
			tagInput.AddToClassList("yucp-input");
			tagInput.style.flexGrow = 1;
			tagInput.style.flexShrink = 1;
			tagInput.style.marginRight = 8;
			tagInput.style.minWidth = 0;
			tagInput.style.maxWidth = Length.Percent(100);
			
			// Add placeholder
			var placeholderLabel = new Label("Enter tag name...");
			placeholderLabel.AddToClassList("yucp-input-placeholder");
			placeholderLabel.style.position = Position.Absolute;
			placeholderLabel.style.left = 8;
			placeholderLabel.style.top = 4;
			placeholderLabel.pickingMode = PickingMode.Ignore;
			tagInput.style.position = Position.Relative;
			tagInput.Add(placeholderLabel);
			
			tagInput.RegisterValueChangedCallback(evt =>
			{
				placeholderLabel.style.display = string.IsNullOrEmpty(evt.newValue) ? DisplayStyle.Flex : DisplayStyle.None;
			});
			tagInput.RegisterCallback<FocusInEvent>(_ => placeholderLabel.style.display = DisplayStyle.None);
			tagInput.RegisterCallback<FocusOutEvent>(_ => placeholderLabel.style.display = string.IsNullOrEmpty(tagInput.value) ? DisplayStyle.Flex : DisplayStyle.None);
			placeholderLabel.style.display = string.IsNullOrEmpty(tagInput.value) ? DisplayStyle.Flex : DisplayStyle.None;
			
			tagInput.RegisterCallback<KeyDownEvent>(evt =>
			{
				if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
				{
					var tag = tagInput.value?.Trim();
					if (!string.IsNullOrEmpty(tag))
					{
						BulkAddTag(selectedAvatars, tag);
						tagInput.value = string.Empty;
						BuildAvatarGridView(profile);
						UpdateBulkEditPanel(profile);
					}
					evt.StopPropagation();
				}
			});
			tagsInputRow.Add(tagInput);

			var addTagButton = new Button(() =>
			{
				var tag = tagInput.value?.Trim();
				if (!string.IsNullOrEmpty(tag))
				{
					BulkAddTag(selectedAvatars, tag);
					tagInput.value = string.Empty;
					BuildAvatarGridView(profile);
					UpdateBulkEditPanel(profile);
				}
			}) { text = "Add" };
			addTagButton.AddToClassList("yucp-button");
			addTagButton.AddToClassList("yucp-button-action");
			addTagButton.AddToClassList("yucp-button-small");
			addTagButton.style.marginRight = 8;
			addTagButton.style.flexShrink = 0;
			addTagButton.style.minWidth = 60;
			tagsInputRow.Add(addTagButton);

			var removeTagButton = new Button(() =>
			{
				var tag = tagInput.value?.Trim();
				if (!string.IsNullOrEmpty(tag))
				{
					BulkRemoveTag(selectedAvatars, tag);
					tagInput.value = string.Empty;
					BuildAvatarGridView(profile);
					UpdateBulkEditPanel(profile);
				}
			}) { text = "Remove" };
			removeTagButton.AddToClassList("yucp-button");
			removeTagButton.AddToClassList("yucp-button-small");
			removeTagButton.style.flexShrink = 0;
			removeTagButton.style.minWidth = 60;
			tagsInputRow.Add(removeTagButton);
			
			tagsSection.Add(tagsInputRow);
			contentContainer.Add(tagsSection);

			panel.Add(contentContainer);
			_bulkEditPanel.Add(panel);
		}

		private VisualElement CreateBulkEditSection(string title, string description)
		{
			var section = new VisualElement();
			section.AddToClassList("yucp-bulk-edit-section");
			section.style.marginBottom = 20;
			section.style.width = Length.Percent(100);

			var sectionHeader = new VisualElement();
			sectionHeader.style.marginBottom = 12;

			var sectionTitle = new Label(title);
			sectionTitle.AddToClassList("yucp-bulk-edit-section-title");
			sectionHeader.Add(sectionTitle);

			var sectionDescription = new Label(description);
			sectionDescription.AddToClassList("yucp-bulk-edit-section-description");
			sectionHeader.Add(sectionDescription);

			section.Add(sectionHeader);
			return section;
		}

		private void BulkSetPlatform(List<AvatarAsset> avatars, PlatformSwitcher.BuildPlatform platform, bool enable)
		{
			foreach (var avatar in avatars)
			{
				if (avatar == null) continue;

				Undo.RecordObject(avatar, $"Bulk {(enable ? "Enable" : "Disable")} {platform}");
				if (platform == PlatformSwitcher.BuildPlatform.PC)
				{
					avatar.buildPC = enable;
				}
				else
				{
					avatar.buildQuest = enable;
				}
				EditorUtility.SetDirty(avatar);
			}
		}

		private void BulkSetReleaseStatus(List<AvatarAsset> avatars, ReleaseStatus status)
		{
			foreach (var avatar in avatars)
			{
				if (avatar == null) continue;

				Undo.RecordObject(avatar, "Bulk Set Release Status");
				avatar.releaseStatus = status;
				EditorUtility.SetDirty(avatar);
			}
		}

		private void BulkAddTag(List<AvatarAsset> avatars, string tag)
		{
			foreach (var avatar in avatars)
			{
				if (avatar == null) continue;

				Undo.RecordObject(avatar, "Bulk Add Tag");
				if (avatar.tags == null)
					avatar.tags = new List<string>();
				
				if (!avatar.tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
				{
					avatar.tags.Add(tag);
				}
				EditorUtility.SetDirty(avatar);
			}
		}

		private void BulkRemoveTag(List<AvatarAsset> avatars, string tag)
		{
			foreach (var avatar in avatars)
			{
				if (avatar == null || avatar.tags == null) continue;

				Undo.RecordObject(avatar, "Bulk Remove Tag");
				avatar.tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
				EditorUtility.SetDirty(avatar);
			}
		}

		private async void BuildSelectedAvatars(AvatarCollection profile, BuildWorkflow workflow)
		{
			if (profile == null || _selectedAvatarIndices.Count == 0)
				return;

			var selectedAvatars = new List<AvatarAsset>();
			foreach (var index in _selectedAvatarIndices)
			{
				if (index >= 0 && index < profile.avatars.Count && profile.avatars[index] != null)
				{
					selectedAvatars.Add(profile.avatars[index]);
				}
			}

			if (selectedAvatars.Count == 0)
			{
				_toast?.ShowWarning("No valid avatars selected.", "No Selection", 3f);
				return;
			}

			if (workflow == BuildWorkflow.Publish)
			{
				// Check for validation errors before proceeding
				var validationErrors = GetValidationErrors();
				if (validationErrors.Count > 0)
				{
					var errorMessage = "Cannot publish due to validation errors:\n\n" + string.Join("\n", validationErrors.Select(e => $"• {e}"));
					EditorUtility.DisplayDialog("Validation Errors", errorMessage + "\n\nPlease fix these issues in the Validation section above.", "OK");
					return;
				}

				try
				{
					EnsureUploadDisclaimerAcknowledged();
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}

			// Remember avatar positions
			_avatarPositions.Clear();
			for (int i = 0; i < profile.avatars.Count; i++)
			{
				if (profile.avatars[i] != null)
				{
					_avatarPositions[profile.avatars[i]] = (profile, i);
				}
			}

			_isBuilding = true;
			string actionName = workflow == BuildWorkflow.Publish ? "Publishing" : workflow == BuildWorkflow.TestOnly ? "Testing" : "Building";
			UpdateProgress(0f, $"{actionName} {selectedAvatars.Count} avatar(s)...");
			UpdateBottomBar();

			int dispatched = 0;
			int total = selectedAvatars.Count;
			for (int i = 0; i < selectedAvatars.Count; i++)
			{
				var cfg = selectedAvatars[i];
				UpdateProgress((float)i / total, $"{actionName} {cfg.avatarName ?? cfg.avatarPrefab?.name ?? "Avatar"} ({i + 1}/{total})...");

				if (cfg.buildPC && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.PC, workflow))
					dispatched++;
				if (cfg.buildQuest && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.Quest, workflow))
					dispatched++;
			}

			_isBuilding = false;
			UpdateProgress(0f, string.Empty);
			UpdateBottomBar();

			if (dispatched == 0)
			{
				_toast?.ShowWarning($"No {workflow.ToString().ToLower()} operations were dispatched. Make sure avatars have platforms enabled.", "No Operations", 4f);
			}
			else
			{
				_toast?.ShowSuccess($"Queued {dispatched} {workflow.ToString().ToLower()}(s) for {selectedAvatars.Count} avatar(s).", "Operations Queued", 3f);
			}
		}

		private void BuildAvatarGridView(AvatarCollection profile)
		{
			if (_avatarGridHost == null)
				return;

			_avatarGridHost.Clear();

			var section = new VisualElement();
			section.AddToClassList("yucp-section");
			section.AddToClassList("yucp-avatar-grid-section");

			var headerRow = new VisualElement();
			headerRow.style.flexDirection = FlexDirection.Row;
			headerRow.style.alignItems = Align.Center;
			headerRow.style.justifyContent = Justify.SpaceBetween;
			headerRow.style.marginBottom = 12;

			var titleContainer = new VisualElement();
			titleContainer.style.flexDirection = FlexDirection.Row;
			titleContainer.style.alignItems = Align.Center;

			var title = new Label("Avatars");
			title.AddToClassList("yucp-section-title");
			title.style.marginBottom = 0;
			titleContainer.Add(title);

			// Selection info
			if (_selectedAvatarIndices.Count > 0)
			{
				var selectionInfo = new Label($"{_selectedAvatarIndices.Count} selected");
				selectionInfo.AddToClassList("yucp-label-secondary");
				selectionInfo.style.marginLeft = 12;
				titleContainer.Add(selectionInfo);
			}

			headerRow.Add(titleContainer);

			var headerActions = new VisualElement();
			headerActions.style.flexDirection = FlexDirection.Row;

			// Select All / Deselect All buttons
			if (profile.avatars != null && profile.avatars.Count > 0)
			{
				var selectAllButton = new Button(() =>
				{
					_selectedAvatarIndices.Clear();
					for (int i = 0; i < profile.avatars.Count; i++)
					{
						if (profile.avatars[i] != null)
							_selectedAvatarIndices.Add(i);
					}
					BuildAvatarGridView(profile);
					UpdateBulkEditPanel(profile);
				})
				{ text = "Select All" };
				selectAllButton.AddToClassList("yucp-button");
				selectAllButton.AddToClassList("yucp-button-small");
				selectAllButton.style.marginRight = 8;
				headerActions.Add(selectAllButton);

				var deselectAllButton = new Button(() =>
				{
					_selectedAvatarIndices.Clear();
					BuildAvatarGridView(profile);
					UpdateBulkEditPanel(profile);
				})
				{ text = "Deselect All" };
				deselectAllButton.AddToClassList("yucp-button");
				deselectAllButton.AddToClassList("yucp-button-small");
				deselectAllButton.style.marginRight = 8;
				headerActions.Add(deselectAllButton);
			}

			var addButton = new Button(() => AddAvatarToProfile(profile)) { text = "+ Add Avatar" };
			addButton.AddToClassList("yucp-button");
			addButton.AddToClassList("yucp-button-action");
			addButton.AddToClassList("yucp-button-small");
			addButton.style.marginRight = 8;
			headerActions.Add(addButton);

			var refreshButton = new Button(() => BuildAvatarGridView(profile)) { text = "Refresh" };
			refreshButton.AddToClassList("yucp-button");
			refreshButton.AddToClassList("yucp-button-small");
			headerActions.Add(refreshButton);

			headerRow.Add(headerActions);
			section.Add(headerRow);

			// Avatar grid
			_avatarGridScroll = new ScrollView(ScrollViewMode.Vertical);
			_avatarGridScroll.AddToClassList("yucp-store-grid-scroll");
			_avatarGridContent = new VisualElement();
			_avatarGridContent.AddToClassList("yucp-store-grid");
			_avatarGridScroll.Add(_avatarGridContent);

			if (profile.avatars == null || profile.avatars.Count == 0)
			{
				var emptyState = new VisualElement();
				emptyState.AddToClassList("yucp-avatar-grid-empty");
				var emptyTitle = new Label("No avatars yet");
				emptyTitle.AddToClassList("yucp-empty-state-title");
				emptyState.Add(emptyTitle);
				var emptyDesc = new Label("Use + Add Avatar to include prefabs in this collection.");
				emptyDesc.AddToClassList("yucp-empty-state-description");
				emptyState.Add(emptyDesc);
				_avatarGridContent.Add(emptyState);
			}
			else
			{
				// Dispose all existing preview renderers before clearing to prevent leaks
				if (_avatarGridContent != null)
				{
					foreach (var child in _avatarGridContent.Children().ToList())
					{
						if (child is VisualElement tile)
						{
							// Find and dispose any AvatarTilePreviewRenderer instances
							var previewRenderer = tile.Q<AvatarTilePreviewRenderer>();
							if (previewRenderer != null)
							{
								previewRenderer.Dispose();
							}
						}
					}
					_avatarGridContent.Clear();
				}
				
				for (int i = 0; i < profile.avatars.Count; i++)
				{
					var avatarConfig = profile.avatars[i];
					if (avatarConfig == null)
						continue;

					// Don't populate from PipelineManager here - it's expensive and causes duplicates
					// Only populate when avatar is selected (in ShowAvatarDetails) or when Control Panel selects it
					EnsureGalleryList(avatarConfig);
					EnsureGalleryLoaded(profile, avatarConfig);

					var tile = CreateAvatarTile(profile, avatarConfig, i);
					if (i == _selectedAvatarIndex)
					{
						tile.AddToClassList("yucp-avatar-tile-selected");
					}
					_avatarGridContent.Add(tile);
				}
			}

			section.Add(_avatarGridScroll);
			_avatarGridHost.Add(section);
		}

		private VisualElement CreateProfileSettingsSection(AvatarCollection profile)
		{
			var section = new VisualElement();
			section.AddToClassList("yucp-section");
			section.AddToClassList("yucp-section");

			var title = new Label("Profile Settings");
			title.AddToClassList("yucp-section-title");
			title.AddToClassList("yucp-section-title");
			section.Add(title);

			// Profile Name
			var nameRow = CreateFormRow("Profile Name");
			var nameField = new TextField { value = profile.collectionName };
			nameField.focusable = true;
			nameField.isReadOnly = false;
			nameField.SetEnabled(true);
			nameField.AddToClassList("yucp-input");
			nameField.AddToClassList("yucp-form-field");
			nameField.RegisterValueChangedCallback(evt =>
			{
				if (profile != null)
				{
					Undo.RecordObject(profile, "Change Profile Name");
					profile.collectionName = evt.newValue;
					EditorUtility.SetDirty(profile);
					UpdateProfileList();
				}
			});
			nameRow.Add(nameField);
			section.Add(nameRow);

			return section;
		}

		private VisualElement CreateBuildSettingsSection(AvatarCollection profile)
		{
			var section = new VisualElement();
			section.AddToClassList("yucp-section");
			section.AddToClassList("yucp-section");

			var title = new Label("Build Settings");
			title.AddToClassList("yucp-section-title");
			title.AddToClassList("yucp-section-title");
			section.Add(title);

			// Auto Build PC
			var pcRow = CreateFormRow("Auto Build PC");
			var pcToggle = new Toggle { value = profile.autoBuildPC };
			pcToggle.AddToClassList("yucp-toggle");
			pcToggle.AddToClassList("yucp-form-field");
			pcToggle.RegisterValueChangedCallback(evt =>
			{
				if (profile != null)
				{
					Undo.RecordObject(profile, "Change Auto Build PC");
					profile.autoBuildPC = evt.newValue;
					EditorUtility.SetDirty(profile);
				}
			});
			pcRow.Add(pcToggle);
			section.Add(pcRow);

			// Auto Build Quest
			var questRow = CreateFormRow("Auto Build Quest");
			var questToggle = new Toggle { value = profile.autoBuildQuest };
			questToggle.AddToClassList("yucp-toggle");
			questToggle.AddToClassList("yucp-form-field");
			questToggle.RegisterValueChangedCallback(evt =>
			{
				if (profile != null)
				{
					Undo.RecordObject(profile, "Change Auto Build Quest");
					profile.autoBuildQuest = evt.newValue;
					EditorUtility.SetDirty(profile);
				}
			});
			questRow.Add(questToggle);
			section.Add(questRow);

			return section;
		}

		private VisualElement CreateAvatarGridSection(AvatarCollection profile)
		{
			var section = new VisualElement();
			section.AddToClassList("yucp-section");
			section.AddToClassList("yucp-store-section");
			section.AddToClassList("yucp-section");

			var headerRow = new VisualElement();
			headerRow.AddToClassList("yucp-store-header");
			headerRow.AddToClassList("yucp-section-header");
			headerRow.style.flexDirection = FlexDirection.Row;
			headerRow.style.justifyContent = Justify.SpaceBetween;
			headerRow.style.alignItems = Align.Center;
			headerRow.style.marginBottom = 12;

			var title = new Label("Avatars");
			title.AddToClassList("yucp-section-title");
			title.style.marginBottom = 0;
			headerRow.Add(title);

			var headerActions = new VisualElement();
			headerActions.AddToClassList("yucp-store-header-actions");

			var addButton = new Button(() => AddAvatarToProfile(profile)) { text = "+ Add Avatar" };
			addButton.AddToClassList("yucp-button");
			addButton.AddToClassList("yucp-button-action");
			addButton.AddToClassList("yucp-button-small");
			headerActions.Add(addButton);

			var refreshButton = new Button(() => BuildAvatarGridView(profile)) { text = "Refresh" };
			refreshButton.AddToClassList("yucp-button");
			refreshButton.AddToClassList("yucp-button-small");
			headerActions.Add(refreshButton);

			headerRow.Add(headerActions);
			section.Add(headerRow);

			_heroPanel = new VisualElement();
			_heroPanel.AddToClassList("yucp-hero-panel");

			var heroContent = new VisualElement();
			heroContent.AddToClassList("yucp-hero-content");

			var heroPreviewContainer = new VisualElement();
			heroPreviewContainer.AddToClassList("yucp-hero-preview-container");

			_heroImageDisplay = new Image();
			_heroImageDisplay.AddToClassList("yucp-hero-gallery-image");
			heroPreviewContainer.Add(_heroImageDisplay);

			_heroOverlay = new VisualElement();
			_heroOverlay.AddToClassList("yucp-hero-overlay");
			_heroSlideLabel = new Label();
			_heroSlideLabel.AddToClassList("yucp-hero-slide-label");
			_heroOverlay.Add(_heroSlideLabel);

			_heroPrevButton = new Button(() => CycleHeroSlide(-1)) { text = "‹" };
			_heroPrevButton.AddToClassList("yucp-hero-slide-button");
			_heroPrevButton.AddToClassList("yucp-hero-slide-prev");
			_heroOverlay.Add(_heroPrevButton);

			var captureButton = new Button(() => CaptureThumbnailFromScene()) { text = "Capture" };
			captureButton.tooltip = "Capture thumbnail from scene view";
			captureButton.AddToClassList("yucp-hero-slide-button");
			captureButton.style.position = Position.Absolute;
			captureButton.style.bottom = 12;
			captureButton.style.left = 12;
			_heroOverlay.Add(captureButton);

			var uploadButton = new Button(() => UploadThumbnailImage()) { text = "Upload" };
			uploadButton.tooltip = "Upload thumbnail image file";
			uploadButton.AddToClassList("yucp-hero-slide-button");
			uploadButton.style.position = Position.Absolute;
			uploadButton.style.bottom = 12;
			uploadButton.style.left = 60;
			_heroOverlay.Add(uploadButton);

			_heroAddButton = new Button(() => AddGalleryImage(profile, _currentHeroConfig)) { text = "+" };
			_heroAddButton.tooltip = "Enable gallery integration and store a VRChat API key in Avatar Tools settings to add gallery images.";
			_heroAddButton.AddToClassList("yucp-hero-slide-button");
			_heroAddButton.AddToClassList("yucp-hero-slide-add");
			_heroOverlay.Add(_heroAddButton);

			_heroSetIconButton = new Button(() => SetActiveGalleryImageAsIcon(profile, _currentHeroConfig)) { text = "Set Icon" };
			_heroSetIconButton.tooltip = "Make the selected gallery image the avatar icon via the VRChat API.";
			_heroSetIconButton.AddToClassList("yucp-hero-slide-button");
			_heroSetIconButton.AddToClassList("yucp-hero-slide-seticon");
			_heroSetIconButton.style.display = DisplayStyle.None;
			_heroOverlay.Add(_heroSetIconButton);

			_heroDeleteButton = new Button(() => DeleteActiveGalleryImage(profile, _currentHeroConfig)) { text = "Delete" };
			_heroDeleteButton.tooltip = "Delete the selected gallery image from VRChat.";
			_heroDeleteButton.AddToClassList("yucp-hero-slide-button");
			_heroDeleteButton.AddToClassList("yucp-hero-slide-delete");
			_heroDeleteButton.style.display = DisplayStyle.None;
			_heroOverlay.Add(_heroDeleteButton);

			_heroNextButton = new Button(() => CycleHeroSlide(1)) { text = "›" };
			_heroNextButton.AddToClassList("yucp-hero-slide-button");
			_heroNextButton.AddToClassList("yucp-hero-slide-next");
			_heroOverlay.Add(_heroNextButton);

			heroPreviewContainer.Add(_heroOverlay);
			heroContent.Add(heroPreviewContainer);

			_heroInfoContainer = new VisualElement();
			_heroInfoContainer.AddToClassList("yucp-hero-info");
			heroContent.Add(_heroInfoContainer);

			_heroPanel.Add(heroContent);
			section.Add(_heroPanel);

			var metadataContainer = new VisualElement();
			metadataContainer.AddToClassList("yucp-hero-metadata-container");
			metadataContainer.style.marginTop = 16;
			section.Add(metadataContainer);

			_avatarGridScroll = new ScrollView(ScrollViewMode.Vertical);
			_avatarGridScroll.AddToClassList("yucp-store-grid-scroll");
			_avatarGridContent = new VisualElement();
			_avatarGridContent.AddToClassList("yucp-store-grid");
			_avatarGridScroll.Add(_avatarGridContent);

			section.Add(_avatarGridScroll);

			BuildAvatarGridView(profile);

			return section;
		}

		private void BuildAvatarGrid(AvatarCollection profile)
		{
			if (_avatarGridContent == null || _heroPanel == null || _heroInfoContainer == null)
				return;

			_avatarGridContent.Clear();

				if (profile.avatars == null || profile.avatars.Count == 0)
				{
				_heroPanel.style.display = DisplayStyle.None;

				var emptyState = new VisualElement();
				emptyState.AddToClassList("yucp-avatar-grid-empty");
				var emptyTitle = new Label("No avatars yet");
				emptyTitle.AddToClassList("yucp-empty-state-title");
				emptyState.Add(emptyTitle);
				var emptyDesc = new Label("Use + Add Avatar to include prefabs in this profile.");
				emptyDesc.AddToClassList("yucp-empty-state-description");
				emptyState.Add(emptyDesc);
				_avatarGridContent.Add(emptyState);
				return;
			}

			if (_selectedAvatarIndex < 0 || _selectedAvatarIndex >= profile.avatars.Count)
			{
				_selectedAvatarIndex = 0;
			}

			_heroPanel.style.display = DisplayStyle.Flex;
			if (_selectedAvatarIndex >= 0 && _selectedAvatarIndex < profile.avatars.Count && profile.avatars[_selectedAvatarIndex] != null)
			{
			UpdateHeroPanel(profile, profile.avatars[_selectedAvatarIndex], _selectedAvatarIndex);
			}

					for (int i = 0; i < profile.avatars.Count; i++)
					{
				var avatarConfig = profile.avatars[i];
				if (avatarConfig == null)
					continue;
				
				// Populate avatar data from PipelineManager when building grid
				// Don't populate from PipelineManager here - it's expensive and causes lag
				// Only populate when avatar is selected (in ShowAvatarDetails)
					
				EnsureGalleryList(avatarConfig);
				EnsureGalleryLoaded(profile, avatarConfig);

				var tile = CreateAvatarTile(profile, avatarConfig, i);
				if (i == _selectedAvatarIndex)
				{
					tile.AddToClassList("yucp-avatar-tile-selected");
				}
				_avatarGridContent.Add(tile);
			}
		}

		private VisualElement CreateAvatarTile(AvatarCollection profile, AvatarAsset config, int index)
		{
			var tile = new VisualElement();
			tile.AddToClassList("yucp-avatar-tile");

			// Multi-select checkbox (top-left corner)
			var checkbox = new Toggle();
			checkbox.value = _selectedAvatarIndices.Contains(index);
			checkbox.AddToClassList("yucp-avatar-tile-checkbox");
			checkbox.RegisterValueChangedCallback(evt =>
			{
				if (evt.newValue)
				{
					_selectedAvatarIndices.Add(index);
				}
				else
				{
					_selectedAvatarIndices.Remove(index);
				}
				UpdateBulkEditPanel(profile);
				BuildCollectionHeader(profile); // Update batch button states
				BuildAvatarGridView(profile); // Refresh to update visual state
			});
			checkbox.RegisterCallback<ClickEvent>(evt =>
			{
				evt.StopPropagation(); // Prevent tile click when clicking checkbox
			});
			checkbox.RegisterCallback<MouseDownEvent>(evt =>
			{
				evt.StopPropagation(); // Prevent tile click when clicking checkbox
			});
			tile.Add(checkbox);

			// Status indicators (top-right corner)
			var statusContainer = new VisualElement();
			statusContainer.AddToClassList("yucp-avatar-tile-status");
			statusContainer.style.position = Position.Absolute;
			statusContainer.style.top = 4;
			statusContainer.style.right = 4;
			statusContainer.style.flexDirection = FlexDirection.Row;

			// Build status badge
			if (!string.IsNullOrEmpty(config.blueprintIdPC) || !string.IsNullOrEmpty(config.blueprintIdQuest))
			{
				var builtBadge = new Label("✓");
				builtBadge.AddToClassList("yucp-avatar-status-badge");
				builtBadge.AddToClassList("yucp-avatar-status-built");
				builtBadge.tooltip = "Has blueprint ID";
				statusContainer.Add(builtBadge);
			}
			else
			{
				var notBuiltBadge = new Label("○");
				notBuiltBadge.AddToClassList("yucp-avatar-status-badge");
				notBuiltBadge.AddToClassList("yucp-avatar-status-not-built");
				notBuiltBadge.tooltip = "Not built yet";
				statusContainer.Add(notBuiltBadge);
			}

			tile.Add(statusContainer);

			// Preview - ALWAYS show 3D preview in grid tiles when prefab exists (icon is only for hero section)
			if (config.avatarPrefab != null)
			{
				// Use 3D preview renderer with head bone tracking mouse
				var previewRenderer = new AvatarTilePreviewRenderer();
				previewRenderer.AddToClassList("yucp-avatar-preview");
				previewRenderer.SetTarget(config.avatarPrefab);
				tile.Add(previewRenderer);
			}
			else
			{
				// Fallback placeholder when no prefab
				var previewImage = new Image();
				previewImage.AddToClassList("yucp-avatar-preview");
				tile.Add(previewImage);
			}

			// Name label
			var nameLabel = new Label(string.IsNullOrEmpty(config.avatarName) ? $"Avatar {index + 1}" : config.avatarName);
			nameLabel.AddToClassList("yucp-avatar-tile-title");
			tile.Add(nameLabel);

			// Tags label
			var tagsLabel = new Label(config.tags != null && config.tags.Count > 0 ? string.Join(", ", config.tags) : "No tags");
			tagsLabel.AddToClassList("yucp-avatar-tile-tags");
			tile.Add(tagsLabel);

			// Platform badges
			var platformRow = new VisualElement();
			platformRow.AddToClassList("yucp-avatar-tile-platforms");
			if (config.buildPC)
			{
				var pcBadge = new Label("PC");
				pcBadge.AddToClassList("yucp-avatar-tile-badge");
				pcBadge.AddToClassList("yucp-avatar-tile-badge-pc");
				platformRow.Add(pcBadge);
			}
			if (config.buildQuest)
			{
				var questBadge = new Label("Quest");
				questBadge.AddToClassList("yucp-avatar-tile-badge");
				questBadge.AddToClassList("yucp-avatar-tile-badge-quest");
				platformRow.Add(questBadge);
			}
			tile.Add(platformRow);

			// Selection state
			if (_selectedAvatarIndices.Contains(index))
			{
				tile.AddToClassList("yucp-avatar-tile-multi-selected");
			}

			// Hover effects
			tile.RegisterCallback<MouseEnterEvent>(_ =>
			{
				tile.AddToClassList("yucp-avatar-tile-hover");
			});
			tile.RegisterCallback<MouseLeaveEvent>(_ =>
			{
				tile.RemoveFromClassList("yucp-avatar-tile-hover");
			});

			// Right-click context menu
			tile.RegisterCallback<MouseDownEvent>(evt =>
			{
				if (evt.button == 1) // Right click
				{
					ShowAvatarContextMenu(profile, config, index, evt);
					evt.StopPropagation();
				}
			});

			// Click handler - toggle selection or show details
			tile.RegisterCallback<ClickEvent>(evt =>
			{
				// If clicking on checkbox, let it handle it
				if (evt.target == checkbox || checkbox.Contains(evt.target as VisualElement))
				{
					return;
				}

				// Ctrl/Cmd click for multi-select
				if (evt.ctrlKey || evt.commandKey)
				{
					if (_selectedAvatarIndices.Contains(index))
					{
						_selectedAvatarIndices.Remove(index);
					}
					else
					{
						_selectedAvatarIndices.Add(index);
					}
					UpdateBulkEditPanel(profile);
						BuildCollectionHeader(profile);
					BuildAvatarGridView(profile);
				}
				else
				{
					// Single click - show details
				_selectedAvatarIndex = index;
				// Reset selection tracking when a different avatar is selected
				_lastSelectedAvatarRoot = null;
				_isSelectingAvatar = false;
					var selectedConfig = profile.avatars != null && index >= 0 && index < profile.avatars.Count 
						? profile.avatars[index] 
						: null;
					
					// Clear multi-select when single-clicking
					_selectedAvatarIndices.Clear();
					_selectedAvatarIndices.Add(index);
					
					// Show avatar details panel
					ShowAvatarDetails(profile, selectedConfig, index);
					
					// Update visual selection state in grid without rebuilding
					UpdateGridSelectionVisualState();
					
					UpdateBulkEditPanel(profile);
					BuildCollectionHeader(profile);
				}
				
				SaveSelectedAvatar();
				evt.StopPropagation();
			});

			return tile;
		}

		private void ShowAvatarDetails(AvatarCollection profile, AvatarAsset config, int index)
		{
			if (_avatarDetailsHost == null)
				return;

			if (config == null)
			{
				_avatarDetailsHost.style.display = DisplayStyle.None;
				_currentHeroConfig = null;
				_selectedAvatarIndex = -1;
				return;
			}

			// IMPORTANT: Set current config immediately to prevent previous avatar data from showing
			_currentHeroConfig = config;
			_selectedAvatarIndex = index;
			_selectedProfile = profile;

			// Reset selection tracking ONLY if this is a different avatar
			// This ensures the Control Panel will select the new avatar
			if (config?.avatarPrefab != null)
			{
				var descriptorRoot = EnsureDescriptorRoot(config.avatarPrefab, config);
				if (_lastSelectedAvatarRoot != descriptorRoot)
				{
					// Different avatar - reset flags so it will be selected
					_lastSelectedAvatarRoot = null;
					_isSelectingAvatar = false;
				}
				// If it's the same avatar, keep the flags to prevent unnecessary re-selection
			}
			else
			{
				// No prefab - reset flags
				_lastSelectedAvatarRoot = null;
				_isSelectingAvatar = false;
			}

			// Clear all section hosts to prevent showing stale data
			_iconSidebarHost?.Clear();
			_gallerySectionHost?.Clear();
			_metadataSectionHost?.Clear();
			_performanceSectionHost?.Clear();
			_visibilitySectionHost?.Clear();
			_validationSectionHost?.Clear();
			_buildActionsHost?.Clear();
			_advancedSectionHost?.Clear();

			// Show the details panel immediately (don't block on expensive operations)
			_avatarDetailsHost.style.display = DisplayStyle.Flex;

			// Update grid selection visual state
			UpdateGridSelectionVisualState();

			// Build icon sidebar
			BuildIconSidebar(config);

			// Ensure gallery is loaded from API FIRST (always fetch fresh data)
			EnsureGalleryLoaded(profile, config, forceReload: true);

			// Build gallery card (will show empty initially, then update when loaded)
			BuildGalleryCard(profile, config);

			// Build all other sections
			BuildMetadataSection();
			BuildPerformanceSection();
			BuildVisibilitySection();
			BuildValidationSection();
			BuildBuildActionsSection();
			BuildAdvancedSection();

			// Build hero section in details panel if it doesn't exist (for grid view)
			BuildHeroSectionInDetailsPanel(profile, config, index);

			// IMPORTANT: Select avatar in Control Panel BEFORE updating hero panel
			// This ensures the Control Panel has the correct avatar selected when we sync data
			if (config?.avatarPrefab != null)
			{
				ControlPanelBridge.EnsureBuilder(state =>
				{
					if (state.Builder != null && _currentHeroConfig == config)
					{
						var descriptorRoot = EnsureDescriptorRoot(config.avatarPrefab, config);
						if (descriptorRoot != null && _lastSelectedAvatarRoot != descriptorRoot)
						{
							try
							{
								var descriptor = descriptorRoot.GetComponent<VRCAvatarDescriptor>();
								if (descriptor != null)
								{
									Debug.Log($"[AvatarUploader] ShowAvatarDetails: Selecting avatar {descriptorRoot.name} in Control Panel");
									_isSelectingAvatar = true;
									_lastSelectedAvatarRoot = descriptorRoot;
									state.Builder.SelectAvatar(descriptorRoot);
									
									// Wait for Control Panel to load avatar data before syncing
									// Use multiple delays to ensure Control Panel has time to populate
									EditorApplication.delayCall += () =>
									{
										EditorApplication.delayCall += () =>
										{
											EditorApplication.delayCall += () =>
											{
												if (_currentHeroConfig == config && _lastSelectedAvatarRoot == descriptorRoot)
												{
													// Sync metadata from Control Panel after it loads (including icon)
													if (TrySyncAvatarMetadataFromControlPanel(config))
													{
														Debug.Log($"[AvatarUploader] ShowAvatarDetails: Synced metadata including icon from Control Panel");
														// Update icon sidebar to show the extracted icon
														BuildIconSidebar(config);
													}
												}
												_isSelectingAvatar = false;
											};
										};
									};
								}
							}
							catch (Exception ex)
							{
								Debug.LogWarning($"[AvatarUploader] ShowAvatarDetails: Failed to select avatar in Control Panel: {ex.Message}");
								_isSelectingAvatar = false;
							}
						}
					}
				});
			}

			// Update hero panel immediately with existing data
			UpdateHeroPanel(profile, config, index);

			// Sync blueprint ID and metadata asynchronously (defer expensive operation to avoid lag)
			// Try Control Panel first, then fallback to PipelineManager
			if (config != null && config.avatarPrefab != null)
			{
				EditorApplication.delayCall += () =>
				{
					if (_currentHeroConfig == config) // Only if still selected
					{
						var hasBlueprintId = !string.IsNullOrEmpty(config.GetBlueprintId(PlatformSwitcher.BuildPlatform.PC)) 
							|| !string.IsNullOrEmpty(config.GetBlueprintId(PlatformSwitcher.BuildPlatform.Quest));
						
						if (!hasBlueprintId)
						{
							// Try to get from Control Panel first (if it has the avatar selected)
							if (!TrySyncBlueprintIdFromControlPanel(config))
							{
								// Fallback to PipelineManager
								config.PopulateFromPipelineManager();
							}
						}
						
						// Try to sync metadata from Control Panel
						ControlPanelBridge.EnsureBuilder(state =>
						{
							if (_currentHeroConfig == config && state.Builder != null)
							{
								// Sync metadata (name, description, tags, release status, icon)
								if (TrySyncAvatarMetadataFromControlPanel(config))
								{
									// Rebuild hero slides to show updated icon
									RebuildHeroSlides(config);
									EditorApplication.delayCall += () =>
									{
										if (_currentHeroConfig == config)
										{
											UpdateHeroPanel(profile, config, index);
										}
									};
								}
							}
						});
						
						var blueprintId = config.GetBlueprintId(PlatformSwitcher.BuildPlatform.PC);
						if (string.IsNullOrEmpty(blueprintId))
							blueprintId = config.GetBlueprintId(PlatformSwitcher.BuildPlatform.Quest);
						
						// Update UI after population and also try API for images
						if (!string.IsNullOrEmpty(blueprintId))
						{
							EditorApplication.delayCall += () =>
							{
								if (_currentHeroConfig == config)
								{
									UpdateHeroPanel(profile, config, index);
									// API call for images (gallery, thumbnail) - Control Panel handles metadata
									PopulateAvatarFromAPI(config);
								}
							};
						}
					}
				};
			}
		}

		private void BuildHeroSectionInDetailsPanel(AvatarCollection profile, AvatarAsset config, int index)
		{
			if (_heroSectionHost == null)
				return;

			_heroSectionHost.Clear();

			// Create hero content container
			_heroContentContainer = new VisualElement();
			_heroContentContainer.AddToClassList("yucp-hero-section");

			// Create hero panel (preview + info)
			_heroPanel = new VisualElement();
			_heroPanel.AddToClassList("yucp-hero-panel");

			var heroContent = new VisualElement();
			heroContent.AddToClassList("yucp-hero-content");

			// Preview container (icon and gallery only, no 3D preview)
			var heroPreviewContainer = new VisualElement();
			heroPreviewContainer.AddToClassList("yucp-hero-preview-container");

			_heroImageDisplay = new Image();
			_heroImageDisplay.AddToClassList("yucp-hero-gallery-image");
			heroPreviewContainer.Add(_heroImageDisplay);

			// Overlay with controls
			_heroOverlay = new VisualElement();
			_heroOverlay.AddToClassList("yucp-hero-overlay");
			
			_heroSlideLabel = new Label();
			_heroSlideLabel.AddToClassList("yucp-hero-slide-label");
			_heroOverlay.Add(_heroSlideLabel);

			_heroPrevButton = new Button(() => CycleHeroSlide(-1)) { text = "‹" };
			_heroPrevButton.AddToClassList("yucp-hero-slide-button");
			_heroPrevButton.AddToClassList("yucp-hero-slide-prev");
			_heroOverlay.Add(_heroPrevButton);

			var captureButton = new Button(() => CaptureThumbnailFromScene()) { text = "Capture" };
			captureButton.tooltip = "Capture thumbnail from scene view";
			captureButton.AddToClassList("yucp-hero-slide-button");
			captureButton.style.position = Position.Absolute;
			captureButton.style.bottom = 12;
			captureButton.style.left = 12;
			_heroOverlay.Add(captureButton);

			var uploadButton = new Button(() => UploadThumbnailImage()) { text = "Upload" };
			uploadButton.tooltip = "Upload thumbnail image file";
			uploadButton.AddToClassList("yucp-hero-slide-button");
			uploadButton.style.position = Position.Absolute;
			uploadButton.style.bottom = 12;
			uploadButton.style.left = 60;
			_heroOverlay.Add(uploadButton);

			_heroAddButton = new Button(() => AddGalleryImage(profile, config)) { text = "+" };
			_heroAddButton.tooltip = "Enable gallery integration and store a VRChat API key in Avatar Tools settings to add gallery images.";
			_heroAddButton.AddToClassList("yucp-hero-slide-button");
			_heroAddButton.AddToClassList("yucp-hero-slide-add");
			_heroOverlay.Add(_heroAddButton);

			_heroSetIconButton = new Button(() => SetActiveGalleryImageAsIcon(profile, config)) { text = "Set Icon" };
			_heroSetIconButton.tooltip = "Make the selected gallery image the avatar icon via the VRChat API.";
			_heroSetIconButton.AddToClassList("yucp-hero-slide-button");
			_heroSetIconButton.AddToClassList("yucp-hero-slide-seticon");
			_heroSetIconButton.style.display = DisplayStyle.None;
			_heroOverlay.Add(_heroSetIconButton);

			_heroDeleteButton = new Button(() => DeleteActiveGalleryImage(profile, config)) { text = "Delete" };
			_heroDeleteButton.tooltip = "Delete the selected gallery image from VRChat.";
			_heroDeleteButton.AddToClassList("yucp-hero-slide-button");
			_heroDeleteButton.AddToClassList("yucp-hero-slide-delete");
			_heroDeleteButton.style.display = DisplayStyle.None;
			_heroOverlay.Add(_heroDeleteButton);

			_heroNextButton = new Button(() => CycleHeroSlide(1)) { text = "›" };
			_heroNextButton.AddToClassList("yucp-hero-slide-button");
			_heroNextButton.AddToClassList("yucp-hero-slide-next");
			_heroOverlay.Add(_heroNextButton);

			heroPreviewContainer.Add(_heroOverlay);
			heroContent.Add(heroPreviewContainer);

			// Info container
			_heroInfoContainer = new VisualElement();
			_heroInfoContainer.AddToClassList("yucp-hero-info");
			heroContent.Add(_heroInfoContainer);

			_heroPanel.Add(heroContent);
			_heroContentContainer.Add(_heroPanel);

			// Thumbnail host
			_thumbnailHost = new VisualElement();
			_thumbnailHost.AddToClassList("yucp-hero-thumbnail-host");
			_heroContentContainer.Add(_thumbnailHost);

			_heroSectionHost.Add(_heroContentContainer);
		}

		private void BuildIconSidebar(AvatarAsset config)
		{
			if (_iconSidebarHost == null)
				return;

			_iconSidebarHost.Clear();

			if (config == null)
			{
				var emptyLabel = new Label("Select an avatar to manage icon");
				emptyLabel.AddToClassList("yucp-label-secondary");
				emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
				emptyLabel.style.paddingTop = 20;
				emptyLabel.style.paddingBottom = 20;
				_iconSidebarHost.Add(emptyLabel);
				return;
			}

			var title = new Label("Avatar Icon");
			title.AddToClassList("yucp-card-title");
			title.style.marginBottom = 4;
			_iconSidebarHost.Add(title);

			var subtitle = new Label("The icon shown in VRChat's avatar selection menu. Required for new avatars.");
			subtitle.AddToClassList("yucp-card-subtitle");
			subtitle.style.marginBottom = 12;
			_iconSidebarHost.Add(subtitle);

			var iconPreview = new Image();
			iconPreview.AddToClassList("yucp-icon-preview");
			if (config.avatarIcon != null)
			{
				iconPreview.image = config.avatarIcon;
			}
			else
			{
				iconPreview.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
			}
			_iconSidebarHost.Add(iconPreview);

			var statusLabel = new Label(config.avatarIcon != null ? "Icon Set" : "No Icon");
			statusLabel.AddToClassList("yucp-label-small");
			statusLabel.style.marginTop = 0;
			statusLabel.style.marginBottom = 12;
			statusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
			if (config.avatarIcon != null)
			{
				statusLabel.style.color = new Color(0.36f, 0.75f, 0.69f);
			}
			else
			{
				statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
			}
			_iconSidebarHost.Add(statusLabel);

			var buttonContainer = new VisualElement();
			buttonContainer.style.flexDirection = FlexDirection.Column;
			buttonContainer.style.flexGrow = 0;

			var captureButton = new Button(() => CaptureThumbnailFromScene()) { text = "Capture" };
			captureButton.tooltip = "Capture thumbnail from scene view";
			captureButton.AddToClassList("yucp-button");
			captureButton.AddToClassList("yucp-button-action");
			captureButton.style.marginBottom = 8;
			buttonContainer.Add(captureButton);

			var uploadButton = new Button(() => UploadThumbnailImage()) { text = "Upload" };
			uploadButton.tooltip = "Upload thumbnail image file";
			uploadButton.AddToClassList("yucp-button");
			uploadButton.AddToClassList("yucp-button-action");
			uploadButton.style.marginBottom = 8;
			buttonContainer.Add(uploadButton);

			if (config.avatarIcon != null)
			{
				var clearButton = new Button(() =>
				{
					if (EditorUtility.DisplayDialog("Clear Icon", "Are you sure you want to clear the avatar icon?", "Yes", "No"))
					{
						Undo.RecordObject(_selectedProfile, "Clear Avatar Icon");
						config.avatarIcon = null;
						EditorUtility.SetDirty(_selectedProfile);
						BuildIconSidebar(config);
					}
				}) { text = "Clear" };
				clearButton.tooltip = "Remove the avatar icon";
				clearButton.AddToClassList("yucp-button");
				clearButton.AddToClassList("yucp-button-danger");
				buttonContainer.Add(clearButton);
			}

			_iconSidebarHost.Add(buttonContainer);
		}

		private void BuildGalleryCard(AvatarCollection profile, AvatarAsset config)
		{
			if (_gallerySectionHost == null)
				return;

			_gallerySectionHost.Clear();

			if (config == null)
			{
				var emptyCard = CreateCard("Gallery", null, "Manage gallery images for your avatar. These images appear in the VRChat avatar selection menu.");
				var emptyLabel = new Label("Select an avatar to view gallery");
				emptyLabel.AddToClassList("yucp-label-secondary");
				emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
				emptyLabel.style.paddingTop = 20;
				emptyLabel.style.paddingBottom = 20;
				emptyCard.Q(className: "yucp-card-content").Add(emptyLabel);
				_gallerySectionHost.Add(emptyCard);
				return;
			}

			var cardContent = new VisualElement();

			// Gallery carousel (full-size image viewer)
			var carouselContainer = new VisualElement();
			carouselContainer.AddToClassList("yucp-gallery-carousel-container");

			var carouselPreview = new VisualElement();
			carouselPreview.AddToClassList("yucp-gallery-carousel-preview");

			_heroImageDisplay = new Image();
			_heroImageDisplay.AddToClassList("yucp-gallery-carousel-image");
			carouselPreview.Add(_heroImageDisplay);

			// Overlay with controls
			_heroOverlay = new VisualElement();
			_heroOverlay.AddToClassList("yucp-gallery-carousel-overlay");
			
			_heroSlideLabel = new Label();
			_heroSlideLabel.AddToClassList("yucp-gallery-carousel-label");
			_heroOverlay.Add(_heroSlideLabel);

			_heroPrevButton = new Button(() => CycleHeroSlide(-1)) { text = "‹" };
			_heroPrevButton.AddToClassList("yucp-gallery-carousel-button");
			_heroPrevButton.AddToClassList("yucp-gallery-carousel-prev");
			_heroOverlay.Add(_heroPrevButton);

			_heroAddButton = new Button(() => AddGalleryImage(profile, config)) { text = "+" };
			_heroAddButton.tooltip = "Add gallery image";
			_heroAddButton.AddToClassList("yucp-gallery-carousel-button");
			_heroAddButton.AddToClassList("yucp-gallery-carousel-add");
			_heroOverlay.Add(_heroAddButton);

			_heroSetIconButton = new Button(() => SetActiveGalleryImageAsIcon(profile, config)) { text = "Set Icon" };
			_heroSetIconButton.tooltip = "Make the selected gallery image the avatar icon via the VRChat API.";
			_heroSetIconButton.AddToClassList("yucp-gallery-carousel-button");
			_heroSetIconButton.AddToClassList("yucp-gallery-carousel-seticon");
			_heroSetIconButton.style.display = DisplayStyle.None;
			_heroOverlay.Add(_heroSetIconButton);

			_heroDeleteButton = new Button(() => DeleteActiveGalleryImage(profile, config)) { text = "Delete" };
			_heroDeleteButton.tooltip = "Delete the selected gallery image from VRChat.";
			_heroDeleteButton.AddToClassList("yucp-gallery-carousel-button");
			_heroDeleteButton.AddToClassList("yucp-gallery-carousel-delete");
			_heroDeleteButton.style.display = DisplayStyle.None;
			_heroOverlay.Add(_heroDeleteButton);

			_heroNextButton = new Button(() => CycleHeroSlide(1)) { text = "›" };
			_heroNextButton.AddToClassList("yucp-gallery-carousel-button");
			_heroNextButton.AddToClassList("yucp-gallery-carousel-next");
			_heroOverlay.Add(_heroNextButton);

			carouselPreview.Add(_heroOverlay);
			carouselContainer.Add(carouselPreview);

			// Thumbnail grid
			var thumbnailGrid = new VisualElement();
			thumbnailGrid.AddToClassList("yucp-gallery-grid");

			EnsureGalleryList(config);
			Debug.Log($"[AvatarUploader] BuildGalleryCard: Building thumbnail grid, galleryImages: {config.galleryImages?.Count ?? 0}");
			
			if (config.galleryImages != null && config.galleryImages.Count > 0)
			{
				Debug.Log($"[AvatarUploader] BuildGalleryCard: Creating {config.galleryImages.Count} thumbnails");
				for (int i = 0; i < config.galleryImages.Count; i++)
				{
					var galleryImage = config.galleryImages[i];
					Debug.Log($"[AvatarUploader] BuildGalleryCard: Creating thumbnail {i+1}/{config.galleryImages.Count} - URL: {galleryImage?.url ?? "null"}, thumbnail: {(galleryImage?.thumbnail != null ? "exists" : "null")}");
					
					var thumbnail = new Image();
					thumbnail.AddToClassList("yucp-gallery-thumbnail");
					
					// Load thumbnail lazily
					if (galleryImage?.thumbnail != null)
					{
						Debug.Log($"[AvatarUploader] BuildGalleryCard: Using existing thumbnail for image {i+1}");
						thumbnail.image = galleryImage.thumbnail;
					}
					else if (galleryImage != null && !string.IsNullOrEmpty(galleryImage.url))
					{
						Debug.Log($"[AvatarUploader] BuildGalleryCard: Loading thumbnail lazily for image {i+1} from URL: {galleryImage.url}");
						// Show placeholder, load thumbnail on demand
						thumbnail.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
						LoadThumbnailAsync(thumbnail, galleryImage);
					}
					else
					{
						Debug.LogWarning($"[AvatarUploader] BuildGalleryCard: Gallery image {i+1} is null or has no URL");
					}
					
					int index = i;
					thumbnail.RegisterCallback<ClickEvent>(_ =>
					{
						// Remove active class from all thumbnails
						foreach (var child in thumbnailGrid.Children())
						{
							if (child is Image thumb)
							{
								thumb.RemoveFromClassList("yucp-gallery-thumbnail-active");
							}
						}
						
						// Add active class to clicked thumbnail
						thumbnail.AddToClassList("yucp-gallery-thumbnail-active");
						
						_activeHeroSlideIndex = index;
						ShowHeroSlide();
					});

					// Highlight active thumbnail
					if (i == _activeHeroSlideIndex)
					{
						thumbnail.AddToClassList("yucp-gallery-thumbnail-active");
					}

					thumbnailGrid.Add(thumbnail);
				}
			}
			else
			{
				var emptyLabel = new Label("No gallery images. Click + to add.");
				emptyLabel.AddToClassList("yucp-label-secondary");
				emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
				emptyLabel.style.paddingTop = 20;
				emptyLabel.style.paddingBottom = 20;
				thumbnailGrid.Add(emptyLabel);
			}

			cardContent.Add(carouselContainer);
			cardContent.Add(thumbnailGrid);

			var card = CreateCard("Gallery", cardContent, "Manage gallery images for your avatar. These images appear in the VRChat avatar selection menu.");
			_gallerySectionHost.Add(card);

			// Rebuild hero slides to populate carousel
			RebuildHeroSlides(config);
		}

		private void UpdateHeroPanel(AvatarCollection profile, AvatarAsset config, int index)
		{
			// If switching to a different avatar, clear hero slides immediately to prevent showing stale content
			var previousConfig = _currentHeroConfig;
			_currentHeroConfig = config;
			
			if (config == null)
			{
				if (_heroPanel != null)
					_heroPanel.style.display = DisplayStyle.None;
				_heroSlides.Clear();
				if (_heroImageDisplay != null)
				{
					_heroImageDisplay.image = null;
					_heroImageDisplay.style.display = DisplayStyle.None;
				}
				return;
			}
			
			// Clear hero slides when switching avatars to prevent showing previous avatar's content
			if (previousConfig != config)
			{
				_heroSlides.Clear();
				if (_heroImageDisplay != null)
				{
					_heroImageDisplay.image = null;
					_heroImageDisplay.style.display = DisplayStyle.None;
				}
				if (_heroSlideLabel != null)
				{
					_heroSlideLabel.text = "Loading...";
				}
			}

			// Ensure UI elements are initialized - build them if they don't exist (for details panel)
			if (_heroPanel == null || _heroInfoContainer == null)
			{
				// Try to build hero section in details panel
				if (_avatarDetailsHost != null && _avatarDetailsHost.style.display == DisplayStyle.Flex)
				{
					BuildHeroSectionInDetailsPanel(profile, config, index);
				}
				
				// If still null after building, return
				if (_heroPanel == null || _heroInfoContainer == null)
					return;
			}

			// Sync blueprint ID from Control Panel if available, otherwise from PipelineManager
			if (config.avatarPrefab != null)
			{
				var hasBlueprintId = !string.IsNullOrEmpty(config.GetBlueprintId(PlatformSwitcher.BuildPlatform.PC)) 
					|| !string.IsNullOrEmpty(config.GetBlueprintId(PlatformSwitcher.BuildPlatform.Quest));
				
				if (!hasBlueprintId)
				{
					// Try to get from Control Panel first (if it has the avatar selected)
					if (!TrySyncBlueprintIdFromControlPanel(config))
					{
						// Fallback to PipelineManager
						config.PopulateFromPipelineManager();
					}
				}
			}
			
			EnsureGalleryList(config);
			EnsureGalleryLoaded(profile, config);

			// Rebuild hero slides for the new avatar
			RebuildHeroSlides(config);

			_heroInfoContainer.Clear();
			_heroInfoContainer.Add(CreateHeroInfoCard(profile, config, index));
			_cpUiBinder?.BindActiveAvatar(profile, config);
			
			// Also try API as fallback (for images and any missing data)
			if (config.avatarPrefab != null)
			{
				PopulateAvatarFromAPI(config);
			}
			
			// Select avatar in Control Panel and sync metadata (only if not already selected to prevent loops)
			if (config.avatarPrefab != null)
			{
				var descriptorRoot = EnsureDescriptorRoot(config.avatarPrefab, config);
				if (descriptorRoot != null)
				{
					// Check if this is the same avatar we already selected (prevent loop)
					// Only skip if we're CURRENTLY selecting it (to prevent loops)
					// But if flags are null/false, we need to select it
					if (_lastSelectedAvatarRoot == descriptorRoot && _isSelectingAvatar)
					{
						// Already in the process of selecting this avatar, skip to avoid loop
						Debug.Log($"[AvatarUploader] UpdateHeroPanel: Already selecting avatar {descriptorRoot.name}, skipping");
						BuildMetadataSection();
						BuildPerformanceSection();
						BuildVisibilitySection();
						BuildValidationSection();
						BuildBuildActionsSection();
						BuildAdvancedSection();
						return;
					}
					
					// If it's a different avatar, or flags were reset, we need to select it
					// Use EnsureBuilder to get the builder (it might not be initialized yet)
					ControlPanelBridge.EnsureBuilder(state =>
					{
						if (state.Builder != null && descriptorRoot != null)
						{
							try
							{
								var descriptorRoot = EnsureDescriptorRoot(config.avatarPrefab, config);
								if (descriptorRoot == null)
									return;
								
								// Additional validation before calling SelectAvatar
								if (descriptorRoot.activeInHierarchy || PrefabUtility.IsPartOfPrefabAsset(descriptorRoot))
								{
									// Check if descriptor has required components
									var descriptor = descriptorRoot.GetComponent<VRCAvatarDescriptor>();
									if (descriptor != null)
									{
										// Mark that we're selecting to prevent recursive calls
										_isSelectingAvatar = true;
										_lastSelectedAvatarRoot = descriptorRoot;
										
										state.Builder.SelectAvatar(descriptorRoot);
										
										// After SelectAvatar, wait for Control Panel to load data, then sync
										// Use multiple delays to ensure Control Panel has time to populate
										EditorApplication.delayCall += () =>
										{
											EditorApplication.delayCall += () =>
											{
												EditorApplication.delayCall += () =>
												{
													if (_currentHeroConfig == config && _lastSelectedAvatarRoot == descriptorRoot)
													{
														// Sync metadata from Control Panel after it loads
														if (TrySyncAvatarMetadataFromControlPanel(config))
														{
															// Rebuild hero slides to show updated icon
															RebuildHeroSlides(config);
														}
													}
													_isSelectingAvatar = false;
												};
											};
										};
									}
									else
									{
										Debug.LogWarning($"[AvatarUploader] Descriptor root does not have VRCAvatarDescriptor component");
									}
								}
								else
								{
									Debug.LogWarning($"[AvatarUploader] Descriptor root is not active or is not a prefab asset");
								}
							}
							catch (NullReferenceException ex)
							{
								Debug.LogWarning($"[AvatarUploader] SelectAvatar failed (NullReference): {ex.Message}\n{ex.StackTrace}");
								_isSelectingAvatar = false;
								// Continue anyway - UI can still be built
							}
							catch (Exception ex)
							{
								Debug.LogWarning($"[AvatarUploader] SelectAvatar failed: {ex.Message}");
								_isSelectingAvatar = false;
								// Continue anyway - UI can still be built
							}
						}
						BuildMetadataSection();
						BuildPerformanceSection();
						BuildVisibilitySection();
						BuildValidationSection();
						BuildBuildActionsSection();
						BuildAdvancedSection();
					});
				}
				else
				{
					BuildMetadataSection();
					BuildPerformanceSection();
					BuildVisibilitySection();
					BuildValidationSection();
					BuildBuildActionsSection();
					BuildAdvancedSection();
				}
			}
			else
			{
				// Build sections even if Control Panel builder isn't available
				BuildMetadataSection();
				BuildPerformanceSection();
				BuildVisibilitySection();
				BuildValidationSection();
				BuildBuildActionsSection();
				BuildAdvancedSection();
			}
		}

		private VisualElement CreateHeroInfoCard(AvatarCollection profile, AvatarAsset config, int index)
		{
			var info = new VisualElement();
			info.AddToClassList("yucp-hero-info-root");

			var nameLabel = new Label(string.IsNullOrEmpty(config.avatarName) ? $"Avatar {index + 1}" : config.avatarName);
			nameLabel.AddToClassList("yucp-hero-title-field");
			nameLabel.style.marginBottom = 8;
			info.Add(nameLabel);

			var descriptionPreview = new Label(string.IsNullOrWhiteSpace(config.description)
				? "No description"
				: (config.description.Length > 150 ? config.description.Substring(0, 150) + "..." : config.description));
			descriptionPreview.AddToClassList("yucp-hero-description-label");
			descriptionPreview.style.whiteSpace = WhiteSpace.Normal;
			descriptionPreview.style.marginBottom = 12;
			info.Add(descriptionPreview);

			var quickInfo = new VisualElement();
			quickInfo.style.flexDirection = FlexDirection.Row;
			quickInfo.style.flexWrap = Wrap.Wrap;
			quickInfo.style.marginBottom = 12;

			if (config.buildPC)
			{
				var pcBadge = new Label("PC");
				pcBadge.AddToClassList("yucp-avatar-tile-badge");
				quickInfo.Add(pcBadge);
			}

			if (config.buildQuest)
			{
				var questBadge = new Label("Quest");
				questBadge.AddToClassList("yucp-avatar-tile-badge");
				quickInfo.Add(questBadge);
			}

			if (config.tags != null && config.tags.Count > 0)
			{
				var tagsLabel = new Label($"Tags: {string.Join(", ", config.tags)}");
				tagsLabel.AddToClassList("yucp-label-small");
				tagsLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
				tagsLabel.style.marginLeft = 8;
				quickInfo.Add(tagsLabel);
			}

			info.Add(quickInfo);

			var actionsRow = new VisualElement();
			actionsRow.style.flexDirection = FlexDirection.Row;
			actionsRow.style.marginTop = 8;

			var pingButton = new Button(() =>
			{
				if (config.avatarPrefab != null)
				{
					Selection.activeObject = config.avatarPrefab;
					EditorGUIUtility.PingObject(config.avatarPrefab);
				}
			}) { text = "Ping Prefab" };
			pingButton.AddToClassList("yucp-button");
			pingButton.AddToClassList("yucp-button-small");
			actionsRow.Add(pingButton);

			var removeButton = new Button(() => RemoveAvatarFromProfile(profile, index)) { text = "Remove" };
			removeButton.AddToClassList("yucp-button");
			removeButton.AddToClassList("yucp-button-danger");
			removeButton.AddToClassList("yucp-button-small");
			removeButton.style.marginLeft = 8;
			actionsRow.Add(removeButton);

			info.Add(actionsRow);

			return info;
		}

		private VisualElement CreateHeroStats(AvatarCollection profile, AvatarAsset config)
		{
			var row = new VisualElement();
			row.AddToClassList("yucp-hero-stat-row");

			row.Add(CreateHeroStatCard("Avatars", (profile.avatars?.Count ?? 0).ToString()));
			row.Add(CreateHeroStatCard("Last Build", string.IsNullOrEmpty(profile.LastBuildTime) ? "Never" : profile.LastBuildTime));
			row.Add(CreateHeroStatCard("Blueprint", string.IsNullOrEmpty(config.blueprintIdPC) && string.IsNullOrEmpty(config.blueprintIdQuest) ? "Unset" : "Assigned"));

			return row;
		}

		private VisualElement CreateHeroStatCard(string label, string value)
		{
			var card = new VisualElement();
			card.AddToClassList("yucp-hero-stat-card");

			var labelEl = new Label(label);
			labelEl.AddToClassList("yucp-hero-stat-label");
			card.Add(labelEl);

			var valueEl = new Label(value);
			valueEl.AddToClassList("yucp-hero-stat-value");
			card.Add(valueEl);

			return card;
		}

		private VisualElement CreateHeroQuickActions(AvatarCollection profile, AvatarAsset config, int index)
		{
			var container = new VisualElement();
			container.AddToClassList("yucp-hero-quick-actions");

			var buildButton = new Button(() => BuildSingleAvatar(profile, config, PlatformSwitcher.BuildPlatform.PC))
			{
				text = "Build PC"
			};
			buildButton.AddToClassList("yucp-button");
			buildButton.AddToClassList("yucp-button-build");
			buildButton.AddToClassList("yucp-button-small");
			buildButton.SetEnabled(config.avatarPrefab != null);
			container.Add(buildButton);

			var questButton = new Button(() => BuildSingleAvatar(profile, config, PlatformSwitcher.BuildPlatform.Quest))
			{
				text = "Build Quest"
			};
			questButton.AddToClassList("yucp-button");
			questButton.AddToClassList("yucp-button-build");
			questButton.AddToClassList("yucp-button-small");
			questButton.SetEnabled(config.avatarPrefab != null);
			container.Add(questButton);

			var openButton = new Button(() =>
			{
				if (config.avatarPrefab != null)
				{
					Selection.activeObject = config.avatarPrefab;
					EditorGUIUtility.PingObject(config.avatarPrefab);
				}
			})
			{
				text = "Select Prefab"
			};
			openButton.AddToClassList("yucp-button");
			openButton.AddToClassList("yucp-button-action");
			openButton.AddToClassList("yucp-button-small");
			container.Add(openButton);

			return container;
		}

		private async void BuildSingleAvatar(AvatarCollection profile, AvatarAsset config, PlatformSwitcher.BuildPlatform platform)
		{
			if (config == null)
				return;

			try
			{
				EnsureUploadDisclaimerAcknowledged();
			}
			catch (OperationCanceledException)
			{
				return;
			}

			var success = await DispatchControlPanelBuild(config, platform, BuildWorkflow.BuildOnly);
			if (success)
			{
				_toast?.ShowInfo($"Dispatched build for '{config.avatarName}' ({platform}).", "Build Started", 3f);
			}
		}

		private VisualElement CreateFormRow(string labelText, string tooltip = null)
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

		private void BuildBuildActionsSection()
		{
			if (_buildActionsHost == null)
				return;

			_buildActionsHost.Clear();

			if (_selectedProfile == null || _currentHeroConfig == null)
			{
				var emptyCard = CreateCard("Build Actions", null);
				var emptyLabel = new Label("Select an avatar to build");
				emptyLabel.AddToClassList("yucp-label-secondary");
				emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
				emptyLabel.style.paddingTop = 20;
				emptyLabel.style.paddingBottom = 20;
				emptyCard.Q(className: "yucp-card-content").Add(emptyLabel);
				_buildActionsHost.Add(emptyCard);
				return;
			}

			var section = new VisualElement();

			var helpText = new Label("Build and upload your avatar to VRChat. Test builds allow you to preview before publishing.");
			helpText.AddToClassList("yucp-help-text");
			helpText.style.marginBottom = 16;
			section.Add(helpText);

			// Check for validation issues
			var validationErrors = GetValidationErrors();
			bool hasErrors = validationErrors.Count > 0;

			// Show validation errors if any
			if (hasErrors)
			{
				var errorBox = new VisualElement();
				errorBox.AddToClassList("yucp-validation-error");
				errorBox.style.marginBottom = 16;
				errorBox.style.paddingTop = 16;
				errorBox.style.paddingBottom = 16;
				errorBox.style.paddingLeft = 16;
				errorBox.style.paddingRight = 16;

				var errorTitle = new Label("Cannot Publish - Fix Issues Below");
				errorTitle.AddToClassList("yucp-validation-error-text");
				errorTitle.style.fontSize = 14;
				errorTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
				errorTitle.style.marginBottom = 8;
				errorBox.Add(errorTitle);

				var errorList = new VisualElement();
				errorList.style.flexDirection = FlexDirection.Column;
				foreach (var error in validationErrors)
				{
					var errorItem = new Label($"• {error}");
					errorItem.AddToClassList("yucp-validation-error-text");
					errorItem.style.marginBottom = 4;
					errorItem.style.whiteSpace = WhiteSpace.Normal;
					errorList.Add(errorItem);
				}
				errorBox.Add(errorList);

				var helpNote = new Label("Please fix the issues shown in the Validation section above before publishing.");
				helpNote.AddToClassList("yucp-help-text");
				helpNote.style.marginTop = 12;
				helpNote.style.fontSize = 11;
				errorBox.Add(helpNote);

				section.Add(errorBox);
			}

			var buttonContainer = new VisualElement();
			buttonContainer.AddToClassList("yucp-build-actions-container");
			buttonContainer.style.flexDirection = FlexDirection.Row;
			buttonContainer.style.justifyContent = Justify.FlexStart;
			buttonContainer.style.flexWrap = Wrap.Wrap;

			bool canStart = !_isBuilding && _selectedProfile != null && _currentHeroConfig != null;
			bool canPublish = canStart && !hasErrors; // Disable publish if there are errors

			_testButton = new Button(TestSelectedProfile)
			{
				text = "Build & Test"
			};
			_testButton.AddToClassList("yucp-button");
			_testButton.AddToClassList("yucp-button-large");
			_testButton.style.marginRight = 12;
			_testButton.SetEnabled(canStart);
			buttonContainer.Add(_testButton);

			_buildButton = new Button(BuildSelectedProfile)
			{
				text = "Build & Publish"
			};
			_buildButton.AddToClassList("yucp-button");
			_buildButton.AddToClassList("yucp-button-primary");
			_buildButton.AddToClassList("yucp-button-large");
			_buildButton.SetEnabled(canPublish);
			
			// Add tooltip if disabled due to validation errors
			if (hasErrors)
			{
				_buildButton.tooltip = "Fix validation errors before publishing. See the Validation section above.";
			}
			
			buttonContainer.Add(_buildButton);

			section.Add(buttonContainer);

			// Note: Platform selection is available in Advanced Settings section
			var platformNote = new Label("Platform selection is available in Advanced Settings below.");
			platformNote.AddToClassList("yucp-help-text");
			platformNote.style.marginTop = 12;
			platformNote.style.fontSize = 11;
			section.Add(platformNote);

			var card = CreateCard("Build Actions", section);
			_buildActionsHost.Add(card);
		}

		/// <summary>
		/// Get validation errors for the current avatar configuration.
		/// Returns a list of error messages that prevent publishing.
		/// </summary>
		private List<string> GetValidationErrors()
		{
			var errors = new List<string>();

			if (_currentHeroConfig == null)
			{
				errors.Add("No avatar selected");
				return errors;
			}

			// Check for missing prefab
			if (_currentHeroConfig.avatarPrefab == null)
			{
				errors.Add("Avatar Prefab is not assigned");
			}

			// Check for missing name
			if (string.IsNullOrWhiteSpace(_currentHeroConfig.avatarName))
			{
				errors.Add("Avatar name is required");
			}

			// Check for missing icon on new avatars (before first upload)
			bool isNewAvatar = string.IsNullOrEmpty(_currentHeroConfig.blueprintIdPC) && 
			                   string.IsNullOrEmpty(_currentHeroConfig.blueprintIdQuest);
			if (isNewAvatar && _currentHeroConfig.avatarIcon == null)
			{
				errors.Add("Avatar icon is required before first upload");
			}

			// Check Control Panel validation issues
			if (_controlPanelBuilder != null)
			{
				try
				{
					if (ControlPanelDataBridge.TryGetValidationIssues(_controlPanelBuilder, out var issues))
					{
						foreach (var issue in issues)
						{
							if (issue.Severity == ControlPanelDataBridge.ValidationSeverity.Error)
							{
								errors.Add(issue.Message);
							}
						}
					}
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"[AvatarUploader] Failed to get validation issues: {ex.Message}");
				}
			}

			return errors;
		}

		private void UpdateBottomBar()
		{
			// Rebuild build actions section to update button states
			BuildBuildActionsSection();
			// Also refresh validation to ensure errors are shown
			BuildValidationSection();
		}

		private void UpdateProgress(float progress, string status)
		{
			_progress = progress;
			_status = status;

			bool shouldShow = _isBuilding && (progress > 0f || !string.IsNullOrEmpty(status));

			// Show/hide entire bottom bar based on build state
			var bottomBarHost = rootVisualElement?.Q<VisualElement>("bottom-bar-host");
			if (bottomBarHost != null)
			{
				bottomBarHost.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (_progressContainer != null)
			{
				_progressContainer.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (_progressFill != null)
			{
				_progressFill.style.width = Length.Percent(progress * 100);
			}

			if (_progressText != null)
			{
				_progressText.text = string.IsNullOrEmpty(status) ? $"{progress * 100:F0}%" : status;
			}
		}

		private void ReloadProfiles()
		{
			_profiles.Clear();
			string[] guids = AssetDatabase.FindAssets("t:AvatarCollection");
			foreach (var guid in guids)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var p = AssetDatabase.LoadAssetAtPath<AvatarCollection>(path);
				if (p != null) _profiles.Add(p);
			}
			_profiles.Sort((a, b) => string.Compare(a.collectionName, b.collectionName, StringComparison.OrdinalIgnoreCase));
			if (_profiles.Count == 0)
			{
				_selectedIndex = -1;
				_selectedProfile = null;
			}
			else if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count)
			{
				_selectedIndex = 0;
				_selectedProfile = _profiles[0];
			}
		}

		private void CreateNewProfile()
		{
			var dir = "Assets/YUCP/AvatarCollections";
			if (!AssetDatabase.IsValidFolder("Assets/YUCP"))
			{
				AssetDatabase.CreateFolder("Assets", "YUCP");
			}
			if (!AssetDatabase.IsValidFolder(dir))
			{
				AssetDatabase.CreateFolder("Assets/YUCP", "AvatarCollections");
			}
			var profile = ScriptableObject.CreateInstance<AvatarCollection>();
			profile.collectionName = "New Avatar Profile";
			var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/New Avatar Upload Profile.asset");
			AssetDatabase.CreateAsset(profile, path);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			ReloadProfiles();
			_selectedIndex = _profiles.FindIndex(p => AssetDatabase.GetAssetPath(p) == path);
			_selectedProfile = _profiles[_selectedIndex];
			UpdateProfileList();
			UpdateProfileDetails();
			UpdateBottomBar();
			Selection.activeObject = profile;
			EditorGUIUtility.PingObject(profile);
		}

		private void CloneSelectedProfile()
		{
			if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;
			var source = _profiles[_selectedIndex];
			var clone = Instantiate(source);
			clone.name = source.name + " (Clone)";
			var dir = AssetDatabase.GetAssetPath(source);
			dir = System.IO.Path.GetDirectoryName(dir).Replace('\\', '/');
			var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{clone.name}.asset");
			AssetDatabase.CreateAsset(clone, path);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			ReloadProfiles();
			_selectedIndex = _profiles.FindIndex(p => AssetDatabase.GetAssetPath(p) == path);
			_selectedProfile = _profiles[_selectedIndex];
			UpdateProfileList();
			UpdateProfileDetails();
			UpdateBottomBar();
			Selection.activeObject = clone;
			EditorGUIUtility.PingObject(clone);
		}

		private void DeleteSelectedProfile()
		{
			if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;
			var profile = _profiles[_selectedIndex];
			var path = AssetDatabase.GetAssetPath(profile);
			if (EditorUtility.DisplayDialog("Delete Profile", $"Delete profile '{profile.collectionName}'? This cannot be undone.", "Delete", "Cancel"))
			{
				AssetDatabase.DeleteAsset(path);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				ReloadProfiles();
				if (_profiles.Count > 0)
				{
					_selectedIndex = 0;
					_selectedProfile = _profiles[0];
				}
				else
				{
					_selectedIndex = -1;
					_selectedProfile = null;
				}
				UpdateProfileList();
				UpdateProfileDetails();
				UpdateBottomBar();
			}
		}

		private void AddAvatarToProfile(AvatarCollection profile)
		{
			if (profile.avatars == null)
			{
				profile.avatars = new List<AvatarAsset>();
			}
			
			// Create a new AvatarAsset ScriptableObject
			var avatarAsset = ScriptableObject.CreateInstance<AvatarAsset>();
			avatarAsset.name = "New Avatar Asset";
			
			var settings = AvatarUploaderSettings.Instance;
			avatarAsset.buildPC = settings.DefaultBuildPC;
			avatarAsset.buildQuest = settings.DefaultBuildQuest;
			avatarAsset.assignedCollection = profile;
			
			// Save the asset file next to the collection
			var collectionPath = AssetDatabase.GetAssetPath(profile);
			var directory = System.IO.Path.GetDirectoryName(collectionPath).Replace('\\', '/');
			var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/Avatar_{avatarAsset.GetInstanceID()}.asset");
			
			AssetDatabase.CreateAsset(avatarAsset, assetPath);
			EditorUtility.SetDirty(avatarAsset);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh(); // Ensure the asset is fully saved
			
			Undo.RecordObject(profile, "Add Avatar");
			profile.avatars.Add(avatarAsset);
			EditorUtility.SetDirty(profile);
			UpdateProfileDetails();
		}

		private void RemoveAvatarFromProfile(AvatarCollection profile, int index)
		{
			if (profile.avatars == null || index < 0 || index >= profile.avatars.Count) return;
			if (EditorUtility.DisplayDialog("Remove Avatar", $"Remove avatar #{index + 1} from this profile?", "Remove", "Cancel"))
			{
				Undo.RecordObject(profile, "Remove Avatar");
				profile.avatars.RemoveAt(index);
			EditorUtility.SetDirty(profile);

				if (_selectedAvatarIndex >= profile.avatars.Count)
				{
			_selectedAvatarIndex = profile.avatars.Count - 1;
				}

				BuildAvatarGridView(profile);
			}
		}

		private async void BuildSelectedProfile()
		{
			if (_selectedProfile == null)
				return;

			// Check for validation errors before proceeding
			var validationErrors = GetValidationErrors();
			if (validationErrors.Count > 0)
			{
				var errorMessage = "Cannot publish due to validation errors:\n\n" + string.Join("\n", validationErrors.Select(e => $"• {e}"));
				EditorUtility.DisplayDialog("Validation Errors", errorMessage + "\n\nPlease fix these issues in the Validation section above.", "OK");
				return;
			}

			try
			{
				EnsureUploadDisclaimerAcknowledged();
			}
			catch (OperationCanceledException)
			{
				return;
			}

			var configs = _selectedProfile.avatars?.ToList();
			if (configs == null || configs.Count == 0)
		{
				EditorUtility.DisplayDialog("Avatar Tools", "This collection has no avatars to build.", "OK");
				return;
			}

			// Remember avatar positions before building
			_avatarPositions.Clear();
			for (int i = 0; i < configs.Count; i++)
			{
				if (configs[i] != null)
				{
					_avatarPositions[configs[i]] = (_selectedProfile, i);
				}
			}

			_isBuilding = true;
			UpdateProgress(0f, "Dispatching builds...");

			int dispatched = 0;
			foreach (var cfg in configs)
			{
				if (cfg == null)
					continue;

				if (cfg.buildPC && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.PC, BuildWorkflow.Publish))
					dispatched++;
				if (cfg.buildQuest && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.Quest, BuildWorkflow.Publish))
					dispatched++;
			}

			_isBuilding = false;
			UpdateProgress(0f, string.Empty);
			UpdateBottomBar();
			if (dispatched == 0)
			{
				_toast?.ShowWarning("No uploads were dispatched. Make sure avatars are selected and configured.", "No Uploads", 4f);
			}
			else
			{
				_toast?.ShowSuccess($"Queued {dispatched} Control Panel upload(s).", "Uploads Queued", 3f);
			}
		}

		private async void TestSelectedProfile()
		{
			if (_selectedProfile == null)
				return;

			var configs = _selectedProfile.avatars?.ToList();
			if (configs == null || configs.Count == 0)
			{
				EditorUtility.DisplayDialog("Avatar Tools", "This collection has no avatars to test.", "OK");
				return;
			}

			// Remember avatar positions before building
			_avatarPositions.Clear();
			for (int i = 0; i < configs.Count; i++)
			{
				if (configs[i] != null)
				{
					_avatarPositions[configs[i]] = (_selectedProfile, i);
				}
			}

			int dispatched = 0;
			foreach (var cfg in configs)
			{
				if (cfg == null)
					continue;

				if (cfg.buildPC && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.PC, BuildWorkflow.TestOnly))
					dispatched++;
				if (cfg.buildQuest && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.Quest, BuildWorkflow.TestOnly))
					dispatched++;
			}

			if (dispatched == 0)
			{
				_toast?.ShowWarning("No avatars were queued for build & test. Make sure avatars are selected and configured.", "No Builds", 4f);
			}
			else
			{
				_toast?.ShowSuccess($"Queued {dispatched} Control Panel test build(s).", "Test Builds Queued", 3f);
			}
		}

		private async void BuildAllAvatars(AvatarCollection profile)
		{
			if (profile == null || profile.avatars == null || profile.avatars.Count == 0)
			{
				_toast?.ShowWarning("No avatars to build in this collection.", "No Avatars", 3f);
				return;
			}

			try
			{
				EnsureUploadDisclaimerAcknowledged();
			}
			catch (OperationCanceledException)
			{
				return;
			}

			var configs = profile.avatars.Where(a => a != null && a.avatarPrefab != null).ToList();
			if (configs.Count == 0)
			{
				_toast?.ShowWarning("No valid avatars to build.", "No Valid Avatars", 3f);
				return;
			}

			// Remember avatar positions
			_avatarPositions.Clear();
			for (int i = 0; i < profile.avatars.Count; i++)
			{
				if (profile.avatars[i] != null)
				{
					_avatarPositions[profile.avatars[i]] = (profile, i);
				}
			}

			_isBuilding = true;
			UpdateProgress(0f, $"Building {configs.Count} avatar(s)...");
			UpdateBottomBar();

			int dispatched = 0;
			int total = configs.Count;
			for (int i = 0; i < configs.Count; i++)
			{
				var cfg = configs[i];
				UpdateProgress((float)i / total, $"Building {cfg.avatarName ?? cfg.avatarPrefab?.name ?? "Avatar"} ({i + 1}/{total})...");

				if (cfg.buildPC && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.PC, BuildWorkflow.BuildOnly))
					dispatched++;
				if (cfg.buildQuest && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.Quest, BuildWorkflow.BuildOnly))
					dispatched++;
			}

			_isBuilding = false;
			UpdateProgress(0f, string.Empty);
			UpdateBottomBar();

			if (dispatched == 0)
			{
				_toast?.ShowWarning("No builds were dispatched. Make sure avatars have platforms enabled.", "No Builds", 4f);
			}
			else
			{
				_toast?.ShowSuccess($"Queued {dispatched} build(s) for {configs.Count} avatar(s).", "Builds Queued", 3f);
			}
		}

		private async void TestAllAvatars(AvatarCollection profile)
		{
			if (profile == null || profile.avatars == null || profile.avatars.Count == 0)
			{
				_toast?.ShowWarning("No avatars to test in this collection.", "No Avatars", 3f);
				return;
			}

			var configs = profile.avatars.Where(a => a != null && a.avatarPrefab != null).ToList();
			if (configs.Count == 0)
			{
				_toast?.ShowWarning("No valid avatars to test.", "No Valid Avatars", 3f);
				return;
			}

			// Remember avatar positions
			_avatarPositions.Clear();
			for (int i = 0; i < profile.avatars.Count; i++)
			{
				if (profile.avatars[i] != null)
				{
					_avatarPositions[profile.avatars[i]] = (profile, i);
				}
			}

			_isBuilding = true;
			UpdateProgress(0f, $"Testing {configs.Count} avatar(s)...");
			UpdateBottomBar();

			int dispatched = 0;
			int total = configs.Count;
			for (int i = 0; i < configs.Count; i++)
			{
				var cfg = configs[i];
				UpdateProgress((float)i / total, $"Testing {cfg.avatarName ?? cfg.avatarPrefab?.name ?? "Avatar"} ({i + 1}/{total})...");

				if (cfg.buildPC && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.PC, BuildWorkflow.TestOnly))
					dispatched++;
				if (cfg.buildQuest && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.Quest, BuildWorkflow.TestOnly))
					dispatched++;
			}

			_isBuilding = false;
			UpdateProgress(0f, string.Empty);
			UpdateBottomBar();

			if (dispatched == 0)
			{
				_toast?.ShowWarning("No test builds were dispatched. Make sure avatars have platforms enabled.", "No Builds", 4f);
			}
			else
			{
				_toast?.ShowSuccess($"Queued {dispatched} test build(s) for {configs.Count} avatar(s).", "Test Builds Queued", 3f);
			}
		}

		private async void PublishAllAvatars(AvatarCollection profile)
		{
			if (profile == null || profile.avatars == null || profile.avatars.Count == 0)
			{
				_toast?.ShowWarning("No avatars to publish in this collection.", "No Avatars", 3f);
				return;
			}

			try
			{
				EnsureUploadDisclaimerAcknowledged();
			}
			catch (OperationCanceledException)
			{
				return;
			}

			var configs = profile.avatars.Where(a => a != null && a.avatarPrefab != null).ToList();
			if (configs.Count == 0)
			{
				_toast?.ShowWarning("No valid avatars to publish.", "No Valid Avatars", 3f);
				return;
			}

			// Remember avatar positions
			_avatarPositions.Clear();
			for (int i = 0; i < profile.avatars.Count; i++)
			{
				if (profile.avatars[i] != null)
				{
					_avatarPositions[profile.avatars[i]] = (profile, i);
				}
			}

			_isBuilding = true;
			UpdateProgress(0f, $"Publishing {configs.Count} avatar(s)...");
			UpdateBottomBar();

			int dispatched = 0;
			int total = configs.Count;
			for (int i = 0; i < configs.Count; i++)
			{
				var cfg = configs[i];
				UpdateProgress((float)i / total, $"Publishing {cfg.avatarName ?? cfg.avatarPrefab?.name ?? "Avatar"} ({i + 1}/{total})...");

				if (cfg.buildPC && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.PC, BuildWorkflow.Publish))
					dispatched++;
				if (cfg.buildQuest && await DispatchControlPanelBuild(cfg, PlatformSwitcher.BuildPlatform.Quest, BuildWorkflow.Publish))
					dispatched++;
			}

			_isBuilding = false;
			UpdateProgress(0f, string.Empty);
			UpdateBottomBar();

			if (dispatched == 0)
			{
				_toast?.ShowWarning("No uploads were dispatched. Make sure avatars have platforms enabled.", "No Uploads", 4f);
			}
			else
			{
				_toast?.ShowSuccess($"Queued {dispatched} upload(s) for {configs.Count} avatar(s).", "Uploads Queued", 3f);
			}
		}

		private async Task<bool> DispatchControlPanelBuild(AvatarAsset config, PlatformSwitcher.BuildPlatform platform, BuildWorkflow workflow)
		{
			if (config == null || config.avatarPrefab == null)
			{
				Debug.LogWarning("[AvatarUploader] Cannot build because the avatar prefab is missing.");
				return false;
			}

			var descriptorRoot = EnsureDescriptorRoot(config.avatarPrefab, config);
			if (descriptorRoot == null)
				return false;

			VRCAvatar avatarPayload = default;
			string thumbnailPath = null;
			if (workflow == BuildWorkflow.Publish)
			{
				if (!TryPrepareAvatarPayload(config, platform, out avatarPayload, out thumbnailPath))
				return false;
			}

			// Store original build target to restore later
			var originalBuildTarget = EditorUserBuildSettings.activeBuildTarget;
			var originalBuildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
			var targetBuildTarget = platform == PlatformSwitcher.BuildPlatform.PC 
				? BuildTarget.StandaloneWindows64 
				: BuildTarget.Android;
			var targetBuildTargetGroup = platform == PlatformSwitcher.BuildPlatform.PC 
				? BuildTargetGroup.Standalone 
				: BuildTargetGroup.Android;

			// Check if we need to switch platforms
			bool needsPlatformSwitch = originalBuildTarget != targetBuildTarget;
			bool platformSwitched = false;

			if (needsPlatformSwitch)
			{
				// Ask user before switching (like VRChat SDK does)
				var platformName = platform == PlatformSwitcher.BuildPlatform.PC ? "PC (Windows)" : "Quest (Android)";
				if (!EditorUtility.DisplayDialog("Build Target Switcher",
					$"This build requires switching your build target to {platformName}. This could take a while.\n\n" +
					"Your current build target will be restored after the build completes.",
					"Switch & Build", "Cancel"))
				{
					return false;
				}

				// Switch the build target
				EditorUserBuildSettings.selectedBuildTargetGroup = targetBuildTargetGroup;
				var switched = EditorUserBuildSettings.SwitchActiveBuildTargetAsync(targetBuildTargetGroup, targetBuildTarget);
				if (!switched)
				{
					EditorUtility.DisplayDialog("Build Error", 
						$"Failed to switch to {platformName} build target.\n\n" +
						"Check if the Platform Support is installed in Unity Hub.",
						"OK");
					return false;
				}

				// Wait for platform switch to complete
				while (EditorUserBuildSettings.activeBuildTarget != targetBuildTarget)
				{
					await Task.Delay(100);
				}

				platformSwitched = true;
			}

			try
			{
				var state = await ControlPanelBridge.GetStateAsync();
				state.Builder.SelectAvatar(descriptorRoot);

				// Ensure PipelineManager has a blueprintId before building
				// For Build & Test, the SDK will assign one automatically, but we need to ensure the component exists
				if (!descriptorRoot.TryGetComponent<VRC.Core.PipelineManager>(out var pipelineManager))
				{
					EditorUtility.DisplayDialog("Build Error", 
						"The avatar prefab must have a PipelineManager component.", "OK");
					return false;
				}

				// For Build & Test, if there's no blueprintId, the SDK will assign one automatically
				// For Build & Publish, the blueprintId must exist (it will be created during ReserveAvatarId)
				// For Build Only, we need to ensure there's a blueprintId
				if (workflow == BuildWorkflow.BuildOnly && string.IsNullOrWhiteSpace(pipelineManager.blueprintId))
				{
					Undo.RecordObject(pipelineManager, "Assign Blueprint ID");
					pipelineManager.AssignId();
					EditorUtility.SetDirty(pipelineManager);
				}

				// Subscribe to progress events if the builder is VRCSdkControlPanelAvatarBuilder
				EventHandler<(string status, float percentage)> uploadProgressHandler = null;
				EventHandler<string> buildProgressHandler = null;
				EventHandler<string> uploadSuccessHandler = null;
				EventHandler<string> uploadFinishHandler = null;
				
				if (state.Builder is VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder builderImpl)
				{
					_isBuilding = true;
					UpdateProgress(0f, "Starting build...");
					
					uploadProgressHandler = (sender, args) =>
					{
						// Switch to main thread before updating UI (like Control Panel does)
						// Use EditorApplication.delayCall to ensure we're on the main thread
						EditorApplication.delayCall += () =>
						{
							UpdateProgress(args.percentage, args.status);
						};
					};
					
					buildProgressHandler = (sender, status) =>
					{
						UpdateProgress(0.5f, status);
					};
					
					uploadSuccessHandler = (sender, avatarId) =>
					{
						// The avatarId parameter IS the blueprint ID that was assigned
						// The SDK sets pM.blueprintId = avatar.ID in ReserveAvatarId
						// So we can use this directly, but we'll also verify by reading from PipelineManager
						if (!string.IsNullOrEmpty(avatarId) && config != null)
						{
							// Update immediately with the ID from the event
							config.SetBlueprintId(platform, avatarId);
							EditorUtility.SetDirty(config);
							
							// Show success toast
							var avatarName = string.IsNullOrEmpty(config.avatarName) ? config.avatarPrefab?.name ?? "Avatar" : config.avatarName;
							_toast?.ShowSuccess($"Avatar '{avatarName}' uploaded successfully! Blueprint ID: {avatarId}", "Upload Complete", 5f);
						}
					};
					
					uploadFinishHandler = (sender, message) =>
					{
						// Verify blueprint ID from PipelineManager after upload finishes
						// The SDK sets pM.blueprintId = avatar.ID in ReserveAvatarId (line 3508)
						// This is set on the PREFAB ASSET, not a scene instance
						// We verify by reading from the prefab asset file, exactly like SyncBlueprintIdFromComponent does
						EditorApplication.delayCall += () =>
						{
							// First, try to read from the prefab asset's PipelineManager (most reliable)
							string blueprintIdToUse = null;
							
							if (config != null && config.avatarPrefab != null)
							{
								// Get the prefab asset path - this is the KEY: we need the PREFAB ASSET, not scene instance
								var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(config.avatarPrefab);
								if (string.IsNullOrEmpty(assetPath))
								{
									// If it's already a prefab asset, get its path directly
									assetPath = AssetDatabase.GetAssetPath(config.avatarPrefab);
								}
								
								if (!string.IsNullOrEmpty(assetPath))
								{
									// Load the prefab asset from disk (this is the actual asset file, not a scene instance)
									var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
									if (prefabAsset != null && prefabAsset.TryGetComponent<VRC.Core.PipelineManager>(out var pm))
									{
										// The SDK sets pm.blueprintId = avatar.ID in ReserveAvatarId
										// So we read it directly from the prefab asset's PipelineManager
										if (!string.IsNullOrEmpty(pm.blueprintId))
										{
											blueprintIdToUse = pm.blueprintId;
										}
									}
								}
							}
							
							// If we still don't have it, try the builder's selected avatar (but this might be a scene instance)
							if (string.IsNullOrEmpty(blueprintIdToUse))
							{
								var selectedAvatar = builderImpl.SelectedAvatar;
								if (selectedAvatar != null)
								{
									// Try to get the prefab asset from the selected avatar
									var selectedAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selectedAvatar);
									if (!string.IsNullOrEmpty(selectedAssetPath))
									{
										var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(selectedAssetPath);
										if (prefabAsset != null && prefabAsset.TryGetComponent<VRC.Core.PipelineManager>(out var pm))
										{
											if (!string.IsNullOrEmpty(pm.blueprintId))
											{
												blueprintIdToUse = pm.blueprintId;
											}
										}
									}
									else if (selectedAvatar.TryGetComponent<VRC.Core.PipelineManager>(out var pm))
									{
										// Fallback to scene instance if prefab asset path not available
										if (!string.IsNullOrEmpty(pm.blueprintId))
										{
											blueprintIdToUse = pm.blueprintId;
										}
									}
								}
							}
							
							// Update the AvatarAsset with the blueprint ID we found
							if (!string.IsNullOrEmpty(blueprintIdToUse) && config != null)
							{
								config.SetBlueprintId(platform, blueprintIdToUse);
								
								// Restore avatar position if it was tracked
								if (_avatarPositions.TryGetValue(config, out var position))
								{
									// Ensure the avatar is still in the collection at the expected position
									if (position.collection != null && position.collection.avatars != null)
									{
										var currentIndex = position.collection.avatars.IndexOf(config);
										if (currentIndex != position.index && currentIndex >= 0)
										{
											// Avatar moved, restore it to original position
											Undo.RecordObject(position.collection, "Restore Avatar Position");
											position.collection.avatars.RemoveAt(currentIndex);
											if (position.index < position.collection.avatars.Count)
											{
												position.collection.avatars.Insert(position.index, config);
											}
											else
											{
												position.collection.avatars.Add(config);
											}
											EditorUtility.SetDirty(position.collection);
										}
									}
								}
								
								// Refresh the UI to show the updated blueprint ID
								if (_currentHeroConfig == config)
								{
									UpdateHeroPanel(_selectedProfile, config, _selectedAvatarIndex);
								}
							}
						};
					};
					
					builderImpl.OnSdkUploadProgress += uploadProgressHandler;
					builderImpl.OnSdkBuildProgress += buildProgressHandler;
					builderImpl.OnSdkUploadSuccess += uploadSuccessHandler;
					builderImpl.OnSdkUploadFinish += uploadFinishHandler;
				}

				try
				{
					switch (workflow)
					{
						case BuildWorkflow.Publish:
							// Reserve avatar ID and handle copyright agreement ourselves before BuildAndUpload
							// BuildAndUpload will see the blueprint ID is already set and copyright is already agreed,
							// so it will skip ReserveAvatarId and CheckCopyrightAgreement
							if (!await PrepareAvatarForUpload(config, pipelineManager, descriptorRoot, avatarPayload))
							{
								_toast?.ShowError("Failed to prepare avatar for upload.", "Upload Cancelled", 5f);
								return false;
							}
							
							// Call BuildAndUpload directly - it will use the blueprint ID we set and skip copyright check
							await state.Builder.BuildAndUpload(descriptorRoot, avatarPayload, thumbnailPath);
							break;
						case BuildWorkflow.TestOnly:
							// Set build type to Test so SDK knows to assign ID if needed
							VRC.SDKBase.Editor.VRC_SdkBuilder.ActiveBuildType = VRC.SDKBase.Editor.VRC_SdkBuilder.BuildType.Test;
							await state.Builder.BuildAndTest(descriptorRoot);
							break;
						default:
							await state.Builder.Build(descriptorRoot);
							break;
					}

					return true;
			}
			finally
			{
					// Unsubscribe from events
					if (state.Builder is VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder builderImpl2)
					{
						if (uploadProgressHandler != null)
							builderImpl2.OnSdkUploadProgress -= uploadProgressHandler;
						if (buildProgressHandler != null)
							builderImpl2.OnSdkBuildProgress -= buildProgressHandler;
						if (uploadSuccessHandler != null)
							builderImpl2.OnSdkUploadSuccess -= uploadSuccessHandler;
						if (uploadFinishHandler != null)
							builderImpl2.OnSdkUploadFinish -= uploadFinishHandler;
					}
					
				_isBuilding = false;
					UpdateProgress(0f, string.Empty);
					UpdateBottomBar(); // Update button states
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Control Panel build failed: {ex.Message}");
				
				// Provide more helpful error messages for common issues
				string errorMessage = ex.Message;
				if (ex.Message.Contains("main thread") || ex.Message.Contains("YGNodeStyleSetWidthPercent"))
				{
					errorMessage = "Upload failed due to a threading issue in the VRChat SDK. This is a known SDK limitation. Try uploading again - it may succeed on retry.";
				}
				else if (ex.Message.Contains("Failed to upload file") || ex.Message.Contains("upload"))
				{
					errorMessage = "File upload failed. This may be due to network issues or SDK limitations. Check your internet connection and try again.";
				}
				
				_toast?.ShowError($"Build failed: {errorMessage}", "Build Error", 6f);
				_isBuilding = false;
				UpdateProgress(0f, string.Empty);
				UpdateBottomBar();
				return false;
			}
			finally
			{
				// Restore original build target if we switched it
				if (platformSwitched && originalBuildTarget != EditorUserBuildSettings.activeBuildTarget)
				{
					EditorUserBuildSettings.selectedBuildTargetGroup = originalBuildTargetGroup;
					EditorUserBuildSettings.SwitchActiveBuildTargetAsync(originalBuildTargetGroup, originalBuildTarget);
				}

				// Clean up thumbnail temp file
				if (!string.IsNullOrEmpty(thumbnailPath) && File.Exists(thumbnailPath))
				{
					File.Delete(thumbnailPath);
				}
			}
		}

		private bool TryPrepareAvatarPayload(AvatarAsset config, PlatformSwitcher.BuildPlatform platform, out VRCAvatar avatar, out string thumbnailPath)
		{
			avatar = default;
			thumbnailPath = null;

			var resolvedName = string.IsNullOrWhiteSpace(config.avatarName)
				? config.avatarPrefab != null ? config.avatarPrefab.name : "Avatar"
				: config.avatarName.Trim();

			if (string.IsNullOrEmpty(resolvedName))
			{
				EditorUtility.DisplayDialog("Avatar Tools", "Cannot publish an avatar without a name.", "OK");
				return false;
			}

			var blueprintId = GetBlueprintIdForPlatform(config, platform);
			if (string.IsNullOrEmpty(blueprintId) && config.avatarIcon == null)
			{
				EditorUtility.DisplayDialog("Avatar Tools", $"Avatar '{resolvedName}' needs an icon before the first upload.", "OK");
				return false;
			}

			var tags = config.tags != null && config.tags.Count > 0
				? new List<string>(config.tags)
				: new List<string> { "avatar" };

			avatar = new VRCAvatar
			{
				ID = blueprintId,
				Name = resolvedName,
				Description = config.description ?? string.Empty,
				Tags = tags,
				ReleaseStatus = config.releaseStatus == ReleaseStatus.Public ? "public" : "private",
				Styles = new VRCAvatar.AvatarStyles(),
				AuthorName = APIUser.CurrentUser?.displayName,
				AuthorId = APIUser.CurrentUser?.id
			};

			thumbnailPath = TryCreateTemporaryThumbnail(config);

			return true;
		}

		private static string GetBlueprintIdForPlatform(AvatarAsset config, PlatformSwitcher.BuildPlatform platform)
		{
			if (config == null)
				return null;

			if (platform == PlatformSwitcher.BuildPlatform.Quest)
			{
				if (!string.IsNullOrEmpty(config.blueprintIdQuest))
					return config.blueprintIdQuest;

				if (config.useSameBlueprintId)
					return config.blueprintIdPC;
			}

			return config.blueprintIdPC;
		}

		private static string TryCreateTemporaryThumbnail(AvatarAsset config)
		{
			var texture = config.avatarIcon;
			if (texture == null)
				return null;

			try
			{
				var directory = Path.Combine(Path.GetTempPath(), "YUCP.AvatarTools.Thumbnails");
				if (!Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}

				var safeName = MakeSafeFileName(string.IsNullOrEmpty(config.avatarName) ? texture.name : config.avatarName);
				var filePath = Path.Combine(directory, $"{safeName}_{Guid.NewGuid():N}.png");
				var pngData = texture.EncodeToPNG();
				if (pngData == null || pngData.Length == 0)
				{
					Debug.LogWarning("[AvatarUploader] Unable to encode avatar icon to PNG.");
					return null;
				}
				File.WriteAllBytes(filePath, pngData);
				return filePath;
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to create thumbnail: {ex.Message}");
				return null;
			}
		}

		private static string MakeSafeFileName(string value)
		{
			if (string.IsNullOrEmpty(value))
				return "avatar";

			foreach (var invalid in Path.GetInvalidFileNameChars())
			{
				value = value.Replace(invalid, '_');
			}

			return value;
		}

	// Track temporary avatar instances created for Control Panel (when prefabs aren't in active scene)
	private static Dictionary<GameObject, GameObject> _temporaryInstances = new Dictionary<GameObject, GameObject>();

	internal static GameObject EnsureDescriptorRootStatic(GameObject input)
	{
		return EnsureDescriptorRoot(input, null);
	}

	internal static GameObject EnsureDescriptorRootStatic(GameObject input, AvatarAsset config)
	{
		return EnsureDescriptorRoot(input, config);
	}

	private static GameObject EnsureDescriptorRoot(GameObject input, AvatarAsset config = null)
	{
		if (input == null)
			return null;

		// Get the active scene (Control Panel only sees objects in the active scene)
		var activeScene = SceneManager.GetActiveScene();
		if (!activeScene.IsValid())
		{
			Debug.LogWarning("[AvatarUploader] No valid active scene. Control Panel requires an active scene.");
			return null;
		}

		// First, check if AvatarAsset has a stored scene instance we should use
		GameObject sourceInstance = null;
		VRCAvatarDescriptor sourceDescriptor = null;
		if (config != null)
		{
			sourceInstance = config.GetSourceSceneInstance();
			if (sourceInstance != null)
			{
				sourceDescriptor = FindDescriptor(sourceInstance);
				if (sourceDescriptor != null)
				{
					Debug.Log($"[AvatarUploader] Found stored scene instance for '{input.name}' from scene '{config.sourceScenePath}'");
					
					// If the source instance is in the active scene, use it directly
					if (sourceInstance.scene == activeScene)
					{
						Debug.Log($"[AvatarUploader] Using source scene instance directly (already in active scene)");
						return sourceDescriptor.gameObject;
					}
					
					// Otherwise, we'll need to copy the descriptor to a temporary instance in active scene
					// (handled later in the code)
				}
			}
		}

		var descriptor = FindDescriptor(input);
		if (descriptor != null)
			return descriptor.gameObject;

		// Check if input is a prefab asset or get the prefab asset from an instance
		GameObject prefabAsset = null;
		if (PrefabUtility.IsPartOfPrefabAsset(input))
		{
			// Input is already a prefab asset
			prefabAsset = input;
			descriptor = FindDescriptor(prefabAsset);
			if (descriptor != null)
				return descriptor.gameObject;
		}
		else
		{
			// Input might be an instance, try to get the prefab asset
			prefabAsset = GetPrefabAssetRoot(input);
			if (prefabAsset != null && prefabAsset != input)
			{
				descriptor = FindDescriptor(prefabAsset);
				if (descriptor != null)
					return descriptor.gameObject;
			}
		}

		// Search for existing instances in the ACTIVE SCENE (what Control Panel sees)
		foreach (var candidate in Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>())
		{
			if (candidate == null || candidate.gameObject == null)
				continue;

			// Only consider objects in the active scene (what Control Panel sees)
			if (candidate.gameObject.scene != activeScene)
				continue;

			var source = PrefabUtility.GetCorrespondingObjectFromSource(candidate.gameObject);
			if (ReferenceEquals(candidate.gameObject, input) ||
			    ReferenceEquals(source, input) ||
			    ReferenceEquals(source, prefabAsset))
			{
				return candidate.gameObject;
			}
		}

		// If no instance found in active scene and we have a prefab asset, instantiate temporarily
		var targetPrefab = prefabAsset ?? input;
		
		// Verify targetPrefab is actually a prefab asset (check if it's in AssetDatabase)
		if (targetPrefab != null && PrefabUtility.IsPartOfPrefabAsset(targetPrefab))
		{
			// Check if we already have a temporary instance in the active scene
			if (_temporaryInstances.TryGetValue(targetPrefab, out var existingInstance))
			{
				if (existingInstance != null && existingInstance.scene == activeScene)
				{
					descriptor = FindDescriptor(existingInstance);
					if (descriptor != null)
					{
						Debug.Log($"[AvatarUploader] Reusing existing temporary instance of '{targetPrefab.name}' in active scene");
						return descriptor.gameObject;
					}
				}
				else
				{
					// Clean up stale instance from different scene
					if (existingInstance != null)
						UnityEngine.Object.DestroyImmediate(existingInstance);
					_temporaryInstances.Remove(targetPrefab);
				}
			}

			// Use source descriptor if we have one and it's not in active scene (already set above)
			if (sourceDescriptor != null && sourceInstance != null && sourceInstance.scene != activeScene)
			{
				Debug.Log($"[AvatarUploader] Will copy descriptor from source scene instance for prefab '{targetPrefab.name}' in scene '{config.sourceScenePath}'");
			}

			// Create temporary instance in the active scene
			// Note: We instantiate first, then check for descriptor, because the descriptor
			// might be on a child object that's only accessible after instantiation
			try
			{
				Debug.Log($"[AvatarUploader] Instantiating prefab '{targetPrefab.name}' temporarily in active scene '{activeScene.name}' for Control Panel");
				var instance = PrefabUtility.InstantiatePrefab(targetPrefab, activeScene) as GameObject;
				if (instance != null)
				{
					// Ensure instance is active so Control Panel can find it
					instance.SetActive(true);
					
					// Hide it from hierarchy but keep it active and in scene for Control Panel
					// Control Panel needs active objects to find them via FindSceneObjectsOfTypeAll
					instance.hideFlags = HideFlags.HideAndDontSave;
					
					// Find descriptor on the instantiated object (it might be on a child)
					descriptor = FindDescriptor(instance);
					
					// If not found and we have a source descriptor, copy it to the instance
					if (descriptor == null && sourceDescriptor != null)
					{
						Debug.Log($"[AvatarUploader] No descriptor on instantiated prefab, copying descriptor from source scene instance...");
						
						// Find the root object where we should add the descriptor
						// (usually the same as the source descriptor's root)
						var descriptorRoot = instance;
						if (sourceDescriptor.gameObject != sourceInstance)
						{
							// Source descriptor is on a child - find corresponding child in instance
							var sourcePath = GetRelativePath(sourceInstance, sourceDescriptor.gameObject);
							descriptorRoot = FindChildByPath(instance, sourcePath);
							if (descriptorRoot == null)
								descriptorRoot = instance; // Fallback to root
						}
						
						// Add descriptor component and copy all data (visemes, etc.)
						descriptor = descriptorRoot.AddComponent<VRCAvatarDescriptor>();
						EditorUtility.CopySerialized(sourceDescriptor, descriptor);
						
						Debug.Log($"[AvatarUploader] Copied descriptor from source scene instance to temporary instance (includes visemes and other data)");
					}
					
					// Also copy PipelineManager from source scene instance if it exists and temporary instance doesn't have one with blueprintId
					if (sourceInstance != null)
					{
						var sourcePMs = sourceInstance.GetComponentsInChildren<PipelineManager>(true);
						foreach (var sourcePM in sourcePMs)
						{
							// Find corresponding GameObject in temporary instance
							GameObject targetPMObj = null;
							if (sourcePM.gameObject == sourceInstance)
							{
								targetPMObj = instance;
							}
							else
							{
								var sourcePath = GetRelativePath(sourceInstance, sourcePM.gameObject);
								targetPMObj = FindChildByPath(instance, sourcePath);
								if (targetPMObj == null)
									targetPMObj = instance; // Fallback to root
							}
							
							// Check if target already has a PipelineManager with blueprintId
							if (targetPMObj.TryGetComponent<PipelineManager>(out var targetPM))
							{
								// If source has blueprintId but target doesn't, copy it
								if (!string.IsNullOrEmpty(sourcePM.blueprintId) && string.IsNullOrEmpty(targetPM.blueprintId))
								{
									Debug.Log($"[AvatarUploader] Copying PipelineManager blueprintId from source scene instance to temporary instance...");
									EditorUtility.CopySerialized(sourcePM, targetPM);
									Debug.Log($"[AvatarUploader] Copied PipelineManager data (including blueprintId '{sourcePM.blueprintId}') from '{sourcePM.gameObject.name}' to '{targetPMObj.name}'");
								}
							}
							else
							{
								// Target doesn't have PipelineManager, add it and copy data
								if (!string.IsNullOrEmpty(sourcePM.blueprintId))
								{
									Debug.Log($"[AvatarUploader] Adding PipelineManager to temporary instance and copying from source scene instance...");
									var newPM = targetPMObj.AddComponent<PipelineManager>();
									EditorUtility.CopySerialized(sourcePM, newPM);
									Debug.Log($"[AvatarUploader] Copied PipelineManager (including blueprintId '{sourcePM.blueprintId}') from '{sourcePM.gameObject.name}' to '{targetPMObj.name}'");
								}
							}
						}
					}
					
					// If still not found, try searching all objects in the scene that match this prefab
					if (descriptor == null)
					{
						Debug.LogWarning($"[AvatarUploader] FindDescriptor returned null for '{instance.name}'. Searching scene for matching descriptor...");
						foreach (var candidate in Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>())
						{
							if (candidate == null || candidate.gameObject == null)
								continue;
								
							if (candidate.gameObject.scene == activeScene)
							{
								var source = PrefabUtility.GetCorrespondingObjectFromSource(candidate.gameObject);
								if (ReferenceEquals(source, targetPrefab) || ReferenceEquals(candidate.gameObject, instance))
								{
									descriptor = candidate;
									Debug.Log($"[AvatarUploader] Found descriptor via scene search: {descriptor.name} on {descriptor.gameObject.name}");
									break;
								}
							}
						}
					}
					
					if (descriptor != null)
					{
						// Store reference for cleanup
						_temporaryInstances[targetPrefab] = instance;
						
						Debug.Log($"[AvatarUploader] Successfully created temporary instance of '{targetPrefab.name}' in active scene for Control Panel. Descriptor: {descriptor.name} on {descriptor.gameObject.name}");
						return descriptor.gameObject;
					}
					else
					{
						// Clean up if descriptor not found
						Debug.LogWarning($"[AvatarUploader] Instantiated '{targetPrefab.name}' but could not find VRCAvatarDescriptor on instance or its children. Instance active: {instance.activeSelf}, Scene: {instance.scene.name}. Cleaning up.");
						UnityEngine.Object.DestroyImmediate(instance);
					}
				}
				else
				{
					Debug.LogWarning($"[AvatarUploader] PrefabUtility.InstantiatePrefab returned null for '{targetPrefab.name}'");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AvatarUploader] Failed to instantiate prefab '{targetPrefab.name}' in active scene: {ex.Message}\n{ex.StackTrace}");
			}
		}
		else
		{
			Debug.LogWarning($"[AvatarUploader] '{input.name}' is not a prefab asset. Cannot instantiate for Control Panel. IsPartOfPrefabAsset: {targetPrefab != null && PrefabUtility.IsPartOfPrefabAsset(targetPrefab)}");
		}

		Debug.LogWarning($"[AvatarUploader] No VRCAvatarDescriptor found on '{input.name}'. Control Panel may reject it.");
		return null;
	}

	/// <summary>
	/// Get the relative path from a parent GameObject to a child GameObject.
	/// </summary>
	private static string GetRelativePath(GameObject parent, GameObject child)
	{
		if (parent == null || child == null)
			return null;

		var path = new List<string>();
		var current = child.transform;
		var parentTransform = parent.transform;

		while (current != null && current != parentTransform)
		{
			path.Insert(0, current.name);
			current = current.parent;
		}

		if (current != parentTransform)
			return null; // Child is not actually a child of parent

		return string.Join("/", path);
	}

	/// <summary>
	/// Find a child GameObject by relative path from a parent.
	/// </summary>
	private static GameObject FindChildByPath(GameObject parent, string path)
	{
		if (parent == null || string.IsNullOrEmpty(path))
			return null;

		var parts = path.Split('/');
		var current = parent.transform;

		foreach (var part in parts)
		{
			var child = current.Find(part);
			if (child == null)
				return null;
			current = child;
		}

		return current.gameObject;
	}

	/// <summary>
	/// Clean up temporary avatar instances, optionally only from scenes other than the active one.
	/// </summary>
	private static void CleanupTemporaryInstances(bool onlyFromOtherScenes = false)
	{
		var activeScene = SceneManager.GetActiveScene();
		var toRemove = new List<GameObject>();

		foreach (var kvp in _temporaryInstances)
		{
			if (kvp.Value == null)
			{
				toRemove.Add(kvp.Key);
				continue;
			}

			// If only cleaning up from other scenes, skip instances in active scene
			if (onlyFromOtherScenes && kvp.Value.scene == activeScene && activeScene.IsValid())
				continue;

			// Destroy the instance
			UnityEngine.Object.DestroyImmediate(kvp.Value);
			toRemove.Add(kvp.Key);
		}

		foreach (var key in toRemove)
		{
			_temporaryInstances.Remove(key);
		}
	}

		private static VRCAvatarDescriptor FindDescriptor(GameObject root)
		{
			if (root == null)
				return null;

			return root.GetComponent<VRCAvatarDescriptor>() ??
			       root.GetComponentInChildren<VRCAvatarDescriptor>(true) ??
			       root.GetComponentInParent<VRCAvatarDescriptor>(true);
		}

		private static GameObject GetPrefabAssetRoot(GameObject instance)
		{
			if (instance == null)
				return null;

			var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
			if (string.IsNullOrEmpty(assetPath))
				return null;

			return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
		}

		/// <summary>
		/// Try to sync blueprint ID from Control Panel's selected avatar.
		/// Returns true if blueprint ID was successfully synced, false otherwise.
		/// </summary>
		private bool TrySyncBlueprintIdFromControlPanel(AvatarAsset config)
		{
			if (config == null || config.avatarPrefab == null)
				return false;

			try
			{
				// Check if Control Panel has a builder and selected avatar
				if (!ControlPanelBridge.TryGetBuilder(out var builder))
					return false;

				// Use reflection to get the selected avatar from the builder
				var builderType = builder.GetType();
				var selectedAvatarProperty = builderType.GetProperty("SelectedAvatar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (selectedAvatarProperty == null)
					return false;

				var selectedAvatar = selectedAvatarProperty.GetValue(builder) as GameObject;
				if (selectedAvatar == null)
					return false;

				// Check if the selected avatar matches our config's prefab
				var configDescriptorRoot = EnsureDescriptorRoot(config.avatarPrefab);
				if (configDescriptorRoot == null)
					return false;

				var selectedDescriptorRoot = EnsureDescriptorRoot(selectedAvatar);
				if (selectedDescriptorRoot == null)
					return false;

				// Check if they match (same prefab asset)
				var configPrefab = PrefabUtility.GetCorrespondingObjectFromSource(configDescriptorRoot);
				var selectedPrefab = PrefabUtility.GetCorrespondingObjectFromSource(selectedDescriptorRoot);
				
				if (!ReferenceEquals(configPrefab, selectedPrefab) && !ReferenceEquals(configDescriptorRoot, selectedDescriptorRoot))
					return false;

				// Get PipelineManager from the selected avatar
				if (selectedDescriptorRoot.TryGetComponent<PipelineManager>(out var pm))
				{
					if (!string.IsNullOrEmpty(pm.blueprintId))
					{
						Undo.RecordObject(config, "Sync Blueprint ID from Control Panel");
						config.blueprintIdPC = pm.blueprintId;
						config.blueprintIdQuest = pm.blueprintId;
						EditorUtility.SetDirty(config);
						return true;
					}
				}

				return false;
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to sync blueprint ID from Control Panel: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Sync all avatar metadata from Control Panel (name, description, tags, release status).
		/// Control Panel is the source of truth, so we always sync when it has data.
		/// Returns true if any data was synced, false otherwise.
		/// </summary>
		private bool TrySyncAvatarMetadataFromControlPanel(AvatarAsset config)
		{
			if (config == null)
			{
				Debug.LogWarning("[AvatarUploader] TrySyncAvatarMetadataFromControlPanel: config is null");
				return false;
			}

			try
			{
				// Try to use the cached builder first, then fallback to getting a fresh one
				IVRCSdkControlPanelBuilder builder = _controlPanelBuilder;
				if (builder == null)
				{
					if (!ControlPanelBridge.TryGetControlPanelBuilder(out builder))
					{
						return false;
					}
				}

				// Verify that the Control Panel has the correct avatar selected
				// Use the same verification logic as TrySyncBlueprintIdFromControlPanel
				if (!ControlPanelBridge.TryGetBuilder(out var builderForVerification))
					return false;

				var builderType = builderForVerification.GetType();
				var selectedAvatarProperty = builderType.GetProperty("SelectedAvatar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (selectedAvatarProperty == null)
					return false;

				var selectedAvatar = selectedAvatarProperty.GetValue(builderForVerification) as GameObject;
				if (selectedAvatar == null || config.avatarPrefab == null)
					return false;

				// Check if the selected avatar matches our config's prefab
				var configDescriptorRoot = EnsureDescriptorRoot(config.avatarPrefab);
				if (configDescriptorRoot == null)
					return false;

				var selectedDescriptorRoot = EnsureDescriptorRoot(selectedAvatar);
				if (selectedDescriptorRoot == null)
					return false;

				// Verify they match (same prefab asset)
				var configPrefab = PrefabUtility.GetCorrespondingObjectFromSource(configDescriptorRoot);
				var selectedPrefab = PrefabUtility.GetCorrespondingObjectFromSource(selectedDescriptorRoot);
				
				if (!ReferenceEquals(configPrefab, selectedPrefab) && !ReferenceEquals(configDescriptorRoot, selectedDescriptorRoot))
				{
					// Different avatar is selected in Control Panel, don't sync to prevent wrong data
					return false;
				}

				bool synced = false;
				Undo.RecordObject(config, "Sync Avatar Metadata from Control Panel");

				// Helper to check if string is placeholder text
				bool IsPlaceholderText(string text)
				{
					if (string.IsNullOrEmpty(text))
						return false;
					return text.Contains("Enter your avatar") || text.Contains("...");
				}

				// Sync name (Control Panel is source of truth)
				// Don't sync placeholder text, and set empty strings to empty (not placeholder)
				if (ControlPanelDataBridge.TryGetAvatarName(builder, out var name))
				{
					// Filter out placeholder text
					if (IsPlaceholderText(name))
					{
						name = string.Empty;
					}
					
					if (config.avatarName != name)
					{
						config.avatarName = name ?? string.Empty;
						synced = true;
					}
				}

				// Sync description (Control Panel is source of truth)
				// Don't sync placeholder text, and set empty strings to empty (not placeholder)
				if (ControlPanelDataBridge.TryGetAvatarDescription(builder, out var description))
				{
					// Filter out placeholder text
					if (IsPlaceholderText(description))
					{
						description = string.Empty;
					}
					
					if (config.description != description)
					{
						config.description = description ?? string.Empty;
						synced = true;
					}
				}

				// Sync tags (Control Panel is source of truth)
				if (ControlPanelDataBridge.TryGetAvatarTags(builder, out var tags))
				{
					if (tags != null)
					{
						if (config.tags == null || !tags.SequenceEqual(config.tags))
						{
							config.tags = new List<string>(tags);
							synced = true;
						}
					}
				}

				// Sync release status (Control Panel is source of truth)
				if (ControlPanelDataBridge.TryGetReleaseStatus(builder, out var status))
				{
					if (!string.IsNullOrEmpty(status))
					{
						var newStatus = status.Equals("public", StringComparison.OrdinalIgnoreCase) 
							? ReleaseStatus.Public 
							: ReleaseStatus.Private;
						
						if (config.releaseStatus != newStatus)
						{
							config.releaseStatus = newStatus;
							synced = true;
						}
					}
				}

				// Sync thumbnail/icon (Control Panel is source of truth)
				// Always sync from Control Panel to keep icon up-to-date
				if (ControlPanelDataBridge.TryGetAvatarThumbnail(builder, out var thumbnail))
				{
					if (thumbnail != null)
					{
						if (config.avatarIcon != thumbnail)
						{
							Debug.Log($"[AvatarUploader] TrySyncAvatarMetadataFromControlPanel: Extracted thumbnail from Control Panel ({thumbnail.width}x{thumbnail.height})");
							config.avatarIcon = thumbnail;
							synced = true;
						}
						else
						{
							Debug.Log($"[AvatarUploader] TrySyncAvatarMetadataFromControlPanel: Thumbnail already matches, skipping");
						}
					}
					else
					{
						Debug.Log($"[AvatarUploader] TrySyncAvatarMetadataFromControlPanel: TryGetAvatarThumbnail returned true but thumbnail is null");
					}
				}
				else
				{
					Debug.Log($"[AvatarUploader] TrySyncAvatarMetadataFromControlPanel: TryGetAvatarThumbnail returned false (could not extract thumbnail)");
				}

				if (synced)
				{
					EditorUtility.SetDirty(config);
				}

				return synced;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AvatarUploader] Failed to sync avatar metadata from Control Panel: {ex.Message}\n{ex.StackTrace}");
				return false;
			}
		}

		/// <summary>
		/// Update the visual selection state of tiles in the grid without rebuilding.
		/// </summary>
		private void UpdateGridSelectionVisualState()
		{
			if (_avatarGridContent == null)
				return;

			var tiles = _avatarGridContent.Children().ToList();
			for (int i = 0; i < tiles.Count; i++)
			{
				var tile = tiles[i];
				if (i == _selectedAvatarIndex)
				{
					tile.AddToClassList("yucp-avatar-tile-selected");
					tile.RemoveFromClassList("yucp-avatar-tile-multi-selected");
				}
				else
				{
					tile.RemoveFromClassList("yucp-avatar-tile-selected");
					// Keep multi-select class if this tile is in multi-select
					if (!_selectedAvatarIndices.Contains(i))
					{
						tile.RemoveFromClassList("yucp-avatar-tile-multi-selected");
					}
				}
			}
		}

		/// <summary>
		/// Update a specific avatar tile in the grid without rebuilding the entire grid.
		/// </summary>
		private void UpdateAvatarTileInGrid(AvatarCollection profile, AvatarAsset config)
		{
			if (profile == null || config == null || _avatarGridContent == null)
				return;

			// Find the index of this avatar in the profile
			int avatarIndex = -1;
			if (profile.avatars != null)
			{
				for (int i = 0; i < profile.avatars.Count; i++)
				{
					if (ReferenceEquals(profile.avatars[i], config))
					{
						avatarIndex = i;
						break;
					}
				}
			}

			if (avatarIndex < 0)
				return;

			// Find and update the tile
			var tiles = _avatarGridContent.Children().ToList();
			if (avatarIndex < tiles.Count)
			{
				var oldTile = tiles[avatarIndex];
				if (oldTile != null)
				{
					// Dispose preview renderer if it exists
					var previewRenderer = oldTile.Q<AvatarTilePreviewRenderer>();
					if (previewRenderer != null)
					{
						previewRenderer.Dispose();
					}

					// Remove old tile
					oldTile.RemoveFromHierarchy();

					// Create new tile
					var newTile = CreateAvatarTile(profile, config, avatarIndex);
					if (avatarIndex == _selectedAvatarIndex)
					{
						newTile.AddToClassList("yucp-avatar-tile-selected");
					}

					// Insert at the same position
					_avatarGridContent.Insert(avatarIndex, newTile);
				}
			}
		}

		private void ShowProfileContextMenu(AvatarCollection profile, int index, MouseDownEvent evt)
		{
			_selectedIndex = index;
			_selectedProfile = profile;
			UpdateProfileList();

			var menu = new GenericMenu();
			menu.AddItem(new GUIContent("Build"), false, () => BuildSelectedProfile());
			menu.AddSeparator("");
			menu.AddItem(new GUIContent("Clone"), false, () => CloneSelectedProfile());
			menu.AddItem(new GUIContent("Delete"), false, () => DeleteSelectedProfile());
			menu.AddSeparator("");
			menu.AddItem(new GUIContent("Select in Project"), false, () =>
			{
				Selection.activeObject = profile;
				EditorGUIUtility.PingObject(profile);
			});
			menu.ShowAsContext();
		}

		private void ShowAvatarContextMenu(AvatarCollection profile, AvatarAsset config, int index, MouseDownEvent evt)
		{
			var menu = new GenericMenu();
			
			// Selection
			if (_selectedAvatarIndices.Contains(index))
			{
				menu.AddItem(new GUIContent("Deselect"), false, () =>
				{
					_selectedAvatarIndices.Remove(index);
					UpdateBulkEditPanel(profile);
					BuildCollectionHeader(profile);
					BuildAvatarGridView(profile);
				});
			}
			else
			{
				menu.AddItem(new GUIContent("Select"), false, () =>
				{
					_selectedAvatarIndices.Add(index);
					UpdateBulkEditPanel(profile);
					BuildCollectionHeader(profile);
					BuildAvatarGridView(profile);
				});
			}
			
			if (_selectedAvatarIndices.Count > 1)
			{
				menu.AddItem(new GUIContent("Select All"), false, () =>
				{
					_selectedAvatarIndices.Clear();
					for (int i = 0; i < profile.avatars.Count; i++)
					{
						_selectedAvatarIndices.Add(i);
					}
					UpdateBulkEditPanel(profile);
					BuildCollectionHeader(profile);
					BuildAvatarGridView(profile);
				});
				menu.AddItem(new GUIContent("Deselect All"), false, () =>
				{
					_selectedAvatarIndices.Clear();
					UpdateBulkEditPanel(profile);
					BuildCollectionHeader(profile);
					BuildAvatarGridView(profile);
				});
			}
			
			menu.AddSeparator("");
			
			// Platform toggles
			menu.AddItem(new GUIContent($"Platform/PC - {(config.buildPC ? "Disable" : "Enable")}"), false, () =>
			{
				Undo.RecordObject(config, "Toggle PC Platform");
				config.buildPC = !config.buildPC;
				EditorUtility.SetDirty(config);
				BuildAvatarGridView(profile);
			});
			menu.AddItem(new GUIContent($"Platform/Quest - {(config.buildQuest ? "Disable" : "Enable")}"), false, () =>
			{
				Undo.RecordObject(config, "Toggle Quest Platform");
				config.buildQuest = !config.buildQuest;
				EditorUtility.SetDirty(config);
				BuildAvatarGridView(profile);
			});
			
			menu.AddSeparator("");
			
			// Release status
			menu.AddItem(new GUIContent($"Visibility/Set Public"), config.releaseStatus == ReleaseStatus.Public, () =>
			{
				Undo.RecordObject(config, "Set Release Status");
				config.releaseStatus = ReleaseStatus.Public;
				EditorUtility.SetDirty(config);
				BuildAvatarGridView(profile);
			});
			menu.AddItem(new GUIContent($"Visibility/Set Private"), config.releaseStatus == ReleaseStatus.Private, () =>
			{
				Undo.RecordObject(config, "Set Release Status");
				config.releaseStatus = ReleaseStatus.Private;
				EditorUtility.SetDirty(config);
				BuildAvatarGridView(profile);
			});
			
			menu.AddSeparator("");
			
			// Actions
			menu.AddItem(new GUIContent("Edit Details"), false, () =>
			{
				_selectedAvatarIndex = index;
				_selectedAvatarIndices.Clear();
				_selectedAvatarIndices.Add(index);
				ShowAvatarDetails(profile, config, index);
				UpdateBulkEditPanel(profile);
				BuildCollectionHeader(profile);
				BuildAvatarGridView(profile);
			});
			
			menu.AddItem(new GUIContent("Delete Avatar"), false, () =>
			{
				if (EditorUtility.DisplayDialog("Delete Avatar", $"Are you sure you want to delete '{config.avatarName}'?", "Delete", "Cancel"))
				{
					Undo.RecordObject(profile, "Delete Avatar");
					profile.avatars.RemoveAt(index);
					EditorUtility.SetDirty(profile);
					_selectedAvatarIndices.Remove(index);
					BuildAvatarGridView(profile);
					UpdateBulkEditPanel(profile);
				}
			});
			
			menu.ShowAsContext();
		}

		private string GetProfileDisplayName(AvatarCollection profile)
		{
			return string.IsNullOrEmpty(profile.collectionName) ? profile.name : profile.collectionName;
		}


		private enum HeroSlideType
		{
			GalleryImage
		}

		private class HeroSlide
		{
			public HeroSlideType Type;
			public AvatarGalleryImage Gallery;
			public Texture2D Texture;
		}

		private VisualElement CreateCard(string title, VisualElement content, string description = null)
		{
			var card = new VisualElement();
			card.AddToClassList("yucp-card");

			if (!string.IsNullOrEmpty(title))
			{
				var titleLabel = new Label(title);
				titleLabel.AddToClassList("yucp-card-title");
				card.Add(titleLabel);
			}

			if (!string.IsNullOrEmpty(description))
			{
				var descriptionLabel = new Label(description);
				descriptionLabel.AddToClassList("yucp-card-subtitle");
				card.Add(descriptionLabel);
			}

			var contentContainer = new VisualElement();
			contentContainer.AddToClassList("yucp-card-content");
			if (content != null)
			{
				contentContainer.Add(content);
			}
			card.Add(contentContainer);

			return card;
		}

		private void BuildMetadataSection()
		{
			if (_metadataSectionHost == null)
				return;

			// Don't rebuild if user is actively typing - this causes focus loss
			if (_isUserTyping)
				return;

			_metadataSectionHost.Clear();

			if (_currentHeroConfig == null || _selectedProfile == null)
			{
				var emptyCard = CreateCard("Basic Information", null);
				var emptyLabel = new Label("Select an avatar to edit metadata");
				emptyLabel.AddToClassList("yucp-label-secondary");
				emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
				emptyLabel.style.paddingTop = 20;
				emptyLabel.style.paddingBottom = 20;
				emptyCard.Q(className: "yucp-card-content").Add(emptyLabel);
				_metadataSectionHost.Add(emptyCard);
				return;
			}

			var content = new VisualElement();
			BuildMetadataContent(content);
			var card = CreateCard("Basic Information", content);
			_metadataSectionHost.Add(card);
		}

		private void BuildMetadataContent(VisualElement container)
		{
			if (_currentHeroConfig == null || _selectedProfile == null)
				return;

			var section = container;

			// Help text for new users
			if (_currentHeroConfig.avatarPrefab == null)
			{
				var helpBox = new VisualElement();
				helpBox.AddToClassList("yucp-help-box");
				helpBox.style.marginBottom = 16;
				var helpText = new Label("👋 New here? Start by selecting an Avatar Prefab below. Then fill in the name, description, and other details. When ready, scroll down to the Build section to upload your avatar.");
				helpText.AddToClassList("yucp-help-box-text");
				helpBox.Add(helpText);
				section.Add(helpBox);
			}

			// Avatar Prefab - MOST IMPORTANT, should be first and visible
			var prefabRow = CreateFormRow("Avatar Prefab");
			var prefabHelp = new Label("Select the avatar prefab you want to upload. This must have a VRCAvatarDescriptor component.");
			prefabHelp.AddToClassList("yucp-label-small");
			prefabHelp.style.color = new Color(0.6f, 0.6f, 0.6f);
			prefabHelp.style.marginBottom = 4;
			section.Add(prefabHelp);

			var prefabField = new ObjectField { objectType = typeof(GameObject), value = _currentHeroConfig.avatarPrefab };
			prefabField.AddToClassList("yucp-form-field");
			prefabField.RegisterValueChangedCallback(evt =>
			{
				if (_currentHeroConfig != null)
				{
					Undo.RecordObject(_currentHeroConfig, "Change Avatar Prefab");
					
					GameObject prefabToStore = null;
					var selectedObject = evt.newValue as GameObject;
					
					if (selectedObject != null)
					{
						// Convert scene instance to prefab asset reference
						// This is critical - scene instances don't persist after Unity restarts
						var prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selectedObject);
						if (!string.IsNullOrEmpty(prefabAssetPath))
						{
							// It's a prefab instance - get the prefab asset
							prefabToStore = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
						}
						else
						{
							// Check if it's already a prefab asset
							var assetPath = AssetDatabase.GetAssetPath(selectedObject);
							if (!string.IsNullOrEmpty(assetPath) && AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) == selectedObject)
							{
								// It's already a prefab asset
								prefabToStore = selectedObject;
							}
							else
							{
								// It's a scene instance without a prefab - warn the user
								EditorUtility.DisplayDialog("Prefab Required",
									$"The GameObject '{selectedObject.name}' is a scene instance without a prefab.\n\n" +
									"Avatar Tools requires a prefab asset reference that persists between Unity sessions.\n\n" +
									"Please:\n" +
									"1. Create a prefab from this GameObject (drag it to Project window)\n" +
									"2. Then select the prefab asset instead of the scene instance.",
									"OK");
								// Reset the field to the previous value
								prefabField.value = _currentHeroConfig.avatarPrefab;
								return;
							}
						}
					}
					
					_currentHeroConfig.avatarPrefab = prefabToStore;
					
					// Mark as dirty and save immediately to ensure persistence
					EditorUtility.SetDirty(_currentHeroConfig);
					
					// Force save the asset file to ensure prefab reference persists
					var configAssetPath = AssetDatabase.GetAssetPath(_currentHeroConfig);
					if (!string.IsNullOrEmpty(configAssetPath))
					{
						// Save the asset immediately
						AssetDatabase.SaveAssets();
						AssetDatabase.Refresh();
					}
					
					// Sync blueprint ID from component if available
					if (_currentHeroConfig.avatarPrefab != null)
					{
						SyncBlueprintIdFromComponent(_currentHeroConfig);
					}
					
					// Update the hero panel with the new prefab
					UpdateHeroPanel(_selectedProfile, _currentHeroConfig, _selectedAvatarIndex);
					SyncAvatarToControlPanel();
				}
			});
			prefabRow.Add(prefabField);
			section.Add(prefabRow);

			// Basic Info - Name and Description
			var nameRow = CreateFormRow("Name");
			var nameField = new TextField { value = _currentHeroConfig.avatarName ?? string.Empty };
			nameField.AddToClassList("yucp-input");
			nameField.AddToClassList("yucp-form-field");
			_nameFieldRef = nameField; // Store reference
			nameField.RegisterCallback<FocusInEvent>(_ => _isUserTyping = true);
			nameField.RegisterCallback<FocusOutEvent>(_ => _isUserTyping = false);
			nameField.RegisterValueChangedCallback(evt =>
			{
				_isUserTyping = true;
				UpdateActiveAvatarName(evt.newValue);
				// Don't sync to Control Panel on every keystroke - defer until focus out
				// Don't rebuild UI - just update the hero card title if needed
				var heroCard = _heroInfoContainer.Q(className: "yucp-hero-info");
				if (heroCard != null)
				{
					var titleLabel = heroCard.Q<Label>(className: "yucp-hero-title");
					if (titleLabel != null)
						titleLabel.text = evt.newValue;
				}
			});
			// Sync to Control Panel only when focus is lost (user finished typing)
			nameField.RegisterCallback<FocusOutEvent>(_ =>
			{
				_isUserTyping = false;
				SyncAvatarToControlPanel();
			});
			nameRow.Add(nameField);
			section.Add(nameRow);

			var descRow = CreateFormRow("Description");
			var descField = new TextField { value = _currentHeroConfig.description ?? string.Empty };
			descField.multiline = true;
			descField.AddToClassList("yucp-input");
			descField.AddToClassList("yucp-input-multiline");
			descField.AddToClassList("yucp-form-field");
			descField.style.minHeight = 60;
			_descFieldRef = descField; // Store reference
			descField.RegisterCallback<FocusInEvent>(_ => _isUserTyping = true);
			descField.RegisterCallback<FocusOutEvent>(_ => _isUserTyping = false);
			descField.RegisterValueChangedCallback(evt =>
			{
				_isUserTyping = true;
				UpdateActiveAvatarDescription(evt.newValue);
				// Don't sync to Control Panel on every keystroke - defer until focus out
			});
			// Sync to Control Panel only when focus is lost (user finished typing)
			descField.RegisterCallback<FocusOutEvent>(_ =>
			{
				_isUserTyping = false;
				SyncAvatarToControlPanel();
			});
			descRow.Add(descField);
			section.Add(descRow);

			var tagsLabel = new Label("Tags");
			tagsLabel.AddToClassList("yucp-form-label");
			tagsLabel.style.marginBottom = 8;
			section.Add(tagsLabel);

			var tagsDisplay = new VisualElement();
			tagsDisplay.style.flexDirection = FlexDirection.Row;
			tagsDisplay.style.flexWrap = Wrap.Wrap;
			tagsDisplay.style.marginBottom = 12;

			if (_currentHeroConfig.tags != null && _currentHeroConfig.tags.Count > 0)
			{
				foreach (var tag in _currentHeroConfig.tags)
				{
					var tagChip = new VisualElement();
					tagChip.style.flexDirection = FlexDirection.Row;
					tagChip.style.alignItems = Align.Center;
					tagChip.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
					tagChip.style.borderLeftWidth = 3;
					tagChip.style.borderLeftColor = new Color(0.36f, 0.75f, 0.69f);
					tagChip.style.paddingLeft = 10;
					tagChip.style.paddingRight = 10;
					tagChip.style.paddingTop = 6;
					tagChip.style.paddingBottom = 6;
					tagChip.style.marginRight = 8;
					tagChip.style.marginBottom = 8;
					tagChip.style.borderTopLeftRadius = 2;
					tagChip.style.borderTopRightRadius = 2;
					tagChip.style.borderBottomLeftRadius = 2;
					tagChip.style.borderBottomRightRadius = 2;

					var tagLabel = new Label(tag);
					tagLabel.style.fontSize = 12;
					tagLabel.style.color = new Color(0.36f, 0.75f, 0.69f);
					tagChip.Add(tagLabel);

					var removeButton = new Button(() =>
					{
						RemoveActiveAvatarTag(tag);
						SyncAvatarToControlPanel();
						BuildMetadataSection();
					}) { text = "×" };
					removeButton.style.width = 20;
					removeButton.style.height = 20;
					removeButton.style.marginLeft = 8;
					removeButton.style.backgroundColor = Color.clear;
					removeButton.style.color = new Color(0.8f, 0.8f, 0.8f);
					removeButton.style.fontSize = 16;
					removeButton.style.unityFontStyleAndWeight = FontStyle.Bold;
					tagChip.Add(removeButton);

					tagsDisplay.Add(tagChip);
				}
			}

			var tagsInputRow = new VisualElement();
			tagsInputRow.style.flexDirection = FlexDirection.Row;
			tagsInputRow.style.alignItems = Align.Center;

			var tagsInput = new TextField();
			tagsInput.AddToClassList("yucp-input");
			tagsInput.style.flexGrow = 1;
			tagsInput.style.marginRight = 8;
			tagsInput.RegisterCallback<KeyDownEvent>(evt =>
			{
				if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
				{
					var tag = tagsInput.value?.Trim();
					if (!string.IsNullOrEmpty(tag))
					{
						AddActiveAvatarTag(tag);
						tagsInput.value = string.Empty;
						SyncAvatarToControlPanel();
						BuildMetadataSection();
					}
					evt.StopPropagation();
				}
			});

			var addTagButton = new Button(() =>
			{
				var tag = tagsInput.value?.Trim();
				if (!string.IsNullOrEmpty(tag))
				{
					AddActiveAvatarTag(tag);
					tagsInput.value = string.Empty;
					SyncAvatarToControlPanel();
					BuildMetadataSection();
				}
			}) { text = "Add Tag" };
			addTagButton.AddToClassList("yucp-button");
			addTagButton.AddToClassList("yucp-button-action");
			addTagButton.AddToClassList("yucp-button-small");

			tagsInputRow.Add(tagsInput);
			tagsInputRow.Add(addTagButton);

			section.Add(tagsDisplay);
			section.Add(tagsInputRow);

			// Category
			var categoryRow = CreateFormRow("Category");
			var categoryField = new EnumField(_currentHeroConfig.category);
			categoryField.AddToClassList("yucp-form-field");
			categoryField.RegisterValueChangedCallback(evt =>
			{
				if (_currentHeroConfig != null)
				{
					Undo.RecordObject(_selectedProfile, "Change Category");
					_currentHeroConfig.category = (AvatarCategory)evt.newValue;
					EditorUtility.SetDirty(_selectedProfile);
				}
			});
			categoryRow.Add(categoryField);
			section.Add(categoryRow);

			// Styles - Use StyleField (PopupField) like Control Panel does
			if (_controlPanelBuilder != null)
			{
				try
				{
					// Use VRChat's StyleField which is a PopupField that loads styles from API (like "Sci Fi", "Anime", etc.)
					var primaryStyleRow = CreateFormRow("Primary Style");
					var primaryStyleField = new StyleField();
					primaryStyleField.AddToClassList("yucp-form-field");
					
					if (ControlPanelDataBridge.TryGetAvatarStyle(_controlPanelBuilder, true, out var primaryStyle))
					{
						// Style is stored as ID, need to get name
						primaryStyleField.SetValue(primaryStyle ?? string.Empty);
					}
					
					primaryStyleField.RegisterValueChangedCallback(evt =>
					{
						if (_currentHeroConfig != null)
						{
							// Get the style ID from the name
							var styleId = primaryStyleField.GetStyleId(evt.newValue);
							ControlPanelDataBridge.TrySetAvatarStyle(_controlPanelBuilder, true, styleId ?? evt.newValue);
							SyncAvatarToControlPanel();
						}
					});
					primaryStyleRow.Add(primaryStyleField);
					section.Add(primaryStyleRow);

					var secondaryStyleRow = CreateFormRow("Secondary Style");
					var secondaryStyleField = new StyleField();
					secondaryStyleField.AddToClassList("yucp-form-field");
					
					if (ControlPanelDataBridge.TryGetAvatarStyle(_controlPanelBuilder, false, out var secondaryStyle))
					{
						secondaryStyleField.SetValue(secondaryStyle ?? string.Empty);
					}
					
					secondaryStyleField.RegisterValueChangedCallback(evt =>
					{
						if (_currentHeroConfig != null)
						{
							var styleId = secondaryStyleField.GetStyleId(evt.newValue);
							ControlPanelDataBridge.TrySetAvatarStyle(_controlPanelBuilder, false, styleId ?? evt.newValue);
							SyncAvatarToControlPanel();
						}
					});
					secondaryStyleRow.Add(secondaryStyleField);
					section.Add(secondaryStyleRow);
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"[AvatarUploader] Could not create style fields: {ex.Message}");
				}
			}

			// Advanced Settings Foldout
			var advancedFoldout = new Foldout { text = "Advanced Settings", value = false };
			advancedFoldout.AddToClassList("yucp-hero-foldout");
			advancedFoldout.style.marginTop = 12;

			// Platforms
			var platformsRow = CreateFormRow("Build For");
			var platformsContainer = new VisualElement();
			platformsContainer.AddToClassList("yucp-form-field");
			platformsContainer.style.flexDirection = FlexDirection.Row;
			platformsContainer.style.alignItems = Align.Center;

			var pcToggle = new Toggle("PC") { value = _currentHeroConfig.buildPC };
			pcToggle.AddToClassList("yucp-toggle");
			pcToggle.style.marginRight = 12;
			pcToggle.RegisterValueChangedCallback(evt =>
			{
				if (_currentHeroConfig != null)
				{
					Undo.RecordObject(_selectedProfile, "Change Build PC");
					_currentHeroConfig.buildPC = evt.newValue;
					EditorUtility.SetDirty(_selectedProfile);
					BuildAvatarGridView(_selectedProfile);
				}
			});
			platformsContainer.Add(pcToggle);

			var questToggle = new Toggle("Quest") { value = _currentHeroConfig.buildQuest };
			questToggle.AddToClassList("yucp-toggle");
			questToggle.RegisterValueChangedCallback(evt =>
			{
				if (_currentHeroConfig != null)
				{
					Undo.RecordObject(_selectedProfile, "Change Build Quest");
					_currentHeroConfig.buildQuest = evt.newValue;
					EditorUtility.SetDirty(_selectedProfile);
					BuildAvatarGridView(_selectedProfile);
				}
			});
			platformsContainer.Add(questToggle);

			platformsRow.Add(platformsContainer);
			advancedFoldout.Add(platformsRow);

			// Version
			var versionRow = CreateFormRow("Version");
			var versionField = new TextField { value = _currentHeroConfig.version ?? string.Empty };
			versionField.AddToClassList("yucp-input");
			versionField.AddToClassList("yucp-form-field");
			versionField.RegisterValueChangedCallback(evt =>
			{
				if (_currentHeroConfig != null)
				{
					Undo.RecordObject(_selectedProfile, "Change Version");
					_currentHeroConfig.version = evt.newValue;
					EditorUtility.SetDirty(_selectedProfile);
				}
			});
			versionRow.Add(versionField);
			advancedFoldout.Add(versionRow);

			// Manual Blueprint ID Editing (Advanced)
			var blueprintHelp = new Label("Manually set blueprint IDs for existing avatars. Leave empty to let VRChat assign on upload.");
			blueprintHelp.AddToClassList("yucp-help-box-text");
			blueprintHelp.style.marginBottom = 8;
			advancedFoldout.Add(blueprintHelp);

			var blueprintPcRow = CreateFormRow("Blueprint ID (PC)");
			var blueprintPcField = new TextField { value = _currentHeroConfig.blueprintIdPC ?? string.Empty };
			blueprintPcField.AddToClassList("yucp-input");
			blueprintPcField.AddToClassList("yucp-form-field");
			blueprintPcField.RegisterValueChangedCallback(evt =>
			{
				if (_currentHeroConfig != null)
				{
					Undo.RecordObject(_selectedProfile, "Change Blueprint ID PC");
					_currentHeroConfig.blueprintIdPC = evt.newValue;
					EditorUtility.SetDirty(_selectedProfile);
				}
			});
			blueprintPcRow.Add(blueprintPcField);
			advancedFoldout.Add(blueprintPcRow);

			var blueprintQuestRow = CreateFormRow("Blueprint ID (Quest)");
			var blueprintQuestField = new TextField { value = _currentHeroConfig.blueprintIdQuest ?? string.Empty };
			blueprintQuestField.AddToClassList("yucp-input");
			blueprintQuestField.AddToClassList("yucp-form-field");
			blueprintQuestField.RegisterValueChangedCallback(evt =>
			{
				if (_currentHeroConfig != null)
				{
					Undo.RecordObject(_selectedProfile, "Change Blueprint ID Quest");
					_currentHeroConfig.blueprintIdQuest = evt.newValue;
					EditorUtility.SetDirty(_selectedProfile);
				}
			});
			blueprintQuestRow.Add(blueprintQuestField);
			advancedFoldout.Add(blueprintQuestRow);

			section.Add(advancedFoldout);

			// Blueprint ID Display (read-only, shows current values)
			var blueprintDisplay = new VisualElement();
			blueprintDisplay.style.marginTop = 12;
			blueprintDisplay.style.paddingTop = 12;
			blueprintDisplay.style.borderTopWidth = 1;
			blueprintDisplay.style.borderTopColor = new Color(0.2f, 0.2f, 0.2f);

			var blueprintLabel = new Label("Blueprint IDs");
			blueprintLabel.AddToClassList("yucp-form-label");
			blueprintLabel.style.marginBottom = 8;
			blueprintDisplay.Add(blueprintLabel);

			var blueprintInfo = new Label("Assigned by VRChat on upload. Use Advanced Settings to manually set for existing avatars.");
			blueprintInfo.AddToClassList("yucp-label-small");
			blueprintInfo.style.color = new Color(0.6f, 0.6f, 0.6f);
			blueprintInfo.style.marginBottom = 8;
			blueprintDisplay.Add(blueprintInfo);

			var pcIdLabel = new Label($"PC: {(_currentHeroConfig.blueprintIdPC ?? "Unassigned")}");
			pcIdLabel.AddToClassList("yucp-label-small");
			if (string.IsNullOrEmpty(_currentHeroConfig.blueprintIdPC))
				pcIdLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
			blueprintDisplay.Add(pcIdLabel);

			var questIdLabel = new Label($"Quest: {(_currentHeroConfig.blueprintIdQuest ?? "Unassigned")}");
			questIdLabel.AddToClassList("yucp-label-small");
			if (string.IsNullOrEmpty(_currentHeroConfig.blueprintIdQuest))
				questIdLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
			blueprintDisplay.Add(questIdLabel);

			section.Add(blueprintDisplay);

			// Remove Avatar Button
			var removeAvatarRow = new VisualElement();
			removeAvatarRow.style.flexDirection = FlexDirection.Row;
			removeAvatarRow.style.justifyContent = Justify.FlexEnd;
			removeAvatarRow.style.marginTop = 20;
			removeAvatarRow.style.paddingTop = 16;
			removeAvatarRow.style.borderTopWidth = 1;
			removeAvatarRow.style.borderTopColor = new Color(0.16f, 0.16f, 0.16f);

			var removeAvatarButton = new Button(() =>
			{
				if (_selectedProfile != null && _currentHeroConfig != null && _selectedAvatarIndex >= 0)
				{
					RemoveAvatarFromProfile(_selectedProfile, _selectedAvatarIndex);
					// After removal, hide details panel
					if (_avatarDetailsHost != null)
					{
						_avatarDetailsHost.style.display = DisplayStyle.None;
					}
					_currentHeroConfig = null;
					_selectedAvatarIndex = -1;
				}
			})
			{
				text = "Remove Avatar"
			};
			removeAvatarButton.AddToClassList("yucp-button");
			removeAvatarButton.AddToClassList("yucp-button-danger");
			removeAvatarButton.tooltip = "Remove this avatar from the collection";
			removeAvatarRow.Add(removeAvatarButton);
			section.Add(removeAvatarRow);
		}

		private void BuildPerformanceSection()
		{
			if (_performanceSectionHost == null)
				return;

			_performanceSectionHost.Clear();

			if (_currentHeroConfig == null || _currentHeroConfig.avatarPrefab == null)
			{
				var emptyCard = CreateCard("Performance", null, "View detailed performance metrics for your avatar. VRChat rates avatars based on polygon count and material usage.");
				var emptyLabel = new Label("Select an avatar prefab to see performance metrics");
				emptyLabel.AddToClassList("yucp-label-secondary");
				emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
				emptyLabel.style.paddingTop = 20;
				emptyLabel.style.paddingBottom = 20;
				emptyCard.Q(className: "yucp-card-content").Add(emptyLabel);
				_performanceSectionHost.Add(emptyCard);
				return;
			}

			// Calculate performance if not already calculated
			if (_currentHeroConfig.polyCountPC == 0 && _currentHeroConfig.polyCountQuest == 0)
			{
				CalculateAvatarPerformance(_currentHeroConfig);
			}

			var section = new VisualElement();

			var helpText = new Label("VRChat evaluates avatars based on polygon count and material usage. These metrics determine your avatar's performance rank.");
			helpText.AddToClassList("yucp-label-small");
			helpText.style.color = new Color(0.7f, 0.7f, 0.7f);
			helpText.style.marginBottom = 12;
			helpText.style.whiteSpace = WhiteSpace.Normal;
			section.Add(helpText);

			var metricsGrid = new VisualElement();
			metricsGrid.style.flexDirection = FlexDirection.Row;
			metricsGrid.style.flexWrap = Wrap.Wrap;
			metricsGrid.style.marginTop = 12;

			// PC Performance
			if (_currentHeroConfig.buildPC)
			{
				var pcCard = CreatePerformanceCard("PC", _currentHeroConfig.polyCountPC, _currentHeroConfig.performanceRatingPC, _currentHeroConfig.materialCount);
				metricsGrid.Add(pcCard);
			}

			// Quest Performance
			if (_currentHeroConfig.buildQuest)
			{
				var questCard = CreatePerformanceCard("Quest", _currentHeroConfig.polyCountQuest, _currentHeroConfig.performanceRatingQuest, _currentHeroConfig.materialCount);
				metricsGrid.Add(questCard);
			}

			// Material Count (shared)
			var materialCard = new VisualElement();
			materialCard.AddToClassList("yucp-performance-card");
			materialCard.style.marginRight = 12;
			materialCard.style.marginBottom = 12;
			materialCard.style.minWidth = 160;

			var materialLabel = new Label("Materials");
			materialLabel.AddToClassList("yucp-performance-label");
			materialCard.Add(materialLabel);

			var materialValue = new Label(_currentHeroConfig.materialCount.ToString());
			materialValue.AddToClassList("yucp-performance-value");
			materialCard.Add(materialValue);

			var materialStatus = _currentHeroConfig.materialCount <= 1 ? "Excellent" :
				_currentHeroConfig.materialCount <= 2 ? "Good" :
				_currentHeroConfig.materialCount <= 3 ? "Medium" :
				_currentHeroConfig.materialCount <= 4 ? "Poor" : "Very Poor";
			
			var materialStatusLabel = new Label(materialStatus);
			materialStatusLabel.AddToClassList("yucp-performance-rating");
			materialStatusLabel.style.color = GetMaterialStatusColor(_currentHeroConfig.materialCount);
			materialCard.Add(materialStatusLabel);

			var materialInsight = new Label(GetMaterialInsight(_currentHeroConfig.materialCount));
			materialInsight.AddToClassList("yucp-label-small");
			materialInsight.style.color = _currentHeroConfig.materialCount > 4 
				? new Color(0.9f, 0.5f, 0.3f) 
				: new Color(0.7f, 0.7f, 0.7f);
			materialInsight.style.marginTop = 4;
			materialInsight.style.whiteSpace = WhiteSpace.Normal;
			materialCard.Add(materialInsight);

			metricsGrid.Add(materialCard);

			section.Add(metricsGrid);
			var card = CreateCard("Performance", section, "View detailed performance metrics for your avatar. VRChat rates avatars based on polygon count and material usage.");
			_performanceSectionHost.Add(card);
		}

		private Color GetMaterialStatusColor(int materialCount)
		{
			return materialCount <= 1 ? new Color(0.2f, 0.8f, 0.2f) :
				materialCount <= 2 ? new Color(0.5f, 0.8f, 0.2f) :
				materialCount <= 3 ? new Color(0.8f, 0.8f, 0.2f) :
				materialCount <= 4 ? new Color(0.8f, 0.5f, 0.2f) :
				new Color(0.8f, 0.2f, 0.2f);
		}

		private string GetMaterialInsight(int materialCount)
		{
			return materialCount <= 1 ? "Optimal! Single material is best for performance." :
				materialCount <= 2 ? "Good. Two materials is acceptable." :
				materialCount <= 3 ? "Medium. Three materials may impact performance." :
				materialCount <= 4 ? "Poor. Four materials will reduce performance." :
				$"Very Poor! {materialCount} materials significantly impacts performance. Consider combining materials or using material atlases.";
		}

		private VisualElement CreatePerformanceCard(string platform, int polyCount, PerformanceRating rating, int materialCount)
		{
			var card = new VisualElement();
			card.AddToClassList("yucp-performance-card");
			card.style.marginRight = 12;
			card.style.marginBottom = 12;
			card.style.minWidth = 180;
			card.style.flexGrow = 1;

			var platformLabel = new Label($"{platform} Platform");
			platformLabel.AddToClassList("yucp-performance-label");
			platformLabel.style.fontSize = 12;
			platformLabel.style.marginBottom = 8;
			card.Add(platformLabel);

			// Triangles count
			var trianglesRow = new VisualElement();
			trianglesRow.style.flexDirection = FlexDirection.Row;
			trianglesRow.style.alignItems = Align.FlexStart;
			trianglesRow.style.marginBottom = 4;

			var polyValue = new Label(polyCount > 0 ? polyCount.ToString("N0") : "—");
			polyValue.AddToClassList("yucp-performance-value");
			polyValue.style.fontSize = 24;
			trianglesRow.Add(polyValue);

			var polyLabel = new Label(" triangles");
			polyLabel.AddToClassList("yucp-performance-label");
			polyLabel.style.fontSize = 11;
			polyLabel.style.marginLeft = 4;
			trianglesRow.Add(polyLabel);

			card.Add(trianglesRow);

			// Performance rating badge
			var ratingLabel = new Label(GetPerformanceRatingText(rating));
			ratingLabel.AddToClassList("yucp-performance-rating");
			ratingLabel.style.color = GetPerformanceRatingColor(rating);
			ratingLabel.style.marginTop = 4;
			ratingLabel.style.marginBottom = 8;
			card.Add(ratingLabel);

			// Detailed insight with actionable information
			var insightLabel = new Label(GetPerformanceInsight(rating, polyCount, platform == "Quest"));
			insightLabel.AddToClassList("yucp-label-small");
			insightLabel.style.color = rating == PerformanceRating.VeryPoor || rating == PerformanceRating.Poor 
				? new Color(0.9f, 0.6f, 0.4f) 
				: new Color(0.7f, 0.7f, 0.7f);
			insightLabel.style.marginTop = 4;
			insightLabel.style.whiteSpace = WhiteSpace.Normal;
			card.Add(insightLabel);

			// Material count for this platform
			var materialInfo = new Label($"Materials: {materialCount}");
			materialInfo.AddToClassList("yucp-label-small");
			materialInfo.style.color = new Color(0.6f, 0.6f, 0.6f);
			materialInfo.style.marginTop = 8;
			card.Add(materialInfo);

			return card;
		}

		private string GetPerformanceInsight(PerformanceRating rating, int polyCount, bool isQuest)
		{
			if (polyCount == 0)
				return "Select an avatar prefab to calculate performance metrics.";

			var limit = isQuest ? 15000 : 75000;
			var remaining = limit - polyCount;
			var percentage = limit > 0 ? (polyCount * 100f / limit) : 0;

			var insight = rating switch
			{
				PerformanceRating.Excellent => $"Excellent! {polyCount:N0} triangles is well below the {limit:N0} limit ({percentage:F0}% used). This avatar will perform smoothly for all users.",
				PerformanceRating.Good => $"Good performance. {polyCount:N0} triangles ({percentage:F0}% of {limit:N0} limit). Should work well for most users.",
				PerformanceRating.Medium => $"Medium performance. {polyCount:N0} triangles ({percentage:F0}% of {limit:N0} limit). May cause lag on lower-end devices. Consider optimization.",
				PerformanceRating.Poor => $"Poor performance. {polyCount:N0} triangles ({percentage:F0}% of {limit:N0} limit). Only {remaining:N0} triangles remaining. Strongly consider reducing polygon count.",
				PerformanceRating.VeryPoor => $"Very Poor! {polyCount:N0} triangles exceeds the {limit:N0} limit by {Math.Abs(remaining):N0} ({percentage:F0}% over limit). This avatar may not be visible to other users. Optimization required.",
				_ => "Performance not calculated."
			};

			// Add specific optimization tips
			if (rating == PerformanceRating.Poor || rating == PerformanceRating.VeryPoor)
			{
				insight += "\n💡 Tip: Use decimation tools, remove hidden meshes, or create a Quest-optimized version.";
			}

			return insight;
		}

		private string GetPerformanceRatingText(PerformanceRating rating)
		{
			return rating switch
			{
				PerformanceRating.Excellent => "Excellent",
				PerformanceRating.Good => "Good",
				PerformanceRating.Medium => "Medium",
				PerformanceRating.Poor => "Poor",
				PerformanceRating.VeryPoor => "Very Poor",
				_ => "Unknown"
			};
		}

		private Color GetPerformanceRatingColor(PerformanceRating rating)
		{
			return rating switch
			{
				PerformanceRating.Excellent => new Color(0.2f, 0.8f, 0.2f),
				PerformanceRating.Good => new Color(0.5f, 0.8f, 0.2f),
				PerformanceRating.Medium => new Color(0.8f, 0.8f, 0.2f),
				PerformanceRating.Poor => new Color(0.8f, 0.5f, 0.2f),
				PerformanceRating.VeryPoor => new Color(0.8f, 0.2f, 0.2f),
				_ => Color.gray
			};
		}

		private void CalculateAvatarPerformance(AvatarAsset config)
		{
			if (config?.avatarPrefab == null)
				return;

			var descriptor = config.avatarPrefab.GetComponent<VRCAvatarDescriptor>();
			if (descriptor == null)
				return;

			// Calculate polygon count - VRChat counts ALL meshes including disabled ones
			int totalPolys = 0;
			int totalVertices = 0;
			var allMeshes = new HashSet<Mesh>();
			var renderers = config.avatarPrefab.GetComponentsInChildren<Renderer>(true); // Include inactive
			
			foreach (var renderer in renderers)
			{
				Mesh mesh = null;
				bool meshReadWriteEnabled = false;

				// Check SkinnedMeshRenderer (most common for avatars)
				if (renderer is SkinnedMeshRenderer skinnedRenderer)
				{
					mesh = skinnedRenderer.sharedMesh;
					if (mesh != null)
					{
						// Check if mesh has read/write enabled (required for accurate count)
						var meshPath = AssetDatabase.GetAssetPath(mesh);
						if (!string.IsNullOrEmpty(meshPath))
						{
							var importer = AssetImporter.GetAtPath(meshPath) as ModelImporter;
							meshReadWriteEnabled = importer != null && importer.isReadable;
						}
					}
				}
				// Check MeshFilter (for static meshes)
				else if (renderer is MeshRenderer)
				{
					var meshFilter = renderer.GetComponent<MeshFilter>();
					if (meshFilter != null)
					{
						mesh = meshFilter.sharedMesh;
						if (mesh != null)
						{
							var meshPath = AssetDatabase.GetAssetPath(mesh);
							if (!string.IsNullOrEmpty(meshPath))
							{
								var importer = AssetImporter.GetAtPath(meshPath) as ModelImporter;
								meshReadWriteEnabled = importer != null && importer.isReadable;
							}
						}
					}
				}

				if (mesh != null && !allMeshes.Contains(mesh))
				{
					allMeshes.Add(mesh);
					
					// Count triangles per submesh
					int meshTriangles = 0;
					for (int i = 0; i < mesh.subMeshCount; i++)
					{
						var triangles = mesh.GetTriangles(i);
						meshTriangles += triangles.Length / 3;
					}
					
					totalPolys += meshTriangles;
					totalVertices += mesh.vertexCount;
					
					// If mesh doesn't have read/write enabled, VRChat marks it as Very Poor
					if (!meshReadWriteEnabled && meshTriangles > 0)
					{
						// This will affect the rating
					}
				}
			}

			config.polyCountPC = totalPolys;
			config.polyCountQuest = totalPolys; // Quest typically uses same mesh

			// Calculate material count - VRChat counts ALL materials including in material swaps
			var materials = new HashSet<Material>();
			foreach (var renderer in renderers)
			{
				if (renderer.sharedMaterials != null)
				{
					foreach (var mat in renderer.sharedMaterials)
					{
						if (mat != null)
							materials.Add(mat);
					}
				}
			}
			config.materialCount = materials.Count;

			// Calculate performance rating using VRChat's actual thresholds
			config.performanceRatingPC = CalculatePerformanceRating(totalPolys, materials.Count, false, totalVertices);
			config.performanceRatingQuest = CalculatePerformanceRating(totalPolys, materials.Count, true, totalVertices);
		}

		private PerformanceRating CalculatePerformanceRating(int polyCount, int materialCount, bool isQuest = false, int vertexCount = 0)
		{
			// VRChat's actual performance rating thresholds (from VRChat documentation):
			// Excellent: < 7,500 tris AND <= 1 material
			// Good: < 15,000 tris AND <= 2 materials  
			// Medium: < 32,000 tris AND <= 3 materials
			// Poor: < 75,000 tris AND <= 4 materials (PC) OR < 15,000 tris AND <= 4 materials (Quest)
			// Very Poor: >= 75,000 tris (PC) OR >= 15,000 tris (Quest) OR >= 5 materials

			if (materialCount >= 5)
				return PerformanceRating.VeryPoor;

			if (isQuest)
			{
				// Quest limits are much stricter
				if (polyCount < 7500 && materialCount <= 1)
					return PerformanceRating.Excellent;
				if (polyCount < 15000 && materialCount <= 2)
					return PerformanceRating.Good;
				if (polyCount < 15000 && materialCount <= 3)
					return PerformanceRating.Medium;
				if (polyCount < 15000 && materialCount <= 4)
					return PerformanceRating.Poor;
				return PerformanceRating.VeryPoor; // Quest: >= 15k is Very Poor
			}
			else
			{
				// PC limits
				if (polyCount < 7500 && materialCount <= 1)
					return PerformanceRating.Excellent;
				if (polyCount < 15000 && materialCount <= 2)
					return PerformanceRating.Good;
				if (polyCount < 32000 && materialCount <= 3)
					return PerformanceRating.Medium;
				if (polyCount < 75000 && materialCount <= 4)
					return PerformanceRating.Poor;
				return PerformanceRating.VeryPoor; // PC: >= 75k is Very Poor
			}
		}

		private void BuildVisibilitySection()
		{
			if (_visibilitySectionHost == null)
				return;

			_visibilitySectionHost.Clear();

			if (_currentHeroConfig == null || _selectedProfile == null)
				return;

			var section = new VisualElement();

			var statusRow = CreateFormRow("Release Status");
			var statusOptions = new List<string> { "Private", "Public" };
			var currentStatus = _currentHeroConfig.releaseStatus == ReleaseStatus.Public ? "Public" : "Private";
			var statusField = new PopupField<string>(statusOptions, currentStatus);
			statusField.AddToClassList("yucp-form-field");
			statusField.RegisterValueChangedCallback(evt =>
			{
				UpdateActiveAvatarVisibility(evt.newValue.ToLowerInvariant());
				SyncAvatarToControlPanel();
			});
			statusRow.Add(statusField);
			section.Add(statusRow);

			var helpBox = new VisualElement();
			helpBox.AddToClassList("yucp-help-box");
			var helpText = new Label(_currentHeroConfig.releaseStatus == ReleaseStatus.Public
				? "Public avatars are visible to everyone and can be used by other users."
				: "Private avatars are only visible to you and cannot be used by other users.");
			helpText.AddToClassList("yucp-help-box-text");
			helpBox.Add(helpText);
			section.Add(helpBox);

			var card = CreateCard("Visibility", section);
			_visibilitySectionHost.Add(card);
		}

		private void BuildValidationSection()
		{
			if (_validationSectionHost == null)
				return;

			_validationSectionHost.Clear();

			if (_currentHeroConfig == null || _controlPanelBuilder == null)
			{
				if (_controlPanelBuilder == null)
				{
					var emptyCard = CreateCard("Validation", null, "Check for issues and warnings before uploading. Fix any errors to ensure your avatar uploads successfully.");
					var warningBox = new VisualElement();
					warningBox.AddToClassList("yucp-validation-warning");
					var warningText = new Label("Control Panel builder unavailable. Click 'Refresh Builder' to connect.");
					warningText.AddToClassList("yucp-validation-warning-text");
					warningBox.Add(warningText);
					emptyCard.Q(className: "yucp-card-content").Add(warningBox);
					_validationSectionHost.Add(emptyCard);
				}
				return;
			}

			var section = new VisualElement();

			// Add icon validation check for new avatars
			bool isNewAvatar = string.IsNullOrEmpty(_currentHeroConfig.blueprintIdPC) && 
			                   string.IsNullOrEmpty(_currentHeroConfig.blueprintIdQuest);
			if (isNewAvatar && _currentHeroConfig.avatarIcon == null)
			{
				var iconErrorCard = new VisualElement();
				iconErrorCard.AddToClassList("yucp-validation-error");
				var iconErrorText = new Label("Avatar icon is required before first upload. Use the Capture or Upload button in the hero section to add an icon.");
				iconErrorText.AddToClassList("yucp-validation-error-text");
				iconErrorText.style.whiteSpace = WhiteSpace.Normal;
				iconErrorCard.Add(iconErrorText);
				section.Add(iconErrorCard);
			}
			else if (!isNewAvatar && _currentHeroConfig.avatarIcon == null)
			{
				var iconWarningCard = new VisualElement();
				iconWarningCard.AddToClassList("yucp-validation-warning");
				var iconWarningText = new Label("Avatar icon is missing. Consider adding one using the Capture or Upload button in the hero section.");
				iconWarningText.AddToClassList("yucp-validation-warning-text");
				iconWarningText.style.whiteSpace = WhiteSpace.Normal;
				iconWarningCard.Add(iconWarningText);
				section.Add(iconWarningCard);
			}

			try
			{
				var tempContainer = new VisualElement();
				_controlPanelBuilder.CreateValidationsGUI(tempContainer);
				
				if (tempContainer.childCount > 0)
				{
					foreach (var child in tempContainer.Children().ToList())
					{
						child.RemoveFromHierarchy();
						var label = child.Q<Label>();
						if (label != null)
						{
							var message = label.text;
							var severity = ControlPanelDataBridge.ValidationSeverity.Info;
							
							if (child.ClassListContains("error") || message.ToLowerInvariant().Contains("error"))
								severity = ControlPanelDataBridge.ValidationSeverity.Error;
							else if (child.ClassListContains("warning") || message.ToLowerInvariant().Contains("warning"))
								severity = ControlPanelDataBridge.ValidationSeverity.Warning;
							
							var validationCard = new VisualElement();
							if (severity == ControlPanelDataBridge.ValidationSeverity.Error)
							{
								validationCard.AddToClassList("yucp-validation-error");
								var errorText = new Label(message);
								errorText.AddToClassList("yucp-validation-error-text");
								validationCard.Add(errorText);
							}
							else if (severity == ControlPanelDataBridge.ValidationSeverity.Warning)
							{
								validationCard.AddToClassList("yucp-validation-warning");
								var warningText = new Label(message);
								warningText.AddToClassList("yucp-validation-warning-text");
								validationCard.Add(warningText);
							}
							else
							{
								validationCard.AddToClassList("yucp-validation-success");
								var infoText = new Label(message);
								infoText.AddToClassList("yucp-validation-success-text");
								validationCard.Add(infoText);
							}
							section.Add(validationCard);
						}
						else
						{
							section.Add(child);
						}
					}
				}
				else
				{
					var successCard = new VisualElement();
					successCard.AddToClassList("yucp-validation-success");
					var successText = new Label("No validation issues found. Ready to build.");
					successText.AddToClassList("yucp-validation-success-text");
					section.Add(successCard);
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to get validation issues: {ex.Message}");
				var warningBox = new VisualElement();
				warningBox.AddToClassList("yucp-validation-warning");
				var warningText = new Label("Unable to retrieve validation issues from Control Panel.");
				warningText.AddToClassList("yucp-validation-warning-text");
				warningBox.Add(warningText);
				section.Add(warningBox);
			}

			var card = CreateCard("Validation", section, "Check for issues and warnings before uploading. Fix any errors to ensure your avatar uploads successfully.");
			_validationSectionHost.Add(card);
		}

		private void BuildAdvancedSection()
		{
			if (_advancedSectionHost == null)
				return;

			_advancedSectionHost.Clear();

			if (_currentHeroConfig == null || _selectedProfile == null)
				return;

			var content = new VisualElement();
			
			var foldout = new Foldout();
			foldout.text = "Advanced Settings";
			foldout.value = false; // Collapsed by default
			foldout.AddToClassList("yucp-foldout");
			content.style.paddingTop = 12;

			// Blueprint ID Editor
			var blueprintEditor = BlueprintIdEditor.Create(_currentHeroConfig, _selectedProfile, () =>
			{
				EditorUtility.SetDirty(_currentHeroConfig);
				BuildAdvancedSection(); // Refresh to show updated IDs
			});
			if (blueprintEditor != null)
			{
				content.Add(blueprintEditor);
			}

			// Additional advanced settings can be added here
			var helpText = new Label("Advanced settings are for manual configuration. Most users don't need to modify these.");
			helpText.AddToClassList("yucp-help-text");
			helpText.style.marginTop = 12;
			content.Add(helpText);

			foldout.Add(content);
			var card = CreateCard("Advanced Settings", foldout, "Configure blueprint IDs, version numbers, and other advanced options for your avatar.");
			_advancedSectionHost.Add(card);
		}

		private void SyncAvatarToControlPanel()
		{
			if (_cpUiBinder == null || _currentHeroConfig == null || _isSyncingToControlPanel)
				return;

			try
			{
				_isSyncingToControlPanel = true;
				_cpUiBinder.BindActiveAvatar(_selectedProfile, _currentHeroConfig);
			}
			finally
			{
				_isSyncingToControlPanel = false;
			}
		}

		private sealed class ControlPanelUiBinder
		{
			private readonly AvatarToolsWindow _window;
			private IVRCSdkControlPanelBuilder _builder;

			internal ControlPanelUiBinder(AvatarToolsWindow window)
			{
				_window = window;
			}

			internal bool IsFor(IVRCSdkControlPanelBuilder builder) => _builder == builder;

			internal void Initialize(IVRCSdkControlPanelBuilder builder)
			{
				_builder = builder;
			}

			internal void BindActiveAvatar(AvatarCollection profile, AvatarAsset config)
			{
				if (_builder == null || config == null)
					return;

				try
				{
					// Suppress events to prevent UI rebuilds while syncing
					if (!string.IsNullOrEmpty(config.avatarName))
						ControlPanelDataBridge.TrySetAvatarName(_builder, config.avatarName, suppressEvents: true);
					if (!string.IsNullOrEmpty(config.description))
						ControlPanelDataBridge.TrySetAvatarDescription(_builder, config.description, suppressEvents: true);
					if (config.tags != null)
						ControlPanelDataBridge.TrySetAvatarTags(_builder, config.tags, suppressEvents: true);
					var releaseStatus = config.releaseStatus == ReleaseStatus.Public ? "public" : "private";
					ControlPanelDataBridge.TrySetReleaseStatus(_builder, releaseStatus, suppressEvents: true);
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"[AvatarUploader] Failed to sync avatar to Control Panel: {ex.Message}");
				}
			}

			internal bool TryApplyThumbnailFromTexture(Texture2D texture, string hint)
			{
				if (_builder == null || texture == null)
					return false;

				try
				{
					var directory = Path.Combine(Path.GetTempPath(), "YUCP.AvatarTools.Thumbnails");
					if (!Directory.Exists(directory))
					{
						Directory.CreateDirectory(directory);
					}

					var safeName = MakeSafeFileName(hint);
					var filePath = Path.Combine(directory, $"{safeName}_{Guid.NewGuid():N}.png");
					var pngData = texture.EncodeToPNG();
					if (pngData == null || pngData.Length == 0)
						return false;

					File.WriteAllBytes(filePath, pngData);
					return ControlPanelDataBridge.TryUploadThumbnail(_builder, filePath);
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"[AvatarUploader] Failed to pipe thumbnail to Control Panel: {ex.Message}");
					return false;
				}
			}

			internal IVRCSdkControlPanelBuilder GetBuilder() => _builder;

			internal void Dispose()
			{
				_builder = null;
			}
		}

		private void EnsureGalleryList(AvatarAsset config)
		{
			if (config == null)
				return;
			
			if (config.galleryImages == null)
			{
				config.galleryImages = new List<AvatarGalleryImage>();
			}
		}

		private void EnsureGalleryLoaded(AvatarCollection profile, AvatarAsset config, bool forceReload = false)
		{
			Debug.Log($"[AvatarUploader] EnsureGalleryLoaded called for config: {config?.avatarName ?? "null"}, forceReload: {forceReload}");
			
			if (config == null)
			{
				Debug.LogWarning("[AvatarUploader] EnsureGalleryLoaded: config is null");
				return;
			}

			EnsureGalleryList(config);

			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration)
			{
				Debug.LogWarning("[AvatarUploader] EnsureGalleryLoaded: Gallery integration is disabled");
				return;
			}

			if (_loadingGalleries.Contains(config))
			{
				Debug.Log("[AvatarUploader] EnsureGalleryLoaded: Already loading gallery for this config");
				return;
			}

			// Check if existing images have valid URLs - if not, force reload
			bool hasValidImages = false;
			if (config.galleryImages != null && config.galleryImages.Count > 0)
			{
				hasValidImages = config.galleryImages.Any(img => img != null && !string.IsNullOrEmpty(img.url));
				Debug.Log($"[AvatarUploader] EnsureGalleryLoaded: Gallery has {config.galleryImages.Count} images, valid URLs: {hasValidImages}");
			}

			// If gallery already has valid images and we're not forcing reload, skip
			if (!forceReload && hasValidImages)
			{
				Debug.Log($"[AvatarUploader] EnsureGalleryLoaded: Gallery already has valid images, skipping load");
				return;
			}

			// Clear invalid/stale images before loading
			if (!hasValidImages && config.galleryImages != null && config.galleryImages.Count > 0)
			{
				Debug.Log("[AvatarUploader] EnsureGalleryLoaded: Clearing invalid/stale gallery images");
				config.galleryImages.Clear();
			}

			var avatarId = GetPreferredBlueprintId(config);
			if (string.IsNullOrEmpty(avatarId))
			{
				Debug.LogWarning("[AvatarUploader] EnsureGalleryLoaded: No blueprint ID found for avatar");
				return;
			}

			Debug.Log($"[AvatarUploader] EnsureGalleryLoaded: Starting gallery load for avatarId: {avatarId}");
			LoadGalleryAsync(profile, config, avatarId);
		}

		private async void LoadGalleryAsync(AvatarCollection profile, AvatarAsset config, string avatarId)
		{
			Debug.Log($"[AvatarUploader] LoadGalleryAsync: Starting for avatarId: {avatarId}");
			_loadingGalleries.Add(config);
			try
			{
				Debug.Log($"[AvatarUploader] LoadGalleryAsync: Calling GetGalleryAsync...");
				var entries = await AvatarGalleryClient.GetGalleryAsync(avatarId);
				Debug.Log($"[AvatarUploader] LoadGalleryAsync: Received {entries?.Count ?? 0} gallery entries");
				
				EnsureGalleryList(config);
				config.galleryImages.Clear();
				config.galleryImages.AddRange(entries);
				Debug.Log($"[AvatarUploader] LoadGalleryAsync: Added {config.galleryImages.Count} images to config.galleryImages");
			
			// Don't download thumbnails upfront - load them lazily when needed

				// Update gallery card and hero slides if this is still the current avatar
				if (_currentHeroConfig == config)
				{
					Debug.Log($"[AvatarUploader] LoadGalleryAsync: Current config matches, scheduling UI update");
					EditorApplication.delayCall += () =>
					{
						if (_currentHeroConfig == config)
						{
							Debug.Log($"[AvatarUploader] LoadGalleryAsync: Updating UI with {config.galleryImages.Count} images");
							BuildGalleryCard(profile, config);
							RebuildHeroSlides(config);
							Debug.Log($"[AvatarUploader] LoadGalleryAsync: Rebuilt hero slides, count: {_heroSlides.Count}");
							// Show the first image if available
							if (_heroSlides.Count > 0)
							{
								_activeHeroSlideIndex = 0;
								ShowHeroSlide();
							}
							// Only update the specific tile in the grid, not rebuild everything
							UpdateAvatarTileInGrid(profile, config);
						}
						else
						{
							Debug.Log("[AvatarUploader] LoadGalleryAsync: Config changed, skipping UI update");
						}
					};
				}
				else
				{
					Debug.Log($"[AvatarUploader] LoadGalleryAsync: Current config doesn't match (current: {_currentHeroConfig?.avatarName ?? "null"}, loaded: {config?.avatarName ?? "null"})");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AvatarUploader] LoadGalleryAsync: Exception occurred: {ex.Message}\n{ex.StackTrace}");
			}
			finally
			{
				_loadingGalleries.Remove(config);
				Debug.Log("[AvatarUploader] LoadGalleryAsync: Completed and removed from loading set");
			}
		}

		private void RebuildHeroSlides(AvatarAsset config)
		{
			if (config == null)
			{
				_heroSlides.Clear();
				ShowHeroSlide();
				return;
			}

			// Only rebuild if config matches current hero config (prevent stale data from async operations)
			if (config != _currentHeroConfig)
			{
				return;
			}

			// Clear slides before rebuilding
			_heroSlides.Clear();

			// Only add gallery images - icon is now in the sidebar
			// Add slides even without thumbnails - we'll load them lazily
			if (config.galleryImages != null)
			{
				foreach (var img in config.galleryImages)
				{
					if (img == null)
						continue;
					_heroSlides.Add(new HeroSlide
					{
						Type = HeroSlideType.GalleryImage,
						Gallery = img,
						Texture = img.thumbnail // May be null, will load on demand
					});
				}
			}

			_activeHeroSlideIndex = Mathf.Clamp(_currentHeroConfig?.activeGalleryIndex ?? 0, 0, _heroSlides.Count - 1);
			ShowHeroSlide();
		}

		private async void ShowHeroSlide()
		{
			Debug.Log($"[AvatarUploader] ShowHeroSlide: Called, slides count: {_heroSlides.Count}, active index: {_activeHeroSlideIndex}");
			
			if (_heroImageDisplay == null || _heroSlideLabel == null)
			{
				Debug.LogWarning("[AvatarUploader] ShowHeroSlide: _heroImageDisplay or _heroSlideLabel is null");
				return;
			}

			if (_heroSlides.Count == 0)
			{
				Debug.Log("[AvatarUploader] ShowHeroSlide: No slides, showing empty state");
				_heroImageDisplay.style.display = DisplayStyle.None;
				_heroImageDisplay.image = null;
				_heroSlideLabel.text = "No gallery images";
				UpdateHeroOverlayState();
				return;
			}

			_activeHeroSlideIndex = Mathf.Clamp(_activeHeroSlideIndex, 0, _heroSlides.Count - 1);
			if (_currentHeroConfig != null)
			{
				_currentHeroConfig.activeGalleryIndex = _activeHeroSlideIndex;
			}

			var slide = _heroSlides[_activeHeroSlideIndex];
			Debug.Log($"[AvatarUploader] ShowHeroSlide: Slide type: {slide.Type}, Gallery: {(slide.Gallery != null ? "exists" : "null")}, URL: {slide.Gallery?.url ?? "null"}, Texture: {(slide.Texture != null ? "exists" : "null")}");
			
			if (slide.Type == HeroSlideType.GalleryImage)
			{
				_heroImageDisplay.style.display = DisplayStyle.Flex;
				_heroSlideLabel.text = $"Gallery Image {_activeHeroSlideIndex + 1} of {_heroSlides.Count}";
				
				// Load image lazily if not already loaded
				if (slide.Texture == null && slide.Gallery != null && !string.IsNullOrEmpty(slide.Gallery.url))
				{
					Debug.Log($"[AvatarUploader] ShowHeroSlide: Loading image from URL: {slide.Gallery.url}");
					// Show loading state
					_heroImageDisplay.image = null;
					_heroImageDisplay.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
					
					// Load image from URL
					var texture = await AvatarGalleryClient.DownloadImageAsync(slide.Gallery.url);
					Debug.Log($"[AvatarUploader] ShowHeroSlide: Downloaded texture: {(texture != null ? $"exists ({texture.width}x{texture.height})" : "null")}");
					
					if (texture != null && _currentHeroConfig != null && _activeHeroSlideIndex < _heroSlides.Count && _heroSlides[_activeHeroSlideIndex].Gallery == slide.Gallery)
					{
						slide.Texture = texture;
						slide.Gallery.thumbnail = texture; // Cache for thumbnail grid
						_heroImageDisplay.image = texture;
						_heroImageDisplay.style.backgroundColor = Color.clear;
						Debug.Log("[AvatarUploader] ShowHeroSlide: Image loaded and displayed successfully");
					}
					else
					{
						Debug.LogWarning($"[AvatarUploader] ShowHeroSlide: Failed to set image - texture: {texture != null}, currentConfig: {_currentHeroConfig != null}, index valid: {_activeHeroSlideIndex < _heroSlides.Count}, gallery match: {(_activeHeroSlideIndex < _heroSlides.Count && _heroSlides[_activeHeroSlideIndex].Gallery == slide.Gallery)}");
					}
				}
				else
				{
					Debug.Log($"[AvatarUploader] ShowHeroSlide: Using existing texture: {(slide.Texture != null ? "exists" : "null")}");
					_heroImageDisplay.image = slide.Texture;
					_heroImageDisplay.style.backgroundColor = Color.clear;
				}
			}

			UpdateHeroOverlayState();
			UpdateThumbnailSelection();
		}

		private void UpdateThumbnailSelection()
		{
			if (_gallerySectionHost == null)
				return;

			// Find the thumbnail grid in the gallery section
			var thumbnailGrid = _gallerySectionHost.Q(className: "yucp-gallery-grid");
			if (thumbnailGrid == null)
				return;

			// Update all thumbnails
			int index = 0;
			foreach (var child in thumbnailGrid.Children())
			{
				if (child is Image thumbnail)
				{
					if (index == _activeHeroSlideIndex)
					{
						thumbnail.AddToClassList("yucp-gallery-thumbnail-active");
					}
					else
					{
						thumbnail.RemoveFromClassList("yucp-gallery-thumbnail-active");
					}
					index++;
				}
			}
		}

		private void UpdateHeroOverlayState()
		{
			bool hasMultiple = _heroSlides.Count > 1;
			_heroPrevButton?.SetEnabled(hasMultiple);
			_heroNextButton?.SetEnabled(hasMultiple);

			if (_heroSetIconButton != null)
			{
				// Show "Set Icon" button on gallery image slides
				var showSetIcon = _heroSlides.Count > 0 && 
				                  _heroSlides[_activeHeroSlideIndex].Type == HeroSlideType.GalleryImage &&
				                  _heroSlides[_activeHeroSlideIndex].Gallery != null;
				_heroSetIconButton.style.display = showSetIcon ? DisplayStyle.Flex : DisplayStyle.None;
				_heroSetIconButton.SetEnabled(showSetIcon);
			}

			if (_heroDeleteButton != null)
			{
				// Show "Delete" button on gallery image slides
				var showDelete = _heroSlides.Count > 0 && 
				                 _heroSlides[_activeHeroSlideIndex].Type == HeroSlideType.GalleryImage &&
				                 _heroSlides[_activeHeroSlideIndex].Gallery != null;
				_heroDeleteButton.style.display = showDelete ? DisplayStyle.Flex : DisplayStyle.None;
				_heroDeleteButton.SetEnabled(showDelete);
			}

			// Show capture/upload buttons when on icon slide or when no icon exists
			if (_heroOverlay != null)
			{
				var captureButton = _heroOverlay.Q<Button>(className: "yucp-hero-slide-button");
				var uploadButton = _heroOverlay.Q<Button>(className: "yucp-hero-slide-button");
				// These buttons are positioned absolutely, so they're always visible when needed
				// The logic is handled by their click handlers checking the current state
			}
		}

		private void CaptureThumbnailFromScene()
		{
			if (_currentHeroConfig == null || _currentHeroConfig.avatarPrefab == null)
			{
				EditorUtility.DisplayDialog("Avatar Tools", "Select an avatar prefab first.", "OK");
				return;
			}

			// Open the new capture window
			AvatarCaptureWindow.OpenForAvatar(_currentHeroConfig, _selectedProfile, OnCaptureComplete);
		}

		private void OnCaptureComplete(Texture2D capturedTexture)
		{
			if (capturedTexture != null && _currentHeroConfig != null)
			{
				// Save to avatar config
				Undo.RecordObject(_selectedProfile, "Set Avatar Icon");
				_currentHeroConfig.avatarIcon = capturedTexture;
				EditorUtility.SetDirty(_selectedProfile);
				
				// Sync to Control Panel if available
				if (_cpUiBinder != null)
				{
					var nameHint = string.IsNullOrEmpty(_currentHeroConfig.avatarName) ? "avatar" : _currentHeroConfig.avatarName;
					_cpUiBinder.TryApplyThumbnailFromTexture(capturedTexture, nameHint);
				}
				
				// Update icon sidebar
				BuildIconSidebar(_currentHeroConfig);
				
				// Update the hero slides to show the new thumbnail
				RebuildHeroSlides(_currentHeroConfig);
				
				// Refresh the grid view to show the new icon
				BuildAvatarGridView(_selectedProfile);
			}
		}

		private void UploadThumbnailImage()
		{
			if (_currentHeroConfig == null)
			{
				EditorUtility.DisplayDialog("Avatar Tools", "Select an avatar first.", "OK");
				return;
			}

			// Use Unity's file picker directly - no Control Panel needed!
			string path = EditorUtility.OpenFilePanel("Select thumbnail image", "", "png,jpg,jpeg");
			if (string.IsNullOrEmpty(path))
				return;

			try
			{
				// Load the image file
				var texture = new Texture2D(2, 2);
				if (texture.LoadImage(File.ReadAllBytes(path)))
				{
					// Save to avatar config
					Undo.RecordObject(_selectedProfile, "Set Avatar Icon");
					_currentHeroConfig.avatarIcon = texture;
					EditorUtility.SetDirty(_selectedProfile);
					
					// Sync to Control Panel if available
					if (_cpUiBinder != null)
					{
						var nameHint = string.IsNullOrEmpty(_currentHeroConfig.avatarName) ? "avatar" : _currentHeroConfig.avatarName;
						_cpUiBinder.TryApplyThumbnailFromTexture(texture, nameHint);
					}
					
					// Update icon sidebar
					BuildIconSidebar(_currentHeroConfig);
					
					// Update the hero slides to show the new thumbnail
					RebuildHeroSlides(_currentHeroConfig);
					
					EditorUtility.DisplayDialog("Avatar Tools", "Thumbnail uploaded successfully!", "OK");
				}
				else
				{
					EditorUtility.DisplayDialog("Avatar Tools", "Failed to load image file. Please ensure it's a valid PNG, JPG, or JPEG image.", "OK");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AvatarUploader] Thumbnail upload failed: {ex.Message}\n{ex.StackTrace}");
				EditorUtility.DisplayDialog("Avatar Tools", $"Thumbnail upload failed: {ex.Message}", "OK");
			}
		}

		private void CycleHeroSlide(int direction)
		{
			if (_heroSlides.Count == 0)
				return;

			_activeHeroSlideIndex = (_activeHeroSlideIndex + direction + _heroSlides.Count) % _heroSlides.Count;
			ShowHeroSlide();
		}

		private async void LoadThumbnailAsync(Image thumbnailElement, AvatarGalleryImage galleryImage)
		{
			if (thumbnailElement == null || galleryImage == null || string.IsNullOrEmpty(galleryImage.url))
				return;

			try
			{
				// Download thumbnail (use URL with size parameter if available, otherwise full image)
				var thumbnailUrl = galleryImage.url;
				// VRChat API supports size parameters - try to get a smaller version for thumbnails
				// If URL already has parameters, append; otherwise add ?size=256
				if (!thumbnailUrl.Contains("?"))
				{
					thumbnailUrl += "?size=256";
				}
				else if (!thumbnailUrl.Contains("size="))
				{
					thumbnailUrl += "&size=256";
				}

				var texture = await AvatarGalleryClient.DownloadImageAsync(thumbnailUrl);
				if (texture != null && thumbnailElement != null && thumbnailElement.parent != null)
				{
					galleryImage.thumbnail = texture;
					thumbnailElement.image = texture;
					thumbnailElement.style.backgroundColor = Color.clear;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to load thumbnail: {ex.Message}");
			}
		}

		private string GetPreferredBlueprintId(AvatarAsset config)
		{
			if (!string.IsNullOrEmpty(config?.blueprintIdPC))
				return config.blueprintIdPC;
			if (!string.IsNullOrEmpty(config?.blueprintIdQuest))
				return config.blueprintIdQuest;
			return null;
		}

		private async void AddGalleryImage(AvatarCollection profile, AvatarAsset config)
		{
			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration)
			{
				EditorUtility.DisplayDialog("Gallery", "Enable gallery integration and store an API key in Avatar Tools settings before uploading images.", "OK");
				return;
			}

			if (config == null)
			{
				EditorUtility.DisplayDialog("Gallery", "Select an avatar first.", "OK");
				return;
			}

			var avatarId = GetPreferredBlueprintId(config);
			if (string.IsNullOrEmpty(avatarId))
			{
				EditorUtility.DisplayDialog("Gallery", "Assign a blueprint ID before uploading gallery images.", "OK");
				return;
			}

			string path = EditorUtility.OpenFilePanel("Select gallery image", "", "png,jpg,jpeg");
			if (string.IsNullOrEmpty(path))
				return;

			byte[] data;
			try
			{
				data = File.ReadAllBytes(path);
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AvatarUploader] Failed to read image file: {ex.Message}");
				EditorUtility.DisplayDialog("Gallery", "Unable to read the selected file.", "OK");
				return;
			}

			var fileName = Path.GetFileName(path);
			var uploadProgress = 0f;
			_status = "Uploading gallery image...";
			UpdateProgress(uploadProgress, _status);

			try
			{
				var success = await AvatarGalleryClient.UploadGalleryImageAsync(avatarId, data, fileName);
				if (success)
				{
					// Reload gallery after successful upload
					var entries = await AvatarGalleryClient.GetGalleryAsync(avatarId);
					EnsureGalleryList(config);
					config.galleryImages.Clear();
					config.galleryImages.AddRange(entries);
					// Don't download thumbnails upfront - load them lazily
					BuildGalleryCard(profile, config);
					RebuildHeroSlides(config);
					// Show the newly uploaded image (last in the list)
					if (_heroSlides.Count > 0)
					{
						_activeHeroSlideIndex = _heroSlides.Count - 1;
						ShowHeroSlide();
					}
					BuildAvatarGridView(profile);
					_toast?.ShowSuccess("Gallery image uploaded successfully.", "Upload Complete", 3f);
				}
				else
				{
					_toast?.ShowError("Gallery upload failed. Check console for details.", "Upload Failed", 5f);
					Debug.LogError("[AvatarUploader] Gallery upload returned false. Check API key and authentication.");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AvatarUploader] Gallery upload failed: {ex.Message}");
				Debug.LogException(ex);
				_toast?.ShowError($"Gallery upload failed: {ex.Message}", "Upload Error", 5f);
			}
			finally
			{
				_status = string.Empty;
				UpdateProgress(0f, _status);
			}
		}

		private async void DeleteActiveGalleryImage(AvatarCollection profile, AvatarAsset config)
		{
			if (config == null)
			{
				EditorUtility.DisplayDialog("Gallery", "Select an avatar first.", "OK");
				return;
			}

			if (_heroSlides.Count == 0)
			{
				EditorUtility.DisplayDialog("Gallery", "No gallery images available to delete.", "OK");
				return;
			}

			var slide = _heroSlides[Mathf.Clamp(_activeHeroSlideIndex, 0, _heroSlides.Count - 1)];
			if (slide.Type != HeroSlideType.GalleryImage || slide.Gallery == null)
			{
				EditorUtility.DisplayDialog("Gallery", "Select a gallery image to delete.", "OK");
				return;
			}

			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration)
			{
				EditorUtility.DisplayDialog("Gallery", "Enable gallery integration in Avatar Tools settings before deleting images.", "OK");
				return;
			}

			if (string.IsNullOrEmpty(config.blueprintIdPC) && string.IsNullOrEmpty(config.blueprintIdQuest))
			{
				EditorUtility.DisplayDialog("Gallery", "Assign a blueprint ID before managing gallery images.", "OK");
				return;
			}

			var fileId = slide.Gallery.fileId;
			if (string.IsNullOrEmpty(fileId))
			{
				EditorUtility.DisplayDialog("Gallery", "Invalid gallery image file ID.", "OK");
				return;
			}

			if (!EditorUtility.DisplayDialog("Delete Gallery Image", 
				"Are you sure you want to delete this gallery image from VRChat? This action cannot be undone.", 
				"Delete", "Cancel"))
			{
				return;
			}

			_status = "Deleting gallery image...";
			UpdateProgress(0f, _status);

			try
			{
				var success = await AvatarGalleryClient.DeleteGalleryImageAsync(fileId);
				if (success)
				{
					// Remove from local list
					if (config.galleryImages != null)
					{
						config.galleryImages.RemoveAll(img => img.fileId == fileId);
					}

					// Reload gallery from API to ensure consistency
					var avatarId = config.blueprintIdPC ?? config.blueprintIdQuest;
					var entries = await AvatarGalleryClient.GetGalleryAsync(avatarId);
					EnsureGalleryList(config);
					config.galleryImages.Clear();
					config.galleryImages.AddRange(entries);
					// Don't download thumbnails upfront - load them lazily
					BuildGalleryCard(profile, config);
					RebuildHeroSlides(config);
					// Show the first image if available, or clear if none
					if (_heroSlides.Count > 0)
					{
						_activeHeroSlideIndex = 0;
						ShowHeroSlide();
					}
					else
					{
						ShowHeroSlide(); // This will show "No gallery images" message
					}
					BuildAvatarGridView(profile);
					_toast?.ShowSuccess("Gallery image deleted successfully.", "Delete Complete", 3f);
				}
				else
				{
					_toast?.ShowError("Gallery delete failed. Check console for details.", "Delete Failed", 5f);
					Debug.LogError("[AvatarUploader] Gallery delete returned false. Check authentication.");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AvatarUploader] Gallery delete failed: {ex.Message}");
				Debug.LogException(ex);
				_toast?.ShowError($"Gallery delete failed: {ex.Message}", "Delete Error", 5f);
			}
			finally
			{
				_status = string.Empty;
				UpdateProgress(0f, _status);
			}
		}

		private async void SetActiveGalleryImageAsIcon(AvatarCollection profile, AvatarAsset config)
		{
			if (config == null)
			{
				EditorUtility.DisplayDialog("Gallery", "Select an avatar first.", "OK");
				return;
			}

			if (_heroSlides.Count == 0)
			{
				EditorUtility.DisplayDialog("Gallery", "No gallery images available to use as an icon.", "OK");
				return;
			}

			var slide = _heroSlides[Mathf.Clamp(_activeHeroSlideIndex, 0, _heroSlides.Count - 1)];
			if (slide.Type != HeroSlideType.GalleryImage || slide.Gallery == null)
			{
				EditorUtility.DisplayDialog("Gallery", "Cycle to a gallery image before setting the icon.", "OK");
				return;
			}

			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration)
			{
				EditorUtility.DisplayDialog("Gallery", "Enable gallery integration in Avatar Tools settings to call the VRChat API.", "Open Settings", "Cancel");
				return;
			}

			var avatarId = GetPreferredBlueprintId(config);
			if (string.IsNullOrEmpty(avatarId))
			{
				EditorUtility.DisplayDialog("Gallery", "Assign a blueprint ID before changing the icon.", "OK");
				return;
			}

			if (string.IsNullOrEmpty(slide.Gallery?.fileId))
			{
				EditorUtility.DisplayDialog("Gallery", "The selected gallery entry is missing its VRChat file identifier.", "OK");
				return;
			}

			var texture = slide.Texture;
			if (texture != null && _cpUiBinder != null)
			{
				var nameHint = string.IsNullOrEmpty(config.avatarName) ? "avatar" : config.avatarName;
				if (_cpUiBinder.TryApplyThumbnailFromTexture(texture, nameHint))
				{
					Undo.RecordObject(profile, "Set Avatar Icon");
					config.avatarIcon = texture;
					EditorUtility.SetDirty(profile);
					BuildIconSidebar(config);
					EditorUtility.DisplayDialog("Gallery", "Thumbnail queued in the Control Panel build section. Finish the upload from the Build / Publish panel.", "OK");
					return;
				}
			}

			try
			{
				var updated = await AvatarGalleryClient.SetAvatarIconAsync(avatarId, slide.Gallery.fileId);
				if (updated)
				{
					Undo.RecordObject(profile, "Set Avatar Icon");
					config.avatarIcon = slide.Texture;
					EditorUtility.SetDirty(profile);
					BuildIconSidebar(config);
					BuildAvatarGridView(profile);
					EditorUtility.DisplayDialog("Gallery", "Avatar icon updated. It may take a few moments for VRChat to reflect the change.", "OK");
				}
				else
				{
					EditorUtility.DisplayDialog("Gallery", "The VRChat API rejected the icon change. Check the console for details.", "OK");
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to set avatar icon: {ex.Message}");
				EditorUtility.DisplayDialog("Gallery", "Avatar icon update failed. Review the console for more information.", "OK");
			}
		}

		private void ShowStartupDisclaimerIfNeeded()
		{
			if (SessionState.GetBool(StartupDisclaimerKey, false))
				return;

			const string title = "Avatar Tools Notice";
			const string message = @"Avatar Tools sits on top of the official VRChat Control Panel. Every validation step, check, and upload is still executed by the VRChat SDK. This UI does not bypass, suppress, or alter SDK safeguards; it simply drives the same Control Panel features in a different layout.

Because the tool automates Control Panel interactions, use may still fall under the VRChat SDK Terms of Service. YUCP Studios, Yeusepe, and contributors provide this utility without warranty and accept no responsibility for enforcement actions, moderation outcomes, data loss, or any other consequences stemming from its use.

By continuing you acknowledge these terms and accept full responsibility for any actions initiated through Avatar Tools.";

			EditorUtility.DisplayDialog(title, message, "I Understand");
			SessionState.SetBool(StartupDisclaimerKey, true);
		}

		private void EnsureUploadDisclaimerAcknowledged()
		{
			if (SessionState.GetBool(UploadDisclaimerKey, false))
				return;

			const string title = "Upload Compliance Notice";
			const string message = @"Avatar Tools dispatches builds and uploads to the VRChat SDK Control Panel. The VRChat SDK remains fully responsible for packaging content, running validations, and transferring data; all submissions remain subject to the VRChat Terms of Service, Community Guidelines, and SDK license.

Using this feature does not grant exemptions from anti-automation provisions, copyright requirements, or content restrictions. Confirm you own or are licensed to use every asset, ensure your uploads comply with VRChat policies, and maintain independent backups. YUCP Studios, Yeusepe, and contributors make no guarantees and accept no liability for account actions, takedowns, data corruption, or damages resulting from this workflow.

Select Continue to confirm you understand and accept these conditions.";

			if (EditorUtility.DisplayDialog(title, message, "Continue", "Cancel"))
			{
				SessionState.SetBool(UploadDisclaimerKey, true);
			}
			else
			{
				throw new OperationCanceledException("Upload cancelled by user");
			}
		}

		private const string StartupDisclaimerKey = "YUCP.AvatarTools.DisclaimerShown";
		private const string UploadDisclaimerKey = "YUCP.AvatarTools.UploadDisclaimerShown";

		/// <summary>
		/// Reserve avatar ID and assign blueprint ID to PipelineManager for new avatars.
		/// Also handle copyright agreement ourselves so BuildAndUpload skips its dialog.
		/// </summary>
		private async Task<bool> PrepareAvatarForUpload(
			AvatarAsset config,
			VRC.Core.PipelineManager pipelineManager, 
			GameObject avatarRoot, 
			VRCAvatar avatarPayload)
		{
			// For new avatars, reserve the avatar ID and assign it to the PipelineManager
			// This is what ReserveAvatarId does in BuildAndUpload, but we do it ourselves first
			bool isNewAvatar = string.IsNullOrWhiteSpace(pipelineManager.blueprintId);
			
			if (isNewAvatar)
			{
				// Validate that avatarPayload is properly initialized
				if (avatarPayload.Name == null || string.IsNullOrWhiteSpace(avatarPayload.Name))
				{
					_toast?.ShowError("Avatar name is required to reserve avatar ID.", "Build Error", 5f);
					return false;
				}

				// Check for missing icon on new avatars
				if (config != null && config.avatarIcon == null)
				{
					_toast?.ShowError("Avatar icon is required before first upload. Use the Capture or Upload button in the hero section to add an icon.", "Icon Required", 5f);
					return false;
				}
				
				try
				{
					// Call VRCApi.CreateAvatarRecord directly (same as ReserveAvatarId does)
					var createdAvatar = await VRCApi.CreateAvatarRecord(avatarPayload, (status, percentage) => 
					{
						UpdateProgress(percentage * 0.1f, $"Reserving avatar ID: {status}");
					}, System.Threading.CancellationToken.None);
					
					if (string.IsNullOrEmpty(createdAvatar.ID))
					{
						_toast?.ShowError("Failed to reserve avatar ID. Cannot proceed with upload.", "Build Error", 5f);
						return false;
					}
					
					// Set the blueprint ID on the PipelineManager that BuildAndUpload will use
					// This is the PipelineManager on avatarRoot (the GameObject)
					Undo.RecordObject(pipelineManager, "Assigning a new ID");
					pipelineManager.blueprintId = createdAvatar.ID;
					EditorUtility.SetDirty(pipelineManager);
					
					// Also update the prefab asset's PipelineManager if avatarRoot is a prefab instance
					// This ensures the blueprint ID persists in the prefab asset
					var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(avatarRoot);
					if (!string.IsNullOrEmpty(prefabPath))
					{
						var prefabAssetObj = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
						if (prefabAssetObj != null && prefabAssetObj.TryGetComponent<VRC.Core.PipelineManager>(out var prefabPM))
						{
							Undo.RecordObject(prefabPM, "Assigning a new ID");
							prefabPM.blueprintId = createdAvatar.ID;
							EditorUtility.SetDirty(prefabPM);
						}
					}
					
					// Save the asset to ensure the blueprint ID is persisted
					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh();
					
					// Update the avatarPayload with the reserved ID so BuildAndUpload uses it
					avatarPayload.ID = createdAvatar.ID;
				}
				catch (Exception ex)
				{
					_toast?.ShowError($"Failed to reserve avatar ID: {ex.Message}", "Build Error", 5f);
					Debug.LogException(ex);
					return false;
				}
			}
			
			// Now handle copyright agreement ourselves
			string contentId = pipelineManager.blueprintId;
			
			// For existing avatars, try to get the content ID from the API
			if (!isNewAvatar)
			{
				try
				{
					var content = await VRCApi.GetAvatar(pipelineManager.blueprintId);
					if (!string.IsNullOrEmpty(content.ID))
					{
						contentId = content.ID; // Use content ID for existing avatars
					}
				}
				catch
				{
					// Avatar doesn't exist yet, use blueprintId
					contentId = pipelineManager.blueprintId;
				}
			}

			// Check if already agreed using reflection (HasAgreement is internal)
			var hasAgreementMethod = typeof(VRC.SDKBase.VRCCopyrightAgreement).GetMethod("HasAgreement", 
				BindingFlags.NonPublic | BindingFlags.Static);
			if (hasAgreementMethod != null)
			{
				var hasAgreed = await (Task<bool>)hasAgreementMethod.Invoke(null, new object[] { contentId });
				if (hasAgreed)
				{
					return true; // Already agreed
				}
			}

			// Show our own copyright dialog
			var agreed = await ShowCopyrightDialogAsync();
			if (!agreed)
			{
				return false; // User cancelled
			}

			// Call Agree using reflection (it's internal)
			var agreeMethod = typeof(VRC.SDKBase.VRCCopyrightAgreement).GetMethod("Agree", 
				BindingFlags.NonPublic | BindingFlags.Static);
			if (agreeMethod != null)
			{
				var agreedResult = await (Task<bool>)agreeMethod.Invoke(null, new object[] { contentId });
				if (!agreedResult)
				{
					_toast?.ShowError("Failed to register copyright agreement with VRChat.", "Agreement Error", 5f);
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Show copyright agreement dialog using our own UI (non-blocking, async).
		/// </summary>
		private Task<bool> ShowCopyrightDialogAsync()
		{
			var tcs = new TaskCompletionSource<bool>();
			var agreementText = VRC.SDKBase.VRCCopyrightAgreement.AgreementText;

			// Modal overlay with backdrop
			var modalOverlay = new VisualElement();
			modalOverlay.name = "copyright-modal-overlay";
			modalOverlay.style.position = Position.Absolute;
			modalOverlay.style.left = 0;
			modalOverlay.style.top = 0;
			modalOverlay.style.right = 0;
			modalOverlay.style.bottom = 0;
			modalOverlay.style.backgroundColor = new Color(0, 0, 0, 0.85f);
			modalOverlay.style.justifyContent = Justify.Center;
			modalOverlay.style.alignItems = Align.Center;
			modalOverlay.style.flexDirection = FlexDirection.Column;

			// Main modal container
			var modalContainer = new VisualElement();
			modalContainer.name = "copyright-modal-container";
			modalContainer.style.width = 560;
			modalContainer.style.maxWidth = Length.Percent(90);
			modalContainer.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f); // #1a1a1a
			modalContainer.style.borderTopWidth = 2;
			modalContainer.style.borderTopColor = new Color(0.212f, 0.749f, 0.694f); // #36BFB1 accent
			modalContainer.style.paddingTop = 24;
			modalContainer.style.paddingBottom = 24;
			modalContainer.style.paddingLeft = 28;
			modalContainer.style.paddingRight = 28;

			// Header section with icon and title
			var headerSection = new VisualElement();
			headerSection.style.flexDirection = FlexDirection.Row;
			headerSection.style.alignItems = Align.Center;
			headerSection.style.marginBottom = 20;

			// Icon circle (using square since UIElements doesn't support borderRadius)
			var iconCircle = new VisualElement();
			iconCircle.style.width = 48;
			iconCircle.style.height = 48;
			iconCircle.style.backgroundColor = new Color(0.212f, 0.749f, 0.694f, 0.2f); // Teal with opacity
			iconCircle.style.justifyContent = Justify.Center;
			iconCircle.style.alignItems = Align.Center;
			iconCircle.style.marginRight = 16;

			var iconLabel = new Label("©");
			iconLabel.style.fontSize = 24;
			iconLabel.style.color = new Color(0.212f, 0.749f, 0.694f); // Teal
			iconLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
			iconCircle.Add(iconLabel);

			headerSection.Add(iconCircle);

			// Title
			var titleContainer = new VisualElement();
			titleContainer.style.flexGrow = 1;
			titleContainer.style.flexDirection = FlexDirection.Column;

			var title = new Label("Copyright Ownership Agreement");
			title.style.fontSize = 20;
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.color = new Color(1f, 1f, 1f); // White
			title.style.marginBottom = 4;
			titleContainer.Add(title);

			var subtitle = new Label("Required for content upload");
			subtitle.style.fontSize = 12;
			subtitle.style.color = new Color(0.69f, 0.69f, 0.69f); // #b0b0b0
			titleContainer.Add(subtitle);

			headerSection.Add(titleContainer);

			modalContainer.Add(headerSection);

			// Content section with agreement text
			var contentSection = new VisualElement();
			contentSection.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f); // Slightly lighter than background
			contentSection.style.paddingTop = 20;
			contentSection.style.paddingBottom = 20;
			contentSection.style.paddingLeft = 20;
			contentSection.style.paddingRight = 20;
			contentSection.style.marginBottom = 24;
			contentSection.style.borderLeftWidth = 3;
			contentSection.style.borderLeftColor = new Color(0.212f, 0.749f, 0.694f); // Teal accent

			var message = new Label(agreementText);
			message.style.whiteSpace = WhiteSpace.Normal;
			message.style.fontSize = 13;
			message.style.color = new Color(0.88f, 0.88f, 0.88f); // Light gray
			contentSection.Add(message);

			modalContainer.Add(contentSection);

			// Action buttons
			var buttonContainer = new VisualElement();
			buttonContainer.style.flexDirection = FlexDirection.Row;
			buttonContainer.style.justifyContent = Justify.FlexEnd;
			buttonContainer.style.alignItems = Align.Center;

			var cancelButton = new Button(() =>
			{
				rootVisualElement.Remove(modalOverlay);
				tcs.SetResult(false);
			})
			{
				text = "Cancel"
			};
			cancelButton.AddToClassList("yucp-button");
			cancelButton.style.marginRight = 12;
			buttonContainer.Add(cancelButton);

			var agreeButton = new Button(() =>
			{
				rootVisualElement.Remove(modalOverlay);
				tcs.SetResult(true);
			})
			{
				text = "I Agree"
			};
			agreeButton.AddToClassList("yucp-button");
			agreeButton.AddToClassList("yucp-button-primary");
			buttonContainer.Add(agreeButton);

			modalContainer.Add(buttonContainer);
			modalOverlay.Add(modalContainer);
			rootVisualElement.Add(modalOverlay);

			// Focus the agree button
			agreeButton.Focus();

			return tcs.Task;
		}

		// ============================================================================
		// RESPONSIVE LAYOUT
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

			// Apply appropriate class based on width
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
				_leftPaneOverlay.style.display = DisplayStyle.Flex;
				_leftPaneOverlay.style.visibility = Visibility.Visible;
				_leftPaneOverlay.style.position = Position.Absolute;
				_leftPaneOverlay.style.width = 270;
				_leftPaneOverlay.style.top = 0;
				_leftPaneOverlay.style.bottom = 0;
				_leftPaneOverlay.style.left = -270;
				_leftPaneOverlay.style.opacity = 0;

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

		private void CloseOverlay()
		{
			_isOverlayOpen = false;

			// Hide overlay
			if (_leftPaneOverlay != null)
			{
				_leftPaneOverlay.style.left = -270;
				_leftPaneOverlay.style.opacity = 0;

				_leftPaneOverlay.schedule.Execute(() =>
				{
					if (_leftPaneOverlay != null)
					{
						_leftPaneOverlay.style.display = DisplayStyle.None;
						_leftPaneOverlay.style.visibility = Visibility.Hidden;
					}
				}).StartingIn(300);
			}

			// Hide backdrop
			if (_overlayBackdrop != null)
			{
				_overlayBackdrop.style.opacity = 0;

				_overlayBackdrop.schedule.Execute(() =>
				{
					if (_overlayBackdrop != null)
					{
						_overlayBackdrop.style.display = DisplayStyle.None;
						_overlayBackdrop.style.visibility = Visibility.Hidden;
					}
				}).StartingIn(300);
			}
		}

		private VisualElement CreateLeftPaneOverlay()
		{
			var overlay = new VisualElement();
			overlay.AddToClassList("yucp-left-pane-overlay");

			// Create a copy of the left pane structure
			var container = new VisualElement();
			container.AddToClassList("yucp-left-pane");

			var header = new Label("Collections");
			header.AddToClassList("yucp-section-header");
			container.Add(header);

			// Add profile toolbar
			var toolbarContainer = new VisualElement();
			toolbarContainer.AddToClassList("yucp-profile-toolbar");
			ConfigureProfileToolbarForOverlay(toolbarContainer);
			container.Add(toolbarContainer);

			// Add profile list scroll
			var scrollView = new ScrollView();
			scrollView.AddToClassList("yucp-profile-list-scroll");
			var profileListHost = new VisualElement();
			profileListHost.name = "profile-list-host-overlay";
			scrollView.Add(profileListHost);
			container.Add(scrollView);

			overlay.Add(container);
			overlay.style.display = DisplayStyle.None;
			overlay.style.visibility = Visibility.Hidden;

			return overlay;
		}

		private void ConfigureProfileToolbarForOverlay(VisualElement toolbarHost)
		{
			if (toolbarHost == null)
				return;

			toolbarHost.Clear();

			var searchRow = new VisualElement();
			searchRow.AddToClassList("yucp-search-row");
			var searchField = new ToolbarSearchField();
			searchField.RegisterValueChangedCallback(evt =>
			{
				_profileSearchFilter = string.IsNullOrEmpty(evt.newValue)
					? string.Empty
					: evt.newValue.ToLowerInvariant();
				UpdateProfileList();
			});
			searchRow.Add(searchField);
			toolbarHost.Add(searchRow);

			var filterRow = new VisualElement();
			filterRow.AddToClassList("yucp-filter-row");
			var filterAvatars = new Toggle("Has Avatars") { value = _filterHasAvatarsValue };
			filterAvatars.AddToClassList("yucp-toggle");
			filterAvatars.RegisterValueChangedCallback(evt =>
			{
				_filterHasAvatarsValue = evt.newValue;
				UpdateProfileList();
			});
			filterRow.Add(filterAvatars);

			var filterBuilds = new Toggle("Has Builds") { value = _filterHasBuildsValue };
			filterBuilds.AddToClassList("yucp-toggle");
			filterBuilds.RegisterValueChangedCallback(evt =>
			{
				_filterHasBuildsValue = evt.newValue;
				UpdateProfileList();
			});
			filterRow.Add(filterBuilds);
			toolbarHost.Add(filterRow);

			var buttonRow = new VisualElement();
			buttonRow.AddToClassList("yucp-profile-buttons");

			var newButton = new Button(() => { CreateNewProfile(); CloseOverlay(); }) { text = "+ New" };
			newButton.AddToClassList("yucp-button");
			newButton.AddToClassList("yucp-button-action");
			buttonRow.Add(newButton);

			var cloneButton = new Button(() => { CloneSelectedProfile(); CloseOverlay(); }) { text = "Clone" };
			cloneButton.AddToClassList("yucp-button");
			buttonRow.Add(cloneButton);

			var deleteButton = new Button(() => { DeleteSelectedProfile(); CloseOverlay(); }) { text = "Delete" };
			deleteButton.AddToClassList("yucp-button");
			deleteButton.AddToClassList("yucp-button-danger");
			buttonRow.Add(deleteButton);

			toolbarHost.Add(buttonRow);
		}
	}
}
