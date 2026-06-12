using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.Optimizer.World
{
    /// <summary>The atlas texture plus the [0,1] rect each source texture occupies within it.</summary>
    public class AtlasResult
    {
        public Texture2D Atlas;
        public readonly Dictionary<Texture, Rect> Rects = new Dictionary<Texture, Rect>();
    }

    /// <summary>
    /// Packs a set of (albedo) textures into a single atlas using Unity's <c>Texture2D.PackTextures</c>.
    /// Source textures are first copied to readable sRGB form so compressed/non-readable inputs work.
    /// </summary>
    public static class TextureAtlasBuilder
    {
        public static AtlasResult Build(IEnumerable<Texture> textures, int maxSize, int padding)
        {
            var unique = new List<Texture>();
            foreach (var t in textures)
                if (t != null && !unique.Contains(t))
                    unique.Add(t);

            var result = new AtlasResult();
            if (unique.Count == 0)
                return result;

            var readables = new Texture2D[unique.Count];
            for (int i = 0; i < unique.Count; i++)
                readables[i] = TextureUtil.ToReadable(unique[i], linear: false);

            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "YUCP_Atlas" };
            Rect[] rects = atlas.PackTextures(readables, Mathf.Max(0, padding), Mathf.Max(64, maxSize));
            atlas.Apply(true);

            for (int i = 0; i < unique.Count; i++)
                result.Rects[unique[i]] = rects[i];

            // The readable copies were only needed for packing; the atlas now owns the pixels.
            foreach (var r in readables)
                Object.DestroyImmediate(r);

            result.Atlas = atlas;
            return result;
        }
    }
}
