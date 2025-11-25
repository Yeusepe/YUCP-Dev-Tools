using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.AvatarUploader.UI.Components
{
	public class AvatarPreviewRenderer : VisualElement, IDisposable
	{
		private readonly IMGUIContainer _imguiContainer;
		private PreviewRenderUtility _previewUtility;
		private GameObject _previewInstance;
		private float _rotation;
		private Vector2 _dragStart;
		private bool _isDragging;
		private bool _disposed;
		private readonly Color _backgroundColor = Color.black;

		public AvatarPreviewRenderer()
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

			RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
			_imguiContainer.RegisterCallback<MouseDownEvent>(OnMouseDown);
			_imguiContainer.RegisterCallback<MouseUpEvent>(OnMouseUp);
			_imguiContainer.RegisterCallback<MouseMoveEvent>(OnMouseMove);

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
			if (_previewInstance != null)
			{
				_previewUtility.AddSingleGO(_previewInstance);
			}

			if (_previewInstance == null)
			{
				_imguiContainer.MarkDirtyRepaint();
				return;
			}

			_previewInstance.hideFlags = HideFlags.HideAndDontSave;
			_previewInstance.transform.position = Vector3.zero;
			_previewInstance.transform.rotation = Quaternion.identity;

			Bounds bounds = CalculateBounds(_previewInstance);
			float magnitude = bounds.extents.magnitude;
			if (Mathf.Approximately(magnitude, 0f))
			{
				magnitude = 1f;
			}

			_previewUtility.camera.transform.position = bounds.center + new Vector3(0, bounds.extents.y * 0.3f, magnitude * 3.2f);
			_previewUtility.camera.transform.LookAt(bounds.center);
			_previewUtility.camera.nearClipPlane = 0.1f;
			_previewUtility.camera.farClipPlane = 1000f;
			_rotation = 145f;

			_imguiContainer.MarkDirtyRepaint();
		}

		private void OnGUIHandler()
		{
			if (_previewUtility == null || _previewInstance == null)
			{
				var width = resolvedStyle.width > 0 ? resolvedStyle.width : 100;
				var height = resolvedStyle.height > 0 ? resolvedStyle.height : 100;
				EditorGUI.DrawRect(new Rect(0, 0, width, height), _backgroundColor * 0.9f);
				GUI.Label(new Rect(0, 0, width, height), "Assign an avatar prefab to see a preview", EditorStyles.centeredGreyMiniLabel);
				return;
			}

			var rect = _imguiContainer.contentRect;
			if (rect.width <= 4f || rect.height <= 4f || rect.width <= 0 || rect.height <= 0)
			{
				return;
			}

			Quaternion rotation = Quaternion.Euler(0f, _rotation, 0f);
			_previewInstance.transform.rotation = rotation;

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
			GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);

			if (!_isDragging)
			{
				_rotation += Time.deltaTime * 5f;
				_imguiContainer.MarkDirtyRepaint();
			}
		}

		private void OnMouseDown(MouseDownEvent evt)
		{
			if (evt.button != (int)MouseButton.LeftMouse)
				return;

			_isDragging = true;
			_dragStart = evt.mousePosition;
			evt.StopPropagation();
		}

		private void OnMouseUp(MouseUpEvent evt)
		{
			if (evt.button != (int)MouseButton.LeftMouse)
				return;

			_isDragging = false;
			evt.StopPropagation();
		}

		private void OnMouseMove(MouseMoveEvent evt)
		{
			if (!_isDragging)
				return;

			Vector2 delta = evt.mousePosition - _dragStart;
			_dragStart = evt.mousePosition;
			_rotation -= delta.x * 0.4f;
			_imguiContainer.MarkDirtyRepaint();
			evt.StopPropagation();
		}

		private void EnsurePreviewUtility()
		{
			if (_previewUtility != null)
				return;

			_previewUtility = new PreviewRenderUtility();
			_previewUtility.cameraFieldOfView = 30f;
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
