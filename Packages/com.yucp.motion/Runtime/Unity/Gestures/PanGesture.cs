using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Pan info passed to pan handlers.
    /// </summary>
    public struct PanInfo
    {
        public Point Point;
        public Point Delta;
        public Point Offset;
        public Point Velocity;
    }
    
    /// <summary>
    /// Pan gesture handler (similar to drag but for panning/scroll-like interactions).
    /// </summary>
    public class PanGesture
    {
        private readonly VisualElement m_Element;
        private System.Action<PointerMoveEvent, PanInfo> m_OnPan;
        private System.Action<IPointerEvent, PanInfo> m_OnPanStart;
        private System.Action<IPointerEvent, PanInfo> m_OnPanEnd;
        
        private bool m_IsPanning;
        private int m_PointerId;
        private Vector2 m_PointerStartPos;
        private Vector2 m_LastPos;
        private Point m_TotalOffset;
        private float m_LastUpdateTime;
        
        public bool IsPanning => m_IsPanning;
        
        /// <summary>
        /// Creates a pan gesture.
        /// </summary>
        public PanGesture(
            VisualElement element,
            System.Action<PointerMoveEvent, PanInfo> onPan = null,
            System.Action<IPointerEvent, PanInfo> onPanStart = null,
            System.Action<IPointerEvent, PanInfo> onPanEnd = null)
        {
            m_Element = element;
            m_OnPan = onPan;
            m_OnPanStart = onPanStart;
            m_OnPanEnd = onPanEnd;
            
            RegisterCallbacks();
        }
        
        private void RegisterCallbacks()
        {
            m_Element.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_Element.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_Element.RegisterCallback<PointerUpEvent>(OnPointerUp);
            m_Element.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
        }
        
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (m_IsPanning)
                return;
            
            m_IsPanning = true;
            m_PointerId = GestureUtils.GetPointerId(evt);
            m_PointerStartPos = GestureUtils.GetPanelPosition(evt);
            m_LastPos = m_PointerStartPos;
            m_TotalOffset = new Point(0.0f, 0.0f);
            m_LastUpdateTime = (float)UnityEngine.Time.time;
            
            m_Element.CapturePointer(m_PointerId);
            
            // Fire pan start
            var panInfo = CreatePanInfo(evt, true);
            m_OnPanStart?.Invoke(evt, panInfo);
        }
        
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!m_IsPanning || evt.pointerId != m_PointerId)
                return;
            
            var panInfo = CreatePanInfo(evt, false);
            m_OnPan?.Invoke(evt, panInfo);
            
            m_LastPos = GestureUtils.GetPanelPosition(evt);
            m_LastUpdateTime = (float)UnityEngine.Time.time;
        }
        
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!m_IsPanning || evt.pointerId != m_PointerId)
                return;
            
            EndPan(evt);
        }
        
        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (!m_IsPanning || evt.pointerId != m_PointerId)
                return;
            
            EndPan(evt);
        }
        
        private void EndPan(IPointerEvent evt)
        {
            if (!m_IsPanning)
                return;
            
            m_IsPanning = false;
            m_Element.ReleasePointer(m_PointerId);
            
            if (evt != null)
            {
                var panInfo = CreatePanInfo(evt, false);
                m_OnPanEnd?.Invoke(evt, panInfo);
            }
        }
        
        private PanInfo CreatePanInfo(IPointerEvent evt, bool isStart)
        {
            Vector2 currentPos = GestureUtils.GetPanelPosition(evt);
            Vector2 delta = currentPos - m_LastPos;
            
            if (isStart)
            {
                delta = Vector2.zero;
            }
            
            m_TotalOffset.X += delta.x;
            m_TotalOffset.Y += delta.y;
            
            // Calculate velocity (pixels per second)
            float deltaTime = (float)UnityEngine.Time.time - m_LastUpdateTime;
            Point velocity = deltaTime > 0.0f
                ? new Point(delta.x / deltaTime * 1000.0f, delta.y / deltaTime * 1000.0f)
                : new Point(0.0f, 0.0f);
            
            return new PanInfo
            {
                Point = new Point(currentPos.x, currentPos.y),
                Delta = new Point(delta.x, delta.y),
                Offset = m_TotalOffset,
                Velocity = velocity
            };
        }
        
        /// <summary>
        /// Disposes the gesture and cleans up.
        /// </summary>
        public void Dispose()
        {
            if (m_IsPanning)
            {
                m_Element.ReleasePointer(m_PointerId);
                m_IsPanning = false;
            }
            
            m_Element.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            m_Element.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            m_Element.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            m_Element.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
        }
    }
}
