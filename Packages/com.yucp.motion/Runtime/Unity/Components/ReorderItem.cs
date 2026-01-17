using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// ReorderItem that can be dragged to reorder within a ReorderGroup.
    /// Similar to motion-main's Reorder.Item.
    /// </summary>
    public class ReorderItem : VisualElement
    {
        private readonly ReorderGroup m_Group;
        private readonly object m_Value;
        private MotionHandle m_MotionHandle;
        private EnhancedDragGesture m_DragGesture;
        private float m_DragOffset;
        
        /// <summary>
        /// The value this item represents.
        /// </summary>
        public object Value => m_Value;
        
        /// <summary>
        /// Creates a new ReorderItem.
        /// </summary>
        public ReorderItem(ReorderGroup group, object value)
        {
            m_Group = group ?? throw new System.ArgumentNullException(nameof(group));
            m_Value = value ?? throw new System.ArgumentNullException(nameof(value));
            
            Initialize();
        }
        
        private void Initialize()
        {
            // Create motion handle
            m_MotionHandle = Motion.Attach(this);
            
            // Create drag gesture with axis locking
            var styleApplier = new MotionValueStyleApplier(this);
            var dragOptions = new DragGestureOptions
            {
                LockX = !m_Group.IsVertical,
                LockY = m_Group.IsVertical,
                Momentum = false // Disable momentum for reordering
            };
            
            m_DragGesture = new EnhancedDragGesture(
                this,
                styleApplier.X,
                styleApplier.Y,
                dragOptions
            );
            
            // Register with group on layout change
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            
            // Track drag to update order
            TrackDrag();
        }
        
        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // Register layout with group
            var layout = this.layout;
            float min = m_Group.IsVertical ? layout.y : layout.x;
            float max = min + (m_Group.IsVertical ? layout.height : layout.width);
            
            m_Group.RegisterItem(m_Value, min, max);
        }
        
        private void TrackDrag()
        {
            // Subscribe to drag updates
            // This would integrate with EnhancedDragGesture's callbacks
            // For now, we'll use a simplified approach
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
        }
        
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!m_DragGesture.IsDragging)
                return;
            
            // Get current drag offset
            var currentTransform = m_MotionHandle.GetCurrentTransform();
            float offset = m_Group.IsVertical ? currentTransform.Y : currentTransform.X;
            
            // Get velocity
            var styleApplier = new MotionValueStyleApplier(this);
            float velocity = m_Group.IsVertical 
                ? styleApplier.Y.GetVelocity() 
                : styleApplier.X.GetVelocity();
            
            // Update order in group
            m_Group.UpdateOrder(m_Value, offset, velocity);
        }
        
        /// <summary>
        /// Cleans up the item.
        /// </summary>
        public void Cleanup()
        {
            m_DragGesture?.Dispose();
            m_MotionHandle?.Dispose();
        }
    }
}
