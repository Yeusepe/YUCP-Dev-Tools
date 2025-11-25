using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Tracks application of a PatchPackage to a specific imported FBX target.
	/// Provides enable/disable and rebuild controls.
	/// </summary>
	public class AppliedPatchState : ScriptableObject
	{
		[SerializeField] public PatchPackage patch;
		[SerializeField] public string targetManifestId;
		[SerializeField] public string correspondenceMapId;
		[SerializeField] public float confidenceScore;
		[SerializeField] public bool enabledForTarget = true;
		[SerializeField] public List<UnityEngine.Object> producedDerivedAssets = new List<UnityEngine.Object>();

		// Audit trail
		[SerializeField] public string appliedByUser;
		[SerializeField] public DateTime appliedAtUtc = DateTime.UtcNow;
		[SerializeField] public string toolVersion = "1.0.0";
		[SerializeField] public string policyVersion = "1";

		public void ToggleEnable(bool enabled)
		{
			enabledForTarget = enabled;
			EditorUtility.SetDirty(this);
		}
	}
}




