#if UNITY_EDITOR
using UnityEditor;
using YUCP.Motion.Core;
using YUCP.Motion;

namespace YUCP.Motion.Editor
{
    /// <summary>
    /// Editor bootstrap that initializes the motion system in editor mode.
    /// Uses InitializeOnLoad to set up automatically.
    /// </summary>
    [InitializeOnLoad]
    static class MotionEditorBootstrap
    {
        private static EditorTickDriver s_EditorDriver;
        
        static MotionEditorBootstrap()
        {
            // Register adapter update callback
            TickSystem.AfterTick += MotionHandleRegistry.UpdateAll;
            
            // Ensure editor driver is installed
            EnsureEditorDriver();
            
            // Handle play mode state changes
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
        
        private static void EnsureEditorDriver()
        {
            if (s_EditorDriver == null && !EditorApplication.isPlaying)
            {
                s_EditorDriver = new EditorTickDriver();
                TickSystem.Initialize(s_EditorDriver);
            }
        }
        
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            {
                // Play mode entered - editor driver will be inactive, runtime driver takes over
                if (s_EditorDriver != null)
                {
                    s_EditorDriver.Dispose();
                    s_EditorDriver = null;
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                // Back to edit mode - ensure editor driver is active
                EnsureEditorDriver();
            }
        }
    }
}
#endif
