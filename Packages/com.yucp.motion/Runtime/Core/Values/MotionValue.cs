using System;
using System.Collections.Generic;
using YUCP.Motion.Core.Animation;

namespace YUCP.Motion.Core
{
    /// <summary>
    /// MotionValue is used to track the state and velocity of motion values.
    /// Unity-free core implementation matching motion-main behavior.
    /// </summary>
    public class MotionValue<T> : IMotionValue
    {
        /// <summary>
        /// Maximum time between the value of two frames, beyond which we assume the velocity has since been 0.
        /// </summary>
        private const float MAX_VELOCITY_DELTA_MS = 30.0f;
        
        /// <summary>
        /// The current state of the MotionValue.
        /// </summary>
        private T m_Current;
        
        /// <summary>
        /// The previous state of the MotionValue.
        /// </summary>
        private T m_Prev;
        
        /// <summary>
        /// The previous state of the MotionValue at the end of the previous frame.
        /// </summary>
        private T m_PrevFrameValue;
        
        /// <summary>
        /// The last time the MotionValue was updated (in milliseconds).
        /// </summary>
        private float m_UpdatedAt;
        
        /// <summary>
        /// The time prevFrameValue was updated (in milliseconds).
        /// </summary>
        private float? m_PrevUpdatedAt;
        
        /// <summary>
        /// Tracks whether this value can output a velocity. Currently this is only true
        /// if the value is numerical.
        /// </summary>
        private bool? m_CanTrackVelocity;
        
        /// <summary>
        /// A list of MotionValues whose values are computed from this one.
        /// </summary>
        private HashSet<IMotionValue> m_Dependents;
        
        /// <summary>
        /// A reference to the currently-controlling animation.
        /// </summary>
        private IAnimationPlaybackControls m_Animation;
        
        /// <summary>
        /// Passive effect that intercepts calls to Set.
        /// </summary>
        private PassiveEffect<T> m_PassiveEffect;
        
        /// <summary>
        /// Stop function for passive effect.
        /// </summary>
        private Action m_StopPassiveEffect;
        
        /// <summary>
        /// Whether the passive effect is active.
        /// </summary>
        private bool m_IsEffectActive;
        
        /// <summary>
        /// Event subscriptions.
        /// </summary>
        private Dictionary<string, SubscriptionManager<Action<T>>> m_Events;
        
        /// <summary>
        /// Whether this value has animated.
        /// </summary>
        public bool HasAnimated { get; private set; }
        
        /// <summary>
        /// Creates a new MotionValue with the given initial value.
        /// </summary>
        public MotionValue(T init, ITimeSource timeSource = null)
        {
            m_TimeSource = timeSource ?? SyncTimeSource.Instance;
            SetCurrent(init);
            m_Events = new Dictionary<string, SubscriptionManager<Action<T>>>();
        }
        
        /// <summary>
        /// Sets the current value and timestamp.
        /// </summary>
        private void SetCurrent(T current)
        {
            m_Current = current;
            m_UpdatedAt = m_TimeSource.Now();
            
            if (m_CanTrackVelocity == null && current != null)
            {
                m_CanTrackVelocity = IsNumeric(current);
            }
        }
        
        /// <summary>
        /// Sets the previous frame value.
        /// </summary>
        private void SetPrevFrameValue(T prevFrameValue = default(T))
        {
            if (EqualityComparer<T>.Default.Equals(prevFrameValue, default(T)))
            {
                prevFrameValue = m_Current;
            }
            
            m_PrevFrameValue = prevFrameValue;
            m_PrevUpdatedAt = m_UpdatedAt;
        }
        
        /// <summary>
        /// Gets the current value.
        /// </summary>
        public T Get()
        {
            return m_Current;
        }
        
        /// <summary>
        /// Gets the previous value.
        /// </summary>
        public T GetPrevious()
        {
            return m_Prev;
        }
        
