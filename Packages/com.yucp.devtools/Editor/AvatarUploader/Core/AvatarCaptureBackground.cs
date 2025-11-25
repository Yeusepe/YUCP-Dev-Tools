using System;
using UnityEngine;
using YUCP.DevTools.Editor.AvatarUploader;

namespace YUCP.DevTools.Editor.AvatarUploader.Core
{
	/// <summary>
	/// Background type for avatar capture.
	/// </summary>
	public enum BackgroundType
	{
		Transparent,
		SolidColor,
		Gradient,
		CustomTexture
	}

	/// <summary>
	/// Background controller for avatar capture.
	/// </summary>
	public class AvatarCaptureBackground : IDisposable
	{
		private BackgroundType _type = BackgroundType.Transparent;
		private Color _color1 = Color.clear;
		private Color _color2 = Color.white;
		private Texture2D _customTexture;

		/// <summary>
		/// Set background type.
		/// </summary>
		public void SetType(BackgroundType type)
		{
			_type = type;
		}

		/// <summary>
		/// Set solid color (for SolidColor type).
		/// </summary>
		public void SetColor(Color color)
		{
			_color1 = color;
		}

		/// <summary>
		/// Set gradient colors (for Gradient type).
		/// </summary>
		public void SetGradient(Color color1, Color color2)
		{
			_color1 = color1;
			_color2 = color2;
		}

		/// <summary>
		/// Set custom texture (for CustomTexture type).
		/// </summary>
		public void SetCustomTexture(Texture2D texture)
		{
			_customTexture = texture;
		}

		/// <summary>
		/// Apply background to camera.
		/// </summary>
		public void ApplyToCamera(Camera camera)
		{
			if (camera == null)
				return;

			switch (_type)
			{
				case BackgroundType.Transparent:
					camera.clearFlags = CameraClearFlags.SolidColor;
					camera.backgroundColor = Color.clear;
					break;
				case BackgroundType.SolidColor:
					camera.clearFlags = CameraClearFlags.SolidColor;
					camera.backgroundColor = _color1;
					break;
				case BackgroundType.Gradient:
					// For gradient, we'll use solid color as a fallback
					// Full gradient support would require shader/post-processing
					camera.clearFlags = CameraClearFlags.SolidColor;
					camera.backgroundColor = Color.Lerp(_color1, _color2, 0.5f);
					break;
				case BackgroundType.CustomTexture:
					// Custom texture would require a skybox or post-processing
					camera.clearFlags = CameraClearFlags.SolidColor;
					camera.backgroundColor = Color.gray;
					break;
			}
		}

		/// <summary>
		/// Apply background settings from a preset.
		/// </summary>
		public void ApplyPreset(BackgroundSettings settings)
		{
			if (settings == null)
				return;

			_type = settings.type;
			_color1 = settings.color1;
			_color2 = settings.color2;
			_customTexture = settings.customTexture;
		}

		/// <summary>
		/// Get current background settings.
		/// </summary>
		public BackgroundSettings GetSettings()
		{
			return new BackgroundSettings
			{
				type = _type,
				color1 = _color1,
				color2 = _color2,
				customTexture = _customTexture
			};
		}

		public void Dispose()
		{
			// Nothing to dispose
		}
	}

	/// <summary>
	/// Background settings for presets.
	/// </summary>
	[Serializable]
	public class BackgroundSettings
	{
		public BackgroundType type = BackgroundType.Transparent;
		public Color color1 = Color.clear;
		public Color color2 = Color.white;
		public Texture2D customTexture;
	}
}

