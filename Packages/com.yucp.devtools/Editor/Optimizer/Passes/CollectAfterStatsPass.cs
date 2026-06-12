using System;

namespace YUCP.DevTools.Editor.Optimizer.Passes
{
    /// <summary>Snapshots the optimized copy's footprint so the UI can report before/after deltas.</summary>
    public class CollectAfterStatsPass : IOptimizationPass
    {
        public string Name => "Measuring result";

        public bool CanRun(OptimizationContext ctx) => ctx.Root != null;

        public void Execute(OptimizationContext ctx, Action<float, string> report)
        {
            ctx.After = OptimizerStatsCollector.Collect(ctx.Root);
            report(1f, $"Result: {ctx.After}");
        }
    }
}
