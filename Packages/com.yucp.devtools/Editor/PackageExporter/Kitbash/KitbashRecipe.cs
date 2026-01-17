#if YUCP_KITBASH_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash
{
    /// <summary>
    /// Defines how a source part is assembled into the synthetic base.
    /// </summary>
    [Serializable]
    public class KitbashSourcePart
    {
        /// <summary>
        /// GUID of the source FBX asset.
        /// </summary>
        public string sourceFbxGuid;
        
        /// <summary>
        /// Node/mesh path within the FBX to include.
        /// Empty string means the root.
        /// </summary>
        public string meshPath = "";
        
        /// <summary>
        /// Optional display name for the part.
        /// </summary>
        public string displayName;
        
        /// <summary>
        /// Position offset when assembling.
        /// </summary>
        public Vector3 positionOffset = Vector3.zero;
        
        /// <summary>
        /// Rotation offset when assembling.
        /// </summary>
        public Quaternion rotationOffset = Quaternion.identity;
        
        /// <summary>
        /// Scale multiplier when assembling.
        /// </summary>
        public Vector3 scaleMultiplier = Vector3.one;
        
        /// <summary>
        /// Optional bone root mapping for armature merging.
        /// Format: "sourceBone:targetBone"
        /// </summary>
        public string boneRootMapping;
        
        /// <summary>
        /// Hints for robustness when GUID resolution fails.
        /// </summary>
        public KitbashPartHints hints;
    }
    
    /// <summary>
    /// Hints for identifying a source mesh when GUID resolution fails.
    /// Used for fallback resolution after asset moves/renames.
    /// </summary>
    [Serializable]
    public struct KitbashPartHints
    {
        /// <summary>
        /// Original asset file name (without path).
        /// </summary>
        public string originalFileName;
        
        /// <summary>
        /// Total vertex count of the mesh.
        /// </summary>
        public int vertexCount;
        
        /// <summary>
        /// Total triangle count of the mesh.
        /// </summary>
        public int triangleCount;
        
        /// <summary>
        /// Bounding box extents (local space).
        /// </summary>
        public Vector3 boundsExtents;
        
        /// <summary>
        /// Names of top-level bones (first 10, sorted).
        /// </summary>
        public string[] boneNames;
        
        /// <summary>
        /// Blend shape names if any.
        /// </summary>
        public string[] blendShapeNames;
        
        /// <summary>
        /// Scores similarity between this hint and another mesh.
        /// Returns 0-1 where 1 is perfect match.
        /// </summary>
        public float ScoreSimilarity(KitbashPartHints other)
        {
            float score = 0f;
            int weights = 0;
            
            // Filename match (strong signal)
            if (!string.IsNullOrEmpty(originalFileName) && !string.IsNullOrEmpty(other.originalFileName))
            {
                if (originalFileName == other.originalFileName)
                    score += 3f;
                else if (originalFileName.Contains(other.originalFileName) || other.originalFileName.Contains(originalFileName))
                    score += 1.5f;
                weights += 3;
            }
            
            // Vertex count (within 5% tolerance)
            if (vertexCount > 0 && other.vertexCount > 0)
            {
                float ratio = (float)Mathf.Min(vertexCount, other.vertexCount) / Mathf.Max(vertexCount, other.vertexCount);
                if (ratio > 0.95f) score += 2f;
                else if (ratio > 0.8f) score += 1f;
                weights += 2;
            }
            
            // Triangle count (within 5% tolerance)
            if (triangleCount > 0 && other.triangleCount > 0)
            {
                float ratio = (float)Mathf.Min(triangleCount, other.triangleCount) / Mathf.Max(triangleCount, other.triangleCount);
                if (ratio > 0.95f) score += 2f;
                else if (ratio > 0.8f) score += 1f;
                weights += 2;
            }
            
            // Bounds extents (within 10% tolerance)
            if (boundsExtents.sqrMagnitude > 0 && other.boundsExtents.sqrMagnitude > 0)
            {
                float extentDiff = (boundsExtents - other.boundsExtents).magnitude / Mathf.Max(boundsExtents.magnitude, 0.001f);
                if (extentDiff < 0.1f) score += 1.5f;
                else if (extentDiff < 0.25f) score += 0.5f;
                weights += 2;
            }
            
            // Bone name overlap
            if (boneNames != null && other.boneNames != null && boneNames.Length > 0 && other.boneNames.Length > 0)
            {
                int matches = 0;
                foreach (var bn in boneNames)
                {
                    if (Array.IndexOf(other.boneNames, bn) >= 0) matches++;
                }
                float overlap = (float)matches / Mathf.Max(boneNames.Length, other.boneNames.Length);
                score += overlap * 2f;
                weights += 2;
            }
            
            return weights > 0 ? score / weights : 0f;
        }
    }

    /// <summary>
    /// Recipe defining how a synthetic base FBX is assembled from multiple sources.
    /// </summary>
    [CreateAssetMenu(menuName = "YUCP/Kitbash/Recipe", fileName = "KitbashRecipe")]
    public class KitbashRecipe : ScriptableObject
    {
        /// <summary>
        /// GUID of the derived FBX this recipe is for.
        /// </summary>
        public string targetDerivedFbxGuid;
        
        /// <summary>
        /// List of source parts to assemble.
        /// </summary>
        public List<KitbashSourcePart> parts = new List<KitbashSourcePart>();
        
        /// <summary>
        /// Reference to the ownership map (by GUID).
        /// </summary>
        public string ownershipMapGuid;
        
        /// <summary>
        /// Cached recipe hash for deterministic build caching.
        /// Computed from canonical JSON of essential fields.
        /// </summary>
        public string recipeHash;
        
        /// <summary>
        /// Gets all unique source FBX GUIDs referenced by this recipe.
        /// </summary>
        public string[] GetSourceGuids()
        {
            var guids = new HashSet<string>();
            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part.sourceFbxGuid))
                {
                    guids.Add(part.sourceFbxGuid);
                }
            }
            
            var result = new string[guids.Count];
            guids.CopyTo(result);
            return result;
        }
        
        /// <summary>
        /// Computes and stores the recipe hash based on essential fields.
        /// </summary>
        public void ComputeHash()
        {
            var sb = new StringBuilder();
            sb.Append("v1:"); // Version prefix for future format changes
            
            // Sort parts by sourceFbxGuid for determinism
            var sortedParts = new List<KitbashSourcePart>(parts);
            sortedParts.Sort((a, b) => string.Compare(a.sourceFbxGuid, b.sourceFbxGuid, StringComparison.Ordinal));
            
            foreach (var part in sortedParts)
            {
                sb.Append(part.sourceFbxGuid ?? "");
                sb.Append("|");
                sb.Append(part.meshPath ?? "");
                sb.Append("|");
                sb.AppendFormat("{0:F6},{1:F6},{2:F6}|", part.positionOffset.x, part.positionOffset.y, part.positionOffset.z);
                sb.AppendFormat("{0:F6},{1:F6},{2:F6},{3:F6}|", part.rotationOffset.x, part.rotationOffset.y, part.rotationOffset.z, part.rotationOffset.w);
                sb.AppendFormat("{0:F6},{1:F6},{2:F6}|", part.scaleMultiplier.x, part.scaleMultiplier.y, part.scaleMultiplier.z);
                sb.Append(part.boneRootMapping ?? "");
                sb.Append(";");
            }
            
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                recipeHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
        
        /// <summary>
        /// Serializes the recipe to JSON for embedding in DerivedFbxAsset.
        /// </summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
        
        /// <summary>
        /// Deserializes a recipe from JSON.
        /// </summary>
        public static KitbashRecipe FromJson(string json)
        {
            return JsonUtility.FromJson<KitbashRecipe>(json);
        }
        
        /// <summary>
        /// Generates hints from a source FBX asset.
        /// </summary>
        public static KitbashPartHints GenerateHints(string assetPath, GameObject prefab)
        {
            var hints = new KitbashPartHints();
            
            hints.originalFileName = System.IO.Path.GetFileName(assetPath);
            
            if (prefab == null) return hints;
            
            // Get mesh data
            var skinnedMesh = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
            var meshFilter = prefab.GetComponentInChildren<MeshFilter>();
            Mesh mesh = skinnedMesh?.sharedMesh ?? meshFilter?.sharedMesh;
            
            if (mesh != null)
            {
                hints.vertexCount = mesh.vertexCount;
                hints.triangleCount = mesh.triangles.Length / 3;
                hints.boundsExtents = mesh.bounds.extents;
                
                // Get blend shape names
                if (mesh.blendShapeCount > 0)
                {
                    var shapes = new List<string>();
                    for (int i = 0; i < Mathf.Min(mesh.blendShapeCount, 20); i++)
                    {
                        shapes.Add(mesh.GetBlendShapeName(i));
                    }
                    hints.blendShapeNames = shapes.ToArray();
                }
            }
            
            // Get bone names from skinned mesh
            if (skinnedMesh?.bones != null && skinnedMesh.bones.Length > 0)
            {
                var boneList = new List<string>();
                foreach (var bone in skinnedMesh.bones.Take(10))
                {
                    if (bone != null)
                        boneList.Add(bone.name);
                }
                boneList.Sort();
                hints.boneNames = boneList.ToArray();
            }
            
            return hints;
        }
        
        /// <summary>
        /// Validates all parts and attempts fallback resolution for missing GUIDs.
        /// Returns list of unresolvable parts (empty = all resolved).
        /// </summary>
        public List<string> ValidateAndResolve()
        {
            var unresolved = new List<string>();
            
            #if UNITY_EDITOR
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part.sourceFbxGuid)) continue;
                
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(part.sourceFbxGuid);
                if (!string.IsNullOrEmpty(path)) continue; // Already resolved
                
                // Try fallback resolution using hints
                if (part.hints.originalFileName != null)
                {
                    string resolved = TryResolveByhints(part.hints);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        part.sourceFbxGuid = UnityEditor.AssetDatabase.AssetPathToGUID(resolved);
                        Debug.Log($"[KitbashRecipe] Resolved {part.displayName} via hints: {resolved}");
                        continue;
                    }
                }
                
                unresolved.Add(part.displayName ?? part.sourceFbxGuid);
            }
            #endif
            
            return unresolved;
        }
        
        #if UNITY_EDITOR
        /// <summary>
        /// Attempts to find a matching FBX by hints.
        /// </summary>
        private static string TryResolveByhints(KitbashPartHints targetHints)
        {
            // Search all FBX files for best match
            string[] fbxGuids = UnityEditor.AssetDatabase.FindAssets("t:Model");
            
            float bestScore = 0.5f; // Minimum threshold for match
            string bestPath = null;
            
            foreach (var guid in fbxGuids)
            {
                try
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                    
                    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;
                    
                    var candidateHints = GenerateHints(path, prefab);
                    float score = targetHints.ScoreSimilarity(candidateHints);
                    
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPath = path;
                    }
                }
                catch { }
            }
            
            return bestPath;
        }
        #endif
    }
}
#endif
