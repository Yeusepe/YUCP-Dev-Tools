using System;
using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// UseInView controller - tracks whether an element is in view.
    /// Similar to motion-main's useInView hook.
    /// </summary>
    public class UseInView : IDisposable
    {
        private readonly VisualElement m_Element;
        private readonly MotionValue<bool> m_IsInView;
        private System.Func<bool> m_UnsubscribeLayout;
        private bool m_Disposed;
        
        /// <summary>
        /// Gets the isInView MotionValue.
        /// </summary>
        public MotionValue<bool> IsInView => m_IsInView;
        
        /// <summary>
        /// Creates a new UseInView controller.
        /// </summary>
        public UseInView(VisualElement element)
        {
            m_Element = element ?? throw new ArgumentNullException(nameof(element));
            m_IsInView = new MotionValue<bool>(false);
            
            // Check initial state
            UpdateInView();
            
            // Subscribe to layout changes
            m_UnsubscribeLayout = LayoutMeasurement.OnLayoutChanged(m_Element, UpdateInView);
            
            // Subscribe to panel changes
            element.RegisterCallback<AttachToPanelEvent>(_ => UpdateInView());
            element.RegisterCallback<DetachFromPanelEvent>(_ => m_IsInView.Set(false));
        }
        
        private void UpdateInView()
        {
            if (m_Element == null || m_Element.panel == null)
            {
                m_IsInView.Set(false);
                return;
            }
            
            // Check if element is visible and in viewport
            bool isInView = IsElementInView(m_Element);
            m_IsInView.Set(isInView);
        }
        
        /// <summary>
        /// Checks if an element is in view using geometry checks and panel visibility heuristics.
        /// </summary>
        private bool IsElementInView(VisualElement element)
        {
            // Check if element has valid layout
            if (!LayoutMeasurement.HasValidLayout(element))
                return false;
            
            // Check if element is visible
            if (element.visible == false || element.resolvedStyle.visibility == Visibility.Hidden)
                return false;
            
            // Get world bound
            var worldBound = LayoutMeasurement.GetWorldBound(element);
            
            // Check if bound is valid (has area)
            if (worldBound.width <= 0 || worldBound.height <= 0)
                return false;
            
            // Get panel bounds
            var panel = element.panel;
            if (panel == null)
                return false;
            
            // Check if element intersects with panel viewport
            // This is a simplified check - in a full implementation, we'd check against the actual viewport
            return true; // Simplified: assume in view if visible and has layout
        }
        
        public void Dispose()
        {
            if (m_Disposed)
                return;
            
            m_Disposed = true;
            
            m_UnsubscribeLayout?.Invoke();
            
            m_Element?.UnregisterCallback<AttachToPanelEvent>(_ => UpdateInView());
            m_Element?.UnregisterCallback<DetachFromPanelEvent>(_ => m_IsInView.Set(false));
            
            m_IsInView.Destroy();
        }
    }
}
