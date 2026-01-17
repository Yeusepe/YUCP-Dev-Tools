namespace YUCP.Motion.Core
{
    /// <summary>
    /// Math utilities (Unity-free).
    /// </summary>
    public static class MathUtils
    {
        public const float Epsilon = 0.0001f;
        
        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
        
        public static float Clamp01(float value)
        {
            return Clamp(value, 0.0f, 1.0f);
        }
        
        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Clamp01(t);
        }
        
        public static bool Approximately(float a, float b)
        {
            float diff = a - b;
            return diff >= -Epsilon && diff <= Epsilon;
        }
        
        /// <summary>
        /// Wraps a value within a range [min, max).
        /// </summary>
        public static float Wrap(float min, float max, float value)
        {
            float rangeSize = max - min;
            return ((((value - min) % rangeSize) + rangeSize) % rangeSize) + min;
        }
        
        /// <summary>
        /// Progress within given range.
        /// Given a lower limit and an upper limit, returns the progress
        /// (expressed as a number 0-1) represented by the given value.
        /// </summary>
        public static float Progress(float from, float to, float value)
        {
            float toFromDifference = to - from;
            return toFromDifference == 0.0f ? 1.0f : (value - from) / toFromDifference;
        }
        
        /// <summary>
        /// Mixes (interpolates) between two numbers.
        /// </summary>
        public static float Mix(float from, float to, float progress)
        {
            return from + (to - from) * progress;
        }
    }
}
