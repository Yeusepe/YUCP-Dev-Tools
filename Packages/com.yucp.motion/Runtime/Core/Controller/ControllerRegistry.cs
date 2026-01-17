using System.Collections.Generic;

namespace YUCP.Motion.Core
{
    /// <summary>
    /// Allocation-free registry for motion controllers. Uses List + HashSet for O(1) membership checks.
    /// No string keys, no boxing. Safe for mutations during iteration using pending add/remove pattern.
    /// </summary>
    public class ControllerRegistry
    {
        private readonly List<MotionController> m_Controllers = new List<MotionController>();
        private readonly HashSet<int> m_ControllerIds = new HashSet<int>();
        private readonly List<MotionController> m_PendingAdds = new List<MotionController>();
        private readonly HashSet<int> m_PendingRemoves = new HashSet<int>();
        private bool m_IsIterating;
        
        public int Count => m_Controllers.Count;
        
        public MotionController this[int index] => m_Controllers[index];
        
        /// <summary>
        /// Registers a controller. Returns true if added, false if already registered.
        /// Safe to call during iteration - will be applied after iteration completes.
        /// </summary>
        public bool Register(MotionController controller)
        {
            int id = controller.Id.Value;
            if (m_ControllerIds.Contains(id) || m_PendingRemoves.Contains(id))
                return false;
            
            if (m_IsIterating)
            {
                m_PendingAdds.Add(controller);
            }
            else
            {
                m_Controllers.Add(controller);
                m_ControllerIds.Add(id);
            }
            return true;
        }
        
        /// <summary>
        /// Unregisters a controller. Returns true if removed, false if not found.
        /// Safe to call during iteration - will be applied after iteration completes.
        /// </summary>
        public bool Unregister(MotionController controller)
        {
            int id = controller.Id.Value;
            if (!m_ControllerIds.Contains(id))
                return false;
            
            if (m_IsIterating)
            {
                m_PendingRemoves.Add(id);
            }
            else
            {
                m_ControllerIds.Remove(id);
                m_Controllers.Remove(controller);
            }
            return true;
        }
        
        /// <summary>
        /// Checks if a controller is registered.
        /// </summary>
        public bool Contains(MotionController controller)
        {
            return m_ControllerIds.Contains(controller.Id.Value);
        }
        
        /// <summary>
        /// Marks the start of iteration. Pending adds/removes will be applied after iteration.
        /// </summary>
        internal void BeginIteration()
        {
            m_IsIterating = true;
        }
        
        /// <summary>
        /// Marks the end of iteration and applies pending adds/removes.
        /// </summary>
        internal void EndIteration()
        {
            m_IsIterating = false;
            
            // Apply pending removes (swap-back removal for O(1))
            foreach (int idToRemove in m_PendingRemoves)
            {
                for (int i = m_Controllers.Count - 1; i >= 0; i--)
                {
                    if (m_Controllers[i].Id.Value == idToRemove)
                    {
                        // Swap with last element and remove
                        int lastIndex = m_Controllers.Count - 1;
                        if (i != lastIndex)
                        {
                            m_Controllers[i] = m_Controllers[lastIndex];
                        }
                        m_Controllers.RemoveAt(lastIndex);
                        m_ControllerIds.Remove(idToRemove);
                        break;
                    }
                }
            }
            m_PendingRemoves.Clear();
            
            // Apply pending adds
            foreach (var controller in m_PendingAdds)
            {
                int id = controller.Id.Value;
                if (!m_ControllerIds.Contains(id))
                {
                    m_Controllers.Add(controller);
                    m_ControllerIds.Add(id);
                }
            }
            m_PendingAdds.Clear();
        }
        
        /// <summary>
        /// Clears all controllers.
        /// </summary>
        public void Clear()
        {
            m_Controllers.Clear();
            m_ControllerIds.Clear();
            m_PendingAdds.Clear();
            m_PendingRemoves.Clear();
        }
    }
}
