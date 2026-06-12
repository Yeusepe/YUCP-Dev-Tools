using System;

namespace YUCP.DevTools.Editor.Optimizer
{
    /// <summary>
    /// A single ordered stage of the optimization pipeline. Passes are run synchronously on the main
    /// thread by <see cref="OptimizerRunner"/>; only passes whose <see cref="CanRun"/> returns true execute.
    /// </summary>
    public interface IOptimizationPass
    {
        /// <summary>Short human-readable name shown in the progress UI.</summary>
        string Name { get; }

        /// <summary>Whether this pass applies to the current context (e.g. mode/option gating).</summary>
        bool CanRun(OptimizationContext ctx);

        /// <summary>
        /// Run the pass. <paramref name="report"/> takes a local 0..1 fraction and a status message;
        /// the runner maps it into overall progress.
        /// </summary>
        void Execute(OptimizationContext ctx, Action<float, string> report);
    }
}
