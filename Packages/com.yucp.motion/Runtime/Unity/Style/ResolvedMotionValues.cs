using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Cached resolved motion values. Used by adapters to avoid redundant style writes.
    /// </summary>
    public struct ResolvedMotionValues
    {
        public TransformState Transform;
        public ColorRGBA BackgroundColor;
        public float Opacity;
        
        public DirtyMask DirtyMask;
        
        public ResolvedMotionValues(TransformState transform, ColorRGBA backgroundColor, float opacity, DirtyMask dirtyMask)
        {
            Transform = transform;
            BackgroundColor = backgroundColor;
            Opacity = opacity;
            DirtyMask = dirtyMask;
        }
    }
}
