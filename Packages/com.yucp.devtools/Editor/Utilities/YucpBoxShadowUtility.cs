using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.Utilities
{
    /// <summary>
    /// Utility class for creating reusable box shadow effects in Unity UI Toolkit
    /// </summary>
    public static class YucpBoxShadowUtility
    {
        /// <summary>
        /// Configuration for box shadow appearance
        /// </summary>
        public struct BoxShadowConfig
        {
            public Color color;
            public float offsetX;
            public float offsetY;
            public float blurRadius;
            public float spread;
            public float borderRadius;
            
            public static BoxShadowConfig Default => new BoxShadowConfig
            {
                color = new Color(0f, 0f, 0f, 0.2f),
                offsetX = 0f,
                offsetY = 1f,
                blurRadius = 3f,
                spread = 0f,
                borderRadius = 6f
            };
            
            public static BoxShadowConfig Hover => new BoxShadowConfig
            {
                color = new Color(0f, 0f, 0f, 0.4f), // Neutral dark shadow
                offsetX = 0f,
                offsetY = 2f,
                blurRadius = 6f,
                spread = 0f,
                borderRadius = 6f
            };
        }
        
        /// <summary>
        /// Adds a box shadow effect to a VisualElement using layered shadow elements
        /// </summary>
        /// <param name="element">The element to add the shadow to</param>
        /// <param name="config">Shadow configuration</param>
        /// <returns>The shadow container element (can be used to update/remove shadow)</returns>
        public static VisualElement AddBoxShadow(VisualElement element, BoxShadowConfig config)
        {
            if (element == null)
                return null;
            
            // Ensure element has relative positioning for absolute children
            // Relative positioning is required for absolute-positioned shadow children to work properly
            element.style.position = Position.Relative;
            
            // Create shadow container
            var shadowContainer = new VisualElement();
            shadowContainer.name = "yucp-box-shadow-container";
            shadowContainer.style.position = Position.Absolute;
            shadowContainer.pickingMode = PickingMode.Ignore; // Don't intercept mouse events
            
            // Calculate shadow bounds
            float totalSpread = config.spread + config.blurRadius;
            shadowContainer.style.left = config.offsetX - totalSpread;
            shadowContainer.style.top = config.offsetY - totalSpread;
            shadowContainer.style.right = -(config.offsetX + totalSpread);
            shadowContainer.style.bottom = -(config.offsetY + totalSpread);
            
            // Create shadow layers (simulating blur with multiple semi-transparent layers)
            int layerCount = Mathf.Max(1, Mathf.CeilToInt(config.blurRadius / 2f));
            
            for (int i = layerCount - 1; i >= 0; i--)
            {
                var shadowLayer = new VisualElement();
                shadowLayer.name = $"yucp-shadow-layer-{i}";
                shadowLayer.style.position = Position.Absolute;
                shadowLayer.pickingMode = PickingMode.Ignore;
                
                // Calculate layer size (smaller layers = closer to element)
                float layerSpread = (i + 1) * (config.blurRadius / layerCount);
                float layerAlpha = config.color.a * (0.3f + (0.2f * (layerCount - i) / layerCount));
                
                shadowLayer.style.left = totalSpread - layerSpread;
                shadowLayer.style.top = totalSpread - layerSpread + config.offsetY;
                shadowLayer.style.right = -(totalSpread - layerSpread);
                shadowLayer.style.bottom = -(totalSpread - layerSpread - config.offsetY);
                
                shadowLayer.style.backgroundColor = new Color(
                    config.color.r, 
                    config.color.g, 
                    config.color.b, 
                    layerAlpha
                );
                
                // Apply border radius to match element
                shadowLayer.style.borderTopLeftRadius = config.borderRadius - (layerSpread * 0.5f);
                shadowLayer.style.borderTopRightRadius = config.borderRadius - (layerSpread * 0.5f);
                shadowLayer.style.borderBottomLeftRadius = config.borderRadius - (layerSpread * 0.5f);
                shadowLayer.style.borderBottomRightRadius = config.borderRadius - (layerSpread * 0.5f);
                
                shadowContainer.Insert(0, shadowLayer);
            }
            
            // Insert shadow container as first child (will be behind content)
            element.Insert(0, shadowContainer);
            
            return shadowContainer;
        }
        
        /// <summary>
        /// Adds a box shadow with default configuration
        /// </summary>
        public static VisualElement AddBoxShadow(VisualElement element)
        {
            return AddBoxShadow(element, BoxShadowConfig.Default);
        }
        
        /// <summary>
        /// Removes box shadow from an element
        /// </summary>
        public static void RemoveBoxShadow(VisualElement element)
        {
            if (element == null)
                return;
            
            var shadowContainer = element.Q<VisualElement>("yucp-box-shadow-container");
            if (shadowContainer != null)
            {
                shadowContainer.RemoveFromHierarchy();
            }
        }
        
        /// <summary>
        /// Updates existing box shadow with new configuration
        /// </summary>
        public static void UpdateBoxShadow(VisualElement element, BoxShadowConfig config)
        {
            RemoveBoxShadow(element);
            AddBoxShadow(element, config);
        }
        
        /// <summary>
        /// Adds hover effect that enhances the box shadow on mouse over
        /// </summary>
        public static void AddHoverShadowEffect(VisualElement element, BoxShadowConfig normalConfig, BoxShadowConfig hoverConfig)
        {
            VisualElement shadowContainer = AddBoxShadow(element, normalConfig);
            
            if (shadowContainer == null)
                return;
            
            element.RegisterCallback<MouseEnterEvent>(evt =>
            {
                UpdateBoxShadow(element, hoverConfig);
            });
            
            element.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                UpdateBoxShadow(element, normalConfig);
            });
        }
    }
}

