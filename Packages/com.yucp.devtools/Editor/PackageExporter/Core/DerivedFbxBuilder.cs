using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
#if ENABLE_FBX_SDK
using Autodesk.Fbx;
#endif

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Builds a derived FBX file from a base FBX and DerivedFbxAsset operations.
	/// The created FBX preserves the original derived FBX's GUID for prefab compatibility.
	/// 
	/// This file incorporates code from BlendShare (com.triturbo.blendshare)
	/// BlendShare is MIT licensed: https://github.com/Tr1turbo/BlendShare
	/// Original implementation: Triturbo.BlendShapeShare.BlendShapeData.BlendShapeAppender
	/// </summary>
	public static class DerivedFbxBuilder
	{
		/// <summary>
		/// Builds a derived FBX file from base FBX and DerivedFbxAsset.
		/// Uses BlendShare's approach: import base FBX into FBX Scene, modify directly, then export.
		/// This preserves the original FBX structure (bones, materials, etc.).
		/// </summary>
		public static string BuildDerivedFbx(string baseFbxPath, DerivedFbxAsset derivedAsset, string outputPath, string targetGuid)
		{
			if (string.IsNullOrEmpty(baseFbxPath) || derivedAsset == null)
			{
				Debug.LogError("[DerivedFbxBuilder] Invalid inputs: baseFbxPath or derivedAsset is null");
				return null;
			}
			
			// Ensure output path ends with .fbx
			string fbxPath = outputPath;
			if (!fbxPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
				fbxPath += ".fbx";
			
			// Use FBX SDK
			if (IsFbxSdkAvailable())
			{
				return BuildDerivedFbxUsingSdk(baseFbxPath, derivedAsset, fbxPath, targetGuid);
			}
			else
			{
				Debug.LogError("[DerivedFbxBuilder] Autodesk FBX SDK (com.autodesk.fbx) is not available. Cannot export FBX file.\nPlease install Autodesk FBX SDK via Package Manager: Window > Package Manager > Search 'FBX SDK' > Install");
					return null;
				}
			}
			
		/// <summary>
		/// Builds derived FBX using FBX SDK (BlendShare's approach).
		/// Imports base FBX into FBX Scene, modifies blendshapes directly, then exports.
		/// This preserves the original FBX structure perfectly.
		/// </summary>
		private static string BuildDerivedFbxUsingSdk(string baseFbxPath, DerivedFbxAsset derivedAsset, string outputPath, string targetGuid)
		{
#if ENABLE_FBX_SDK
			try
			{
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				string physicalBasePath = Path.Combine(projectPath, baseFbxPath.Replace('/', Path.DirectorySeparatorChar));
				string physicalOutputPath = Path.Combine(projectPath, outputPath.Replace('/', Path.DirectorySeparatorChar));
				
				if (!File.Exists(physicalBasePath))
				{
					Debug.LogError($"[DerivedFbxBuilder] Base FBX file not found: {physicalBasePath}");
				return null;
			}
			
				var fbxManager = FbxManager.Create();
				var ios = FbxIOSettings.Create(fbxManager, "IOSROOT");
				fbxManager.SetIOSettings(ios);
				var scene = FbxScene.Create(fbxManager, Path.GetFileNameWithoutExtension(outputPath));
				
				// Import base FBX (preserves all structure - bones, materials, etc.)
				FbxImporter fbxImporter = FbxImporter.Create(fbxManager, "");
				
				// Use -1 to auto-detect importer format (BlendShare approach)
				if (!fbxImporter.Initialize(physicalBasePath, -1, fbxManager.GetIOSettings()))
				{
					Debug.LogError($"[DerivedFbxBuilder] Failed to initialize FBX importer for base FBX: {physicalBasePath}");
					fbxImporter.Destroy();
					fbxManager.Destroy();
					return null;
				}
				
				fbxImporter.Import(scene);
				fbxImporter.Destroy();
				
				var rootNode = scene.GetRootNode();
				
				// Apply blendshape modifications directly to FBX scene (BlendShare approach)
				ApplyBlendshapesToFbxScene(rootNode, derivedAsset);
				
				// Apply other modifications (mesh deltas, UVs, bones, materials) to FBX scene
				ApplyOtherModificationsToFbxScene(scene, rootNode, derivedAsset);
				
				// Ensure output directory exists
				string outputDir = Path.GetDirectoryName(physicalOutputPath);
				if (!Directory.Exists(outputDir))
					Directory.CreateDirectory(outputDir);
				
				// Delete existing file if it exists
				if (File.Exists(physicalOutputPath))
					File.Delete(physicalOutputPath);
				
				// Export modified FBX scene
				var exporter = FbxExporter.Create(fbxManager, "");
				int exportFormat = fbxManager.GetIOPluginRegistry().FindWriterIDByDescription("FBX binary (*.fbx)");
				if (!exporter.Initialize(physicalOutputPath, exportFormat, fbxManager.GetIOSettings()))
				{
					Debug.LogError($"[DerivedFbxBuilder] Failed to initialize FBX exporter for output: {physicalOutputPath}");
					exporter.Destroy();
					fbxManager.Destroy();
					return null;
				}
				
				exporter.Export(scene);
				exporter.Destroy();
				fbxManager.Destroy();
				
				AssetDatabase.Refresh();
				
				// Preserve GUID + import settings from the original derived FBX .meta if we have it
				TryCopyMetaWithGuid(physicalOutputPath, derivedAsset?.originalDerivedFbxPath, baseFbxPath, targetGuid);
				AssetDatabase.Refresh();
				
				return outputPath;
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"[DerivedFbxBuilder] Error exporting FBX using SDK: {ex.Message}\n{ex.StackTrace}");
				return null;
			}
#else
			return null;
#endif
		}
		
		
		/// <summary>
		/// Checks if Autodesk FBX SDK is available.
		/// </summary>
		private static bool IsFbxSdkAvailable()
		{
#if ENABLE_FBX_SDK
			return true;
#else
			return false;
#endif
		}
		
		private static Dictionary<string, Mesh> LoadMeshesByName(string assetPath)
		{
			var dict = new Dictionary<string, Mesh>();
			var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
			foreach (var a in assets)
			{
				if (a is Mesh m && !dict.ContainsKey(m.name))
					dict.Add(m.name, m);
			}
			return dict;
		}
		
		private static void UpdateMeshReferences(GameObject prefab, Dictionary<string, Mesh> baseMeshes, Dictionary<string, Mesh> derivedMeshes)
		{
			// Update MeshFilter components
			foreach (var meshFilter in prefab.GetComponentsInChildren<MeshFilter>(true))
			{
				if (meshFilter.sharedMesh != null && baseMeshes.ContainsKey(meshFilter.sharedMesh.name))
				{
					if (derivedMeshes.TryGetValue(meshFilter.sharedMesh.name, out var derivedMesh))
					{
						meshFilter.sharedMesh = derivedMesh;
					}
				}
			}
			
			// Update SkinnedMeshRenderer components
			foreach (var skinnedMeshRenderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (skinnedMeshRenderer.sharedMesh != null && baseMeshes.ContainsKey(skinnedMeshRenderer.sharedMesh.name))
				{
					if (derivedMeshes.TryGetValue(skinnedMeshRenderer.sharedMesh.name, out var derivedMesh))
					{
						skinnedMeshRenderer.sharedMesh = derivedMesh;
					}
				}
			}
		}
		
		private static void SetUvs(Mesh mesh, int channel, Vector2[] uvs)
		{
			if (uvs == null || uvs.Length != mesh.vertexCount) return;
			
			switch (channel)
			{
				case 0: mesh.uv = uvs; break;
				case 1: mesh.uv2 = uvs; break;
				case 2: mesh.uv3 = uvs; break;
				case 3: mesh.uv4 = uvs; break;
#if UNITY_2019_4_OR_NEWER
				case 4: mesh.uv5 = uvs; break;
				case 5: mesh.uv6 = uvs; break;
				case 6: mesh.uv7 = uvs; break;
				case 7: mesh.uv8 = uvs; break;
#endif
			}
		}
		
		private static void ApplyBoneHierarchyChanges(GameObject root, DerivedFbxAsset derivedAsset)
		{
			foreach (var op in derivedAsset.operations)
			{
				if (op is DerivedFbxAsset.EmbeddedBoneHierarchyOp boneOp)
				{
					Transform bone = FindTransformByPath(root.transform, boneOp.bonePath);
					if (bone != null)
					{
						bone.localPosition = boneOp.localPosition;
						bone.localRotation = boneOp.localRotation;
						bone.localScale = boneOp.localScale;
					}
				}
			}
		}
		
		private static void ApplyMaterialAssignmentChanges(GameObject root, DerivedFbxAsset derivedAsset)
		{
			foreach (var op in derivedAsset.operations)
			{
				if (op is DerivedFbxAsset.EmbeddedMaterialAssignmentOp matOp)
				{
					Transform rendererTransform = FindTransformByPath(root.transform, matOp.rendererPath);
					if (rendererTransform != null)
					{
						var renderer = rendererTransform.GetComponent<Renderer>();
						if (renderer != null && matOp.materialGuids != null)
						{
							var materials = new Material[matOp.materialGuids.Length];
							for (int i = 0; i < matOp.materialGuids.Length; i++)
							{
								if (!string.IsNullOrEmpty(matOp.materialGuids[i]))
								{
									string matPath = AssetDatabase.GUIDToAssetPath(matOp.materialGuids[i]);
									if (!string.IsNullOrEmpty(matPath))
									{
										materials[i] = AssetDatabase.LoadAssetAtPath<Material>(matPath);
									}
								}
							}
							renderer.sharedMaterials = materials;
						}
					}
				}
			}
		}
		
#if ENABLE_FBX_SDK
		/// <summary>
		/// Applies blendshapes to FBX scene following BlendShare's approach.
		/// Adapted from BlendShare's AddBlendShapes method to work with DerivedFbxAsset.
		/// </summary>
		private static void ApplyBlendshapesToFbxScene(FbxNode rootNode, DerivedFbxAsset derivedAsset)
		{
			// Group blendshape operations by mesh (similar to BlendShare's MeshData grouping)
			Dictionary<string, List<DerivedFbxAsset.EmbeddedBlendshapeOp>> blendshapeOpsByMesh = new Dictionary<string, List<DerivedFbxAsset.EmbeddedBlendshapeOp>>();
			foreach (var op in derivedAsset.operations)
			{
				if (op is DerivedFbxAsset.EmbeddedBlendshapeOp bsOp)
				{
					if (!blendshapeOpsByMesh.TryGetValue(bsOp.targetMeshName, out var list))
					{
						list = new List<DerivedFbxAsset.EmbeddedBlendshapeOp>();
						blendshapeOpsByMesh[bsOp.targetMeshName] = list;
					}
					list.Add(bsOp);
				}
			}
			
			// Process each mesh (adapted from BlendShare's AddBlendShapes)
			foreach (var kvp in blendshapeOpsByMesh)
			{
				string meshName = kvp.Key;
				FbxNode meshNode = FindMeshChild(rootNode, meshName);
				if (meshNode == null || meshNode.GetMesh() == null)
				{
					Debug.LogError($"[DerivedFbxBuilder] Can not find mesh: {meshName} in FBX file");
					continue;
				}
				
				FbxMesh targetMesh = meshNode.GetMesh();
				int controlPointCount = targetMesh.GetControlPointsCount();
				
				// Get or create deformer (BlendShare approach: use empty string as deformer ID)
				FbxBlendShape deformer = GetOrCreateDeformer(targetMesh, "");
				
				// Build set of blendshape names from operations
				HashSet<string> operationBlendshapeNames = new HashSet<string>();
				foreach (var bsOp in kvp.Value)
				{
					if (bsOp.vertexIndices != null && bsOp.deltaVertices != null)
					{
						operationBlendshapeNames.Add(bsOp.blendshapeName);
					}
				}
				
				HashSet<string> existingBlendshapes = new HashSet<string>();
				
				// Overwrite blend shapes that are in our operations (BlendShare's approach)
				for (int i = 0; i < targetMesh.GetDeformerCount(FbxDeformer.EDeformerType.eBlendShape); i++)
				{
					var existingDeformer = targetMesh.GetBlendShapeDeformer(i);
					for (int j = existingDeformer.GetBlendShapeChannelCount() - 1; j >= 0; j--)
					{
						var name = existingDeformer.GetBlendShapeChannel(j).GetName();
						if (operationBlendshapeNames.Contains(name))
						{
							Debug.LogWarning($"[DerivedFbxBuilder] Warning: The blendshape with the name '{name}' already exists in the node '{meshNode.GetName()}'. The existing blendshape was overwritten.");
							
							var channel = existingDeformer.GetBlendShapeChannel(j);
							int shapeCount = channel.GetTargetShapeCount();
							
							// Clear all existing shapes (BlendShare approach)
							for (int shape = 0; shape < shapeCount; shape++)
							{
								channel.RemoveTargetShape(channel.GetTargetShape(shape));
							}
							
							// Find operation for this blendshape and create new target shape
							var bsOp = kvp.Value.FirstOrDefault(op => op.blendshapeName == name);
							if (bsOp != null && bsOp.vertexIndices != null && bsOp.deltaVertices != null)
							{
								CreateFbxBlendShapeChannel(channel, targetMesh, bsOp, controlPointCount);
								existingBlendshapes.Add(name);
							}
						}
					}
				}
				
				// Add new blendshapes that don't exist yet (BlendShare approach)
				foreach (var bsOp in kvp.Value)
				{
					if (bsOp.vertexIndices != null && bsOp.deltaVertices != null && !existingBlendshapes.Contains(bsOp.blendshapeName))
					{
						deformer.AddBlendShapeChannel(CreateFbxBlendShapeChannel(bsOp.blendshapeName, targetMesh, bsOp, controlPointCount));
					}
				}
			}
		}
		
		/// <summary>
		/// Gets or creates a blendshape deformer for a mesh (adapted from BlendShare's GetDeformer).
		/// </summary>
		private static FbxBlendShape GetOrCreateDeformer(FbxMesh targetMesh, string deformerID)
		{
			if (!string.IsNullOrEmpty(deformerID))
			{
				int deformerCount = targetMesh.GetDeformerCount(FbxDeformer.EDeformerType.eBlendShape);
				for (int i = deformerCount - 1; i >= 0; i--)
				{
					var existingDeformer = targetMesh.GetBlendShapeDeformer(i);
					if (existingDeformer.GetName() == deformerID)
					{
						return existingDeformer;
					}
				}
			}
			else
			{
				// If deformerID is empty, return first existing deformer if available
				int deformerCount = targetMesh.GetDeformerCount(FbxDeformer.EDeformerType.eBlendShape);
				if (deformerCount > 0)
				{
					return targetMesh.GetBlendShapeDeformer(0);
				}
			}
			
			// Create new deformer if not found and add it to the mesh
			FbxBlendShape newDeformer = FbxBlendShape.Create(targetMesh, deformerID);
			targetMesh.AddDeformer(newDeformer);
			return newDeformer;
		}
		
		/// <summary>
		/// Finds a mesh child node by name (adapted from BlendShare's FbxUtil.FindMeshChild).
		/// Inlined here so it works in the temp package without FbxUtil dependency.
		/// </summary>
		private static FbxNode FindMeshChild(FbxNode rootNode, string name)
		{
			if (rootNode.GetName() == name && rootNode.GetMesh() != null)
			{ 
				return rootNode;
			}
			
			for (int i = 0; i < rootNode.GetChildCount(); i++)
			{
				FbxNode child = rootNode.GetChild(i);
				if (child.GetName() == name && child.GetMesh() != null)
				{ 
					return child;
				}
			}

			for (int i = 0; i < rootNode.GetChildCount(); i++)
			{
				FbxNode found = FindMeshChild(rootNode.GetChild(i), name);
				if (found != null)
					return found;
			}
			
			return null;
		}
		
		/// <summary>
		/// Creates an FBX blendshape channel from operation data (adapted from BlendShare's CreateFbxBlendShapeChannel).
		/// Our format has a single frame per blendshape, so we create a single target shape.
		/// </summary>
		private static FbxBlendShapeChannel CreateFbxBlendShapeChannel(string name, FbxMesh mesh, DerivedFbxAsset.EmbeddedBlendshapeOp bsOp, int controlPointCount)
		{
			FbxBlendShapeChannel fbxBlendShapeChannel = FbxBlendShapeChannel.Create(mesh, name);
			
			// Convert sparse storage to full arrays
			Vector3[] fullDeltaVertices = bsOp.GetFullDeltaVertices(controlPointCount);
			
			// Create single target shape (our format has one frame per blendshape)
			FbxShape newShape = FbxShape.Create(mesh, name);
			newShape.InitControlPoints(controlPointCount);
			
			// Apply deltas to create shape control points (BlendShare approach)
			for (int pointIndex = 0; pointIndex < controlPointCount; pointIndex++)
			{
				FbxVector4 basePoint = mesh.GetControlPointAt(pointIndex);
				Vector3 delta = (fullDeltaVertices != null && pointIndex < fullDeltaVertices.Length) 
					? fullDeltaVertices[pointIndex] * bsOp.scale 
					: Vector3.zero;
				
				FbxVector4 controlPoint = basePoint + new FbxVector4(delta.x, delta.y, delta.z, 0);
				newShape.SetControlPointAt(controlPoint, pointIndex);
			}
			
			// Add target shape with frame weight (BlendShare uses 100.0 * (shapeIndex + 1) / shapeCount, we use frameWeight directly)
			fbxBlendShapeChannel.AddTargetShape(newShape, bsOp.frameWeight);
			
			return fbxBlendShapeChannel;
		}
		
		/// <summary>
		/// Updates an existing FBX blendshape channel with operation data (adapted from BlendShare's CreateFbxBlendShapeChannel overload).
		/// </summary>
		private static void CreateFbxBlendShapeChannel(FbxBlendShapeChannel fbxBlendShapeChannel, FbxMesh mesh, DerivedFbxAsset.EmbeddedBlendshapeOp bsOp, int controlPointCount)
		{
			// Convert sparse storage to full arrays
			Vector3[] fullDeltaVertices = bsOp.GetFullDeltaVertices(controlPointCount);
			
			// Create single target shape
			FbxShape newShape = FbxShape.Create(mesh, fbxBlendShapeChannel.GetName());
			newShape.InitControlPoints(controlPointCount);
			
			// Apply deltas to create shape control points (BlendShare approach)
			for (int pointIndex = 0; pointIndex < controlPointCount; pointIndex++)
			{
				FbxVector4 basePoint = mesh.GetControlPointAt(pointIndex);
				Vector3 delta = (fullDeltaVertices != null && pointIndex < fullDeltaVertices.Length) 
					? fullDeltaVertices[pointIndex] * bsOp.scale 
					: Vector3.zero;
				
				FbxVector4 controlPoint = basePoint + new FbxVector4(delta.x, delta.y, delta.z, 0);
				newShape.SetControlPointAt(controlPoint, pointIndex);
			}
			
			// Add target shape with frame weight
			fbxBlendShapeChannel.AddTargetShape(newShape, bsOp.frameWeight);
		}
		
		/// <summary>
		/// Applies other modifications (mesh deltas, UVs, bones, materials) to FBX scene.
		/// Note: Some modifications may not be directly applicable to FBX format.
		/// </summary>
		private static void ApplyOtherModificationsToFbxScene(FbxScene scene, FbxNode rootNode, DerivedFbxAsset derivedAsset)
		{
			// Mesh deltas: modify control points directly
			foreach (var op in derivedAsset.operations)
			{
				if (op is DerivedFbxAsset.EmbeddedMeshDeltaOp meshOp)
				{
					FbxNode meshNode = FindMeshChild(rootNode, meshOp.targetMeshName);
					if (meshNode != null && meshNode.GetMesh() != null)
					{
						FbxMesh fbxMesh = meshNode.GetMesh();
						int controlPointCount = fbxMesh.GetControlPointsCount();
						
						if (meshOp.positionDeltas != null)
						{
							if (meshOp.positionDeltas.Length != controlPointCount)
							{
								Debug.LogWarning($"[DerivedFbxBuilder] Mesh delta vertex count mismatch for '{meshOp.targetMeshName}'");
								continue;
							}
							
							for (int i = 0; i < controlPointCount; i++)
							{
								FbxVector4 basePoint = fbxMesh.GetControlPointAt(i);
								Vector3 delta = meshOp.positionDeltas[i];
								FbxVector4 newPoint = new FbxVector4(
									basePoint.X + delta.x,
									basePoint.Y + delta.y,
									basePoint.Z + delta.z,
									basePoint.W
								);
								fbxMesh.SetControlPointAt(newPoint, i);
							}
						}
					}
				}
			}
			
			// Bone hierarchy changes: modify node transforms
			foreach (var op in derivedAsset.operations)
			{
				if (op is DerivedFbxAsset.EmbeddedBoneHierarchyOp boneOp)
				{
					FbxNode boneNode = FindFbxNodeByPath(rootNode, boneOp.bonePath);
					if (boneNode != null)
					{
						FbxDouble3 translation = new FbxDouble3(boneOp.localPosition.x, boneOp.localPosition.y, boneOp.localPosition.z);
						FbxDouble3 rotation = new FbxDouble3(boneOp.localRotation.eulerAngles.x, boneOp.localRotation.eulerAngles.y, boneOp.localRotation.eulerAngles.z);
						FbxDouble3 scale = new FbxDouble3(boneOp.localScale.x, boneOp.localScale.y, boneOp.localScale.z);
						
						boneNode.LclTranslation.Set(translation);
						boneNode.LclRotation.Set(rotation);
						boneNode.LclScaling.Set(scale);
					}
				}
			}
			
			// Material assignments and UV changes are typically preserved in FBX structure
			// and don't need explicit modification here
		}
		
		/// <summary>
		/// Finds an FBX node by path (e.g., "Root/Bone1/Bone2").
		/// </summary>
		private static FbxNode FindFbxNodeByPath(FbxNode rootNode, string path)
		{
			if (string.IsNullOrEmpty(path))
				return rootNode;
			
			string[] parts = path.Split('/');
			FbxNode currentNode = rootNode;
			
			foreach (string part in parts)
			{
				bool found = false;
				for (int i = 0; i < currentNode.GetChildCount(); i++)
				{
					FbxNode child = currentNode.GetChild(i);
					if (child.GetName() == part)
					{
						currentNode = child;
						found = true;
						break;
					}
				}
				if (!found)
					return null;
			}
			
			return currentNode;
		}
		

		private static Vector3[] ToVector3Array(Vector4[] tangents)
		{
			if (tangents == null)
				return null;
			
			var arr = new Vector3[tangents.Length];
			for (int i = 0; i < tangents.Length; i++)
			{
				arr[i] = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
			}
			return arr;
		}
		
		private static List<List<int>> GetWeldingGroups(FbxMesh mesh)
		{
			var groups = new List<List<int>>();
			if (mesh == null)
				return groups;
			
			int count = mesh.GetControlPointsCount();
			var buckets = new Dictionary<(long, long, long), List<int>>();
			
			for (int i = 0; i < count; i++)
			{
				var pos = mesh.GetControlPointAt(i);
				var key = ((long)Math.Round(pos.X * 1e5), (long)Math.Round(pos.Y * 1e5), (long)Math.Round(pos.Z * 1e5));
				if (!buckets.TryGetValue(key, out var list))
				{
					list = new List<int>();
					buckets[key] = list;
				}
				list.Add(i);
			}
			
			foreach (var kv in buckets)
			{
				if (kv.Value.Count > 1)
					groups.Add(kv.Value);
			}
			
			return groups;
		}
		
		private static void ApplyWeldingGroups(Vector3[] deltas, List<List<int>> weldingGroups)
		{
			if (deltas == null || weldingGroups == null || weldingGroups.Count == 0)
				return;
			
			foreach (var group in weldingGroups)
			{
				if (group == null || group.Count < 2)
					continue;
				
				Vector3 sum = Vector3.zero;
				int valid = 0;
				foreach (int idx in group)
				{
					if (idx >= 0 && idx < deltas.Length)
					{
						sum += deltas[idx];
						valid++;
					}
				}
				if (valid == 0) continue;
				Vector3 avg = sum / valid;
				foreach (int idx in group)
				{
					if (idx >= 0 && idx < deltas.Length)
					{
						deltas[idx] = avg;
					}
				}
			}
		}
		
		private static void TryCopyMetaWithGuid(string physicalOutputPath, string originalDerivedFbxPath, string baseFbxPath, string targetGuid)
		{
			string outputMetaPath = physicalOutputPath + ".meta";
			
			if (!string.IsNullOrEmpty(originalDerivedFbxPath))
			{
				try
				{
					string projectPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
					string originalPhysical = System.IO.Path.Combine(projectPath, originalDerivedFbxPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
					string originalMeta = originalPhysical + ".meta";
					
					if (System.IO.File.Exists(originalMeta))
					{
						string metaContent = System.IO.File.ReadAllText(originalMeta);
						if (!string.IsNullOrEmpty(targetGuid))
						{
							metaContent = System.Text.RegularExpressions.Regex.Replace(
								metaContent,
								@"guid:\s*[a-f0-9]{32}",
								$"guid: {targetGuid}",
								System.Text.RegularExpressions.RegexOptions.IgnoreCase
							);
						}
						
						System.IO.File.WriteAllText(outputMetaPath, metaContent);
						return;
					}
				}
				catch (System.Exception ex)
				{
					Debug.LogWarning($"[DerivedFbxBuilder] Failed to copy original .meta: {ex.Message}");
				}
			}
			
			// Fallback: copy base FBX .meta (helps preserve importer settings like mesh optimization)
			if (!string.IsNullOrEmpty(baseFbxPath))
			{
				try
				{
					string projectPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
					string basePhysical = System.IO.Path.Combine(projectPath, baseFbxPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
					string baseMeta = basePhysical + ".meta";
					
					if (System.IO.File.Exists(baseMeta))
					{
						string metaContent = System.IO.File.ReadAllText(baseMeta);
						if (!string.IsNullOrEmpty(targetGuid))
						{
							metaContent = System.Text.RegularExpressions.Regex.Replace(
								metaContent,
								@"guid:\s*[a-f0-9]{32}",
								$"guid: {targetGuid}",
								System.Text.RegularExpressions.RegexOptions.IgnoreCase
							);
						}
						
						System.IO.File.WriteAllText(outputMetaPath, metaContent);
						return;
					}
				}
				catch (System.Exception ex)
				{
					Debug.LogWarning($"[DerivedFbxBuilder] Failed to copy base .meta: {ex.Message}");
				}
			}
			
			if (!string.IsNullOrEmpty(targetGuid))
			{
				MetaFileManager.WriteGuid(physicalOutputPath, targetGuid);
			}
		}
		
		private static void ApplyNormalsAndTangentsToShape(FbxShape shape, Vector3[] cpDeltaNormals, Vector3[] cpDeltaTangents, Vector3[] cpBaseNormals, Vector3[] cpBaseTangents)
		{
			var normals = CombineBaseAndDelta(cpBaseNormals, cpDeltaNormals);
			var tangents = CombineBaseAndDelta(cpBaseTangents, cpDeltaTangents);
			
			if (normals != null && normals.Length > 0)
			{
				SetNormalsOnShape(shape, normals);
			}
			
			if (tangents != null && tangents.Length > 0)
			{
				SetTangentsOnShape(shape, tangents);
			}
		}
		
		private static Vector3[] CombineBaseAndDelta(Vector3[] baseArray, Vector3[] deltaArray)
		{
			if (baseArray == null && deltaArray == null)
				return null;
			
			int len = Math.Max(baseArray?.Length ?? 0, deltaArray?.Length ?? 0);
			var result = new Vector3[len];
			
			for (int i = 0; i < len; i++)
			{
				Vector3 baseVal = (baseArray != null && i < baseArray.Length) ? baseArray[i] : Vector3.zero;
				Vector3 deltaVal = (deltaArray != null && i < deltaArray.Length) ? deltaArray[i] : Vector3.zero;
				result[i] = baseVal + deltaVal;
			}
			
			return result;
		}
		
		private static void EnsureLayerExists(FbxGeometryBase geometry)
		{
			if (geometry.GetLayerCount() == 0)
			{
				geometry.CreateLayer();
			}
		}
		
		private static void SetNormalsOnShape(FbxShape shape, Vector3[] normals)
		{
			EnsureLayerExists(shape);
			var layer = shape.GetLayer(0);
			if (layer == null)
				return;
			
			var normalElem = FbxLayerElementNormal.Create(shape, "");
			normalElem.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
			normalElem.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
			
			var arr = normalElem.GetDirectArray();
			arr.SetCount(normals.Length);
			
			for (int i = 0; i < normals.Length; i++)
			{
				var n = normals[i];
				arr.SetAt(i, new FbxVector4(n.x, n.y, n.z, 0));
			}
			
			layer.SetNormals(normalElem);
		}
		
		private static void SetTangentsOnShape(FbxShape shape, Vector3[] tangents)
		{
			EnsureLayerExists(shape);
			var layer = shape.GetLayer(0);
			if (layer == null)
				return;
			
			var tangentElem = FbxLayerElementTangent.Create(shape, "");
			tangentElem.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
			tangentElem.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
			
			var arr = tangentElem.GetDirectArray();
			arr.SetCount(tangents.Length);
			
			for (int i = 0; i < tangents.Length; i++)
			{
				var t = tangents[i];
				arr.SetAt(i, new FbxVector4(t.x, t.y, t.z, 0));
			}
			
			layer.SetTangents(tangentElem);
		}
#endif
		
		private static Transform FindTransformByPath(Transform root, string path)
		{
			if (string.IsNullOrEmpty(path))
				return root;
			
			string[] parts = path.Split('/');
			Transform current = root;
			
			foreach (string part in parts)
			{
				if (string.IsNullOrEmpty(part))
					continue;
					
				current = current.Find(part);
				if (current == null)
					return null;
			}
			
			return current;
		}
	}
}
