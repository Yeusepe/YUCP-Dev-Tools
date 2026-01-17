using System;

namespace YUCP.Motion.Core
{
    /// <summary>
    /// Authoring struct holding optional animation targets with a mask.
    /// Set by code or UXML parsing.
    /// </summary>
    public struct MotionTargets
    {
        public bool HasX;
        public float X;
        
        public bool HasY;
        public float Y;
        
        public bool HasScaleX;
        public float ScaleX;
        
        public bool HasScaleY;
        public float ScaleY;
        
        public bool HasRotateDeg;
        public float RotateDeg;
        
        public bool HasOpacity;
        public float Opacity;
        
        public bool HasBgRGBA;
        public ColorRGBA BgRGBA;
        
        /// <summary>
        /// Gets a mask indicating which properties are set.
        /// </summary>
        public DirtyMask GetDirtyMask()
        {
            DirtyMask mask = DirtyMask.None;
            
            if (HasX || HasY || HasScaleX || HasScaleY || HasRotateDeg)
                mask |= DirtyMask.Transform;
            
            if (HasOpacity || HasBgRGBA)
                mask |= DirtyMask.Paint;
            
            return mask;
        }
        
        public static MotionTargets Empty => default;
    }
}
