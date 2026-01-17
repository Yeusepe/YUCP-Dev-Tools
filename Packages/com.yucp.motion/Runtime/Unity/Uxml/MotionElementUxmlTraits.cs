using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// UXML traits for MotionElement. Parses attributes once in Init, never in hot path.
    /// </summary>
    public class MotionElementUxmlTraits : UxmlTraits
    {
        private UxmlStringAttributeDescription m_Animate = new UxmlStringAttributeDescription 
        { 
            name = "animate", 
            defaultValue = "" 
        };
        
        private UxmlFloatAttributeDescription m_TransitionDuration = new UxmlFloatAttributeDescription 
        { 
            name = "transition-duration", 
            defaultValue = 0.25f 
        };
        
        private UxmlStringAttributeDescription m_TransitionEase = new UxmlStringAttributeDescription 
        { 
            name = "transition-ease", 
            defaultValue = "easeOut" 
        };
        
        public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
        {
            base.Init(ve, bag, cc);
            
            if (ve is MotionElement motionElement)
            {
                // Parse animate attribute (x:100; y:10; scale:1.1; opacity:0.9; bg:#ff00ffaa)
                string animateStr = m_Animate.GetValueFromBag(bag, cc);
                MotionTargets targets = ParseAnimateAttribute(animateStr);
                
                // Parse transition
                float duration = m_TransitionDuration.GetValueFromBag(bag, cc);
                string easeStr = m_TransitionEase.GetValueFromBag(bag, cc);
                EasingType easing = ParseEasingType(easeStr);
                Transition transition = new Transition(duration, easing);
                
                // Apply initial animation if targets were provided
                if (!string.IsNullOrEmpty(animateStr))
                {
                    motionElement.Animate(targets, transition);
                }
            }
        }
        
        private static MotionTargets ParseAnimateAttribute(string animateStr)
        {
            MotionTargets targets = MotionTargets.Empty;
            
            if (string.IsNullOrEmpty(animateStr))
                return targets;
            
            // Parse format: "x:100; y:10; scale:1.1; opacity:0.9; bg:#ff00ffaa"
            string[] parts = animateStr.Split(';');
            
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;
                
                int colonIndex = trimmed.IndexOf(':');
                if (colonIndex < 0)
                    continue;
                
                string key = trimmed.Substring(0, colonIndex).Trim().ToLower();
                string value = trimmed.Substring(colonIndex + 1).Trim();
                
                switch (key)
                {
                    case "x":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
                        {
                            targets.HasX = true;
                            targets.X = x;
                        }
                        break;
                    
                    case "y":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                        {
                            targets.HasY = true;
                            targets.Y = y;
                        }
                        break;
                    
                    case "scale":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
                        {
                            targets.HasScaleX = true;
                            targets.ScaleX = scale;
                            targets.HasScaleY = true;
                            targets.ScaleY = scale;
                        }
                        break;
                    
                    case "opacity":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float opacity))
                        {
                            targets.HasOpacity = true;
                            targets.Opacity = opacity;
                        }
                        break;
                    
                    case "bg":
                        // Parse hex color: #rrggbbaa or #rrggbb
                        ColorRGBA color = ParseHexColor(value);
                        if (color.A > 0) // Valid color parsed
                        {
                            targets.HasBgRGBA = true;
                            targets.BgRGBA = color;
                        }
                        break;
                }
            }
            
            return targets;
        }
        
        private static ColorRGBA ParseHexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || !hex.StartsWith("#"))
                return default;
            
            hex = hex.Substring(1); // Remove #
            
            if (hex.Length == 6)
            {
                // #rrggbb
                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                {
                    float r = ((rgb >> 16) & 0xFF) / 255f;
                    float g = ((rgb >> 8) & 0xFF) / 255f;
                    float b = (rgb & 0xFF) / 255f;
                    return new ColorRGBA(r, g, b, 1.0f);
                }
            }
            else if (hex.Length == 8)
            {
                // #rrggbbaa
                if (long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long rgba))
                {
                    float r = ((rgba >> 24) & 0xFF) / 255f;
                    float g = ((rgba >> 16) & 0xFF) / 255f;
                    float b = ((rgba >> 8) & 0xFF) / 255f;
                    float a = (rgba & 0xFF) / 255f;
                    return new ColorRGBA(r, g, b, a);
                }
            }
            
            return default;
        }
        
        private static EasingType ParseEasingType(string easeStr)
        {
            if (string.IsNullOrEmpty(easeStr))
                return EasingType.EaseOut;
            
            easeStr = easeStr.Trim().ToLower();
            
            return easeStr switch
            {
                "linear" => EasingType.Linear,
                "easein" => EasingType.EaseIn,
                "easeout" => EasingType.EaseOut,
                "easeinout" => EasingType.EaseInOut,
                "easeinquad" => EasingType.EaseInQuad,
                "easeoutquad" => EasingType.EaseOutQuad,
                "easeinoutquad" => EasingType.EaseInOutQuad,
                "easeincubic" => EasingType.EaseInCubic,
                "easeoutcubic" => EasingType.EaseOutCubic,
                "easeinoutcubic" => EasingType.EaseInOutCubic,
                _ => EasingType.EaseOut
            };
        }
    }
}
