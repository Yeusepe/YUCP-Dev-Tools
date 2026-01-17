using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Handle for controlling motion on a VisualElement.
    /// Handles registration/unregistration, attach/detach event hooks to avoid leaks.
    /// </summary>
    public class MotionHandle
    {
        private readonly VisualElement m_Element;
        private readonly MotionController m_Controller;
        private readonly MotionViewAdapter m_Adapter;
        private bool m_Disposed;
        
        internal MotionHandle(VisualElement element, MotionTargets? initial)
        {
            m_Element = element;
            
            // Create controller and adapter
            ElementId id = ElementId.Create();
            m_Controller = new MotionController(id);
            m_Adapter = new MotionViewAdapter(element);
            
            // Register with motion system
            MotionSystem system = TickSystem.GetMotionSystem();
            system.Register(m_Controller);
            
            // Register handle for adapter updates
            MotionHandleRegistry.Register(m_Controller, this);
            
            // Hook attach/detach events to avoid leaks
            element.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            element.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            
            // Apply initial targets if provided
            if (initial.HasValue)
            {
                Animate(initial.Value, Transition.Default);
            }
            
            // Update adapter from controller initially
            UpdateAdapter();
        }
        
        /// <summary>
        /// Animates to the given targets with the specified transition.
        /// </summary>
        public void Animate(MotionTargets targets, Transition transition)
        {
            if (m_Disposed)
                return;
            
            m_Controller.AnimateTo(targets, transition);
        }
        
        /// <summary>
        /// Animates to the given targets with default transition.
        /// </summary>
        public void Animate(MotionTargets targets)
        {
            Animate(targets, Transition.Default);
        }
        
        /// <summary>
        /// Gets the current transform state.
        /// </summary>
        public TransformState GetCurrentTransform()
        {
            return m_Controller.GetTransform();
        }
        
        /// <summary>
        /// Disposes the handle, unregistering and unhooking events.
        /// </summary>
        public void Dispose()
        {
            if (m_Disposed)
                return;
            
            m_Disposed = true;
            
            // Unregister from motion system
            MotionSystem system = TickSystem.GetMotionSystem();
            system.Unregister(m_Controller);
            
            // Unregister handle
            MotionHandleRegistry.Unregister(m_Controller);
            
            // Unhook events
            m_Element.UnregisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            m_Element.UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }
        
        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            // Re-register if needed
            MotionSystem system = TickSystem.GetMotionSystem();
            if (!system.Register(m_Controller))
            {
                // Already registered, just update adapter
                UpdateAdapter();
            }
        }
        
        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // Unregister when detached
            MotionSystem system = TickSystem.GetMotionSystem();
            system.Unregister(m_Controller);
        }
        
        private void UpdateAdapter()
        {
            m_Adapter.UpdateFromController(m_Controller);
            m_Adapter.Apply();
        }
        
        // Internal update called by tick system
        internal void Update()
        {
            if (m_Disposed)
                return;
            
            UpdateAdapter();
        }
    }
}
