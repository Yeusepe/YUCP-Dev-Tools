using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Utility for reading and writing Unity .meta files to preserve GUIDs.
	/// </summary>
	public static class MetaFileManager
	{
		/// <summary>
		/// Reads the GUID from a .meta file.
		/// </summary>
		public static string ReadGuid(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath))
				return null;
			
			string metaPath = assetPath + ".meta";
			if (!File.Exists(metaPath))
				return null;
			
			try
			{
				string metaContent = File.ReadAllText(metaPath);
				// Unity .meta files contain: guid: GUID_VALUE
				var match = Regex.Match(metaContent, @"guid:\s*([a-f0-9]{32})", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					return match.Groups[1].Value;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[MetaFileManager] Failed to read GUID from {metaPath}: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Writes a GUID to a .meta file, creating the file if it doesn't exist.
		/// </summary>
		public static bool WriteGuid(string assetPath, string guid)
		{
			if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(guid))
				return false;
			
			// Validate GUID format (32 hex characters)
			if (!Regex.IsMatch(guid, @"^[a-f0-9]{32}$", RegexOptions.IgnoreCase))
			{
				Debug.LogWarning($"[MetaFileManager] Invalid GUID format: {guid}");
				return false;
			}
			
			string metaPath = assetPath + ".meta";
			string directory = Path.GetDirectoryName(metaPath);
			if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}
			
			try
			{
				string metaContent;
				if (File.Exists(metaPath))
				{
					// Update existing .meta file
					metaContent = File.ReadAllText(metaPath);
					// Replace existing GUID
					if (Regex.IsMatch(metaContent, @"guid:\s*[a-f0-9]{32}", RegexOptions.IgnoreCase))
					{
						metaContent = Regex.Replace(metaContent, @"guid:\s*[a-f0-9]{32}", $"guid: {guid}", RegexOptions.IgnoreCase);
					}
					else
					{
						// GUID not found, append it (shouldn't happen, but handle gracefully)
						metaContent += $"\nguid: {guid}";
					}
				}
				else
				{
					// Create new .meta file with minimal content
					metaContent = $"fileFormatVersion: 2\nguid: {guid}\n";
				}
				
				File.WriteAllText(metaPath, metaContent);
				return true;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[MetaFileManager] Failed to write GUID to {metaPath}: {ex.Message}");
				return false;
			}
		}
		
		/// <summary>
		/// Copies the GUID from source asset's .meta file to target asset's .meta file.
		/// </summary>
		public static bool CopyGuid(string sourceAssetPath, string targetAssetPath)
		{
			string sourceGuid = ReadGuid(sourceAssetPath);
			if (string.IsNullOrEmpty(sourceGuid))
			{
				Debug.LogWarning($"[MetaFileManager] Could not read GUID from source: {sourceAssetPath}");
				return false;
			}
			
			return WriteGuid(targetAssetPath, sourceGuid);
		}
	}
}




