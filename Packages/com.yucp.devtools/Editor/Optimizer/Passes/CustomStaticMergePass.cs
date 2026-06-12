using System;
using YUCP.DevTools.Editor.Optimizer.World;

namespace YUCP.DevTools.Editor.Optimizer.Passes
{
    /// <summary>
    /// World branch: combines qualifying static MeshRenderers into a single atlased mesh + material via
    /// <see cref="StaticMeshCombiner"/>. Non-combinable renderers (lightmapped, tiled, etc.) are kept and reported.
    /// </summary>
    public class CustomStaticMergePass : IOptimizationPass
    {
        public string Name => "Combining static meshes";

        public bool CanRun(OptimizationContext ctx) => ctx.Mode == OptimizerMode.World;

        public void Execute(OptimizationContext ctx, Action<float, string> report)
        {
            StaticMeshCombiner.Combine(ctx, report);
        }
    }
}
