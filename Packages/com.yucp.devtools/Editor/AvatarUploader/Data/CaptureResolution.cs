using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Resolution presets for avatar capture.
	/// </summary>
	public enum CaptureResolution
	{
		VRChatStandard,  // 512x512
		HighQuality,     // 1024x1024
		Square1024,      // 1024x1024
		Square2048,      // 2048x2048
		Custom
	}

	/// <summary>
	/// Helper class for CaptureResolution enum.
	/// </summary>
	public static class CaptureResolutionHelper
	{
		/// <summary>
		/// Get width and height for a resolution preset.
		/// </summary>
		public static void GetDimensions(CaptureResolution resolution, out int width, out int height)
		{
			switch (resolution)
			{
				case CaptureResolution.VRChatStandard:
					width = 512;
					height = 512;
					break;
				case CaptureResolution.HighQuality:
				case CaptureResolution.Square1024:
					width = 1024;
					height = 1024;
					break;
				case CaptureResolution.Square2048:
					width = 2048;
					height = 2048;
					break;
				default:
					width = 512;
					height = 512;
					break;
			}
		}

		/// <summary>
		/// Get resolution as Vector2 (width, height).
		/// </summary>
		public static Vector2 GetResolution(CaptureResolution resolution)
		{
			GetDimensions(resolution, out int width, out int height);
			return new Vector2(width, height);
		}
	}
}

