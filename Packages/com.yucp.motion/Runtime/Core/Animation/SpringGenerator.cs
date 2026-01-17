using System;
using YUCP.Motion.Core;

namespace YUCP.Motion.Core.Animation
{
    /// <summary>
    /// Spring generator that implements spring physics.
    /// Based on motion-main's spring generator.
    /// </summary>
    public class SpringGenerator : IKeyframeGenerator<float>
    {
        private readonly float m_Origin;
        private readonly float m_Target;
        private readonly SpringOptions m_Options;
        private readonly float m_DampingRatio;
        private readonly float m_UndampedAngularFreq;
        private readonly float m_InitialDelta;
        private readonly float m_InitialVelocity;
        private readonly float m_RestSpeed;
        private readonly float m_RestDelta;
        private readonly bool m_IsResolvedFromDuration;
        private readonly float? m_Duration;
        
        public float? CalculatedDuration => m_IsResolvedFromDuration ? m_Duration : null;
        
        /// <summary>
        /// Creates a spring generator.
        /// </summary>
        public SpringGenerator(float origin, float target, SpringOptions options)
        {
            m_Origin = origin;
            m_Target = target;
            m_Options = options;
            
            m_InitialDelta = target - origin;
            m_InitialVelocity = -TimeUtils.MillisecondsToSeconds(options.Velocity);
            
            m_DampingRatio = options.Damping / (2.0f * (float)System.Math.Sqrt(options.Stiffness * options.Mass));
            m_UndampedAngularFreq = TimeUtils.MillisecondsToSeconds((float)System.Math.Sqrt(options.Stiffness / options.Mass));
            
            // Determine rest thresholds
            bool isGranularScale = System.Math.Abs(m_InitialDelta) < 5.0f;
            m_RestSpeed = options.RestSpeed > 0.0f 
                ? options.RestSpeed 
                : (isGranularScale ? 0.01f : 2.0f);
            m_RestDelta = options.RestDelta > 0.0f 
                ? options.RestDelta 
                : (isGranularScale ? 0.005f : 0.5f);
            
            m_IsResolvedFromDuration = false;
            m_Duration = null;
        }
        
        public AnimationState<float> Next(float t)
        {
            float timeSeconds = TimeUtils.MillisecondsToSeconds(t);
            float current = ResolveSpring(timeSeconds);
            
            bool done;
            if (m_IsResolvedFromDuration && m_Duration.HasValue)
            {
                done = t >= m_Duration.Value;
            }
            else
            {
                // Check rest conditions
                float currentVelocity = t == 0.0f ? m_InitialVelocity : 0.0f;
                
                if (m_DampingRatio < 1.0f)
                {
                    // Underdamped - calculate velocity
                    currentVelocity = CalculateVelocity(timeSeconds, current);
                }
                
                bool isBelowVelocity = System.Math.Abs(currentVelocity) <= m_RestSpeed;
                bool isBelowDisplacement = System.Math.Abs(m_Target - current) <= m_RestDelta;
                done = isBelowVelocity && isBelowDisplacement;
            }
            
            return new AnimationState<float>
            {
                Done = done,
                Value = done ? m_Target : current
            };
        }
        
        private float ResolveSpring(float t)
        {
            if (m_DampingRatio < 1.0f)
            {
                // Underdamped spring
                float angularFreq = CalcAngularFreq(m_UndampedAngularFreq, m_DampingRatio);
                float envelope = (float)System.Math.Exp(-m_DampingRatio * m_UndampedAngularFreq * t);
                
                return m_Target - envelope * (
                    ((m_InitialVelocity + m_DampingRatio * m_UndampedAngularFreq * m_InitialDelta) / angularFreq) *
                    (float)System.Math.Sin(angularFreq * t) +
                    m_InitialDelta * (float)System.Math.Cos(angularFreq * t)
                );
            }
            else if (System.Math.Abs(m_DampingRatio - 1.0f) < 0.001f)
            {
                // Critically damped spring
                return m_Target - (float)System.Math.Exp(-m_UndampedAngularFreq * t) *
                    (m_InitialDelta + (m_InitialVelocity + m_UndampedAngularFreq * m_InitialDelta) * t);
            }
            else
            {
                // Overdamped spring
                float dampedAngularFreq = m_UndampedAngularFreq * (float)System.Math.Sqrt(m_DampingRatio * m_DampingRatio - 1.0f);
                float envelope = (float)System.Math.Exp(-m_DampingRatio * m_UndampedAngularFreq * t);
                float freqForT = System.Math.Min(dampedAngularFreq * t, 300.0f);
                
                return m_Target - (envelope * (
                    (m_InitialVelocity + m_DampingRatio * m_UndampedAngularFreq * m_InitialDelta) *
                    (float)System.Math.Sinh(freqForT) +
                    dampedAngularFreq * m_InitialDelta * (float)System.Math.Cosh(freqForT)
                )) / dampedAngularFreq;
            }
        }
        
        private float CalcAngularFreq(float undampedAngularFreq, float dampingRatio)
        {
            return undampedAngularFreq * (float)System.Math.Sqrt(1.0f - dampingRatio * dampingRatio);
        }
        
        private float CalculateVelocity(float t, float current)
        {
            // Numerical derivative approximation
            float dt = 0.001f;
            float next = ResolveSpring(t + dt);
            return TimeUtils.SecondsToMilliseconds((next - current) / dt);
        }
    }
}
