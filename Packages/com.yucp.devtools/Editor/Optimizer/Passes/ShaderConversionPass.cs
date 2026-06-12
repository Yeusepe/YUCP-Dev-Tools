using System;
using System.Collections.Generic;
using UnityEngine;
using YUCP.DevTools.Editor.Optimizer.ShaderConversion;

namespace YUCP.DevTools.Editor.Optimizer.Passes
{
    /// <summary>
    /// Converts every in-scope material onto a single chosen shader before merging. Runs in both avatar and
    /// world modes. Uses the map-or-skip policy from <see cref="ShaderConverter"/>: materials whose textures
    /// can't be represented on the target are left untouched (and excluded from later same-shader merging),
    /// with a warning surfaced to the user.
    /// </summary>
    public class ShaderConversionPass : IOptimizationPass
    {
        public string Name => "Converting shaders";

        public bool CanRun(OptimizationContext ctx) => ctx.Options.convertShaders;

        public void Execute(OptimizationContext ctx, Action<float, string> report)
        {
            if (ctx.TargetShader == null)
            {
                ctx.AddWarning($"Shader conversion was requested but the target shader '{ctx.Options.targetShaderName}' " +
                               "could not be found in the project. Skipped conversion.");
                return;
            }

            var cache = new Dictionary<Material, Material>();
            var emittedWarnings = new HashSet<string>();
            int converted = 0, skipped = 0;

            for (int r = 0; r < ctx.Renderers.Count; r++)
            {
                var renderer = ctx.Renderers[r];
                if (renderer == null) continue;

                var shared = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < shared.Length; i++)
                {
                    var original = shared[i];
                    if (original == null) continue;

                    if (!cache.TryGetValue(original, out var mapped))
                    {
                        var result = ShaderConverter.Convert(original, ctx.TargetShader);
                        mapped = result.Material;
                        cache[original] = mapped;

                        if (result.Converted)
                        {
                            converted++;
                            ctx.GeneratedAssets.Add(mapped);
                        }
                        else if (original.shader != ctx.TargetShader)
                        {
                            skipped++;
                        }

                        foreach (var w in result.Warnings)
                            if (emittedWarnings.Add(w))
                                ctx.AddWarning(w);
                    }

                    if (mapped != original)
                    {
                        shared[i] = mapped;
                        changed = true;
                    }
                }

                if (changed)
                    renderer.sharedMaterials = shared;

                report((r + 1f) / ctx.Renderers.Count, $"Converting materials ({converted} done, {skipped} kept)...");
            }

            report(1f, $"Shader conversion: {converted} converted, {skipped} kept as-is");
        }
    }
}
