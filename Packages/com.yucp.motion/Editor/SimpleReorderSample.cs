#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.Motion.Editor
{
    /// <summary>
    /// Simple reorder sample similar to Framer Motion's Reorder example.
    /// Demonstrates a vertical list of items that can be dragged to reorder.
    /// </summary>
    public class SimpleReorderSample : EditorWindow
    {
        [MenuItem("Tools/YUCP/Others/Motion/Open Reorder Demo")]
        public static void Open()
        {
            SimpleReorderSample window = GetWindow<SimpleReorderSample>();
            window.titleContent = new GUIContent("Motion Reorder Demo");
            window.Show();
        }

        private ReorderGroup m_ReorderGroup;
        private List<string> m_Items = new List<string> { "🍅 Tomato", "🥒 Cucumber", "🧀 Cheese", "🥬 Lettuce" };
        private List<ReorderItem> m_ReorderItems = new List<ReorderItem>();

        private void CreateGUI()
        {
            Motion.Initialize();

            var root = rootVisualElement;
            root.style.paddingLeft = 20;
            root.style.paddingRight = 20;
            root.style.paddingTop = 20;
            root.style.paddingBottom = 20;

            // Create reorder group (equivalent to Reorder.Group with axis="y")
            m_ReorderGroup = new ReorderGroup(
                vertical: true,
                onReorder: OnReorder
            );

            m_ReorderGroup.style.flexDirection = FlexDirection.Column;
            root.Add(m_ReorderGroup);

            // Create reorder items (equivalent to mapping over items)
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

            // Create new items (equivalent to items.map((item) => <Item key={item} item={item} />))
            foreach (var itemValue in m_Items)
            {
                var reorderItem = CreateItem(itemValue);
                m_ReorderGroup.Add(reorderItem);
                m_ReorderItems.Add(reorderItem);
            }
        }

        private ReorderItem CreateItem(string itemValue)
        {
            // Use Item component (equivalent to <Item key={item} item={item} />)
            return new ReorderItemComponent(m_ReorderGroup, itemValue);
        }

        // Equivalent to onReorder={setItems} callback
        private void OnReorder(object[] newOrder)
        {
            // Update items list (equivalent to setItems)
            m_Items.Clear();
            foreach (var value in newOrder)
            {
                if (value is string str)
                {
                    m_Items.Add(str);
                }
            }

            // Refresh UI to reflect new order
            RefreshItems();
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
