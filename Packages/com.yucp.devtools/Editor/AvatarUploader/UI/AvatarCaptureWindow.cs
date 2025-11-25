using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.DevTools.Editor.AvatarUploader.Core;
using YUCP.DevTools.Editor.AvatarUploader.UI.Components;

namespace YUCP.DevTools.Editor.AvatarUploader.UI
{
	/// <summary>
	/// Dedicated window for capturing high-quality avatar thumbnails with full control over camera, lighting, and backgrounds.
	/// </summary>
	public class AvatarCaptureWindow : EditorWindow
	{
		private const string WindowUxmlPath = "Packages/com.yucp.devtools/Editor/AvatarUploader/UI/AvatarCaptureWindow.uxml";
		private const string WindowUssPath = "Packages/com.yucp.devtools/Editor/AvatarUploader/UI/Styles/AvatarCaptureWindow.uss";

		private AvatarAsset _targetAvatar;
		private AvatarCollection _targetProfile;
		private Action<Texture2D> _onCaptureComplete;

		// UI Elements
		private VisualElement _previewContainer;
		private VisualElement _controlsPanel;
		private IMGUIContainer _previewRenderer;

		// Capture systems
		private AvatarCaptureCamera _camera;
		private AvatarCaptureLighting _lighting;
		private AvatarCaptureBackground _background;
		private AvatarCapturePostProcess _postProcess;
		private CapturePreviewRenderer _previewComponent;

		// Settings
		private CaptureMode _currentMode = CaptureMode.Headshot;
		private CaptureResolution _currentResolution = CaptureResolution.VRChatStandard;
		private CapturePreset _currentPreset;

		/// <summary>
		/// Open the capture window for a specific avatar.
		/// </summary>
		public static void OpenForAvatar(AvatarAsset avatar, AvatarCollection profile, Action<Texture2D> onComplete = null)
		{
			var window = GetWindow<AvatarCaptureWindow>();
			window.titleContent = new GUIContent("Avatar Capture");
			window.minSize = new Vector2(1000, 700);
			window._targetAvatar = avatar;
			window._targetProfile = profile;
			window._onCaptureComplete = onComplete;
			window.Initialize();
			
			// Delay population to ensure window is fully initialized
			EditorApplication.delayCall += () =>
			{
				window.PopulateFromPipelineManager();
			};
			
			window.Show();
		}

		private void Initialize()
		{
			if (rootVisualElement == null)
				return;

			// Load UXML
			var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
			if (visualTree == null)
			{
				// Fallback: create basic layout programmatically
				CreateBasicLayout();
			}
			else
			{
				try
				{
					visualTree.CloneTree(rootVisualElement);
				}
				catch (System.Exception ex)
				{
					Debug.LogError($"[AvatarCaptureWindow] Failed to load UXML: {ex.Message}");
					CreateBasicLayout();
				}
			}

			// Load styles
			var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(WindowUssPath);
			if (styleSheet != null)
			{
				try
				{
					rootVisualElement.styleSheets.Add(styleSheet);
				}
				catch (System.Exception ex)
				{
					Debug.LogWarning($"[AvatarCaptureWindow] Failed to load stylesheet: {ex.Message}");
				}
			}

			// Initialize capture systems
			_camera = new AvatarCaptureCamera();
			_lighting = new AvatarCaptureLighting();
			_background = new AvatarCaptureBackground();
			_postProcess = new AvatarCapturePostProcess();

			// Bind UI elements
			BindUIElements();

			// Setup preview
			SetupPreview();

			// Load default preset
			LoadDefaultPreset();
		}