        /// <summary>
        /// Sets the state of the MotionValue.
        /// </summary>
        public void Set(T value)
        {
            if (m_PassiveEffect == null)
            {
                UpdateAndNotify(value);
            }
            else
            {
                m_PassiveEffect(value, UpdateAndNotify);
            }
        }
        
        /// <summary>
        /// Sets the state with explicit velocity tracking.
        /// </summary>
        public void SetWithVelocity(T prev, T current, float deltaMs)
        {
            Set(current);
            m_Prev = default(T);
            m_PrevFrameValue = prev;
            m_PrevUpdatedAt = m_UpdatedAt - deltaMs;
        }
        
        /// <summary>
        /// Set the state, stopping any active animations and resetting velocity to 0.
        /// </summary>
        public void Jump(T value, bool endAnimation = true)
        {
            UpdateAndNotify(value);
            m_Prev = value;
            m_PrevUpdatedAt = null;
            m_PrevFrameValue = default(T);
            
            if (endAnimation)
            {
                Stop();
            }
            
            m_StopPassiveEffect?.Invoke();
        }
        
        /// <summary>
        /// Stops the currently active animation.
        /// </summary>
        public void Stop()
        {
            if (m_Animation != null)
            {
                m_Animation.Stop();
                NotifyEvent("animationCancel");
            }
            ClearAnimation();
        }
        
        /// <summary>
        /// Returns true if this value is currently animating.
        /// </summary>
        public bool IsAnimating()
        {
            return m_Animation != null;
        }
        
        /// <summary>
        /// Returns the latest velocity of MotionValue.
        /// Returns 0 if the state is non-numerical.
        /// </summary>
        public float GetVelocity()
        {
            float currentTime = m_TimeSource.Now();
            
            if (!m_CanTrackVelocity.Value || 
                m_PrevFrameValue == null || 
                !m_PrevUpdatedAt.HasValue ||
                currentTime - m_UpdatedAt > MAX_VELOCITY_DELTA_MS)
            {
                return 0.0f;
            }
            
            float delta = System.Math.Min(
                m_UpdatedAt - m_PrevUpdatedAt.Value,
                MAX_VELOCITY_DELTA_MS
            );
            
            if (delta <= 0.0f)
            {
                return 0.0f;
            }
            
            float currentFloat = ConvertToFloat(m_Current);
            float prevFloat = ConvertToFloat(m_PrevFrameValue);
            
            return VelocityPerSecond(currentFloat - prevFloat, delta);
        }
        
        /// <summary>
        /// Subscribes to change events.
        /// </summary>
        public System.Func<bool> OnChange(Action<T> callback)
        {
            return On("change", callback);
        }
        
        /// <summary>
        /// Subscribes to an event.
        /// </summary>
        public System.Func<bool> On(string eventName, Action<T> callback)
        {
            if (!m_Events.TryGetValue(eventName, out var manager))
            {
                manager = new SubscriptionManager<Action<T>>();
                m_Events[eventName] = manager;
            }
            
            var unsubscribe = manager.Add(callback);
            
            if (eventName == "change")
            {
                // If we have no more change listeners by the start of the next frame, stop active animations.
                // This would need to be handled by the frame loop system.
                return () =>
                {
                    unsubscribe();
                    // Frame read callback would go here - would need integration with render steps
                    return true;
                };
            }
            
            return unsubscribe;
        }
        
        /// <summary>
        /// Attaches a passive effect to the MotionValue.
        /// </summary>
        public void Attach(PassiveEffect<T> passiveEffect, Action stopPassiveEffect)
        {
            m_PassiveEffect = passiveEffect;
            m_StopPassiveEffect = stopPassiveEffect;
        }
        
        /// <summary>
        /// Adds a dependent MotionValue.
        /// </summary>
        public void AddDependent(IMotionValue dependent)
        {
            if (m_Dependents == null)
            {
                m_Dependents = new HashSet<IMotionValue>();
            }
            m_Dependents.Add(dependent);
        }
        
