using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Motion.Core;
using static YUCP.Motion.Core.MathUtils;

namespace YUCP.Motion
{
    /// <summary>
    /// Applies grouped style writes with dirty flags and value caching.
    /// Applies translate once, scale once, rotate once to avoid redundant writes.
    /// </summary>
    public class StyleApplier
    {
        private readonly VisualElement m_Element;
        
        // Cached last applied values to avoid redundant writes
        private TransformState m_LastTransform;
        private ColorRGBA m_LastColor;
        private float m_LastOpacity;
        
        public StyleApplier(VisualElement element)
        {
            m_Element = element;
            m_LastTransform = TransformState.Identity;
            m_LastColor = default;
            m_LastOpacity = 1.0f;
        }
        
        /// <summary>
        /// Applies the resolved motion values, only updating what's dirty.
        /// </summary>
        public void Apply(ResolvedMotionValues values)
        {
            DirtyMask dirty = values.DirtyMask;
            
            // Apply transform properties (grouped)
            if ((dirty & DirtyMask.Transform) != 0)
            {
                TransformState current = values.Transform;
                
                // Only update if values changed
                if (!TransformStateEquals(current, m_LastTransform))
                {
                    m_Element.style.translate = UiToolkitCompat.PxTranslate(current.X, current.Y);
                    m_Element.style.scale = UiToolkitCompat.XYScale(current.ScaleX, current.ScaleY);
                    m_Element.style.rotate = UiToolkitCompat.DegRotate(current.RotateDeg);
                    
                    m_LastTransform = current;
                }
            }
            
            // Apply paint properties
            if ((dirty & DirtyMask.Paint) != 0)
            {
                if (!Approximately(values.Opacity, m_LastOpacity))
                {
                    m_Element.style.opacity = values.Opacity;
                    m_LastOpacity = values.Opacity;
                }
                
                if (!ColorRGBAEquals(values.BackgroundColor, m_LastColor))
                {
                    ColorRGBA c = values.BackgroundColor;
                    m_Element.style.backgroundColor = new Color(c.R, c.G, c.B, c.A);
                    m_LastColor = values.BackgroundColor;
                }
            }
        }
        
        /// <summary>
        /// Reads initial values from the element.
        /// Uses style.value for compatibility across Unity versions.
        /// Defaults to identity/zero if not set.
        /// </summary>
        public ResolvedMotionValues ReadInitial()
        {
            TransformState transform = TransformState.Identity;
            
            // Read translate - use style.value (more compatible across versions)
            if (m_Element.style.translate.value != null)
            {
                Translate translate = m_Element.style.translate.value;
                transform.X = translate.x.value;
                transform.Y = translate.y.value;
            }
            
            // Read scale
            if (m_Element.style.scale.value != null)
            {
                Scale scale = m_Element.style.scale.value;
                transform.ScaleX = scale.value.x;
                transform.ScaleY = scale.value.y;
            }
            
            // Read rotate
            if (m_Element.style.rotate.value != null)
            {
                Rotate rotate = m_Element.style.rotate.value;
                transform.RotateDeg = rotate.angle.value;
            }
            
            // Read opacity - style.opacity.value returns float directly
            float opacity = m_Element.style.opacity.value;
            
            // Read background color
            ColorRGBA bgColor = default;
            if (m_Element.style.backgroundColor.value != null)
            {
                Color c = m_Element.style.backgroundColor.value;
                bgColor = new ColorRGBA(c.r, c.g, c.b, c.a);
            }
            
            return new ResolvedMotionValues(transform, bgColor, opacity, DirtyMask.All);
        }
        
        private static bool TransformStateEquals(TransformState a, TransformState b)
        {
            return Approximately(a.X, b.X) &&
                   Approximately(a.Y, b.Y) &&
                   Approximately(a.ScaleX, b.ScaleX) &&
                   Approximately(a.ScaleY, b.ScaleY) &&
                   Approximately(a.RotateDeg, b.RotateDeg);
        }
        
        private static bool ColorRGBAEquals(ColorRGBA a, ColorRGBA b)
        {
            return Approximately(a.R, b.R) &&
                   Approximately(a.G, b.G) &&
                   Approximately(a.B, b.B) &&
                   Approximately(a.A, b.A);
        }
    }
}
