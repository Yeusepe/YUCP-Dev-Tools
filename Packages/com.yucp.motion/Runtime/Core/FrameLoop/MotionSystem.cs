namespace YUCP.Motion.Core
{
    /// <summary>
    /// Motion system that iterates controllers with an index loop. Single tick for all controllers.
    /// </summary>
    public class MotionSystem
    {
        private readonly ControllerRegistry m_Registry = new ControllerRegistry();
        
        /// <summary>
        /// Registers a controller.
        /// </summary>
        public bool Register(MotionController controller)
        {
            return m_Registry.Register(controller);
        }
        
        /// <summary>
        /// Unregisters a controller.
        /// </summary>
        public bool Unregister(MotionController controller)
        {
            return m_Registry.Unregister(controller);
        }
        
        /// <summary>
        /// Ticks all registered controllers. Called once per frame.
        /// Safe for mutations during iteration.
        /// </summary>
        public void Tick(FrameData frame)
        {
            // Begin iteration - mutations will be queued
            m_Registry.BeginIteration();
            
            try
            {
                // Use index loop for best performance (no allocations)
                for (int i = 0; i < m_Registry.Count; i++)
                {
                    MotionController controller = m_Registry[i];
                    controller.Tick(frame);
                }
            }
            finally
            {
                // End iteration - apply pending adds/removes
                m_Registry.EndIteration();
            }
        }
        
        /// <summary>
        /// Gets the number of registered controllers.
        /// </summary>
        public int ControllerCount => m_Registry.Count;
    }
}
