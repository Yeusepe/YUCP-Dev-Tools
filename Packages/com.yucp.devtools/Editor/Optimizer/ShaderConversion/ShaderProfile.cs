using System.Collections.Generic;
using System.Linq;

namespace YUCP.DevTools.Editor.Optimizer.ShaderConversion
{
    /// <summary>Semantic material channels used to translate properties between different shaders.</summary>
    public enum CanonicalSlot
    {
        MainTex,
        MainColor,
        NormalMap,
        Metallic,
        Smoothness,
        Occlusion,
        EmissionMap,
        EmissionColor,
        AlphaCutoff,
    }

    public enum SlotKind
    {
        Texture,
        Color,
        Float,
    }

    /// <summary>Binds a canonical slot to a concrete shader property name + type for one shader.</summary>
    public class SlotBinding
    {
        public CanonicalSlot Slot;
        public string Property;
        public SlotKind Kind;

        public SlotBinding(CanonicalSlot slot, string property, SlotKind kind)
        {
            Slot = slot;
            Property = property;
            Kind = kind;
        }
    }

    /// <summary>
    /// Describes how a given shader exposes the canonical slots. Used as the "source" profile to read a
    /// material's meaningful properties and as the "target" profile to write them onto a new material.
    /// </summary>
    public class ShaderProfile
    {
        /// <summary>Human-readable name shown in the UI.</summary>
        public string DisplayName;

        /// <summary>Exact shader name passed to <c>Shader.Find</c> when this profile is used as a target.</summary>
        public string TargetShaderName;

        /// <summary>Case-insensitive substrings matched against a material's shader name to pick this profile.</summary>
        public string[] ShaderNameMatches;

        public List<SlotBinding> Bindings = new List<SlotBinding>();

        public ShaderProfile() { }

        public ShaderProfile(string displayName, string targetShaderName, string[] matches, List<SlotBinding> bindings)
        {
            DisplayName = displayName;
            TargetShaderName = targetShaderName;
            ShaderNameMatches = matches;
            Bindings = bindings ?? new List<SlotBinding>();
        }

        public SlotBinding Get(CanonicalSlot slot) => Bindings.FirstOrDefault(b => b.Slot == slot);

        /// <summary>The set of shader property names this profile knows about (used to detect unmapped textures).</summary>
        public HashSet<string> KnownProperties() => new HashSet<string>(Bindings.Select(b => b.Property));

        public bool Matches(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName) || ShaderNameMatches == null)
                return false;
            foreach (var m in ShaderNameMatches)
            {
                if (!string.IsNullOrEmpty(m) && shaderName.IndexOf(m, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
