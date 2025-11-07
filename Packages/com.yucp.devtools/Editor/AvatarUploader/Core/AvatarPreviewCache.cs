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
		private static readonly Dictionary<GameObject, List<Action<Texture2D>>> PendingCallbacks = new Dictionary<GameObject, List<Action<Texture2D>>>();
		private static readonly HashSet<GameObject> PendingRequests = new HashSet<GameObject>();
		private static bool _isListening;

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

