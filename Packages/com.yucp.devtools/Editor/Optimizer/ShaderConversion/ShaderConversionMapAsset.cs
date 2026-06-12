using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.Optimizer.ShaderConversion
{
    /// <summary>
    /// Serializable form of a <see cref="ShaderProfile"/> so users can add conversion support for custom
    /// shaders without recompiling. Discovered automatically by <see cref="ShaderProfileRegistry"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "ShaderConversionMap", menuName = "YUCP/Optimizer/Shader Conversion Map")]
    public class ShaderConversionMapAsset : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public CanonicalSlot slot;
            public string property;
            public SlotKind kind;
        }

        [Tooltip("Friendly name for this shader profile.")]
        public string displayName;

        [Tooltip("Exact shader name (Shader.Find) used when this profile is the conversion target.")]
        public string targetShaderName;

        [Tooltip("Case-insensitive substrings matched against a material's shader name to select this profile.")]
        public string[] shaderNameMatches;

        public List<Entry> bindings = new List<Entry>();

        public ShaderProfile ToProfile()
        {
            var list = new List<SlotBinding>();
            foreach (var e in bindings)
                list.Add(new SlotBinding(e.slot, e.property, e.kind));
            return new ShaderProfile(displayName, targetShaderName, shaderNameMatches, list);
        }
    }
}
