using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageSigning.UI
{
    internal static class SigningProfileAvatar
    {
        private const string AvatarImageName = "yucp-signing-profile-avatar-image";
        private static readonly Dictionary<string, Texture2D> TextureCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Task<Texture2D>> PendingDownloads = new Dictionary<string, Task<Texture2D>>(StringComparer.Ordinal);

        public static VisualElement Create(
            string displayName,
            string imageUrl,
            float size,
            float borderWidth,
            Color backgroundColor,
            Color borderColor,
            Color textColor,
            int fontSize)
        {
            var avatar = new VisualElement();
            avatar.style.position = Position.Relative;
            avatar.style.width = size;
            avatar.style.height = size;
            avatar.style.overflow = Overflow.Hidden;
            avatar.style.alignItems = Align.Center;
            avatar.style.justifyContent = Justify.Center;
            avatar.style.backgroundColor = backgroundColor;
            avatar.style.borderTopWidth = borderWidth;
            avatar.style.borderRightWidth = borderWidth;
            avatar.style.borderBottomWidth = borderWidth;
            avatar.style.borderLeftWidth = borderWidth;
            avatar.style.borderTopColor = borderColor;
            avatar.style.borderRightColor = borderColor;
            avatar.style.borderBottomColor = borderColor;
            avatar.style.borderLeftColor = borderColor;

            float radius = size * 0.5f;
            avatar.style.borderTopLeftRadius = radius;
            avatar.style.borderTopRightRadius = radius;
            avatar.style.borderBottomLeftRadius = radius;
            avatar.style.borderBottomRightRadius = radius;

            var fallbackLabel = new Label(GetInitial(displayName));
            fallbackLabel.style.fontSize = fontSize;
            fallbackLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            fallbackLabel.style.color = textColor;
            fallbackLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            avatar.Add(fallbackLabel);

            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                _ = ApplyImageAsync(avatar, fallbackLabel, imageUrl, size);
            }

            return avatar;
        }

        private static async Task ApplyImageAsync(VisualElement avatar, Label fallbackLabel, string imageUrl, float size)
        {
            Texture2D texture = await GetOrDownloadTextureAsync(imageUrl);
            if (texture == null)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (avatar == null || avatar.panel == null)
                {
                    return;
                }

                SetAvatarImage(avatar, fallbackLabel, texture, size);
            };
        }

        private static async Task<Texture2D> GetOrDownloadTextureAsync(string imageUrl)
        {
            if (TextureCache.TryGetValue(imageUrl, out Texture2D cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            if (PendingDownloads.TryGetValue(imageUrl, out Task<Texture2D> pendingTask))
            {
                return await pendingTask;
            }

            Task<Texture2D> downloadTask = DownloadTextureAsync(imageUrl);
            PendingDownloads[imageUrl] = downloadTask;

            try
            {
                Texture2D texture = await downloadTask;
                if (texture != null)
                {
                    TextureCache[imageUrl] = texture;
                }

                return texture;
            }
            finally
            {
                PendingDownloads.Remove(imageUrl);
            }
        }

        private static async Task<Texture2D> DownloadTextureAsync(string imageUrl)
        {
            using var request = UnityWebRequestTexture.GetTexture(imageUrl);
            request.timeout = 10;
            request.SetRequestHeader("User-Agent", "YUCP Unity Package Exporter");
            request.SetRequestHeader("Accept-Encoding", "identity");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to download profile avatar ({request.responseCode}): {request.error}");
                return null;
            }

            return DownloadHandlerTexture.GetContent(request);
        }

        private static void SetAvatarImage(VisualElement avatar, Label fallbackLabel, Texture2D texture, float size)
        {
            var image = avatar.Q<Image>(AvatarImageName);
            if (image == null)
            {
                image = new Image
                {
                    name = AvatarImageName,
                    scaleMode = ScaleMode.ScaleAndCrop,
                    pickingMode = PickingMode.Ignore,
                };
                image.style.position = Position.Absolute;
                image.style.left = 0;
                image.style.top = 0;
                image.style.width = size;
                image.style.height = size;

                float radius = size * 0.5f;
                image.style.borderTopLeftRadius = radius;
                image.style.borderTopRightRadius = radius;
                image.style.borderBottomLeftRadius = radius;
                image.style.borderBottomRightRadius = radius;

                avatar.Add(image);
            }

            image.image = texture;
            fallbackLabel.style.display = DisplayStyle.None;
        }

        private static string GetInitial(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return "C";
            }

            string trimmed = displayName.Trim();
            return trimmed.Substring(0, 1).ToUpperInvariant();
        }
    }
}
