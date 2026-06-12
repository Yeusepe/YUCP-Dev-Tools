using UnityEngine;

namespace YUCP.DevTools.Editor.Optimizer.World
{
    /// <summary>Texture helpers for atlasing — produces readable, color-space-correct CPU copies.</summary>
    public static class TextureUtil
    {
        /// <summary>
        /// Returns a readable RGBA32 copy of <paramref name="source"/> via a RenderTexture blit, so even
        /// non-readable / compressed source textures can be packed. <paramref name="linear"/> must be true
        /// for data maps (normal/metallic/mask) and false for color (albedo/emission) to avoid gamma shifts.
        /// </summary>
        public static Texture2D ToReadable(Texture source, bool linear)
        {
            int w = Mathf.Max(1, source.width);
            int h = Mathf.Max(1, source.height);

            var rt = RenderTexture.GetTemporary(
                w, h, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);

            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            var readable = new Texture2D(w, h, TextureFormat.RGBA32, true, linear);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply(true);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return readable;
        }
    }
}
