using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Represents a build log entry for an avatar build operation.
	/// </summary>
	[Serializable]
	public class BuildLog
	{
		public string avatarName;
		public string profileName;
		public string platform;
		public DateTime buildTime;
		public bool success;
		public string errorMessage;
		public float buildTimeSeconds;
		public string outputPath;
		public List<string> warnings = new List<string>();
		public BuildMetrics metrics = new BuildMetrics();
	}

	[Serializable]
	public class BuildMetrics
	{
		public int polyCount;
		public int materialCount;
		public int textureCount;
		public long fileSizeBytes;
		public PerformanceRating performanceRating;
	}
}






