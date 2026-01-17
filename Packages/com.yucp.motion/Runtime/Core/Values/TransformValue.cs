using System;
using System.Collections.Generic;

namespace YUCP.Motion.Core
{
    /// <summary>
    /// TransformValue composes multiple MotionValues into a transform state.
    /// Similar to motion-main's transform composition.
    /// </summary>
    public class TransformValue
    {
        private readonly MotionValue<float> m_X;
        private readonly MotionValue<float> m_Y;
        private readonly MotionValue<float> m_ScaleX;
        private readonly MotionValue<float> m_ScaleY;
        private readonly MotionValue<float> m_RotateDeg;
        
        /// <summary>
        /// X translation value.
        /// </summary>
        public MotionValue<float> X => m_X;
        
        /// <summary>
        /// Y translation value.
        /// </summary>
        public MotionValue<float> Y => m_Y;
        
        /// <summary>
        /// X scale value.
        /// </summary>
        public MotionValue<float> ScaleX => m_ScaleX;
        
        /// <summary>
        /// Y scale value.
        /// </summary>
        public MotionValue<float> ScaleY => m_ScaleY;
        
        /// <summary>
        /// Rotation value in degrees.
        /// </summary>
        public MotionValue<float> RotateDeg => m_RotateDeg;
        
        /// <summary>
        /// Creates a new TransformValue with initial values.
        /// </summary>
        public TransformValue(
            float x = 0.0f,
            float y = 0.0f,
            float scaleX = 1.0f,
            float scaleY = 1.0f,
            float rotateDeg = 0.0f)
        {
            m_X = new MotionValue<float>(x);
            m_Y = new MotionValue<float>(y);
            m_ScaleX = new MotionValue<float>(scaleX);
            m_ScaleY = new MotionValue<float>(scaleY);
            m_RotateDeg = new MotionValue<float>(rotateDeg);
        }
        
        /// <summary>
        /// Gets the current transform state.
        /// </summary>
        public TransformState GetTransformState()
        {
            return new TransformState
            {
                X = m_X.Get(),
                Y = m_Y.Get(),
                ScaleX = m_ScaleX.Get(),
                ScaleY = m_ScaleY.Get(),
                RotateDeg = m_RotateDeg.Get()
            };
        }
        
        /// <summary>
        /// Sets all transform values at once.
        /// </summary>
        public void SetTransform(TransformState transform)
        {
            m_X.Set(transform.X);
            m_Y.Set(transform.Y);
            m_ScaleX.Set(transform.ScaleX);
            m_ScaleY.Set(transform.ScaleY);
            m_RotateDeg.Set(transform.RotateDeg);
        }
        
        /// <summary>
        /// Jumps all values to the given transform (stops animations).
        /// </summary>
        public void JumpTo(TransformState transform)
        {
            m_X.Jump(transform.X);
            m_Y.Jump(transform.Y);
            m_ScaleX.Jump(transform.ScaleX);
            m_ScaleY.Jump(transform.ScaleY);
            m_RotateDeg.Jump(transform.RotateDeg);
        }
        
        /// <summary>
        /// Stops all animations.
        /// </summary>
        public void Stop()
        {
            m_X.Stop();
            m_Y.Stop();
            m_ScaleX.Stop();
            m_ScaleY.Stop();
            m_RotateDeg.Stop();
        }
        
        /// <summary>
        /// Destroys all underlying MotionValues.
        /// </summary>
        public void Destroy()
        {
            m_X.Destroy();
            m_Y.Destroy();
            m_ScaleX.Destroy();
            m_ScaleY.Destroy();
            m_RotateDeg.Destroy();
        }
    }
}
