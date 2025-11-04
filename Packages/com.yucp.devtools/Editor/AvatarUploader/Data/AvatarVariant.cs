using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Per-avatar configuration and related enums used by the Avatar Batch Uploader.
	/// </summary>
	[Serializable]
	public class AvatarBuildConfig
	{
		// Avatar Reference
		public GameObject avatarPrefab;

		// Blueprint IDs (one per platform or shared)
		public string blueprintIdPC;
		public string blueprintIdQuest;
		public bool useSameBlueprintId = false;

		// Platform Selection
		public bool buildPC = true;
		public bool buildQuest = true;

		// Avatar Metadata (matches VRChat upload fields)
		public string avatarName;
		[TextArea(3, 6)] public string description;
		public Texture2D avatarIcon;

		// Categories/Tags
		public List<string> tags = new List<string>();
		public AvatarCategory category = AvatarCategory.Generic;

		// Release Settings
		public ReleaseStatus releaseStatus = ReleaseStatus.Private;
		public string version = "1.0";

		// Performance Tracking (auto-calculated)
		[Header("Performance Info (Read-Only)")]
		public int polyCountPC;
		public int polyCountQuest;
		public int materialCount;
		public PerformanceRating performanceRatingPC;
		public PerformanceRating performanceRatingQuest;

		// Build Results
		[Header("Build Status")]
		public BuildStatus lastBuildStatusPC;
		public BuildStatus lastBuildStatusQuest;
		public string lastBuildTime;
	}

	public enum AvatarCategory
	{
		Generic,
		Anime,
		Furry,
		Robot,
		Animal,
		Human,
		Other
	}

	public enum ReleaseStatus
	{
		Private,
		Public
	}

	public enum PerformanceRating
	{
		Excellent,
		Good,
		Medium,
		Poor,
		VeryPoor
	}

	public enum BuildStatus
	{
		NotBuilt,
		Building,
		Success,
		Failed
	}
}


