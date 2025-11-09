using System;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Builder;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	public static class AvatarBuilder
	{
		public static UploadResult BuildAvatar(AvatarUploadProfile profile, AvatarBuildConfig config, PlatformSwitcher.BuildPlatform platform, Action<string> onProgress = null)
		{
			var result = new UploadResult { platform = platform == PlatformSwitcher.BuildPlatform.PC ? "PC" : "Quest" };

			// Switch platform
			if (!PlatformSwitcher.EnsurePlatform(platform))
			{
				result.success = false;
				result.errorMessage = "Failed to switch build platform";
				return result;
			}

			// Ensure PipelineManager + blueprint ID
			var targetRoot = config.avatarPrefab;
			var blueprintId = platform == PlatformSwitcher.BuildPlatform.PC ? config.blueprintIdPC : config.blueprintIdQuest;
			if (config.useSameBlueprintId)
			{
				if (string.IsNullOrWhiteSpace(config.blueprintIdPC) && !string.IsNullOrWhiteSpace(config.blueprintIdQuest))
					blueprintId = config.blueprintIdQuest;
				else if (!string.IsNullOrWhiteSpace(config.blueprintIdPC) && string.IsNullOrWhiteSpace(config.blueprintIdQuest))
					blueprintId = config.blueprintIdPC;
			}
			if (!string.IsNullOrWhiteSpace(blueprintId))
			{
				BlueprintManager.SetBlueprintId(targetRoot, blueprintId);
			}

			var builder = new VRCAvatarBuilder();
			string builtPath = null;
			bool buildOk = false;
			DateTime start = DateTime.Now;

			builder.OnBuildProgress += (_, msg) => { onProgress?.Invoke(msg); };
			builder.OnBuildSuccess += (_, path) =>
			{
				buildOk = true;
				builtPath = path;
			};
			builder.OnBuildError += (_, err) =>
			{
				buildOk = false;
				result.errorMessage = err;
			};

			try
			{
				onProgress?.Invoke("Building avatar bundle...");
				buildOk = builder.ExportAvatarBlueprint(targetRoot);
			}
			catch (Exception ex)
			{
				buildOk = false;
				result.errorMessage = ex.Message;
			}

			result.success = buildOk;
			result.outputPath = builtPath;
			result.buildTimeSeconds = (float)(DateTime.Now - start).TotalSeconds;
			return result;
		}
	}
}



