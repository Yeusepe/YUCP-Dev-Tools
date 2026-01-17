#if UNITY_EDITOR
using UnityEngine.UIElements;
using YUCP.Motion;

namespace YUCP.Motion.Editor
{
    /// <summary>
    /// Item component for reorderable items.
    /// Similar to the Item component in the React/Framer Motion example.
    /// </summary>
    public class ReorderItemComponent : ReorderItem
    {
        private readonly string m_ItemText;

        /// <summary>
        /// Creates a new Item component.
        /// </summary>
        public ReorderItemComponent(ReorderGroup group, string item) : base(group, item)
        {
            m_ItemText = item;
            SetupStyles();
            AddContent();
        }

        private void SetupStyles()
        {
            style.backgroundColor = new UnityEngine.Color(0.25f, 0.25f, 0.25f);
            style.paddingLeft = 16;
            style.paddingRight = 16;
            style.paddingTop = 12;
            style.paddingBottom = 12;
            style.minHeight = 50;
            style.borderTopLeftRadius = 4;
            style.borderTopRightRadius = 4;
            style.borderBottomLeftRadius = 4;
            style.borderBottomRightRadius = 4;
            style.marginBottom = 8;
        }

        private void AddContent()
        {
            var label = new Label(m_ItemText)
            {
                style =
                {
                    fontSize = 16,
                    color = UnityEngine.Color.white
                }
            };
            Add(label);
        }
    }
}
#endif