		private void CreateBasicLayout()
		{
			rootVisualElement.Clear();
			rootVisualElement.AddToClassList("ac-window");

			// Main container with two panels
			var mainContainer = new VisualElement();
			mainContainer.style.flexDirection = FlexDirection.Row;
			mainContainer.style.flexGrow = 1f;
			rootVisualElement.Add(mainContainer);

			// Preview panel (left, 60%)
			_previewContainer = new VisualElement();
			_previewContainer.style.width = new StyleLength(new Length(60f, LengthUnit.Percent));
			_previewContainer.style.flexGrow = 0f;
			_previewContainer.AddToClassList("ac-preview-container");
			mainContainer.Add(_previewContainer);

			// Controls panel (right, 40%)
			_controlsPanel = new VisualElement();
			_controlsPanel.style.width = new StyleLength(new Length(40f, LengthUnit.Percent));
			_controlsPanel.style.flexGrow = 0f;
			_controlsPanel.AddToClassList("ac-controls-panel");
			mainContainer.Add(_controlsPanel);

			// Action buttons at bottom
			var actionBar = new VisualElement();
			actionBar.style.flexDirection = FlexDirection.Row;
			actionBar.style.justifyContent = Justify.FlexEnd;
			actionBar.style.paddingTop = 8;
			actionBar.style.paddingBottom = 8;
			actionBar.style.paddingLeft = 8;
			actionBar.style.paddingRight = 8;
			rootVisualElement.Add(actionBar);

			var captureButton = new Button(OnCapture) { text = "Capture" };
			captureButton.AddToClassList("ac-button");
			captureButton.AddToClassList("ac-button-primary");
			actionBar.Add(captureButton);

			var cancelButton = new Button(() => Close()) { text = "Cancel" };
			cancelButton.AddToClassList("ac-button");
			actionBar.Add(cancelButton);
		}

		private void BindUIElements()
		{
			if (rootVisualElement == null)
				return;

			if (_previewContainer == null)
			{
				_previewContainer = rootVisualElement.Q<VisualElement>("preview-container");
				if (_previewContainer == null && rootVisualElement.childCount > 0)
				{
					// Try to find it in the main container
					var mainContainer = rootVisualElement.Q<VisualElement>("main-container");
					if (mainContainer != null)
						_previewContainer = mainContainer.Q<VisualElement>("preview-container");
				}
			}
			
			if (_controlsPanel == null)
			{
				_controlsPanel = rootVisualElement.Q<VisualElement>("controls-panel");
				if (_controlsPanel == null && rootVisualElement.childCount > 0)
				{
					// Try to find it in the main container
					var mainContainer = rootVisualElement.Q<VisualElement>("main-container");
					if (mainContainer != null)
						_controlsPanel = mainContainer.Q<VisualElement>("controls-panel");
				}
			}

			// Build controls UI
			BuildControlsUI();
		}

		private void BuildControlsUI()
		{
			if (_controlsPanel == null)
				return;

			_controlsPanel.Clear();

			// Create tabs or sections for different control groups
			var scrollView = new ScrollView();
			_controlsPanel.Add(scrollView);

			// Camera section
			BuildCameraSection(scrollView);

			// Lighting section
			BuildLightingSection(scrollView);

			// Background section
			BuildBackgroundSection(scrollView);

			// Post-processing section
			BuildPostProcessSection(scrollView);

			// Resolution section
			BuildResolutionSection(scrollView);

			// Presets section
			BuildPresetsSection(scrollView);
		}

