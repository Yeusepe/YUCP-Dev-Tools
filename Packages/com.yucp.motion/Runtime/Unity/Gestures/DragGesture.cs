using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Drag gesture handler with correct pointer math, pointer capture, and coordinate conversion.
    /// </summary>
    public class DragGesture
    {
        private readonly VisualElement m_Element;
        private readonly MotionHandle m_MotionHandle;
        
        private bool m_IsDragging;
        private int m_PointerId;
        private Vector2 m_PointerStartPanelPos;
        private TransformState m_ValueStartTransform;
        
        public DragGesture(VisualElement element, MotionHandle motionHandle)
        {
            m_Element = element;
            m_MotionHandle = motionHandle;
            
            // Register pointer events
            element.RegisterCallback<PointerDownEvent>(OnPointerDown);
            element.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            element.RegisterCallback<PointerUpEvent>(OnPointerUp);
            element.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            element.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            element.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }
        
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (m_IsDragging)
                return;
            
            m_IsDragging = true;
            m_PointerId = GestureUtils.GetPointerId(evt);
            m_PointerStartPanelPos = GestureUtils.GetPanelPosition(evt);
            
            // Store current transform as start
            TransformState current = m_MotionHandle.GetCurrentTransform();
            m_ValueStartTransform = current;
            
            // Capture pointer
            m_Element.CapturePointer(m_PointerId);
            
            evt.StopPropagation();
        }
        
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!m_IsDragging || evt.pointerId != m_PointerId)
                return;
            
            Vector2 currentPanelPos = GestureUtils.GetPanelPosition(evt);
            Vector2 delta = currentPanelPos - m_PointerStartPanelPos;
            
            // Calculate new translate: valueStartTranslate + delta
            float newX = m_ValueStartTransform.X + delta.x;
            float newY = m_ValueStartTransform.Y + delta.y;
            
            // Update motion targets
            MotionTargets targets = new MotionTargets
            {
                HasX = true,
                X = newX,
                HasY = true,
                Y = newY
            };
            
            // Animate with zero duration for immediate update
            m_MotionHandle.Animate(targets, new Transition(0.0f));
            
            evt.StopPropagation();
        }
        
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!m_IsDragging || evt.pointerId != m_PointerId)
                return;
            
            EndDrag();
            evt.StopPropagation();
        }
        
        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (!m_IsDragging || evt.pointerId != m_PointerId)
                return;
            
            EndDrag();
            evt.StopPropagation();
        }
        
        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (m_IsDragging && evt.pointerId == m_PointerId)
            {
                EndDrag();
            }
        }
        
        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (m_IsDragging)
            {
                EndDrag();
            }
        }
        
        private void EndDrag()
        {
            if (!m_IsDragging)
                return;
            
            m_IsDragging = false;
            m_Element.ReleasePointer(m_PointerId);
        }
        
        public void Dispose()
        {
            if (m_IsDragging)
            {
                m_Element.ReleasePointer(m_PointerId);
                m_IsDragging = false;
            }
            
            m_Element.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            m_Element.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            m_Element.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            m_Element.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            m_Element.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            m_Element.UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }
    }
}
