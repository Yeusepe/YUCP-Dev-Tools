using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEditor;

namespace YUCP.DevTools.Editor.Optimizer
{
    /// <summary>
    /// A snapshot of footprint metrics for a hierarchy, used to report before/after results.
    /// </summary>
    [Serializable]
    public struct OptimizerStats
    {
        public int rendererCount;
        public int materialCount;   // unique material assets
        public int subMeshCount;    // ~= draw calls / material slots
        public long triangleCount;
        public int textureCount;    // unique texture assets
        public long textureBytes;   // estimated VRAM of unique textures

        public string TextureMegabytes => (textureBytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";

        public override string ToString()
        {
            return $"{rendererCount} renderers, {materialCount} materials, {subMeshCount} slots, " +
                   $"{triangleCount:N0} tris, {textureCount} textures (~{TextureMegabytes})";
        }
    }

    /// <summary>
    /// Collects <see cref="OptimizerStats"/> from a GameObject hierarchy. Mirrors the renderer/submesh/
    /// material counting approach used by d4rkAvatarOptimizerEditor, and estimates texture VRAM via
    /// <see cref="Profiler.GetRuntimeMemorySizeLong"/>.
    /// </summary>
    public static class OptimizerStatsCollector
    {
        public static OptimizerStats Collect(GameObject root)
        {
            var stats = new OptimizerStats();
            if (root == null) return stats;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var uniqueMaterials = new HashSet<Material>();
            var uniqueTextures = new HashSet<Texture>();

            foreach (var renderer in renderers)
            {
                if (renderer is ParticleSystemRenderer)
                    continue;

                stats.rendererCount++;

                var mesh = GetSharedMesh(renderer);
                if (mesh != null)
                {
                    int subMeshes = mesh.subMeshCount;
                    stats.subMeshCount += subMeshes;
                    for (int i = 0; i < subMeshes; i++)
                    {
                        if (mesh.GetTopology(i) == MeshTopology.Triangles)
                            stats.triangleCount += mesh.GetIndexCount(i) / 3;
                    }
                }

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !uniqueMaterials.Add(material))
                        continue;
                    CollectTextures(material, uniqueTextures);
                }
            }

            stats.materialCount = uniqueMaterials.Count;
            stats.textureCount = uniqueTextures.Count;
            foreach (var texture in uniqueTextures)
                stats.textureBytes += Profiler.GetRuntimeMemorySizeLong(texture);

            return stats;
        }

        /// <summary>Returns the shared mesh for either a SkinnedMeshRenderer or a MeshRenderer (via its MeshFilter).</summary>
        public static Mesh GetSharedMesh(Renderer renderer)
        {
            switch (renderer)
            {
                case SkinnedMeshRenderer smr:
                    return smr.sharedMesh;
                case MeshRenderer _:
                    var filter = renderer.GetComponent<MeshFilter>();
                    return filter != null ? filter.sharedMesh : null;
                default:
                    return null;
            }
        }

        /// <summary>Adds every non-null texture referenced by the material's shader texture properties.</summary>
        public static void CollectTextures(Material material, HashSet<Texture> into)
        {
            var shader = material.shader;
            if (shader == null) return;

            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                    continue;

                string propName = ShaderUtil.GetPropertyName(shader, i);
                if (!material.HasProperty(propName))
                    continue;

                var texture = material.GetTexture(propName);
                if (texture != null)
                    into.Add(texture);
            }
        }
    }
}
