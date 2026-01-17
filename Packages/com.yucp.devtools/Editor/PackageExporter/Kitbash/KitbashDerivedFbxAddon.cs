#if YUCP_KITBASH_ENABLED
using System;
using System.IO;
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
            
            // Ensure recipe hash is computed
            recipe.ComputeHash();
            
            Debug.Log($"[KitbashDerivedFbxAddon] Processing kitbash for {derivedFbxPath} with recipe hash {recipe.recipeHash}");
            
            try
            {
                // Build synthetic base FBX
                string syntheticBasePath = BuildSyntheticBase(recipe);
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
        private string BuildSyntheticBase(KitbashRecipe recipe)
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
            return SyntheticBaseBuilder.BuildFromRecipe(recipe);
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
