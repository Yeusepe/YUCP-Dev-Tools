using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.DevTools.Editor.AvatarUploader;

namespace YUCP.DevTools.Editor.AvatarUploader.UI.Components
{
	/// <summary>
	/// Mini 3D preview renderer for avatar grid tiles with head bone tracking mouse.
	/// </summary>
	public class AvatarTilePreviewRenderer : VisualElement, IDisposable
	{
		// Static shared mouse position for all tile renderers
		private static Vector2 s_globalMousePosition = Vector2.zero;
		private static EditorWindow s_trackingWindow = null;

		/// <summary>
		/// Update the shared global mouse position (called from window's mouse move handler).
		/// </summary>
		public static void UpdateGlobalMousePosition(Vector2 position)
		{
			s_globalMousePosition = position;
		}

		private readonly IMGUIContainer _imguiContainer;
		private PreviewRenderUtility _previewUtility;
		private GameObject _previewInstance;
		private GameObject _prefabAsset;
		private Transform _headBone;
		private Transform _leftEyeBone;
		private Transform _rightEyeBone;
		private Transform _leftShoulder;
		private Transform _rightShoulder;
	private Transform _leftUpperArm;
	private Transform _rightUpperArm;
	private Quaternion _leftEyeRestRotation;
	private Quaternion _rightEyeRestRotation;
	private Quaternion _headRestRotation;
	private bool _disposed;
	private EditorWindow _parentWindow;
	private readonly Color _backgroundColor = Color.black;
	private bool _useLowSpecMode;
		private bool _disableCursorTracking;

		public AvatarTilePreviewRenderer()
		{
			style.flexGrow = 1;
			style.flexShrink = 0;
			style.backgroundColor = _backgroundColor;

			_imguiContainer = new IMGUIContainer(OnGUIHandler)
			{
				style =
				{
					flexGrow = 1f,
					flexShrink = 0f
				}
			};

			RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
			RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
			RegisterCallback<MouseMoveEvent>(OnMouseMove);
			RegisterCallback<MouseEnterEvent>(OnMouseEnter);
			RegisterCallback<MouseLeaveEvent>(OnMouseLeave);

			Add(_imguiContainer);
		}

		private void OnAttachToPanel(AttachToPanelEvent evt)
		{
			// Find the parent EditorWindow for window dimensions
			VisualElement element = this;
			while (element != null && _parentWindow == null)
			{
				if (element.panel != null && element.panel.visualTree != null)
				{
					var root = element.panel.visualTree;
					if (root.userData is EditorWindow window)
					{
						_parentWindow = window;
						s_trackingWindow = window;
						break;
					}
				}
				element = element.parent;
			}
		}

