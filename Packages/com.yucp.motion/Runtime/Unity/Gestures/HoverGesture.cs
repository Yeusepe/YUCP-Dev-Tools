using UnityEngine.UIElements;

namespace YUCP.Motion
{
    /// <summary>
    /// Hover gesture handler.
    /// Similar to motion-main's hover gesture.
    /// </summary>
    public class HoverGesture
    {
        private readonly VisualElement m_Element;
        private System.Action<PointerEnterEvent> m_OnHoverStart;
        private System.Action<PointerLeaveEvent> m_OnHoverEnd;
        private bool m_IsHovering;
        
        public bool IsHovering => m_IsHovering;
        
        /// <summary>
        /// Creates a hover gesture.
        /// </summary>
        public HoverGesture(
            VisualElement element,
            System.Action<PointerEnterEvent> onHoverStart = null,
            System.Action<PointerLeaveEvent> onHoverEnd = null)
        {
            m_Element = element;
            m_OnHoverStart = onHoverStart;
            m_OnHoverEnd = onHoverEnd;
            
            RegisterCallbacks();
        }
        
        private void RegisterCallbacks()
        {
            m_Element.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            m_Element.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
        }
        
        private void OnPointerEnter(PointerEnterEvent evt)
        {
            // Filter out touch events
            if (evt.pointerType == UnityEngine.UIElements.PointerType.touch)
                return;
            
            if (m_IsHovering)
                return;
            
            m_IsHovering = true;
            m_OnHoverStart?.Invoke(evt);
        }
        
        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            // Filter out touch events
            if (evt.pointerType == UnityEngine.UIElements.PointerType.touch)
                return;
            
            if (!m_IsHovering)
                return;
            
            m_IsHovering = false;
            m_OnHoverEnd?.Invoke(evt);
        }
        
        /// <summary>
        /// Disposes the gesture and cleans up.
        /// </summary>
        public void Dispose()
        {
            m_Element.UnregisterCallback<PointerEnterEvent>(OnPointerEnter);
            m_Element.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
        }
    }
}
