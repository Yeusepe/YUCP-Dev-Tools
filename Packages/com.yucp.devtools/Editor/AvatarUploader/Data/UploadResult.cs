using System;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Result information for avatar build and (future) upload operations.
	/// </summary>
	[Serializable]
	public class UploadResult
	{
		public bool success;
		public string errorMessage;
		public string outputPath; // .vrca path when building succeeds
		public string platform;   // "PC" or "Quest"
		public float buildTimeSeconds;
	}
}



