using System.Collections.Generic;
using YUCP.DevTools.Editor.Optimizer.Passes;

namespace YUCP.DevTools.Editor.Optimizer
{
    /// <summary>
    /// Builds the ordered pass list for a run. Passes self-gate via <see cref="IOptimizationPass.CanRun"/>,
    /// so avatar-only and world-only stages can coexist in one list.
    /// </summary>
    public static class OptimizerPipeline
    {
        public static List<IOptimizationPass> BuildPasses(OptimizationContext ctx)
        {
            return new List<IOptimizationPass>
            {
                new SelectionPass(),
                new PrepareCopyPass(),
                new ShaderConversionPass(),   // Avatar + World (gated by options.convertShaders)
                new D4rkDelegationPass(),     // Avatar
                new CustomStaticMergePass(),  // World
                new CollectAfterStatsPass(),
                new FinalizeOutputPass(),
            };
        }
    }
}