		private void BuildCameraSection(VisualElement parent)
		{
			var section = new Foldout { text = "Camera", value = true };
			section.AddToClassList("ac-section");

			// Capture mode
			var modeField = new EnumField("Mode", _currentMode);
			modeField.RegisterValueChangedCallback(evt =>
			{
				_currentMode = (CaptureMode)evt.newValue;
				_camera?.SetMode(_currentMode);
				UpdatePreview();
			});
			section.Add(modeField);

			// Position controls
			var positionLabel = new Label("Position");
			positionLabel.AddToClassList("ac-label");
			section.Add(positionLabel);

			var positionX = new FloatField("X");
			positionX.value = 0f;
			positionX.RegisterValueChangedCallback(evt => { _camera?.SetPositionX(evt.newValue); UpdatePreview(); });
			section.Add(positionX);

			var positionY = new FloatField("Y");
			positionY.value = 0f;
			positionY.RegisterValueChangedCallback(evt => { _camera?.SetPositionY(evt.newValue); UpdatePreview(); });
			section.Add(positionY);

			var positionZ = new FloatField("Z");
			positionZ.value = 1.5f;
			positionZ.RegisterValueChangedCallback(evt => { _camera?.SetPositionZ(evt.newValue); UpdatePreview(); });
			section.Add(positionZ);

			// Rotation controls
			var rotationLabel = new Label("Rotation");
			rotationLabel.AddToClassList("ac-label");
			section.Add(rotationLabel);

			var rotationY = new FloatField("Yaw");
			rotationY.value = 0f;
			rotationY.RegisterValueChangedCallback(evt => { _camera?.SetRotationY(evt.newValue); UpdatePreview(); });
			section.Add(rotationY);

			var rotationX = new FloatField("Pitch");
			rotationX.value = 0f;
			rotationX.RegisterValueChangedCallback(evt => { _camera?.SetRotationX(evt.newValue); UpdatePreview(); });
			section.Add(rotationX);

			// FOV
			var fovField = new FloatField("Field of View");
			fovField.value = 35f;
			fovField.RegisterValueChangedCallback(evt => { _camera?.SetFOV(evt.newValue); UpdatePreview(); });
			section.Add(fovField);

			// Distance
			var distanceField = new FloatField("Distance");
			distanceField.value = 1.5f;
			distanceField.RegisterValueChangedCallback(evt => { _camera?.SetDistance(evt.newValue); UpdatePreview(); });
			section.Add(distanceField);

			parent.Add(section);
		}

		private void BuildLightingSection(VisualElement parent)
		{
			var section = new Foldout { text = "Lighting", value = false };
			section.AddToClassList("ac-section");

			// Main light
			var mainLightLabel = new Label("Main Light");
			mainLightLabel.AddToClassList("ac-label");
			section.Add(mainLightLabel);

			var mainIntensity = new FloatField("Intensity");
			mainIntensity.value = 1.1f;
			mainIntensity.RegisterValueChangedCallback(evt => { _lighting?.SetMainIntensity(evt.newValue); UpdatePreview(); });
			section.Add(mainIntensity);

			// Fill light
			var fillLightLabel = new Label("Fill Light");
			fillLightLabel.AddToClassList("ac-label");
			section.Add(fillLightLabel);

			var fillIntensity = new FloatField("Intensity");
			fillIntensity.value = 0.75f;
			fillIntensity.RegisterValueChangedCallback(evt => { _lighting?.SetFillIntensity(evt.newValue); UpdatePreview(); });
			section.Add(fillIntensity);

			// Ambient
			var ambientLabel = new Label("Ambient");
			ambientLabel.AddToClassList("ac-label");
			section.Add(ambientLabel);

			var ambientIntensity = new FloatField("Intensity");
			ambientIntensity.value = 0.25f;
			ambientIntensity.RegisterValueChangedCallback(evt => { _lighting?.SetAmbientIntensity(evt.newValue); UpdatePreview(); });
			section.Add(ambientIntensity);

			parent.Add(section);
		}

		private void BuildBackgroundSection(VisualElement parent)
		{
			var section = new Foldout { text = "Background", value = false };
			section.AddToClassList("ac-section");

			// Background type
			var bgTypeField = new EnumField("Type", BackgroundType.Transparent);
			bgTypeField.RegisterValueChangedCallback(evt =>
			{
				_background?.SetType((BackgroundType)evt.newValue);
				UpdatePreview();
			});
			section.Add(bgTypeField);

			// Color picker (for solid/gradient)
			var colorField = new ColorField("Color");
			colorField.value = Color.clear;
			colorField.RegisterValueChangedCallback(evt => { _background?.SetColor(evt.newValue); UpdatePreview(); });
			section.Add(colorField);

			parent.Add(section);
		}

