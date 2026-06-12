namespace YUCP.DevTools.Editor.Optimizer
{
    /// <summary>
    /// The kind of hierarchy the optimizer is operating on. Auto-detected by
    /// <see cref="Passes.SelectionPass"/> based on the presence of a VRChat avatar descriptor.
    /// </summary>
    public enum OptimizerMode
    {
        /// <summary>VRChat avatar (has a VRCAvatarDescriptor). Merge/atlas is delegated to d4rkAvatarOptimizer.</summary>
        Avatar,

        /// <summary>Generic GameObject hierarchy / VRChat world (static MeshRenderers). Uses the custom static pipeline.</summary>
        World,
    }
}
