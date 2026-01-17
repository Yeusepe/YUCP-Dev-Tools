namespace YUCP.Motion.Core
{
    /// <summary>
    /// Transition configuration (duration and easing).
    /// </summary>
    public struct Transition
    {
        public float Duration;
        public EasingType Easing;
        
        public Transition(float duration, EasingType easing = EasingType.Linear)
        {
            Duration = duration;
            Easing = easing;
        }
        
        public static Transition Default => new Transition(0.25f, EasingType.EaseOut);
    }
    
    /// <summary>
    /// Easing function types.
    /// </summary>
    public enum EasingType : byte
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut,
        EaseInQuad,
        EaseOutQuad,
        EaseInOutQuad,
        EaseInCubic,
        EaseOutCubic,
        EaseInOutCubic,
        CircIn,
        CircOut,
        CircInOut,
        BackIn,
        BackOut,
        BackInOut,
        Anticipate
    }
}