		private void BuildPostProcessSection(VisualElement parent)
		{
			var section = new Foldout { text = "Post-Processing", value = false };
			section.AddToClassList("ac-section");

			// Brightness
			var brightnessField = new FloatField("Brightness");
			brightnessField.value = 0f;
			brightnessField.RegisterValueChangedCallback(evt => { _postProcess?.SetBrightness(evt.newValue); });
			section.Add(brightnessField);

			// Contrast
			var contrastField = new FloatField("Contrast");
			contrastField.value = 0f;
			contrastField.RegisterValueChangedCallback(evt => { _postProcess?.SetContrast(evt.newValue); });
			section.Add(contrastField);

			// Saturation
			var saturationField = new FloatField("Saturation");
			saturationField.value = 0f;
			saturationField.RegisterValueChangedCallback(evt => { _postProcess?.SetSaturation(evt.newValue); });
			section.Add(saturationField);

			parent.Add(section);
		}

		private void BuildResolutionSection(VisualElement parent)
		{
			var section = new Foldout { text = "Resolution", value = false };
			section.AddToClassList("ac-section");

			// Resolution preset
			var resolutionField = new EnumField("Preset", _currentResolution);
			resolutionField.RegisterValueChangedCallback(evt =>
			{
				_currentResolution = (CaptureResolution)evt.newValue;
				UpdatePreview();
			});
			section.Add(resolutionField);

			// Custom resolution fields (shown when Custom is selected)
			var widthField = new IntegerField("Width");
			widthField.value = 1200;
			section.Add(widthField);

			var heightField = new IntegerField("Height");
			heightField.value = 900;
			section.Add(heightField);

			parent.Add(section);
		}

		private void BuildPresetsSection(VisualElement parent)
		{
			var section = new Foldout { text = "Presets", value = false };
			section.AddToClassList("ac-section");

			// Load preset button
			var loadButton = new Button(LoadPreset) { text = "Load Preset" };
			loadButton.AddToClassList("ac-button");
			section.Add(loadButton);

			// Save preset button
			var saveButton = new Button(SavePreset) { text = "Save Preset" };
			saveButton.AddToClassList("ac-button");
			section.Add(saveButton);

			parent.Add(section);
		}

		private void SetupPreview()
		{
			if (_previewContainer == null)
				return;

			_previewContainer.Clear();

			// Create preview renderer component
			_previewComponent = new CapturePreviewRenderer();
			_previewComponent.SetTarget(_targetAvatar?.avatarPrefab);
			_previewComponent.SetCamera(_camera);
			_previewComponent.SetLighting(_lighting);
			_previewComponent.SetBackground(_background);
			_previewContainer.Add(_previewComponent);
		}

		private void UpdatePreview()
		{
			_previewComponent?.MarkDirtyRepaint();
		}

		private void LoadDefaultPreset()
		{
			// Load VRChat Standard preset
			_currentPreset = CapturePreset.GetDefaultPreset(CapturePresetType.VRChatStandard);
			if (_currentPreset != null)
			{
				ApplyPreset(_currentPreset);
			}
		}

		private void LoadPreset()
		{
			// TODO: Implement preset loading dialog
			EditorUtility.DisplayDialog("Load Preset", "Preset loading not yet implemented.", "OK");
		}

		private void SavePreset()
		{
			// TODO: Implement preset saving
			EditorUtility.DisplayDialog("Save Preset", "Preset saving not yet implemented.", "OK");
		}

		private void ApplyPreset(CapturePreset preset)
		{
			if (preset == null)
				return;

			_camera?.ApplyPreset(preset.cameraSettings);
			_lighting?.ApplyPreset(preset.lightingSettings);
			_background?.ApplyPreset(preset.backgroundSettings);
			_postProcess?.ApplyPreset(preset.postProcessSettings);
			_currentResolution = preset.resolution;
			_currentMode = preset.mode;

			UpdatePreview();
		}