		public void SetTarget(GameObject prefab)
		{
			var settings = AvatarUploaderSettings.Instance;
			_useLowSpecMode = settings.UseLowSpecMode || IsLowSpecSystem();
			_disableCursorTracking = settings.DisableCursorTracking;

			_prefabAsset = prefab;

			if (_useLowSpecMode)
			{
				CleanupPreviewInstance();
				_imguiContainer.MarkDirtyRepaint();
				return;
			}

			EnsurePreviewUtility();
			CleanupPreviewInstance();

			if (prefab == null)
			{
				_imguiContainer.MarkDirtyRepaint();
				return;
			}

			_previewInstance = UnityEngine.Object.Instantiate(prefab);
			if (_previewInstance == null)
			{
				_imguiContainer.MarkDirtyRepaint();
				return;
			}

			_previewInstance.hideFlags = HideFlags.HideAndDontSave;
			_previewInstance.transform.position = Vector3.zero;
			_previewInstance.transform.rotation = Quaternion.identity;
			
			// Ensure the preview instance is enabled even if the prefab was disabled
			_previewInstance.SetActive(true);
			
			// Also enable all children in case any are disabled
			foreach (Transform child in _previewInstance.GetComponentsInChildren<Transform>(true))
			{
				if (child.gameObject != _previewInstance)
				{
					child.gameObject.SetActive(true);
				}
			}

			_previewUtility.AddSingleGO(_previewInstance);

			// Find bones
			_headBone = FindHeadBone(_previewInstance);
			_leftEyeBone = FindEyeBone(_previewInstance, true);
			_rightEyeBone = FindEyeBone(_previewInstance, false);
			_leftShoulder = FindShoulderBone(_previewInstance, true);
			_rightShoulder = FindShoulderBone(_previewInstance, false);
			_leftUpperArm = FindUpperArmBone(_previewInstance, true);
			_rightUpperArm = FindUpperArmBone(_previewInstance, false);

			// Store rest rotations (where they're looking by default)
			// For eyes, we need to normalize and store the forward-looking rotation
			_headRestRotation = _headBone != null ? _headBone.localRotation : Quaternion.identity;
			
			// For eyes, store their current rotation as the "forward" direction
			// This accounts for any default rotation the eyes might have
			if (_leftEyeBone != null)
			{
				_leftEyeRestRotation = _leftEyeBone.localRotation;
			}
			else
			{
				_leftEyeRestRotation = Quaternion.identity;
			}
			
			if (_rightEyeBone != null)
			{
				_rightEyeRestRotation = _rightEyeBone.localRotation;
			}
			else
			{
				_rightEyeRestRotation = Quaternion.identity;
			}

			// Set avatar to A-pose (arms down)
			SetAPose();

			// Position camera to focus on head/shoulders
			Bounds bounds = CalculateBounds(_previewInstance);
			Vector3 focusPoint = _headBone != null ? _headBone.position : GetUpperTorsoPosition(_previewInstance);
			
			float distance = 1.5f;
			_previewUtility.camera.transform.position = focusPoint + new Vector3(0, 0.2f, distance);
			_previewUtility.camera.transform.LookAt(focusPoint);
			_previewUtility.camera.nearClipPlane = 0.1f;
			_previewUtility.camera.farClipPlane = 1000f;
			_previewUtility.camera.fieldOfView = 35f;

			_imguiContainer.MarkDirtyRepaint();
		}

		private void SetAPose()
		{
			// Set arms to A-pose (arms down, relaxed position)
			// A-pose: arms hang down naturally at the sides, not T-pose (arms out horizontally)
			// In VRChat avatars, T-pose typically has upper arms/clavicles rotated to point arms horizontally
			// To get A-pose, we need to rotate them so arms hang down
			
			// Try multiple bones in the arm chain
			// Start with clavicle/shoulder, then upper arm
			
			// Left side
			if (_leftShoulder != null)
			{
				// Rotate clavicle/shoulder to bring arm down
				// Try rotating around X axis to bring arm from horizontal to vertical
				var currentRot = _leftShoulder.localRotation.eulerAngles;
				// Reset to identity and then apply A-pose rotation
				_leftShoulder.localRotation = Quaternion.Euler(0f, 0f, 0f);
			}
			
			if (_leftUpperArm != null)
			{
				// Rotate upper arm to bring it down
				// T-pose: arm is horizontal (typically needs rotation around X or Z)
				// A-pose: arm hangs down (typically identity or slight forward rotation)
				var currentRot = _leftUpperArm.localRotation.eulerAngles;
				
				// Try multiple approaches:
				// 1. If Z rotation is around 90 (T-pose), reset it
				// 2. Rotate around X to bring arm down
				// 3. Try identity
				
				if (Mathf.Abs(currentRot.z - 90f) < 45f || Mathf.Abs(currentRot.z - 270f) < 45f)
				{
					// T-pose detected: reset Z and rotate X
					_leftUpperArm.localRotation = Quaternion.Euler(-90f, 0f, 0f);
				}
				else if (Mathf.Abs(currentRot.x) < 30f && Mathf.Abs(currentRot.y) < 30f && Mathf.Abs(currentRot.z) < 30f)
				{
					// Close to identity, might already be A-pose, but ensure it's correct
					_leftUpperArm.localRotation = Quaternion.Euler(-90f, 0f, 0f);
				}
				else
				{
					// Try identity
					_leftUpperArm.localRotation = Quaternion.identity;
				}
			}
			
			// Right side
			if (_rightShoulder != null)
			{
				var currentRot = _rightShoulder.localRotation.eulerAngles;
				_rightShoulder.localRotation = Quaternion.Euler(0f, 0f, 0f);
			}
			
			if (_rightUpperArm != null)
			{
				var currentRot = _rightUpperArm.localRotation.eulerAngles;
				if (Mathf.Abs(currentRot.z - 90f) < 45f || Mathf.Abs(currentRot.z - 270f) < 45f)
				{
					_rightUpperArm.localRotation = Quaternion.Euler(-90f, 0f, 0f);
				}
				else if (Mathf.Abs(currentRot.x) < 30f && Mathf.Abs(currentRot.y) < 30f && Mathf.Abs(currentRot.z) < 30f)
				{
					_rightUpperArm.localRotation = Quaternion.Euler(-90f, 0f, 0f);
				}
				else
				{
					_rightUpperArm.localRotation = Quaternion.identity;
				}
			}
		}

