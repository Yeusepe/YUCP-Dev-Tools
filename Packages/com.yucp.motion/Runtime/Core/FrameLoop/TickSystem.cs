namespace YUCP.Motion.Core
{
    /// <summary>
    /// Global tick system that owns the list of controllers and calls Tick(frame) once per frame.
    /// </summary>
    public static class TickSystem
    {
        private static ITickDriver s_Driver;
        private static MotionSystem s_MotionSystem;
        private static bool s_Initialized;
        
        /// <summary>
        /// Event fired after each tick. Subscribers can update adapters or perform other post-tick work.
        /// </summary>
        public static event System.Action AfterTick;
        
        /// <summary>
        /// Initializes the tick system with a driver. Idempotent - safe to call multiple times.
        /// </summary>
        public static void Initialize(ITickDriver driver)
        {
            if (s_Initialized)
            {
                // Already initialized - switch driver if different
                if (s_Driver != driver)
                {
                    Shutdown();
                }
                else
                {
                    return; // Same driver, no-op
                }
            }
            
            s_Driver = driver;
            s_MotionSystem = new MotionSystem();
            s_Initialized = true;
        }
        
        /// <summary>
        /// Shuts down the tick system.
        /// </summary>
        public static void Shutdown()
        {
            s_Driver = null;
            s_MotionSystem = null;
            s_Initialized = false;
            AfterTick = null; // Clear all subscribers
        }
        
        /// <summary>
        /// Ticks the system. Should be called once per frame by the driver.
        /// </summary>
        public static void Tick()
        {
            if (!s_Initialized || s_Driver == null || !s_Driver.IsActive || s_MotionSystem == null)
                return;
            
            FrameData frame = s_Driver.GetFrameData();
            s_MotionSystem.Tick(frame);
            
            // Notify subscribers after tick
            AfterTick?.Invoke();
        }
        
        /// <summary>
        /// Gets the motion system instance.
        /// </summary>
        public static MotionSystem GetMotionSystem()
        {
            return s_MotionSystem;
        }
        
        /// <summary>
        /// Gets the current tick driver.
        /// </summary>
        internal static ITickDriver GetDriver()
        {
            return s_Driver;
        }
        
        /// <summary>
        /// Gets the current frame data (for frame loop integration).
        /// </summary>
        internal static FrameData GetFrameData()
        {
            if (s_Driver != null)
            {
                return s_Driver.GetFrameData();
            }
            return new FrameData(0.0f, System.DateTime.UtcNow.Ticks / 10000.0, false);
        }
    }
}
