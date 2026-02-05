using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	// DerivedSettings is now defined in Data/DerivedSettings.cs

	internal static class YUCPFbxDerivedImportSettingsGUI
	{
		public static void Draw(ModelImporter importer)
		{
			if (importer == null) return;

			EditorGUILayout.Space(15);
			
			// Main Section Header
			GUILayout.Label("YUCP Import Options", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Configure how this asset is exported as a lightweight derived FBX package. Derived FBXs allow you to distribute modifications without including the original large assets.", MessageType.Info);
			
			EditorGUILayout.Space(5);
			DerivedSettings settings = null;
			try
			{
				settings = string.IsNullOrEmpty(importer.userData) ? new DerivedSettings() : JsonUtility.FromJson<DerivedSettings>(importer.userData);
				if (settings == null) settings = new DerivedSettings();
			}
			catch
			{
				settings = new DerivedSettings();
			}

			EditorGUI.BeginChangeCheck();

			// Main Card Style
			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				// -- Header --
				using (new EditorGUILayout.HorizontalScope())
				{
					var headerStyle = new GUIStyle(EditorStyles.boldLabel);
					settings.isDerived = EditorGUILayout.ToggleLeft("Export as Derived FBX", settings.isDerived, headerStyle);
				}

				if (settings.isDerived)
				{
					EditorGUILayout.Space(5);

					// -- Source Section --
					DrawSectionHeader("Source");
					DrawBaseList(settings);

					EditorGUILayout.Space(10);

					// -- Metadata Section --
					DrawSectionHeader("Package Metadata");
					
					settings.friendlyName = EditorGUILayout.TextField(new GUIContent("Display Name", "The name used for this patch in the package installer."), string.IsNullOrEmpty(settings.friendlyName) ? System.IO.Path.GetFileNameWithoutExtension(importer.assetPath) : settings.friendlyName);
					DrawHelpText("The user-friendly name shown in the installer (e.g., 'Blue Shirt Variant').");
					
					EditorGUILayout.Space(2);
					
					settings.category = EditorGUILayout.TextField(new GUIContent("Category", "Group this patch under a category in the package installer."), settings.category);
					DrawHelpText("Organizes patches in the installer (e.g., 'Clothes/Shirts').");

					EditorGUILayout.Space(10);

					// -- Advanced Actions --
					DrawAdvancedActions(importer, settings);
					
					EditorGUILayout.Space(2);
				}
			}

			if (EditorGUI.EndChangeCheck())
			{
				importer.userData = JsonUtility.ToJson(settings);
				EditorUtility.SetDirty(importer);
				AssetDatabase.SaveAssets();
			}
		}

		private static void DrawSectionHeader(string title)
		{
			EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
		}
		
		private static void DrawHelpText(string text)
		{
			// Subtle help text style
			var style = new GUIStyle(EditorStyles.miniLabel);
			style.wordWrap = true;
			style.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.4f, 0.4f, 0.4f);
			EditorGUILayout.LabelField(text, style);
		}

		private static void DrawBaseList(DerivedSettings settings)
		{
			if (settings.baseGuids == null)
				settings.baseGuids = new List<string>();

			if (settings.baseGuids.Count == 0)
			{
				EditorGUILayout.HelpBox("REQUIRED: Add at least one base FBX. This patch will store ONLY the differences between your file and each base.", MessageType.Error);
			}
			else
			{
				DrawHelpText("Add one or more base FBXs. The installer will use the first compatible base it finds.");
			}

			for (int i = 0; i < settings.baseGuids.Count; i++)
			{
				UnityEngine.Object currentBase = null;
				if (!string.IsNullOrEmpty(settings.baseGuids[i]))
				{
					string path = AssetDatabase.GUIDToAssetPath(settings.baseGuids[i]);
					if (!string.IsNullOrEmpty(path))
						currentBase = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
				}

				using (new EditorGUILayout.HorizontalScope())
				{
					var newBase = EditorGUILayout.ObjectField(
						new GUIContent($"Base FBX {i + 1}", "A base FBX that this derived asset can be reconstructed from."),
						currentBase,
						typeof(UnityEngine.Object),
						false);

					if (newBase != currentBase)
					{
						string path = AssetDatabase.GetAssetPath(newBase);
						settings.baseGuids[i] = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
					}

					if (GUILayout.Button("Remove", GUILayout.Width(70)))
					{
						settings.baseGuids.RemoveAt(i);
						i--;
						continue;
					}
				}
			}

			if (GUILayout.Button("Add Base FBX", GUILayout.Height(22)))
			{
				settings.baseGuids.Add(null);
			}
		}

		private static void DrawAdvancedActions(ModelImporter importer, DerivedSettings settings)
		{
			string foldoutKey = "YUCP_DerivedSettings_Advanced_" + importer.assetPath.GetHashCode();
			bool foldout = UnityEditor.SessionState.GetBool(foldoutKey, false);

			foldout = EditorGUILayout.Foldout(foldout, "Advanced Settings", true);
			if (foldout)
			{
				UnityEditor.SessionState.SetBool(foldoutKey, true);
				EditorGUI.indentLevel++;
				
				// Reference Override
				EditorGUILayout.LabelField("Reference Handling", EditorStyles.miniBoldLabel);
				EditorGUILayout.BeginHorizontal();
				settings.overrideOriginalReferences = EditorGUILayout.Toggle(settings.overrideOriginalReferences, GUILayout.Width(15));
				EditorGUILayout.LabelField(new GUIContent("Replace Original References", "After generating the derived FBX, automatically update all scene/prefab references to point to this new file instead of the original."), EditorStyles.label);
				EditorGUILayout.EndHorizontal();
				
				DrawHelpText("When enabled, Unity will update all scenes and prefabs to use THIS patch file instead of the Original FBX. Useful if you want to seamlessly swap the asset in your project.");
				
				EditorGUILayout.Space(5);

				// Restore Original
				EditorGUILayout.LabelField("Restore Original FBX", EditorStyles.miniBoldLabel);
				DrawRestoreOriginalAction(importer);

				EditorGUI.indentLevel--;
			}
			else
			{
				UnityEditor.SessionState.SetBool(foldoutKey, false);
			}
		}
		
		/// <summary>
		/// Regenerates the GUID for the current derived FBX and updates all references.
		/// This allows the original FBX to be reimported and properly referenced as the base FBX.
		/// </summary>
		private static void RegenerateAndRelinkGuids(ModelImporter importer, DerivedSettings settings)
		{
			if (importer == null)
			{
				EditorUtility.DisplayDialog("Error", "ModelImporter is null.", "OK");
				return;
			}
			
			string currentFbxPath = importer.assetPath;
			string currentGuid = AssetDatabase.AssetPathToGUID(currentFbxPath);
			
			if (string.IsNullOrEmpty(currentGuid))
			{
				EditorUtility.DisplayDialog("Error", $"Could not read file information for:\n{currentFbxPath}\n\nPlease make sure the file exists and try again.", "OK");
				return;
			}
			
			// Check if any base GUID exists (it might not if the file was replaced)
			bool hasBaseGuid = false;
			bool baseStillExists = false;
			if (settings.baseGuids != null)
			{
				foreach (var guid in settings.baseGuids)
				{
					if (string.IsNullOrEmpty(guid)) continue;
					hasBaseGuid = true;
					string basePath = AssetDatabase.GUIDToAssetPath(guid);
					if (!string.IsNullOrEmpty(basePath))
					{
						baseStillExists = true;
						break;
					}
				}
			}
			
			// Build user-friendly message explaining what will happen
			string message = $"This will fix file references for:\n{Path.GetFileName(currentFbxPath)}\n\n";
			
			message += $"What will happen:\n";
			message += $"- This file will get a new internal ID\n";
			message += $"- All connections to this file (from prefabs, scenes, etc.) will be updated\n";
			message += $"- All import settings, materials, and textures will be preserved\n\n";
			
			if (!baseStillExists && hasBaseGuid)
			{
				message += $"Why you need this:\n";
				message += $"You replaced the original FBX file with this derived version.\n";
				message += $"After this, you can:\n";
				message += $"1. Move this derived FBX to a different location\n";
				message += $"2. Reimport your original FBX file\n";
				message += $"3. The system will recognize the original FBX again\n\n";
			}
			else if (!hasBaseGuid)
			{
				message += $"Why you need this:\n";
				message += $"You replaced the original FBX file with this derived version.\n";
				message += $"After this, you can reimport your original FBX file\n";
				message += $"and the system will recognize it properly again.\n\n";
			}
			
			message += "Warning: This cannot be easily undone. Make sure you have a backup!\n\n";
			message += "Continue?";
			
			bool confirmed = EditorUtility.DisplayDialog(
				"Fix File References",
				message,
				"Yes, Continue",
				"Cancel"
			);
			
			if (!confirmed)
				return;
			
			var repairResult = GuidRepairUtility.RepairDerivedGuid(currentFbxPath, true);
			if (!repairResult.success)
			{
				EditorUtility.DisplayDialog("Error", repairResult.errorMessage, "OK");
				return;
			}

			if (!string.IsNullOrEmpty(repairResult.warningMessage))
			{
				Debug.LogWarning($"[YUCP] {repairResult.warningMessage}");
			}

			string resultMessage = $"Success!\n\n";
			resultMessage += $"File: {Path.GetFileName(currentFbxPath)}\n";
			resultMessage += $"Files updated: {repairResult.updatedCount}\n\n";
				
			if (hasBaseGuid && !baseStillExists)
			{
				resultMessage += $"Next steps to restore your original FBX:\n\n";
				resultMessage += $"1. Move this derived FBX file to a different folder\n";
				resultMessage += $"2. Put your original FBX file back at this location\n";
				resultMessage += $"3. Unity will automatically reimport it\n";
				resultMessage += $"4. The system will recognize your original file again\n\n";
				resultMessage += $"All your materials and settings will be preserved!";
			}
			else
			{
				resultMessage += $"Next steps:\n\n";
				resultMessage += $"1. You can now move this derived FBX to a different location\n";
				resultMessage += $"2. Put your original FBX file back at this location\n";
				resultMessage += $"3. Update the \"Base FBX\" field above to point to your original file\n\n";
				resultMessage += $"All your materials and settings will be preserved!";
			}
			
			EditorUtility.DisplayDialog(
				"Complete!",
				resultMessage,
				"OK"
			);
		}

		private static void DrawRestoreOriginalAction(ModelImporter importer)
		{
			DrawHelpText("Use this only if you replaced the original FBX and want to bring the original back.");
			
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Restore Original FBX (Guided)", GUILayout.Height(24)))
			{
				DerivedFbxGuidRepairWizard.Open(importer);
			}
			EditorGUILayout.EndHorizontal();
		}
	}
}








