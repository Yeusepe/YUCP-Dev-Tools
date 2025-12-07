using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Utility for updating GUID references across Unity project files.
	/// </summary>
	public static class GuidReferenceUpdater
	{
		/// <summary>
		/// Updates all references from oldGuid to newGuid across the project.
		/// </summary>
		/// <param name="oldGuid">The GUID to find and replace</param>
		/// <param name="newGuid">The new GUID to use</param>
		/// <param name="excludePath">Path to exclude from updates (the asset being updated)</param>
		/// <returns>Number of files updated</returns>
		public static int UpdateReferences(string oldGuid, string newGuid, string excludePath = null)
		{
			if (string.IsNullOrEmpty(oldGuid) || string.IsNullOrEmpty(newGuid))
			{
				Debug.LogWarning("[GuidReferenceUpdater] Old GUID or new GUID is empty");
				return 0;
			}
			
			if (oldGuid == newGuid)
			{
				Debug.LogWarning("[GuidReferenceUpdater] Old GUID and new GUID are the same");
				return 0;
			}
			
			int updatedCount = 0;
			string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			
			// Get the exclude path in physical format
			string excludePhysicalPath = null;
			if (!string.IsNullOrEmpty(excludePath))
			{
				excludePhysicalPath = Path.Combine(projectPath, excludePath.Replace('/', Path.DirectorySeparatorChar));
			}
			
			try
			{
				// Search in Assets and Packages directories
				string[] searchPaths = new string[]
				{
					Path.Combine(projectPath, "Assets"),
					Path.Combine(projectPath, "Packages")
				};
				
				foreach (var searchPath in searchPaths)
				{
					if (!Directory.Exists(searchPath))
						continue;
					
					// Search all .meta files
					string[] metaFiles = Directory.GetFiles(searchPath, "*.meta", SearchOption.AllDirectories);
					
					foreach (var metaFile in metaFiles)
					{
						try
						{
							// Skip the file being updated
							if (!string.IsNullOrEmpty(excludePhysicalPath))
							{
								string assetPath = metaFile.Substring(0, metaFile.Length - 5); // Remove .meta extension
								if (assetPath.Equals(excludePhysicalPath, StringComparison.OrdinalIgnoreCase))
								{
									continue;
								}
							}
							
							string content = File.ReadAllText(metaFile);
							bool wasModified = false;
							
							// Use word boundaries to match complete GUIDs only
							string oldGuidPattern = @"\b" + Regex.Escape(oldGuid) + @"\b";
							if (Regex.IsMatch(content, oldGuidPattern, RegexOptions.IgnoreCase))
							{
								content = Regex.Replace(content, oldGuidPattern, newGuid, RegexOptions.IgnoreCase);
								wasModified = true;
							}
							
							if (wasModified)
							{
								File.WriteAllText(metaFile, content);
								string relativePath = metaFile.Replace(projectPath, "").TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
								Debug.Log($"[GuidReferenceUpdater] Updated references in: {relativePath}");
								updatedCount++;
							}
						}
						catch (Exception ex)
						{
							Debug.LogWarning($"[GuidReferenceUpdater] Could not process {metaFile}: {ex.Message}");
						}
					}
					
					// Also search in scene files and prefab files (.prefab, .unity, .asset)
					// These can contain GUID references in serialized data
					string[] assetExtensions = new[] { "*.prefab", "*.unity", "*.asset", "*.mat", "*.controller", "*.anim" };
					
					foreach (var extension in assetExtensions)
					{
						string[] assetFiles = Directory.GetFiles(searchPath, extension, SearchOption.AllDirectories);
						
						foreach (var assetFile in assetFiles)
						{
							try
							{
								// Skip the file being updated
								if (!string.IsNullOrEmpty(excludePhysicalPath) && 
								    assetFile.Equals(excludePhysicalPath, StringComparison.OrdinalIgnoreCase))
								{
									continue;
								}
								
								string content = File.ReadAllText(assetFile);
								bool wasModified = false;
								
								// Search for GUID references in YAML format
								string oldGuidPattern = @"guid:\s*" + Regex.Escape(oldGuid);
								if (Regex.IsMatch(content, oldGuidPattern, RegexOptions.IgnoreCase))
								{
									content = Regex.Replace(content, oldGuidPattern, $"guid: {newGuid}", RegexOptions.IgnoreCase);
									wasModified = true;
								}
								
								if (wasModified)
								{
									File.WriteAllText(assetFile, content);
									string relativePath = assetFile.Replace(projectPath, "").TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
									Debug.Log($"[GuidReferenceUpdater] Updated references in: {relativePath}");
									updatedCount++;
								}
							}
							catch (Exception ex)
							{
								// Some files might be binary or locked, ignore errors silently
								// Debug.LogWarning($"[GuidReferenceUpdater] Could not process {assetFile}: {ex.Message}");
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[GuidReferenceUpdater] Error updating references: {ex.Message}\n{ex.StackTrace}");
			}
			
			return updatedCount;
		}
	}
}



