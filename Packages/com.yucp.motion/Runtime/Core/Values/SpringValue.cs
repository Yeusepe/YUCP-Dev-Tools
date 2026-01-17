using System;
using YUCP.Motion.Core.Animation;

namespace YUCP.Motion.Core
{
    /// <summary>
    /// Spring physics options.
    /// </summary>
    public struct SpringOptions
    {
        public float Stiffness;
        public float Damping;
        public float Mass;
        public float Velocity;
        public float RestSpeed;
        public float RestDelta;
        
        public static SpringOptions Default => new SpringOptions
        {
            Stiffness = 100.0f,
            Damping = 10.0f,
            Mass = 1.0f,
            Velocity = 0.0f,
            RestSpeed = 2.0f,
            RestDelta = 0.5f
        };
    }
    
    /// <summary>
    /// SpringValue wraps a MotionValue and applies spring physics as a passive effect.
    /// Similar to motion-main's springValue.
    /// </summary>
    public class SpringValue<T> where T : struct
    {
        private readonly MotionValue<T> m_Value;
        private readonly SpringOptions m_Options;
        private IAnimationPlaybackControls m_ActiveAnimation;
        
        /// <summary>
        /// Gets the underlying MotionValue.
        /// </summary>
        public MotionValue<T> Value => m_Value;
        
        /// <summary>
        /// Creates a SpringValue that animates to its latest value using spring physics.
        /// </summary>
        public SpringValue(T initialValue, SpringOptions? options = null)
        {
            m_Options = options ?? SpringOptions.Default;
            m_Value = new MotionValue<T>(initialValue);
            
            // Attach spring as passive effect
            m_Value.Attach(OnSet, StopAnimation);
        }
        
        /// <summary>
        /// Creates a SpringValue that tracks another MotionValue.
        /// </summary>
        public static SpringValue<T> Track(MotionValue<T> source, SpringOptions? options = null)
        {
            var springValue = new SpringValue<T>(source.Get(), options);
            
            // Subscribe to source changes
            source.OnChange(_ =>
            {
                springValue.m_Value.Set(source.Get());
            });
            
            return springValue;
        }
        
        /// <summary>
        /// Passive effect handler - starts spring animation when value is set.
        /// </summary>
        private void OnSet(T targetValue, Action<T> setter)
        {
            // Stop any existing animation
            StopAnimation();
            
            // Start spring animation (simplified - would integrate with animation system)
            // For now, just set the value directly
            // TODO: Integrate with Phase 3 animation engine
            setter(targetValue);
        }
        
        /// <summary>
        /// Stops the active spring animation.
        /// </summary>
        private void StopAnimation()
        {
            m_ActiveAnimation?.Stop();
            m_ActiveAnimation = null;
        }
        
        /// <summary>
        /// Gets the current value.
        /// </summary>
        public T Get() => m_Value.Get();
        
        /// <summary>
        /// Sets the target value (triggers spring animation).
        /// </summary>
        public void Set(T value) => m_Value.Set(value);
        
        /// <summary>
        /// Gets the velocity.
        /// </summary>
        public float GetVelocity() => m_Value.GetVelocity();
    }
}
