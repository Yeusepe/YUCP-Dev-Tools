using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter.UI.Components
{
    /// <summary>
    /// Represents a single step in the onboarding tour.
    /// </summary>
    public class OnboardingStep
    {
        public string Title;
        public string Description;
        public string TargetElementName; // Name of the UI element to highlight
        public VisualElement TargetElement; // Direct reference (optional override)
        public Action OnStepShown;
        public Action OnStepHidden;
        public string SecondaryActionLabel;
        public Action SecondaryAction;
        
        // Set to true if OnStepShown triggers layout changes (like opening sidebar) that need time to complete
        public bool RequiresLayoutDelay = false;
        
        // Optional: Custom spotlight padding or offset
        public Vector4 SpotlightPadding = new Vector4(10, 10, 10, 10); // Left, Top, Right, Bottom
        
        public OnboardingStep(string title, string description, string targetName = null)
        {
            Title = title;
            Description = description;
            TargetElementName = targetName;
        }
    }
}
