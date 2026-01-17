using System;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// UseSpring controller - returns a SpringValue tied to a MotionValue.
    /// Similar to motion-main's useSpring hook.
    /// </summary>
    public class UseSpring<T> : IDisposable where T : struct
    {
        private readonly SpringValue<T> m_SpringValue;
        private bool m_Disposed;
        
        /// <summary>
        /// Gets the SpringValue.
        /// </summary>
        public SpringValue<T> SpringValue => m_SpringValue;
        
        /// <summary>
        /// Gets the underlying MotionValue.
        /// </summary>
        public MotionValue<T> Value => m_SpringValue.Value;
        
        /// <summary>
        /// Creates a new UseSpring controller.
        /// </summary>
        public UseSpring(T initialValue, SpringOptions? options = null)
        {
            m_SpringValue = new SpringValue<T>(initialValue, options);
        }
        
        /// <summary>
        /// Creates a UseSpring that tracks another MotionValue.
        /// </summary>
        public UseSpring(MotionValue<T> source, SpringOptions? options = null)
        {
            m_SpringValue = SpringValue<T>.Track(source, options);
        }
        
        /// <summary>
        /// Gets the current value.
        /// </summary>
        public T Get() => m_SpringValue.Get();
        
        /// <summary>
        /// Sets the target value (triggers spring animation).
        /// </summary>
        public void Set(T value) => m_SpringValue.Set(value);
        
        /// <summary>
        /// Gets the velocity.
        /// </summary>
        public float GetVelocity() => m_SpringValue.GetVelocity();
        
        public void Dispose()
        {
            if (m_Disposed)
                return;
            
            m_Disposed = true;
            // SpringValue cleanup would be handled by its MotionValue
        }
    }
}
