using UnityEditor;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	public static class PlatformSwitcher
	{
		public enum BuildPlatform
		{
			PC,
			Quest
		}

		public static bool EnsurePlatform(BuildPlatform platform)
		{
			switch (platform)
			{
				case BuildPlatform.PC:
					return SwitchToPC();
				case BuildPlatform.Quest:
					return SwitchToQuest();
			}
			return false;
		}

		public static bool SwitchToPC()
		{
			var current = EditorUserBuildSettings.activeBuildTarget;
			if (current == BuildTarget.StandaloneWindows || current == BuildTarget.StandaloneWindows64)
				return true;

			return EditorUserBuildSettings.SwitchActiveBuildTarget(
				BuildTargetGroup.Standalone,
				BuildTarget.StandaloneWindows64
			);
		}

		public static bool SwitchToQuest()
		{
			var current = EditorUserBuildSettings.activeBuildTarget;
			if (current == BuildTarget.Android)
				return true;

			return EditorUserBuildSettings.SwitchActiveBuildTarget(
				BuildTargetGroup.Android,
				BuildTarget.Android
			);
		}
	}
}


