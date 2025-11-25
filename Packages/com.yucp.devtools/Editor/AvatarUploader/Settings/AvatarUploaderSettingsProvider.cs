using UnityEditor;
using UnityEngine;
using VRC.Core;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	public class AvatarToolsSettingsProvider : SettingsProvider
	{
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
			settings.EnableGalleryIntegration = EditorGUILayout.ToggleLeft("Enable gallery API requests (uses Control Panel session)", settings.EnableGalleryIntegration);

			using (new EditorGUI.DisabledScope(!settings.EnableGalleryIntegration))
			{
				EditorGUILayout.HelpBox("Avatar gallery uploads reuse the official VRChat Control Panel login. Stay signed into the Control Panel before adding or editing gallery items.", MessageType.Info);
				var status = APIUser.IsLoggedIn
					? "Status: Control Panel session detected."
					: "Status: Please log into the VRChat Control Panel.";
				EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
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

