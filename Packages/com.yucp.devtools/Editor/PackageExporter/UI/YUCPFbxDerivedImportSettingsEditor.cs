using System;
using System.Linq;
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
	private UnityEditor.Editor m_GameObjectEditor; // Store the GameObject editor we create for assetTarget
	private static Type s_ModelImporterEditorType;
	private static System.Reflection.FieldInfo s_OnEnableCalledField;
	private static System.Reflection.MethodInfo s_GetInspectorCopyCountMethod;
	private static System.Reflection.MethodInfo s_ReleaseInspectorCopyMethod;

	/// <summary>
	/// Safely disposes an AssetImporterEditor by manually releasing inspector copies.
	/// This ensures inspector copies are released to prevent memory leaks.
	/// </summary>
	private static void SafeDisposeAssetImporterEditor(AssetImporterEditor editor)
	{
		if (editor == null)
			return;

		if (s_OnEnableCalledField == null)
		{
			s_OnEnableCalledField = typeof(AssetImporterEditor).GetField("m_OnEnableCalled", BindingFlags.NonPublic | BindingFlags.Instance);
		}

		var targetsInstanceIDField = typeof(AssetImporterEditor).GetField("m_TargetsInstanceID", BindingFlags.NonPublic | BindingFlags.Instance);
		var targetsInstanceID = targetsInstanceIDField?.GetValue(editor) as System.Collections.Generic.List<int>;
		
		bool onEnableCalled = s_OnEnableCalledField != null && (bool)(s_OnEnableCalledField.GetValue(editor) ?? false);
		bool onEnableCompleted = targetsInstanceID != null && targetsInstanceID.Count > 0;

		if (onEnableCalled && onEnableCompleted && targetsInstanceID != null)
		{
			foreach (int instanceID in targetsInstanceID)
			{
				try
				{
					if (s_ReleaseInspectorCopyMethod != null)
					{
						s_ReleaseInspectorCopyMethod.Invoke(null, new object[] { instanceID, editor });
					}
				}
				catch (System.Exception)
				{
				}
			}
			
			if (targetsInstanceIDField != null)
			{
				targetsInstanceIDField.SetValue(editor, new System.Collections.Generic.List<int>());
			}
		}
		
		if (s_OnEnableCalledField != null)
		{
			s_OnEnableCalledField.SetValue(editor, true);
		}
	}

		static YUCPFbxDerivedImportSettingsEditor()
		{
			var assembly = typeof(ModelImporter).Assembly;
			s_ModelImporterEditorType = assembly.GetType("UnityEditor.ModelImporterEditor");
			
			var assetImporterEditorType = typeof(AssetImporterEditor);
			s_GetInspectorCopyCountMethod = assetImporterEditorType.GetMethod("GetInspectorCopyCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			s_ReleaseInspectorCopyMethod = assetImporterEditorType.GetMethod("ReleaseInspectorCopy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
		}

		public override void OnEnable()
	{
		if (m_DefaultEditor != null)
		{
			if (m_DefaultEditor is AssetImporterEditor existingAssetEditor)
			{
				SafeDisposeAssetImporterEditor(existingAssetEditor);
			}
			DestroyImmediate(m_DefaultEditor);
			m_DefaultEditor = null;
		}
		
		if (m_GameObjectEditor != null)
		{
			DestroyImmediate(m_GameObjectEditor);
			m_GameObjectEditor = null;
		}
		
		if (s_ModelImporterEditorType != null && targets != null && targets.Length > 0)
		{
			var targetInstanceIDs = targets.Select(t => t.GetInstanceID()).ToHashSet();
			
			var existingEditors = Resources.FindObjectsOfTypeAll(s_ModelImporterEditorType)
				.Cast<UnityEditor.Editor>()
				.Where(e => e is AssetImporterEditor aie && 
				           aie.targets != null && 
				           aie.targets.Length > 0 &&
				           aie.targets.Any(t => targetInstanceIDs.Contains(t.GetInstanceID())))
				.ToList();
			
			foreach (var existingEditor in existingEditors)
			{
				if (existingEditor is AssetImporterEditor assetEditor)
				{
					SafeDisposeAssetImporterEditor(assetEditor);
					DestroyImmediate(existingEditor);
				}
			}
		}
		
		base.OnEnable();
		
		var targetsInstanceIDField = typeof(AssetImporterEditor).GetField("m_TargetsInstanceID", BindingFlags.NonPublic | BindingFlags.Instance);
		var ourTargetsInstanceID = targetsInstanceIDField?.GetValue(this) as System.Collections.Generic.List<int>;
		
		if (ourTargetsInstanceID != null && ourTargetsInstanceID.Count > 0)
		{
			foreach (int instanceID in ourTargetsInstanceID)
			{
				try
				{
					if (s_ReleaseInspectorCopyMethod != null)
					{
						s_ReleaseInspectorCopyMethod.Invoke(null, new object[] { instanceID, this });
					}
				}
				catch (System.Exception)
				{
				}
			}
		}
		
		if (s_ModelImporterEditorType != null && targets != null && targets.Length > 0)
		{
			m_DefaultEditor = UnityEditor.Editor.CreateEditor(targets, s_ModelImporterEditorType);
			
			if (m_DefaultEditor != null && m_DefaultEditor is AssetImporterEditor defaultAssetEditor)
			{
				var assetTargetProp = typeof(AssetImporterEditor).GetProperty("assetTarget", BindingFlags.NonPublic | BindingFlags.Instance);
				var assetTarget = assetTargetProp?.GetValue(defaultAssetEditor);
				
				if (assetTarget == null && targets != null && targets.Length > 0)
				{
					if (targets[0] is ModelImporter importer)
					{
						var importedGameObject = AssetDatabase.LoadAssetAtPath<GameObject>(importer.assetPath);
						if (importedGameObject != null)
						{
							var internalSetMethod = typeof(AssetImporterEditor).GetMethod("InternalSetAssetImporterTargetEditor", BindingFlags.NonPublic | BindingFlags.Instance);
							if (internalSetMethod != null)
							{
								if (m_GameObjectEditor != null)
								{
									DestroyImmediate(m_GameObjectEditor);
									m_GameObjectEditor = null;
								}
								
								m_GameObjectEditor = UnityEditor.Editor.CreateEditor(importedGameObject);
								if (m_GameObjectEditor != null)
								{
									internalSetMethod.Invoke(defaultAssetEditor, new object[] { m_GameObjectEditor });
								}
							}
						}
					}
				}
				
				if (defaultAssetEditor.targets == null || defaultAssetEditor.targets.Length == 0)
				{
					var targetsField = typeof(AssetImporterEditor).GetField("targets", BindingFlags.Public | BindingFlags.Instance);
					if (targetsField != null)
					{
						targetsField.SetValue(defaultAssetEditor, targets);
					}
				}
			}
		}
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
			if (m_DefaultEditor is AssetImporterEditor defaultAssetEditor)
			{
				SafeDisposeAssetImporterEditor(defaultAssetEditor);
			}
			DestroyImmediate(m_DefaultEditor);
			m_DefaultEditor = null;
		}
		
		var targetsInstanceIDField = typeof(AssetImporterEditor).GetField("m_TargetsInstanceID", BindingFlags.NonPublic | BindingFlags.Instance);
		if (targetsInstanceIDField != null)
		{
			var ourTargetsInstanceID = targetsInstanceIDField.GetValue(this) as System.Collections.Generic.List<int>;
			if (ourTargetsInstanceID != null && ourTargetsInstanceID.Count > 0)
			{
				targetsInstanceIDField.SetValue(this, new System.Collections.Generic.List<int>());
			}
		}
		
		base.OnDisable();
	}

		public override void SaveChanges()
		{
			// Apply changes from the default editor's serializedObject first
			// The tabs modify the default editor's serializedObject, so we need to apply those changes
			if (m_DefaultEditor != null && m_DefaultEditor is AssetImporterEditor defaultAssetEditor)
			{
				var defaultSerializedObjectField = typeof(AssetImporterEditor).GetField("serializedObject", BindingFlags.NonPublic | BindingFlags.Instance);
				if (defaultSerializedObjectField != null)
				{
					var defaultSerializedObject = defaultSerializedObjectField.GetValue(defaultAssetEditor) as SerializedObject;
					if (defaultSerializedObject != null && defaultSerializedObject.hasModifiedProperties)
					{
						defaultSerializedObject.ApplyModifiedProperties();
					}
				}
				
				var defaultExtraDataSerializedObjectProp = typeof(AssetImporterEditor).GetProperty("extraDataSerializedObject", BindingFlags.Public | BindingFlags.Instance);
				if (defaultExtraDataSerializedObjectProp != null)
				{
					var defaultExtraDataSerializedObject = defaultExtraDataSerializedObjectProp.GetValue(defaultAssetEditor) as SerializedObject;
					if (defaultExtraDataSerializedObject != null && defaultExtraDataSerializedObject.hasModifiedProperties)
					{
						defaultExtraDataSerializedObject.ApplyModifiedProperties();
					}
				}
			}
			
			// Now call base SaveChanges which will apply our serializedObject and import
			base.SaveChanges();
			
			// Call PostApply() on the default editor's tabs after importing
			// PostApply() is normally called by AssetImporterTabbedEditor.Apply(), but since we're calling
			// base.SaveChanges() directly, we need to manually call PostApply() on the tabs
			// This ensures tabs like ModelImporterRigEditor call ResetAvatar() to reload the Avatar
			if (m_DefaultEditor != null && m_DefaultEditor is AssetImporterEditor defaultAssetEditor2)
			{
				var defaultEditorType = m_DefaultEditor.GetType();
				var tabsProp = defaultEditorType.GetProperty("tabs", BindingFlags.NonPublic | BindingFlags.Instance);
				if (tabsProp == null)
				{
					tabsProp = defaultEditorType.BaseType?.GetProperty("tabs", BindingFlags.NonPublic | BindingFlags.Instance);
				}
				
				Array tabsArray = null;
				if (tabsProp != null)
				{
					tabsArray = tabsProp.GetValue(m_DefaultEditor) as Array;
				}
				else
				{
					// Try m_Tabs field directly
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
				
				if (tabsArray != null)
				{
					// Call PostApply() on each tab using reflection
					// BaseAssetImporterTabUI is internal, so we get the method from the tab's type
					foreach (var tab in tabsArray)
					{
						if (tab != null)
						{
							var tabType = tab.GetType();
							
							var postApplyMethod = tabType.GetMethod("PostApply", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
							if (postApplyMethod != null)
							{
								postApplyMethod.Invoke(tab, null);
							}
							
							// Also manually call ResetAvatar() on the Rig tab to reload the Avatar
							// And directly set m_Avatar if ResetAvatar() doesn't work (e.g., if assetTarget is null)
							if (tabType.Name == "ModelImporterRigEditor")
							{
								// First, try to load the imported GameObject to make assetTarget available
								if (defaultAssetEditor2.targets != null && defaultAssetEditor2.targets.Length > 0)
								{
									if (defaultAssetEditor2.targets[0] is ModelImporter importer)
									{
										// Load the imported GameObject to make assetTarget available
										var importedGameObject = AssetDatabase.LoadAssetAtPath<GameObject>(importer.assetPath);
										
										// Try ResetAvatar() first - it checks assetTarget, but we might have loaded it above
										var resetAvatarMethod = tabType.GetMethod("ResetAvatar", BindingFlags.NonPublic | BindingFlags.Instance);
										if (resetAvatarMethod != null)
										{
											try
											{
												resetAvatarMethod.Invoke(tab, null);
											}
											catch (Exception ex)
											{
												Debug.LogWarning($"[YUCPFbxEditor] ResetAvatar() failed: {ex.Message}");
											}
										}
										
										// Also directly load the Avatar and set it via reflection
										// ResetAvatar() might fail if assetTarget is null, so we do it directly
										var allAssets = AssetDatabase.LoadAllAssetsAtPath(importer.assetPath);
										Avatar avatar = null;
										foreach (var asset in allAssets)
										{
											if (asset is Avatar av)
											{
												avatar = av;
												break;
											}
										}
										
										if (avatar == null)
										{
											avatar = AssetDatabase.LoadAssetAtPath<Avatar>(importer.assetPath);
										}
										
										if (avatar != null)
										{
											var mAvatarField = tabType.GetField("m_Avatar", BindingFlags.NonPublic | BindingFlags.Instance);
											if (mAvatarField != null)
											{
												mAvatarField.SetValue(tab, avatar);
											}
										}
									}
								}
							}
						}
					}
				}
				
				// Call Apply() on the default editor to update its saved state
				// Apply() calls UpdateSavedData() which updates the saved state that IsSerializedDataEqual() compares against
				// This ensures HasModified() returns false after applying
				var applyMethod = typeof(AssetImporterEditor).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance);
				if (applyMethod != null)
				{
					applyMethod.Invoke(defaultAssetEditor2, null);
				}
				else
				{
					Debug.LogWarning("[YUCPFbxEditor] Apply() method not found");
				}
				
				// Also update the serializedObject to refresh its state from the target
				var defaultSerializedObjectField = typeof(AssetImporterEditor).GetField("serializedObject", BindingFlags.NonPublic | BindingFlags.Instance);
				if (defaultSerializedObjectField != null)
				{
					var defaultSerializedObject = defaultSerializedObjectField.GetValue(defaultAssetEditor2) as SerializedObject;
					if (defaultSerializedObject != null)
					{
						// If there are modified properties, apply them first
						if (defaultSerializedObject.hasModifiedProperties)
						{
							defaultSerializedObject.ApplyModifiedProperties();
						}
						
						// Update to sync with the target object (which was just imported)
						defaultSerializedObject.Update();
						defaultSerializedObject.SetIsDifferentCacheDirty();
						defaultSerializedObject.Update();
						
						// Force another update to recalculate hasModifiedProperties
						defaultSerializedObject.Update();
					}
				}
				
				// Also update extraDataSerializedObject
				var defaultExtraDataSerializedObjectProp = typeof(AssetImporterEditor).GetProperty("extraDataSerializedObject", BindingFlags.Public | BindingFlags.Instance);
				if (defaultExtraDataSerializedObjectProp != null)
				{
					var defaultExtraDataSerializedObject = defaultExtraDataSerializedObjectProp.GetValue(defaultAssetEditor2) as SerializedObject;
					if (defaultExtraDataSerializedObject != null)
					{
						defaultExtraDataSerializedObject.Update();
						defaultExtraDataSerializedObject.SetIsDifferentCacheDirty();
						defaultExtraDataSerializedObject.Update();
					}
				}
				
				// Force a repaint so the UI updates immediately
				defaultAssetEditor2.Repaint();
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
			AssetImporterEditor defaultAssetEditor = null;
			if (m_DefaultEditor != null && m_DefaultEditor is AssetImporterEditor)
			{
				defaultAssetEditor = m_DefaultEditor as AssetImporterEditor;
				var defaultSerializedObjectField = typeof(AssetImporterEditor).GetField("serializedObject", BindingFlags.NonPublic | BindingFlags.Instance);
				if (defaultSerializedObjectField != null)
				{
					var defaultSerializedObject = defaultSerializedObjectField.GetValue(defaultAssetEditor) as SerializedObject;
					if (defaultSerializedObject != null)
					{
						// Update the default editor's serializedObject to sync with the target
						defaultSerializedObject.Update();
					}
				}
				
				// Also update extraDataSerializedObject
				var defaultExtraDataSerializedObjectProp = typeof(AssetImporterEditor).GetProperty("extraDataSerializedObject", BindingFlags.Public | BindingFlags.Instance);
				if (defaultExtraDataSerializedObjectProp != null)
				{
					var defaultExtraDataSerializedObject = defaultExtraDataSerializedObjectProp.GetValue(defaultAssetEditor) as SerializedObject;
					if (defaultExtraDataSerializedObject != null)
					{
						defaultExtraDataSerializedObject.Update();
					}
				}
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

			// Sync state after drawing
			if (defaultAssetEditor != null)
			{
				var defaultSerializedObjectField = typeof(AssetImporterEditor).GetField("serializedObject", BindingFlags.NonPublic | BindingFlags.Instance);
				if (defaultSerializedObjectField != null)
				{
					var defaultSerializedObject = defaultSerializedObjectField.GetValue(defaultAssetEditor) as SerializedObject;
					if (defaultSerializedObject != null)
					{
						// Just update our serializedObject to sync state, but DON'T apply
						// The default editor's serializedObject will be applied in SaveChanges()
						serializedObject.Update();
					}
				}
				
				// Also sync extraDataSerializedObject state
				var defaultExtraDataSerializedObjectProp = typeof(AssetImporterEditor).GetProperty("extraDataSerializedObject", BindingFlags.Public | BindingFlags.Instance);
				if (defaultExtraDataSerializedObjectProp != null)
				{
					var defaultExtraDataSerializedObject = defaultExtraDataSerializedObjectProp.GetValue(defaultAssetEditor) as SerializedObject;
					if (defaultExtraDataSerializedObject != null && extraDataSerializedObject != null)
					{
						extraDataSerializedObject.Update();
					}
				}
			}
			
			// DO NOT apply modified properties here - that prevents further edits
			// Changes will be applied when SaveChanges() is called (when user clicks Apply)
			
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
								// Fallback: try to find and set the backing field directly
								// The property might have a backing field, but since it's auto-implemented,
								// we can't easily access it. Just log a warning.
								Debug.LogWarning("[YUCPFbxEditor] Could not set activeTab - setter not found");
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
			
			// Check if there are actual changes using our editor's HasModified()
			bool hasActualChanges = HasModified();
			
			// If Avatar exists and no changes, clear the tab's serializedObject's hasModifiedProperties
			if (avatar != null && !hasActualChanges)
			{
				var serializedObjectProp = rigTabType.GetProperty("serializedObject", BindingFlags.Public | BindingFlags.Instance);
				if (serializedObjectProp != null)
				{
					var tabSerializedObject = serializedObjectProp.GetValue(rigTab) as SerializedObject;
					if (tabSerializedObject != null)
					{
						// Apply any changes to clear the flag
						if (tabSerializedObject.hasModifiedProperties)
						{
							tabSerializedObject.ApplyModifiedProperties();
						}
						tabSerializedObject.Update();
					}
				}
			}
			
			// Call the tab's OnInspectorGUI
			var onInspectorGUIMethod = rigTabType.GetMethod("OnInspectorGUI", BindingFlags.Public | BindingFlags.Instance);
			if (onInspectorGUIMethod != null)
			{
				// Set m_Avatar before OnInspectorGUI is called
				// Try ResetAvatar() first, then fall back to direct assignment
				if (avatar != null && mAvatarField != null)
				{
					var resetAvatarMethod = rigTabType.GetMethod("ResetAvatar", BindingFlags.NonPublic | BindingFlags.Instance);
					if (resetAvatarMethod != null)
					{
						try
						{
							resetAvatarMethod.Invoke(rigTab, null);
							// Verify ResetAvatar() set it
							var avatarAfterReset = mAvatarField.GetValue(rigTab) as Avatar;
							if (avatarAfterReset == null)
							{
								// ResetAvatar() didn't work, set it directly
								mAvatarField.SetValue(rigTab, avatar);
							}
							else
							{
								avatar = avatarAfterReset; // Use the one from ResetAvatar()
							}
						}
						catch
						{
							// ResetAvatar() failed, set it directly
							mAvatarField.SetValue(rigTab, avatar);
						}
					}
					else
					{
						// No ResetAvatar() method, set it directly
						mAvatarField.SetValue(rigTab, avatar);
					}
				}
				
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


