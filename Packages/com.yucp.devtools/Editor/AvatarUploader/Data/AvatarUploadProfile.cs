using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Collection that groups multiple avatar assets and global settings for batch building/uploading.
	/// Similar to how PackageExporter uses ExportProfile to group packages.
	/// </summary>
	[CreateAssetMenu(fileName = "New Avatar Collection", menuName = "YUCP/Avatar Collection", order = 110)]
	public class AvatarCollection : ScriptableObject
	{
		[Header("Collection Settings")]
		[Tooltip("Display name for this collection")]
		public string collectionName;

		[Tooltip("Avatar assets included in this collection")]
		public List<AvatarAsset> avatars = new List<AvatarAsset>();

		[Header("Build Settings")]
		[Tooltip("Automatically build for PC when building this collection")]
		public bool autoBuildPC = true;
		
		[Tooltip("Automatically build for Quest when building this collection")]
		public bool autoBuildQuest = true;

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
}



