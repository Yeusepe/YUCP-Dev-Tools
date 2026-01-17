using System;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// UseMotionValue controller - creates and manages a MotionValue.
    /// Similar to motion-main's useMotionValue hook.
    /// </summary>
    public class UseMotionValue<T> : IDisposable
    {
        private readonly MotionValue<T> m_Value;
        private bool m_Disposed;
        
        /// <summary>
        /// Gets the MotionValue.
        /// </summary>
        public MotionValue<T> Value => m_Value;
        
        /// <summary>
        /// Creates a new UseMotionValue controller.
        /// </summary>
        public UseMotionValue(T initialValue)
        {
            m_Value = new MotionValue<T>(initialValue);
        }
        
        /// <summary>
        /// Gets the current value.
        /// </summary>
        public T Get() => m_Value.Get();
        
        /// <summary>
        /// Sets the value.
        /// </summary>
        public void Set(T value) => m_Value.Set(value);
        
        /// <summary>
        /// Jumps to the value (stops animations).
        /// </summary>
        public void Jump(T value) => m_Value.Jump(value);
        
        /// <summary>
        /// Gets the velocity.
        /// </summary>
        public float GetVelocity() => m_Value.GetVelocity();
        
        public void Dispose()
        {
            if (m_Disposed)
                return;
            
            m_Disposed = true;
            m_Value.Destroy();
        }
    }
}