		private void OnGUIHandler()
		{
			var rect = _imguiContainer.contentRect;
			if (rect.width <= 4f || rect.height <= 4f || rect.width <= 0 || rect.height <= 0)
			{
				return;
			}

			if (_useLowSpecMode)
			{
				DrawIconPreview(rect);
				return;
			}

			if (_previewUtility == null || _previewInstance == null)
			{
				var width = resolvedStyle.width > 0 ? resolvedStyle.width : 100;
				var height = resolvedStyle.height > 0 ? resolvedStyle.height : 100;
				EditorGUI.DrawRect(new Rect(0, 0, width, height), _backgroundColor * 0.9f);
				return;
			}

			// Get mouse position relative to this preview render area
			// In IMGUI, Event.current.mousePosition is relative to the GUI coordinate system
			// We need to convert it to be relative to this specific preview area
			Vector2 mousePos = Event.current.mousePosition;
			
			// Validate rect dimensions
			if (rect.width <= 0 || rect.height <= 0 || float.IsNaN(rect.width) || float.IsNaN(rect.height))
			{
				return; // Skip rotation updates if rect is invalid
			}
			
			// Convert to local coordinates relative to this preview area
			// The rect is the IMGUIContainer's content rect (the actual render area)
			Vector2 localMousePos = mousePos - new Vector2(rect.x, rect.y);
			
			// Use the preview render area center - this is where the avatar is rendered
			var previewCenter = new Vector2(rect.width * 0.5f, rect.height * 0.5f);
			
			// Validate preview center
			if (previewCenter.x <= 0 || previewCenter.y <= 0 || float.IsNaN(previewCenter.x) || float.IsNaN(previewCenter.y))
			{
				return; // Skip rotation updates if center is invalid
			}
			
			// Normalize mouse position relative to preview center
			// When mouse is at preview center, normalized values are 0 (avatar looks straight ahead)
			// Normalized: -1 (left/bottom) to +1 (right/top)
			var normalizedX = Mathf.Clamp(((localMousePos.x - previewCenter.x) / previewCenter.x), -1f, 1f);
			var normalizedY = Mathf.Clamp(((previewCenter.y - localMousePos.y) / previewCenter.y), -1f, 1f);
			
			// Validate normalized values
			if (float.IsNaN(normalizedX) || float.IsNaN(normalizedY) || float.IsInfinity(normalizedX) || float.IsInfinity(normalizedY))
			{
				normalizedX = 0f;
				normalizedY = 0f;
			}
			
			// Update eyes to track cursor (more responsive than head)
			// Eyes should look TOWARDS the mouse - EXAGGERATED movement
			// Account for their rest rotation (where they're looking by default)
			if (!_disableCursorTracking && (_leftEyeBone != null || _rightEyeBone != null))
			{
				// Calculate look direction for eyes - reduced exaggeration
				// Apply rotations independently
				var eyeYaw = -normalizedX * 30f; // Reduced: Mouse right = look right (only affects Y axis)
				var eyePitch = normalizedY * 20f; // Reduced: Mouse up = look up (Y axis inverted, so no negative)
				
				// Apply rotations independently
				// Convert rest rotation to Euler, add tracking, convert back
				if (_leftEyeBone != null)
				{
					// Get rest rotation as Euler angles
					var restEuler = _leftEyeRestRotation.eulerAngles;
					// Add tracking rotation independently to each axis
					var finalEuler = new Vector3(
						restEuler.x + eyePitch,  // Pitch (X axis) - only affected by Y mouse movement
						restEuler.y + eyeYaw,    // Yaw (Y axis) - only affected by X mouse movement
						restEuler.z              // Roll (Z axis) - unchanged
					);
					
					// Validate Euler angles before creating quaternion
					if (!float.IsNaN(finalEuler.x) && !float.IsNaN(finalEuler.y) && !float.IsNaN(finalEuler.z) &&
					    !float.IsInfinity(finalEuler.x) && !float.IsInfinity(finalEuler.y) && !float.IsInfinity(finalEuler.z))
					{
						_leftEyeBone.localRotation = Quaternion.Euler(finalEuler);
					}
				}
				if (_rightEyeBone != null)
				{
					var restEuler = _rightEyeRestRotation.eulerAngles;
					var finalEuler = new Vector3(
						restEuler.x + eyePitch,  // Pitch (X axis) - only affected by Y mouse movement
						restEuler.y + eyeYaw,    // Yaw (Y axis) - only affected by X mouse movement
						restEuler.z              // Roll (Z axis) - unchanged
					);
					
					// Validate Euler angles before creating quaternion
					if (!float.IsNaN(finalEuler.x) && !float.IsNaN(finalEuler.y) && !float.IsNaN(finalEuler.z) &&
					    !float.IsInfinity(finalEuler.x) && !float.IsInfinity(finalEuler.y) && !float.IsInfinity(finalEuler.z))
					{
						_rightEyeBone.localRotation = Quaternion.Euler(finalEuler);
					}
				}
			}
			
			// Update head bone with subtle movement (eyes do most of the tracking)
			if (!_disableCursorTracking && _headBone != null)
			{
				// Head moves less than eyes - subtle following
				// Fix X inversion: when mouse is right (+normalizedX), head should look right
				var headYaw = -normalizedX * 5f; // Reduced: Mouse right = look right (negated for correct direction)
				var headPitch = -normalizedY * 3f; // Reduced: Mouse up = look up
				
				// Validate angles before creating quaternion
				if (!float.IsNaN(headYaw) && !float.IsNaN(headPitch) && 
				    !float.IsInfinity(headYaw) && !float.IsInfinity(headPitch))
				{
					// Apply rotation relative to rest position
					var headRotation = Quaternion.Euler(headPitch, headYaw, 0f);
					
					// Validate quaternion before applying
					if (!float.IsNaN(headRotation.x) && !float.IsNaN(headRotation.y) && 
					    !float.IsNaN(headRotation.z) && !float.IsNaN(headRotation.w))
					{
						_headBone.localRotation = _headRestRotation * headRotation;
					}
				}
			}

			// Ensure rect is valid for rendering
			if (rect.width > 0 && rect.height > 0)
			{
				_previewUtility.BeginPreview(rect, GUIStyle.none);
				_previewUtility.camera.backgroundColor = _backgroundColor;
				_previewUtility.camera.clearFlags = CameraClearFlags.Color;
				_previewUtility.lights[0].intensity = 1.1f;
				_previewUtility.lights[0].transform.rotation = Quaternion.Euler(30f, 45f, 0f);
				_previewUtility.lights[1].intensity = 0.75f;
				_previewUtility.lights[1].transform.rotation = Quaternion.Euler(315f, 135f, 0f);
				_previewUtility.ambientColor = new Color(0.25f, 0.28f, 0.3f, 1f);

				_previewUtility.Render(true, true);

				Texture result = _previewUtility.EndPreview();
				if (result != null)
				{
					GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
				}
			}

			// Continuously repaint to keep 3D render alive and update eye/head tracking throughout window
			// Continuously repaint to maintain smooth rendering and high resolution
			// Use EditorApplication.update for consistent frame rate
			if (!_useLowSpecMode && !_disposed && _previewInstance != null)
			{
				EditorApplication.delayCall += () => 
				{
					if (!_disposed && _imguiContainer != null)
						_imguiContainer.MarkDirtyRepaint();
				};
			}
		}

