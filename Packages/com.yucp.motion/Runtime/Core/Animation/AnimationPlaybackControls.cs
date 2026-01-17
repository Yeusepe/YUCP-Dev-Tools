using System;
using System.Threading.Tasks;

namespace YUCP.Motion.Core.Animation
{
    /// <summary>
    /// Animation play state.
    /// </summary>
    public enum AnimationPlayState
    {
        Idle,
        Running,
        Paused,
        Finished
    }
    
    /// <summary>
    /// Interface for animation playback controls.
    /// Similar to motion-main's AnimationPlaybackControls.
    /// </summary>
    public interface IAnimationPlaybackControls
    {
        /// <summary>
        /// Current time of the animation in milliseconds.
        /// </summary>
        float Time { get; }
        
        /// <summary>
        /// Playback speed (1 = normal, 2 = double, 0.5 = half).
        /// </summary>
        float Speed { get; set; }
        
        /// <summary>
        /// Start time in milliseconds, or null if not started.
        /// </summary>
        float? StartTime { get; }
        
        /// <summary>
        /// Animation state.
        /// </summary>
        AnimationPlayState State { get; }
        
        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        float Duration { get; }
        
        /// <summary>
        /// Stops the animation at its current state.
        /// </summary>
        void Stop();
        
        /// <summary>
        /// Plays the animation.
        /// </summary>
        void Play();
        
        /// <summary>
        /// Pauses the animation.
        /// </summary>
        void Pause();
        
        /// <summary>
        /// Completes the animation and applies final state.
        /// </summary>
        void Complete();
        
        /// <summary>
        /// Cancels the animation and applies initial state.
        /// </summary>
        void Cancel();
        
        /// <summary>
        /// Promise that resolves when animation finishes.
        /// </summary>
        Task Finished { get; }
    }
    
    /// <summary>
    /// Extended playback controls with Then support.
    /// </summary>
    public interface IAnimationPlaybackControlsWithThen : IAnimationPlaybackControls
    {
        /// <summary>
        /// Chains promise callbacks.
        /// </summary>
        Task Then(Action onResolve, Action onReject = null);
    }
}
