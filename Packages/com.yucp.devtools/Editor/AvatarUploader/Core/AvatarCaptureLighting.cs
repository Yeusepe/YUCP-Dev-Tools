using System;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader.Core
{
	/// <summary>
	/// Lighting controller for avatar capture with main, fill, and ambient lights.
	/// </summary>
	public class AvatarCaptureLighting : IDisposable
	{
		// Main light (key light)
		private float _mainIntensity = 1.1f;
		private Quaternion _mainRotation = Quaternion.Euler(30f, 45f, 0f);
		private Color _mainColor = Color.white;

		// Fill light
		private float _fillIntensity = 0.75f;
		private Quaternion _fillRotation = Quaternion.Euler(315f, 135f, 0f);
		private Color _fillColor = Color.white;

		// Ambient light
		private float _ambientIntensity = 0.25f;
		private Color _ambientColor = new Color(0.25f, 0.28f, 0.3f, 1f);

		// Rim light (optional)
		private bool _useRimLight = false;
		private float _rimIntensity = 0.5f;
		private Quaternion _rimRotation = Quaternion.Euler(0f, 180f, 0f);
		private Color _rimColor = Color.white;

		/// <summary>
		/// Set main light intensity.
		/// </summary>
		public void SetMainIntensity(float intensity)
		{
			_mainIntensity = Mathf.Max(0f, intensity);
		}

		/// <summary>
		/// Set main light rotation.
		/// </summary>
		public void SetMainRotation(Quaternion rotation)
		{
			_mainRotation = rotation;
		}

		/// <summary>
		/// Set main light color.
		/// </summary>
		public void SetMainColor(Color color)
		{
			_mainColor = color;
		}

		/// <summary>
		/// Set fill light intensity.
		/// </summary>
		public void SetFillIntensity(float intensity)
		{
			_fillIntensity = Mathf.Max(0f, intensity);
		}

		/// <summary>
		/// Set fill light rotation.
		/// </summary>
		public void SetFillRotation(Quaternion rotation)
		{
			_fillRotation = rotation;
		}

		/// <summary>
		/// Set fill light color.
		/// </summary>
		public void SetFillColor(Color color)
		{
			_fillColor = color;
		}

		/// <summary>
		/// Set ambient light intensity.
		/// </summary>
		public void SetAmbientIntensity(float intensity)
		{
			_ambientIntensity = Mathf.Clamp01(intensity);
		}

		/// <summary>
		/// Set ambient light color.
		/// </summary>
		public void SetAmbientColor(Color color)
		{
			_ambientColor = color;
		}

		/// <summary>
		/// Apply lighting to a PreviewRenderUtility (for preview rendering).
		/// </summary>
		public void ApplyToPreviewUtility(PreviewRenderUtility previewUtility)
		{
			if (previewUtility == null)
				return;

			// Setup main light
			if (previewUtility.lights.Length > 0)
			{
				var mainLight = previewUtility.lights[0];
				mainLight.intensity = _mainIntensity;
				mainLight.transform.rotation = _mainRotation;
				mainLight.color = _mainColor;
			}

			// Setup fill light
			if (previewUtility.lights.Length > 1)
			{
				var fillLight = previewUtility.lights[1];
				fillLight.intensity = _fillIntensity;
				fillLight.transform.rotation = _fillRotation;
				fillLight.color = _fillColor;
			}

			// Setup ambient
			previewUtility.ambientColor = _ambientColor * _ambientIntensity;
		}

		/// <summary>
		/// Apply lighting to the scene (for scene camera capture).
		/// Creates temporary lights in the scene.
		/// </summary>
		public void ApplyToScene()
		{
			// For scene camera capture, we'll use the scene's existing lighting
			// or create temporary lights. This is a simplified version.
			// In practice, users should set up lighting in their scene.
			RenderSettings.ambientLight = _ambientColor * _ambientIntensity;
		}

		/// <summary>
		/// Apply lighting settings from a preset.
		/// </summary>
		public void ApplyPreset(LightingSettings settings)
		{
			if (settings == null)
				return;

			_mainIntensity = settings.mainIntensity;
			_mainRotation = settings.mainRotation;
			_mainColor = settings.mainColor;
			_fillIntensity = settings.fillIntensity;
			_fillRotation = settings.fillRotation;
			_fillColor = settings.fillColor;
			_ambientIntensity = settings.ambientIntensity;
			_ambientColor = settings.ambientColor;
			_useRimLight = settings.useRimLight;
			_rimIntensity = settings.rimIntensity;
			_rimRotation = settings.rimRotation;
			_rimColor = settings.rimColor;
		}

		/// <summary>
		/// Get current lighting settings.
		/// </summary>
		public LightingSettings GetSettings()
		{
			return new LightingSettings
			{
				mainIntensity = _mainIntensity,
				mainRotation = _mainRotation,
				mainColor = _mainColor,
				fillIntensity = _fillIntensity,
				fillRotation = _fillRotation,
				fillColor = _fillColor,
				ambientIntensity = _ambientIntensity,
				ambientColor = _ambientColor,
				useRimLight = _useRimLight,
				rimIntensity = _rimIntensity,
				rimRotation = _rimRotation,
				rimColor = _rimColor
			};
		}

		public void Dispose()
		{
			// Nothing to dispose for lighting
		}
	}

	/// <summary>
	/// Lighting settings for presets.
	/// </summary>
	[Serializable]
	public class LightingSettings
	{
		public float mainIntensity = 1.1f;
		public Quaternion mainRotation = Quaternion.Euler(30f, 45f, 0f);
		public Color mainColor = Color.white;
		public float fillIntensity = 0.75f;
		public Quaternion fillRotation = Quaternion.Euler(315f, 135f, 0f);
		public Color fillColor = Color.white;
		public float ambientIntensity = 0.25f;
		public Color ambientColor = new Color(0.25f, 0.28f, 0.3f, 1f);
		public bool useRimLight = false;
		public float rimIntensity = 0.5f;
		public Quaternion rimRotation = Quaternion.Euler(0f, 180f, 0f);
		public Color rimColor = Color.white;
	}
}

