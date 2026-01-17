using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Static API for attaching motion to any VisualElement.
    /// Works for Inspector, EditorWindow, and Runtime UIDocument contexts.
    /// </summary>
    public static class Motion
    {
        private static bool s_Initialized;
        
        /// <summary>
        /// Initializes the motion system. Should be called once at startup.
        /// Editor bootstrapping is handled separately in the Editor assembly.
        /// </summary>
        public static void Initialize()
        {
            if (s_Initialized)
                return;
            
            // Initialize frame loop manager first
            FrameLoopManager.Initialize();
            
            // Runtime initialization - editor bootstrap handles editor mode
            if (!Application.isEditor || Application.isPlaying)
            {
                var runtimeDriver = RuntimeTickDriver.GetOrCreate();
                TickSystem.Initialize(runtimeDriver);
            }
            
            // Register adapter update callback
            TickSystem.AfterTick += MotionHandleRegistry.UpdateAll;
            
            s_Initialized = true;
        }
        
        /// <summary>
        /// Attaches motion to a VisualElement. Returns a handle for controlling the animation.
        /// </summary>
        public static MotionHandle Attach(VisualElement element, MotionTargets? initial = null)
        {
            if (!s_Initialized)
                Initialize();
            
            return new MotionHandle(element, initial);
        }
    }
}
