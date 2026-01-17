using System;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// UseTransform controller - derived value mapping.
    /// Similar to motion-main's useTransform hook.
    /// </summary>
    public class UseTransform<TInput, TOutput> : IDisposable
    {
        private readonly MotionValue<TOutput> m_OutputValue;
        private readonly Func<TInput, TOutput> m_Transform;
        private readonly MotionValue<TInput> m_InputValue;
        private System.Func<bool> m_Unsubscribe;
        private bool m_Disposed;
        
        /// <summary>
        /// Gets the output MotionValue.
        /// </summary>
        public MotionValue<TOutput> Value => m_OutputValue;
        
        /// <summary>
        /// Creates a new UseTransform controller.
        /// </summary>
        public UseTransform(MotionValue<TInput> input, Func<TInput, TOutput> transform)
        {
            m_InputValue = input ?? throw new ArgumentNullException(nameof(input));
            m_Transform = transform ?? throw new ArgumentNullException(nameof(transform));
            
            // Create derived value
            m_OutputValue = DerivedMotionValue.Create(
                () => m_Transform(m_InputValue.Get()),
                m_InputValue
            );
        }
        
        /// <summary>
        /// Gets the current transformed value.
        /// </summary>
        public TOutput Get() => m_OutputValue.Get();
        
        public void Dispose()
        {
            if (m_Disposed)
                return;
            
            m_Disposed = true;
            m_Unsubscribe?.Invoke();
            m_OutputValue.Destroy();
        }
    }
}
