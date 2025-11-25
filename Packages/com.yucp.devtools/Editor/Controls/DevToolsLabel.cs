using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.Controls
{
    /// <summary>
    /// Styled label helper for YUCP DevTools.
    /// Consistent with Package Guardian PgLabel.
    /// </summary>
    public static class DevToolsLabel
    {
        /// <summary>
        /// Create a standard label with optional secondary styling.
        /// </summary>
        /// <param name="text">Label text</param>
        /// <param name="secondary">Apply secondary (gray) styling</param>
        /// <returns>Styled label element</returns>
        public static Label Create(string text, bool secondary = false)
        {
            var label = new Label(text);
            label.AddToClassList("yucp-label");
            
            if (secondary)
            {
                label.AddToClassList("yucp-label-secondary");
            }
            
            return label;
        }
        
        /// <summary>
        /// Create a small label (reduced font size).
        /// </summary>
        /// <param name="text">Label text</param>
        /// <returns>Small label element</returns>
        public static Label CreateSmall(string text)
        {
            var label = new Label(text);
            label.AddToClassList("yucp-label-small");
            return label;
        }
        
        /// <summary>
        /// Create a title label (large, bold).
        /// </summary>
        /// <param name="text">Title text</param>
        /// <returns>Title label element</returns>
        public static Label CreateTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("yucp-title");
            return label;
        }
        
        /// <summary>
        /// Create a subtitle label (medium size).
        /// </summary>
        /// <param name="text">Subtitle text</param>
        /// <returns>Subtitle label element</returns>
        public static Label CreateSubtitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("yucp-subtitle");
            return label;
        }
        
        /// <summary>
        /// Create a section title label (small, bold, uppercase style).
        /// </summary>
        /// <param name="text">Section title text</param>
        /// <returns>Section title label element</returns>
        public static Label CreateSectionTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("yucp-section-title");
            return label;
        }
        
        /// <summary>
        /// Create a status label with color-coded styling.
        /// </summary>
        /// <param name="text">Status text</param>
        /// <param name="status">Status type (determines color)</param>
        /// <returns>Status label element</returns>
        public static Label CreateStatus(string text, StatusType status)
        {
            var label = Create(text);
            
            switch (status)
            {
                case StatusType.Success:
                    label.AddToClassList("yucp-status-badge-success");
                    break;
                case StatusType.Warning:
                    label.AddToClassList("yucp-status-badge-warning");
                    break;
                case StatusType.Error:
                    label.AddToClassList("yucp-status-badge-error");
                    break;
                case StatusType.Active:
                    label.AddToClassList("yucp-status-badge-active");
                    break;
            }
            
            return label;
        }
    }
    
    /// <summary>
    /// Status types for color-coded labels.
    /// </summary>
    public enum StatusType
    {
        Success,
        Warning,
        Error,
        Active
    }
}



















