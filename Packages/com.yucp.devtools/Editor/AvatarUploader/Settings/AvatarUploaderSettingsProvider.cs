using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	public class AvatarToolsSettingsProvider : SettingsProvider
	{
		private static string _apiKeyBuffer = string.Empty;

		public AvatarToolsSettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
			: base(path, scope) { }

		public override void OnGUI(string searchContext)
		{
			var settings = AvatarUploaderSettings.Instance;

			EditorGUI.BeginChangeCheck();

			EditorGUILayout.LabelField("Build Settings", EditorStyles.boldLabel);
			settings.AutoUploadAfterBuild = EditorGUILayout.Toggle("Auto Upload After Build", settings.AutoUploadAfterBuild);
			settings.ShowBuildNotifications = EditorGUILayout.Toggle("Show Build Notifications", settings.ShowBuildNotifications);
			settings.ShowUploadNotifications = EditorGUILayout.Toggle("Show Upload Notifications", settings.ShowUploadNotifications);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Default Settings", EditorStyles.boldLabel);
			settings.DefaultBuildPC = EditorGUILayout.Toggle("Default Build PC", settings.DefaultBuildPC);
			settings.DefaultBuildQuest = EditorGUILayout.Toggle("Default Build Quest", settings.DefaultBuildQuest);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Log Settings", EditorStyles.boldLabel);
			settings.MaxLogEntries = EditorGUILayout.IntField("Max Log Entries", settings.MaxLogEntries);
			settings.SaveBuildLogs = EditorGUILayout.Toggle("Save Build Logs", settings.SaveBuildLogs);
			settings.SaveUploadLogs = EditorGUILayout.Toggle("Save Upload Logs", settings.SaveUploadLogs);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
			settings.EnableParallelBuilds = EditorGUILayout.Toggle("Enable Parallel Builds", settings.EnableParallelBuilds);
			if (settings.EnableParallelBuilds)
			{
				settings.MaxParallelBuilds = EditorGUILayout.IntSlider("Max Parallel Builds", settings.MaxParallelBuilds, 1, 8);
			}
			settings.EnableBuildCaching = EditorGUILayout.Toggle("Enable Build Caching", settings.EnableBuildCaching);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Gallery Integration", EditorStyles.boldLabel);
			settings.EnableGalleryIntegration = EditorGUILayout.ToggleLeft("Enable gallery API requests (requires VRChat API key)", settings.EnableGalleryIntegration);

			using (new EditorGUI.DisabledScope(!settings.EnableGalleryIntegration))
			{
				EditorGUILayout.HelpBox("Avatar gallery uploads call VRChat's REST API. Provide an API key obtained from the SDK's Control Panel. The key is stored using Windows DPAPI under your user account.", MessageType.Info);

				_apiKeyBuffer = EditorGUILayout.PasswordField("API Key", _apiKeyBuffer);
				using (new EditorGUILayout.HorizontalScope())
				{
					if (GUILayout.Button("Save Key", GUILayout.Width(120)))
					{
						settings.SetApiKey(_apiKeyBuffer.Trim());
						_apiKeyBuffer = string.Empty;
					}

					if (GUILayout.Button("Import From Control Panel", GUILayout.Width(210)))
					{
						if (AvatarGalleryClient.TryGetSdkApiKey(out var importedKey) && !string.IsNullOrEmpty(importedKey))
						{
							settings.SetApiKey(importedKey);
							EditorUtility.DisplayDialog("Avatar Tools", "Imported the VRChat API key from the current SDK session.", "OK");
						}
						else
						{
							EditorUtility.DisplayDialog("Avatar Tools", "The VRChat SDK did not expose an API key. Open the Control Panel at least once and log in.", "OK");
						}
					}

					GUI.enabled = settings.HasStoredApiKey;
					if (GUILayout.Button("Clear Stored Key", GUILayout.Width(150)))
					{
						settings.ClearApiKey();
					}
					GUI.enabled = true;
				}

				EditorGUILayout.LabelField(settings.HasStoredApiKey ? "Status: API key stored securely." : "Status: No API key stored.", EditorStyles.wordWrappedMiniLabel);
			}

			if (EditorGUI.EndChangeCheck())
			{
				settings.Save();
			}
		}

		[SettingsProvider]
		public static SettingsProvider CreateSettingsProvider()
		{
			return new AvatarToolsSettingsProvider("Project/YUCP Avatar Tools", SettingsScope.Project);
		}
	}
}

