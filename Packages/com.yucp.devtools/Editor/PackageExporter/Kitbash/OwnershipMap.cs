#if YUCP_KITBASH_ENABLED
using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash
{
    /// <summary>
    /// A point sampled on the mesh surface for robustness after mesh edits.
    /// Positions are stored in LOCAL mesh space for portability.
    /// </summary>
    [Serializable]
    public struct SamplePoint
    {
        /// <summary>
        /// LOCAL mesh-space position of the sample.
        /// </summary>
        public Vector3 position;
        
        /// <summary>
        /// Normal at the sample point (local space).
        /// </summary>
        public Vector3 normal;
        
        /// <summary>
        /// Original triangle index (for re-projection after edits).
        /// </summary>
        public int nearestTriangleIndex;
    }
    
    /// <summary>
    /// Represents ownership assignment for a region of triangles.
    /// </summary>
    [Serializable]
    public class OwnershipRegion
    {
        /// <summary>
        /// Index into recipe.parts. -1 means Unknown.
        /// </summary>
        public int sourcePartIndex = -1;
        
        /// <summary>
        /// Triangle indices assigned to this owner.
        /// </summary>
        public int[] triangleIndices = Array.Empty<int>();
        
        /// <summary>
        /// Confidence score from auto-seeding (0-1).
        /// 1.0 = user-painted, &lt;1.0 = auto-suggested.
        /// </summary>
        public float confidence = 1f;
        
        /// <summary>
        /// Sample points for robustness after mesh edits.
        /// </summary>
        public SamplePoint[] samplePoints = Array.Empty<SamplePoint>();
    }
    
    /// <summary>
    /// Grouping of ownership parts for organized display.
    /// </summary>
    [Serializable]
    public class OwnershipGroup
    {
        /// <summary>
        /// Display name (e.g., "Head", "Body", "Accessories").
        /// </summary>
        public string name;
        
        /// <summary>
        /// Indices of source parts in this group.
        /// </summary>
        public int[] partIndices = Array.Empty<int>();
        
        /// <summary>
        /// UI collapsed state.
        /// </summary>
        public bool collapsed;
    }
    
    /// <summary>
    /// Stores ownership (source part) assignments for mesh triangles.
    /// Created by the Ownership Map paint tool.
    /// </summary>
    [CreateAssetMenu(menuName = "YUCP/Kitbash/Ownership Map", fileName = "OwnershipMap")]
    public class OwnershipMap : ScriptableObject
    {
        /// <summary>
        /// GUID of the target mesh/FBX this ownership map is for.
        /// </summary>
        public string targetMeshGuid;
        
        /// <summary>
        /// Name of the specific mesh within the FBX (if compound).
        /// </summary>
        public string targetMeshName;
        
        /// <summary>
        /// Total triangle count at time of creation.
        /// Used for validation.
        /// </summary>
        public int expectedTriangleCount;
        
        /// <summary>
        /// Ownership regions (one per assigned source part).
        /// Unknown triangles may not be explicitly listed.
        /// </summary>
        public List<OwnershipRegion> regions = new List<OwnershipRegion>();
        
        /// <summary>
        /// Grouping metadata for UI organization.
        /// </summary>
        public List<OwnershipGroup> groups = new List<OwnershipGroup>();
        
        /// <summary>
        /// Gets the owner index for a specific triangle.
        /// Returns -1 if unknown.
        /// </summary>
        public int GetOwnerForTriangle(int triangleIndex)
        {
            foreach (var region in regions)
            {
                if (Array.IndexOf(region.triangleIndices, triangleIndex) >= 0)
                {
                    return region.sourcePartIndex;
                }
            }
            return -1; // Unknown
        }
        
        /// <summary>
        /// Calculates coverage statistics.
        /// </summary>
        public (int assigned, int unknown, float percentage) GetCoverageStats()
        {
            var assignedTriangles = new HashSet<int>();
            
            foreach (var region in regions)
            {
                if (region.sourcePartIndex >= 0) // Not unknown
                {
                    foreach (int tri in region.triangleIndices)
                    {
                        assignedTriangles.Add(tri);
                    }
                }
            }
            
            int assigned = assignedTriangles.Count;
            int unknown = expectedTriangleCount - assigned;
            float percentage = expectedTriangleCount > 0 ? (float)assigned / expectedTriangleCount * 100f : 0f;
            
            return (assigned, unknown, percentage);
        }
        
        /// <summary>
        /// Gets all triangle indices that are unknown (not assigned to any source).
        /// </summary>
        public int[] GetUnknownTriangles()
        {
            var assigned = new HashSet<int>();
            
            foreach (var region in regions)
            {
                if (region.sourcePartIndex < 0) continue;
                foreach (int tri in region.triangleIndices)
                {
                    assigned.Add(tri);
                }
            }
            
            var unknown = new List<int>();
            for (int i = 0; i < expectedTriangleCount; i++)
            {
                if (!assigned.Contains(i))
                {
                    unknown.Add(i);
                }
            }
            
            return unknown.ToArray();
        }
        
        /// <summary>
        /// Reprojects ownership from stored sample points to a new mesh.
        /// Used when triangle count has changed after mesh edits.
        /// </summary>
        /// <param name="newTriangleCenters">Centers of triangles in the new mesh (LOCAL space)</param>
        /// <param name="newTriangleNormals">Normals of triangles in the new mesh (LOCAL space)</param>
        /// <returns>Array of owner indices for each new triangle (-1 for unknown)</returns>
        public int[] ReprojectToMesh(Vector3[] newTriangleCenters, Vector3[] newTriangleNormals)
        {
            int newTriCount = newTriangleCenters.Length;
            int[] ownership = new int[newTriCount];
            float[] confidence = new float[newTriCount];
            
            // Initialize all as unknown with 0 confidence
            for (int i = 0; i < newTriCount; i++)
            {
                ownership[i] = -1; // Unknown
                confidence[i] = 0f;
            }
            
            // For each region with sample points, vote for nearby triangles
            foreach (var region in regions)
            {
                if (region.samplePoints == null || region.samplePoints.Length == 0)
                    continue;
                
                foreach (var sample in region.samplePoints)
                {
                    // Find nearest triangle to this sample
                    int nearestTri = -1;
                    float nearestDistSq = float.MaxValue;
                    
                    for (int t = 0; t < newTriCount; t++)
                    {
                        float distSq = (newTriangleCenters[t] - sample.position).sqrMagnitude;
                        
                        // Also consider normal agreement
                        float normalDot = Vector3.Dot(newTriangleNormals[t], sample.normal);
                        if (normalDot < 0.5f) continue; // Skip triangles with very different normals
                        
                        if (distSq < nearestDistSq)
                        {
                            nearestDistSq = distSq;
                            nearestTri = t;
                        }
                    }
                    
                    // Vote for this triangle if close enough
                    if (nearestTri >= 0 && nearestDistSq < 0.01f) // ~10cm threshold
                    {
                        float voteStrength = 1f / (1f + nearestDistSq * 100f);
                        
                        // Higher confidence vote wins, or accumulate for same region
                        if (ownership[nearestTri] == region.sourcePartIndex)
                        {
                            confidence[nearestTri] += voteStrength;
                        }
                        else if (voteStrength > confidence[nearestTri])
                        {
                            confidence[nearestTri] = voteStrength;
                            ownership[nearestTri] = region.sourcePartIndex;
                        }
                    }
                }
            }
            
            return ownership;
        }
        
        /// <summary>
        /// Reprojects ownership using a faster spatial hash approach.
        /// </summary>
        public int[] ReprojectToMeshFast(Vector3[] newTriangleCenters, Vector3[] newTriangleNormals, float searchRadius = 0.02f)
        {
            int newTriCount = newTriangleCenters.Length;
            int[] ownership = new int[newTriCount];
            for (int i = 0; i < ownership.Length; i++)
                ownership[i] = -1;
            
            // Build spatial lookup of all samples
            var allSamples = new List<(Vector3 pos, Vector3 normal, int sourcePartIndex)>();
            foreach (var region in regions)
            {
                if (region.samplePoints == null) continue;
                foreach (var sample in region.samplePoints)
                {
                    allSamples.Add((sample.position, sample.normal, region.sourcePartIndex));
                }
            }
            
            if (allSamples.Count == 0)
            {
                // No samples - all unknown
                for (int i = 0; i < ownership.Length; i++)
                    ownership[i] = -1;
                return ownership;
            }
            
            // For each new triangle, find nearest sample
            float searchRadiusSq = searchRadius * searchRadius;
            for (int t = 0; t < newTriCount; t++)
            {
                Vector3 triCenter = newTriangleCenters[t];
                Vector3 triNormal = newTriangleNormals[t];
                
                int bestSource = -1; // Default to Unknown
                float bestScore = 0f;
                
                foreach (var (pos, normal, sourcePartIndex) in allSamples)
                {
                    float distSq = (triCenter - pos).sqrMagnitude;
                    if (distSq > searchRadiusSq) continue;
                    
                    float normalDot = Vector3.Dot(triNormal, normal);
                    if (normalDot < 0.5f) continue;
                    
                    // Score = similarity (closer + more aligned = better)
                    float score = normalDot / (1f + distSq * 100f);
                    
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestSource = sourcePartIndex;
                    }
                }
                
                ownership[t] = bestSource;
            }
            
            return ownership;
        }
    }
}
#endif
