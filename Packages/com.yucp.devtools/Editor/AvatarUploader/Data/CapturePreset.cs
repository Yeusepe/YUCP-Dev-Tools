using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using YUCP.DevTools.Editor.AvatarUploader.Core;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Preset type for default presets.
	/// </summary>
	public enum CapturePresetType
	{
		VRChatStandard,
		ProfessionalHeadshot,
		FullBodyShowcase
	}

	/// <summary>
	/// ScriptableObject for storing avatar capture presets.
	/// </summary>
	[CreateAssetMenu(fileName = "New Capture Preset", menuName = "YUCP/Capture Preset", order = 112)]
	public class CapturePreset : ScriptableObject
	{
		[Header("Capture Settings")]
		public CaptureMode mode = CaptureMode.Headshot;
		public CaptureResolution resolution = CaptureResolution.VRChatStandard;

		[Header("Camera")]
		public CameraSettings cameraSettings = new CameraSettings();

		[Header("Lighting")]
		public Core.LightingSettings lightingSettings = new Core.LightingSettings();

		[Header("Background")]
		public BackgroundSettings backgroundSettings = new BackgroundSettings();

		[Header("Post-Processing")]
		public PostProcessSettings postProcessSettings = new PostProcessSettings();

		/// <summary>
		/// Get a default preset by type.
		/// </summary>
		public static CapturePreset GetDefaultPreset(CapturePresetType type)
		{
			var preset = CreateInstance<CapturePreset>();

			switch (type)
			{
				case CapturePresetType.VRChatStandard:
					preset.mode = CaptureMode.Headshot;
					preset.resolution = CaptureResolution.VRChatStandard;
					preset.cameraSettings = new CameraSettings
					{
						position = Vector3.zero,
						rotation = Vector3.zero,
						fov = 35f,
						distance = 1.5f,
						lookAtTarget = Vector3.zero,
						mode = CaptureMode.Headshot
					};
					preset.lightingSettings = new Core.LightingSettings
					{
						mainIntensity = 1.1f,
						mainRotation = Quaternion.Euler(30f, 45f, 0f),
						mainColor = Color.white,
						fillIntensity = 0.75f,
						fillRotation = Quaternion.Euler(315f, 135f, 0f),
						fillColor = Color.white,
						ambientIntensity = 0.25f,
						ambientColor = new Color(0.25f, 0.28f, 0.3f, 1f)
					};
					preset.backgroundSettings = new BackgroundSettings
					{
						type = BackgroundType.Transparent,
						color1 = Color.clear
					};
					break;

				case CapturePresetType.ProfessionalHeadshot:
					preset.mode = CaptureMode.Headshot;
					preset.resolution = CaptureResolution.HighQuality;
					preset.cameraSettings = new CameraSettings
					{
						position = Vector3.zero,
						rotation = new Vector3(5f, 0f, 0f),
						fov = 30f,
						distance = 1.2f,
						lookAtTarget = Vector3.zero,
						mode = CaptureMode.Headshot
					};
					preset.lightingSettings = new Core.LightingSettings
					{
						mainIntensity = 1.2f,
						mainRotation = Quaternion.Euler(25f, 40f, 0f),
						mainColor = Color.white,
						fillIntensity = 0.6f,
						fillRotation = Quaternion.Euler(320f, 140f, 0f),
						fillColor = new Color(0.95f, 0.95f, 1f),
						ambientIntensity = 0.2f,
						ambientColor = new Color(0.2f, 0.25f, 0.3f, 1f)
					};
					preset.backgroundSettings = new BackgroundSettings
					{
						type = BackgroundType.Transparent,
						color1 = Color.clear
					};
					break;

				case CapturePresetType.FullBodyShowcase:
					preset.mode = CaptureMode.FullBody;
					preset.resolution = CaptureResolution.VRChatStandard;
					preset.cameraSettings = new CameraSettings
					{
						position = Vector3.zero,
						rotation = Vector3.zero,
						fov = 30f,
						distance = 3.5f,
						lookAtTarget = Vector3.zero,
						mode = CaptureMode.FullBody
					};
					preset.lightingSettings = new Core.LightingSettings
					{
						mainIntensity = 1.0f,
						mainRotation = Quaternion.Euler(30f, 45f, 0f),
						mainColor = Color.white,
						fillIntensity = 0.8f,
						fillRotation = Quaternion.Euler(315f, 135f, 0f),
						fillColor = Color.white,
						ambientIntensity = 0.3f,
						ambientColor = new Color(0.25f, 0.28f, 0.3f, 1f)
					};
					preset.backgroundSettings = new BackgroundSettings
					{
						type = BackgroundType.Transparent,
						color1 = Color.clear
					};
					break;
			}

			return preset;
		}

		/// <summary>
		/// Save preset to disk.
		/// </summary>
		public void Save(string path)
		{
			if (string.IsNullOrEmpty(path))
				return;

			// Ensure directory exists
			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}

			AssetDatabase.CreateAsset(this, path);
			AssetDatabase.SaveAssets();
		}

		/// <summary>
		/// Load preset from disk.
		/// </summary>
		public static CapturePreset Load(string path)
		{
			return AssetDatabase.LoadAssetAtPath<CapturePreset>(path);
		}

		/// <summary>
		/// Get all saved presets.
		/// </summary>
		public static List<CapturePreset> GetAllPresets()
		{
			var presets = new List<CapturePreset>();
			var guids = AssetDatabase.FindAssets("t:CapturePreset");
			foreach (var guid in guids)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var preset = AssetDatabase.LoadAssetAtPath<CapturePreset>(path);
				if (preset != null)
					presets.Add(preset);
			}
			return presets;
		}
	}
}

