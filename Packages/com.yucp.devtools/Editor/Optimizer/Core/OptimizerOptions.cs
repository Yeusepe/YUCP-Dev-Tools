using System;

namespace YUCP.DevTools.Editor.Optimizer
{
    /// <summary>
    /// Where the optimized result is written when running the optimizer interactively.
    /// (Build-time application is handled separately by the build processor + marker component.)
    /// </summary>
    public enum OptimizerOutputMode
    {
        /// <summary>Duplicate the hierarchy, optimize the copy, disable the original. Nothing is saved to disk.</summary>
        InSceneCopy,

        /// <summary>Optimize a copy and save it (plus generated meshes/materials/textures) as a reusable prefab.</summary>
        SaveAsPrefab,

        /// <summary>Both: leave an optimized copy in the scene AND save it as a prefab.</summary>
        Both,
    }

    /// <summary>
    /// How the custom (world) atlas pipeline deals with materials whose textures tile (UV scale != 1
    /// or mesh UVs outside [0,1]). Tiled textures cannot share a packed atlas rect without artifacts.
    /// </summary>
    public enum TilingStrategy
    {
        /// <summary>Keep tiled materials as their own submesh/material; only atlas non-tiled materials.</summary>
        ExcludeTiled,

        /// <summary>Pack every material's textures into a Texture2DArray (each keeps a full slice, so tiling is preserved).</summary>
        TextureArray,
    }

    /// <summary>
    /// User-configurable options for a single optimizer run. Serializable so it can be persisted
    /// in <see cref="RendererOptimizerSettings"/> and embedded in the build-time marker component.
    /// </summary>
    [Serializable]
    public class OptimizerOptions
    {
        // ---- Shader conversion (Phase 1) ----

        /// <summary>Convert every material to a single shader before merging.</summary>
        public bool convertShaders = false;

        /// <summary>Fully-qualified name of the target shader (e.g. "Standard", "lilToon", ".poiyomi/Poiyomi Toon").</summary>
        public string targetShaderName = "Standard";

        // ---- Texture atlasing (Phase 2, world path) ----

        /// <summary>Pack textures into a shared atlas (world/static path only).</summary>
        public bool enableAtlas = true;

        /// <summary>Maximum atlas dimension in pixels.</summary>
        public int atlasMaxSize = 4096;

        /// <summary>Gutter padding (pixels) between atlas tiles to prevent mip bleeding.</summary>
        public int atlasPadding = 4;

        /// <summary>Generate mipmaps for the atlas output.</summary>
        public bool generateMips = true;

        /// <summary>How to handle materials whose textures tile.</summary>
        public TilingStrategy tilingStrategy = TilingStrategy.ExcludeTiled;

        // ---- d4rk passthrough (Phase 0, avatar path) ----

        /// <summary>Let d4rkAvatarOptimizer auto-pick settings based on avatar complexity (ignores the explicit toggles below).</summary>
        public bool useAutoSettings = false;

        public bool mergeSkinnedMeshes = true;
        public bool mergeDifferentPropertyMaterials = false;
        public bool mergeSameDimensionTextures = false;
        public bool mergeMainTex = false;
        public bool writePropertiesAsStaticValues = false;

        // ---- Output ----

        public OptimizerOutputMode outputMode = OptimizerOutputMode.InSceneCopy;

        /// <summary>Project-relative folder ("Assets/...") used when <see cref="outputMode"/> saves a prefab.</summary>
        public string prefabFolder = "Assets/YUCP/Optimized";

        public OptimizerOptions Clone()
        {
            return (OptimizerOptions)MemberwiseClone();
        }
    }
}
