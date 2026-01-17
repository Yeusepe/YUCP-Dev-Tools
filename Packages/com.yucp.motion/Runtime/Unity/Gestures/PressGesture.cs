using UnityEngine.UIElements;

namespace YUCP.Motion
{
    /// <summary>
    /// Press gesture handler (tap/press).
    /// Similar to motion-main's press gesture.
    /// </summary>
    public class PressGesture
    {
        private readonly VisualElement m_Element;
        private System.Action<PointerDownEvent> m_OnPressStart;
        private System.Action<PointerUpEvent, bool> m_OnPressEnd;
        private bool m_IsPressing;
        private int m_PointerId;
        
        public bool IsPressing => m_IsPressing;
        
        /// <summary>
        /// Creates a press gesture.
        /// </summary>
        public PressGesture(
            VisualElement element,
            System.Action<PointerDownEvent> onPressStart = null,
            System.Action<PointerUpEvent, bool> onPressEnd = null)
        {
            m_Element = element;
            m_OnPressStart = onPressStart;
            m_OnPressEnd = onPressEnd;
            
            RegisterCallbacks();
        }
        
        private void RegisterCallbacks()
        {
            m_Element.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_Element.RegisterCallback<PointerUpEvent>(OnPointerUp);
            m_Element.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
        }
        
        private void OnPointerDown(PointerDownEvent evt)
        {
            // Only handle primary pointer
            if (evt.button != 0)
                return;
            
            if (m_IsPressing)
                return;
            
            m_IsPressing = true;
            m_PointerId = evt.pointerId;
            
            m_OnPressStart?.Invoke(evt);
        }
        
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!m_IsPressing || evt.pointerId != m_PointerId)
                return;
            
            bool success = evt.target == m_Element || m_Element.Contains(evt.target as VisualElement);
            
            m_IsPressing = false;
            m_OnPressEnd?.Invoke(evt, success);
        }
        
        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (!m_IsPressing || evt.pointerId != m_PointerId)
                return;
            
            m_IsPressing = false;
            m_OnPressEnd?.Invoke(null, false);
        }
        
        /// <summary>
        /// Disposes the gesture and cleans up.
        /// </summary>
        public void Dispose()
        {
            m_Element.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            m_Element.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            m_Element.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
        }
    }
}
