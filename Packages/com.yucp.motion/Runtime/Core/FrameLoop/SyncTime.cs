namespace YUCP.Motion.Core
{
    /// <summary>
    /// Synchronous time source for consistent time within a frame.
    /// Similar to motion-main's sync-time.ts
    /// </summary>
    public static class SyncTime
    {
        private static double? s_Now;
        private static FrameData s_FrameData;
        
        /// <summary>
        /// Gets the current time (synchronized within a frame).
        /// </summary>
        public static double Now()
        {
            if (s_Now == null)
            {
                s_Now = s_FrameData.IsProcessing ? s_FrameData.Now : System.DateTime.UtcNow.Ticks / 10000.0;
            }
            
            return s_Now.Value;
        }
        
        /// <summary>
        /// Sets the current time (called by frame loop).
        /// </summary>
        public static void Set(FrameData frameData)
        {
            s_FrameData = frameData;
            s_Now = frameData.Now;
        }
        
        /// <summary>
        /// Clears the cached time (called after frame processing).
        /// </summary>
        internal static void Clear()
        {
            s_Now = null;
        }
    }
}
