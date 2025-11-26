using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Consolidated asset containing all information needed to recreate a derived FBX.
	/// All patch data is embedded directly in this asset (no separate sidecar files).
	/// Uses sparse storage for blendshape data (only non-zero deltas) for efficiency.
	/// </summary>
	[UnityEngine.Scripting.Preserve]
	public class DerivedFbxAsset : ScriptableObject
	{
		[SerializeField] public string baseFbxGuid; // GUID of the base FBX this derives from (for direct targeting)
		[SerializeField] public string derivedFbxGuid; // GUID of the original derived FBX (preserved for prefab compatibility)
		[SerializeField] public string sourceManifestId; // Hash of base FBX structure for compatibility checking
		[SerializeField] public string targetFbxName; // Name for the derived FBX that will be created
		[SerializeField] public string originalDerivedFbxPath; // Original path of the derived FBX (for reconstruction)
		
		[SerializeField] public Policy policy = new Policy();
		[SerializeField] public UIHints uiHints = new UIHints();
		[SerializeField] public SeedMaps seedMaps = new SeedMaps();
		
		// Embedded operations with all data included (no external asset references)
		[SerializeReference] public List<EmbeddedOperation> operations = new List<EmbeddedOperation>();
		
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
		
		[Serializable]
		public abstract class EmbeddedOperation
		{
			public abstract string GetSummary();
		}
		
		[Serializable]
		public class EmbeddedMeshDeltaOp : EmbeddedOperation
		{
			public string targetMeshName;
			public int vertexCount;
			// Embedded mesh delta data (previously in MeshDeltaAsset)
			public Vector3[] positionDeltas;
			public Vector3[] normalDeltas;
			public Vector3[] tangentDeltas;
			
			public override string GetSummary() => $"MeshDelta → {targetMeshName}";
		}
		
		[Serializable]
		public class EmbeddedUVLayerOp : EmbeddedOperation
		{
			public string targetMeshName;
			public int channel;
			// Embedded UV data (previously in UVLayerAsset)
			public Vector2[] uvs;
			public bool replaceExisting;
			
			public override string GetSummary() => $"UV {(replaceExisting ? "Replace" : "Add")} ch{channel} → {targetMeshName}";
		}
		
		[Serializable]
		public class EmbeddedMaterialOverrideOp : EmbeddedOperation
		{
			public string targetRendererPath;
			// Material override data could be embedded here if needed
			// For now, keeping reference-based approach if materials are external
			// Note: MaterialOverrideAsset is optional and may not be available in temp package
			[System.NonSerialized]
			public UnityEngine.Object overridesAsset;
			
			public override string GetSummary() => $"MaterialOverride → {targetRendererPath}";
		}
		
		[Serializable]
		public class EmbeddedBlendshapeOp : EmbeddedOperation
		{
			public string targetMeshName;
			public string blendshapeName;
			public float scale = 1f;
			// Sparse storage: only store non-zero deltas
			public float frameWeight = 100f;
			
			// Vertex deltas (sparse)
			public List<int> vertexIndices;
			public List<Vector3> deltaVertices;
			
			// Normal deltas (sparse)
			public List<int> normalIndices;
			public List<Vector3> deltaNormals;
			
			// Tangent deltas (sparse)
			public List<int> tangentIndices;
			public List<Vector3> deltaTangents;
			
			// Helper methods to convert between sparse and full arrays
			public Vector3[] GetFullDeltaVertices(int vertexCount)
			{
				Vector3[] full = new Vector3[vertexCount];
				if (vertexIndices != null && deltaVertices != null)
				{
					for (int i = 0; i < vertexIndices.Count && i < deltaVertices.Count; i++)
					{
						int idx = vertexIndices[i];
						if (idx >= 0 && idx < vertexCount)
							full[idx] = deltaVertices[i];
					}
				}
				return full;
			}
			
			public Vector3[] GetFullDeltaNormals(int vertexCount)
			{
				Vector3[] full = new Vector3[vertexCount];
				if (normalIndices != null && deltaNormals != null)
				{
					for (int i = 0; i < normalIndices.Count && i < deltaNormals.Count; i++)
					{
						int idx = normalIndices[i];
						if (idx >= 0 && idx < vertexCount)
							full[idx] = deltaNormals[i];
					}
				}
				return full;
			}
			
			public Vector3[] GetFullDeltaTangents(int vertexCount)
			{
				Vector3[] full = new Vector3[vertexCount];
				if (tangentIndices != null && deltaTangents != null)
				{
					for (int i = 0; i < tangentIndices.Count && i < deltaTangents.Count; i++)
					{
						int idx = tangentIndices[i];
						if (idx >= 0 && idx < vertexCount)
							full[idx] = deltaTangents[i];
					}
				}
				return full;
			}
			
			public override string GetSummary() => $"Blendshape {blendshapeName} (x{scale:0.##}) → {targetMeshName}";
		}
		
		[Serializable]
		public class EmbeddedSkinWeightDeltaOp : EmbeddedOperation
		{
			public string targetMeshName;
			public int maxInfluences = 4;
			public int[] vertexIndices;
			public int[] boneIndices;
			public float[] weights;
			
			public override string GetSummary() => $"SkinWeightDelta → {targetMeshName}";
		}
		
		[Serializable]
		public class EmbeddedAnimationOverrideOp : EmbeddedOperation
		{
			public string clipName;
			public AnimationClip clip;
			public bool additive;
			
			public override string GetSummary() => $"Animation {(additive ? "Additive" : "Replace")} → {clipName}";
		}
		
		[Serializable]
		public class EmbeddedBoneHierarchyOp : EmbeddedOperation
		{
			public string bonePath;
			public Vector3 localPosition;
			public Quaternion localRotation;
			public Vector3 localScale;
			public string parentBonePath;
			
			public override string GetSummary() => $"BoneHierarchy → {bonePath}";
		}
		
		[Serializable]
		public class EmbeddedMaterialAssignmentOp : EmbeddedOperation
		{
			public string rendererPath;
			public string[] materialNames;
			public string[] materialGuids; // GUIDs for material references
			
			public override string GetSummary() => $"MaterialAssignment → {rendererPath}";
		}
		
		[Serializable]
		public class EmbeddedMeshReorganizationOp : EmbeddedOperation
		{
			public enum ReorganizationType
			{
				Add,
				Remove,
				Rename
			}
			
			public ReorganizationType type;
			public string meshName;
			public string newMeshName; // For rename operations
			public string meshPath;
			
			public override string GetSummary() => $"MeshReorganization {type} → {meshName}";
		}
	}
}

