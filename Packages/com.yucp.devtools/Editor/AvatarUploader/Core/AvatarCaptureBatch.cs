using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader.Core
{
	/// <summary>
	/// Batch capture system for capturing multiple avatars with the same preset.
	/// </summary>
	public class AvatarCaptureBatch
	{
		/// <summary>
		/// Capture multiple avatars with the same preset.
		/// </summary>
		public static async Task<List<BatchCaptureResult>> CaptureBatch(
			List<AvatarAsset> avatars,
			AvatarCollection profile,
			CapturePreset preset,
			Action<int, int> onProgress = null)
		{
			var results = new List<BatchCaptureResult>();

			if (avatars == null || avatars.Count == 0)
				return results;

			var camera = new AvatarCaptureCamera();
			var lighting = new AvatarCaptureLighting();
			var background = new AvatarCaptureBackground();
			var postProcess = new AvatarCapturePostProcess();

			// Apply preset
			if (preset != null)
			{
				camera.ApplyPreset(preset.cameraSettings);
				lighting.ApplyPreset(preset.lightingSettings);
				background.ApplyPreset(preset.backgroundSettings);
				postProcess.ApplyPreset(preset.postProcessSettings);
			}

			var resolution = CaptureResolutionHelper.GetResolution(preset != null ? preset.resolution : CaptureResolution.VRChatStandard);

			for (int i = 0; i < avatars.Count; i++)
			{
				var avatar = avatars[i];
				onProgress?.Invoke(i + 1, avatars.Count);

				if (avatar == null || avatar.avatarPrefab == null)
				{
					results.Add(new BatchCaptureResult
					{
						avatar = avatar,
						success = false,
						error = "Avatar or prefab is null"
					});
					continue;
				}

				try
				{
					// Capture
					var texture = camera.Capture(avatar.avatarPrefab, (int)resolution.x, (int)resolution.y, lighting, background);
					if (texture == null)
					{
						results.Add(new BatchCaptureResult
						{
							avatar = avatar,
							success = false,
							error = "Capture returned null"
						});
						continue;
					}

					// Post-process
					var processedTexture = postProcess.Process(texture);

					// Save to avatar config
					Undo.RecordObject(profile, "Batch Capture Avatar Icon");
					avatar.avatarIcon = processedTexture;
					EditorUtility.SetDirty(profile);

					results.Add(new BatchCaptureResult
					{
						avatar = avatar,
						success = true,
						texture = processedTexture
					});

					// Small delay
					await Task.Delay(50);
				}
				catch (Exception ex)
				{
					results.Add(new BatchCaptureResult
					{
						avatar = avatar,
						success = false,
						error = ex.Message
					});
				}
			}

			camera.Dispose();
			lighting.Dispose();
			background.Dispose();
			postProcess.Dispose();

			return results;
		}
	}

	/// <summary>
	/// Result of a batch capture operation.
	/// </summary>
	public class BatchCaptureResult
	{
		public AvatarAsset avatar;
		public bool success;
		public Texture2D texture;
		public string error;
	}
}

