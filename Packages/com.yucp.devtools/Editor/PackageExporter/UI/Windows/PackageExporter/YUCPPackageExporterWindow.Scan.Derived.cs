using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using YUCP.DevTools.Components;
using YUCP.DevTools.Editor.PackageExporter.UI.Components;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
		private bool IsDerivedFbx(string assetPath, out DerivedSettings settings, out string basePath)
		{
			settings = null;
			basePath = null;
			if (string.IsNullOrEmpty(assetPath)) return false;
			if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return false;
			
			// Convert to relative path if needed (for AssetDatabase/AssetImporter APIs)
			string relativePath = assetPath;
			if (Path.IsPathRooted(relativePath))
			{
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				if (relativePath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
				{
					relativePath = relativePath.Substring(projectPath.Length).Replace('\\', '/').TrimStart('/');
				}
			}
			
			// Try reading from importer first
			var importer = AssetImporter.GetAtPath(relativePath) as ModelImporter;
			if (importer != null)
			{
				try
				{
					string userDataJson = importer.userData;
					if (!string.IsNullOrEmpty(userDataJson))
					{
						settings = JsonUtility.FromJson<DerivedSettings>(userDataJson);
						if (settings != null && settings.isDerived)
						{
							basePath = string.IsNullOrEmpty(settings.baseGuid) ? null : AssetDatabase.GUIDToAssetPath(settings.baseGuid);
							return true;
						}
					}
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"[YUCP PackageExporter] Failed to parse importer userData for {assetPath}: {ex.Message}\nuserData was: '{importer.userData}'");
				}
			}
			
			// Fallback: read directly from .meta file
			try
			{
				// Use original assetPath for file system operations (can be absolute or relative)
				string metaPath = assetPath + ".meta";
				// If relative path, convert to absolute for File operations
				if (!Path.IsPathRooted(metaPath))
				{
					string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
					metaPath = Path.GetFullPath(Path.Combine(projectPath, metaPath));
				}
				if (File.Exists(metaPath))
				{
					string metaContent = File.ReadAllText(metaPath);
					// Look for userData in the meta file
					// Unity stores it as: userData: <json string>
					// But it might be on multiple lines or have special formatting
					
					// Try regex to find userData line
					// Match: userData: <value> where value can be quoted JSON or empty
					var userDataMatch = System.Text.RegularExpressions.Regex.Match(metaContent, @"userData:\s*([^\r\n]*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
					if (userDataMatch.Success)
					{
						string userDataJson = userDataMatch.Groups[1].Value.Trim();
						// Skip if empty or matches Unity's default format
						if (string.IsNullOrEmpty(userDataJson) || userDataJson == "assetBundleName:")
						{
							// Not our custom userData, skip
						}
						else
						{
							// Unity stores userData as a quoted string in YAML, so strip surrounding quotes first
							if ((userDataJson.StartsWith("'") && userDataJson.EndsWith("'")) || 
							    (userDataJson.StartsWith("\"") && userDataJson.EndsWith("\"")))
							{
								userDataJson = userDataJson.Substring(1, userDataJson.Length - 2);
							}
							
							// Now check if it's our JSON format
							if (!string.IsNullOrEmpty(userDataJson) && userDataJson.StartsWith("{"))
							{
								try
								{
									settings = JsonUtility.FromJson<DerivedSettings>(userDataJson);
									if (settings != null && settings.isDerived)
									{
										basePath = string.IsNullOrEmpty(settings.baseGuid) ? null : AssetDatabase.GUIDToAssetPath(settings.baseGuid);
										return true;
									}
								}
								catch (Exception jsonEx)
								{
									Debug.LogWarning($"[YUCP PackageExporter] Failed to parse userData JSON from .meta for {Path.GetFileName(assetPath)}: {jsonEx.Message}\nExtracted value: '{userDataJson}'");
								}
							}
						}
					}
					else
					{
						// No userData found - check if the CustomEditor is even saving
						// This is normal if the FBX hasn't been marked as derived
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[YUCP PackageExporter] Failed to read .meta file for {assetPath}: {ex.Message}\n{ex.StackTrace}");
			}
			
			return false;
		}

		private class DerivedSettings
		{
			public bool isDerived;
			public string baseGuid;
			public string friendlyName;
			public string category;
		}

    }
}
