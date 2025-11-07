using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Tracks upload history for avatars.
	/// </summary>
	[Serializable]
	public class UploadHistory
	{
		[SerializeField] private List<UploadHistoryEntry> entries = new List<UploadHistoryEntry>();

		public void AddEntry(UploadHistoryEntry entry)
		{
			entries.Add(entry);
			if (entries.Count > 1000)
			{
				entries.RemoveAt(0);
			}
		}

		public List<UploadHistoryEntry> GetEntries(int maxCount = 100)
		{
			var sorted = new List<UploadHistoryEntry>(entries);
			sorted.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));
			return sorted.GetRange(0, Mathf.Min(maxCount, sorted.Count));
		}

		public List<UploadHistoryEntry> GetEntriesForAvatar(string avatarName, int maxCount = 50)
		{
			var filtered = entries.FindAll(e => e.avatarName == avatarName);
			filtered.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));
			return filtered.GetRange(0, Mathf.Min(maxCount, filtered.Count));
		}
	}

	[Serializable]
	public class UploadHistoryEntry
	{
		public string avatarName;
		public string platform;
		public string blueprintId;
		public bool success;
		public string errorMessage;
		public DateTime timestamp;
		public float uploadTimeSeconds;
		public string buildPath;
	}
}

