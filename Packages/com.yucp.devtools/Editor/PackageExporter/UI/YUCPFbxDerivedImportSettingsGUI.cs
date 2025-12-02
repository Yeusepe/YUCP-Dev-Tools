using System;
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
			}

			if (EditorGUI.EndChangeCheck())
			{
				importer.userData = JsonUtility.ToJson(settings);
				EditorUtility.SetDirty(importer);
				AssetDatabase.SaveAssets();
			}
		}
	}
}




