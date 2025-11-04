using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Profile that groups multiple avatars and global settings for batch building/uploading.
	/// </summary>
	[CreateAssetMenu(fileName = "New Avatar Upload Profile", menuName = "YUCP/Avatar Upload Profile", order = 110)]
	public class AvatarUploadProfile : ScriptableObject
	{
		[Header("Profile Settings")]
		public string profileName;

		[Tooltip("Avatars included in this profile")] public List<AvatarBuildConfig> avatars = new List<AvatarBuildConfig>();

		[Header("Build Settings")]
		public bool autoBuildPC = true;
		public bool autoBuildQuest = true;
		public ValidationLevel validationLevel = ValidationLevel.Normal;

		[Header("Statistics (Read-only)")]
		[SerializeField] private string lastBuildTime;
		[SerializeField] private int buildCount;

		public string LastBuildTime => lastBuildTime;
		public int BuildCount => buildCount;

		public void RecordBuild()
		{
			lastBuildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
			buildCount++;
		}
	}

	public enum ValidationLevel
	{
		Lenient,
		Normal,
		Strict
	}
}


