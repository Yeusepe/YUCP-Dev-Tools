using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Motion.Core;
using YUCP.Motion.Core.Animation;

namespace YUCP.Motion
{
    /// <summary>
    /// Enhanced drag gesture with axis locking, constraints, elastic edges, and momentum.
    /// Works with MotionValues directly.
    /// </summary>
    public class EnhancedDragGesture
    {
        private readonly VisualElement m_Element;
        private readonly MotionValue<float> m_X;
        private readonly MotionValue<float> m_Y;
        private readonly DragGestureOptions m_Options;
        
        private bool m_IsDragging;
        private int m_PointerId;
        private Vector2 m_PointerStartPanelPos;
        private float m_ValueStartX;
        private float m_ValueStartY;
        private IAnimationPlaybackControls m_MomentumAnimation;
        
        public bool IsDragging => m_IsDragging;
        
        /// <summary>
        /// Creates an enhanced drag gesture.
        /// </summary>
        public EnhancedDragGesture(
            VisualElement element,
            MotionValue<float> x,
            MotionValue<float> y,
            DragGestureOptions? options = null)
        {
            m_Element = element;
            m_X = x;
            m_Y = y;
            m_Options = options ?? DragGestureOptions.Default;
            
            RegisterCallbacks();
        }
        
        private void RegisterCallbacks()
        {
            m_Element.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_Element.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_Element.RegisterCallback<PointerUpEvent>(OnPointerUp);
            m_Element.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            m_Element.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            m_Element.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }
        
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (m_IsDragging)
                return;
            
            // Stop any momentum animation
            m_MomentumAnimation?.Stop();
            m_MomentumAnimation = null;
            
            m_IsDragging = true;
            m_PointerId = GestureUtils.GetPointerId(evt);
            m_PointerStartPanelPos = GestureUtils.GetPanelPosition(evt);
            
            // Store current values as start
            m_ValueStartX = m_X.Get();
            m_ValueStartY = m_Y.Get();
            
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
            
            // Apply axis locking
            if (m_Options.LockX)
            {
                delta.y = 0.0f;
            }
            else if (m_Options.LockY)
            {
                delta.x = 0.0f;
            }
            
            // Calculate new positions
            float newX = m_ValueStartX + delta.x;
            float newY = m_ValueStartY + delta.y;
            
            // Apply constraints with elastic edges
            newX = ApplyConstraints(newX, m_Options.XConstraints, m_Options.Elastic, true);
            newY = ApplyConstraints(newY, m_Options.YConstraints, m_Options.Elastic, false);
            
            // Update MotionValues directly (immediate, no animation)
            m_X.Jump(newX, false);
            m_Y.Jump(newY, false);
            
            evt.StopPropagation();
        }
        
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!m_IsDragging || evt.pointerId != m_PointerId)
                return;
            
            EndDrag(evt);
            evt.StopPropagation();
        }
        
        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (!m_IsDragging || evt.pointerId != m_PointerId)
                return;
            
            EndDrag(evt);
            evt.StopPropagation();
        }
        
        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (m_IsDragging && evt.pointerId == m_PointerId)
            {
                EndDrag(null);
            }
        }
        
        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (m_IsDragging)
            {
                EndDrag(null);
            }
        }
        
        private void EndDrag(IPointerEvent evt)
        {
            if (!m_IsDragging)
                return;
            
            m_IsDragging = false;
            m_Element.ReleasePointer(m_PointerId);
            
            // Apply momentum if enabled
            if (m_Options.Momentum && evt != null)
            {
                ApplyMomentum();
            }
        }
        
        /// <summary>
        /// Applies constraints with elastic edges.
        /// </summary>
        private float ApplyConstraints(float value, Axis? constraints, float? elastic, bool isX)
        {
            if (!constraints.HasValue)
                return value;
            
            var axis = constraints.Value;
            float elasticFactor = elastic ?? 0.0f;
            
            if (axis.Min.HasValue && value < axis.Min.Value)
            {
                if (elasticFactor > 0.0f)
                {
                    // Apply elastic constraint - mix between constraint and value
                    float overshoot = axis.Min.Value - value;
                    float elasticAmount = overshoot * elasticFactor;
                    value = axis.Min.Value - elasticAmount;
                }
                else
                {
                    value = MathUtils.Clamp(value, axis.Min.Value, float.MaxValue);
                }
            }
            else if (axis.Max.HasValue && value > axis.Max.Value)
            {
                if (elasticFactor > 0.0f)
                {
                    // Apply elastic constraint - mix between constraint and value
                    float overshoot = value - axis.Max.Value;
                    float elasticAmount = overshoot * elasticFactor;
                    value = axis.Max.Value + elasticAmount;
                }
                else
                {
                    value = MathUtils.Clamp(value, float.MinValue, axis.Max.Value);
                }
            }
            
            return value;
        }
        
        /// <summary>
        /// Applies momentum (inertia) animation after drag ends.
        /// </summary>
        private void ApplyMomentum()
        {
            // Get velocity from MotionValues
            float velocityX = m_X.GetVelocity();
            float velocityY = m_Y.GetVelocity();
            
            if (System.Math.Abs(velocityX) < 0.1f && System.Math.Abs(velocityY) < 0.1f)
                return;
            
            // Calculate target positions based on momentum
            float power = m_Options.MomentumPower;
            float amplitudeX = power * velocityX;
            float amplitudeY = power * velocityY;
            
            float targetX = m_X.Get() + amplitudeX;
            float targetY = m_Y.Get() + amplitudeY;
            
            // Apply constraints to targets
            targetX = ApplyConstraints(targetX, m_Options.XConstraints, m_Options.Elastic, true);
            targetY = ApplyConstraints(targetY, m_Options.YConstraints, m_Options.Elastic, false);
            
            // Create inertia animation
            // TODO: Integrate with InertiaGenerator when implemented
            // For now, use spring animation as approximation
            var springOptions = new SpringOptions
            {
                Stiffness = 100.0f,
                Damping = 10.0f,
                Mass = 1.0f,
                Velocity = velocityX,
                RestSpeed = 2.0f,
                RestDelta = 0.5f
            };
            
            // Animate X and Y with spring
            // This would be done via animation system
        }
        
        /// <summary>
        /// Disposes the gesture and cleans up.
        /// </summary>
        public void Dispose()
        {
            if (m_IsDragging)
            {
                m_Element.ReleasePointer(m_PointerId);
                m_IsDragging = false;
            }
            
            m_MomentumAnimation?.Stop();
            
            m_Element.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            m_Element.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            m_Element.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            m_Element.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            m_Element.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            m_Element.UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }
    }
}