		private void OnMouseMove(MouseMoveEvent evt)
		{
			// Update shared global mouse position
			s_globalMousePosition = evt.mousePosition;
			_imguiContainer.MarkDirtyRepaint();
			evt.StopPropagation();
		}

	private void OnMouseEnter(MouseEnterEvent evt)
	{
		// Start continuous repainting for live 3D render
		_imguiContainer.MarkDirtyRepaint();
	}

	private void OnMouseLeave(MouseLeaveEvent evt)
	{
		// Keep tracking mouse throughout window
		_imguiContainer.MarkDirtyRepaint();
	}

		private void EnsurePreviewUtility()
		{
			if (_previewUtility != null)
				return;

			_previewUtility = new PreviewRenderUtility();
			_previewUtility.cameraFieldOfView = 35f;
		}

		private Transform FindHeadBone(GameObject avatar)
		{
			// Common VRChat avatar bone hierarchy patterns
			var commonPaths = new[]
			{
				"Hips/Spine/Spine1/Spine2/Neck/Head",
				"Armature/Hips/Spine/Spine1/Spine2/Neck/Head",
				"Root/Hips/Spine/Spine1/Spine2/Neck/Head",
				"Hips/Spine/Spine1/Neck/Head",
				"Armature/Hips/Spine/Spine1/Neck/Head"
			};

			foreach (var path in commonPaths)
			{
				var bone = avatar.transform.Find(path);
				if (bone != null)
					return bone;
			}

			// Fallback: search for "Head" bone
			var allChildren = avatar.GetComponentsInChildren<Transform>();
			foreach (var child in allChildren)
			{
				if (child.name.Equals("Head", System.StringComparison.OrdinalIgnoreCase))
					return child;
			}

			return null;
		}

