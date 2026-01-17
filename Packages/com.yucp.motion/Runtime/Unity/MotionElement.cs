using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Optional sugar VisualElement that internally uses Motion.Attach(this).
    /// </summary>
    public class MotionElement : VisualElement
    {
        private MotionHandle m_Handle;
        
        public MotionElement()
        {
            // Attach motion when element is added to panel
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }
        
        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (m_Handle == null)
            {
                // Parse initial targets from style if set via UXML
                MotionTargets? initial = null;
                // This will be set by UXML traits
                
                m_Handle = Motion.Attach(this, initial);
            }
        }
        
        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (m_Handle != null)
            {
                m_Handle.Dispose();
                m_Handle = null;
            }
        }
        
        /// <summary>
        /// Animates to the given targets.
        /// </summary>
        public void Animate(MotionTargets targets, Transition transition)
        {
            m_Handle?.Animate(targets, transition);
        }
        
        /// <summary>
        /// Animates to the given targets with default transition.
        /// </summary>
        public void Animate(MotionTargets targets)
        {
            m_Handle?.Animate(targets);
        }
    }
}
