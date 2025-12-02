using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.DevTools.Editor.AvatarUploader.Core;

namespace YUCP.DevTools.Editor.AvatarUploader.UI.Components
{
	/// <summary>
	/// Live preview renderer for avatar capture window.
	/// </summary>
	public class CapturePreviewRenderer : VisualElement, IDisposable
	{
		private readonly IMGUIContainer _imguiContainer;
		private PreviewRenderUtility _previewUtility;
		private GameObject _previewInstance;
		private AvatarCaptureCamera _camera;
		private AvatarCaptureLighting _lighting;
		private AvatarCaptureBackground _background;
		private bool _disposed;
		private readonly Color _defaultBackground = new Color(0.07f, 0.1f, 0.13f, 1f);

		public CapturePreviewRenderer()
		{
			style.flexGrow = 1;
			style.flexShrink = 0;
			style.backgroundColor = _defaultBackground;

			_imguiContainer = new IMGUIContainer(OnGUIHandler)
			{
				style =
				{
					flexGrow = 1f,
					flexShrink = 0f
				}
			};

			RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
			Add(_imguiContainer);
		}

		public void SetTarget(GameObject prefab)
		{
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
			_previewInstance.SetActive(true);

			// Enable all children
			foreach (Transform child in _previewInstance.GetComponentsInChildren<Transform>(true))
			{
				if (child.gameObject != _previewInstance)
					child.gameObject.SetActive(true);
			}

			// Set A-pose
			SetAPose();

			_previewUtility.AddSingleGO(_previewInstance);
			_imguiContainer.MarkDirtyRepaint();
		}

		public void SetCamera(AvatarCaptureCamera camera)
		{
			_camera = camera;
			_imguiContainer.MarkDirtyRepaint();
		}

		public void SetLighting(AvatarCaptureLighting lighting)
		{
			_lighting = lighting;
			_imguiContainer.MarkDirtyRepaint();
		}

		public void SetBackground(AvatarCaptureBackground background)
		{
			_background = background;
			_imguiContainer.MarkDirtyRepaint();
		}

		private void SetAPose()
		{
			if (_previewInstance == null)
				return;

			var leftUpperArm = FindBone("LeftUpperArm");
			var rightUpperArm = FindBone("RightUpperArm");

			if (leftUpperArm != null)
				leftUpperArm.localRotation = Quaternion.Euler(-90f, 0f, 0f);
			if (rightUpperArm != null)
				rightUpperArm.localRotation = Quaternion.Euler(-90f, 0f, 0f);
		}

		private Transform FindBone(string boneName)
		{
			if (_previewInstance == null)
				return null;

			var allChildren = _previewInstance.GetComponentsInChildren<Transform>();
			foreach (var child in allChildren)
			{
				if (child.name.Equals(boneName, System.StringComparison.OrdinalIgnoreCase))
					return child;
			}
			return null;
		}

		private void OnGUIHandler()
		{
			if (_previewUtility == null || _previewInstance == null)
			{
				var width = resolvedStyle.width > 0 ? resolvedStyle.width : 100;
				var height = resolvedStyle.height > 0 ? resolvedStyle.height : 100;
				EditorGUI.DrawRect(new Rect(0, 0, width, height), _defaultBackground * 0.9f);
				return;
			}

			var rect = _imguiContainer.contentRect;
			
			// Validate rect dimensions
			if (rect.width <= 4f || rect.height <= 4f || rect.width <= 0 || rect.height <= 0 ||
			    float.IsNaN(rect.width) || float.IsNaN(rect.height) ||
			    float.IsInfinity(rect.width) || float.IsInfinity(rect.height))
			{
				// Draw background if rect is invalid
				var width = resolvedStyle.width > 0 ? resolvedStyle.width : 100;
				var height = resolvedStyle.height > 0 ? resolvedStyle.height : 100;
				EditorGUI.DrawRect(new Rect(0, 0, width, height), _defaultBackground * 0.9f);
				return;
			}

			// Update camera if available
			if (_camera != null)
			{
				var previewCam = _camera.GetPreviewCamera();
				if (previewCam != null)
				{
					// Camera is already configured by AvatarCaptureCamera
				}
			}

			// Setup lighting
			if (_lighting != null)
			{
				_lighting.ApplyToPreviewUtility(_previewUtility);
			}
			else
			{
				// Default lighting
				_previewUtility.lights[0].intensity = 1.1f;
				_previewUtility.lights[0].transform.rotation = Quaternion.Euler(30f, 45f, 0f);
				_previewUtility.lights[1].intensity = 0.75f;
				_previewUtility.lights[1].transform.rotation = Quaternion.Euler(315f, 135f, 0f);
				_previewUtility.ambientColor = new Color(0.25f, 0.28f, 0.3f, 1f);
			}

			// Setup background
			if (_background != null && _previewUtility.camera != null)
			{
				_background.ApplyToCamera(_previewUtility.camera);
			}
			else if (_previewUtility.camera != null)
			{
				_previewUtility.camera.clearFlags = CameraClearFlags.SolidColor;
				_previewUtility.camera.backgroundColor = _defaultBackground;
			}

			// Render
			_previewUtility.BeginPreview(rect, GUIStyle.none);
			_previewUtility.Render(true, true);
			Texture result = _previewUtility.EndPreview();
			GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);

			// Continuously repaint for live preview
			EditorApplication.delayCall += () => _imguiContainer.MarkDirtyRepaint();
		}

		private void EnsurePreviewUtility()
		{
			if (_previewUtility != null)
				return;

			_previewUtility = new PreviewRenderUtility();
			_previewUtility.cameraFieldOfView = 35f;
		}

		private void CleanupPreviewInstance()
		{
			if (_previewInstance != null)
			{
				UnityEngine.Object.DestroyImmediate(_previewInstance);
				_previewInstance = null;
			}
		}

		public void Dispose()
		{
			if (_disposed)
				return;

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

