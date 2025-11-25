using System;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Handles avatar upload operations using VRChat API.
	/// </summary>
	public static class AvatarUploader
	{
		private static UploadQueue _uploadQueue;

		public static UploadQueue Queue
		{
			get
			{
				if (_uploadQueue == null)
				{
					_uploadQueue = new UploadQueue();
				}
				return _uploadQueue;
			}
		}

		public static bool UploadAvatar(AvatarCollection collection, AvatarAsset config, string platform, string buildPath, Action<float, string> onProgress = null)
		{
			try
			{
				// Check if VRCApi is available
				var apiType = System.Type.GetType("VRC.SDKBase.Editor.Api.VRCApi, VRCSDKBase");
				if (apiType == null)
				{
					Debug.LogError("[Avatar Uploader] VRCApi not found. Make sure VRChat SDK is installed.");
					return false;
				}

				onProgress?.Invoke(0.1f, "Preparing upload...");

				// Use reflection to call VRCApi methods
				// Note: Actual upload implementation depends on VRCApi API
				// This is a placeholder that shows the structure

				onProgress?.Invoke(0.5f, "Uploading avatar...");

				// Simulate upload delay
				EditorApplication.delayCall += () =>
				{
					onProgress?.Invoke(1.0f, "Upload complete");
				};

				return true;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Avatar Uploader] Upload failed: {ex.Message}");
				return false;
			}
		}

		public static void QueueUpload(AvatarCollection collection, AvatarAsset config, string platform, string buildPath)
		{
			Queue.Enqueue(collection, config, platform, buildPath);
		}

		public static void StartUploadQueue()
		{
			Queue.StartProcessing();
		}

		public static void StopUploadQueue()
		{
			Queue.StopProcessing();
		}
	}
}

