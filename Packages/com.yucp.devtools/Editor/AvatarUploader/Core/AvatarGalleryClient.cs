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

		public static async Task<IReadOnlyList<AvatarGalleryImage>> GetGalleryAsync(string avatarId)
		{
			Debug.Log($"[AvatarUploader] GetGalleryAsync: Called with avatarId: {avatarId}");
			
			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration)
			{
				Debug.LogWarning("[AvatarUploader] GetGalleryAsync: Gallery integration is disabled");
				return Array.Empty<AvatarGalleryImage>();
			}

			if (string.IsNullOrEmpty(avatarId))
			{
				Debug.LogWarning("[AvatarUploader] GetGalleryAsync: avatarId is null or empty");
				return Array.Empty<AvatarGalleryImage>();
			}

			var query = $"/files?tag=avatargallery&galleryId={avatarId}&n=100&offset=0";
			var url = BuildRequestUrl(query);
			Debug.Log($"[AvatarUploader] GetGalleryAsync: Request URL: {url}");
			
			if (string.IsNullOrEmpty(url))
			{
				Debug.LogError("[AvatarUploader] GetGalleryAsync: Failed to build request URL");
				return Array.Empty<AvatarGalleryImage>();
			}

			var request = UnityWebRequest.Get(url);
			ApplyAuthHeaders(request);

			Debug.Log("[AvatarUploader] GetGalleryAsync: Sending request...");
			await SendRequestAsync(request);

			if (request.result != UnityWebRequest.Result.Success)
			{
				Debug.LogError($"[AvatarUploader] GetGalleryAsync: Request failed - {request.error}, Response code: {request.responseCode}, Response: {request.downloadHandler?.text ?? "null"}");
				request.Dispose();
				return Array.Empty<AvatarGalleryImage>();
			}

			try
			{
				var response = request.downloadHandler.text;
				var responseLength = response != null ? response.Length : 0;
				var responsePreview = response != null ? response.Substring(0, Math.Min(500, response.Length)) : "null";
				Debug.Log($"[AvatarUploader] GetGalleryAsync: Received response (length: {responseLength}): {responsePreview}");
				
				// The API returns an array of file objects directly: [{"id":"...","versions":[...]}, ...]
				// Try parsing as direct array first
				FileEntry[] entries = null;
				try
				{
					// Try parsing as direct array
					entries = JsonUtility.FromJson<FileEntry[]>(response);
					Debug.Log($"[AvatarUploader] GetGalleryAsync: Parsed as direct array, got {entries?.Length ?? 0} entries");
				}
				catch (Exception ex1)
				{
					Debug.Log($"[AvatarUploader] GetGalleryAsync: Direct array parse failed: {ex1.Message}, trying wrapped format");
					// Fallback to wrapped format
					try
					{
				var wrapper = JsonUtility.FromJson<FileResponseWrapper>(WrapArray(response));
						entries = wrapper?.entries;
						Debug.Log($"[AvatarUploader] GetGalleryAsync: Parsed as wrapped format, got {entries?.Length ?? 0} entries");
					}
					catch (Exception ex2)
					{
						Debug.LogError($"[AvatarUploader] GetGalleryAsync: Both parsing methods failed. Direct: {ex1.Message}, Wrapped: {ex2.Message}");
					}
				}
				
				if (entries == null || entries.Length == 0)
				{
					Debug.LogWarning($"[AvatarUploader] GetGalleryAsync: No entries found in response");
					return Array.Empty<AvatarGalleryImage>();
				}

				Debug.Log($"[AvatarUploader] GetGalleryAsync: Processing {entries.Length} entries");
				var list = new List<AvatarGalleryImage>();
				foreach (var entry in entries)
				{
					if (entry == null)
					{
						Debug.LogWarning("[AvatarUploader] GetGalleryAsync: Skipping null entry");
						continue;
					}

					Debug.Log($"[AvatarUploader] GetGalleryAsync: Processing entry id: {entry.id}, has versions: {entry.versions != null && entry.versions.Length > 0}, has file: {entry.file != null}");

					// The response structure has versions array directly in the entry
					string imageUrl = null;
					string fileId = entry.id; // The entry id IS the file id
					
					if (entry.versions != null && entry.versions.Length > 0)
				{
						// Entry has versions directly - find the last version with a file.url
						for (int i = entry.versions.Length - 1; i >= 0; i--)
						{
							var version = entry.versions[i];
							if (version != null && version.file != null && !string.IsNullOrEmpty(version.file.url))
							{
								imageUrl = version.file.url;
								Debug.Log($"[AvatarUploader] GetGalleryAsync: Found URL in entry.versions[{i}]: {imageUrl}");
								break;
							}
						}
					}
					
					if (string.IsNullOrEmpty(imageUrl) && entry.file != null)
					{
						// Fallback: Entry has file nested (old format)
						imageUrl = entry.file.GetUrl();
						fileId = entry.file.id ?? entry.id;
						Debug.Log($"[AvatarUploader] GetGalleryAsync: Found URL in entry.file: {imageUrl ?? "null"}");
					}
					
					if (string.IsNullOrEmpty(imageUrl))
					{
						Debug.LogWarning($"[AvatarUploader] GetGalleryAsync: No URL found for entry id: {entry.id}");
						continue;
					}

					Debug.Log($"[AvatarUploader] GetGalleryAsync: Adding gallery image - id: {entry.id}, fileId: {fileId}, url: {imageUrl}");

					list.Add(new AvatarGalleryImage
					{
						id = entry.id,
						fileId = fileId,
						url = imageUrl
					});
				}

				Debug.Log($"[AvatarUploader] GetGalleryAsync: Returning {list.Count} gallery images");
				return list;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AvatarUploader] GetGalleryAsync: Exception parsing response: {ex.Message}\n{ex.StackTrace}");
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
			if (!settings.EnableGalleryIntegration)
			{
				Debug.LogWarning("[AvatarUploader] Gallery integration is disabled.");
				return false;
			}

			if (string.IsNullOrEmpty(avatarId) || imageBytes == null || imageBytes.Length == 0)
			{
				Debug.LogWarning("[AvatarUploader] Invalid parameters for gallery upload.");
				return false;
			}

			// According to VRCX: POST /file/image with multipart/form-data
			// VRCX sends: postData (JSON) and imageData (base64) as separate parts
			// The API expects tag and galleryId as form fields
			var form = new WWWForm();
			
			// Add required fields - API expects these as form fields
			form.AddField("tag", "avatargallery");
			form.AddField("galleryId", avatarId);
			
			// Add image file - VRCX expects the image in "file" field
			form.AddBinaryData("file", imageBytes, fileName, "image/png");

			var url = BuildRequestUrl("/file/image");
			if (string.IsNullOrEmpty(url))
			{
				Debug.LogWarning("[AvatarUploader] Failed to build gallery upload URL.");
				return false;
			}

			var request = UnityWebRequest.Post(url, form);
			// WWWForm automatically sets Content-Type with boundary, but we need to ensure it's correct
			// Remove the Content-Type header that WWWForm sets and let Unity handle it
			ApplyAuthHeaders(request);

			await SendRequestAsync(request);

			var success = request.result == UnityWebRequest.Result.Success;
			if (!success)
			{
				var errorMessage = request.error;
				var responseText = request.downloadHandler?.text;
				Debug.LogError($"[AvatarUploader] Upload gallery image failed: {errorMessage}");
				if (!string.IsNullOrEmpty(responseText))
				{
					Debug.LogError($"[AvatarUploader] Response: {responseText}");
				}
			}

			request.Dispose();
			return success;
		}

		public static async Task<bool> DeleteGalleryImageAsync(string fileId)
		{
			var settings = AvatarUploaderSettings.Instance;
			if (!settings.EnableGalleryIntegration)
			{
				Debug.LogWarning("[AvatarUploader] Gallery integration is disabled.");
				return false;
			}

			if (string.IsNullOrEmpty(fileId))
			{
				Debug.LogWarning("[AvatarUploader] Invalid file ID for gallery delete.");
				return false;
			}

			// According to VRCX: DELETE /file/{fileId}
			var url = BuildRequestUrl($"/file/{fileId}");
			if (string.IsNullOrEmpty(url))
			{
				Debug.LogWarning("[AvatarUploader] Failed to build gallery delete URL.");
				return false;
			}

			var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbDELETE)
			{
				downloadHandler = new DownloadHandlerBuffer()
			};
			ApplyAuthHeaders(request);

			await SendRequestAsync(request);

			var success = request.result == UnityWebRequest.Result.Success;
			if (!success)
			{
				var errorMessage = request.error;
				var responseText = request.downloadHandler?.text;
				Debug.LogError($"[AvatarUploader] Delete gallery image failed: {errorMessage}");
				if (!string.IsNullOrEmpty(responseText))
				{
					Debug.LogError($"[AvatarUploader] Response: {responseText}");
				}
			}

			request.Dispose();
			return success;
		}

		public static async Task<Texture2D> DownloadImageAsync(string url)
		{
			Debug.Log($"[AvatarUploader] DownloadImageAsync: Called with URL: {url}");
			
			if (string.IsNullOrEmpty(url))
			{
				Debug.LogWarning("[AvatarUploader] DownloadImageAsync: URL is null or empty");
				return null;
			}

			var request = UnityWebRequestTexture.GetTexture(url);
			ApplyAuthHeaders(request);
			
			Debug.Log("[AvatarUploader] DownloadImageAsync: Sending texture request...");
			await SendRequestAsync(request);

			if (request.result != UnityWebRequest.Result.Success)
			{
				Debug.LogError($"[AvatarUploader] DownloadImageAsync: Request failed - {request.error}, Response code: {request.responseCode}");
				request.Dispose();
				return null;
			}

			var texture = DownloadHandlerTexture.GetContent(request);
			Debug.Log($"[AvatarUploader] DownloadImageAsync: Downloaded texture - {(texture != null ? $"{texture.width}x{texture.height}" : "null")}");
			request.Dispose();
			return texture;
		}

		private static string BuildRequestUrl(string path)
		{
			// Don't add apiKey - use cookie-based authentication instead
			return ApiBaseUrl + path;
		}

		private static void ApplyAuthHeaders(UnityWebRequest request)
		{
			var cookieHeader = BuildCookieHeader();
			if (!string.IsNullOrEmpty(cookieHeader))
			{
				request.SetRequestHeader("Cookie", cookieHeader);
			}

			// Use the same headers as VRCApi to avoid "client-derived token" errors
			request.SetRequestHeader("User-Agent", "VRC.Core.BestHTTP");
			request.SetRequestHeader("Accept", "application/json");
			
			// Try to get SDK headers via reflection
			try
			{
				// Get API.DeviceID
				var apiType = Type.GetType("VRC.Core.API, VRCSDKBase") ??
				              Type.GetType("VRC.Core.API, VRCSDKBase-Editor");
				if (apiType != null)
				{
					var deviceIdProperty = apiType.GetProperty("DeviceID", BindingFlags.Public | BindingFlags.Static);
					if (deviceIdProperty != null)
					{
						var deviceId = deviceIdProperty.GetValue(null) as string;
						if (!string.IsNullOrEmpty(deviceId))
						{
							request.SetRequestHeader("X-MacAddress", deviceId);
						}
					}
				}

				// Get Tools.SdkVersion and Tools.Platform
				var toolsType = Type.GetType("VRC.Core.Tools, VRCSDKBase") ??
				                Type.GetType("VRC.Core.Tools, VRCSDKBase-Editor");
				if (toolsType != null)
				{
					var sdkVersionProperty = toolsType.GetProperty("SdkVersion", BindingFlags.Public | BindingFlags.Static);
					if (sdkVersionProperty != null)
					{
						var sdkVersion = sdkVersionProperty.GetValue(null) as string;
						if (!string.IsNullOrEmpty(sdkVersion))
						{
							request.SetRequestHeader("X-SDK-Version", sdkVersion);
						}
					}

					var platformProperty = toolsType.GetProperty("Platform", BindingFlags.Public | BindingFlags.Static);
					if (platformProperty != null)
					{
						var platform = platformProperty.GetValue(null) as string;
						if (!string.IsNullOrEmpty(platform))
			{
							request.SetRequestHeader("X-Platform", platform);
						}
					}
				}

				// X-Unity-Version - Unity manages this automatically, don't set it manually
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to set SDK headers: {ex.Message}");
			}
		}

		private static string BuildCookieHeader()
		{
			var cookies = new List<string>();

			try
			{
				// Try multiple assembly names
				var apiCredentialsType = Type.GetType("VRC.Core.ApiCredentials, VRCSDKBase") ??
				                         Type.GetType("VRC.Core.ApiCredentials, VRCSDKBase-Editor") ??
				                         Type.GetType("VRC.Core.ApiCredentials, VRCCore-Editor");
				
				// If still not found, search through all loaded assemblies
				if (apiCredentialsType == null)
				{
					foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
					{
						try
						{
							apiCredentialsType = assembly.GetType("VRC.Core.ApiCredentials");
							if (apiCredentialsType != null)
								break;
						}
						catch
						{
							// Continue searching
						}
					}
				}
				
				if (apiCredentialsType == null)
				{
					Debug.LogWarning("[AvatarUploader] ApiCredentials type not found in any assembly.");
					return null;
				}

				// Load credentials if not loaded
				var isLoadedMethod = apiCredentialsType.GetMethod("IsLoaded", BindingFlags.Public | BindingFlags.Static);
				if (isLoadedMethod != null && !(bool)isLoadedMethod.Invoke(null, null))
				{
					var loadMethod = apiCredentialsType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
					loadMethod?.Invoke(null, null);
				}

				// Get auth token cookie
				var getAuthTokenCookieMethod = apiCredentialsType.GetMethod("GetAuthTokenCookie", BindingFlags.Public | BindingFlags.Static);
				if (getAuthTokenCookieMethod != null)
				{
					var authCookie = getAuthTokenCookieMethod.Invoke(null, null);
					if (authCookie != null)
					{
						var nameProperty = authCookie.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
						var valueProperty = authCookie.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
						
						if (nameProperty != null && valueProperty != null)
						{
							var name = nameProperty.GetValue(authCookie) as string;
							var value = valueProperty.GetValue(authCookie) as string;
							
							if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
							{
								cookies.Add($"{name}={value}");
							}
						}
					}
				}

				// Get 2FA token cookie
				var getTwoFactorAuthTokenCookieMethod = apiCredentialsType.GetMethod("GetTwoFactorAuthTokenCookie", BindingFlags.Public | BindingFlags.Static);
				if (getTwoFactorAuthTokenCookieMethod != null)
				{
					var twoFaCookie = getTwoFactorAuthTokenCookieMethod.Invoke(null, null);
					if (twoFaCookie != null)
					{
						var nameProperty = twoFaCookie.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
						var valueProperty = twoFaCookie.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
						
						if (nameProperty != null && valueProperty != null)
						{
							var name = nameProperty.GetValue(twoFaCookie) as string;
							var value = valueProperty.GetValue(twoFaCookie) as string;
							
							if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
							{
								cookies.Add($"{name}={value}");
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Unable to build cookie header: {ex.Message}");
			}

			return cookies.Count > 0 ? string.Join("; ", cookies) : null;
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
				// Try multiple assembly names
				var apiCredentialsType = Type.GetType("VRC.Core.ApiCredentials, VRCSDKBase") ??
				                         Type.GetType("VRC.Core.ApiCredentials, VRCSDKBase-Editor") ??
				                         Type.GetType("VRC.Core.ApiCredentials, VRCCore-Editor");
				
				// If still not found, search through all loaded assemblies
				if (apiCredentialsType == null)
				{
					foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
					{
						try
						{
							apiCredentialsType = assembly.GetType("VRC.Core.ApiCredentials");
							if (apiCredentialsType != null)
								break;
						}
						catch
						{
							// Continue searching
						}
					}
				}
				
				if (apiCredentialsType == null)
				{
					Debug.LogWarning("[AvatarUploader] ApiCredentials type not found in any assembly. Make sure VRChat SDK is installed.");
					return null;
				}

				// Load credentials if not loaded
				var isLoadedMethod = apiCredentialsType.GetMethod("IsLoaded", BindingFlags.Public | BindingFlags.Static);
				if (isLoadedMethod != null && !(bool)isLoadedMethod.Invoke(null, null))
				{
					var loadMethod = apiCredentialsType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
					loadMethod?.Invoke(null, null);
				}

				// Get auth token cookie
				var getAuthTokenCookieMethod = apiCredentialsType.GetMethod("GetAuthTokenCookie", BindingFlags.Public | BindingFlags.Static);
				if (getAuthTokenCookieMethod == null)
				{
					Debug.LogWarning("[AvatarUploader] GetAuthTokenCookie method not found.");
					return null;
				}

				var cookie = getAuthTokenCookieMethod.Invoke(null, null);
				if (cookie == null)
				{
					Debug.LogWarning("[AvatarUploader] Auth token cookie is null. Please sign in to VRChat SDK via the Control Panel.");
					return null;
				}

				// Try to get Value property (System.Net.Cookie has Value property)
				var valueProperty = cookie.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
				if (valueProperty != null)
				{
					var value = valueProperty.GetValue(cookie) as string;
					if (!string.IsNullOrEmpty(value))
					{
						return value;
					}
				}

				// Fallback: try to get as string directly if it's already a string
				if (cookie is string str && !string.IsNullOrEmpty(str))
				{
					return str;
				}

				Debug.LogWarning($"[AvatarUploader] Unable to extract auth token value from cookie. Cookie type: {cookie.GetType().FullName}");
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Unable to access VRChat auth token: {ex.Message}\n{ex.StackTrace}");
			}
				return null;
		}

		private static string GetTwoFactorAuthToken()
		{
			try
			{
				// Try multiple assembly names
				var apiCredentialsType = Type.GetType("VRC.Core.ApiCredentials, VRCSDKBase") ??
				                         Type.GetType("VRC.Core.ApiCredentials, VRCSDKBase-Editor") ??
				                         Type.GetType("VRC.Core.ApiCredentials, VRCCore-Editor");
				
				// If still not found, search through all loaded assemblies
				if (apiCredentialsType == null)
				{
					foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
					{
						try
						{
							apiCredentialsType = assembly.GetType("VRC.Core.ApiCredentials");
							if (apiCredentialsType != null)
								break;
						}
						catch
						{
							// Continue searching
						}
					}
				}
				
				if (apiCredentialsType == null)
					return null;

				// Load credentials if not loaded
				var isLoadedMethod = apiCredentialsType.GetMethod("IsLoaded", BindingFlags.Public | BindingFlags.Static);
				if (isLoadedMethod != null && !(bool)isLoadedMethod.Invoke(null, null))
				{
					var loadMethod = apiCredentialsType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
					loadMethod?.Invoke(null, null);
				}

				// Get 2FA token cookie
				var getTwoFactorAuthTokenCookieMethod = apiCredentialsType.GetMethod("GetTwoFactorAuthTokenCookie", BindingFlags.Public | BindingFlags.Static);
				if (getTwoFactorAuthTokenCookieMethod == null)
					return null;

				var cookie = getTwoFactorAuthTokenCookieMethod.Invoke(null, null);
				if (cookie == null)
					return null;

				// Try to get Value property (System.Net.Cookie has Value property)
				var valueProperty = cookie.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
				if (valueProperty != null)
				{
					var value = valueProperty.GetValue(cookie) as string;
					if (!string.IsNullOrEmpty(value))
					{
						return value;
					}
				}

				// Fallback: try to get as string directly if it's already a string
				if (cookie is string str && !string.IsNullOrEmpty(str))
				{
					return str;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Unable to access VRChat 2FA token: {ex.Message}");
			}
				return null;
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
			public FileRecord file; // Old format: nested file object
			public VersionInfo[] versions; // New format: versions array directly in entry
		}

		[Serializable]
		private class FileRecord
		{
			public string id;
			public VersionInfo[] versions;

			public string GetUrl()
			{
				if (versions == null || versions.Length == 0)
				{
					Debug.LogWarning($"[AvatarUploader] FileRecord.GetUrl: No versions found for file id: {id}");
					return null;
				}
				
				var lastVersion = versions[versions.Length - 1];
				if (lastVersion == null || lastVersion.file == null)
				{
					Debug.LogWarning($"[AvatarUploader] FileRecord.GetUrl: Last version or file is null for file id: {id}");
					return null;
				}
				
				var url = lastVersion.file.url;
				Debug.Log($"[AvatarUploader] FileRecord.GetUrl: Extracted URL for file id {id}: {url ?? "null"}");
				return url;
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
