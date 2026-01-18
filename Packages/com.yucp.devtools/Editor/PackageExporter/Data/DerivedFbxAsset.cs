using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Asset containing information needed to recreate a derived FBX using binary patching.
	/// Uses HDiffPatch binary diff files instead of semantic operations.
	/// </summary>
	[UnityEngine.Scripting.Preserve]
	public class DerivedFbxAsset : ScriptableObject
	{
		[SerializeField] public string baseFbxGuid; // GUID of the base FBX this derives from (for direct targeting)
		[SerializeField] public string derivedFbxGuid; // GUID of the original derived FBX (preserved for prefab compatibility)
		[SerializeField] public string sourceManifestId; // Hash of base FBX structure for compatibility checking
		[SerializeField] public string targetFbxName; // Name for the derived FBX that will be created
		[SerializeField] public string originalDerivedFbxPath; // Original path of the derived FBX (for reconstruction)
		[SerializeField] public string hdiffFilePath; // Path to the .hdiff binary diff file
	[SerializeField] public string baseFbxHash; // Optional hash of base FBX for additional verification
	[SerializeField] public bool overrideOriginalReferences = false; // If true, replace all references to original FBX with new FBX
	[SerializeField] public string embeddedMetaFileContent; // Embedded meta file content from original derived FBX (preserves humanoid Avatar mappings)
		
		// ============================================================
		// Kitbash mode extensions (requires YUCP_KITBASH_ENABLED)
		// ============================================================
		
		/// <summary>
		/// Derived mode - determines how the base is resolved.
		/// </summary>
		[SerializeField] public DerivedMode mode = DerivedMode.SingleBaseHdiff;
		
		/// <summary>
		/// Canonical JSON of the kitbash recipe (KitbashRecipeHdiff mode only).
		/// </summary>
		[SerializeField] public string kitbashRecipeJson;
		
		/// <summary>
		/// Hash of the recipe for cache validation (KitbashRecipeHdiff mode only).
		/// </summary>
		[SerializeField] public string recipeHash;
		
		/// <summary>
		/// GUIDs of source FBXs required for reconstruction (KitbashRecipeHdiff mode only).
		/// </summary>
		[SerializeField] public string[] requiredSourceGuids;

		/// <summary>
		/// Per-triangle source part index mapping (KitbashRecipeHdiff mode only).
		/// Indexes into KitbashRecipe.parts (0-based).
		/// </summary>
		[SerializeField] public int[] kitbashSourcePartIndices;

		/// <summary>
		/// Per-triangle source triangle index mapping (KitbashRecipeHdiff mode only).
		/// Each entry is a triangle index into the source mesh's triangles array.
		/// </summary>
		[SerializeField] public int[] kitbashSourceTriangleIndices;
		
		[SerializeField] public Policy policy = new Policy();
		[SerializeField] public UIHints uiHints = new UIHints();
		[SerializeField] public SeedMaps seedMaps = new SeedMaps();
		
		[Serializable]
		public struct Policy
		{
			public float autoApplyThreshold;
			public float reviewThreshold;
			public bool strictTopology;
		}
		
		[Serializable]
		public struct UIHints
		{
			public string friendlyName;
			public Texture2D thumbnail;
			public string category;
		}
		
		[Serializable]
		public class SeedMaps
		{
			public List<StringPair> boneAliases = new List<StringPair>();
			public List<StringPair> materialAliases = new List<StringPair>();
			public List<StringPair> blendshapeAliases = new List<StringPair>();
		}
		
		[Serializable]
		public struct StringPair
		{
			public string from;
			public string to;
		}
	}
}
