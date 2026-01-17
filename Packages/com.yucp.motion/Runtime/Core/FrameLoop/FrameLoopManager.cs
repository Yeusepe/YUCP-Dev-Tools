using System;

namespace YUCP.Motion.Core
{
    /// <summary>
    /// Global frame loop manager that coordinates RenderBatcher with TickSystem.
    /// Similar to motion-main's frame loop integration.
    /// </summary>
    public static class FrameLoopManager
    {
        private static RenderBatcher s_Batcher;
        private static bool s_Initialized;
        
        /// <summary>
        /// Gets the global render batcher.
        /// </summary>
        public static RenderBatcher Batcher
        {
            get
            {
                if (s_Batcher == null)
                {
                    Initialize();
                }
                return s_Batcher;
            }
        }
        
        /// <summary>
        /// Initializes the frame loop manager.
        /// </summary>
        public static void Initialize()
        {
            if (s_Initialized)
                return;
            
            // Create render batcher with schedule function
            s_Batcher = new RenderBatcher(ScheduleNextBatch, allowKeepAlive: true);
            
            // Integrate with TickSystem
            TickSystem.AfterTick += OnAfterTick;
            
            s_Initialized = true;
        }
        
        /// <summary>
        /// Shuts down the frame loop manager.
        /// </summary>
        public static void Shutdown()
        {
            TickSystem.AfterTick -= OnAfterTick;
            s_Batcher = null;
            s_Initialized = false;
        }
        
        /// <summary>
        /// Schedules the next batch (called by RenderBatcher).
        /// </summary>
        private static void ScheduleNextBatch(Action processBatch)
        {
            // Schedule to run on next frame via tick system
            // The processBatch will be called during OnAfterTick
            // Store it for execution
            s_PendingBatch = processBatch;
        }
        
        private static Action s_PendingBatch;
        
        /// <summary>
        /// Called after each tick to update SyncTime and process render steps.
        /// </summary>
        private static void OnAfterTick()
        {
            if (s_Batcher == null)
                return;
            
            // Get frame data from tick system
            var motionSystem = TickSystem.GetMotionSystem();
            FrameData frameData;
            
            // Get actual frame data from driver
            var driver = TickSystem.GetDriver();
            if (driver != null)
            {
                frameData = driver.GetFrameData();
            }
            else
            {
                // Fallback: use default frame data
                frameData = new FrameData(16.67f, System.DateTime.UtcNow.Ticks / 10000.0, motionSystem != null);
            }
            
            // Update SyncTime with current frame data
            SyncTime.Set(frameData);
            
            // Process pending batch if scheduled
            if (s_PendingBatch != null)
            {
                var batch = s_PendingBatch;
                s_PendingBatch = null;
                batch();
            }
            else if (!s_Batcher.State.IsProcessing)
            {
                // Process render batcher
                s_Batcher.ProcessBatch(frameData.Now);
            }
            
            // Clear SyncTime after frame
            SyncTime.Clear();
        }
    }
}
