using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// VRChat SDK namespaces (present when SDK is installed in the project)
using VRC.SDK3.Avatars.Components;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Performs pre-build validation for avatars and profiles.
	/// </summary>
	public static class AvatarValidator
	{
		public struct ValidationResult
		{
			public bool isValid;
			public string errorMessage;
			public List<string> warnings;
		}

		public static ValidationResult ValidateAvatarConfig(AvatarUploadProfile profile, AvatarBuildConfig config)
		{
			var result = new ValidationResult { isValid = true, warnings = new List<string>() };

			if (config == null)
			{
				return Fail(ref result, "Configuration is null");
			}

			if (config.avatarPrefab == null)
			{
				return Fail(ref result, "Avatar prefab is not assigned");
			}

			// Prefab must have VRCAvatarDescriptor on root
			var descriptor = config.avatarPrefab.GetComponent<VRCAvatarDescriptor>();
			if (descriptor == null)
			{
				return Fail(ref result, "Avatar prefab is missing VRCAvatarDescriptor on the root object");
			}

			// PipelineManager should be present on root as well (added by SDK normally) – check via reflection
			var pmType = System.Type.GetType("VRC.Core.PipelineManager, VRCSDKBase");
			Component pipeline = null;
			if (pmType != null)
			{
				pipeline = config.avatarPrefab.GetComponent(pmType);
			}
			if (pipeline == null)
			{
				result.warnings.Add("PipelineManager is missing; it will be auto-added by the SDK during build, but ensure blueprint IDs are tracked correctly.");
			}

			// Blueprint IDs
			if (config.useSameBlueprintId)
			{
				if (string.IsNullOrWhiteSpace(config.blueprintIdPC) && string.IsNullOrWhiteSpace(config.blueprintIdQuest))
				{
					result.warnings.Add("Using same blueprint ID but none provided; SDK may generate a new one on upload.");
				}
			}
			else
			{
				if (config.buildPC && string.IsNullOrWhiteSpace(config.blueprintIdPC))
				{
					result.warnings.Add("PC build selected but PC blueprint ID not set.");
				}
				if (config.buildQuest && string.IsNullOrWhiteSpace(config.blueprintIdQuest))
				{
					result.warnings.Add("Quest build selected but Quest blueprint ID not set.");
				}
			}

			// Metadata
			if (string.IsNullOrWhiteSpace(config.avatarName))
			{
				result.warnings.Add("Avatar Name is empty.");
			}

			// Basic platform selections
			if (!config.buildPC && !config.buildQuest)
			{
				return Fail(ref result, "No platforms selected (PC/Quest)");
			}

			// Optional: simple performance hints (editor-time, approximate)
			var renderers = config.avatarPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			int approxTriangles = 0;
			foreach (var r in renderers)
			{
				if (r.sharedMesh != null) approxTriangles += r.sharedMesh.triangles.Length / 3;
			}
			if (approxTriangles > 70000)
			{
				result.warnings.Add("High triangle count detected (>70k). Consider optimization, especially for Quest.");
			}

			return result;
		}

		private static ValidationResult Fail(ref ValidationResult result, string message)
		{
			result.isValid = false;
			result.errorMessage = message;
			return result;
		}
	}
}


