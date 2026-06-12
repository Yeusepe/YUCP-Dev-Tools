using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.Optimizer.ShaderConversion
{
    public class ShaderConversionResult
    {
        /// <summary>The material to use going forward: a newly converted material, or the original if kept.</summary>
        public Material Material;

        /// <summary>True if a new converted material was produced; false if the original was kept (skipped).</summary>
        public bool Converted;

        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Converts a material onto a target shader using canonical-slot profiles. Follows a strict
    /// "map-or-skip" policy: if the source has assigned textures the target cannot represent, the
    /// original material is kept and a warning is emitted — appearance is never silently degraded.
    /// </summary>
    public static class ShaderConverter
    {
        public static ShaderConversionResult Convert(Material src, Shader targetShader)
        {
            var result = new ShaderConversionResult();
            if (src == null || targetShader == null)
            {
                result.Material = src;
                return result;
            }

            if (src.shader == targetShader)
            {
                result.Material = src; // already the target shader
                return result;
            }

            var srcProfile = ShaderProfileRegistry.Match(src.shader.name);
            var tgtProfile = ShaderProfileRegistry.Match(targetShader.name);

            if (srcProfile == null)
            {
                result.Material = src;
                result.Warnings.Add($"No conversion profile for source shader '{src.shader.name}' (material '{src.name}'). Kept original.");
                return result;
            }
            if (tgtProfile == null)
            {
                result.Material = src;
                result.Warnings.Add($"No conversion profile for target shader '{targetShader.name}'. Kept material '{src.name}' unchanged.");
                return result;
            }

            // Significant-unmapped detection: any assigned source texture the target can't carry → skip.
            var unmapped = FindUnmappableTextures(src, srcProfile, tgtProfile);
            if (unmapped.Count > 0)
            {
                result.Material = src;
                result.Warnings.Add($"Material '{src.name}' has texture(s) the target shader can't represent: " +
                                    $"{string.Join(", ", unmapped)}. Kept original to preserve appearance.");
                return result;
            }

            // Build the converted material.
            var mat = new Material(targetShader) { name = src.name + " (YUCP)" };
            mat.renderQueue = src.renderQueue;
            bool emissive = false;

            foreach (CanonicalSlot slot in Enum.GetValues(typeof(CanonicalSlot)))
            {
                var sb = srcProfile.Get(slot);
                var tb = tgtProfile.Get(slot);
                if (sb == null || tb == null || sb.Kind != tb.Kind)
                    continue;
                if (!src.HasProperty(sb.Property) || !mat.HasProperty(tb.Property))
                    continue;

                switch (tb.Kind)
                {
                    case SlotKind.Texture:
                        var tex = src.GetTexture(sb.Property);
                        if (tex != null)
                        {
                            mat.SetTexture(tb.Property, tex);
                            mat.SetTextureScale(tb.Property, src.GetTextureScale(sb.Property));
                            mat.SetTextureOffset(tb.Property, src.GetTextureOffset(sb.Property));
                            if (slot == CanonicalSlot.EmissionMap) emissive = true;
                        }
                        break;
                    case SlotKind.Color:
                        var col = src.GetColor(sb.Property);
                        mat.SetColor(tb.Property, col);
                        if (slot == CanonicalSlot.EmissionColor && col.maxColorComponent > 0f) emissive = true;
                        break;
                    case SlotKind.Float:
                        mat.SetFloat(tb.Property, src.GetFloat(sb.Property));
                        break;
                }
            }

            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            result.Material = mat;
            result.Converted = true;
            return result;
        }

        private static List<string> FindUnmappableTextures(Material src, ShaderProfile srcProfile, ShaderProfile tgtProfile)
        {
            var unmapped = new List<string>();
            var known = srcProfile.KnownProperties();
            var shader = src.shader;

            // Assigned textures the source profile doesn't even know about.
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                    continue;
                string prop = ShaderUtil.GetPropertyName(shader, i);
                if (known.Contains(prop)) continue;
                if (src.HasProperty(prop) && src.GetTexture(prop) != null)
                    unmapped.Add(prop);
            }

            // Known source textures that are assigned but have no matching target slot.
            foreach (var binding in srcProfile.Bindings)
            {
                if (binding.Kind != SlotKind.Texture) continue;
                if (!src.HasProperty(binding.Property) || src.GetTexture(binding.Property) == null) continue;
                var tb = tgtProfile.Get(binding.Slot);
                if (tb == null || tb.Kind != SlotKind.Texture)
                    unmapped.Add($"{binding.Property} ({binding.Slot})");
            }

            return unmapped;
        }
    }
}