		private void OnCapture()
		{
			if (_targetAvatar == null || _targetAvatar.avatarPrefab == null)
			{
				EditorUtility.DisplayDialog("Capture Error", "No avatar selected.", "OK");
				return;
			}

			// Capture the image
			var capturedTexture = CaptureImage();
			if (capturedTexture == null)
			{
				EditorUtility.DisplayDialog("Capture Error", "Failed to capture image.", "OK");
				return;
			}

			// Apply post-processing
			var processedTexture = _postProcess?.Process(capturedTexture) ?? capturedTexture;

			// Save to avatar config
			if (_targetAvatar != null && _targetProfile != null)
			{
				Undo.RecordObject(_targetProfile, "Set Avatar Icon");
				_targetAvatar.avatarIcon = processedTexture;
				EditorUtility.SetDirty(_targetProfile);
			}

			// Callback
			_onCaptureComplete?.Invoke(processedTexture);

			EditorUtility.DisplayDialog("Capture", "Avatar icon captured successfully!", "OK");
			Close();
		}

		private Texture2D CaptureImage()
		{
			if (_targetAvatar?.avatarPrefab == null)
				return null;

			// Get resolution
			var resolution = CaptureResolutionHelper.GetResolution(_currentResolution);
			
			// Use camera system to capture
			return _camera?.Capture(_targetAvatar.avatarPrefab, (int)resolution.x, (int)resolution.y, _lighting, _background);
		}

		/// <summary>
		/// Populate avatar data from PipelineManager component (like Control Panel does).
		/// </summary>
		private void PopulateFromPipelineManager()
		{
			if (_targetAvatar == null)
			{
				Debug.LogError("[AvatarCaptureWindow] _targetAvatar is null!");
				return;
			}
			
			if (_targetAvatar.avatarPrefab == null)
			{
				Debug.LogError($"[AvatarCaptureWindow] avatarPrefab is null for avatar: {_targetAvatar.avatarName}");
				return;
			}

			// Use the same method as the main window - this syncs from PipelineManager to the asset
			_targetAvatar.PopulateFromPipelineManager();

			// Also try reading directly (in case PopulateFromPipelineManager didn't work)
			var directBlueprintId = ReadBlueprintIdFromPipelineManager();
			
			// Use direct read if asset doesn't have it
			var blueprintId = GetPreferredBlueprintId();
			if (string.IsNullOrEmpty(blueprintId) && !string.IsNullOrEmpty(directBlueprintId))
			{
				if (_targetProfile != null)
				{
					Undo.RecordObject(_targetProfile, "Set Blueprint ID from PipelineManager");
					_targetAvatar.blueprintIdPC = directBlueprintId;
					_targetAvatar.blueprintIdQuest = directBlueprintId;
					EditorUtility.SetDirty(_targetProfile);
					blueprintId = directBlueprintId;
				}
			}

			// Ensure gallery list exists
			EnsureGalleryList();

			// Load gallery images and main image from API if blueprint ID exists
			if (!string.IsNullOrEmpty(blueprintId))
			{
				_ = LoadGalleryAndThumbnailAsync(blueprintId);
				_ = PopulateAvatarFromAPI();
			}
			else
			{
				var prefabPath = AssetDatabase.GetAssetPath(_targetAvatar.avatarPrefab);
				Debug.LogWarning($"[AvatarCaptureWindow] ✗ No blueprint ID found. Prefab: {_targetAvatar.avatarPrefab.name}, Path: {prefabPath ?? "null"}");
				
				// Try to verify PipelineManager exists
				if (!string.IsNullOrEmpty(prefabPath))
				{
					var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
					if (prefabAsset != null)
					{
						var hasPM = prefabAsset.TryGetComponent<VRC.Core.PipelineManager>(out var pm);
						Debug.LogWarning($"[AvatarCaptureWindow] Prefab has PipelineManager: {hasPM}, BlueprintId: {(hasPM ? pm.blueprintId ?? "empty" : "N/A")}");
					}
				}
			}
		}

