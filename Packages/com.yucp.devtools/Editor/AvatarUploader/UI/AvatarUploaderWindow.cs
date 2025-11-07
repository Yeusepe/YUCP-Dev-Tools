using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.DevTools.Editor.AvatarUploader.UI.Components;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	public class AvatarToolsWindow : EditorWindow
	{
		[MenuItem("Tools/YUCP/Avatar Tools")]
		public static void ShowWindow()
		{
			var window = GetWindow<AvatarToolsWindow>();
			var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DevTools.png");
			window.titleContent = new GUIContent("YUCP Avatar Tools", icon);
			window.minSize = new Vector2(400, 500);
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
		private VisualElement _progressFill;
		private Label _progressText;
		private Button _buildButton;
		private ToolbarSearchField _profileSearchField;
		private Toggle _filterHasAvatars;
		private Toggle _filterHasBuilds;
		private Button _testButton;
		private Label _buildInfoLabel;
		private Button _settingsButton;

		// State
		private List<AvatarUploadProfile> _profiles = new List<AvatarUploadProfile>();
		private AvatarUploadProfile _selectedProfile;
		private int _selectedIndex = -1;
		private string _profileSearchFilter = string.Empty;
		private bool _filterHasAvatarsValue;
		private bool _filterHasBuildsValue;
		private ScrollView _avatarGridScroll;
		private VisualElement _avatarGridContent;
		private int _selectedAvatarIndex = -1;
		private VisualElement _heroPanel;
		private AvatarPreviewRenderer _heroPreview;
		private Image _heroImageDisplay;
		private VisualElement _heroOverlay;
		private VisualElement _heroInfoContainer;
		private Button _heroPrevButton;
		private Button _heroAddButton;
		private Button _heroSetIconButton;
		private Button _heroNextButton;
		private Label _heroSlideLabel;
		private readonly List<HeroSlide> _heroSlides = new List<HeroSlide>();
		private int _activeHeroSlideIndex;
		private readonly HashSet<AvatarBuildConfig> _loadingGalleries = new HashSet<AvatarBuildConfig>();
		private AvatarBuildConfig _currentHeroConfig;

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

		private void OnEnable()
		{
			ReloadProfiles();
			LoadResources();
			ControlPanelBridge.Initialize();
		}

		private void LoadResources()
		{
			_logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DevTools.png");
		}

		private void CreateGUI()
		{
			var root = rootVisualElement;
			root.AddToClassList("au-window");
			ShowStartupDisclaimerIfNeeded();

			// Load stylesheet
			var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.yucp.devtools/Editor/AvatarUploader/UI/Styles/AvatarUploader.uss");
			if (styleSheet != null)
			{
				root.styleSheets.Add(styleSheet);
			}

			var exporterStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.yucp.devtools/Editor/PackageExporter/Styles/PackageExporter.uss");
			if (exporterStyleSheet != null)
			{
				root.styleSheets.Add(exporterStyleSheet);
			}

			// Main container
			var mainContainer = new VisualElement();
			mainContainer.AddToClassList("au-main-container");

			// Top Bar
			mainContainer.Add(CreateTopBar());

			// Content Container (Left + Right Panes)
			_contentContainer = new VisualElement();
			_contentContainer.AddToClassList("au-content-container");

			// Create overlay backdrop (for mobile menu)
			_overlayBackdrop = new VisualElement();
			_overlayBackdrop.AddToClassList("au-overlay-backdrop");
			_overlayBackdrop.RegisterCallback<ClickEvent>(evt => CloseOverlay());
			_overlayBackdrop.style.display = DisplayStyle.None;
			_overlayBackdrop.style.visibility = Visibility.Hidden;
			_contentContainer.Add(_overlayBackdrop);

			// Create left pane overlay (for mobile)
			_leftPaneOverlay = CreateLeftPane(isOverlay: true);
			_leftPaneOverlay.AddToClassList("au-left-pane-overlay");
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

			// Initial UI update
			UpdateProfileList();
			UpdateProfileDetails();
			UpdateBottomBar();

			// Register for geometry changes to handle responsive layout
			root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

			// Schedule initial responsive check after layout is ready
			root.schedule.Execute(() =>
			{
				UpdateResponsiveLayout(rootVisualElement.resolvedStyle.width);
			}).StartingIn(100);

			// Register keyboard shortcuts
			root.RegisterCallback<KeyDownEvent>(OnKeyDown);
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
			topBar.AddToClassList("au-top-bar");
			topBar.AddToClassList("pe-top-bar");

			// Mobile toggle button (hamburger menu)
			_mobileToggleButton = new Button(ToggleOverlay);
			_mobileToggleButton.text = "☰";
			_mobileToggleButton.AddToClassList("au-mobile-toggle");
			_mobileToggleButton.AddToClassList("pe-mobile-toggle");
			topBar.Add(_mobileToggleButton);

			// Logo
			if (_logoTexture != null)
			{
				var logo = new Image
				{
					scaleMode = ScaleMode.ScaleToFit,
					image = _logoTexture
				};
				logo.AddToClassList("au-logo");
				logo.AddToClassList("pe-logo");

				float textureAspect = (float)_logoTexture.width / _logoTexture.height;
				float maxHeight = 50f;
				float calculatedWidth = maxHeight * textureAspect;

				logo.style.width = calculatedWidth;
				logo.style.height = maxHeight;

				topBar.Add(logo);
			}

			// Title
			var title = new Label("Avatar Tools");
			title.AddToClassList("au-title");
			title.AddToClassList("pe-title");
			topBar.Add(title);

			_settingsButton = new Button(OpenSettings)
			{
				tooltip = "Open Avatar Tools settings"
			};
			_settingsButton.text = "⚙";
			_settingsButton.AddToClassList("au-settings-button");
			topBar.Add(_settingsButton);

			return topBar;
		}

		private void OpenSettings()
		{
			SettingsService.OpenProjectSettings("Project/YUCP Avatar Tools");
		}

		private VisualElement CreateLeftPane(bool isOverlay)
		{
			var leftPane = new VisualElement();
			leftPane.AddToClassList("au-left-pane");

			var container = new VisualElement();
			container.AddToClassList("au-profile-list-container");

			// Header
			var header = new Label("PROFILES");
			header.AddToClassList("au-section-header");
			container.Add(header);

			// Search field
			var searchRow = new VisualElement();
			searchRow.AddToClassList("au-search-row");
			_profileSearchField = new ToolbarSearchField();
			_profileSearchField.RegisterValueChangedCallback(evt =>
			{
				_profileSearchFilter = string.IsNullOrEmpty(evt.newValue)
					? string.Empty
					: evt.newValue.ToLowerInvariant();
				UpdateProfileList();
			});
			searchRow.Add(_profileSearchField);
			container.Add(searchRow);

			// Filter toggles
			var filterRow = new VisualElement();
			filterRow.AddToClassList("au-filter-row");
			_filterHasAvatars = new Toggle("Has Avatars") { value = false };
			_filterHasAvatars.AddToClassList("au-toggle");
			_filterHasAvatars.RegisterValueChangedCallback(evt =>
			{
				_filterHasAvatarsValue = evt.newValue;
				UpdateProfileList();
			});
			filterRow.Add(_filterHasAvatars);
			_filterHasBuilds = new Toggle("Has Builds") { value = false };
			_filterHasBuilds.AddToClassList("au-toggle");
			_filterHasBuilds.RegisterValueChangedCallback(evt =>
			{
				_filterHasBuildsValue = evt.newValue;
				UpdateProfileList();
			});
			filterRow.Add(_filterHasBuilds);
			container.Add(filterRow);

			// Profile list scrollview
			var scrollView = new ScrollView();
			scrollView.AddToClassList("au-profile-list-scroll");

			// Create and store the appropriate container
			var profileListContainer = new VisualElement();
			if (isOverlay)
			{
				_profileListContainerOverlay = profileListContainer;
			}
			else
			{
				_profileListScrollView = scrollView;
				_profileListContainer = profileListContainer;
			}

			scrollView.Add(profileListContainer);
			container.Add(scrollView);

			// Profile buttons
			var buttonContainer = new VisualElement();
			buttonContainer.AddToClassList("au-profile-buttons");

			var newButton = new Button(() => { CreateNewProfile(); CloseOverlay(); }) { text = "+ New" };
			newButton.AddToClassList("au-button");
			newButton.AddToClassList("au-button-action");
			buttonContainer.Add(newButton);

			var cloneButton = new Button(() => { CloneSelectedProfile(); CloseOverlay(); }) { text = "Clone" };
			cloneButton.AddToClassList("au-button");
			cloneButton.AddToClassList("au-button-action");
			buttonContainer.Add(cloneButton);

			var deleteButton = new Button(() => { DeleteSelectedProfile(); CloseOverlay(); }) { text = "Delete" };
			deleteButton.AddToClassList("au-button");
			deleteButton.AddToClassList("au-button-danger");
			buttonContainer.Add(deleteButton);

			container.Add(buttonContainer);

			// Refresh button
			var refreshButton = new Button(() => { ReloadProfiles(); UpdateProfileList(); UpdateProfileDetails(); UpdateBottomBar(); CloseOverlay(); }) { text = "Refresh" };
			refreshButton.AddToClassList("au-button");
			refreshButton.AddToClassList("au-button-action");
			container.Add(refreshButton);

			leftPane.Add(container);
			return leftPane;
		}

		private VisualElement CreateRightPane()
		{
			var rightPane = new VisualElement();
			rightPane.AddToClassList("au-right-pane");

			_rightPaneScrollView = new ScrollView();
			_rightPaneScrollView.AddToClassList("au-panel");
			_rightPaneScrollView.AddToClassList("au-scrollview");

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
			emptyState.AddToClassList("au-empty-state");

			var title = new Label("No Profile Selected");
			title.AddToClassList("au-empty-state-title");
			emptyState.Add(title);

			var description = new Label("Select a profile from the list or create a new one");
			description.AddToClassList("au-empty-state-description");
			emptyState.Add(description);

			return emptyState;
		}

		private VisualElement CreateBottomBar()
		{
			var bottomBar = new VisualElement();
			bottomBar.AddToClassList("au-bottom-bar");

			var buildContainer = new VisualElement();
			buildContainer.AddToClassList("au-build-buttons");

			var infoSection = new VisualElement();
			infoSection.AddToClassList("au-build-info");
			_buildInfoLabel = new Label("Ready.");
			_buildInfoLabel.AddToClassList("au-label-secondary");
			infoSection.Add(_buildInfoLabel);
			buildContainer.Add(infoSection);

			_testButton = new Button(TestSelectedProfile)
			{
				text = "Test in Control Panel"
			};
			_testButton.AddToClassList("au-button");
			_testButton.AddToClassList("au-button-action");
			_testButton.AddToClassList("au-button-large");
			buildContainer.Add(_testButton);

			_buildButton = new Button(BuildSelectedProfile)
			{
				text = "Build Avatars"
			};
			_buildButton.AddToClassList("au-button");
			_buildButton.AddToClassList("au-button-primary");
			_buildButton.AddToClassList("au-button-large");
			buildContainer.Add(_buildButton);

			bottomBar.Add(buildContainer);

			// Progress container
			_progressContainer = new VisualElement();
			_progressContainer.AddToClassList("au-progress-container");
			_progressContainer.style.display = DisplayStyle.None;

			var progressBar = new VisualElement();
			progressBar.AddToClassList("au-progress-bar");

			_progressFill = new VisualElement();
			_progressFill.AddToClassList("au-progress-fill");
			_progressFill.style.width = Length.Percent(0);
			progressBar.Add(_progressFill);

			_progressText = new Label("0%");
			_progressText.AddToClassList("au-progress-text");
			progressBar.Add(_progressText);

			_progressContainer.Add(progressBar);
			bottomBar.Add(_progressContainer);

			return bottomBar;
		}

		private void UpdateProfileList()
		{
			UpdateProfileListContainer(_profileListContainer);
			UpdateProfileListContainer(_profileListContainerOverlay);
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
				emptyLabel.AddToClassList("au-label-secondary");
				emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
				emptyLabel.style.paddingTop = 20;
				emptyLabel.style.paddingBottom = 10;
				container.Add(emptyLabel);

				var hintLabel = new Label(_profiles.Count == 0 ? "Create one using the button below" : "Try adjusting your search or filters");
				hintLabel.AddToClassList("au-label-small");
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

		private VisualElement CreateProfileItem(AvatarUploadProfile profile, int index)
		{
			var item = new VisualElement();
			item.AddToClassList("au-profile-item");

			bool isSelected = index == _selectedIndex;
			if (isSelected)
			{
				item.AddToClassList("au-profile-item-selected");
			}

			// Profile name with status indicator
			var nameRow = new VisualElement();
			nameRow.style.flexDirection = FlexDirection.Row;
			nameRow.style.alignItems = Align.Center;

			var nameLabel = new Label(GetProfileDisplayName(profile));
			nameLabel.AddToClassList("au-profile-item-name");
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
			infoLabel.AddToClassList("au-profile-item-info");
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
					UpdateBottomBar();
					CloseOverlay();
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
				_emptyState.style.display = DisplayStyle.Flex;
				_profileDetailsContainer.style.display = DisplayStyle.None;
				return;
			}

			_emptyState.style.display = DisplayStyle.None;
			_profileDetailsContainer.style.display = DisplayStyle.Flex;
			_profileDetailsContainer.Clear();

			var profile = _selectedProfile;

			// Profile Settings Section
			var profileSection = CreateProfileSettingsSection(profile);
			_profileDetailsContainer.Add(profileSection);

			// Build Settings Section
			var buildSection = CreateBuildSettingsSection(profile);
			_profileDetailsContainer.Add(buildSection);

			// Avatars Section (grid + detail)
			var avatarsSection = CreateAvatarGridSection(profile);
			_profileDetailsContainer.Add(avatarsSection);
		}

		private VisualElement CreateProfileSettingsSection(AvatarUploadProfile profile)
		{
			var section = new VisualElement();
			section.AddToClassList("au-section");
			section.AddToClassList("pe-section");

			var title = new Label("Profile Settings");
			title.AddToClassList("au-section-title");
			title.AddToClassList("pe-section-title");
			section.Add(title);

			// Profile Name
			var nameRow = CreateFormRow("Profile Name");
			var nameField = new TextField { value = profile.profileName };
			nameField.AddToClassList("au-input");
			nameField.AddToClassList("au-form-field");
			nameField.RegisterValueChangedCallback(evt =>
			{
				if (profile != null)
				{
					Undo.RecordObject(profile, "Change Profile Name");
					profile.profileName = evt.newValue;
					EditorUtility.SetDirty(profile);
					UpdateProfileList();
				}
			});
			nameRow.Add(nameField);
			section.Add(nameRow);

			return section;
		}

		private VisualElement CreateBuildSettingsSection(AvatarUploadProfile profile)
		{
			var section = new VisualElement();
			section.AddToClassList("au-section");
			section.AddToClassList("pe-section");

			var title = new Label("Build Settings");
			title.AddToClassList("au-section-title");
			title.AddToClassList("pe-section-title");
			section.Add(title);

			// Auto Build PC
			var pcRow = CreateFormRow("Auto Build PC");
			var pcToggle = new Toggle { value = profile.autoBuildPC };
			pcToggle.AddToClassList("au-toggle");
			pcToggle.AddToClassList("au-form-field");
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
			questToggle.AddToClassList("au-toggle");
			questToggle.AddToClassList("au-form-field");
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

		private VisualElement CreateAvatarGridSection(AvatarUploadProfile profile)
		{
			var section = new VisualElement();
			section.AddToClassList("au-section");
			section.AddToClassList("au-store-section");
			section.AddToClassList("pe-section");

			var headerRow = new VisualElement();
			headerRow.AddToClassList("au-store-header");
			headerRow.AddToClassList("pe-section-header");
			headerRow.style.flexDirection = FlexDirection.Row;
			headerRow.style.justifyContent = Justify.SpaceBetween;
			headerRow.style.alignItems = Align.Center;
			headerRow.style.marginBottom = 12;

			var title = new Label("Avatars");
			title.AddToClassList("au-section-title");
			title.style.marginBottom = 0;
			headerRow.Add(title);

			var headerActions = new VisualElement();
			headerActions.AddToClassList("au-store-header-actions");

			var addButton = new Button(() => AddAvatarToProfile(profile)) { text = "+ Add Avatar" };
			addButton.AddToClassList("au-button");
			addButton.AddToClassList("au-button-action");
			addButton.AddToClassList("au-button-small");
			headerActions.Add(addButton);

			var refreshButton = new Button(() => BuildAvatarGrid(profile)) { text = "Refresh" };
			refreshButton.AddToClassList("au-button");
			refreshButton.AddToClassList("au-button-small");
			headerActions.Add(refreshButton);

			headerRow.Add(headerActions);
			section.Add(headerRow);

			_heroPanel = new VisualElement();
			_heroPanel.AddToClassList("au-hero-panel");

			var heroContent = new VisualElement();
			heroContent.AddToClassList("au-hero-content");

			var heroPreviewContainer = new VisualElement();
			heroPreviewContainer.AddToClassList("au-hero-preview-container");
			_heroPreview = new AvatarPreviewRenderer();
			_heroPreview.AddToClassList("au-hero-preview");
			heroPreviewContainer.Add(_heroPreview);

			_heroImageDisplay = new Image();
			_heroImageDisplay.AddToClassList("au-hero-gallery-image");
			heroPreviewContainer.Add(_heroImageDisplay);

			_heroOverlay = new VisualElement();
			_heroOverlay.AddToClassList("au-hero-overlay");
			_heroSlideLabel = new Label();
			_heroSlideLabel.AddToClassList("au-hero-slide-label");
			_heroOverlay.Add(_heroSlideLabel);

			_heroPrevButton = new Button(() => CycleHeroSlide(-1)) { text = "‹" };
			_heroPrevButton.AddToClassList("au-hero-slide-button");
			_heroPrevButton.AddToClassList("au-hero-slide-prev");
			_heroOverlay.Add(_heroPrevButton);

			_heroAddButton = new Button(() => AddGalleryImage(profile, _currentHeroConfig)) { text = "+" };
			_heroAddButton.tooltip = "Enable gallery integration and store a VRChat API key in Avatar Tools settings to add gallery images.";
			_heroAddButton.AddToClassList("au-hero-slide-button");
			_heroAddButton.AddToClassList("au-hero-slide-add");
			_heroOverlay.Add(_heroAddButton);

			_heroSetIconButton = new Button(() => SetActiveGalleryImageAsIcon(profile, _currentHeroConfig)) { text = "Set Icon" };
			_heroSetIconButton.tooltip = "Make the selected gallery image the avatar icon via the VRChat API.";
			_heroSetIconButton.AddToClassList("au-hero-slide-button");
			_heroSetIconButton.AddToClassList("au-hero-slide-seticon");
			_heroSetIconButton.style.display = DisplayStyle.None;
			_heroOverlay.Add(_heroSetIconButton);

			_heroNextButton = new Button(() => CycleHeroSlide(1)) { text = "›" };
			_heroNextButton.AddToClassList("au-hero-slide-button");
			_heroNextButton.AddToClassList("au-hero-slide-next");
			_heroOverlay.Add(_heroNextButton);

			heroPreviewContainer.Add(_heroOverlay);
			heroContent.Add(heroPreviewContainer);

			_heroInfoContainer = new VisualElement();
			_heroInfoContainer.AddToClassList("au-hero-info");
			heroContent.Add(_heroInfoContainer);

			_heroPanel.Add(heroContent);
			section.Add(_heroPanel);

			_avatarGridScroll = new ScrollView(ScrollViewMode.Vertical);
			_avatarGridScroll.AddToClassList("au-store-grid-scroll");
			_avatarGridContent = new VisualElement();
			_avatarGridContent.AddToClassList("au-store-grid");
			_avatarGridScroll.Add(_avatarGridContent);

			section.Add(_avatarGridScroll);

			BuildAvatarGrid(profile);

			return section;
		}

		private void BuildAvatarGrid(AvatarUploadProfile profile)
		{
			if (_avatarGridContent == null || _heroPanel == null || _heroPreview == null || _heroInfoContainer == null)
				return;

			_avatarGridContent.Clear();

				if (profile.avatars == null || profile.avatars.Count == 0)
				{
				_heroPanel.style.display = DisplayStyle.None;

				var emptyState = new VisualElement();
				emptyState.AddToClassList("au-avatar-grid-empty");
				var emptyTitle = new Label("No avatars yet");
				emptyTitle.AddToClassList("au-empty-state-title");
				emptyState.Add(emptyTitle);
				var emptyDesc = new Label("Use + Add Avatar to include prefabs in this profile.");
				emptyDesc.AddToClassList("au-empty-state-description");
				emptyState.Add(emptyDesc);
				_avatarGridContent.Add(emptyState);
				return;
			}

			if (_selectedAvatarIndex < 0 || _selectedAvatarIndex >= profile.avatars.Count)
			{
				_selectedAvatarIndex = 0;
			}

			_heroPanel.style.display = DisplayStyle.Flex;
			UpdateHeroPanel(profile, profile.avatars[_selectedAvatarIndex], _selectedAvatarIndex);

					for (int i = 0; i < profile.avatars.Count; i++)
					{
				var avatarConfig = profile.avatars[i];
				EnsureGalleryList(avatarConfig);
				EnsureGalleryLoaded(profile, avatarConfig);

				var tile = CreateAvatarTile(profile, avatarConfig, i);
				if (i == _selectedAvatarIndex)
				{
					tile.AddToClassList("au-avatar-tile-selected");
				}
				_avatarGridContent.Add(tile);
			}
		}

		private VisualElement CreateAvatarTile(AvatarUploadProfile profile, AvatarBuildConfig config, int index)
		{
			var tile = new VisualElement();
			tile.AddToClassList("au-avatar-tile");

			var previewImage = new Image();
			previewImage.AddToClassList("au-avatar-preview");

			var previewTexture = AvatarPreviewCache.RequestPreview(config.avatarPrefab, tex => previewImage.image = tex);
			if (previewTexture != null)
			{
				previewImage.image = previewTexture;
			}

			tile.Add(previewImage);

			var nameLabel = new Label(string.IsNullOrEmpty(config.avatarName) ? $"Avatar {index + 1}" : config.avatarName);
			nameLabel.AddToClassList("au-avatar-tile-title");
			tile.Add(nameLabel);

			var tagsLabel = new Label(config.tags != null && config.tags.Count > 0 ? string.Join(", ", config.tags) : "Add tags for quick filtering");
			tagsLabel.AddToClassList("au-avatar-tile-tags");
			tile.Add(tagsLabel);

			var platformRow = new VisualElement();
			platformRow.AddToClassList("au-avatar-tile-platforms");
			if (config.buildPC)
			{
				var pcBadge = new Label("PC");
				pcBadge.AddToClassList("au-avatar-tile-badge");
				platformRow.Add(pcBadge);
			}
			if (config.buildQuest)
			{
				var questBadge = new Label("Quest");
				questBadge.AddToClassList("au-avatar-tile-badge");
				platformRow.Add(questBadge);
			}
			tile.Add(platformRow);

			tile.RegisterCallback<MouseEnterEvent>(_ =>
			{
				tile.AddToClassList("au-avatar-tile-hover");
			});
			tile.RegisterCallback<MouseLeaveEvent>(_ =>
			{
				tile.RemoveFromClassList("au-avatar-tile-hover");
			});

			tile.RegisterCallback<ClickEvent>(evt =>
			{
				if (_selectedAvatarIndex == index)
					return;

				_selectedAvatarIndex = index;
				BuildAvatarGrid(profile);
				evt.StopPropagation();
			});

			return tile;
		}

		private void UpdateHeroPanel(AvatarUploadProfile profile, AvatarBuildConfig config, int index)
		{
			_currentHeroConfig = config;
			if (config == null)
			{
				_heroPanel.style.display = DisplayStyle.None;
				return;
			}

			EnsureGalleryList(config);
			EnsureGalleryLoaded(profile, config);

			_heroPreview.SetTarget(config.avatarPrefab);
			RebuildHeroSlides(config);

			_heroInfoContainer.Clear();
			_heroInfoContainer.Add(CreateHeroInfoCard(profile, config, index));
		}

		private VisualElement CreateHeroInfoCard(AvatarUploadProfile profile, AvatarBuildConfig config, int index)
		{
			var info = new VisualElement();
			info.AddToClassList("au-hero-info-root");

			var headerRow = new VisualElement();
			headerRow.AddToClassList("au-hero-header");

			var headerLeft = new VisualElement();
			headerLeft.AddToClassList("au-hero-header-left");

			var nameField = new TextField { value = string.IsNullOrEmpty(config.avatarName) ? $"Avatar {index + 1}" : config.avatarName };
			nameField.AddToClassList("au-hero-title-field");
			nameField.RegisterValueChangedCallback(evt =>
			{
				if (config != null)
				{
					Undo.RecordObject(profile, "Change Avatar Name");
					config.avatarName = evt.newValue;
				EditorUtility.SetDirty(profile);
					BuildAvatarGrid(profile);
				}
			});
			headerLeft.Add(nameField);
			headerRow.Add(headerLeft);

			var headerButtons = new VisualElement();
			headerButtons.AddToClassList("au-hero-actions");

			var pingButton = new Button(() =>
			{
				if (config.avatarPrefab != null)
				{
					Selection.activeObject = config.avatarPrefab;
					EditorGUIUtility.PingObject(config.avatarPrefab);
				}
			}) { text = "Ping Prefab" };
			pingButton.AddToClassList("au-button");
			pingButton.AddToClassList("au-button-small");
			headerButtons.Add(pingButton);

			var removeButton = new Button(() => RemoveAvatarFromProfile(profile, index)) { text = "Remove" };
			removeButton.AddToClassList("au-button");
			removeButton.AddToClassList("au-button-danger");
			removeButton.AddToClassList("au-button-small");
			headerButtons.Add(removeButton);

			headerRow.Add(headerButtons);
			info.Add(headerRow);

			var descriptionField = new TextField { value = config.description, multiline = true };
			descriptionField.AddToClassList("au-hero-description");
			descriptionField.RegisterValueChangedCallback(evt =>
			{
				if (config != null)
				{
					Undo.RecordObject(profile, "Change Description");
					config.description = evt.newValue;
					EditorUtility.SetDirty(profile);
				}
			});
			info.Add(descriptionField);

			var statsRow = CreateHeroStats(profile, config);
			info.Add(statsRow);

			var platformRow = new VisualElement();
			platformRow.AddToClassList("au-hero-platform-row");

			var pcToggle = new Toggle("PC") { value = config.buildPC };
			pcToggle.AddToClassList("au-toggle");
			pcToggle.style.marginRight = 6;
			pcToggle.RegisterValueChangedCallback(evt =>
			{
				if (config != null)
				{
					Undo.RecordObject(profile, "Change Build PC");
					config.buildPC = evt.newValue;
					EditorUtility.SetDirty(profile);
					BuildAvatarGrid(profile);
				}
			});
			platformRow.Add(pcToggle);

			var questToggle = new Toggle("Quest") { value = config.buildQuest };
			questToggle.AddToClassList("au-toggle");
			questToggle.RegisterValueChangedCallback(evt =>
			{
				if (config != null)
				{
					Undo.RecordObject(profile, "Change Build Quest");
					config.buildQuest = evt.newValue;
					EditorUtility.SetDirty(profile);
					BuildAvatarGrid(profile);
				}
			});
			platformRow.Add(questToggle);

			info.Add(platformRow);

			var blueprintEditor = BlueprintIdEditor.Create(config, profile, () =>
			{
				EditorUtility.SetDirty(profile);
				BuildAvatarGrid(profile);
			});
			blueprintEditor.AddToClassList("au-hero-section");
			info.Add(blueprintEditor);

			var quickActions = CreateHeroQuickActions(profile, config, index);
			info.Add(quickActions);

			var advancedFoldout = new Foldout { text = "Advanced Settings", value = false };
			advancedFoldout.AddToClassList("au-hero-foldout");
			info.Add(advancedFoldout);

			var prefabRow = CreateFormRow("Avatar Prefab");
			var prefabField = new ObjectField { objectType = typeof(GameObject), value = config.avatarPrefab };
			prefabField.AddToClassList("au-form-field");
			prefabField.RegisterValueChangedCallback(evt =>
			{
				if (config != null)
				{
					Undo.RecordObject(profile, "Change Avatar Prefab");
					config.avatarPrefab = evt.newValue as GameObject;
					EditorUtility.SetDirty(profile);
					BuildAvatarGrid(profile);
				}
			});
			prefabRow.Add(prefabField);
			advancedFoldout.Add(prefabRow);

			var iconRow = CreateFormRow("Icon");
			var iconField = new ObjectField { objectType = typeof(Texture2D), value = config.avatarIcon };
			iconField.AddToClassList("au-form-field");
			iconField.RegisterValueChangedCallback(evt =>
			{
				if (config != null)
				{
					Undo.RecordObject(profile, "Change Icon");
					config.avatarIcon = evt.newValue as Texture2D;
					EditorUtility.SetDirty(profile);
				}
			});
			iconRow.Add(iconField);
			advancedFoldout.Add(iconRow);

			var categoryRow = CreateFormRow("Category");
			var categoryField = new EnumField(config.category);
			categoryField.AddToClassList("au-form-field");
			categoryField.RegisterValueChangedCallback(evt =>
			{
				if (config != null)
				{
					Undo.RecordObject(profile, "Change Category");
					config.category = (AvatarCategory)evt.newValue;
					EditorUtility.SetDirty(profile);
				}
			});
			categoryRow.Add(categoryField);
			advancedFoldout.Add(categoryRow);

			var releaseRow = CreateFormRow("Release Status");
			var releaseField = new EnumField(config.releaseStatus);
			releaseField.AddToClassList("au-form-field");
			releaseField.RegisterValueChangedCallback(evt =>
			{
				if (config != null)
				{
					Undo.RecordObject(profile, "Change Release Status");
					config.releaseStatus = (ReleaseStatus)evt.newValue;
					EditorUtility.SetDirty(profile);
				}
			});
			releaseRow.Add(releaseField);
			advancedFoldout.Add(releaseRow);

			var versionRow = CreateFormRow("Version");
			var versionField = new TextField { value = config.version };
			versionField.AddToClassList("au-input");
			versionField.AddToClassList("au-form-field");
			versionField.RegisterValueChangedCallback(evt =>
			{
				if (config != null)
				{
					Undo.RecordObject(profile, "Change Version");
					config.version = evt.newValue;
					EditorUtility.SetDirty(profile);
				}
			});
			versionRow.Add(versionField);
			advancedFoldout.Add(versionRow);

			var tagsRow = CreateFormRow("Tags (comma-separated)");
			var tagsField = new TextField { value = string.Join(", ", config.tags ?? new List<string>()) };
			tagsField.AddToClassList("au-input");
			tagsField.AddToClassList("au-form-field");
			tagsField.RegisterValueChangedCallback(evt =>
			{
				if (config != null)
				{
					Undo.RecordObject(profile, "Change Tags");
					config.tags = SplitTags(evt.newValue);
					EditorUtility.SetDirty(profile);
					BuildAvatarGrid(profile);
				}
			});
			tagsRow.Add(tagsField);
			advancedFoldout.Add(tagsRow);

			return info;
		}

		private VisualElement CreateHeroStats(AvatarUploadProfile profile, AvatarBuildConfig config)
		{
			var row = new VisualElement();
			row.AddToClassList("au-hero-stat-row");

			row.Add(CreateHeroStatCard("Avatars", (profile.avatars?.Count ?? 0).ToString()));
			row.Add(CreateHeroStatCard("Last Build", string.IsNullOrEmpty(profile.LastBuildTime) ? "Never" : profile.LastBuildTime));
			row.Add(CreateHeroStatCard("Blueprint", string.IsNullOrEmpty(config.blueprintIdPC) && string.IsNullOrEmpty(config.blueprintIdQuest) ? "Unset" : "Assigned"));

			return row;
		}

		private VisualElement CreateHeroStatCard(string label, string value)
		{
			var card = new VisualElement();
			card.AddToClassList("au-hero-stat-card");

			var labelEl = new Label(label);
			labelEl.AddToClassList("au-hero-stat-label");
			card.Add(labelEl);

			var valueEl = new Label(value);
			valueEl.AddToClassList("au-hero-stat-value");
			card.Add(valueEl);

			return card;
		}

		private VisualElement CreateHeroQuickActions(AvatarUploadProfile profile, AvatarBuildConfig config, int index)
		{
			var container = new VisualElement();
			container.AddToClassList("au-hero-quick-actions");

			var buildButton = new Button(() => BuildSingleAvatar(profile, config, PlatformSwitcher.BuildPlatform.PC))
			{
				text = "Build PC"
			};
			buildButton.AddToClassList("au-button");
			buildButton.AddToClassList("au-button-build");
			buildButton.AddToClassList("au-button-small");
			buildButton.SetEnabled(config.avatarPrefab != null);
			container.Add(buildButton);

			var questButton = new Button(() => BuildSingleAvatar(profile, config, PlatformSwitcher.BuildPlatform.Quest))
			{
				text = "Build Quest"
			};
			questButton.AddToClassList("au-button");
			questButton.AddToClassList("au-button-build");
			questButton.AddToClassList("au-button-small");
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
			openButton.AddToClassList("au-button");
			openButton.AddToClassList("au-button-action");
			openButton.AddToClassList("au-button-small");
			container.Add(openButton);

			return container;
		}

		private void BuildSingleAvatar(AvatarUploadProfile profile, AvatarBuildConfig config, PlatformSwitcher.BuildPlatform platform)
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

			_isBuilding = true;
			_progress = 0f;
			_status = $"Building {platform}: {config.avatarName}";
			UpdateProgress(_progress, _status);
			_buildInfoLabel.text = "Building avatars...";

			try
			{
				PlatformSwitcher.EnsurePlatform(platform);
				AvatarBuilder.BuildAvatar(profile, config, platform, s => UpdateProgress(_progress, s));
				_progress = 1f;
				UpdateProgress(_progress, "Build complete");
				BuildAvatarGrid(profile);
			}
			finally
			{
				_isBuilding = false;
				_status = string.Empty;
				UpdateProgress(0f, _status);
				_buildInfoLabel.text = "Ready.";
			}
		}

		private VisualElement CreateFormRow(string labelText, string tooltip = null)
		{
			var row = new VisualElement();
			row.AddToClassList("au-form-row");

			var label = new Label(labelText);
			label.AddToClassList("au-form-label");
			if (!string.IsNullOrEmpty(tooltip))
			{
				label.tooltip = tooltip;
			}
			row.Add(label);

			return row;
		}

		private void UpdateBottomBar()
		{
			bool canStart = !_isBuilding && _selectedProfile != null;
			_buildButton?.SetEnabled(canStart);
			_testButton?.SetEnabled(canStart);

			if (_buildInfoLabel != null)
			{
				if (_selectedProfile == null)
				{
					_buildInfoLabel.text = "Select a profile to begin.";
				}
				else
				{
					int avatarCount = _selectedProfile.avatars?.Count ?? 0;
					_buildInfoLabel.text = avatarCount == 0 ? "Profile has no avatars." : $"Profile will process {avatarCount} avatar(s).";
				}
			}
		}

		private void UpdateProgress(float progress, string status)
		{
			_progress = progress;
			_status = status;

			if (_progressContainer != null)
			{
				_progressContainer.style.display = _isBuilding ? DisplayStyle.Flex : DisplayStyle.None;
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
			string[] guids = AssetDatabase.FindAssets("t:AvatarUploadProfile");
			foreach (var guid in guids)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var p = AssetDatabase.LoadAssetAtPath<AvatarUploadProfile>(path);
				if (p != null) _profiles.Add(p);
			}
			_profiles.Sort((a, b) => string.Compare(a.profileName, b.profileName, StringComparison.OrdinalIgnoreCase));
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
			var dir = "Assets/YUCP/AvatarUploadProfiles";
			if (!AssetDatabase.IsValidFolder("Assets/YUCP"))
			{
				AssetDatabase.CreateFolder("Assets", "YUCP");
			}
			if (!AssetDatabase.IsValidFolder(dir))
			{
				AssetDatabase.CreateFolder("Assets/YUCP", "AvatarUploadProfiles");
			}
			var profile = ScriptableObject.CreateInstance<AvatarUploadProfile>();
			profile.profileName = "New Avatar Profile";
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
			if (EditorUtility.DisplayDialog("Delete Profile", $"Delete profile '{profile.profileName}'? This cannot be undone.", "Delete", "Cancel"))
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

		private void AddAvatarToProfile(AvatarUploadProfile profile)
		{
			if (profile.avatars == null)
			{
				profile.avatars = new List<AvatarBuildConfig>();
			}
			Undo.RecordObject(profile, "Add Avatar");
			var settings = AvatarUploaderSettings.Instance;
			profile.avatars.Add(new AvatarBuildConfig
			{
				buildPC = settings.DefaultBuildPC,
				buildQuest = settings.DefaultBuildQuest
			});
			EditorUtility.SetDirty(profile);
			UpdateProfileDetails();
		}

		private void RemoveAvatarFromProfile(AvatarUploadProfile profile, int index)
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

				BuildAvatarGrid(profile);
			}
		}

		private void BuildSelectedProfile()
		{
			if (_selectedProfile == null) return;
			try
			{
				EnsureUploadDisclaimerAcknowledged();
			}
			catch (OperationCanceledException)
			{
				return;
			}
			var profile = _selectedProfile;
			var configs = profile.avatars?.ToList() ?? new List<AvatarBuildConfig>();
			if (configs.Count == 0)
			{
				EditorUtility.DisplayDialog("No Avatars", "This profile has no avatars to build.", "OK");
				return;
			}

			_isBuilding = true;
			_progress = 0f;
			_status = "Preparing...";
			UpdateProgress(_progress, _status);
			_buildInfoLabel.text = "Building avatars...";
			UpdateBottomBar();

			try
			{
				var controlPanelReady = ControlPanelBridge.Initialize();
				var toBuildPC = configs.Where(c => c.buildPC && (profile.autoBuildPC || c.buildPC)).ToList();
				var toBuildQuest = configs.Where(c => c.buildQuest && (profile.autoBuildQuest || c.buildQuest)).ToList();

				int total = toBuildPC.Count + toBuildQuest.Count;
				int built = 0;

				if (toBuildPC.Count > 0)
				{
					_status = "Switching to PC...";
					UpdateProgress(_progress, _status);
					PlatformSwitcher.EnsurePlatform(PlatformSwitcher.BuildPlatform.PC);
					foreach (var cfg in toBuildPC)
					{
						_status = $"Dispatching PC build: {cfg.avatarName}";
						UpdateProgress(_progress, _status);

						bool handledByControlPanel = controlPanelReady &&
							ControlPanelBridge.TryUploadAvatar(profile, cfg, PlatformSwitcher.BuildPlatform.PC);

						if (handledByControlPanel)
						{
							_status = $"Control Panel handling PC upload for '{cfg.avatarName}'";
							UpdateProgress(0f, _status);
							EditorApplication.delayCall += () => UpdateProgress(0f, string.Empty);
							BuildAvatarGrid(profile);
							continue;
						}

						var r = AvatarBuilder.BuildAvatar(profile, cfg, PlatformSwitcher.BuildPlatform.PC,
							s => { _status = s; UpdateProgress(_progress, _status); });
						built++;
						_progress = Mathf.Clamp01(built / (float)total);
						UpdateProgress(_progress, _status);
					}
				}

				if (toBuildQuest.Count > 0)
				{
					_status = "Switching to Quest...";
					UpdateProgress(_progress, _status);
					PlatformSwitcher.EnsurePlatform(PlatformSwitcher.BuildPlatform.Quest);
					foreach (var cfg in toBuildQuest)
					{
						_status = $"Dispatching Quest build: {cfg.avatarName}";
						UpdateProgress(_progress, _status);

						bool handledByControlPanel = controlPanelReady &&
							ControlPanelBridge.TryUploadAvatar(profile, cfg, PlatformSwitcher.BuildPlatform.Quest);

						if (handledByControlPanel)
						{
							_status = $"Control Panel handling Quest upload for '{cfg.avatarName}'";
							UpdateProgress(0f, _status);
							EditorApplication.delayCall += () => UpdateProgress(0f, string.Empty);
							BuildAvatarGrid(profile);
							continue;
						}

						var r = AvatarBuilder.BuildAvatar(profile, cfg, PlatformSwitcher.BuildPlatform.Quest,
							s => { _status = s; UpdateProgress(_progress, _status); });
						built++;
						_progress = Mathf.Clamp01(built / (float)total);
						UpdateProgress(_progress, _status);
					}
				}

				profile.RecordBuild();
				EditorUtility.SetDirty(profile);
				AssetDatabase.SaveAssets();
				EditorUtility.DisplayDialog("Build Complete", "Finished building avatars for the selected profile.", "OK");
			}
			finally
			{
				_isBuilding = false;
				_status = string.Empty;
				_progress = 0f;
				UpdateProgress(_progress, _status);
				UpdateBottomBar();
				_buildInfoLabel.text = "Ready.";
			}
		}

		private void TestSelectedProfile()
		{
			if (_selectedProfile == null)
				return;

			try
			{
				EnsureUploadDisclaimerAcknowledged();
			}
			catch (OperationCanceledException)
			{
				return;
			}

			var profile = _selectedProfile;
			var configs = profile.avatars?.ToList() ?? new List<AvatarBuildConfig>();
			if (configs.Count == 0)
			{
				EditorUtility.DisplayDialog("Avatar Tools", "This profile has no avatars to test.", "OK");
				return;
			}

			if (!ControlPanelBridge.Initialize())
			{
				EditorUtility.DisplayDialog("Avatar Tools", "Unable to locate the VRChat Control Panel. Open the Control Panel once before using Test.", "OK");
				return;
			}

			int scheduled = 0;
			foreach (var cfg in configs)
			{
				if (cfg == null || cfg.avatarPrefab == null)
					continue;

				if (cfg.buildPC && ControlPanelBridge.TryUploadAvatar(profile, cfg, PlatformSwitcher.BuildPlatform.PC))
					scheduled++;

				if (cfg.buildQuest && ControlPanelBridge.TryUploadAvatar(profile, cfg, PlatformSwitcher.BuildPlatform.Quest))
					scheduled++;
			}

			_buildInfoLabel.text = scheduled == 0 ? "No avatars were sent to the Control Panel." : $"Dispatching uploads to Control Panel...";
		}

		private static List<string> SplitTags(string tags)
		{
			if (string.IsNullOrWhiteSpace(tags)) return new List<string>();
			return tags
				.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(t => t.Trim())
				.Where(t => !string.IsNullOrEmpty(t))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private void ShowProfileContextMenu(AvatarUploadProfile profile, int index, MouseDownEvent evt)
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

		private string GetProfileDisplayName(AvatarUploadProfile profile)
		{
			return string.IsNullOrEmpty(profile.profileName) ? profile.name : profile.profileName;
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

			if (_overlayBackdrop != null)
			{
				_overlayBackdrop.style.display = DisplayStyle.Flex;
				_overlayBackdrop.style.visibility = Visibility.Visible;
				_overlayBackdrop.BringToFront();
			}

			if (_leftPaneOverlay != null)
			{
				_leftPaneOverlay.style.display = DisplayStyle.Flex;
				_leftPaneOverlay.style.visibility = Visibility.Visible;
				_leftPaneOverlay.BringToFront();
			}
		}

		private void CloseOverlay()
		{
			_isOverlayOpen = false;

			if (_overlayBackdrop != null)
			{
				_overlayBackdrop.style.display = DisplayStyle.None;
				_overlayBackdrop.style.visibility = Visibility.Hidden;
			}

			if (_leftPaneOverlay != null)
			{
				_leftPaneOverlay.style.display = DisplayStyle.None;
				_leftPaneOverlay.style.visibility = Visibility.Hidden;
			}
		}

		private void OnGeometryChanged(GeometryChangedEvent evt)
		{
			float newWidth = evt.newRect.width;
			UpdateResponsiveLayout(newWidth);
		}

		private void UpdateResponsiveLayout(float width)
		{
			var root = rootVisualElement;

			root.RemoveFromClassList("au-window-narrow");
			root.RemoveFromClassList("au-window-medium");
			root.RemoveFromClassList("au-window-wide");

			if (width < 700f)
			{
				root.AddToClassList("au-window-narrow");
			}
			else if (width < 1000f)
			{
				root.AddToClassList("au-window-medium");
			}
			else
			{
				root.AddToClassList("au-window-wide");
			}

			if (width >= 700f && _isOverlayOpen)
			{
				CloseOverlay();
			}
		}

		private enum HeroSlideType
		{
			Preview3D,
			GalleryImage
		}

		private class HeroSlide
		{
			public HeroSlideType Type;
			public AvatarGalleryImage Gallery;
			public Texture2D Texture;
		}

		private void EnsureGalleryList(AvatarBuildConfig config)
		{
			if (config.galleryImages == null)
			{
				config.galleryImages = new List<AvatarGalleryImage>();
			}
		}

		private void EnsureGalleryLoaded(AvatarUploadProfile profile, AvatarBuildConfig config)
		{
			if (config == null)
				return;

			EnsureGalleryList(config);

			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration || !settings.HasStoredApiKey)
				return;

			if (_loadingGalleries.Contains(config))
				return;

			if (config.galleryImages.Count > 0)
				return;

			var avatarId = GetPreferredBlueprintId(config);
			if (string.IsNullOrEmpty(avatarId))
				return;

			LoadGalleryAsync(profile, config, avatarId);
		}

		private async void LoadGalleryAsync(AvatarUploadProfile profile, AvatarBuildConfig config, string avatarId)
		{
			_loadingGalleries.Add(config);
			try
			{
				var entries = await AvatarGalleryClient.GetGalleryAsync(avatarId);
				EnsureGalleryList(config);
				config.galleryImages.Clear();
				config.galleryImages.AddRange(entries);

				for (int i = 0; i < config.galleryImages.Count; i++)
				{
					var entry = config.galleryImages[i];
					entry.thumbnail = await AvatarGalleryClient.DownloadImageAsync(entry.url);
				}

				BuildAvatarGrid(profile);
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Unable to load avatar gallery: {ex.Message}");
			}
			finally
			{
				_loadingGalleries.Remove(config);
			}
		}

		private void RebuildHeroSlides(AvatarBuildConfig config)
		{
			_heroSlides.Clear();
			_heroSlides.Add(new HeroSlide { Type = HeroSlideType.Preview3D });

			if (config?.galleryImages != null)
			{
				foreach (var img in config.galleryImages)
				{
					if (img?.thumbnail == null)
						continue;
					_heroSlides.Add(new HeroSlide
					{
						Type = HeroSlideType.GalleryImage,
						Gallery = img,
						Texture = img.thumbnail
					});
				}
			}

			_activeHeroSlideIndex = Mathf.Clamp(_currentHeroConfig?.activeGalleryIndex ?? 0, 0, _heroSlides.Count - 1);
			ShowHeroSlide();
		}

		private void ShowHeroSlide()
		{
			if (_heroSlides.Count == 0)
			{
				_heroPreview.SetTarget(_currentHeroConfig?.avatarPrefab);
				_heroPreview.style.display = DisplayStyle.Flex;
				_heroImageDisplay.style.display = DisplayStyle.None;
				_heroSlideLabel.text = "3D Preview";
				UpdateHeroOverlayState();
				return;
			}

			_activeHeroSlideIndex = Mathf.Clamp(_activeHeroSlideIndex, 0, _heroSlides.Count - 1);
			if (_currentHeroConfig != null)
			{
				_currentHeroConfig.activeGalleryIndex = _activeHeroSlideIndex;
			}

			var slide = _heroSlides[_activeHeroSlideIndex];
			switch (slide.Type)
			{
				case HeroSlideType.Preview3D:
					_heroPreview.style.display = DisplayStyle.Flex;
					_heroPreview.SetTarget(_currentHeroConfig?.avatarPrefab);
					_heroImageDisplay.style.display = DisplayStyle.None;
					_heroSlideLabel.text = "3D Preview";
					break;
				case HeroSlideType.GalleryImage:
					_heroPreview.style.display = DisplayStyle.None;
					_heroImageDisplay.style.display = DisplayStyle.Flex;
					_heroImageDisplay.image = slide.Texture;
					int galleryIndex = _activeHeroSlideIndex;
					if (_heroSlides.Count > 0 && _heroSlides[0].Type == HeroSlideType.Preview3D)
					{
						galleryIndex -= 1;
					}
					_heroSlideLabel.text = $"Gallery Image {Mathf.Max(1, galleryIndex + 1)}";
					break;
			}

			UpdateHeroOverlayState();
		}

		private void UpdateHeroOverlayState()
		{
			bool hasMultiple = _heroSlides.Count > 1;
			_heroPrevButton?.SetEnabled(hasMultiple);
			_heroNextButton?.SetEnabled(hasMultiple);

			if (_heroSetIconButton != null)
			{
				var showSetIcon = _heroSlides.Count > 0 && _heroSlides[_activeHeroSlideIndex].Type == HeroSlideType.GalleryImage;
				_heroSetIconButton.style.display = showSetIcon ? DisplayStyle.Flex : DisplayStyle.None;
				_heroSetIconButton.SetEnabled(showSetIcon);
			}
		}

		private void CycleHeroSlide(int direction)
		{
			if (_heroSlides.Count == 0)
				return;

			_activeHeroSlideIndex = (_activeHeroSlideIndex + direction + _heroSlides.Count) % _heroSlides.Count;
			ShowHeroSlide();
		}

		private string GetPreferredBlueprintId(AvatarBuildConfig config)
		{
			if (!string.IsNullOrEmpty(config?.blueprintIdPC))
				return config.blueprintIdPC;
			if (!string.IsNullOrEmpty(config?.blueprintIdQuest))
				return config.blueprintIdQuest;
			return null;
		}

		private async void AddGalleryImage(AvatarUploadProfile profile, AvatarBuildConfig config)
		{
			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration || !settings.HasStoredApiKey)
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
					var entries = await AvatarGalleryClient.GetGalleryAsync(avatarId);
					EnsureGalleryList(config);
					config.galleryImages.Clear();
					config.galleryImages.AddRange(entries);
					for (int i = 0; i < config.galleryImages.Count; i++)
					{
						config.galleryImages[i].thumbnail = await AvatarGalleryClient.DownloadImageAsync(config.galleryImages[i].url);
					}
					BuildAvatarGrid(profile);
					EditorUtility.DisplayDialog("Gallery", "Upload complete.", "OK");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AvatarUploader] Gallery upload failed: {ex.Message}");
				EditorUtility.DisplayDialog("Gallery", "Upload failed. Check console for details.", "OK");
			}
			finally
			{
				_status = string.Empty;
				UpdateProgress(0f, _status);
			}
		}

		private async void SetActiveGalleryImageAsIcon(AvatarUploadProfile profile, AvatarBuildConfig config)
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

			try
			{
				var updated = await AvatarGalleryClient.SetAvatarIconAsync(avatarId, slide.Gallery.fileId);
				if (updated)
				{
					Undo.RecordObject(profile, "Set Avatar Icon");
					config.avatarIcon = slide.Texture;
					EditorUtility.SetDirty(profile);
					BuildAvatarGrid(profile);
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
	}
}
