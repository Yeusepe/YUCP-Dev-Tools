using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	internal static class AvatarUploaderPreferences
	{
		private const string PrefPrefix = "YUCP.AvatarUploader.";
		private const string EmbedKey = PrefPrefix + "EmbeddedControlPanel";
		private const string VerboseKey = PrefPrefix + "VerboseLogs";

		public static bool ShowEmbeddedControlPanel
		{
			get => EditorPrefs.GetBool(EmbedKey, true);
			set => EditorPrefs.SetBool(EmbedKey, value);
		}

		public static bool VerboseLogging
		{
			get => EditorPrefs.GetBool(VerboseKey, false);
			set => EditorPrefs.SetBool(VerboseKey, value);
		}

		[SettingsProvider]
		public static SettingsProvider CreatePreferences()
		{
			return new SettingsProvider("Preferences/YUCP Avatar Tools", SettingsScope.User)
			{
				guiHandler = _ =>
				{
					EditorGUILayout.LabelField("Control Panel Integration", EditorStyles.boldLabel);
					ShowEmbeddedControlPanel = EditorGUILayout.ToggleLeft(
						"Embed Control Panel Builder UI",
						ShowEmbeddedControlPanel);

					EditorGUILayout.Space();
					EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
					VerboseLogging = EditorGUILayout.ToggleLeft(
						"Verbose bridge logging in Avatar Tools window",
						VerboseLogging);

					EditorGUILayout.HelpBox("Disable embedding if the VRChat SDK UI changes or if you prefer to pop out the official Control Panel.", MessageType.Info);
				}
			};
		}
	}
}


