using System;
using UnityEngine;
using YUCP.DevTools.Editor.Optimizer.Util;

namespace YUCP.DevTools.Editor.Optimizer.Passes
{
    /// <summary>
    /// Avatar branch: delegates the proven mesh/material/atlas merge to d4rkAvatarOptimizer (which is
    /// engineered to preserve exact appearance) by configuring it from the optimizer options and invoking
    /// its in-place <c>Optimize()</c> on the working copy.
    /// </summary>
    public class D4rkDelegationPass : IOptimizationPass
    {
        public string Name => "Merging with d4rkAvatarOptimizer";

        public bool CanRun(OptimizationContext ctx) => ctx.Mode == OptimizerMode.Avatar;

        public void Execute(OptimizationContext ctx, Action<float, string> report)
        {
            if (!D4rkReflection.IsInstalled)
            {
                ctx.AddWarning(
                    "d4rkAvatarOptimizer is not installed, so avatar mesh/material merging was skipped. " +
                    "Install it to enable avatar optimization. The unoptimized copy was kept.");
                return;
            }

            report(0.1f, "Configuring optimizer...");
            var optimizer = D4rkReflection.AddAndConfigure(ctx.Root, ctx.Options, ctx.Exclusions);
            if (optimizer == null)
            {
                ctx.AddWarning("Failed to attach d4rkAvatarOptimizer; avatar merge was skipped.");
                return;
            }

            report(0.3f, "Running d4rkAvatarOptimizer (this can take a while)...");
            D4rkReflection.Optimize(optimizer);

            report(1f, "Avatar merge complete");
        }
    }
}