        /// <summary>
        /// Removes a dependent MotionValue.
        /// </summary>
        public void RemoveDependent(IMotionValue dependent)
        {
            m_Dependents?.Remove(dependent);
        }
        
        /// <summary>
        /// Notifies change subscribers without updating value.
        /// </summary>
        public void Dirty()
        {
            NotifyEvent("change", m_Current);
        }
        
        /// <summary>
        /// Clears all listeners.
        /// </summary>
        public void ClearListeners()
        {
            foreach (var manager in m_Events.Values)
            {
                manager.Clear();
            }
        }
        
        /// <summary>
        /// Destroys and cleans up subscribers.
        /// </summary>
        public void Destroy()
        {
            m_Dependents?.Clear();
            NotifyEvent("destroy");
            ClearListeners();
            Stop();
            m_StopPassiveEffect?.Invoke();
        }
        
        /// <summary>
        /// Updates the value and notifies subscribers.
        /// </summary>
        private void UpdateAndNotify(T value)
        {
            float currentTime = m_TimeSource.Now();
            
            // If we're updating during another frame, set the previous frame value
            if (System.Math.Abs(m_UpdatedAt - currentTime) > 0.001f)
            {
                SetPrevFrameValue();
            }
            
            m_Prev = m_Current;
            SetCurrent(value);
            
            // Update change subscribers
            if (!EqualityComparer<T>.Default.Equals(m_Current, m_Prev))
            {
                NotifyEvent("change", m_Current);
                
                if (m_Dependents != null)
                {
                    foreach (var dependent in m_Dependents)
                    {
                        dependent.Dirty();
                    }
                }
            }
        }
        
        private void NotifyEvent(string eventName, T value = default(T))
        {
            if (m_Events.TryGetValue(eventName, out var manager))
            {
                manager.Notify(value);
            }
        }
        
        private void ClearAnimation()
        {
            m_Animation = null;
        }
        
        private bool IsNumeric(T value)
        {
            return value is float || value is double || value is int || value is long ||
                   value is short || value is byte || value is decimal;
        }
        
        private float ConvertToFloat(T value)
        {
            if (value is float f) return f;
            if (value is double d) return (float)d;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is short s) return s;
            if (value is byte b) return b;
            if (value is decimal dec) return (float)dec;
            return 0.0f;
        }
        
        private float VelocityPerSecond(float delta, float deltaMs)
        {
            if (deltaMs <= 0.0f) return 0.0f;
            return (delta / deltaMs) * 1000.0f; // Convert to per second
        }
        
        private ITimeSource m_TimeSource;
    }
    
    /// <summary>
    /// Base interface for MotionValue to support dependent values.
    /// </summary>
    public interface IMotionValue
    {
        void Dirty();
    }
    
    /// <summary>
    /// Delegate for passive effects.
    /// </summary>
    public delegate void PassiveEffect<T>(T value, Action<T> safeSetter);
    
    /// <summary>
    /// Time source interface for Unity-free time tracking.
    /// </summary>
    public interface ITimeSource
    {
        float Now(); // Returns time in milliseconds
    }
    
        /// <summary>
        /// Time source that uses SyncTime for frame-synchronized time.
        /// </summary>
        internal class SyncTimeSource : ITimeSource
        {
            public static readonly SyncTimeSource Instance = new SyncTimeSource();
            
            public float Now()
            {
                return (float)SyncTime.Now(); // Returns milliseconds
            }
        }
        
        /// <summary>
        /// Default time source (uses system time) - fallback when SyncTime not available.
        /// </summary>
        internal class DefaultTimeSource : ITimeSource
        {
            public static readonly DefaultTimeSource Instance = new DefaultTimeSource();
            
            public float Now()
            {
                return (float)(System.DateTime.UtcNow.Ticks / 10000.0); // Convert to milliseconds
            }
        }
}
