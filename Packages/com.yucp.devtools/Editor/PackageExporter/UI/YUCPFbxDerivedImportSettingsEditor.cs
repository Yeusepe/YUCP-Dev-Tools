using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Extends the FBX (ModelImporter) inspector with a lightweight "Export As Patch" section.
	/// Stores settings in AssetImporter.userData as JSON.
	/// This editor preserves Unity's default ModelImporter inspector (including tabs) and adds the YUCP UI at the end.
	/// </summary>
	[CustomEditor(typeof(ModelImporter))]
	public class YUCPFbxDerivedImportSettingsEditor : AssetImporterEditor
	{
		private UnityEditor.Editor m_DefaultEditor;
		private UnityEditor.Editor m_GameObjectEditor; // Editor for imported GameObject (used by Unity's internal ModelImporter editors)
		private static Type s_ModelImporterEditorType;
		private static PropertyInfo s_ExtraDataSerializedObjectProp;

		private static SerializedObject TryGetExtraDataSerializedObject(UnityEditor.Editor editor)
		{
			if (editor == null)
				return null;

			// AssetImporterEditor.extraDataSerializedObject is protected; we can't access it on other instances directly.
			// Use reflection so we can still apply/update changes made by Unity's default importer tabs.
			if (s_ExtraDataSerializedObjectProp == null)
			{
				s_ExtraDataSerializedObjectProp = typeof(AssetImporterEditor).GetProperty(
					"extraDataSerializedObject",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
				);
			}

			return s_ExtraDataSerializedObjectProp?.GetValue(editor) as SerializedObject;
		}

		static YUCPFbxDerivedImportSettingsEditor()
		{
			var assembly = typeof(ModelImporter).Assembly;
			s_ModelImporterEditorType = assembly.GetType("UnityEditor.ModelImporterEditor");
		}

		public override void OnEnable()
		{
			// Fix any orphaned cache entries for our own editor BEFORE base.OnEnable().
			// This handles edge cases where SaveChanges triggers a reimport before OnDisable runs.
			if (targets != null && targets.Length > 0)
			{
				FixOrphanedCacheEntries(GetType());
			}
			
			// Let Unity initialize importer editor internals.
			base.OnEnable();

			// Create (or reuse) Unity's internal ModelImporterEditor to draw the default UI/tabs.
			if (s_ModelImporterEditorType != null && targets != null && targets.Length > 0)
			{
				// If there's an existing editor with different targets, destroy it first.
				// DestroyImmediate will properly call OnDisable which handles cache cleanup.
				if (m_DefaultEditor != null && !ArrayEquals(m_DefaultEditor.targets, targets))
				{
					DestroyImmediate(m_DefaultEditor);
					m_DefaultEditor = null;
				}

				if (m_DefaultEditor == null)
				{
					// Fix any orphaned cache entries before creating the editor.
					// This can happen if a previous editor was destroyed without proper OnDisable.
					FixOrphanedCacheEntries(s_ModelImporterEditorType);
					
					m_DefaultEditor = UnityEditor.Editor.CreateEditor(targets, s_ModelImporterEditorType);
				}

				// Some internal tabs expect an "asset target" (imported GameObject) editor to be present.
				if (m_DefaultEditor is AssetImporterEditor defaultAssetEditor && targets[0] is ModelImporter importer)
				{
					var importedGameObject = AssetDatabase.LoadAssetAtPath<GameObject>(importer.assetPath);
					if (importedGameObject != null)
					{
						var internalSetMethod = typeof(AssetImporterEditor).GetMethod("InternalSetAssetImporterTargetEditor", BindingFlags.NonPublic | BindingFlags.Instance);
						if (internalSetMethod != null)
						{
							if (m_GameObjectEditor == null || m_GameObjectEditor.target != importedGameObject)
							{
								if (m_GameObjectEditor != null)
								{
									DestroyImmediate(m_GameObjectEditor);
								}
								m_GameObjectEditor = UnityEditor.Editor.CreateEditor(importedGameObject);
							}
							if (m_GameObjectEditor != null)
							{
								internalSetMethod.Invoke(defaultAssetEditor, new object[] { m_GameObjectEditor });
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Fixes orphaned cache entries by calling Unity's FixCacheCount method.
		/// This corrects the native cache count to match the actual managed editor count.
		/// </summary>
		/// <param name="editorType">The editor type to search for (e.g., ModelImporterEditor or YUCPFbxDerivedImportSettingsEditor)</param>
		private void FixOrphanedCacheEntries(Type editorType)
		{
			try
			{
				var allEditors = Resources.FindObjectsOfTypeAll(editorType);
				var onEnableCalledField = typeof(AssetImporterEditor).GetField("m_OnEnableCalled", BindingFlags.Instance | BindingFlags.NonPublic);
				var getCountMethod = typeof(AssetImporterEditor).GetMethod("GetInspectorCopyCount", BindingFlags.Static | BindingFlags.NonPublic);
				var fixCacheMethod = typeof(AssetImporterEditor).GetMethod("FixCacheCount", BindingFlags.Static | BindingFlags.NonPublic);

				if (getCountMethod == null || fixCacheMethod == null || targets == null)
					return;

				foreach (var target in targets)
				{
					int instanceId = target.GetInstanceID();
					int cacheCount = (int)getCountMethod.Invoke(null, new object[] { instanceId });

					// Find editors that reference this target with m_OnEnableCalled=true
					var editorsForTarget = new System.Collections.Generic.List<int>();
					foreach (var editor in allEditors)
					{
						if (editor is AssetImporterEditor aie)
						{
							bool onEnableCalled = onEnableCalledField != null ? (bool)onEnableCalledField.GetValue(aie) : false;
							if (onEnableCalled && aie.targets != null)
							{
								foreach (var t in aie.targets)
								{
									if (t != null && t.GetInstanceID() == instanceId)
									{
										editorsForTarget.Add(aie.GetInstanceID());
										break;
									}
								}
							}
						}
					}

					// If there's a mismatch, fix it before CreateEditor runs its check
					if (cacheCount != editorsForTarget.Count)
					{
						fixCacheMethod.Invoke(null, new object[] { instanceId, editorsForTarget.ToArray() });
					}
				}
			}
			catch (Exception)
			{
				// Silently ignore - this is a best-effort fix
			}
		}



		private static bool ArrayEquals(UnityEngine.Object[] a, UnityEngine.Object[] b)
		{
			if (a == null && b == null) return true;
			if (a == null || b == null) return false;
			if (a.Length != b.Length) return false;
			for (int i = 0; i < a.Length; i++)
			{
				if (a[i] != b[i]) return false;
			}
			return true;
		}

	public override void OnDisable()
	{
		if (m_GameObjectEditor != null)
		{
			DestroyImmediate(m_GameObjectEditor);
			m_GameObjectEditor = null;
		}
		
		if (m_DefaultEditor != null)
		{
			DestroyImmediate(m_DefaultEditor);
			m_DefaultEditor = null;
		}

		base.OnDisable();
	}

		public override void SaveChanges()
		{
			// The default tabs modify m_DefaultEditor.serializedObject, so apply those changes first.
			if (m_DefaultEditor != null)
			{
				var so = m_DefaultEditor.serializedObject;
				if (so != null && so.hasModifiedProperties)
					so.ApplyModifiedProperties();

				var extra = TryGetExtraDataSerializedObject(m_DefaultEditor);
				if (extra != null && extra.hasModifiedProperties)
					extra.ApplyModifiedProperties();
			}

			// Now call base SaveChanges which will apply our serializedObject and import.
			base.SaveChanges();

			// Refresh default editor UI state after import.
			if (m_DefaultEditor != null)
			{
				m_DefaultEditor.serializedObject?.Update();
				TryGetExtraDataSerializedObject(m_DefaultEditor)?.Update();
				Repaint();
			}
		}

		public override void OnInspectorGUI()
		{
			// Update serialized objects at the beginning
			// First update our serializedObject
			serializedObject.Update();
			if (extraDataSerializedObject != null)
				extraDataSerializedObject.Update();
			
			// Also update the default editor's serializedObject
			// The tabs modify the default editor's serializedObject, so it needs to be in sync
			if (m_DefaultEditor != null)
			{
				m_DefaultEditor.serializedObject?.Update();
				TryGetExtraDataSerializedObject(m_DefaultEditor)?.Update();
			}

			// Draw Unity's default ModelImporterEditor (includes tabs)
			// Skip ApplyRevertGUI so we can call it on our instance
			if (m_DefaultEditor != null)
			{
				// Better approach: Manually draw the tabbed interface using reflection
				DrawTabbedInterface();
			}
			else
			{
				// Fallback: draw default inspector if we can't create the default editor
				var doDrawMethod = typeof(UnityEditor.Editor).GetMethod("DoDrawDefaultInspector", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(SerializedObject) }, null);
				if (doDrawMethod != null)
				{
					doDrawMethod.Invoke(null, new object[] { serializedObject });
					if (extraDataType != null && extraDataSerializedObject != null)
						doDrawMethod.Invoke(null, new object[] { extraDataSerializedObject });
				}
			}

			// Add our custom YUCP Patch Export UI
			if (targets != null && targets.Length > 0 && targets[0] is ModelImporter importer)
			{
				YUCPFbxDerivedImportSettingsGUI.Draw(importer);
			}

			// Ensure GUI is enabled after the YUCP UI
			GUI.enabled = true;
			
			// IMPORTANT: Commit UI edits into the serialized objects so changes persist between repaints.
			// This does NOT import; importing still happens when Apply is pressed (SaveChanges()).
			if (m_DefaultEditor != null)
			{
				TryGetExtraDataSerializedObject(m_DefaultEditor)?.ApplyModifiedProperties();
				m_DefaultEditor.serializedObject?.ApplyModifiedProperties();
			}
			
			if (extraDataSerializedObject != null)
				extraDataSerializedObject.ApplyModifiedProperties();
			serializedObject.ApplyModifiedProperties();
			
			// Call ApplyRevertGUI on OUR instance - this draws the buttons and ensures they work with our state
			ApplyRevertGUI();
		}

		private void DrawTabbedInterface()
		{
			if (m_DefaultEditor == null)
			{
				Debug.LogWarning("[YUCPFbxEditor] m_DefaultEditor is null, cannot draw tabs");
				return;
			}

			var defaultEditorType = m_DefaultEditor.GetType();
			
			// tabs is a PROTECTED property, not public! Need to use NonPublic binding flags
			var tabsProp = defaultEditorType.GetProperty("tabs", BindingFlags.NonPublic | BindingFlags.Instance);
			if (tabsProp == null)
			{
				// Try base class (AssetImporterTabbedEditor)
				tabsProp = defaultEditorType.BaseType?.GetProperty("tabs", BindingFlags.NonPublic | BindingFlags.Instance);
			}
			
			var tabNamesField = defaultEditorType.GetField("m_TabNames", BindingFlags.NonPublic | BindingFlags.Instance);
			if (tabNamesField == null)
			{
				tabNamesField = defaultEditorType.BaseType?.GetField("m_TabNames", BindingFlags.NonPublic | BindingFlags.Instance);
			}
			
			var activeEditorIndexField = defaultEditorType.GetField("m_ActiveEditorIndex", BindingFlags.NonPublic | BindingFlags.Instance);
			if (activeEditorIndexField == null)
			{
				activeEditorIndexField = defaultEditorType.BaseType?.GetField("m_ActiveEditorIndex", BindingFlags.NonPublic | BindingFlags.Instance);
			}
			
			var activeTabProp = defaultEditorType.GetProperty("activeTab", BindingFlags.Public | BindingFlags.Instance);
			if (activeTabProp == null)
			{
				activeTabProp = defaultEditorType.BaseType?.GetProperty("activeTab", BindingFlags.Public | BindingFlags.Instance);
			}
			
			// If tabs property not found, try m_Tabs field
			Array tabsArray = null;
			if (tabsProp != null)
			{
				tabsArray = tabsProp.GetValue(m_DefaultEditor) as Array;
			}
			else
			{
				// Try accessing m_Tabs field directly
				var mTabsField = defaultEditorType.GetField("m_Tabs", BindingFlags.NonPublic | BindingFlags.Instance);
				if (mTabsField == null)
				{
					mTabsField = defaultEditorType.BaseType?.GetField("m_Tabs", BindingFlags.NonPublic | BindingFlags.Instance);
				}
				if (mTabsField != null)
				{
					tabsArray = mTabsField.GetValue(m_DefaultEditor) as Array;
				}
			}

			if (tabsArray == null || tabNamesField == null || activeEditorIndexField == null || activeTabProp == null)
			{
				Debug.LogWarning("[YUCPFbxEditor] Missing required fields/properties for tabbed interface, falling back to OnInspectorGUI");
				// Fallback: just call OnInspectorGUI
				m_DefaultEditor.OnInspectorGUI();
				return;
			}
			var tabNames = tabNamesField.GetValue(m_DefaultEditor) as string[];
			int activeEditorIndex = (int)(activeEditorIndexField.GetValue(m_DefaultEditor) ?? 0);

			if (tabsArray == null || tabNames == null || activeEditorIndex < 0 || activeEditorIndex >= tabsArray.Length)
			{
				Debug.LogWarning($"[YUCPFbxEditor] Invalid tab state. tabsArray: {tabsArray != null}, tabNames: {tabNames != null}, activeEditorIndex: {activeEditorIndex}, tabsArray.Length: {tabsArray?.Length ?? 0}");
				m_DefaultEditor.OnInspectorGUI();
				return;
			}

			// Draw C4D deprecation warning if needed
			if (targets != null)
			{
				foreach (var target in targets)
				{
					if (target != null && target is AssetImporter importer)
					{
						var path = importer.assetPath;
						if (path.EndsWith(".c4d", StringComparison.OrdinalIgnoreCase))
						{
							EditorGUILayout.HelpBox("Starting with the Unity 2019.3 release, direct import of Cinema4D files will require an external plugin. Keep an eye on our External Tools forum for updates.\n\nPlease note that FBX files exported from Cinema4D will still be supported.", MessageType.Warning);
							break;
						}
					}
				}
			}

			// Draw tab toolbar
			using (new EditorGUI.DisabledScope(false))
			{
				GUI.enabled = true;
				using (new GUILayout.HorizontalScope())
				{
					GUILayout.FlexibleSpace();
					using (var changeCheck = new EditorGUI.ChangeCheckScope())
					{
						activeEditorIndex = GUILayout.Toolbar(activeEditorIndex, tabNames, "LargeButton", GUI.ToolbarButtonSize.FitToContents);
						if (changeCheck.changed)
						{
							activeEditorIndexField.SetValue(m_DefaultEditor, activeEditorIndex);
							EditorPrefs.SetInt(defaultEditorType.Name + "ActiveEditorIndex", activeEditorIndex);
							var newActiveTab = tabsArray.GetValue(activeEditorIndex);
							
							// activeTab has a private setter, so we need to use reflection to set it
							// Get the setter method and invoke it
							var setter = activeTabProp.GetSetMethod(true); // true = include non-public
							if (setter != null)
							{
								setter.Invoke(m_DefaultEditor, new object[] { newActiveTab });
							}
							else
							{
								// For auto-implemented properties, try the backing field directly
								// The backing field name follows C# compiler convention: <PropertyName>k__BackingField
								var backingField = defaultEditorType.BaseType?.GetField("<activeTab>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
								backingField?.SetValue(m_DefaultEditor, newActiveTab);
							}
						}
					}
					GUILayout.FlexibleSpace();
				}
			}

			// Draw active tab content
			// Since we can't set activeTab, get the tab directly from tabsArray using activeEditorIndex
			var activeTab = tabsArray.GetValue(activeEditorIndex);
			if (activeTab != null)
			{
				GUILayout.Space(5f);
				var tabType = activeTab.GetType();
				
				// Try to update activeTab property if possible (for Unity's internal state)
				var setter = activeTabProp.GetSetMethod(true);
				if (setter != null)
				{
					try
					{
						setter.Invoke(m_DefaultEditor, new object[] { activeTab });
					}
					catch
					{
						// Ignore if we can't set it
					}
				}
				
				// Special handling for Rig tab - intercept to manually enable configure button
				if (tabType.Name == "ModelImporterRigEditor")
				{
					DrawRigTabWithManualButtonControl(activeTab, tabType);
				}
				else
				{
					// For other tabs, just call OnInspectorGUI normally
					var onInspectorGUIMethod = tabType.GetMethod("OnInspectorGUI", BindingFlags.Public | BindingFlags.Instance);
					if (onInspectorGUIMethod != null)
					{
						onInspectorGUIMethod.Invoke(activeTab, null);
					}
					else
					{
						Debug.LogWarning($"[YUCPFbxEditor] OnInspectorGUI method not found on {tabType.Name}");
					}
				}
			}
			else
			{
				Debug.LogWarning($"[YUCPFbxEditor] Tab at index {activeEditorIndex} is null");
			}
		}

		private void DrawRigTabWithManualButtonControl(object rigTab, System.Type rigTabType)
		{
			// Ensure Avatar is loaded before drawing
			var mAvatarField = rigTabType.GetField("m_Avatar", BindingFlags.NonPublic | BindingFlags.Instance);
			Avatar avatar = null;
			
			if (mAvatarField == null)
			{
				Debug.LogError("[YUCPFbxEditor] m_AvatarField is null! Cannot access m_Avatar field.");
				return;
			}
			
			if (targets == null || targets.Length == 0 || !(targets[0] is ModelImporter importer))
			{
				Debug.LogError($"[YUCPFbxEditor] Invalid targets. targets: {targets != null}, count: {targets?.Length ?? 0}, is ModelImporter: {targets?[0] is ModelImporter}");
				return;
			}
			
			// Try to get current Avatar
			avatar = mAvatarField.GetValue(rigTab) as Avatar;
			
			// If null, try to load it using multiple methods
			if (avatar == null)
			{
				// Method 1: Try loading all sub-assets and find the Avatar (most reliable for sub-assets)
				var allAssets = AssetDatabase.LoadAllAssetsAtPath(importer.assetPath);
				if (allAssets != null)
				{
					foreach (var asset in allAssets)
					{
						if (asset is Avatar av)
						{
							avatar = av;
							break;
						}
					}
				}
				
				// Method 2: Try loading directly from the asset path
				if (avatar == null)
				{
					avatar = AssetDatabase.LoadAssetAtPath<Avatar>(importer.assetPath);
				}
				
				// Method 3: Try loading the GameObject and getting its Avatar from Animator
				if (avatar == null)
				{
					var gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(importer.assetPath);
					if (gameObject != null)
					{
						var animator = gameObject.GetComponent<Animator>();
						if (animator != null && animator.avatar != null)
						{
							avatar = animator.avatar;
						}
					}
				}
				
				// Set it on the tab if we found it
				if (avatar != null && mAvatarField != null)
				{
					mAvatarField.SetValue(rigTab, avatar);
				}
			}
			
			// Call the tab's OnInspectorGUI
			var onInspectorGUIMethod = rigTabType.GetMethod("OnInspectorGUI", BindingFlags.Public | BindingFlags.Instance);
			if (onInspectorGUIMethod != null)
			{
				// Store original GUI state
				bool originalEnabled = GUI.enabled;
				
				// Ensure GUI is enabled - the DisabledScope will handle the actual disabling
				GUI.enabled = true;
				
				onInspectorGUIMethod.Invoke(rigTab, null);
				
				// Verify m_Avatar is still set and re-set if needed
				if (avatar != null && mAvatarField != null)
				{
					var avatarAfterDraw = mAvatarField.GetValue(rigTab) as Avatar;
					if (avatarAfterDraw == null)
					{
						// Re-set it - this ensures it's available for the next frame
						mAvatarField.SetValue(rigTab, avatar);
					}
				}
				
				// Restore GUI state
				GUI.enabled = originalEnabled;
			}
			else
			{
				Debug.LogError("[YUCPFbxEditor] OnInspectorGUI method not found on Rig tab");
			}
		}
	}
}


