using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.Controls
{
    /// <summary>
    /// Custom button control with YUCP design system styling.
    /// Consistent with Package Guardian PgButton.
    /// </summary>
    public class DevToolsButton : Button
    {
        public DevToolsButton() : base()
        {
            AddToClassList("yucp-button");
        }
        
        public DevToolsButton(System.Action clickEvent) : base(clickEvent)
        {
            AddToClassList("yucp-button");
        }
        
        /// <summary>
        /// Set this button as a primary action button (YUCP teal).
        /// </summary>
        public void SetPrimary()
        {
            AddToClassList("yucp-button-primary");
        }
        
        /// <summary>
        /// Set this button as a danger/delete action button (red).
        /// </summary>
        public void SetDanger()
        {
            AddToClassList("yucp-button-danger");
        }
        
        /// <summary>
        /// Set this button as a small button (24px height).
        /// </summary>
        public void SetSmall()
        {
            AddToClassList("yucp-button-small");
        }
        
        /// <summary>
        /// Set this button as an icon button (square, 32x32px).
        /// </summary>
        public void SetIcon()
        {
            AddToClassList("yucp-button-icon");
        }
    }
}