		private Transform FindEyeBone(GameObject avatar, bool left)
		{
			var eyeName = left ? "LeftEye" : "RightEye";
			var eyeNameAlt = left ? "Eye_L" : "Eye_R";
			
			// Try common paths
			if (_headBone != null)
			{
				var eye = _headBone.Find(eyeName) ?? _headBone.Find(eyeNameAlt);
				if (eye != null)
					return eye;
			}

			// Search all children
			var allChildren = avatar.GetComponentsInChildren<Transform>();
			foreach (var child in allChildren)
			{
				if (child.name.Equals(eyeName, System.StringComparison.OrdinalIgnoreCase) ||
				    child.name.Equals(eyeNameAlt, System.StringComparison.OrdinalIgnoreCase))
					return child;
			}

			return null;
		}

		private Transform FindShoulderBone(GameObject avatar, bool left)
		{
			var shoulderName = left ? "LeftShoulder" : "RightShoulder";
			var clavicleName = left ? "LeftCollar" : "RightCollar";
			var clavicleNameAlt = left ? "LeftClavicle" : "RightClavicle";
			
			// Common paths for shoulder/clavicle bones
			var commonPaths = new[]
			{
				$"Hips/Spine/Spine1/Spine2/{shoulderName}",
				$"Armature/Hips/Spine/Spine1/Spine2/{shoulderName}",
				$"Hips/Spine/Spine1/{shoulderName}",
				$"Armature/Hips/Spine/Spine1/{shoulderName}",
				$"Hips/Spine/Spine1/Spine2/{clavicleName}",
				$"Armature/Hips/Spine/Spine1/Spine2/{clavicleName}",
				$"Hips/Spine/Spine1/Spine2/{clavicleNameAlt}",
				$"Armature/Hips/Spine/Spine1/Spine2/{clavicleNameAlt}"
			};

			foreach (var path in commonPaths)
			{
				var bone = avatar.transform.Find(path);
				if (bone != null)
					return bone;
			}

			// Fallback: search for shoulder/clavicle bones
			var allChildren = avatar.GetComponentsInChildren<Transform>();
			foreach (var child in allChildren)
			{
				if (child.name.Equals(shoulderName, System.StringComparison.OrdinalIgnoreCase) ||
				    child.name.Equals(clavicleName, System.StringComparison.OrdinalIgnoreCase) ||
				    child.name.Equals(clavicleNameAlt, System.StringComparison.OrdinalIgnoreCase))
					return child;
			}

			return null;
		}