		/// <summary>
		/// Read blueprint ID directly from PipelineManager component on the prefab.
		/// Uses the exact same logic as AvatarAsset.SyncBlueprintIdFromComponent.
		/// </summary>
		private string ReadBlueprintIdFromPipelineManager()
		{
			if (_targetAvatar?.avatarPrefab == null)
			{
				Debug.LogWarning("[AvatarCaptureWindow] ReadBlueprintIdFromPipelineManager: avatar or prefab is null");
				return null;
			}

			// Use the exact same logic as AvatarAsset.SyncBlueprintIdFromComponent
			var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(_targetAvatar.avatarPrefab);
			if (string.IsNullOrEmpty(assetPath))
			{
				assetPath = AssetDatabase.GetAssetPath(_targetAvatar.avatarPrefab);
			}

			if (!string.IsNullOrEmpty(assetPath))
			{
				var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
				if (prefabAsset != null && prefabAsset.TryGetComponent<VRC.Core.PipelineManager>(out var pm))
				{
					var id = pm.blueprintId;
					return id;
				}
				else if (prefabAsset == null)
				{
					Debug.LogWarning($"[AvatarCaptureWindow] Failed to load prefab asset from path: {assetPath}");
				}
				else
				{
					Debug.LogWarning($"[AvatarCaptureWindow] Prefab asset does not have PipelineManager component: {assetPath}");
				}
			}
			else
			{
				Debug.LogWarning("[AvatarCaptureWindow] Could not determine prefab asset path");
			}

			return null;
		}

		/// <summary>
		/// Get the preferred blueprint ID (PC first, then Quest).
		/// </summary>
		private string GetPreferredBlueprintId()
		{
			if (_targetAvatar == null)
				return null;

			var pcId = _targetAvatar.blueprintIdPC;
			if (!string.IsNullOrEmpty(pcId))
				return pcId;

			return _targetAvatar.blueprintIdQuest;
		}

		/// <summary>
		/// Ensure gallery list exists.
		/// </summary>
		private void EnsureGalleryList()
		{
			if (_targetAvatar == null)
				return;

			if (_targetAvatar.galleryImages == null)
			{
				_targetAvatar.galleryImages = new System.Collections.Generic.List<AvatarGalleryImage>();
			}
		}

		/// <summary>
		/// Load gallery images and thumbnail from VRChat API.
		/// </summary>
		private async System.Threading.Tasks.Task LoadGalleryAndThumbnailAsync(string avatarId)
		{
			if (string.IsNullOrEmpty(avatarId) || _targetAvatar == null)
				return;

			try
			{
				// Load gallery images
				var galleryEntries = await AvatarGalleryClient.GetGalleryAsync(avatarId);
				if (galleryEntries != null && galleryEntries.Count > 0)
				{
					EnsureGalleryList();
					_targetAvatar.galleryImages.Clear();
					
					foreach (var entry in galleryEntries)
					{
						_targetAvatar.galleryImages.Add(entry);
						// Download thumbnail asynchronously
						_ = System.Threading.Tasks.Task.Run(async () =>
						{
							entry.thumbnail = await AvatarGalleryClient.DownloadImageAsync(entry.url);
							EditorApplication.delayCall += () => UpdatePreview();
						});
					}
				}

				// Load main avatar thumbnail from API
				await LoadAvatarThumbnailAsync(avatarId);
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"[AvatarCaptureWindow] Failed to load gallery: {ex.Message}");
			}
		}

