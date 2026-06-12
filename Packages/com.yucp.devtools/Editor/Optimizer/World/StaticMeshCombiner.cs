using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.Optimizer.World
{
    /// <summary>
    /// Combines qualifying static MeshRenderers under a root into a single mesh + single atlased material.
    /// Correctness-first: a renderer is combined only if ALL its submeshes qualify (non-lightmapped,
    /// main texture with no tiling, has UVs). Anything else is left untouched and reported, so the
    /// visible result matches the original for the combined subset.
    /// </summary>
    public static class StaticMeshCombiner
    {
        private class Entry
        {
            public MeshRenderer Renderer;
            public Mesh Mesh;
        }

        public static void Combine(OptimizationContext ctx, Action<float, string> report)
        {
            var rootTransform = ctx.Root.transform;
            var entries = new List<Entry>();
            var mainTextures = new HashSet<Texture>();
            var skipReasons = new Dictionary<string, int>();

            void Skip(string reason) => skipReasons[reason] = skipReasons.TryGetValue(reason, out var c) ? c + 1 : 1;

            foreach (var mr in ctx.Root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsExcluded(mr.transform, ctx.Exclusions)) { Skip("excluded by user"); continue; }
                if (!mr.enabled || !mr.gameObject.activeInHierarchy) { Skip("disabled/inactive"); continue; }
                if (IsLightmapped(mr)) { Skip("lightmapped (combine would break lighting)"); continue; }

                var filter = mr.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) { Skip("no mesh"); continue; }
                if (mesh.uv == null || mesh.uv.Length != mesh.vertexCount) { Skip("no/invalid UV0"); continue; }

                if (!AllSubmeshesQualify(mr, mesh, out var reason, out var texturesForMesh))
                {
                    Skip(reason);
                    continue;
                }

                entries.Add(new Entry { Renderer = mr, Mesh = mesh });
                foreach (var t in texturesForMesh) mainTextures.Add(t);
            }

            if (entries.Count < 2)
            {
                ctx.AddWarning($"World combine: not enough qualifying static renderers to combine (found {entries.Count}). " +
                               LogSkips(skipReasons));
                return;
            }

            report(0.2f, $"Atlasing {mainTextures.Count} textures...");
            var atlas = TextureAtlasBuilder.Build(mainTextures, ctx.Options.atlasMaxSize, ctx.Options.atlasPadding);
            if (atlas.Atlas == null)
            {
                ctx.AddWarning("World combine: failed to build texture atlas.");
                return;
            }

            report(0.5f, "Combining meshes...");
            var combined = BuildCombinedMesh(entries, rootTransform, atlas, out int triangleCount);

            // Single combined material on the chosen shader.
            var shader = ctx.TargetShader != null ? ctx.TargetShader : Shader.Find("Standard");
            var material = new Material(shader) { name = "YUCP_Combined" };
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", atlas.Atlas);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", atlas.Atlas);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            var go = new GameObject("YUCP_Combined");
            go.transform.SetParent(rootTransform, false);
            go.AddComponent<MeshFilter>().sharedMesh = combined;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            // Disable the originals we folded in (they're fully represented by the combined mesh now).
            foreach (var e in entries)
                e.Renderer.enabled = false;

            ctx.GeneratedAssets.Add(combined);
            ctx.GeneratedAssets.Add(atlas.Atlas);
            ctx.GeneratedAssets.Add(material);

            report(1f, $"Combined {entries.Count} renderers into 1 ({triangleCount:N0} tris). {LogSkips(skipReasons)}");
            if (skipReasons.Count > 0)
                ctx.AddWarning($"World combine kept {SumSkips(skipReasons)} renderer(s) separate: {LogSkips(skipReasons)}");
        }

        private static bool AllSubmeshesQualify(MeshRenderer mr, Mesh mesh, out string reason, out List<Texture> textures)
        {
            reason = null;
            textures = new List<Texture>();
            var mats = mr.sharedMaterials;

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var mat = sub < mats.Length ? mats[sub] : null;
                if (mat == null) { reason = "missing material"; return false; }
                if (!mat.HasProperty("_MainTex")) { reason = "material has no _MainTex"; return false; }

                var tex = mat.GetTexture("_MainTex");
                if (tex == null) { reason = "no main texture"; return false; }

                var scale = mat.GetTextureScale("_MainTex");
                var offset = mat.GetTextureOffset("_MainTex");
                if (scale != Vector2.one || offset != Vector2.zero) { reason = "texture tiling/offset (would bleed in atlas)"; return false; }

                textures.Add(tex);
            }

            return mesh.subMeshCount > 0;
        }

        private static Mesh BuildCombinedMesh(List<Entry> entries, Transform root, AtlasResult atlas, out int triangleCount)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();

            foreach (var e in entries)
            {
                var mesh = e.Mesh;
                var mats = e.Renderer.sharedMaterials;

                var localToCombined = root.worldToLocalMatrix * e.Renderer.transform.localToWorldMatrix;
                var normalMatrix = localToCombined.inverse.transpose;

                var mVerts = mesh.vertices;
                var mNormals = mesh.normals;
                var mTangents = mesh.tangents;
                var mUv = mesh.uv;
                bool hasNormals = mNormals.Length == mVerts.Length;
                bool hasTangents = mTangents.Length == mVerts.Length;

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var mat = sub < mats.Length ? mats[sub] : null;
                    var tex = mat != null ? mat.GetTexture("_MainTex") : null;
                    Rect rect = (tex != null && atlas.Rects.TryGetValue(tex, out var r)) ? r : new Rect(0, 0, 1, 1);

                    var tris = mesh.GetTriangles(sub);
                    var remap = new Dictionary<int, int>();

                    foreach (var oldIndex in tris)
                    {
                        if (!remap.TryGetValue(oldIndex, out var newIndex))
                        {
                            newIndex = vertices.Count;
                            remap[oldIndex] = newIndex;

                            vertices.Add(localToCombined.MultiplyPoint3x4(mVerts[oldIndex]));
                            normals.Add(hasNormals ? normalMatrix.MultiplyVector(mNormals[oldIndex]).normalized : Vector3.up);
                            if (hasTangents)
                            {
                                var t = mTangents[oldIndex];
                                var dir = localToCombined.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                                tangents.Add(new Vector4(dir.x, dir.y, dir.z, t.w));
                            }
                            else
                            {
                                tangents.Add(new Vector4(1, 0, 0, 1));
                            }

                            var uv = mUv[oldIndex];
                            uvs.Add(new Vector2(rect.x + uv.x * rect.width, rect.y + uv.y * rect.height));
                        }
                        indices.Add(newIndex);
                    }
                }
            }

            var combined = new Mesh { name = "YUCP_CombinedMesh" };
            if (vertices.Count > 65535)
                combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            combined.SetVertices(vertices);
            combined.SetNormals(normals);
            combined.SetTangents(tangents);
            combined.SetUVs(0, uvs);
            combined.SetTriangles(indices, 0);
            combined.RecalculateBounds();

            triangleCount = indices.Count / 3;
            return combined;
        }

        private static bool IsExcluded(Transform t, List<Transform> exclusions)
        {
            if (exclusions == null || exclusions.Count == 0) return false;
            for (var cur = t; cur != null; cur = cur.parent)
                if (exclusions.Contains(cur))
                    return true;
            return false;
        }

        private static bool IsLightmapped(MeshRenderer mr)
        {
            // Valid lightmap indices are small; 0xFFFE/0xFFFF are Unity's "none" sentinels.
            int idx = mr.lightmapIndex;
            return idx >= 0 && idx < 65534;
        }

        private static int SumSkips(Dictionary<string, int> skips)
        {
            int total = 0;
            foreach (var kv in skips) total += kv.Value;
            return total;
        }

        private static string LogSkips(Dictionary<string, int> skips)
        {
            if (skips.Count == 0) return string.Empty;
            var parts = new List<string>();
            foreach (var kv in skips) parts.Add($"{kv.Value}× {kv.Key}");
            return "Skipped: " + string.Join("; ", parts) + ".";
        }
    }
}