		private Transform FindUpperArmBone(GameObject avatar, bool left)
		{
			var upperArmName = left ? "LeftUpperArm" : "RightUpperArm";
			
			// Common paths for upper arm bones
			var commonPaths = new[]
			{
				$"Hips/Spine/Spine1/Spine2/{upperArmName}",
				$"Armature/Hips/Spine/Spine1/Spine2/{upperArmName}",
				$"Hips/Spine/Spine1/{upperArmName}",
				$"Armature/Hips/Spine/Spine1/{upperArmName}",
				$"Hips/Spine/Spine1/Spine2/LeftShoulder/{upperArmName}",
				$"Hips/Spine/Spine1/Spine2/RightShoulder/{upperArmName}",
				$"Armature/Hips/Spine/Spine1/Spine2/LeftShoulder/{upperArmName}",
				$"Armature/Hips/Spine/Spine1/Spine2/RightShoulder/{upperArmName}"
			};

			foreach (var path in commonPaths)
			{
				var bone = avatar.transform.Find(path);
				if (bone != null)
					return bone;
			}

			// Fallback: search for upper arm bone
			var allChildren = avatar.GetComponentsInChildren<Transform>();
			foreach (var child in allChildren)
			{
				if (child.name.Equals(upperArmName, System.StringComparison.OrdinalIgnoreCase))
					return child;
			}

			return null;
		}

		private Vector3 GetUpperTorsoPosition(GameObject avatar)
		{
			var renderers = avatar.GetComponentsInChildren<Renderer>();
			if (renderers.Length == 0)
				return Vector3.zero;

			Bounds bounds = renderers[0].bounds;
			for (int i = 1; i < renderers.Length; i++)
			{
				bounds.Encapsulate(renderers[i].bounds);
			}

			return bounds.center + new Vector3(0, bounds.extents.y * 0.4f, 0);
		}

		private Bounds CalculateBounds(GameObject go)
		{
			var renderers = go.GetComponentsInChildren<Renderer>();
			if (renderers.Length == 0)
			{
				return new Bounds(go.transform.position, Vector3.one);
			}

			Bounds bounds = renderers[0].bounds;
			for (int i = 1; i < renderers.Length; i++)
			{
				bounds.Encapsulate(renderers[i].bounds);
			}
			return bounds;
		}

		private void DrawIconPreview(Rect rect)
		{
			EditorGUI.DrawRect(rect, _backgroundColor * 0.9f);

			if (_prefabAsset == null)
			{
				return;
			}

			Texture2D icon = AssetPreview.GetAssetPreview(_prefabAsset);
			if (icon == null)
			{
				icon = EditorGUIUtility.FindTexture("Prefab Icon");
			}

			if (icon != null)
			{
				var iconSize = Mathf.Min(rect.width, rect.height) * 0.7f;
				var iconRect = new Rect(
					rect.x + (rect.width - iconSize) * 0.5f,
					rect.y + (rect.height - iconSize) * 0.5f,
					iconSize,
					iconSize
				);
				GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
			}
		}

		private static bool IsLowSpecSystem()
		{
			var graphicsMemory = SystemInfo.graphicsMemorySize;
			var systemMemory = SystemInfo.systemMemorySize;
			var processorCount = SystemInfo.processorCount;
			var graphicsDeviceType = SystemInfo.graphicsDeviceType;

			bool isLowSpec = false;

			if (graphicsMemory > 0 && graphicsMemory < 2048)
				isLowSpec = true;

			if (systemMemory > 0 && systemMemory < 4096)
				isLowSpec = true;

			if (processorCount < 4)
				isLowSpec = true;

			if (graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2 ||
			    graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3)
				isLowSpec = true;

			return isLowSpec;
		}

		private void CleanupPreviewInstance()
		{
			if (_previewInstance != null)
			{
				UnityEngine.Object.DestroyImmediate(_previewInstance);
				_previewInstance = null;
			}
			_headBone = null;
			_leftEyeBone = null;
			_rightEyeBone = null;
			_leftShoulder = null;
			_rightShoulder = null;
			_leftUpperArm = null;
			_rightUpperArm = null;
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			// No need to unregister - mouse tracking is handled by the window itself
			CleanupPreviewInstance();

			if (_previewUtility != null)
			{
				_previewUtility.Cleanup();
				_previewUtility = null;
			}

			_disposed = true;
		}
	}
}

