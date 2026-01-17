#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.Motion.Samples
{
    /// <summary>
    /// Reorder demo showing drag-to-reorder functionality.
    /// </summary>
    public class ReorderDemo : EditorWindow
    {
        [MenuItem("Tools/YUCP/Others/Motion/Motion Reorder Demo")]
        public static void ShowWindow()
        {
            GetWindow<ReorderDemo>("Reorder Demo");
        }
        
        private ReorderGroup m_ReorderGroup;
        private List<string> m_Items = new List<string> { "Item 1", "Item 2", "Item 3", "Item 4", "Item 5" };
        private List<ReorderItem> m_ReorderItems = new List<ReorderItem>();
        
        private void CreateGUI()
        {
            Motion.Initialize();
            
            var root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;
            
            // Title
            var title = new Label("Reorder Demo - Drag items to reorder")
            {
                style =
                {
                    fontSize = 20,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 20
                }
            };
            root.Add(title);
            
            // Create reorder group
            m_ReorderGroup = new ReorderGroup(
                vertical: true,
                onReorder: OnReorder
            );
            
            m_ReorderGroup.style.flexDirection = FlexDirection.Column;
            m_ReorderGroup.style.gap = 5;
            root.Add(m_ReorderGroup);
            
            // Create reorder items
            RefreshItems();
        }
        
        private void RefreshItems()
        {
            // Clear existing items
            foreach (var item in m_ReorderItems)
            {
                item.Cleanup();
                m_ReorderGroup.Remove(item);
            }
            m_ReorderItems.Clear();
            
            // Create new items
            foreach (var itemValue in m_Items)
            {
                var reorderItem = new ReorderItem(m_ReorderGroup, itemValue);
                
                // Style the item
                reorderItem.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
                reorderItem.style.paddingLeft = 10;
                reorderItem.style.paddingRight = 10;
                reorderItem.style.paddingTop = 5;
                reorderItem.style.paddingBottom = 5;
                reorderItem.style.minHeight = 40;
                reorderItem.style.marginBottom = 5;
                
                // Add label
                var label = new Label(itemValue);
                reorderItem.Add(label);
                
                m_ReorderGroup.Add(reorderItem);
                m_ReorderItems.Add(reorderItem);
            }
        }
        
        private void OnReorder(object[] newOrder)
        {
            // Update items list
            m_Items.Clear();
            foreach (var value in newOrder)
            {
                if (value is string str)
                {
                    m_Items.Add(str);
                }
            }
            
            // Refresh UI
            RefreshItems();
            
            Debug.Log($"Reordered to: {string.Join(", ", m_Items)}");
        }
        
        private void OnDisable()
        {
            foreach (var item in m_ReorderItems)
            {
                item.Cleanup();
            }
            m_ReorderItems.Clear();
        }
    }
}
#endif
