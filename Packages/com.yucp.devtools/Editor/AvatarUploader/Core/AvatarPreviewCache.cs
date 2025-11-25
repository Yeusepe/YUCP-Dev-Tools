using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Provides cached 3D previews for avatar prefabs using Unity's AssetPreview system.
	/// </summary>
	internal static class AvatarPreviewCache
	{
		private static readonly Dictionary<GameObject, Texture2D> Cache = new Dictionary<GameObject, Texture2D>();
		private static readonly Dictionary<GameObject, Texture2D> HeadShouldersCache = new Dictionary<GameObject, Texture2D>();
		private static readonly Dictionary<GameObject, List<Action<Texture2D>>> PendingCallbacks = new Dictionary<GameObject, List<Action<Texture2D>>>();
		private static readonly Dictionary<GameObject, List<Action<Texture2D>>> PendingHeadShouldersCallbacks = new Dictionary<GameObject, List<Action<Texture2D>>>();
		private static readonly HashSet<GameObject> PendingRequests = new HashSet<GameObject>();
		private static readonly HashSet<GameObject> PendingHeadShouldersRequests = new HashSet<GameObject>();
		private static bool _isListening;
		private static bool _isListeningHeadShoulders;

		/// <summary>
		/// Requests a preview texture for the given avatar prefab. Returns immediately if cached; otherwise returns null and invokes the callback when ready.
		/// </summary>
		public static Texture2D RequestPreview(GameObject prefab, Action<Texture2D> onReady)
		{
			if (prefab == null)
				return null;

			if (Cache.TryGetValue(prefab, out var cached) && cached != null)
			{
				return cached;
			}

			var preview = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
			if (preview != null)
			{
				Cache[prefab] = preview;
				return preview;
			}

			if (onReady != null)
			{
				if (!PendingCallbacks.TryGetValue(prefab, out var list))
				{
					list = new List<Action<Texture2D>>();
					PendingCallbacks[prefab] = list;
				}
				list.Add(onReady);
			}

			if (PendingRequests.Add(prefab) && !_isListening)
			{
				EditorApplication.update += Update;
				_isListening = true;
			}

			return null;
		}

		/// <summary>
		/// Requests a head and shoulders preview texture for the given avatar prefab. 
		/// Returns immediately if cached; otherwise returns null and invokes the callback when ready.
		/// </summary>
		public static Texture2D RequestHeadAndShouldersPreview(GameObject prefab, Action<Texture2D> onReady)
		{
			if (prefab == null)
				return null;

			if (HeadShouldersCache.TryGetValue(prefab, out var cached) && cached != null)
			{
				return cached;
			}

			// Try to render immediately
			var preview = RenderHeadAndShouldersPreview(prefab);
			if (preview != null)
			{
				HeadShouldersCache[prefab] = preview;
				return preview;
			}

			if (onReady != null)
			{
				if (!PendingHeadShouldersCallbacks.TryGetValue(prefab, out var list))
				{
					list = new List<Action<Texture2D>>();
					PendingHeadShouldersCallbacks[prefab] = list;
				}
				list.Add(onReady);
			}

			if (PendingHeadShouldersRequests.Add(prefab) && !_isListeningHeadShoulders)
			{
				EditorApplication.update += UpdateHeadShoulders;
				_isListeningHeadShoulders = true;
			}

			return null;
		}

		private static Texture2D RenderHeadAndShouldersPreview(GameObject prefab)
		{
			if (prefab == null)
				return null;

			try
			{
				var previewUtility = new PreviewRenderUtility();
				previewUtility.cameraFieldOfView = 30f;
				previewUtility.camera.nearClipPlane = 0.1f;
				previewUtility.camera.farClipPlane = 1000f;
				previewUtility.camera.backgroundColor = Color.black;
				previewUtility.camera.clearFlags = CameraClearFlags.Color;

				var instance = UnityEngine.Object.Instantiate(prefab);
				if (instance == null)
				{
					previewUtility.Cleanup();
					return null;
				}

				instance.hideFlags = HideFlags.HideAndDontSave;
				instance.transform.position = Vector3.zero;
				instance.transform.rotation = Quaternion.identity;

				previewUtility.AddSingleGO(instance);

				// Find head bone or upper torso
				Transform headBone = FindHeadBone(instance);
				Vector3 focusPoint = headBone != null ? headBone.position : GetUpperTorsoPosition(instance);

				// Position camera to focus on head/shoulders
				float distance = 1.5f;
				previewUtility.camera.transform.position = focusPoint + new Vector3(0, 0.2f, distance);
				previewUtility.camera.transform.LookAt(focusPoint);
				previewUtility.camera.fieldOfView = 35f; // Slightly wider to capture shoulders

				// Setup lighting
				previewUtility.lights[0].intensity = 1.1f;
				previewUtility.lights[0].transform.rotation = Quaternion.Euler(30f, 45f, 0f);
				previewUtility.lights[1].intensity = 0.75f;
				previewUtility.lights[1].transform.rotation = Quaternion.Euler(315f, 135f, 0f);
				previewUtility.ambientColor = new Color(0.25f, 0.28f, 0.3f, 1f);

				// Render to texture (256x256 for grid tiles)
				int size = 256;
				var renderTexture = RenderTexture.GetTemporary(size, size, 16, RenderTextureFormat.ARGB32);
				previewUtility.camera.targetTexture = renderTexture;
				previewUtility.camera.Render();

				// Read render texture to Texture2D
				RenderTexture.active = renderTexture;
				var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
				texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
				texture.Apply();

				// Crop to show only upper portion (head and shoulders)
				int cropHeight = (int)(size * 0.65f); // Show top 65% (head and shoulders)
				var croppedTexture = new Texture2D(size, cropHeight, TextureFormat.RGB24, false);
				var pixels = texture.GetPixels(0, size - cropHeight, size, cropHeight);
				croppedTexture.SetPixels(pixels);
				croppedTexture.Apply();

				// Cleanup
				RenderTexture.active = null;
				RenderTexture.ReleaseTemporary(renderTexture);
				UnityEngine.Object.DestroyImmediate(texture);
				UnityEngine.Object.DestroyImmediate(instance);
				previewUtility.Cleanup();

				return croppedTexture;
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to render head/shoulders preview: {ex.Message}");
				return null;
			}
		}

		private static Transform FindHeadBone(GameObject avatar)
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

		private static Vector3 GetUpperTorsoPosition(GameObject avatar)
		{
			// Calculate bounds and return upper torso position
			var renderers = avatar.GetComponentsInChildren<Renderer>();
			if (renderers.Length == 0)
				return Vector3.zero;

			Bounds bounds = renderers[0].bounds;
			for (int i = 1; i < renderers.Length; i++)
			{
				bounds.Encapsulate(renderers[i].bounds);
			}

			// Return position at upper third of bounds
			return bounds.center + new Vector3(0, bounds.extents.y * 0.4f, 0);
		}

		private static void UpdateHeadShoulders()
		{
			if (PendingHeadShouldersRequests.Count == 0)
			{
				EditorApplication.update -= UpdateHeadShoulders;
				_isListeningHeadShoulders = false;
				return;
			}

			var completed = new List<GameObject>();

			foreach (var prefab in PendingHeadShouldersRequests)
			{
				var preview = RenderHeadAndShouldersPreview(prefab);
				if (preview == null)
					continue;

				HeadShouldersCache[prefab] = preview;
				completed.Add(prefab);

				if (PendingHeadShouldersCallbacks.TryGetValue(prefab, out var callbacks))
				{
					foreach (var callback in callbacks)
					{
						try
						{
							callback?.Invoke(preview);
						}
						catch (Exception ex)
						{
							Debug.LogWarning($"[AvatarUploader] Head/shoulders preview callback failed: {ex.Message}");
						}
					}

					PendingHeadShouldersCallbacks.Remove(prefab);
				}
			}

			foreach (var done in completed)
			{
				PendingHeadShouldersRequests.Remove(done);
			}

			if (PendingHeadShouldersRequests.Count == 0)
			{
				EditorApplication.update -= UpdateHeadShoulders;
				_isListeningHeadShoulders = false;
			}
		}

		private static void Update()
		{
			if (PendingRequests.Count == 0)
			{
				EditorApplication.update -= Update;
				_isListening = false;
				return;
			}

			var completed = new List<GameObject>();

			foreach (var prefab in PendingRequests)
			{
				var preview = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
				if (preview == null)
					continue;

				Cache[prefab] = preview;
				completed.Add(prefab);

				if (PendingCallbacks.TryGetValue(prefab, out var callbacks))
				{
					foreach (var callback in callbacks)
					{
						try
						{
							callback?.Invoke(preview);
						}
						catch (Exception ex)
						{
							Debug.LogWarning($"[AvatarUploader] Avatar preview callback failed: {ex.Message}");
						}
					}

					PendingCallbacks.Remove(prefab);
				}
			}

			foreach (var done in completed)
			{
				PendingRequests.Remove(done);
			}

			if (PendingRequests.Count == 0)
			{
				EditorApplication.update -= Update;
				_isListening = false;
			}
		}
	}
}






