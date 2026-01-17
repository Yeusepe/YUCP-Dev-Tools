namespace YUCP.Motion.Core.Animation
{
    /// <summary>
    /// Animation state returned by generators.
    /// </summary>
    public struct AnimationState<T>
    {
        public bool Done;
        public T Value;
    }
    
    /// <summary>
    /// Keyframe generator interface.
    /// </summary>
    public interface IKeyframeGenerator<T>
    {
        /// <summary>
        /// Calculated duration in milliseconds, or null if dynamic.
        /// </summary>
        float? CalculatedDuration { get; }
        
        /// <summary>
        /// Gets the next animation state at time t (in milliseconds).
        /// </summary>
        AnimationState<T> Next(float t);
    }
    
    /// <summary>
    /// Animation options for value animations.
    /// </summary>
    public struct ValueAnimationOptions<T>
    {
        public float Duration;
        public T[] Keyframes;
        public float[] Times; // Offsets 0-1
        public EasingType Easing;
        public float Velocity;
    }
}
