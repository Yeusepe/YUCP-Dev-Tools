using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Motion
{
    /// <summary>
    /// Gesture utilities for panel-space extraction and coordinate conversion.
    /// </summary>
    public static class GestureUtils
    {
        /// <summary>
        /// Extracts panel-space position from a pointer event.
        /// </summary>
        public static Vector2 GetPanelPosition(IPointerEvent evt)
        {
            return evt.position;
        }
        
        /// <summary>
        /// Converts screen position to panel-space position.
        /// Uses RuntimePanelUtils when available.
        /// </summary>
        public static Vector2 ScreenToPanel(VisualElement element, Vector2 screenPos)
        {
            IPanel panel = element.panel;
            if (panel == null)
                return screenPos;
            
#if UNITY_2021_2_OR_NEWER
            // Use RuntimePanelUtils for runtime panels (works with IPanel interface)
            try
            {
                return RuntimePanelUtils.ScreenToPanel(panel, screenPos);
            }
            catch
            {
                // Not a runtime panel or RuntimePanelUtils not available
            }
#endif
            
            // Fallback: use event position if available, otherwise screen position
            return screenPos;
        }
        
        /// <summary>
        /// Gets the pointer ID from a pointer event.
        /// </summary>
        public static int GetPointerId(IPointerEvent evt)
        {
            return evt.pointerId;
        }
    }
}
