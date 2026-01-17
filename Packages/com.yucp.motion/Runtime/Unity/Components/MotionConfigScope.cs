using System.Collections.Generic;
using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Motion configuration scope tied to a VisualElement subtree.
    /// Stores reducedMotion, defaultTransition, etc.
    /// Similar to motion-main's MotionConfig.
    /// </summary>
    public class MotionConfigScope
    {
        private static readonly Dictionary<VisualElement, MotionConfigScope> s_ScopeCache = new Dictionary<VisualElement, MotionConfigScope>();
        
        private readonly VisualElement m_Root;
        private bool m_ReducedMotion;
        private Transition m_DefaultTransition;
        
        /// <summary>
        /// Whether reduced motion is enabled.
        /// </summary>
        public bool ReducedMotion
        {
            get => m_ReducedMotion;
            set => m_ReducedMotion = value;
        }
        
        /// <summary>
        /// Default transition for animations.
        /// </summary>
        public Transition DefaultTransition
        {
            get => m_DefaultTransition;
            set => m_DefaultTransition = value;
        }
        
        private MotionConfigScope(VisualElement root)
        {
            m_Root = root;
            m_ReducedMotion = false;
            m_DefaultTransition = Transition.Default;
        }
        
        /// <summary>
        /// Finds the MotionConfigScope for a VisualElement by walking up the parent tree.
        /// </summary>
        public static MotionConfigScope FindFor(VisualElement element)
        {
            if (element == null)
                return null;
            
            // Check cache first
            if (s_ScopeCache.TryGetValue(element, out var cached))
            {
                return cached;
            }
            
            // Walk up parent tree
            VisualElement current = element;
            while (current != null)
            {
                // Check if this element has a scope attached
                if (current.userData is MotionConfigScope scope)
                {
                    // Cache for this element and all children
                    CacheScopeForSubtree(element, scope);
                    return scope;
                }
                
                current = current.parent;
            }
            
            return null;
        }
        
        /// <summary>
        /// Creates a new MotionConfigScope and attaches it to an element.
        /// </summary>
        public static MotionConfigScope Create(VisualElement element)
        {
            var scope = new MotionConfigScope(element);
            element.userData = scope;
            s_ScopeCache[element] = scope;
            return scope;
        }
        
        /// <summary>
        /// Caches scope for element and its subtree.
        /// </summary>
        private static void CacheScopeForSubtree(VisualElement element, MotionConfigScope scope)
        {
            s_ScopeCache[element] = scope;
            // Note: In a full implementation, we'd cache for all descendants too
            // For now, we cache on-demand as elements are accessed
        }
        
        /// <summary>
        /// Removes the scope from an element.
        /// </summary>
        public static void Remove(VisualElement element)
        {
            if (element.userData is MotionConfigScope)
            {
                element.userData = null;
            }
            s_ScopeCache.Remove(element);
        }
    }
}
