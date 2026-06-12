using System;
using YUCP.DevTools.Editor.Optimizer.Output;

namespace YUCP.DevTools.Editor.Optimizer.Passes
{
    /// <summary>Final pass: writes the optimized copy out per the selected <see cref="OptimizerOutputMode"/>.</summary>
    public class FinalizeOutputPass : IOptimizationPass
    {
        public string Name => "Finalizing output";

        public bool CanRun(OptimizationContext ctx) => ctx.Root != null;

        public void Execute(OptimizationContext ctx, Action<float, string> report)
        {
            var mode = ctx.Options.outputMode;

            if (mode == OptimizerOutputMode.SaveAsPrefab || mode == OptimizerOutputMode.Both)
            {
                report(0.3f, "Saving prefab...");
                var path = OptimizerOutputWriter.SaveAsPrefab(ctx);
                if (!string.IsNullOrEmpty(path))
                    UnityEngine.Debug.Log($"[YUCP Optimizer] Saved optimized prefab: {path}");
            }

            // Always leave the optimized copy in the scene for InSceneCopy/Both, and as a fallback for
            // SaveAsPrefab until prefab export lands.
            report(0.8f, "Placing optimized copy...");
            OptimizerOutputWriter.FinalizeInScene(ctx);

            report(1f, "Done");
        }
    }
}
