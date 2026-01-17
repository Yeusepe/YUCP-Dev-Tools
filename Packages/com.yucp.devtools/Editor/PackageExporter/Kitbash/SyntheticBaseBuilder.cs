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
