using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Distributable derived FBX package that describes semantic operations to transform a base FBX (sourceManifestId)
	/// into the author's modified version without shipping the original FBX.
	/// </summary>
	public class PatchPackage : ScriptableObject
	{
		[SerializeField] public string sourceManifestId;
		[SerializeField] public string baseFbxGuid; // GUID of the base FBX this patch targets (for direct targeting)
		[SerializeField] public Policy policy = new Policy();
		[SerializeField] public UIHints uiHints = new UIHints();
		[SerializeField] public SeedMaps seedMaps = new SeedMaps();

		// Polymorphic operation log (append-only)
		[SerializeReference] public List<Operation> ops = new List<Operation>();

		[Serializable]
		public struct Policy
		{
			public float autoApplyThreshold;   // score >= auto → apply silently
			public float reviewThreshold;      // review <= score < auto → notify for review
			public bool strictTopology;        // hard stop for topology changes if true
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

		[Serializable]
		public abstract class Operation
		{
			public abstract string GetSummary();
		}

		[Serializable]
		public class MeshDeltaOp : Operation
		{
			public string targetMeshName;
			public MeshDeltaAsset meshDelta;
			public override string GetSummary() => $"MeshDelta → {targetMeshName}";
		}

		[Serializable]
		public class UVLayerOp : Operation
		{
			public string targetMeshName;
			public int channel;
			public UVLayerAsset uvLayer;
			public bool replaceExisting;
			public override string GetSummary() => $"UV {(replaceExisting ? "Replace" : "Add")} ch{channel} → {targetMeshName}";
		}

		[Serializable]
		public class MaterialOverrideOp : Operation
		{
			public string targetRendererPath; // path within prefab/asset, if known
			public MaterialOverrideAsset overridesAsset;
			public override string GetSummary() => $"MaterialOverride → {targetRendererPath}";
		}

		[Serializable]
		public class BlendshapeOp : Operation
		{
			public string targetMeshName;
			public string blendshapeName;
			public float scale = 1f;
			public BlendshapeFrameAsset synthesizedFrame; // optional
			public override string GetSummary() => $"Blendshape {blendshapeName} (x{scale:0.##}) → {targetMeshName}";
		}

		[Serializable]
		public class SkinWeightDeltaOp : Operation
		{
			public string targetMeshName;
			public int maxInfluences = 4;
			// Sparse reweighting: indices and new weights
			public int[] vertexIndices;
			public int[] boneIndices; // flattened, length == vertexIndices.Length * maxInfluences
			public float[] weights;   // flattened, length == vertexIndices.Length * maxInfluences
			public override string GetSummary() => $"SkinWeightDelta → {targetMeshName}";
		}

		[Serializable]
		public class AnimationOverrideOp : Operation
		{
			public string clipName;
			public AnimationClip clip; // accessory included in package; retarget done at import
			public bool additive;
			public override string GetSummary() => $"Animation {(additive ? "Additive" : "Replace")} → {clipName}";
		}
	}
}


