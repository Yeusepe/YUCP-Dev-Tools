using System;
using System.Threading.Tasks;
using YUCP.Motion.Core;
using static YUCP.Motion.Core.SyncTime;

namespace YUCP.Motion.Core.Animation
{
    /// <summary>
    /// Value animation that drives a MotionValue using a generator.
    /// Similar to motion-main's JSAnimation.
    /// </summary>
    public class ValueAnimation<T> : IAnimationPlaybackControlsWithThen
    {
        private readonly IKeyframeGenerator<T> m_Generator;
        private readonly MotionValue<T> m_MotionValue;
        private readonly Action<T> m_OnUpdate;
        private readonly Action m_OnComplete;
        private readonly Action m_OnCancel;
        
        private float m_CurrentTime;
        private float? m_StartTime;
        private float? m_HoldTime;
        private float m_Speed = 1.0f;
        private AnimationPlayState m_State = AnimationPlayState.Idle;
        private TaskCompletionSource<bool> m_FinishedSource;
        private bool m_IsStopped;
        
        public float Time => m_CurrentTime;
        public float Speed { get => m_Speed; set => m_Speed = value; }
        public float? StartTime => m_StartTime;
        public AnimationPlayState State => m_State;
        public float Duration => m_Generator.CalculatedDuration ?? float.MaxValue;
        public Task Finished => m_FinishedSource?.Task ?? Task.CompletedTask;
        
        /// <summary>
        /// Creates a new value animation.
        /// </summary>
        public ValueAnimation(
            IKeyframeGenerator<T> generator,
            MotionValue<T> motionValue,
            Action<T> onUpdate = null,
            Action onComplete = null,
            Action onCancel = null)
        {
            m_Generator = generator ?? throw new ArgumentNullException(nameof(generator));
            m_MotionValue = motionValue ?? throw new ArgumentNullException(nameof(motionValue));
            m_OnUpdate = onUpdate;
            m_OnComplete = onComplete;
            m_OnCancel = onCancel;
        }
        
        public void Play()
        {
            if (m_State == AnimationPlayState.Running)
                return;
            
            m_IsStopped = false;
            m_State = AnimationPlayState.Running;
            
            if (m_HoldTime.HasValue)
            {
                m_CurrentTime = m_HoldTime.Value;
                m_HoldTime = null;
            }
            
            if (!m_StartTime.HasValue)
            {
                m_StartTime = (float)SyncTime.Now();
                m_FinishedSource = new TaskCompletionSource<bool>();
            }
        }
        
        public void Pause()
        {
            if (m_State != AnimationPlayState.Running)
                return;
            
            m_State = AnimationPlayState.Paused;
            m_HoldTime = m_CurrentTime;
        }
        
        public void Stop()
        {
            m_IsStopped = true;
            m_State = AnimationPlayState.Idle;
            m_StartTime = null;
            m_HoldTime = null;
            m_CurrentTime = 0.0f;
            m_FinishedSource?.TrySetCanceled();
            m_FinishedSource = null;
        }
        
        public void Complete()
        {
            var finalState = m_Generator.Next(Duration);
            m_MotionValue.Set(finalState.Value);
            
            m_State = AnimationPlayState.Finished;
            m_FinishedSource?.TrySetResult(true);
            m_OnComplete?.Invoke();
        }
        
        public void Cancel()
        {
            Stop();
            m_OnCancel?.Invoke();
        }
        
        public Task Then(Action onResolve, Action onReject = null)
        {
            if (m_FinishedSource == null)
            {
                onResolve?.Invoke();
                return Task.CompletedTask;
            }
            
            return m_FinishedSource.Task.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    onResolve?.Invoke();
                }
                else
                {
                    onReject?.Invoke();
                }
            });
        }
        
        /// <summary>
        /// Updates the animation (called by frame loop).
        /// </summary>
        public void Update(float deltaMs)
        {
            if (m_State != AnimationPlayState.Running || m_IsStopped)
                return;
            
            m_CurrentTime += deltaMs * m_Speed;
            
            var state = m_Generator.Next(m_CurrentTime);
            m_MotionValue.Set(state.Value);
            m_OnUpdate?.Invoke(state.Value);
            
            if (state.Done)
            {
                Complete();
            }
        }
    }
}
