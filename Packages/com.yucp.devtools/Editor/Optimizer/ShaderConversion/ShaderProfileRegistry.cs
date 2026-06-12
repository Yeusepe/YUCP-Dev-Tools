using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace YUCP.DevTools.Editor.Optimizer.ShaderConversion
{
    /// <summary>
    /// Holds the built-in shader profiles plus any user-supplied <see cref="ShaderConversionMapAsset"/>s,
    /// and resolves which profile applies to a given shader. User assets take precedence over built-ins.
    /// </summary>
    public static class ShaderProfileRegistry
    {
        private static List<ShaderProfile> _userProfiles;

        private static SlotBinding T(CanonicalSlot s, string p) => new SlotBinding(s, p, SlotKind.Texture);
        private static SlotBinding C(CanonicalSlot s, string p) => new SlotBinding(s, p, SlotKind.Color);
        private static SlotBinding F(CanonicalSlot s, string p) => new SlotBinding(s, p, SlotKind.Float);

        private static readonly List<ShaderProfile> Builtins = new List<ShaderProfile>
        {
            new ShaderProfile("Standard", "Standard", new[] { "Standard" }, new List<SlotBinding>
            {
                T(CanonicalSlot.MainTex, "_MainTex"), C(CanonicalSlot.MainColor, "_Color"),
                T(CanonicalSlot.NormalMap, "_BumpMap"), F(CanonicalSlot.Metallic, "_Metallic"),
                F(CanonicalSlot.Smoothness, "_Glossiness"), T(CanonicalSlot.Occlusion, "_OcclusionMap"),
                T(CanonicalSlot.EmissionMap, "_EmissionMap"), C(CanonicalSlot.EmissionColor, "_EmissionColor"),
                F(CanonicalSlot.AlphaCutoff, "_Cutoff"),
            }),
            new ShaderProfile("URP Lit", "Universal Render Pipeline/Lit",
                new[] { "Universal Render Pipeline/Lit", "URP/Lit" }, new List<SlotBinding>
            {
                T(CanonicalSlot.MainTex, "_BaseMap"), C(CanonicalSlot.MainColor, "_BaseColor"),
                T(CanonicalSlot.NormalMap, "_BumpMap"), F(CanonicalSlot.Metallic, "_Metallic"),
                F(CanonicalSlot.Smoothness, "_Smoothness"), T(CanonicalSlot.Occlusion, "_OcclusionMap"),
                T(CanonicalSlot.EmissionMap, "_EmissionMap"), C(CanonicalSlot.EmissionColor, "_EmissionColor"),
                F(CanonicalSlot.AlphaCutoff, "_Cutoff"),
            }),
            new ShaderProfile("lilToon", "lilToon", new[] { "lilToon", "lts", "_lil" }, new List<SlotBinding>
            {
                T(CanonicalSlot.MainTex, "_MainTex"), C(CanonicalSlot.MainColor, "_Color"),
                T(CanonicalSlot.NormalMap, "_BumpMap"), T(CanonicalSlot.EmissionMap, "_EmissionMap"),
                C(CanonicalSlot.EmissionColor, "_EmissionColor"), F(CanonicalSlot.AlphaCutoff, "_Cutoff"),
            }),
            new ShaderProfile("Poiyomi Toon", ".poiyomi/Poiyomi Toon", new[] { "poiyomi" }, new List<SlotBinding>
            {
                T(CanonicalSlot.MainTex, "_MainTex"), C(CanonicalSlot.MainColor, "_Color"),
                T(CanonicalSlot.NormalMap, "_BumpMap"), T(CanonicalSlot.EmissionMap, "_EmissionMap"),
                C(CanonicalSlot.EmissionColor, "_EmissionColor"), F(CanonicalSlot.AlphaCutoff, "_Cutoff"),
            }),
            new ShaderProfile("VRChat Mobile Standard Lite", "VRChat/Mobile/Standard Lite",
                new[] { "VRChat/Mobile/Standard Lite" }, new List<SlotBinding>
            {
                T(CanonicalSlot.MainTex, "_MainTex"), C(CanonicalSlot.MainColor, "_Color"),
                T(CanonicalSlot.NormalMap, "_BumpMap"), F(CanonicalSlot.Metallic, "_Metallic"),
                F(CanonicalSlot.Smoothness, "_Glossiness"),
            }),
            new ShaderProfile("VRChat Mobile Toon Lit", "VRChat/Mobile/Toon Lit",
                new[] { "VRChat/Mobile/Toon Lit" }, new List<SlotBinding>
            {
                // Toon Lit only exposes _MainTex (no color tint, normals, etc.).
                T(CanonicalSlot.MainTex, "_MainTex"),
            }),
        };

        private static IEnumerable<ShaderProfile> UserProfiles()
        {
            if (_userProfiles == null)
            {
                _userProfiles = new List<ShaderProfile>();
                foreach (var guid in AssetDatabase.FindAssets("t:ShaderConversionMapAsset"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadAssetAtPath<ShaderConversionMapAsset>(path);
                    if (asset != null)
                        _userProfiles.Add(asset.ToProfile());
                }
            }
            return _userProfiles;
        }

        /// <summary>Clears the cached user profiles (call after a user adds/edits a map asset).</summary>
        public static void InvalidateCache() => _userProfiles = null;

        /// <summary>All profiles eligible to be a conversion target (user first, then built-in).</summary>
        public static List<ShaderProfile> TargetProfiles()
        {
            return UserProfiles().Concat(Builtins)
                .Where(p => !string.IsNullOrEmpty(p.TargetShaderName))
                .ToList();
        }

        /// <summary>Finds the profile that best matches the given shader name, or null.</summary>
        public static ShaderProfile Match(string shaderName)
        {
            return UserProfiles().FirstOrDefault(p => p.Matches(shaderName))
                   ?? Builtins.FirstOrDefault(p => p.Matches(shaderName));
        }
    }
}
