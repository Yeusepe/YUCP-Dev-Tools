using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	[Serializable]
	internal class DerivedSettings
	{
		public bool isDerived;
		public string baseGuid;
		public string friendlyName;
		public string category;
		public bool overrideOriginalReferences = false;
	}

	internal static class YUCPFbxDerivedImportSettingsGUI
	{
		public static void Draw(ModelImporter importer)
		{
			if (importer == null) return;

			EditorGUILayout.Space(6);
			EditorGUILayout.LabelField("YUCP Patch Export", EditorStyles.boldLabel);

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
			settings.isDerived = EditorGUILayout.Toggle(new GUIContent("Export As Patch (Derived)"), settings.isDerived);

			using (new EditorGUI.DisabledScope(!settings.isDerived))
			{
				UnityEngine.Object currentBase = null;
				if (!string.IsNullOrEmpty(settings.baseGuid))
				{
					string path = AssetDatabase.GUIDToAssetPath(settings.baseGuid);
					if (!string.IsNullOrEmpty(path))
						currentBase = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
				}

				var newBase = EditorGUILayout.ObjectField(new GUIContent("Base FBX (original)"), currentBase, typeof(UnityEngine.Object), false);
				if (newBase != currentBase)
				{
					string path = AssetDatabase.GetAssetPath(newBase);
					settings.baseGuid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
				}

				EditorGUILayout.Space(4);
				EditorGUILayout.LabelField("UI Hints", EditorStyles.boldLabel);
				settings.friendlyName = EditorGUILayout.TextField("Friendly Name", string.IsNullOrEmpty(settings.friendlyName) ? System.IO.Path.GetFileNameWithoutExtension(importer.assetPath) : settings.friendlyName);
				settings.category = EditorGUILayout.TextField("Category", settings.category);

				EditorGUILayout.Space(4);
				EditorGUILayout.HelpBox("Override Original References: After generating the derived FBX, replace all references to the original FBX with the new one. This can be reversed via Tools->YUCP->Revert GUID Override.", MessageType.Info);
				settings.overrideOriginalReferences = EditorGUILayout.Toggle(new GUIContent("Override Original References"), settings.overrideOriginalReferences);
				
				EditorGUILayout.Space(8);
				EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
				EditorGUILayout.Space(4);
				EditorGUILayout.HelpBox("If you replaced the original FBX file with this derived version, Unity lost track of the original file. Click the button below to fix the file references. Afterward, you can put your original FBX file back and Unity will recognize it properly again. All your materials and settings will be preserved.", MessageType.Info);
				
				if (GUILayout.Button("Fix File References", GUILayout.Height(30)))
				{
					RegenerateAndRelinkGuids(importer, settings);
				}
			}

			if (EditorGUI.EndChangeCheck())
			{
				importer.userData = JsonUtility.ToJson(settings);
				EditorUtility.SetDirty(importer);
				AssetDatabase.SaveAssets();
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














