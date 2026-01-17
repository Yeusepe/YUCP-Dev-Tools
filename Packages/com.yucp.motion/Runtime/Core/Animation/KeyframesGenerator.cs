using System;
using YUCP.Motion.Core;

namespace YUCP.Motion.Core.Animation
{
    /// <summary>
    /// Keyframes generator that interpolates between keyframe values.
    /// Similar to motion-main's keyframes generator.
    /// </summary>
    public class KeyframesGenerator<T> : IKeyframeGenerator<T>
    {
        private readonly T[] m_Keyframes;
        private readonly float[] m_AbsoluteTimes;
        private readonly float m_Duration;
        private readonly Func<float, T> m_Interpolator;
        
        public float? CalculatedDuration => m_Duration;
        
        /// <summary>
        /// Creates a keyframes generator.
        /// </summary>
        public KeyframesGenerator(ValueAnimationOptions<T> options)
        {
            m_Duration = options.Duration;
            m_Keyframes = options.Keyframes ?? throw new ArgumentNullException(nameof(options.Keyframes));
            
            if (m_Keyframes.Length == 0)
            {
                throw new ArgumentException("Keyframes array cannot be empty", nameof(options.Keyframes));
            }
            
            // Convert offsets to absolute times
            m_AbsoluteTimes = ConvertOffsetsToTimes(
                options.Times ?? CreateDefaultOffsets(m_Keyframes.Length),
                m_Duration
            );
            
            // Create interpolator
            m_Interpolator = CreateInterpolator(m_AbsoluteTimes, m_Keyframes, options.Easing);
        }
        
        public AnimationState<T> Next(float t)
        {
            var state = new AnimationState<T>
            {
                Done = t >= m_Duration,
                Value = m_Interpolator(MathUtils.Clamp(0.0f, m_Duration, t))
            };
            
            return state;
        }
        
        private float[] CreateDefaultOffsets(int count)
        {
            var offsets = new float[count];
            if (count == 1)
            {
                offsets[0] = 0.0f;
            }
            else
            {
                float step = 1.0f / (count - 1);
                for (int i = 0; i < count; i++)
                {
                    offsets[i] = i * step;
                }
            }
            return offsets;
        }
        
        private float[] ConvertOffsetsToTimes(float[] offsets, float duration)
        {
            var times = new float[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
            {
                times[i] = offsets[i] * duration;
            }
            return times;
        }
        
        private Func<float, T> CreateInterpolator(float[] times, T[] values, EasingType easing)
        {
            // Simple linear interpolation for now
            // TODO: Support easing per segment and complex value types
            return (t) =>
            {
                if (times.Length == 1)
                {
                    return values[0];
                }
                
                // Find the segment
                int segment = 0;
                for (int i = 0; i < times.Length - 1; i++)
                {
                    if (t < times[i + 1])
                    {
                        segment = i;
                        break;
                    }
                    segment = i;
                }
                
                if (segment >= times.Length - 1)
                {
                    return values[values.Length - 1];
                }
                
                // Calculate progress in segment
                float segmentStart = times[segment];
                float segmentEnd = times[segment + 1];
                float segmentDuration = segmentEnd - segmentStart;
                
                if (segmentDuration <= 0.0f)
                {
                    return values[segment];
                }
                
                float progress = (t - segmentStart) / segmentDuration;
                progress = MathUtils.Clamp01(progress);
                
                // Apply easing
                float easedProgress = Easing.Apply(easing, progress);
                
                // Interpolate
                return ValueTypeRegistry.Mix(values[segment], values[segment + 1], easedProgress);
            };
        }
    }
}
