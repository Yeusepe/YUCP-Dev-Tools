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
		/// Writes a GUID to a .meta file, preserving all other settings (materials, importer settings, etc.).
		/// Only the GUID value is changed; everything else in the .meta file remains exactly the same.
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
				if (!File.Exists(metaPath))
				{
					// Create new .meta file with minimal content (shouldn't happen in our use case)
					string newMetaContent = $"fileFormatVersion: 2\nguid: {guid}\n";
					File.WriteAllText(metaPath, newMetaContent);
					return true;
				}
				
				// Read the entire .meta file to preserve ALL settings
				string metaContent = File.ReadAllText(metaPath);
				string originalContent = metaContent; // Keep a copy for comparison
				
				// Replace ONLY the GUID value, preserving the exact format (spacing, etc.)
				// Match the guid line with any spacing: "guid: OLDGUID" or "guid:OLDGUID" etc.
				// Capture the whitespace and format to preserve it
				var guidMatch = Regex.Match(metaContent, @"(guid\s*:\s*)([a-f0-9]{32})", RegexOptions.IgnoreCase);
				if (guidMatch.Success)
				{
					// Preserve the exact format of the guid line (spacing, etc.)
					string guidPrefix = guidMatch.Groups[1].Value; // "guid: " or "guid:" etc.
					// Replace only the GUID value part, keeping the prefix format
					metaContent = metaContent.Substring(0, guidMatch.Index) +
					              guidPrefix + guid +
					              metaContent.Substring(guidMatch.Index + guidMatch.Length);
				}
				else
				{
					// GUID not found - try to add it (shouldn't happen, but handle gracefully)
					Debug.LogWarning($"[MetaFileManager] GUID line not found in .meta file, attempting to add it");
					// Try to find fileFormatVersion line to add GUID after it
					var formatMatch = Regex.Match(metaContent, @"(fileFormatVersion\s*:\s*\d+)", RegexOptions.IgnoreCase);
					if (formatMatch.Success)
					{
						int insertPos = formatMatch.Index + formatMatch.Length;
						// Insert GUID after fileFormatVersion line
						metaContent = metaContent.Insert(insertPos, $"\nguid: {guid}");
					}
					else
					{
						// No fileFormatVersion either, prepend both
						metaContent = $"fileFormatVersion: 2\nguid: {guid}\n" + metaContent;
					}
				}
				
				// Only write if content actually changed
				if (metaContent != originalContent)
				{
					File.WriteAllText(metaPath, metaContent);
					Debug.Log($"[MetaFileManager] Successfully updated GUID in {metaPath}, all other settings preserved");
				}
				else
				{
					Debug.LogWarning($"[MetaFileManager] GUID was already {guid} in {metaPath}");
				}
				
				return true;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[MetaFileManager] Failed to write GUID to {metaPath}: {ex.Message}\n{ex.StackTrace}");
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
		
		/// <summary>
		/// Reads the full content of a .meta file.
		/// </summary>
		public static string ReadMetaFileContent(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath))
				return null;
			
			string metaPath = assetPath + ".meta";
			if (!File.Exists(metaPath))
				return null;
			
			try
			{
				return File.ReadAllText(metaPath);
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[MetaFileManager] Failed to read .meta file content from {metaPath}: {ex.Message}");
				return null;
			}
		}
		
		/// <summary>
		/// Changes the GUID in a .meta file while preserving all other content exactly as it was.
		/// This is safer than WriteGuid because it ensures the exact format is preserved.
		/// </summary>
		public static bool ChangeGuidPreservingContent(string assetPath, string newGuid)
		{
			if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(newGuid))
				return false;
			
			// Validate GUID format
			if (!Regex.IsMatch(newGuid, @"^[a-f0-9]{32}$", RegexOptions.IgnoreCase))
			{
				Debug.LogWarning($"[MetaFileManager] Invalid GUID format: {newGuid}");
				return false;
			}
			
			string metaPath = assetPath + ".meta";
			if (!File.Exists(metaPath))
			{
				Debug.LogWarning($"[MetaFileManager] .meta file does not exist: {metaPath}");
				return false;
			}
			
			try
			{
				// Read the entire .meta file content
				string metaContent = File.ReadAllText(metaPath);
				string originalContent = metaContent;
				
				// Find and replace ONLY the GUID value, preserving everything else
				// Match pattern: "guid: OLDGUID1234567890abcdef1234567890abcdef"
				// We want to replace just the GUID value part
				var match = Regex.Match(metaContent, @"(guid\s*:\s*)([a-f0-9]{32})", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					// Replace only the GUID value, keeping the prefix ("guid: " etc.) exactly as is
					int guidValueStart = match.Groups[2].Index;
					int guidValueEnd = guidValueStart + match.Groups[2].Length;
					
					metaContent = metaContent.Substring(0, guidValueStart) + newGuid + metaContent.Substring(guidValueEnd);
					
					// Write back only if changed
					if (metaContent != originalContent)
					{
						File.WriteAllText(metaPath, metaContent);
						Debug.Log($"[MetaFileManager] Changed GUID in {assetPath} from {match.Groups[2].Value} to {newGuid}, all settings preserved");
						return true;
					}
					else
					{
						Debug.LogWarning($"[MetaFileManager] GUID was already {newGuid} in {assetPath}");
						return true; // Already correct
					}
				}
				else
				{
					Debug.LogError($"[MetaFileManager] Could not find GUID line in .meta file: {metaPath}");
					return false;
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[MetaFileManager] Failed to change GUID in {metaPath}: {ex.Message}\n{ex.StackTrace}");
				return false;
			}
		}
		
		/// <summary>
		/// Regenerates the GUID for an asset by deleting its .meta file, allowing Unity to create a new one.
		/// Returns the new GUID after Unity regenerates it.
		/// </summary>
		public static string RegenerateGuid(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath))
				return null;
			
			string metaPath = assetPath + ".meta";
			
			try
			{
				// Read the old GUID before deleting
				string oldGuid = ReadGuid(assetPath);
				
				// Delete the .meta file to force Unity to regenerate it
				if (File.Exists(metaPath))
				{
					File.Delete(metaPath);
				}
				
				// Force Unity to refresh and regenerate the .meta file
				// We'll need to trigger an import to get Unity to create the new .meta file
				return oldGuid; // Return old GUID so caller can track what changed
			}
			catch (Exception ex)
			{
				Debug.LogError($"[MetaFileManager] Failed to regenerate GUID for {assetPath}: {ex.Message}");
				return null;
			}
		}
	}
}






