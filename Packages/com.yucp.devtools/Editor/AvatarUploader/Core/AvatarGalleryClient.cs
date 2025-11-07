using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	internal static class AvatarGalleryClient
	{
		private const string ApiBaseUrl = "https://api.vrchat.cloud/api/1";
		private static string _cachedApiKey;
		private static bool _apiKeyFetchedFromSdk;

		public static async Task<IReadOnlyList<AvatarGalleryImage>> GetGalleryAsync(string avatarId)
		{
			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration || !settings.HasStoredApiKey)
				return Array.Empty<AvatarGalleryImage>();

			if (string.IsNullOrEmpty(avatarId))
			{
				return Array.Empty<AvatarGalleryImage>();
			}

			var query = $"/files?tag=avatargallery&galleryId={avatarId}&n=100&offset=0";
			var url = BuildRequestUrl(query);
			if (string.IsNullOrEmpty(url))
				return Array.Empty<AvatarGalleryImage>();

			var request = UnityWebRequest.Get(url);
			ApplyAuthHeaders(request);

			await SendRequestAsync(request);

			if (request.result != UnityWebRequest.Result.Success)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to load gallery: {request.error}");
				return Array.Empty<AvatarGalleryImage>();
			}

			try
			{
				var response = request.downloadHandler.text;
				var wrapper = JsonUtility.FromJson<FileResponseWrapper>(WrapArray(response));
				if (wrapper == null || wrapper.entries == null)
					return Array.Empty<AvatarGalleryImage>();

				var list = new List<AvatarGalleryImage>();
				foreach (var entry in wrapper.entries)
				{
					if (entry == null || entry.file == null)
						continue;

					list.Add(new AvatarGalleryImage
					{
						id = entry.id,
						fileId = entry.file.id,
						url = entry.file.GetUrl()
					});
				}

				return list;
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to parse gallery response: {ex.Message}");
				return Array.Empty<AvatarGalleryImage>();
			}
			finally
			{
				request.Dispose();
			}
		}

		public static async Task<bool> UploadGalleryImageAsync(string avatarId, byte[] imageBytes, string fileName)
		{
			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration || !settings.HasStoredApiKey)
				return false;

			if (string.IsNullOrEmpty(avatarId) || imageBytes == null || imageBytes.Length == 0)
			{
				return false;
			}

			var form = new WWWForm();
			form.AddField("tag", "avatargallery");
			form.AddField("galleryId", avatarId);
			form.AddBinaryData("file", imageBytes, fileName, "image/png");

			var url = BuildRequestUrl("/file/image");
			if (string.IsNullOrEmpty(url))
				return false;

			var request = UnityWebRequest.Post(url, form);
			ApplyAuthHeaders(request);

			await SendRequestAsync(request);

			var success = request.result == UnityWebRequest.Result.Success;
			if (!success)
			{
				Debug.LogWarning($"[AvatarUploader] Upload gallery image failed: {request.error}");
			}

			request.Dispose();
			return success;
		}

		public static async Task<Texture2D> DownloadImageAsync(string url)
		{
			if (string.IsNullOrEmpty(url))
				return null;

			var request = UnityWebRequestTexture.GetTexture(url);
			ApplyAuthHeaders(request);
			await SendRequestAsync(request);

			if (request.result != UnityWebRequest.Result.Success)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to download gallery texture: {request.error}");
				return null;
			}

			var texture = DownloadHandlerTexture.GetContent(request);
			request.Dispose();
			return texture;
		}

		private static string BuildRequestUrl(string path)
		{
			var apiKey = GetApiKey();
			if (string.IsNullOrEmpty(apiKey))
			{
				return null;
			}

			if (!path.Contains("apiKey"))
			{
				var separator = path.Contains("?") ? "&" : "?";
				path = $"{path}{separator}apiKey={apiKey}";
			}
			return ApiBaseUrl + path;
		}

		private static void ApplyAuthHeaders(UnityWebRequest request)
		{
			var token = GetAuthToken();
			if (!string.IsNullOrEmpty(token))
			{
				request.SetRequestHeader("Cookie", $"auth={token}");
			}

			request.SetRequestHeader("User-Agent", "YUCP-AvatarUploader/1.0");
		}

		private static async Task SendRequestAsync(UnityWebRequest request)
		{
			var operation = request.SendWebRequest();
			while (!operation.isDone)
			{
				await Task.Delay(20);
			}
		}

		private static string GetAuthToken()
		{
			try
			{
				var apiUserType = Type.GetType("VRC.Core.APIUser, VRCSDKBase");
				if (apiUserType == null)
					return null;

				var currentUserProp = apiUserType.GetProperty("CurrentUser", BindingFlags.Public | BindingFlags.Static);
				var currentUser = currentUserProp?.GetValue(null);
				if (currentUser == null)
					return null;

				var authProp = apiUserType.GetProperty("authToken", BindingFlags.Public | BindingFlags.Instance);
				var token = authProp?.GetValue(currentUser) as string;
				if (!string.IsNullOrEmpty(token))
					return token;

				var authField = apiUserType.GetField("authToken", BindingFlags.NonPublic | BindingFlags.Instance) ??
				                  apiUserType.GetField("authToken", BindingFlags.Public | BindingFlags.Instance);
				return authField?.GetValue(currentUser) as string;
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Unable to access VRChat auth token: {ex.Message}");
				return null;
			}
		}

		public static bool TryGetSdkApiKey(out string apiKey)
		{
			apiKey = null;
			try
			{
				var apiType = Type.GetType("VRC.Core.API, VRCSDKBase") ??
				              AppDomain.CurrentDomain.GetAssemblies()
					              .SelectMany(a =>
					              {
						              try { return a.GetTypes(); }
						              catch (ReflectionTypeLoadException rtl) { return rtl.Types.Where(t => t != null); }
					              })
					              .FirstOrDefault(t => t.FullName == "VRC.Core.API");
				if (apiType == null)
					return false;

				const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
				var property = apiType.GetProperty("ApiKey", flags) ?? apiType.GetProperty("apiKey", flags);
				if (property != null)
				{
					var value = property.GetValue(null) as string;
					if (!string.IsNullOrEmpty(value))
					{
						apiKey = value;
						return true;
					}
				}

				var field = apiType.GetField("ApiKey", flags) ?? apiType.GetField("apiKey", flags) ?? apiType.GetField("api_key", flags);
				if (field != null)
				{
					var value = field.GetValue(null) as string;
					if (!string.IsNullOrEmpty(value))
					{
						apiKey = value;
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Unable to read VRChat API key from SDK: {ex.Message}");
			}

			return false;
		}

		private static string GetApiKey()
		{
			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration)
				return null;

			if (!string.IsNullOrEmpty(_cachedApiKey))
				return _cachedApiKey;

			string key = null;
			if (settings.HasStoredApiKey)
			{
				key = settings.GetApiKey();
			}

			if (string.IsNullOrEmpty(key) && !_apiKeyFetchedFromSdk)
			{
				if (TryGetSdkApiKey(out var sdkKey))
				{
					_apiKeyFetchedFromSdk = true;
					settings.SetApiKey(sdkKey);
					key = sdkKey;
				}
			}
			_cachedApiKey = string.IsNullOrEmpty(key) ? null : key;
			return _cachedApiKey;
		}

		public static async Task<bool> SetAvatarIconAsync(string avatarId, string fileId)
		{
			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration)
				return false;

			if (string.IsNullOrEmpty(avatarId) || string.IsNullOrEmpty(fileId))
				return false;

			var url = BuildRequestUrl($"/avatars/{avatarId}");
			if (string.IsNullOrEmpty(url))
				return false;

			var payload = new AvatarIconPayload
			{
				imageId = fileId,
				thumbnailImageId = fileId
			};

			var json = JsonUtility.ToJson(payload);
			var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT)
			{
				uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
				downloadHandler = new DownloadHandlerBuffer()
			};
			request.SetRequestHeader("Content-Type", "application/json");
			ApplyAuthHeaders(request);

			await SendRequestAsync(request);

			var success = request.result == UnityWebRequest.Result.Success;
			if (!success)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to set avatar icon: {request.error}");
			}

			request.Dispose();
			return success;
		}

		private static string WrapArray(string json)
		{
			if (string.IsNullOrEmpty(json))
				return "{\"entries\":[]}";

			if (json.TrimStart().StartsWith("{"))
				return json;

			var builder = new StringBuilder(json.Length + 20);
			builder.Append("{\"entries\":");
			builder.Append(json);
			builder.Append('}');
			return builder.ToString();
		}

		[Serializable]
		private class FileResponseWrapper
		{
			public FileEntry[] entries;
		}

		[Serializable]
		private class FileEntry
		{
			public string id;
			public FileRecord file;
		}

		[Serializable]
		private class FileRecord
		{
			public string id;
			public VersionInfo[] versions;

			public string GetUrl()
			{
				if (versions == null || versions.Length == 0)
					return null;
				return versions[versions.Length - 1].file?.url;
			}
		}

		[Serializable]
		private class VersionInfo
		{
			public FileRef file;
		}

		[Serializable]
		private class FileRef
		{
			public string url;
		}

		[Serializable]
		private class AvatarIconPayload
		{
			public string imageId;
			public string thumbnailImageId;
		}
	}
}
