using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Layout measurement utilities for layout animations.
    /// Handles worldBound timing and GeometryChangedEvent.
    /// </summary>
    public static class LayoutMeasurement
    {
        /// <summary>
        /// Gets the world bound of an element (only reliable after layout).
        /// </summary>
        public static Rect GetWorldBound(VisualElement element)
        {
            return element.worldBound;
        }
        
        /// <summary>
        /// Gets the layout position of an element.
        /// </summary>
        public static Point GetLayoutPosition(VisualElement element)
        {
            var bound = element.layout;
            return new Point(bound.x, bound.y);
        }
        
        /// <summary>
        /// Gets the layout size of an element.
        /// </summary>
        public static Point GetLayoutSize(VisualElement element)
        {
            var bound = element.layout;
            return new Point(bound.width, bound.height);
        }
        
        /// <summary>
        /// Checks if an element has valid layout (layout.width > 0 && layout.height > 0).
        /// </summary>
        public static bool HasValidLayout(VisualElement element)
        {
            return element.layout.width > 0 && element.layout.height > 0;
        }
        
        /// <summary>
        /// Registers a callback for when layout changes.
        /// Returns an unsubscribe function.
        /// </summary>
        public static System.Func<bool> OnLayoutChanged(VisualElement element, System.Action callback)
        {
            EventCallback<GeometryChangedEvent> handler = evt => callback();
            element.RegisterCallback(handler);
            
            return () =>
            {
                element.UnregisterCallback(handler);
                return true;
            };
        }
        
        /// <summary>
        /// Registers a callback for when element is detached from panel.
        /// </summary>
        public static System.Func<bool> OnDetachedFromPanel(VisualElement element, System.Action callback)
        {
            EventCallback<DetachFromPanelEvent> handler = evt => callback();
            element.RegisterCallback(handler);
            
            return () =>
            {
                element.UnregisterCallback(handler);
                return true;
            };
        }
    }
}
