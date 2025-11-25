using System;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader.Core
{
	/// <summary>
	/// Capture mode for avatar capture.
	/// </summary>
	public enum CaptureMode
	{
		Headshot,
		FullBody,
		Custom
	}

	/// <summary>
	/// Camera controller for avatar capture with position, rotation, FOV, and distance controls.
	/// Uses the scene's main camera or Scene View camera instead of PreviewRenderUtility.
	/// </summary>
	public class AvatarCaptureCamera : IDisposable
	{
		private Camera _sceneCamera;
		private Camera _tempCamera;
		private CaptureMode _mode = CaptureMode.Headshot;
		
		// Camera settings
		private Vector3 _position = Vector3.zero;
		private Vector3 _rotation = Vector3.zero;
		private float _fov = 35f;
		private float _distance = 1.5f;
		private Vector3 _lookAtTarget = Vector3.zero;

		public AvatarCaptureCamera()
		{
			// Will use scene camera when capturing
		}

		/// <summary>
		/// Set the capture mode (affects default camera positioning).
		/// </summary>
		public void SetMode(CaptureMode mode)
		{
			_mode = mode;
			ApplyModeDefaults();
		}

		private void ApplyModeDefaults()
		{
			switch (_mode)
			{
				case CaptureMode.Headshot:
					_distance = 1.5f;
					_fov = 35f;
					_rotation = new Vector3(0f, 0f, 0f);
					break;
				case CaptureMode.FullBody:
					_distance = 3.5f;
					_fov = 30f;
					_rotation = new Vector3(0f, 0f, 0f);
					break;
				case CaptureMode.Custom:
					// Keep current settings
					break;
			}
			UpdateCameraTransform();
		}

		/// <summary>
		/// Set camera position X.
		/// </summary>
		public void SetPositionX(float x)
		{
			_position.x = x;
			UpdateCameraTransform();
		}

		/// <summary>
		/// Set camera position Y.
		/// </summary>
		public void SetPositionY(float y)
		{
			_position.y = y;
			UpdateCameraTransform();
		}

		/// <summary>
		/// Set camera position Z.
		/// </summary>
		public void SetPositionZ(float z)
		{
			_position.z = z;
			_distance = z;
			UpdateCameraTransform();
		}

		/// <summary>
		/// Set camera rotation (yaw).
		/// </summary>
		public void SetRotationY(float yaw)
		{
			_rotation.y = yaw;
			UpdateCameraTransform();
		}

		/// <summary>
		/// Set camera rotation (pitch).
		/// </summary>
		public void SetRotationX(float pitch)
		{
			_rotation.x = pitch;
			UpdateCameraTransform();
		}

		/// <summary>
		/// Set field of view.
		/// </summary>
		public void SetFOV(float fov)
		{
			_fov = Mathf.Clamp(fov, 1f, 179f);
			var camera = GetCamera();
			if (camera != null)
				camera.fieldOfView = _fov;
		}

		/// <summary>
		/// Set distance from avatar.
		/// </summary>
		public void SetDistance(float distance)
		{
			_distance = Mathf.Max(0.1f, distance);
			_position.z = _distance;
			UpdateCameraTransform();
		}

		/// <summary>
		/// Set look-at target position.
		/// </summary>
		public void SetLookAtTarget(Vector3 target)
		{
			_lookAtTarget = target;
			UpdateCameraTransform();
		}

		private void UpdateCameraTransform()
		{
			var camera = GetCamera();
			if (camera == null)
				return;

			// Calculate camera position based on distance and rotation
			var rotation = Quaternion.Euler(_rotation.x, _rotation.y, 0f);
			var offset = rotation * Vector3.back * _distance;
			camera.transform.position = _lookAtTarget + offset;
			camera.transform.LookAt(_lookAtTarget);
		}

		/// <summary>
		/// Get the camera to use for capture (scene camera or temporary camera).
		/// </summary>
		private Camera GetCamera()
		{
			// Try to get Scene View camera first
			var sceneView = UnityEditor.SceneView.lastActiveSceneView;
			if (sceneView != null && sceneView.camera != null)
			{
				return sceneView.camera;
			}

			// Fallback to main camera
			if (Camera.main != null)
			{
				return Camera.main;
			}

			// Create temporary camera if none exists
			if (_tempCamera == null)
			{
				var cameraGO = new GameObject("AvatarCaptureCamera");
				cameraGO.hideFlags = HideFlags.HideAndDontSave;
				_tempCamera = cameraGO.AddComponent<Camera>();
				_tempCamera.fieldOfView = _fov;
				_tempCamera.nearClipPlane = 0.1f;
				_tempCamera.farClipPlane = 1000f;
			}

			return _tempCamera;
		}

		/// <summary>
		/// Apply camera settings from a preset.
		/// </summary>
		public void ApplyPreset(CameraSettings settings)
		{
			if (settings == null)
				return;

			_position = settings.position;
			_rotation = settings.rotation;
			_fov = settings.fov;
			_distance = settings.distance;
			_lookAtTarget = settings.lookAtTarget;
			_mode = settings.mode;

			UpdateCameraTransform();
			var camera = GetCamera();
			if (camera != null)
				camera.fieldOfView = _fov;
		}

		/// <summary>
		/// Get current camera settings.
		/// </summary>
		public CameraSettings GetSettings()
		{
			return new CameraSettings
			{
				position = _position,
				rotation = _rotation,
				fov = _fov,
				distance = _distance,
				lookAtTarget = _lookAtTarget,
				mode = _mode
			};
		}

		/// <summary>
		/// Capture an image of the avatar using the scene camera.
		/// </summary>
		public Texture2D Capture(GameObject avatar, int width, int height, AvatarCaptureLighting lighting, AvatarCaptureBackground background)
		{
			if (avatar == null)
				return null;

			// Get the scene camera
			var camera = GetCamera();
			if (camera == null)
			{
				Debug.LogError("[AvatarCaptureCamera] No camera available for capture. Please ensure Scene View is open or a Main Camera exists in the scene.");
				return null;
			}

			// Store original camera settings
			var originalPosition = camera.transform.position;
			var originalRotation = camera.transform.rotation;
			var originalFOV = camera.fieldOfView;
			var originalTargetTexture = camera.targetTexture;
			var originalClearFlags = camera.clearFlags;
			var originalBackgroundColor = camera.backgroundColor;

			try
			{
				// Setup avatar instance in scene
				var instance = UnityEngine.Object.Instantiate(avatar);
				instance.hideFlags = HideFlags.HideAndDontSave;
				instance.transform.position = Vector3.zero;
				instance.transform.rotation = Quaternion.identity;
				instance.SetActive(true);

				// Enable all children
				foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
				{
					if (child.gameObject != instance)
						child.gameObject.SetActive(true);
				}

				// Set A-pose
				SetAPose(instance);

				// Find look-at target (head bone or center)
				var headBone = FindHeadBone(instance);
				_lookAtTarget = headBone != null ? headBone.position : GetBoundsCenter(instance);
				UpdateCameraTransform();

				// Setup background
				background?.ApplyToCamera(camera);

				// Render
				var renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
				camera.targetTexture = renderTexture;
				camera.Render();

				// Read pixels
				RenderTexture.active = renderTexture;
				var texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
				texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				texture.Apply();

				// Cleanup
				RenderTexture.active = null;
				RenderTexture.ReleaseTemporary(renderTexture);
				UnityEngine.Object.DestroyImmediate(instance);

				return texture;
			}
			finally
			{
				// Restore original camera settings
				if (camera != null)
				{
					camera.transform.position = originalPosition;
					camera.transform.rotation = originalRotation;
					camera.fieldOfView = originalFOV;
					camera.targetTexture = originalTargetTexture;
					camera.clearFlags = originalClearFlags;
					camera.backgroundColor = originalBackgroundColor;
				}
			}
		}

		private void SetAPose(GameObject avatar)
		{
			// Set arms to A-pose (same logic as AvatarTilePreviewRenderer)
			var leftUpperArm = FindBone(avatar, "LeftUpperArm");
			var rightUpperArm = FindBone(avatar, "RightUpperArm");

			if (leftUpperArm != null)
				leftUpperArm.localRotation = Quaternion.Euler(-90f, 0f, 0f);
			if (rightUpperArm != null)
				rightUpperArm.localRotation = Quaternion.Euler(-90f, 0f, 0f);
		}

		private Transform FindHeadBone(GameObject avatar)
		{
			var paths = new[]
			{
				"Hips/Spine/Spine1/Spine2/Neck/Head",
				"Armature/Hips/Spine/Spine1/Spine2/Neck/Head",
				"Root/Hips/Spine/Spine1/Spine2/Neck/Head"
			};

			foreach (var path in paths)
			{
				var bone = avatar.transform.Find(path);
				if (bone != null)
					return bone;
			}

			var allChildren = avatar.GetComponentsInChildren<Transform>();
			foreach (var child in allChildren)
			{
				if (child.name.Equals("Head", System.StringComparison.OrdinalIgnoreCase))
					return child;
			}

			return null;
		}

		private Transform FindBone(GameObject avatar, string boneName)
		{
			var allChildren = avatar.GetComponentsInChildren<Transform>();
			foreach (var child in allChildren)
			{
				if (child.name.Equals(boneName, System.StringComparison.OrdinalIgnoreCase))
					return child;
			}
			return null;
		}

		private Vector3 GetBoundsCenter(GameObject go)
		{
			var renderers = go.GetComponentsInChildren<Renderer>();
			if (renderers.Length == 0)
				return Vector3.zero;

			Bounds bounds = renderers[0].bounds;
			for (int i = 1; i < renderers.Length; i++)
			{
				bounds.Encapsulate(renderers[i].bounds);
			}
			return bounds.center;
		}

		/// <summary>
		/// Get the preview camera for live preview rendering.
		/// </summary>
		public Camera GetPreviewCamera()
		{
			return GetCamera();
		}

		public void Dispose()
		{
			if (_tempCamera != null)
			{
				UnityEngine.Object.DestroyImmediate(_tempCamera.gameObject);
				_tempCamera = null;
			}
		}
	}

	/// <summary>
	/// Camera settings for presets.
	/// </summary>
	[Serializable]
	public class CameraSettings
	{
		public Vector3 position = Vector3.zero;
		public Vector3 rotation = Vector3.zero;
		public float fov = 35f;
		public float distance = 1.5f;
		public Vector3 lookAtTarget = Vector3.zero;
		public CaptureMode mode = CaptureMode.Headshot;
	}
}