		/// <summary>
		/// Load avatar thumbnail from VRChat API.
		/// </summary>
		private async System.Threading.Tasks.Task LoadAvatarThumbnailAsync(string avatarId)
		{
			if (string.IsNullOrEmpty(avatarId) || _targetAvatar == null)
				return;

			try
			{
				// Use reflection to call VRCApi.GetAvatar
				var apiType = System.Type.GetType("VRC.SDKBase.Editor.Api.VRCApi, VRCSDKBase");
				if (apiType == null)
					return;

				var getAvatarMethod = apiType.GetMethod("GetAvatar", new[] { typeof(string), typeof(bool) });
				if (getAvatarMethod == null)
					return;

				var avatarTask = getAvatarMethod.Invoke(null, new object[] { avatarId, true }) as System.Threading.Tasks.Task;
				if (avatarTask == null)
					return;

				await avatarTask;

				// Get the result
				var resultProperty = avatarTask.GetType().GetProperty("Result");
				if (resultProperty == null)
					return;

				var avatar = resultProperty.GetValue(avatarTask);
				if (avatar == null)
					return;

				// Get thumbnail URL
				var thumbnailUrlProperty = avatar.GetType().GetProperty("ThumbnailImageUrl");
				if (thumbnailUrlProperty != null)
				{
					var thumbnailUrl = thumbnailUrlProperty.GetValue(avatar) as string;
					if (!string.IsNullOrEmpty(thumbnailUrl))
					{
						// Download thumbnail
						var thumbnail = await AvatarGalleryClient.DownloadImageAsync(thumbnailUrl);
						if (thumbnail != null && _targetAvatar != null)
						{
							EditorApplication.delayCall += () =>
							{
								if (_targetAvatar != null)
								{
									_targetAvatar.avatarIcon = thumbnail;
									UpdatePreview();
								}
							};
						}
					}
				}
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"[AvatarCaptureWindow] Failed to load avatar thumbnail: {ex.Message}");
			}
		}

		/// <summary>
		/// Populate avatar data from VRChat API (name, description, tags, etc.).
		/// </summary>
		private async System.Threading.Tasks.Task PopulateAvatarFromAPI()
		{
			if (_targetAvatar == null || _targetAvatar.avatarPrefab == null)
				return;

			var blueprintId = GetPreferredBlueprintId();
			if (string.IsNullOrEmpty(blueprintId))
				return;

			try
			{
				// Use reflection to call VRCApi.GetAvatar
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

				// Get the result
				var resultProperty = avatarTask.GetType().GetProperty("Result");
				if (resultProperty == null)
					return;

				var avatar = resultProperty.GetValue(avatarTask);
				if (avatar == null)
					return;

				// Populate fields from API response
				EditorApplication.delayCall += () =>
				{
					if (_targetAvatar == null || _targetProfile == null)
						return;

					Undo.RecordObject(_targetProfile, "Populate Avatar from API");

					var nameProperty = avatar.GetType().GetProperty("Name");
					if (nameProperty != null && string.IsNullOrEmpty(_targetAvatar.avatarName))
					{
						_targetAvatar.avatarName = nameProperty.GetValue(avatar) as string;
					}

					var descProperty = avatar.GetType().GetProperty("Description");
					if (descProperty != null && string.IsNullOrEmpty(_targetAvatar.description))
					{
						_targetAvatar.description = descProperty.GetValue(avatar) as string;
					}

					var tagsProperty = avatar.GetType().GetProperty("Tags");
					if (tagsProperty != null)
					{
						var tags = tagsProperty.GetValue(avatar);
						if (tags != null)
						{
							var tagsList = tags as System.Collections.Generic.List<string>;
							if (tagsList != null && (_targetAvatar.tags == null || _targetAvatar.tags.Count == 0))
							{
								_targetAvatar.tags = new System.Collections.Generic.List<string>(tagsList);
							}
						}
					}

					var releaseStatusProperty = avatar.GetType().GetProperty("ReleaseStatus");
					if (releaseStatusProperty != null)
					{
						var status = releaseStatusProperty.GetValue(avatar) as string;
						if (!string.IsNullOrEmpty(status))
						{
							_targetAvatar.releaseStatus = status.Equals("public", System.StringComparison.OrdinalIgnoreCase) 
								? ReleaseStatus.Public 
								: ReleaseStatus.Private;
						}
					}

					EditorUtility.SetDirty(_targetProfile);
				};
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"[AvatarCaptureWindow] Failed to populate from API: {ex.Message}");
			}
		}

		private void OnDestroy()
		{
			_previewComponent?.Dispose();
			_camera?.Dispose();
			_lighting?.Dispose();
			_background?.Dispose();
			_postProcess?.Dispose();
		}
	}
}

