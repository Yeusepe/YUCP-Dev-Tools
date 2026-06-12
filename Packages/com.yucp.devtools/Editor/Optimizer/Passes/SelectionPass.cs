using System;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using YUCP.DevTools.Editor.Optimizer.Util;

namespace YUCP.DevTools.Editor.Optimizer.Passes
{
    /// <summary>
    /// First pass: records the original hierarchy, auto-detects avatar vs world mode, gathers in-scope
    /// renderers, snapshots "before" footprint stats, and converts the exclusion list to root-relative
    /// paths so it survives the clone step.
    /// </summary>
    public class SelectionPass : IOptimizationPass
    {
        public string Name => "Analyzing selection";

        public bool CanRun(OptimizationContext ctx) => ctx.Root != null;

        public void Execute(OptimizationContext ctx, Action<float, string> report)
        {
            ctx.OriginalRoot = ctx.Root;

            // Avatar mode only if the descriptor is within the selected hierarchy, so the working copy
            // includes it and d4rkAvatarOptimizer can run. (A descriptor on a parent doesn't count —
            // the user should select the avatar root.)
            bool isAvatar = ctx.Root.GetComponentInChildren<VRCAvatarDescriptor>(true) != null;
            ctx.Mode = isAvatar ? OptimizerMode.Avatar : OptimizerMode.World;

            report(0.4f, "Collecting renderers...");
            GatherRenderers(ctx);

            // Persist exclusions as paths, then clear the original-hierarchy transform references.
            ctx.ExclusionPaths.Clear();
            foreach (var t in ctx.Exclusions)
            {
                if (t == null) continue;
                ctx.ExclusionPaths.Add(HierarchyUtil.GetRelativePath(ctx.OriginalRoot.transform, t));
            }
            ctx.Exclusions.Clear();

            report(0.7f, "Measuring footprint...");
            ctx.Before = OptimizerStatsCollector.Collect(ctx.Root);

            report(1f, $"{ctx.Mode} mode — {ctx.Before}");
        }

        private static void GatherRenderers(OptimizationContext ctx)
        {
            ctx.Renderers.Clear();
            foreach (var renderer in ctx.Root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;
                ctx.Renderers.Add(renderer);
            }
        }
    }
}
