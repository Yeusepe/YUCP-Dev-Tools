using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// ReorderGroup container that tracks item layouts and handles reordering.
    /// Similar to motion-main's Reorder.Group.
    /// </summary>
    public class ReorderGroup : VisualElement
    {
        private readonly Dictionary<object, ItemData> m_ItemData = new Dictionary<object, ItemData>();
        private readonly List<object> m_Order = new List<object>();
        private readonly System.Action<object[]> m_OnReorder;
        private readonly bool m_IsVertical;
        private bool m_IsReordering;
        
        /// <summary>
        /// Item data stored in the group.
        /// </summary>
        private struct ItemData
        {
            public object Value;
            public float LayoutMin;
            public float LayoutMax;
        }
        
        /// <summary>
        /// Creates a new ReorderGroup.
        /// </summary>
        public ReorderGroup(bool vertical = true, System.Action<object[]> onReorder = null)
        {
            m_IsVertical = vertical;
            m_OnReorder = onReorder;
        }
        
        /// <summary>
        /// Registers an item with its layout.
        /// </summary>
        internal void RegisterItem(object value, float layoutMin, float layoutMax)
        {
            if (m_ItemData.TryGetValue(value, out var existing))
            {
                // Update existing
                existing.LayoutMin = layoutMin;
                existing.LayoutMax = layoutMax;
                m_ItemData[value] = existing;
            }
            else
            {
                // Add new
                m_ItemData[value] = new ItemData
                {
                    Value = value,
                    LayoutMin = layoutMin,
                    LayoutMax = layoutMax
                };
                m_Order.Add(value);
            }
            
            // Sort by layout position
            m_Order.Sort((a, b) =>
            {
                if (!m_ItemData.TryGetValue(a, out var dataA) || !m_ItemData.TryGetValue(b, out var dataB))
                    return 0;
                return dataA.LayoutMin.CompareTo(dataB.LayoutMin);
            });
        }
        
        /// <summary>
        /// Updates the order based on drag offset and velocity.
        /// </summary>
        internal void UpdateOrder(object itemValue, float offset, float velocity)
        {
            if (m_IsReordering)
                return;
            
            if (!m_ItemData.TryGetValue(itemValue, out var itemData))
                return;
            
            // Calculate new position
            float newMin = itemData.LayoutMin + offset;
            float newMax = itemData.LayoutMax + offset;
            
            // Find new index based on position
            int newIndex = FindInsertionIndex(newMin);
            int currentIndex = m_Order.IndexOf(itemValue);
            
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < m_Order.Count)
            {
                m_IsReordering = true;
                
                // Reorder
                m_Order.RemoveAt(currentIndex);
                m_Order.Insert(newIndex, itemValue);
                
                // Notify callback
                m_OnReorder?.Invoke(m_Order.ToArray());
            }
        }
        
        /// <summary>
        /// Finds the insertion index for a given position.
        /// </summary>
        private int FindInsertionIndex(float position)
        {
            for (int i = 0; i < m_Order.Count; i++)
            {
                if (m_ItemData.TryGetValue(m_Order[i], out var data))
                {
                    if (position < data.LayoutMin)
                    {
                        return i;
                    }
                }
            }
            return m_Order.Count;
        }
        
        /// <summary>
        /// Resets the reordering flag (called after order update completes).
        /// </summary>
        internal void ResetReordering()
        {
            m_IsReordering = false;
        }
        
        /// <summary>
        /// Gets whether the group is vertical.
        /// </summary>
        public bool IsVertical => m_IsVertical;
    }
}
