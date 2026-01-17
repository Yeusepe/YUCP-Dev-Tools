namespace YUCP.Motion.Core
{
    /// <summary>
    /// Time utilities (Unity-free). Time source is provided by tick drivers.
    /// </summary>
    public static class TimeUtils
    {
        /// <summary>
        /// Clamps elapsed time to duration and returns normalized progress [0, 1].
        /// </summary>
        public static float GetProgress(float elapsed, float duration)
        {
            if (duration <= 0.0f)
                return 1.0f;
            
            return MathUtils.Clamp01(elapsed / duration);
        }
        
        /// <summary>
        /// Checks if animation is complete based on elapsed time and duration.
        /// </summary>
        public static bool IsComplete(float elapsed, float duration)
        {
            return elapsed >= duration;
        }
        
        /// <summary>
        /// Converts seconds to milliseconds.
        /// </summary>
        public static float SecondsToMilliseconds(float seconds)
        {
            return seconds * 1000.0f;
        }
        
        /// <summary>
        /// Converts milliseconds to seconds.
        /// </summary>
        public static float MillisecondsToSeconds(float milliseconds)
        {
            return milliseconds / 1000.0f;
        }
    }
}
