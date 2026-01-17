using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Drag gesture options.
    /// </summary>
    public struct DragGestureOptions
    {
        /// <summary>
        /// Lock drag to X axis only.
        /// </summary>
        public bool LockX;
        
        /// <summary>
        /// Lock drag to Y axis only.
        /// </summary>
        public bool LockY;
        
        /// <summary>
        /// Constraints for X axis (min, max in pixels).
        /// </summary>
        public Axis? XConstraints;
        
        /// <summary>
        /// Constraints for Y axis (min, max in pixels).
        /// </summary>
        public Axis? YConstraints;
        
        /// <summary>
        /// Elastic edges factor (0-1, or false to disable).
        /// </summary>
        public float? Elastic;
        
        /// <summary>
        /// Enable momentum (inertia) after drag ends.
        /// </summary>
        public bool Momentum;
        
        /// <summary>
        /// Power factor for momentum (0-1).
        /// </summary>
        public float MomentumPower;
        
        /// <summary>
        /// Time constant for momentum decay.
        /// </summary>
        public float MomentumTimeConstant;
        
        public static DragGestureOptions Default => new DragGestureOptions
        {
            LockX = false,
            LockY = false,
            Elastic = 0.35f,
            Momentum = false,
            MomentumPower = 0.8f,
            MomentumTimeConstant = 325.0f
        };
    }
}
