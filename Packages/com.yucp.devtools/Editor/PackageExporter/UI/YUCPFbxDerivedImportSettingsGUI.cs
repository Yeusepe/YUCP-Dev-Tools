using System;
using System.IO;
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
			EditorGUILayout.HelpBox("Configure how this asset is exported as a lightweight patch package. Patches allow you to distribute modifications without including the original large assets.", MessageType.Info);
			
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
					settings.isDerived = EditorGUILayout.ToggleLeft("Enable Patch Export", settings.isDerived, headerStyle);
				}

				if (settings.isDerived)
				{
					EditorGUILayout.Space(5);
					
					// -- Mode Selection (Kitbash vs Single) --
					#if YUCP_KITBASH_ENABLED
					DrawModeSelection(settings);
					EditorGUILayout.Space(5);
					#endif

					// -- Source Section --
					DrawSectionHeader("Source");
					bool isKitbash = false;
					#if YUCP_KITBASH_ENABLED
					isKitbash = settings.mode == DerivedMode.KitbashRecipeHdiff;
					#endif

					if (isKitbash)
					{
						#if YUCP_KITBASH_ENABLED
						DrawKitbashConfig(importer, settings);
						#endif
					}
					else
					{
						DrawSingleSourceConfig(settings);
					}

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

		#if YUCP_KITBASH_ENABLED
		private static void DrawModeSelection(DerivedSettings settings)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Patch Mode:", GUILayout.Width(80));
			
			// Use a toolbar-style selection
			var modes = new string[] { "Single Source", "Kitbash (Multi-Source)" };
			int currentMode = (int)settings.mode;
			int newMode = GUILayout.Toolbar(currentMode, modes, EditorStyles.miniButton); 
			
			if (newMode != currentMode)
			{
				settings.mode = (DerivedMode)newMode;
			}
			EditorGUILayout.EndHorizontal();
			
			// Explanation for modes
			if (settings.mode == DerivedMode.SingleBaseHdiff)
			{
				DrawHelpText("Standard Mode: Creates a patch for a single Original FBX file. Use this for texture variations or simple mesh edits.");
			}
			else
			{
				DrawHelpText("Kitbash Mode: Creates a patch that combines parts from multiple source FBXs. Use this for complex assemblies.");
			}
		}

		private static void DrawKitbashConfig(ModelImporter importer, DerivedSettings settings)
		{
			// Status
			bool hasRecipe = !string.IsNullOrEmpty(settings.kitbashRecipeGuid);
			
			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					// Icon/Status
					var statusIcon = hasRecipe ? "✓" : "!";
					var statusColor = hasRecipe ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.9f, 0.6f, 0.2f);
					var oldColor = GUI.color;
					GUI.color = statusColor;
					GUILayout.Label(statusIcon, GUILayout.Width(15));
					GUILayout.Label(hasRecipe ? "Configured" : "Recipe Missing", EditorStyles.boldLabel);
					GUI.color = oldColor;

					GUILayout.FlexibleSpace();

					if (GUILayout.Button("Open Kitbash Configurator", GUILayout.Height(20)))
					{
						OpenKitbashConfigurator(importer, settings);
					}
				}
				
				if (!hasRecipe)
				{
					EditorGUILayout.HelpBox("Click 'Open Kitbash Configurator' to select the source FBX files you want to combine.", MessageType.Info);
				}
			}
		}
		#endif

		private static void DrawSingleSourceConfig(DerivedSettings settings)
		{
			UnityEngine.Object currentBase = null;
			if (!string.IsNullOrEmpty(settings.baseGuid))
			{
				string path = AssetDatabase.GUIDToAssetPath(settings.baseGuid);
				if (!string.IsNullOrEmpty(path))
					currentBase = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
			}

			var newBase = EditorGUILayout.ObjectField(new GUIContent("Original FBX", "The original FBX file that this specific mesh is modified from."), currentBase, typeof(UnityEngine.Object), false);
			
			if (newBase != currentBase)
			{
				string path = AssetDatabase.GetAssetPath(newBase);
				settings.baseGuid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
			}

			if (currentBase == null)
			{
				EditorGUILayout.HelpBox("REQUIRED: Select the original FBX file. This patch will store ONLY the differences between your file and this Original FBX.", MessageType.Error);
			}
			else
			{
				DrawHelpText($"This patch will rely on '{currentBase.name}' as its base.");
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

				// Fix File References
				EditorGUILayout.LabelField("Maintenance (Repair)", EditorStyles.miniBoldLabel);
				
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Repair File Identity (GUID)", GUILayout.Height(24)))
				{
					RegenerateAndRelinkGuids(importer, settings);
				}
				EditorGUILayout.EndHorizontal();
				
				DrawHelpText("Use this ONLY if you replaced the file manually and links are broken. It assigns a new ID to this file and updates references.");

				EditorGUI.indentLevel--;
			}
			else
			{
				UnityEditor.SessionState.SetBool(foldoutKey, false);
			}
		}
		
		#if YUCP_KITBASH_ENABLED
		/// <summary>
		/// Opens Kitbash Edit Mode for the specified FBX.
		/// This is the main entry point - like clicking "Configure" on Humanoid.
		/// </summary>
		private static void OpenKitbashConfigurator(ModelImporter importer, DerivedSettings settings)
		{
			// Enter stage-based edit mode (full editor takeover)
			Kitbash.UI.KitbashStage.Enter(importer, settings);
		}
		#endif
		
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
			
			// Check if base GUID exists (it might not if the file was replaced)
			bool hasBaseGuid = !string.IsNullOrEmpty(settings.baseGuid);
			string basePath = null;
			bool baseStillExists = false;
			
			if (hasBaseGuid)
			{
				basePath = AssetDatabase.GUIDToAssetPath(settings.baseGuid);
				baseStillExists = !string.IsNullOrEmpty(basePath);
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
			
			try
			{
				EditorUtility.DisplayProgressBar("Fixing File References", "Preparing...", 0.1f);
				
				// Store the old GUID before regenerating (this is the current derived FBX's GUID)
				string oldDerivedGuid = currentGuid;
				
				// Generate a new GUID using Unity's GUID system
				// Use Unity's GUID.Generate() to ensure compatibility with Unity's GUID format
				UnityEditor.GUID unityGuid = UnityEditor.GUID.Generate();
				string newDerivedGuid = unityGuid.ToString(); // This gives us the 32-character hex string
				
				EditorUtility.DisplayProgressBar("Fixing File References", "Updating file ID...", 0.3f);
				
				// Use the precise method that preserves ALL .meta file content (materials, importer settings, etc.)
				// and only changes the GUID value
				if (!MetaFileManager.ChangeGuidPreservingContent(currentFbxPath, newDerivedGuid))
				{
					EditorUtility.ClearProgressBar();
					EditorUtility.DisplayDialog("Error", "Failed to update the file. Please check the Console window for details and make sure the file is not locked or in use.", "OK");
					return;
				}
				
				EditorUtility.DisplayProgressBar("Fixing File References", "Refreshing Unity...", 0.5f);
				
				// Refresh WITHOUT forcing reimport to avoid Unity regenerating the .meta file
				// We just need Unity to pick up the GUID change, not reimport the asset
				AssetDatabase.Refresh(ImportAssetOptions.Default);
				
				// Wait a moment for Unity to process
				System.Threading.Thread.Sleep(300);
				
				// Verify the new GUID was set correctly
				string verifyGuid = AssetDatabase.AssetPathToGUID(currentFbxPath);
				if (string.IsNullOrEmpty(verifyGuid))
				{
					EditorUtility.ClearProgressBar();
					EditorUtility.DisplayDialog("Error", "Could not verify the file was updated. Please check if the file exists and try again.", "OK");
					return;
				}
				
				// Check if Unity picked up our new GUID or regenerated its own
				if (verifyGuid != newDerivedGuid)
				{
					// Unity might have regenerated a different GUID due to caching
					// Let's read what's actually in the .meta file
					string metaGuid = MetaFileManager.ReadGuid(currentFbxPath);
					if (!string.IsNullOrEmpty(metaGuid) && metaGuid == newDerivedGuid)
					{
						// Our GUID is in the file, but Unity isn't seeing it - cache issue
						Debug.LogWarning($"[YUCP] GUID mismatch - .meta file has {newDerivedGuid} but Unity reports {verifyGuid}. This is likely a Unity cache issue.");
						// Use the GUID from the file since that's what we wrote
						// Unity should pick it up eventually or after restart
					}
					else
					{
						// Unity overwrote our GUID - use what Unity assigned
						newDerivedGuid = verifyGuid;
						Debug.LogWarning($"[YUCP] Unity assigned a different GUID: {verifyGuid} instead of our {newDerivedGuid}");
					}
				}
				
				if (newDerivedGuid == oldDerivedGuid)
				{
					EditorUtility.ClearProgressBar();
					EditorUtility.DisplayDialog("Warning", 
						$"The file ID did not change. Unity might be using cached information.\n\n" +
						$"Try one of these:\n" +
						$"- Close and reopen Unity\n" +
						$"- Restart your computer\n" +
						$"- Contact support if the problem persists", 
						"OK");
					return;
				}
				
				Debug.Log($"[YUCP] Regenerated GUID for derived FBX {currentFbxPath}: {oldDerivedGuid} -> {newDerivedGuid}");
				
				EditorUtility.DisplayProgressBar("Fixing File References", "Updating connections in prefabs and scenes...", 0.7f);
				
				// Update all references that were pointing to the old derived GUID to point to the new one
				int updatedCount = GuidReferenceUpdater.UpdateReferences(oldDerivedGuid, newDerivedGuid, currentFbxPath);
				
				// Note: We don't update references from baseGuid to newDerivedGuid here
				// because the goal is to allow the original FBX to be reimported with its base GUID.
				// References to the base GUID should remain pointing to the base GUID so they work
				// when the original FBX is reimported.
				
				EditorUtility.DisplayProgressBar("Fixing File References", "Finishing up...", 0.9f);
				
				AssetDatabase.Refresh();
				
				EditorUtility.ClearProgressBar();
				
				string resultMessage = $"Success!\n\n";
				resultMessage += $"File: {Path.GetFileName(currentFbxPath)}\n";
				resultMessage += $"Files updated: {updatedCount}\n\n";
				
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
				
				Debug.Log($"[YUCP] GUID regeneration complete. Updated {updatedCount} file(s). Derived FBX now has GUID {newDerivedGuid}, ready for original FBX to be reimported.");
			}
			catch (Exception ex)
			{
				EditorUtility.ClearProgressBar();
				Debug.LogError($"[YUCP] Error regenerating GUID: {ex.Message}\n{ex.StackTrace}");
				EditorUtility.DisplayDialog("Error", $"Failed to regenerate GUID: {ex.Message}", "OK");
			}
		}
	}
}














