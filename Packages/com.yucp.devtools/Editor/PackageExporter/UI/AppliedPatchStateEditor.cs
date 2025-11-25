using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	[CustomEditor(typeof(AppliedPatchState))]
	public class AppliedPatchStateEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			var state = (AppliedPatchState)target;
			EditorGUILayout.LabelField("Patch", state.patch != null ? state.patch.name : "(none)");
			EditorGUILayout.LabelField("Target Manifest", state.targetManifestId);
			EditorGUILayout.LabelField("Map Id", state.correspondenceMapId);
			EditorGUILayout.LabelField("Confidence", state.confidenceScore.ToString("0.##"));
			EditorGUILayout.Space();

			bool enabled = EditorGUILayout.Toggle("Enabled", state.enabledForTarget);
			if (enabled != state.enabledForTarget)
			{
				Undo.RecordObject(state, "Toggle Patch");
				state.ToggleEnable(enabled);
			}

			EditorGUILayout.Space();
			if (GUILayout.Button("Rebuild"))
			{
				Rebuild(state);
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Derived Assets", EditorStyles.boldLabel);
			if (state.producedDerivedAssets == null || state.producedDerivedAssets.Count == 0)
			{
				EditorGUILayout.HelpBox("No derived assets recorded.", MessageType.Info);
			}
			else
			{
				foreach (var a in state.producedDerivedAssets.Where(a => a != null))
					EditorGUILayout.ObjectField(a, typeof(Object), false);
			}
		}

		private void Rebuild(AppliedPatchState state)
		{
			// Find target FBX by manifest id
			var modelGuids = AssetDatabase.FindAssets("t:Model");
			foreach (var guid in modelGuids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var m = ManifestBuilder.BuildForFbx(path);
				if (m.manifestId == state.targetManifestId)
				{
					var patch = state.patch;
					if (patch == null)
					{
						EditorUtility.DisplayDialog("Rebuild", "Patch reference is missing.", "OK");
						return;
					}
					var newState = Applicator.ApplyToTarget(path, patch, state.confidenceScore, state.correspondenceMapId, out var _);
					Selection.activeObject = newState;
					EditorGUIUtility.PingObject(newState);
					return;
				}
			}
			// Locator fallback
			string picked = EditorUtility.OpenFilePanel("Locate Base FBX", Application.dataPath, "fbx");
			if (!string.IsNullOrEmpty(picked))
			{
				string rel = ToUnityRelative(picked);
				if (!string.IsNullOrEmpty(rel))
				{
					var newState = Applicator.ApplyToTarget(rel, state.patch, state.confidenceScore, state.correspondenceMapId, out var _);
					Selection.activeObject = newState;
					EditorGUIUtility.PingObject(newState);
				}
			}
		}

		private string ToUnityRelative(string absolute)
		{
			string project = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..")).Replace("\\", "/");
			string normalized = absolute.Replace("\\", "/");
			if (normalized.StartsWith(project))
			{
				var rel = normalized.Substring(project.Length).TrimStart('/');
				return rel;
			}
			return null;
		}
	}
}




