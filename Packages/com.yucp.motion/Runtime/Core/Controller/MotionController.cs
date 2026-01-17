namespace YUCP.Motion.Core
{
    /// <summary>
    /// Core animation controller. Handles interpolation and state transitions.
    /// </summary>
    public class MotionController
    {
        public ElementId Id { get; }
        
        private TransformState m_CurrentTransform;
        private ColorRGBA m_CurrentColor;
        private float m_CurrentOpacity;
        
        private TransformState m_StartTransform;
        private ColorRGBA m_StartColor;
        private float m_StartOpacity;
        
        private TransformState m_TargetTransform;
        private ColorRGBA m_TargetColor;
        private float m_TargetOpacity;
        
        private float m_ElapsedTime;
        private Transition m_Transition;
        private bool m_IsAnimating;
        private DirtyMask m_DirtyMask;
        
        public MotionController(ElementId id)
        {
            Id = id;
            m_CurrentTransform = TransformState.Identity;
            m_CurrentColor = default;
            m_CurrentOpacity = 1.0f;
            m_IsAnimating = false;
            m_DirtyMask = DirtyMask.None;
        }
        
        /// <summary>
        /// Starts animating to the given targets.
        /// </summary>
        public void AnimateTo(MotionTargets targets, Transition transition)
        {
            // Store current state as start
            m_StartTransform = m_CurrentTransform;
            m_StartColor = m_CurrentColor;
            m_StartOpacity = m_CurrentOpacity;
            
            // Set targets (preserve existing values if not specified)
            if (targets.HasX) m_TargetTransform.X = targets.X;
            else m_TargetTransform.X = m_StartTransform.X;
            
            if (targets.HasY) m_TargetTransform.Y = targets.Y;
            else m_TargetTransform.Y = m_StartTransform.Y;
            
            if (targets.HasScaleX) m_TargetTransform.ScaleX = targets.ScaleX;
            else m_TargetTransform.ScaleX = m_StartTransform.ScaleX;
            
            if (targets.HasScaleY) m_TargetTransform.ScaleY = targets.ScaleY;
            else m_TargetTransform.ScaleY = m_StartTransform.ScaleY;
            
            if (targets.HasRotateDeg) m_TargetTransform.RotateDeg = targets.RotateDeg;
            else m_TargetTransform.RotateDeg = m_StartTransform.RotateDeg;
            
            if (targets.HasOpacity) m_TargetOpacity = targets.Opacity;
            else m_TargetOpacity = m_StartOpacity;
            
            if (targets.HasBgRGBA) m_TargetColor = targets.BgRGBA;
            else m_TargetColor = m_StartColor;
            
            m_Transition = transition;
            m_ElapsedTime = 0.0f;
            m_IsAnimating = true;
            m_DirtyMask = targets.GetDirtyMask();
        }
        
        /// <summary>
        /// Ticks the controller with the given frame data.
        /// </summary>
        public void Tick(FrameData frame)
        {
            if (!m_IsAnimating)
                return;
            
            m_ElapsedTime += frame.Delta;
            
            float progress = TimeUtils.GetProgress(m_ElapsedTime, m_Transition.Duration);
            float easedProgress = Easing.Apply(m_Transition.Easing, progress);
            
            // Interpolate values
            m_CurrentTransform = TransformState.Lerp(m_StartTransform, m_TargetTransform, easedProgress);
            m_CurrentColor = ColorRGBA.Lerp(m_StartColor, m_TargetColor, easedProgress);
            m_CurrentOpacity = MathUtils.Lerp(m_StartOpacity, m_TargetOpacity, easedProgress);
            
            // Check if complete
            if (TimeUtils.IsComplete(m_ElapsedTime, m_Transition.Duration))
            {
                m_IsAnimating = false;
                // Snap to final values
                m_CurrentTransform = m_TargetTransform;
                m_CurrentColor = m_TargetColor;
                m_CurrentOpacity = m_TargetOpacity;
            }
        }
        
        /// <summary>
        /// Gets the current transform state.
        /// </summary>
        public TransformState GetTransform() => m_CurrentTransform;
        
        /// <summary>
        /// Gets the current color.
        /// </summary>
        public ColorRGBA GetColor() => m_CurrentColor;
        
        /// <summary>
        /// Gets the current opacity.
        /// </summary>
        public float GetOpacity() => m_CurrentOpacity;
        
        /// <summary>
        /// Gets the dirty mask indicating what needs to be updated.
        /// </summary>
        public DirtyMask GetDirtyMask() => m_IsAnimating ? m_DirtyMask : DirtyMask.None;
        
        /// <summary>
        /// Checks if the controller is currently animating.
        /// </summary>
        public bool IsAnimating => m_IsAnimating;
    }
}
