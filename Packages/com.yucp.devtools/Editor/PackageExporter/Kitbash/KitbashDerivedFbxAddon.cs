#if YUCP_KITBASH_ENABLED
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YUCP.DevTools.Editor.PackageExporter.Addons;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash
{
    /// <summary>
    /// Export addon that handles kitbash (multi-source) derived FBX conversion.
    /// Builds a synthetic base from the recipe, then uses standard HDiff patching.
    /// </summary>
    public class KitbashDerivedFbxAddon : IExportAddon
    {
        public int Order => 100;
        
        public void OnPreBuild(PackageBuilderContext ctx) { }
        public void OnCollectAssets(PackageBuilderContext ctx) { }
        public void OnPreWriteTempPackage(PackageBuilderContext ctx) { }
        public void OnPostWriteTempPackage(PackageBuilderContext ctx) { }
        
        public bool TryConvertDerivedFbx(
            PackageBuilderContext ctx,
            string derivedFbxPath,
            DerivedSettings settings,
            out string tempAssetPath)
        {
            tempAssetPath = null;
            
            // Only handle kitbash mode
            if (settings.mode != DerivedMode.KitbashRecipeHdiff)
            {
                return false;
            }
            
            // Load the recipe
            if (string.IsNullOrEmpty(settings.kitbashRecipeGuid))
            {
                Debug.LogError($"[KitbashDerivedFbxAddon] No kitbash recipe GUID specified for {derivedFbxPath}");
                return false;
            }
            
            string recipePath = AssetDatabase.GUIDToAssetPath(settings.kitbashRecipeGuid);
            if (string.IsNullOrEmpty(recipePath))
            {
                Debug.LogError($"[KitbashDerivedFbxAddon] Recipe GUID {settings.kitbashRecipeGuid} not found for {derivedFbxPath}");
                return false;
            }
            
            var recipe = AssetDatabase.LoadAssetAtPath<KitbashRecipe>(recipePath);
            if (recipe == null)
            {
                Debug.LogError($"[KitbashDerivedFbxAddon] Failed to load recipe at {recipePath}");
                return false;
            }

            // Load ownership map
            if (string.IsNullOrEmpty(settings.ownershipMapGuid))
            {
                Debug.LogError($"[KitbashDerivedFbxAddon] No ownership map GUID specified for {derivedFbxPath}");
                return false;
            }

            string ownershipPath = AssetDatabase.GUIDToAssetPath(settings.ownershipMapGuid);
            if (string.IsNullOrEmpty(ownershipPath))
            {
                Debug.LogError($"[KitbashDerivedFbxAddon] Ownership map GUID {settings.ownershipMapGuid} not found for {derivedFbxPath}");
                return false;
            }

            var ownershipMap = AssetDatabase.LoadAssetAtPath<OwnershipMap>(ownershipPath);
            if (ownershipMap == null)
            {
                Debug.LogError($"[KitbashDerivedFbxAddon] Failed to load ownership map at {ownershipPath}");
                return false;
            }
            
            // Ensure recipe hash is computed
            recipe.ComputeHash();
            
            Debug.Log($"[KitbashDerivedFbxAddon] Processing kitbash for {derivedFbxPath} with recipe hash {recipe.recipeHash}");
            
            try
            {
                // Build triangle mapping from ownership data
                if (!TryBuildTriangleMap(derivedFbxPath, recipe, ownershipMap, out int[] sourcePartIndices, out int[] sourceTriangleIndices))
                {
                    Debug.LogError($"[KitbashDerivedFbxAddon] Failed to build triangle map for {derivedFbxPath}");
                    return false;
                }

                // Build synthetic base FBX from mapped triangles
                string syntheticBasePath = BuildSyntheticBase(recipe, sourcePartIndices, sourceTriangleIndices);
                if (string.IsNullOrEmpty(syntheticBasePath))
                {
                    Debug.LogError($"[KitbashDerivedFbxAddon] Failed to build synthetic base for {derivedFbxPath}");
                    return false;
                }
                
                // Use existing PatchBuilder to create the derived asset
                var policy = new DerivedFbxAsset.Policy();
                var hints = new DerivedFbxAsset.UIHints
                {
                    friendlyName = string.IsNullOrEmpty(settings.friendlyName)
                        ? Path.GetFileNameWithoutExtension(derivedFbxPath)
                        : settings.friendlyName,
                    category = settings.category
                };
                var seeds = new DerivedFbxAsset.SeedMaps();
                
                var derivedAsset = PatchBuilder.BuildDerivedFbxAsset(
                    syntheticBasePath, 
                    derivedFbxPath, 
                    policy, 
                    hints, 
                    seeds);
                
                if (derivedAsset == null)
                {
                    Debug.LogError($"[KitbashDerivedFbxAddon] BuildDerivedFbxAsset returned null for {derivedFbxPath}");
                    return false;
                }
                
                // Add kitbash-specific fields
                derivedAsset.mode = DerivedMode.KitbashRecipeHdiff;
                derivedAsset.kitbashRecipeJson = recipe.ToJson();
                derivedAsset.recipeHash = recipe.recipeHash;
                derivedAsset.requiredSourceGuids = recipe.GetSourceGuids();
                derivedAsset.kitbashSourcePartIndices = sourcePartIndices;
                derivedAsset.kitbashSourceTriangleIndices = sourceTriangleIndices;
                
                // Save the derived asset
                string derivedFbxGuid = AssetDatabase.AssetPathToGUID(derivedFbxPath);
                if (string.IsNullOrEmpty(derivedFbxGuid))
                {
                    derivedFbxGuid = Guid.NewGuid().ToString("N");
                }
                
                string fileName = $"DerivedFbxAsset_{derivedFbxGuid.Substring(0, 8)}_{SanitizeFileName(hints.friendlyName)}.asset";
                string pkgPath = $"Packages/com.yucp.temp/Patches/{fileName}";
                
                AssetDatabase.CreateAsset(derivedAsset, pkgPath);
                tempAssetPath = pkgPath;
                
                // Also add the hdiff file
                if (!string.IsNullOrEmpty(derivedAsset.hdiffFilePath) && ctx.AssetsToExport != null)
                {
                    if (!ctx.AssetsToExport.Contains(derivedAsset.hdiffFilePath))
                    {
                        ctx.AssetsToExport.Add(derivedAsset.hdiffFilePath);
                    }
                }
                
                Debug.Log($"[KitbashDerivedFbxAddon] Successfully created kitbash patch at {pkgPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KitbashDerivedFbxAddon] Exception processing {derivedFbxPath}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// Builds the synthetic base FBX from the recipe.
        /// Delegates to SyntheticBaseBuilder with caching.
        /// </summary>
        private string BuildSyntheticBase(KitbashRecipe recipe, int[] sourcePartIndices, int[] sourceTriangleIndices)
        {
            // Attempt fallback resolution for any missing GUIDs
            var unresolved = recipe.ValidateAndResolve();
            if (unresolved.Count > 0)
            {
                Debug.LogWarning($"[KitbashDerivedFbxAddon] Some parts could not be resolved by hints: {string.Join(", ", unresolved)}");
            }
            
            // Validate sources
            var (valid, missing) = SyntheticBaseBuilder.ValidateSources(recipe);
            if (!valid)
            {
                Debug.LogError($"[KitbashDerivedFbxAddon] Missing source FBXs: {string.Join(", ", missing)}");
                return null;
            }
            
            // Ensure all parts have hints populated
            foreach (var part in recipe.parts)
            {
                if (string.IsNullOrEmpty(part.hints.originalFileName))
                {
                    // Populate hints for this part
                    string sourcePath = AssetDatabase.GUIDToAssetPath(part.sourceFbxGuid);
                    if (!string.IsNullOrEmpty(sourcePath))
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                        if (prefab != null)
                        {
                            part.hints = KitbashRecipe.GenerateHints(sourcePath, prefab);
                        }
                    }
                }
            }
            
            // Recompute hash after any hint updates
            recipe.ComputeHash();
            EditorUtility.SetDirty(recipe);
            
            // Build using SyntheticBaseBuilder (handles caching internally)
            return SyntheticBaseBuilder.BuildFromRecipeWithMapping(recipe, sourcePartIndices, sourceTriangleIndices);
        }

        private static bool TryBuildTriangleMap(
            string derivedFbxPath,
            KitbashRecipe recipe,
            OwnershipMap ownershipMap,
            out int[] sourcePartIndices,
            out int[] sourceTriangleIndices)
        {
            sourcePartIndices = null;
            sourceTriangleIndices = null;

            GameObject derivedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(derivedFbxPath);
            if (derivedPrefab == null)
            {
                Debug.LogError($"[KitbashDerivedFbxAddon] Failed to load derived FBX prefab: {derivedFbxPath}");
                return false;
            }

            Mesh targetMesh = FindTargetMesh(derivedPrefab, ownershipMap.targetMeshName);
            if (targetMesh == null)
            {
                Debug.LogError($"[KitbashDerivedFbxAddon] Target mesh not found for ownership map: {derivedFbxPath}");
                return false;
            }
            
            int[] targetTriangles = GetAllTriangles(targetMesh);
            int targetTriCount = targetTriangles.Length / 3;
            if (targetTriCount == 0)
            {
                Debug.LogError("[KitbashDerivedFbxAddon] Target mesh has no triangles");
                return false;
            }
            
            bool legacyLayerIndexing = ownershipMap.regions.Any(r => r.sourcePartIndex >= recipe.parts.Count) ||
                                      ownershipMap.regions.All(r => r.sourcePartIndex == 0);
            var ownerByTriangle = BuildOwnerArray(ownershipMap, targetMesh, targetTriangles, targetTriCount, recipe.parts.Count, legacyLayerIndexing);
            if (ownershipMap.expectedTriangleCount != targetTriCount)
            {
                Debug.LogWarning($"[KitbashDerivedFbxAddon] Ownership map triangle count mismatch for {derivedFbxPath}: map has {ownershipMap.expectedTriangleCount}, mesh has {targetTriCount}. Reprojection used.");
            }
            
            int unknownCount = 0;
            for (int i = 0; i < ownerByTriangle.Length; i++)
            {
                if (ownerByTriangle[i] < 0) unknownCount++;
            }
            if (unknownCount > 0)
            {
                Debug.LogWarning($"[KitbashDerivedFbxAddon] Ownership map has {unknownCount} unassigned triangle(s) for {derivedFbxPath}");
            }

            var targetVertices = targetMesh.vertices;
            sourcePartIndices = new int[targetTriCount];
            sourceTriangleIndices = new int[targetTriCount];

            var sourceMeshes = BuildSourceMeshData(recipe);
            if (sourceMeshes.Count == 0)
            {
                Debug.LogError("[KitbashDerivedFbxAddon] No source meshes found for mapping");
                return false;
            }

            for (int tri = 0; tri < targetTriCount; tri++)
            {
                int partIndex = ownerByTriangle[tri];
                sourcePartIndices[tri] = partIndex;

                if (partIndex < 0 || partIndex >= recipe.parts.Count)
                {
                    sourceTriangleIndices[tri] = -1;
                    continue;
                }

                if (!sourceMeshes.TryGetValue(partIndex, out var source))
                {
                    sourceTriangleIndices[tri] = -1;
                    continue;
                }

                int triBase = tri * 3;
                Vector3 a = targetVertices[targetTriangles[triBase]];
                Vector3 b = targetVertices[targetTriangles[triBase + 1]];
                Vector3 c = targetVertices[targetTriangles[triBase + 2]];
                Vector3 center = (a + b + c) / 3f;
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;

                int nearest = source.FindNearestTriangle(center, normal);
                sourceTriangleIndices[tri] = nearest;
            }

            return true;
        }

        private static Mesh FindTargetMesh(GameObject prefab, string targetMeshName)
        {
            if (!string.IsNullOrEmpty(targetMeshName))
            {
                foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh != null && smr.sharedMesh.name == targetMeshName)
                        return smr.sharedMesh;
                }
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh != null && mf.sharedMesh.name == targetMeshName)
                        return mf.sharedMesh;
                }
            }

            var firstSmr = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (firstSmr != null && firstSmr.sharedMesh != null)
                return firstSmr.sharedMesh;

            var firstMf = prefab.GetComponentInChildren<MeshFilter>(true);
            return firstMf != null ? firstMf.sharedMesh : null;
        }

        private static int[] GetAllTriangles(Mesh mesh)
        {
            if (mesh == null) return Array.Empty<int>();
            var all = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                all.AddRange(mesh.GetTriangles(s));
            }
            return all.ToArray();
        }

        private static int[] BuildOwnerArray(
            OwnershipMap ownershipMap,
            Mesh targetMesh,
            int[] targetTriangles,
            int targetTriCount,
            int partCount,
            bool legacyLayerIndexing)
        {
            var ownerByTriangle = new int[targetTriCount];
            for (int i = 0; i < ownerByTriangle.Length; i++) ownerByTriangle[i] = -1;

            if (ownershipMap.expectedTriangleCount == targetTriCount)
            {
                foreach (var region in ownershipMap.regions)
                {
                    if (region.triangleIndices == null) continue;
                    foreach (var triIndex in region.triangleIndices)
                    {
                        if (triIndex >= 0 && triIndex < ownerByTriangle.Length)
                            ownerByTriangle[triIndex] = region.sourcePartIndex;
                    }
                }
            }
            else
            {
                bool hasSamples = ownershipMap.regions.Any(r => r.samplePoints != null && r.samplePoints.Length > 0);
                if (!hasSamples)
                {
                    Debug.LogWarning("[KitbashDerivedFbxAddon] Ownership map has no sample points for reprojection; using raw triangle indices.");
                    foreach (var region in ownershipMap.regions)
                    {
                        if (region.triangleIndices == null) continue;
                        foreach (var triIndex in region.triangleIndices)
                        {
                            if (triIndex >= 0 && triIndex < ownerByTriangle.Length)
                                ownerByTriangle[triIndex] = region.sourcePartIndex;
                        }
                    }
                }
                else
                {
                    var vertices = targetMesh.vertices;
                    var meshNormals = targetMesh.normals;
                    var centers = new Vector3[targetTriCount];
                    var normals = new Vector3[targetTriCount];

                    for (int i = 0; i < targetTriCount; i++)
                    {
                        int baseIdx = i * 3;
                        Vector3 v0 = vertices[targetTriangles[baseIdx]];
                        Vector3 v1 = vertices[targetTriangles[baseIdx + 1]];
                        Vector3 v2 = vertices[targetTriangles[baseIdx + 2]];

                        centers[i] = (v0 + v1 + v2) / 3f;

                        if (meshNormals != null && meshNormals.Length > targetTriangles[baseIdx])
                        {
                            normals[i] = (meshNormals[targetTriangles[baseIdx]] +
                                          meshNormals[targetTriangles[baseIdx + 1]] +
                                          meshNormals[targetTriangles[baseIdx + 2]]).normalized;
                        }
                        else
                        {
                            normals[i] = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                        }
                    }

                    ownerByTriangle = ownershipMap.ReprojectToMeshFast(centers, normals);
                }
            }

            // Convert ownership map indices to recipe part indices
            for (int i = 0; i < ownerByTriangle.Length; i++)
            {
                int raw = ownerByTriangle[i];
                if (legacyLayerIndexing)
                {
                    ownerByTriangle[i] = raw <= 0 ? -1 : raw - 1;
                }
                else
                {
                    ownerByTriangle[i] = raw < 0 ? -1 : raw;
                }

                if (ownerByTriangle[i] >= partCount)
                    ownerByTriangle[i] = -1;
            }

            return ownerByTriangle;
        }

        private class SourceMeshData
        {
            public int[] triangles;
            public Vector3[] vertices;
            public Vector3[] centers;
            public Vector3[] normals;
            public float cellSize;
            public Vector3 origin;
            public Dictionary<Vector3Int, List<int>> buckets = new Dictionary<Vector3Int, List<int>>();

            public int FindNearestTriangle(Vector3 targetCenter, Vector3 targetNormal)
            {
                Vector3Int cell = ToCell(targetCenter);
                int bestTri = -1;
                float bestScore = float.MaxValue;

                for (int radius = 0; radius <= 2; radius++)
                {
                    bool found = false;
                    for (int x = -radius; x <= radius; x++)
                    {
                        for (int y = -radius; y <= radius; y++)
                        {
                            for (int z = -radius; z <= radius; z++)
                            {
                                var key = new Vector3Int(cell.x + x, cell.y + y, cell.z + z);
                                if (!buckets.TryGetValue(key, out var list)) continue;
                                foreach (var triIndex in list)
                                {
                                    Vector3 c = centers[triIndex];
                                    float distSq = (c - targetCenter).sqrMagnitude;
                                    float normalDot = Vector3.Dot(normals[triIndex], targetNormal);
                                    if (normalDot < 0.2f) continue;
                                    float score = distSq * (2f - normalDot);
                                    if (score < bestScore)
                                    {
                                        bestScore = score;
                                        bestTri = triIndex;
                                        found = true;
                                    }
                                }
                            }
                        }
                    }
                    if (found) break;
                }

                if (bestTri < 0)
                {
                    // Fallback to full scan
                    for (int i = 0; i < centers.Length; i++)
                    {
                        float distSq = (centers[i] - targetCenter).sqrMagnitude;
                        float score = distSq;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestTri = i;
                        }
                    }
                }

                return bestTri;
            }

            private Vector3Int ToCell(Vector3 pos)
            {
                Vector3 rel = pos - origin;
                return new Vector3Int(
                    Mathf.FloorToInt(rel.x / cellSize),
                    Mathf.FloorToInt(rel.y / cellSize),
                    Mathf.FloorToInt(rel.z / cellSize));
            }
        }

        private static Dictionary<int, SourceMeshData> BuildSourceMeshData(KitbashRecipe recipe)
        {
            var result = new Dictionary<int, SourceMeshData>();

            for (int partIndex = 0; partIndex < recipe.parts.Count; partIndex++)
            {
                var part = recipe.parts[partIndex];
                if (string.IsNullOrEmpty(part.sourceFbxGuid)) continue;

                string sourcePath = AssetDatabase.GUIDToAssetPath(part.sourceFbxGuid);
                if (string.IsNullOrEmpty(sourcePath)) continue;

                GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (sourcePrefab == null) continue;

                Transform meshTransform = sourcePrefab.transform;
                if (!string.IsNullOrEmpty(part.meshPath))
                {
                    var found = sourcePrefab.transform.Find(part.meshPath);
                    if (found != null) meshTransform = found;
                }

                Mesh mesh = null;
                var smr = meshTransform.GetComponent<SkinnedMeshRenderer>();
                if (smr != null) mesh = smr.sharedMesh;
                if (mesh == null)
                {
                    var mf = meshTransform.GetComponent<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }

                if (mesh == null) continue;

                int[] triangles = GetAllTriangles(mesh);
                if (triangles == null || triangles.Length == 0) continue;

                var vertices = mesh.vertices;
                int triCount = triangles.Length / 3;
                var centers = new Vector3[triCount];
                var normals = new Vector3[triCount];

                Matrix4x4 meshToRoot = meshTransform.localToWorldMatrix;
                Matrix4x4 partMatrix = Matrix4x4.TRS(part.positionOffset, part.rotationOffset, part.scaleMultiplier);

                for (int t = 0; t < triCount; t++)
                {
                    int triBase = t * 3;
                    Vector3 v0 = meshToRoot.MultiplyPoint3x4(vertices[triangles[triBase]]);
                    Vector3 v1 = meshToRoot.MultiplyPoint3x4(vertices[triangles[triBase + 1]]);
                    Vector3 v2 = meshToRoot.MultiplyPoint3x4(vertices[triangles[triBase + 2]]);

                    v0 = partMatrix.MultiplyPoint3x4(v0);
                    v1 = partMatrix.MultiplyPoint3x4(v1);
                    v2 = partMatrix.MultiplyPoint3x4(v2);

                    Vector3 center = (v0 + v1 + v2) / 3f;
                    Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                    centers[t] = center;
                    normals[t] = normal;
                }

                var bounds = new Bounds(centers[0], Vector3.zero);
                for (int i = 1; i < centers.Length; i++)
                    bounds.Encapsulate(centers[i]);
                float cellSize = Mathf.Max(bounds.size.magnitude / 64f, 0.001f);

                var data = new SourceMeshData
                {
                    triangles = triangles,
                    vertices = vertices,
                    centers = centers,
                    normals = normals,
                    cellSize = cellSize,
                    origin = bounds.min
                };

                for (int t = 0; t < triCount; t++)
                {
                    Vector3 c = centers[t];
                    Vector3 rel = c - data.origin;
                    var key = new Vector3Int(
                        Mathf.FloorToInt(rel.x / cellSize),
                        Mathf.FloorToInt(rel.y / cellSize),
                        Mathf.FloorToInt(rel.z / cellSize));

                    if (!data.buckets.TryGetValue(key, out var list))
                    {
                        list = new List<int>();
                        data.buckets[key] = list;
                    }
                    list.Add(t);
                }

                result[partIndex] = data;
            }

            return result;
        }
        
        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
#endif
