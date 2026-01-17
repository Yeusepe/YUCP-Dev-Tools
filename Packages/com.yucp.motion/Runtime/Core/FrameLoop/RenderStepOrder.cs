namespace YUCP.Motion.Core
{
    /// <summary>
    /// Render step order (matching motion-main's order).
    /// </summary>
    public enum RenderStepOrder
    {
        Setup,
        Read,
        ResolveKeyframes,
        PreUpdate,
        Update,
        PreRender,
        Render,
        PostRender
    }
}
