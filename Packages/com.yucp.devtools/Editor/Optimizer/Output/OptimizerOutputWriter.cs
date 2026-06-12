using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.Optimizer.Output
{
    /// <summary>
    /// Applies the optimized result: leaves an optimized copy in the scene and/or saves it (with its
    /// generated mesh/material/texture assets) as a reusable prefab.
    /// </summary>
    public static class OptimizerOutputWriter
    {
        /// <summary>Activates the optimized copy, disables the original, and selects the copy.</summary>
        public static void FinalizeInScene(OptimizationContext ctx)
        {
            if (ctx.Root == null) return;

            ctx.Root.SetActive(true);
            if (ctx.OriginalRoot != null && ctx.OriginalRoot != ctx.Root)
                ctx.OriginalRoot.SetActive(false);

            Selection.activeGameObject = ctx.Root;
            EditorGUIUtility.PingObject(ctx.Root);
        }

        /// <summary>
        /// Persists any in-memory meshes/materials/textures referenced by the optimized copy and saves the
        /// whole hierarchy as a prefab under a per-result subfolder. Returns the prefab path, or null on failure.
        /// </summary>
        public static string SaveAsPrefab(OptimizationContext ctx)
        {
            if (ctx.Root == null) return null;

            string baseFolder = string.IsNullOrEmpty(ctx.Options.prefabFolder) ? "Assets/YUCP/Optimized" : ctx.Options.prefabFolder;
            string safeName = Sanitize(ctx.OriginalRoot != null ? ctx.OriginalRoot.name : ctx.Root.name);
            string folder = EnsureFolder(baseFolder + "/" + safeName);

            try
            {
                AssetDatabase.StartAssetEditing();
                PersistUnsavedDependencies(ctx.Root, folder);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
            }

            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + safeName + ".prefab");
            var prefab = PrefabUtility.SaveAsPrefabAsset(ctx.Root, prefabPath, out bool success);

            if (!success || prefab == null)
            {
                ctx.AddWarning($"Failed to save prefab at '{prefabPath}'.");
                return null;
            }

            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(prefab);
            return prefabPath;
        }

        /// <summary>
        /// Writes any mesh/material/texture referenced by the hierarchy's renderers that isn't yet an asset
        /// into <paramref name="folder"/>. Textures first, then meshes, then materials, so references resolve.
        /// </summary>
        private static void PersistUnsavedDependencies(GameObject root, string folder)
        {
            var textures = new HashSet<Texture>();
            var meshes = new HashSet<Mesh>();
            var materials = new HashSet<Material>();

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = OptimizerStatsCollector.GetSharedMesh(renderer);
                if (mesh != null) meshes.Add(mesh);

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    materials.Add(material);
                    OptimizerStatsCollector.CollectTextures(material, textures);
                }
            }

            foreach (var texture in textures)
                PersistAsset(texture, folder, ".asset");
            foreach (var mesh in meshes)
                PersistAsset(mesh, folder, ".asset");
            foreach (var material in materials)
                PersistAsset(material, folder, ".mat");
        }

        private static void PersistAsset(Object obj, string folder, string extension)
        {
            if (obj == null) return;
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(obj)))
                return; // already an asset (built-in or on disk)

            string name = string.IsNullOrEmpty(obj.name) ? obj.GetType().Name : Sanitize(obj.name);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + extension);
            AssetDatabase.CreateAsset(obj, path);
        }

        private static string EnsureFolder(string folder)
        {
            folder = folder.Replace("\\", "/").TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder))
                return folder;

            var segments = folder.Split('/');
            var path = segments[0]; // "Assets"
            for (int i = 1; i < segments.Length; i++)
            {
                var next = path + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(path, segments[i]);
                path = next;
            }
            return folder;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Optimized";
            return Regex.Replace(name, "[^a-zA-Z0-9 _\\-]", "_").Trim();
        }
    }
}
