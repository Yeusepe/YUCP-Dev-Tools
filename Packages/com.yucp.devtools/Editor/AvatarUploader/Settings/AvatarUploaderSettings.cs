using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	public class AvatarUploaderSettings : ScriptableObject
	{
		private const string SettingsAssetPath = "Assets/Editor/YUCPAvatarToolsSettings.asset";

		private static AvatarUploaderSettings _instance;
		public static AvatarUploaderSettings Instance => _instance ??= LoadOrCreateSettings();

		[SerializeField] private bool autoUploadAfterBuild = false;
		[SerializeField] private bool showBuildNotifications = true;
		[SerializeField] private bool showUploadNotifications = true;
		[SerializeField] private bool defaultBuildPC = true;
		[SerializeField] private bool defaultBuildQuest = true;
		[SerializeField] private int maxLogEntries = 1000;
		[SerializeField] private bool saveBuildLogs = true;
		[SerializeField] private bool saveUploadLogs = true;
		[SerializeField] private bool enableParallelBuilds = false;
		[SerializeField] private int maxParallelBuilds = 2;
		[SerializeField] private bool enableBuildCaching = true;
		[SerializeField] private bool enableGalleryIntegration = false;
		[SerializeField] private bool disableCursorTracking = false;
		[SerializeField] private bool useLowSpecMode = false;

		public bool AutoUploadAfterBuild { get => autoUploadAfterBuild; set => autoUploadAfterBuild = value; }
		public bool ShowBuildNotifications { get => showBuildNotifications; set => showBuildNotifications = value; }
		public bool ShowUploadNotifications { get => showUploadNotifications; set => showUploadNotifications = value; }
		public bool DefaultBuildPC { get => defaultBuildPC; set => defaultBuildPC = value; }
		public bool DefaultBuildQuest { get => defaultBuildQuest; set => defaultBuildQuest = value; }
		public int MaxLogEntries { get => maxLogEntries; set => maxLogEntries = Mathf.Clamp(value, 100, 5000); }
		public bool SaveBuildLogs { get => saveBuildLogs; set => saveBuildLogs = value; }
		public bool SaveUploadLogs { get => saveUploadLogs; set => saveUploadLogs = value; }
		public bool EnableParallelBuilds { get => enableParallelBuilds; set => enableParallelBuilds = value; }
		public int MaxParallelBuilds { get => maxParallelBuilds; set => maxParallelBuilds = Mathf.Clamp(value, 1, 8); }
		public bool EnableBuildCaching { get => enableBuildCaching; set => enableBuildCaching = value; }
		public bool EnableGalleryIntegration { get => enableGalleryIntegration; set => enableGalleryIntegration = value; }
		public bool DisableCursorTracking { get => disableCursorTracking; set => disableCursorTracking = value; }
		public bool UseLowSpecMode { get => useLowSpecMode; set => useLowSpecMode = value; }

		public void Save()
		{
			MarkDirty();
		}

		private static AvatarUploaderSettings LoadOrCreateSettings()
		{
			var normalizedPath = SettingsAssetPath.Replace("\\", "/");
			var settings = AssetDatabase.LoadAssetAtPath<AvatarUploaderSettings>(normalizedPath);
			if (settings != null)
				return settings;

			var dir = System.IO.Path.GetDirectoryName(normalizedPath)?.Replace("\\", "/");
			if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
			{
				var segments = dir.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
				var path = segments[0];
				for (int i = 1; i < segments.Length; i++)
				{
					var next = path + "/" + segments[i];
					if (!AssetDatabase.IsValidFolder(next))
					{
						AssetDatabase.CreateFolder(path, segments[i]);
					}
					path = next;
				}
			}

			settings = CreateInstance<AvatarUploaderSettings>();
			AssetDatabase.CreateAsset(settings, normalizedPath);
			AssetDatabase.SaveAssets();
			return settings;
		}

		private void MarkDirty()
		{
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
		}
	}
}

