#if YUCP_KITBASH_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash
{
    /// <summary>
    /// Handles auto-seeding of ownership from mesh topology and materials.
    /// Provides confidence scores for user review.
    /// </summary>
    public static class OwnershipAutoSeed
    {
        /// <summary>
        /// Result from auto-seeding operation.
        /// </summary>
        public class AutoSeedResult
        {
            public float coveragePercent;
            public int highConfidenceCount;
            public int lowConfidenceCount;
            public List<int> reviewTriangles = new List<int>();
            public Dictionary<int, int> triangleToSource = new Dictionary<int, int>();
            public Dictionary<int, float> triangleConfidence = new Dictionary<int, float>();
        }
        
        /// <summary>
        /// Preview what auto-seeding would do without applying.
        /// </summary>
        public static AutoSeedResult Preview(
            Mesh mesh,
            Renderer renderer,
            List<(string guid, string name)> sources)
        {
            var result = new AutoSeedResult();
            
            if (mesh == null || sources.Count == 0)
            {
                return result;
            }
            
            int triangleCount = mesh.triangles.Length / 3;
            
            // Strategy 1: Use submesh indices to assign sources
            // Each submesh typically corresponds to a different material/source
            if (mesh.subMeshCount > 1)
            {
                SeedFromSubmeshes(mesh, sources, result);
            }
            
            // Strategy 2: Use material assignments on renderer
            if (renderer != null && renderer.sharedMaterials != null)
            {
                SeedFromMaterials(mesh, renderer, sources, result);
            }
            
            // Strategy 3: Use spatial clustering (bounding boxes)
            // Only for triangles not yet assigned
            SeedFromSpatialClustering(mesh, sources, result);
            
            // Calculate coverage
            int assigned = result.triangleToSource.Count;
            result.coveragePercent = triangleCount > 0 ? (float)assigned / triangleCount * 100f : 0f;
            
            // Count high/low confidence
            foreach (var kvp in result.triangleConfidence)
            {
                if (kvp.Value >= 0.8f)
                    result.highConfidenceCount++;
                else
                    result.lowConfidenceCount++;
                
                if (kvp.Value < 0.5f)
                    result.reviewTriangles.Add(kvp.Key);
            }
            
            return result;
        }
        
        /// <summary>
        /// Apply auto-seed results to an ownership map.
        /// </summary>
        public static void Apply(OwnershipMap map, AutoSeedResult result)
        {
            if (map == null || result == null) return;
            
            // Group triangles by source
            var sourceToTriangles = new Dictionary<int, List<int>>();
            var sourceToConfidence = new Dictionary<int, float>();
            
            foreach (var kvp in result.triangleToSource)
            {
                int triIndex = kvp.Key;
                int sourceIndex = kvp.Value;
                
                if (!sourceToTriangles.ContainsKey(sourceIndex))
                {
                    sourceToTriangles[sourceIndex] = new List<int>();
                    sourceToConfidence[sourceIndex] = 0f;
                }
                
                sourceToTriangles[sourceIndex].Add(triIndex);
                
                if (result.triangleConfidence.TryGetValue(triIndex, out float conf))
                {
                    sourceToConfidence[sourceIndex] = Mathf.Max(sourceToConfidence[sourceIndex], conf);
                }
            }
            
            // Create regions
            map.regions.Clear();
            foreach (var kvp in sourceToTriangles)
            {
                var region = new OwnershipRegion
                {
                    sourcePartIndex = kvp.Key,
                    triangleIndices = kvp.Value.ToArray(),
                    confidence = sourceToConfidence.GetValueOrDefault(kvp.Key, 0.5f)
                };
                map.regions.Add(region);
            }
        }
        
        private static void SeedFromSubmeshes(
            Mesh mesh,
            List<(string guid, string name)> sources,
            AutoSeedResult result)
        {
            // Map each submesh to a source (1:1 if counts match)
            int submeshCount = mesh.subMeshCount;
            int sourceCount = Math.Min(submeshCount, sources.Count);
            
            for (int submesh = 0; submesh < sourceCount; submesh++)
            {
                var submeshDesc = mesh.GetSubMesh(submesh);
                int startIndex = submeshDesc.indexStart / 3;
                int triCount = submeshDesc.indexCount / 3;
                
                for (int i = 0; i < triCount; i++)
                {
                    int triIndex = startIndex + i;
                    if (!result.triangleToSource.ContainsKey(triIndex))
                    {
                        result.triangleToSource[triIndex] = submesh;
                        result.triangleConfidence[triIndex] = 0.9f; // High confidence for submesh matching
                    }
                }
            }
        }
        
        private static void SeedFromMaterials(
            Mesh mesh,
            Renderer renderer,
            List<(string guid, string name)> sources,
            AutoSeedResult result)
        {
            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) return;
            
            // Try to match materials to sources by name
            var materialToSource = new Dictionary<int, int>();
            
            for (int matIndex = 0; matIndex < materials.Length; matIndex++)
            {
                var mat = materials[matIndex];
                if (mat == null) continue;
                
                string matName = mat.name.ToLowerInvariant();
                
                for (int srcIndex = 0; srcIndex < sources.Count; srcIndex++)
                {
                    string srcName = sources[srcIndex].name.ToLowerInvariant();
                    
                    // Simple name matching
                    if (matName.Contains(srcName) || srcName.Contains(matName))
                    {
                        materialToSource[matIndex] = srcIndex;
                        break;
                    }
                }
            }
            
            // Apply to triangles based on submesh (submesh index = material index)
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (!materialToSource.TryGetValue(submesh, out int sourceIndex)) continue;
                
                var submeshDesc = mesh.GetSubMesh(submesh);
                int startIndex = submeshDesc.indexStart / 3;
                int triCount = submeshDesc.indexCount / 3;
                
                for (int i = 0; i < triCount; i++)
                {
                    int triIndex = startIndex + i;
                    if (!result.triangleToSource.ContainsKey(triIndex))
                    {
                        result.triangleToSource[triIndex] = sourceIndex;
                        result.triangleConfidence[triIndex] = 0.7f; // Medium confidence for material matching
                    }
                }
            }
        }
        
        private static void SeedFromSpatialClustering(
            Mesh mesh,
            List<(string guid, string name)> sources,
            AutoSeedResult result)
        {
            // Simple spatial clustering based on Y position (head = top, feet = bottom)
            // This is a fallback for triangles not assigned by other methods
            
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            
            // Get mesh bounds
            Bounds bounds = mesh.bounds;
            float height = bounds.size.y;
            
            if (height <= 0) return;
            
            // Divide into regions based on source count
            int regionCount = sources.Count;
            float regionHeight = height / regionCount;
            
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int triIndex = i / 3;
                
                // Skip already assigned
                if (result.triangleToSource.ContainsKey(triIndex)) continue;
                
                // Get triangle center Y
                Vector3 v0 = vertices[triangles[i]];
                Vector3 v1 = vertices[triangles[i + 1]];
                Vector3 v2 = vertices[triangles[i + 2]];
                float centerY = (v0.y + v1.y + v2.y) / 3f;
                
                // Determine region
                float relativeY = centerY - bounds.min.y;
                int region = Mathf.Clamp((int)(relativeY / regionHeight), 0, regionCount - 1);
                
                // Invert so higher = lower index (head = 0 usually)
                region = regionCount - 1 - region;
                
                result.triangleToSource[triIndex] = region;
                result.triangleConfidence[triIndex] = 0.3f; // Low confidence for spatial
            }
        }
    }
    
    /// <summary>
    /// Bootstrap helpers for quickly setting up ownership from existing mesh structure.
    /// </summary>
    public static class OwnershipBootstrap
    {
        /// <summary>
        /// Bootstrap ownership from submesh indices.
        /// Each submesh becomes a separate region.
        /// </summary>
        public static OwnershipMap FromSubmeshes(Mesh mesh, List<string> sourceNames)
        {
            if (mesh == null) return null;
            
            var map = ScriptableObject.CreateInstance<OwnershipMap>();
            map.targetMeshGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh));
            map.targetMeshName = mesh.name;
            map.expectedTriangleCount = mesh.triangles.Length / 3;
            
            int submeshCount = mesh.subMeshCount;
            
            for (int submesh = 0; submesh < submeshCount; submesh++)
            {
                var submeshDesc = mesh.GetSubMesh(submesh);
                int startIndex = submeshDesc.indexStart / 3;
                int triCount = submeshDesc.indexCount / 3;
                
                var triangles = new int[triCount];
                for (int i = 0; i < triCount; i++)
                {
                    triangles[i] = startIndex + i;
                }
                
                var region = new OwnershipRegion
                {
                    sourcePartIndex = submesh < sourceNames.Count ? submesh : -1,
                    triangleIndices = triangles,
                    confidence = 1f // User-confirmed from submeshes
                };
                
                map.regions.Add(region);
            }
            
            // Create groups based on submesh count
            if (sourceNames.Count > 0)
            {
                var defaultGroup = new OwnershipGroup
                {
                    name = "All Parts",
                    partIndices = Enumerable.Range(0, Math.Min(submeshCount, sourceNames.Count)).ToArray()
                };
                map.groups.Add(defaultGroup);
            }
            
            return map;
        }
        
        /// <summary>
        /// Bootstrap ownership from material assignments.
        /// Groups triangles by material slot.
        /// </summary>
        public static OwnershipMap FromMaterials(Mesh mesh, Renderer renderer, List<string> sourceNames)
        {
            if (mesh == null || renderer == null) return null;
            
            var map = ScriptableObject.CreateInstance<OwnershipMap>();
            map.targetMeshGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh));
            map.targetMeshName = mesh.name;
            map.expectedTriangleCount = mesh.triangles.Length / 3;
            
            var materials = renderer.sharedMaterials;
            int matCount = materials?.Length ?? 0;
            
            // Similar to submesh approach but using material indices
            for (int matIndex = 0; matIndex < matCount; matIndex++)
            {
                // In Unity, submesh index typically corresponds to material index
                if (matIndex >= mesh.subMeshCount) break;
                
                var submeshDesc = mesh.GetSubMesh(matIndex);
                int startIndex = submeshDesc.indexStart / 3;
                int triCount = submeshDesc.indexCount / 3;
                
                var triangles = new int[triCount];
                for (int i = 0; i < triCount; i++)
                {
                    triangles[i] = startIndex + i;
                }
                
                var region = new OwnershipRegion
                {
                    sourcePartIndex = matIndex < sourceNames.Count ? matIndex : -1,
                    triangleIndices = triangles,
                    confidence = 1f
                };
                
                map.regions.Add(region);
            }
            
            return map;
        }
    }
}
#endif
