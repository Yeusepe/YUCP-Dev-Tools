#if YUCP_KITBASH_ENABLED
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash
{
    /// <summary>
    /// Builds synthetic base FBX files from kitbash recipes.
    /// Requires Unity FBX Exporter package (com.unity.formats.fbx).
    /// </summary>
    public static class SyntheticBaseBuilder
    {
        private const string FBX_EXPORTER_TYPE = "UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor";
        private const string CACHE_DIR = "Library/YUCP/KitbashCache";
        
        private static Type _modelExporterType;
        private static MethodInfo _exportObjectMethod;
        private static bool _checkedForFbxExporter;
        
        /// <summary>
        /// Checks if Unity FBX Exporter package is installed.
        /// </summary>
        public static bool IsFbxExporterAvailable()
        {
            if (!_checkedForFbxExporter)
            {
                _modelExporterType = Type.GetType(FBX_EXPORTER_TYPE);
                _checkedForFbxExporter = true;
                
                if (_modelExporterType != null)
                {
                    // Find the ExportObject method
                    _exportObjectMethod = _modelExporterType.GetMethod("ExportObject",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new Type[] { typeof(string), typeof(UnityEngine.Object) },
                        null);
                }
            }
            
            return _modelExporterType != null && _exportObjectMethod != null;
        }
        
        /// <summary>
        /// Builds a synthetic base FBX from a recipe.
        /// Returns the path to the created FBX file (cached if unchanged).
        /// </summary>
        public static string BuildFromRecipe(KitbashRecipe recipe)
        {
            if (recipe == null)
            {
                Debug.LogError("[SyntheticBaseBuilder] Recipe is null");
                return null;
            }
            
            // Ensure hash is computed
            recipe.ComputeHash();
            
            // Check cache
            string cachePath = GetCachePath(recipe.recipeHash);
            if (File.Exists(cachePath))
            {
                Debug.Log($"[SyntheticBaseBuilder] Using cached synthetic base: {cachePath}");
                return cachePath;
            }
            
            // Check FBX Exporter
            if (!IsFbxExporterAvailable())
            {
                Debug.LogError("[SyntheticBaseBuilder] Unity FBX Exporter package (com.unity.formats.fbx) is required.\n" +
                    "Install via Package Manager: Window > Package Manager > + > Add package by name > com.unity.formats.fbx");
                return null;
            }
            
            try
            {
                // Assemble GameObjects from recipe
                GameObject root = AssembleFromRecipe(recipe);
                if (root == null)
                {
                    Debug.LogError("[SyntheticBaseBuilder] Failed to assemble from recipe");
                    return null;
                }
                
                try
                {
                    // Export to FBX
                    if (!ExportToFbx(root, cachePath))
                    {
                        Debug.LogError($"[SyntheticBaseBuilder] Failed to export FBX to {cachePath}");
                        return null;
                    }
                    
                    Debug.Log($"[SyntheticBaseBuilder] Created synthetic base: {cachePath}");
                    return cachePath;
                }
                finally
                {
                    // Clean up temporary GameObject
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SyntheticBaseBuilder] Error building synthetic base: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }
        
        /// <summary>
        /// Gets the cache path for a given recipe hash.
        /// </summary>
        public static string GetCachePath(string recipeHash)
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string cacheDir = Path.Combine(projectPath, CACHE_DIR);
            Directory.CreateDirectory(cacheDir);
            return Path.Combine(cacheDir, $"{recipeHash}.fbx");
        }

        /// <summary>
        /// Builds a synthetic base FBX from a recipe and per-triangle source mapping.
        /// Returns the path to the created FBX file (cached if unchanged).
        /// </summary>
        public static string BuildFromRecipeWithMapping(KitbashRecipe recipe, int[] sourcePartIndices, int[] sourceTriangleIndices)
        {
            if (recipe == null)
            {
                Debug.LogError("[SyntheticBaseBuilder] Recipe is null");
                return null;
            }

            if (sourcePartIndices == null || sourceTriangleIndices == null || sourcePartIndices.Length != sourceTriangleIndices.Length)
            {
                Debug.LogError("[SyntheticBaseBuilder] Invalid kitbash triangle mapping arrays");
                return null;
            }

            if (sourcePartIndices.Length == 0)
            {
                Debug.LogError("[SyntheticBaseBuilder] Kitbash triangle mapping is empty");
                return null;
            }

            // Ensure hash is computed
            recipe.ComputeHash();

            string mapHash = ComputeMappingHash(recipe.recipeHash, sourcePartIndices, sourceTriangleIndices);
            string cachePath = GetCachePath(mapHash);
            if (File.Exists(cachePath))
            {
                Debug.Log($"[SyntheticBaseBuilder] Using cached synthetic base (mapped): {cachePath}");
                return cachePath;
            }

            if (!IsFbxExporterAvailable())
            {
                Debug.LogError("[SyntheticBaseBuilder] Unity FBX Exporter package (com.unity.formats.fbx) is required.\n" +
                    "Install via Package Manager: Window > Package Manager > + > Add package by name > com.unity.formats.fbx");
                return null;
            }

            try
            {
                GameObject root = AssembleFromMapping(recipe, sourcePartIndices, sourceTriangleIndices);
                if (root == null)
                {
                    Debug.LogError("[SyntheticBaseBuilder] Failed to assemble from mapping");
                    return null;
                }

                try
                {
                    if (!ExportToFbx(root, cachePath))
                    {
                        Debug.LogError($"[SyntheticBaseBuilder] Failed to export FBX to {cachePath}");
                        return null;
                    }

                    Debug.Log($"[SyntheticBaseBuilder] Created synthetic base (mapped): {cachePath}");
                    return cachePath;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SyntheticBaseBuilder] Error building synthetic base (mapped): {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }
        
        /// <summary>
        /// Clears the synthetic base cache.
        /// </summary>
        public static void ClearCache()
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string cacheDir = Path.Combine(projectPath, CACHE_DIR);
            
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
                Debug.Log("[SyntheticBaseBuilder] Cache cleared");
            }
        }
        
        /// <summary>
        /// Assembles a GameObject hierarchy from a kitbash recipe.
        /// </summary>
        private static GameObject AssembleFromRecipe(KitbashRecipe recipe)
        {
            if (recipe.parts == null || recipe.parts.Count == 0)
            {
                Debug.LogWarning("[SyntheticBaseBuilder] Recipe has no parts");
                return null;
            }
            
            // Create root object
            GameObject root = new GameObject("SyntheticBase");
            
            // Track armature for merging
            Transform armatureRoot = null;
            
            foreach (var part in recipe.parts)
            {
                if (string.IsNullOrEmpty(part.sourceFbxGuid))
                {
                    Debug.LogWarning($"[SyntheticBaseBuilder] Part has no source GUID: {part.displayName}");
                    continue;
                }
                
                // Load source FBX
                string sourcePath = AssetDatabase.GUIDToAssetPath(part.sourceFbxGuid);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    Debug.LogWarning($"[SyntheticBaseBuilder] Source not found for GUID: {part.sourceFbxGuid}");
                    continue;
                }
                
                GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (sourcePrefab == null)
                {
                    Debug.LogWarning($"[SyntheticBaseBuilder] Failed to load source: {sourcePath}");
                    continue;
                }
                
                // Find the specific mesh path if specified
                Transform sourceTransform = sourcePrefab.transform;
                if (!string.IsNullOrEmpty(part.meshPath))
                {
                    sourceTransform = sourcePrefab.transform.Find(part.meshPath);
                    if (sourceTransform == null)
                    {
                        Debug.LogWarning($"[SyntheticBaseBuilder] Mesh path not found: {part.meshPath} in {sourcePath}");
                        sourceTransform = sourcePrefab.transform;
                    }
                }
                
                // Instantiate the part
                GameObject partInstance = UnityEngine.Object.Instantiate(sourceTransform.gameObject);
                partInstance.name = !string.IsNullOrEmpty(part.displayName) 
                    ? part.displayName 
                    : sourceTransform.name;
                
                // Apply transforms
                partInstance.transform.SetParent(root.transform);
                partInstance.transform.localPosition = part.positionOffset;
                partInstance.transform.localRotation = part.rotationOffset;
                partInstance.transform.localScale = Vector3.Scale(partInstance.transform.localScale, part.scaleMultiplier);
                
                // Handle armature merging
                if (!string.IsNullOrEmpty(part.boneRootMapping) && armatureRoot == null)
                {
                    // Find armature in this part
                    var armature = FindArmature(partInstance.transform);
                    if (armature != null)
                    {
                        armatureRoot = armature;
                    }
                }
            }
            
            return root;
        }

        /// <summary>
        /// Assembles a GameObject hierarchy from a kitbash recipe and per-triangle mapping.
        /// </summary>
        private static GameObject AssembleFromMapping(KitbashRecipe recipe, int[] sourcePartIndices, int[] sourceTriangleIndices)
        {
            if (recipe.parts == null || recipe.parts.Count == 0)
            {
                Debug.LogWarning("[SyntheticBaseBuilder] Recipe has no parts");
                return null;
            }

            var trianglesByPart = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < sourcePartIndices.Length; i++)
            {
                int partIndex = sourcePartIndices[i];
                int triIndex = sourceTriangleIndices[i];
                if (partIndex < 0 || triIndex < 0) continue;
                if (!trianglesByPart.TryGetValue(partIndex, out var set))
                {
                    set = new HashSet<int>();
                    trianglesByPart[partIndex] = set;
                }
                set.Add(triIndex);
            }

            GameObject root = new GameObject("SyntheticBase");

            for (int partIndex = 0; partIndex < recipe.parts.Count; partIndex++)
            {
                if (!trianglesByPart.TryGetValue(partIndex, out var triSet) || triSet.Count == 0)
                    continue;

                var part = recipe.parts[partIndex];
                if (string.IsNullOrEmpty(part.sourceFbxGuid))
                {
                    Debug.LogWarning($"[SyntheticBaseBuilder] Part has no source GUID: {part.displayName}");
                    continue;
                }

                string sourcePath = AssetDatabase.GUIDToAssetPath(part.sourceFbxGuid);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    Debug.LogWarning($"[SyntheticBaseBuilder] Source not found for GUID: {part.sourceFbxGuid}");
                    continue;
                }

                GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (sourcePrefab == null)
                {
                    Debug.LogWarning($"[SyntheticBaseBuilder] Failed to load source: {sourcePath}");
                    continue;
                }

                GameObject partRoot = UnityEngine.Object.Instantiate(sourcePrefab);
                partRoot.name = !string.IsNullOrEmpty(part.displayName) ? part.displayName : sourcePrefab.name;

                Transform sourceTransform = partRoot.transform;
                if (!string.IsNullOrEmpty(part.meshPath))
                {
                    var found = partRoot.transform.Find(part.meshPath);
                    if (found != null)
                        sourceTransform = found;
                    else
                        Debug.LogWarning($"[SyntheticBaseBuilder] Mesh path not found: {part.meshPath} in {sourcePath}");
                }

                if (!TryFilterMeshOnTransform(sourceTransform, triSet))
                {
                    UnityEngine.Object.DestroyImmediate(partRoot);
                    continue;
                }

                // Disable other renderers to avoid exporting unrelated meshes
                foreach (var renderer in partRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.transform != sourceTransform)
                        UnityEngine.Object.DestroyImmediate(renderer);
                }
                foreach (var filter in partRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.transform != sourceTransform)
                        UnityEngine.Object.DestroyImmediate(filter);
                }

                partRoot.transform.SetParent(root.transform);
                partRoot.transform.localPosition = part.positionOffset;
                partRoot.transform.localRotation = part.rotationOffset;
                partRoot.transform.localScale = Vector3.Scale(partRoot.transform.localScale, part.scaleMultiplier);
            }

            return root;
        }

        private static bool TryFilterMeshOnTransform(Transform targetTransform, HashSet<int> triangleSet)
        {
            if (targetTransform == null || triangleSet == null || triangleSet.Count == 0)
                return false;

            var skinned = targetTransform.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null && skinned.sharedMesh != null)
            {
                var filtered = BuildFilteredMesh(skinned.sharedMesh, triangleSet);
                if (filtered == null) return false;
                skinned.sharedMesh = filtered;
                return true;
            }

            var filter = targetTransform.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                var filtered = BuildFilteredMesh(filter.sharedMesh, triangleSet);
                if (filtered == null) return false;
                filter.sharedMesh = filtered;
                return true;
            }

            Debug.LogWarning("[SyntheticBaseBuilder] No mesh renderer found on target transform");
            return false;
        }

        private static Mesh BuildFilteredMesh(Mesh sourceMesh, HashSet<int> triangleSet)
        {
            if (sourceMesh == null || triangleSet == null || triangleSet.Count == 0)
                return null;

            var globalTriangles = new List<int>();
            int submeshCount = sourceMesh.subMeshCount;
            var submeshOffsets = new int[submeshCount];
            var globalToSubmesh = new int[0];
            int globalTriCount = 0;
            
            for (int s = 0; s < submeshCount; s++)
            {
                submeshOffsets[s] = globalTriCount;
                var subTriangles = sourceMesh.GetTriangles(s);
                globalTriangles.AddRange(subTriangles);
                globalTriCount += subTriangles.Length / 3;
            }
            
            if (globalTriCount == 0)
                return null;
            
            globalToSubmesh = new int[globalTriCount];
            for (int s = 0; s < submeshCount; s++)
            {
                int start = submeshOffsets[s];
                int count = sourceMesh.GetTriangles(s).Length / 3;
                for (int t = 0; t < count; t++)
                {
                    globalToSubmesh[start + t] = s;
                }
            }

            int[] triangles = globalTriangles.ToArray();

            var vertices = sourceMesh.vertices;
            var normals = sourceMesh.normals;
            var tangents = sourceMesh.tangents;
            var colors = sourceMesh.colors;
            var colors32 = sourceMesh.colors32;
            var boneWeights = sourceMesh.boneWeights;

            var outVertices = new List<Vector3>(triangleSet.Count * 3);
            var sourceVertexIndices = new List<int>(triangleSet.Count * 3);
            var outNormals = normals != null && normals.Length == vertices.Length ? new List<Vector3>(triangleSet.Count * 3) : null;
            var outTangents = tangents != null && tangents.Length == vertices.Length ? new List<Vector4>(triangleSet.Count * 3) : null;
            List<Color> outColors = null;
            List<Color32> outColors32 = null;
            if (colors != null && colors.Length == vertices.Length)
                outColors = new List<Color>(triangleSet.Count * 3);
            else if (colors32 != null && colors32.Length == vertices.Length)
                outColors32 = new List<Color32>(triangleSet.Count * 3);
            var outBoneWeights = boneWeights != null && boneWeights.Length == vertices.Length ? new List<BoneWeight>(triangleSet.Count * 3) : null;
            var outUvs = new List<Vector4>[8];
            var srcUvs = new List<Vector4>[8];
            for (int ch = 0; ch < outUvs.Length; ch++)
            {
                var src = new List<Vector4>();
                sourceMesh.GetUVs(ch, src);
                if (src != null && src.Count == vertices.Length)
                {
                    srcUvs[ch] = src;
                    outUvs[ch] = new List<Vector4>(triangleSet.Count * 3);
                }
            }

            var submeshTriangles = new List<List<int>>(submeshCount);
            for (int s = 0; s < submeshCount; s++)
                submeshTriangles.Add(new List<int>());

            var sorted = new List<int>(triangleSet);
            sorted.Sort();

            foreach (var triIndex in sorted)
            {
                int triBase = triIndex * 3;
                if (triBase + 2 >= triangles.Length)
                    continue;

                int i0 = triangles[triBase];
                int i1 = triangles[triBase + 1];
                int i2 = triangles[triBase + 2];

                int baseIndex = outVertices.Count;
                outVertices.Add(vertices[i0]);
                outVertices.Add(vertices[i1]);
                outVertices.Add(vertices[i2]);
                sourceVertexIndices.Add(i0);
                sourceVertexIndices.Add(i1);
                sourceVertexIndices.Add(i2);

                outNormals?.Add(normals[i0]);
                outNormals?.Add(normals[i1]);
                outNormals?.Add(normals[i2]);

                outTangents?.Add(tangents[i0]);
                outTangents?.Add(tangents[i1]);
                outTangents?.Add(tangents[i2]);

                if (outColors != null)
                {
                    outColors.Add(colors[i0]);
                    outColors.Add(colors[i1]);
                    outColors.Add(colors[i2]);
                }
                else if (outColors32 != null)
                {
                    outColors32.Add(colors32[i0]);
                    outColors32.Add(colors32[i1]);
                    outColors32.Add(colors32[i2]);
                }
                for (int ch = 0; ch < outUvs.Length; ch++)
                {
                    if (outUvs[ch] == null) continue;
                    var src = srcUvs[ch];
                    outUvs[ch].Add(src[i0]);
                    outUvs[ch].Add(src[i1]);
                    outUvs[ch].Add(src[i2]);
                }

                outBoneWeights?.Add(boneWeights[i0]);
                outBoneWeights?.Add(boneWeights[i1]);
                outBoneWeights?.Add(boneWeights[i2]);

                int submeshIndex = globalToSubmesh[triIndex];
                submeshTriangles[submeshIndex].Add(baseIndex);
                submeshTriangles[submeshIndex].Add(baseIndex + 1);
                submeshTriangles[submeshIndex].Add(baseIndex + 2);
            }

            var mesh = new Mesh();
            mesh.name = $"{sourceMesh.name}_KitbashFiltered";
            mesh.indexFormat = sourceMesh.indexFormat;
            mesh.SetVertices(outVertices);
            if (outNormals != null) mesh.SetNormals(outNormals);
            if (outTangents != null) mesh.SetTangents(outTangents);
            if (outColors != null) mesh.SetColors(outColors);
            else if (outColors32 != null) mesh.SetColors(outColors32);
            for (int ch = 0; ch < outUvs.Length; ch++)
            {
                if (outUvs[ch] != null)
                    mesh.SetUVs(ch, outUvs[ch]);
            }
            if (outBoneWeights != null)
            {
                mesh.boneWeights = outBoneWeights.ToArray();
                mesh.bindposes = sourceMesh.bindposes;
            }
            mesh.subMeshCount = submeshCount;
            for (int s = 0; s < submeshCount; s++)
            {
                var subTris = submeshTriangles[s];
                mesh.SetTriangles(subTris, s);
            }
            CopyBlendShapes(sourceMesh, mesh, sourceVertexIndices);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void CopyBlendShapes(Mesh sourceMesh, Mesh targetMesh, List<int> sourceVertexIndices)
        {
            int blendShapeCount = sourceMesh.blendShapeCount;
            if (blendShapeCount == 0) return;
            
            var srcDeltaVertices = new Vector3[sourceMesh.vertexCount];
            var srcDeltaNormals = new Vector3[sourceMesh.vertexCount];
            var srcDeltaTangents = new Vector3[sourceMesh.vertexCount];
            
            int newVertexCount = sourceVertexIndices.Count;
            var outDeltaVertices = new Vector3[newVertexCount];
            var outDeltaNormals = new Vector3[newVertexCount];
            var outDeltaTangents = new Vector3[newVertexCount];
            
            for (int i = 0; i < blendShapeCount; i++)
            {
                string shapeName = sourceMesh.GetBlendShapeName(i);
                int frameCount = sourceMesh.GetBlendShapeFrameCount(i);
                
                for (int f = 0; f < frameCount; f++)
                {
                    sourceMesh.GetBlendShapeFrameVertices(i, f, srcDeltaVertices, srcDeltaNormals, srcDeltaTangents);
                    float weight = sourceMesh.GetBlendShapeFrameWeight(i, f);
                    
                    for (int v = 0; v < newVertexCount; v++)
                    {
                        int srcIdx = sourceVertexIndices[v];
                        outDeltaVertices[v] = srcDeltaVertices[srcIdx];
                        outDeltaNormals[v] = srcDeltaNormals[srcIdx];
                        outDeltaTangents[v] = srcDeltaTangents[srcIdx];
                    }
                    
                    targetMesh.AddBlendShapeFrame(shapeName, weight, outDeltaVertices, outDeltaNormals, outDeltaTangents);
                }
            }
        }

        private static string ComputeMappingHash(string recipeHash, int[] sourcePartIndices, int[] sourceTriangleIndices)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                void AddBytes(byte[] bytes)
                {
                    md5.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }

                byte[] recipeBytes = System.Text.Encoding.UTF8.GetBytes(recipeHash ?? "");
                AddBytes(recipeBytes);

                byte[] partBytes = new byte[sourcePartIndices.Length * sizeof(int)];
                Buffer.BlockCopy(sourcePartIndices, 0, partBytes, 0, partBytes.Length);
                AddBytes(partBytes);

                byte[] triBytes = new byte[sourceTriangleIndices.Length * sizeof(int)];
                Buffer.BlockCopy(sourceTriangleIndices, 0, triBytes, 0, triBytes.Length);
                AddBytes(triBytes);

                md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(md5.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
        
        /// <summary>
        /// Finds the armature root in a transform hierarchy.
        /// </summary>
        private static Transform FindArmature(Transform root)
        {
            // Common armature names
            string[] armatureNames = { "Armature", "armature", "Skeleton", "skeleton", "Root", "root", "Hips", "hips" };
            
            foreach (string name in armatureNames)
            {
                var found = root.Find(name);
                if (found != null) return found;
            }
            
            // Search children
            foreach (Transform child in root)
            {
                var found = FindArmature(child);
                if (found != null) return found;
            }
            
            return null;
        }
        
        /// <summary>
        /// Exports a GameObject to FBX using Unity FBX Exporter.
        /// </summary>
        private static bool ExportToFbx(GameObject obj, string path)
        {
            if (!IsFbxExporterAvailable())
            {
                return false;
            }
            
            try
            {
                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                
                // Call ModelExporter.ExportObject(path, obj)
                object result = _exportObjectMethod.Invoke(null, new object[] { path, obj });
                
                // Check result (returns filename on success, null on failure)
                return result != null && File.Exists(path);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SyntheticBaseBuilder] FBX export failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Validates that all source FBXs for a recipe are available.
        /// </summary>
        public static (bool valid, List<string> missingGuids) ValidateSources(KitbashRecipe recipe)
        {
            var missing = new List<string>();
            
            if (recipe?.parts == null)
            {
                return (false, missing);
            }
            
            foreach (var part in recipe.parts)
            {
                if (string.IsNullOrEmpty(part.sourceFbxGuid)) continue;
                
                string path = AssetDatabase.GUIDToAssetPath(part.sourceFbxGuid);
                if (string.IsNullOrEmpty(path))
                {
                    missing.Add(part.sourceFbxGuid);
                }
            }
            
            return (missing.Count == 0, missing);
        }
    }
}
#endif
