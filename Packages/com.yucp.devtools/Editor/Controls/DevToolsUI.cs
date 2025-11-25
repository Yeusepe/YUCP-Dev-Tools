using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.Controls
{
    /// <summary>
    /// Utility methods for building consistent YUCP DevTools UIs.
    /// </summary>
    public static class DevToolsUI
    {
        /// <summary>
        /// Create a section container with consistent styling.
        /// </summary>
        /// <returns>Styled section container</returns>
        public static VisualElement CreateSection()
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            return section;
        }
        
        /// <summary>
        /// Create a panel container with consistent styling.
        /// </summary>
        /// <returns>Styled panel container</returns>
        public static VisualElement CreatePanel()
        {
            var panel = new VisualElement();
            panel.AddToClassList("yucp-panel");
            return panel;
        }
        
        /// <summary>
        /// Create a list container with consistent styling.
        /// </summary>
        /// <returns>Styled list container</returns>
        public static VisualElement CreateListContainer()
        {
            var container = new VisualElement();
            container.AddToClassList("yucp-list-container");
            return container;
        }
        
        /// <summary>
        /// Create a list item with consistent styling.
        /// </summary>
        /// <returns>Styled list item</returns>
        public static VisualElement CreateListItem()
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-list-item");
            return item;
        }
        
        /// <summary>
        /// Create a status badge with text.
        /// </summary>
        /// <param name="text">Badge text</param>
        /// <param name="active">Whether the badge is active</param>
        /// <returns>Styled status badge</returns>
        public static Label CreateStatusBadge(string text, bool active = false)
        {
            var badge = new Label(text);
            badge.AddToClassList("yucp-status-badge");
            
            if (active)
            {
                badge.AddToClassList("yucp-status-badge-active");
            }
            
            return badge;
        }
        
        /// <summary>
        /// Create an info box for informational messages.
        /// </summary>
        /// <param name="text">Info text</param>
        /// <returns>Info box container</returns>
        public static VisualElement CreateInfoBox(string text)
        {
            var box = new VisualElement();
            box.AddToClassList("yucp-info-box");
            
            var label = new Label(text);
            box.Add(label);
            
            return box;
        }
        
        /// <summary>
        /// Create a warning box for warning messages.
        /// </summary>
        /// <param name="text">Warning text</param>
        /// <returns>Warning box container</returns>
        public static VisualElement CreateWarningBox(string text)
        {
            var box = new VisualElement();
            box.AddToClassList("yucp-warning-box");
            
            var label = new Label(text);
            box.Add(label);
            
            return box;
        }
        
        /// <summary>
        /// Create an error box for error messages.
        /// </summary>
        /// <param name="text">Error text</param>
        /// <returns>Error box container</returns>
        public static VisualElement CreateErrorBox(string text)
        {
            var box = new VisualElement();
            box.AddToClassList("yucp-error-box");
            
            var label = new Label(text);
            box.Add(label);
            
            return box;
        }
        
        /// <summary>
        /// Create a horizontal separator line.
        /// </summary>
        /// <returns>Separator element</returns>
        public static VisualElement CreateSeparator()
        {
            var separator = new VisualElement();
            separator.AddToClassList("yucp-separator");
            return separator;
        }
        
        /// <summary>
        /// Create a spacer element that fills available space.
        /// </summary>
        /// <returns>Spacer element</returns>
        public static VisualElement CreateSpacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList("yucp-spacer");
            return spacer;
        }
        
        /// <summary>
        /// Create an empty state container for when there's no data to display.
        /// </summary>
        /// <param name="title">Empty state title</param>
        /// <param name="description">Empty state description</param>
        /// <returns>Empty state container</returns>
        public static VisualElement CreateEmptyState(string title, string description)
        {
            var container = new VisualElement();
            container.AddToClassList("yucp-empty-state");
            
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("yucp-empty-state-title");
            
            var descLabel = new Label(description);
            descLabel.AddToClassList("yucp-empty-state-description");
            
            container.Add(titleLabel);
            container.Add(descLabel);
            
            return container;
        }
        
        /// <summary>
        /// Create a field row with label and input element.
        /// </summary>
        /// <param name="labelText">Label text</param>
        /// <param name="inputElement">Input element to add</param>
        /// <returns>Field row container</returns>
        public static VisualElement CreateFieldRow(string labelText, VisualElement inputElement)
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-field-row");
            
            var label = new Label(labelText);
            label.AddToClassList("yucp-field-label");
            
            row.Add(label);
            row.Add(inputElement);
            
            return row;
        }
    }
}



















