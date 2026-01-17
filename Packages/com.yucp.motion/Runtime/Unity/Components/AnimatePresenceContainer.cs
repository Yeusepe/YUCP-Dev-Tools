using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// AnimatePresence container that intercepts add/remove, marks exiting, plays exit animation, removes after completion.
    /// Similar to motion-main's AnimatePresence.
    /// </summary>
    public class AnimatePresenceContainer : VisualElement
    {
        private readonly Dictionary<VisualElement, bool> m_ExitingElements = new Dictionary<VisualElement, bool>();
        private readonly Dictionary<VisualElement, MotionHandle> m_ElementHandles = new Dictionary<VisualElement, MotionHandle>();
        private readonly Dictionary<VisualElement, System.Action> m_ExitAnimations = new Dictionary<VisualElement, System.Action>();
        
        /// <summary>
        /// Creates a new AnimatePresenceContainer.
        /// </summary>
        public AnimatePresenceContainer()
        {
            // Override hierarchy changed to intercept add/remove
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }
        
        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            // Hook into hierarchy changes
            // Note: Unity UI Toolkit doesn't have a direct hierarchy changed event
            // We'll need to manually track adds/removes via a wrapper API
        }
        
        /// <summary>
        /// Adds a child with presence tracking.
        /// </summary>
        public void AddChildWithPresence(VisualElement child, MotionTargets? exitAnimation = null)
        {
            if (child == null)
                return;
            
            // Create motion handle for the child
            var handle = Motion.Attach(child);
            m_ElementHandles[child] = handle;
            
            // Store exit animation if provided
            if (exitAnimation.HasValue)
            {
                m_ExitAnimations[child] = () =>
                {
                    handle.Animate(exitAnimation.Value, Transition.Default);
                };
            }
            
            // Add to hierarchy
            Add(child);
        }
        
        /// <summary>
        /// Removes a child with exit animation.
        /// </summary>
        public void RemoveChildWithPresence(VisualElement child)
        {
            if (child == null || !Contains(child))
                return;
            
            // Mark as exiting
            m_ExitingElements[child] = false;
            
            // Play exit animation if available
            if (m_ExitAnimations.TryGetValue(child, out var exitAnimation))
            {
                exitAnimation();
                
                // Wait for animation to complete
                // TODO: Integrate with animation completion callbacks
                // For now, use a simple delay
                schedule.Execute(() =>
                {
                    CompleteExit(child);
                }).ExecuteLater(250); // Default exit duration
            }
            else
            {
                // No exit animation, remove immediately
                CompleteExit(child);
            }
        }
        
        /// <summary>
        /// Completes the exit and removes the element.
        /// </summary>
        private void CompleteExit(VisualElement child)
        {
            m_ExitingElements.Remove(child);
            
            if (m_ElementHandles.TryGetValue(child, out var handle))
            {
                handle.Dispose();
                m_ElementHandles.Remove(child);
            }
            
            m_ExitAnimations.Remove(child);
            
            // Remove from hierarchy
            Remove(child);
        }
        
        /// <summary>
        /// Checks if an element is currently exiting.
        /// </summary>
        public bool IsExiting(VisualElement element)
        {
            return m_ExitingElements.ContainsKey(element);
        }
        
        /// <summary>
        /// Cleans up all presence tracking.
        /// </summary>
        public void Cleanup()
        {
            foreach (var handle in m_ElementHandles.Values)
            {
                handle.Dispose();
            }
            
            m_ElementHandles.Clear();
            m_ExitingElements.Clear();
            m_ExitAnimations.Clear();
        }
    }
}
