using System.Collections.Generic;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Registry for motion handles. Updates adapters after controllers tick.
    /// Safe for mutations during iteration using list with swap-back removal.
    /// </summary>
    public static class MotionHandleRegistry
    {
        private static readonly List<MotionHandle> s_Handles = new List<MotionHandle>();
        private static readonly Dictionary<MotionController, int> s_ControllerToIndex = new Dictionary<MotionController, int>();
        private static readonly Dictionary<MotionHandle, MotionController> s_HandleToController = new Dictionary<MotionHandle, MotionController>();
        private static readonly HashSet<MotionController> s_PendingRemoves = new HashSet<MotionController>();
        
        public static void Register(MotionController controller, MotionHandle handle)
        {
            if (s_ControllerToIndex.ContainsKey(controller))
                return; // Already registered
            
            s_Handles.Add(handle);
            int index = s_Handles.Count - 1;
            s_ControllerToIndex[controller] = index;
            s_HandleToController[handle] = controller;
        }
        
        public static void Unregister(MotionController controller)
        {
            if (!s_ControllerToIndex.TryGetValue(controller, out int index))
                return;
            
            // Mark for removal (will be applied after iteration)
            s_PendingRemoves.Add(controller);
        }
        
        public static void UpdateAll()
        {
            // Apply pending removes first (swap-back removal)
            foreach (var controllerToRemove in s_PendingRemoves)
            {
                if (!s_ControllerToIndex.TryGetValue(controllerToRemove, out int index))
                    continue;
                
                var handleToRemove = s_Handles[index];
                
                // Swap with last element and remove
                int lastIndex = s_Handles.Count - 1;
                if (index != lastIndex)
                {
                    var lastHandle = s_Handles[lastIndex];
                    s_Handles[index] = lastHandle;
                    
                    // Update index map for the swapped handle
                    if (s_HandleToController.TryGetValue(lastHandle, out var swappedController))
                    {
                        s_ControllerToIndex[swappedController] = index;
                    }
                }
                
                s_Handles.RemoveAt(lastIndex);
                
                // Remove from maps
                s_ControllerToIndex.Remove(controllerToRemove);
                s_HandleToController.Remove(handleToRemove);
            }
            s_PendingRemoves.Clear();
            
            // Update all handles (safe to iterate now)
            for (int i = 0; i < s_Handles.Count; i++)
            {
                s_Handles[i]?.Update();
            }
        }
    }
}
