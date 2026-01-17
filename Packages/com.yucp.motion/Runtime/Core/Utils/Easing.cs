namespace YUCP.Motion.Core
{
    /// <summary>
    /// Easing functions (Unity-free).
    /// </summary>
    public static class Easing
    {
        public static float Apply(EasingType type, float t)
        {
            t = MathUtils.Clamp01(t);
            
            return type switch
            {
                EasingType.Linear => Linear(t),
                EasingType.EaseIn => EaseIn(t),
                EasingType.EaseOut => EaseOut(t),
                EasingType.EaseInOut => EaseInOut(t),
                EasingType.EaseInQuad => EaseInQuad(t),
                EasingType.EaseOutQuad => EaseOutQuad(t),
                EasingType.EaseInOutQuad => EaseInOutQuad(t),
                EasingType.EaseInCubic => EaseInCubic(t),
                EasingType.EaseOutCubic => EaseOutCubic(t),
                EasingType.EaseInOutCubic => EaseInOutCubic(t),
                EasingType.CircIn => CircIn(t),
                EasingType.CircOut => CircOut(t),
                EasingType.CircInOut => CircInOut(t),
                EasingType.BackIn => BackIn(t),
                EasingType.BackOut => BackOut(t),
                EasingType.BackInOut => BackInOut(t),
                EasingType.Anticipate => Anticipate(t),
                _ => Linear(t)
            };
        }
        
        private static float Linear(float t) => t;
        
        private static float EaseIn(float t) => t * t;
        
        private static float EaseOut(float t) => 1.0f - (1.0f - t) * (1.0f - t);
        
        private static float EaseInOut(float t)
        {
            return t < 0.5f
                ? 2.0f * t * t
                : 1.0f - 2.0f * (1.0f - t) * (1.0f - t);
        }
        
        private static float EaseInQuad(float t) => t * t;
        
        private static float EaseOutQuad(float t) => 1.0f - (1.0f - t) * (1.0f - t);
        
        private static float EaseInOutQuad(float t)
        {
            return t < 0.5f
                ? 2.0f * t * t
                : 1.0f - 2.0f * (1.0f - t) * (1.0f - t);
        }
        
        private static float EaseInCubic(float t) => t * t * t;
        
        private static float EaseOutCubic(float t)
        {
            float f = 1.0f - t;
            return 1.0f - f * f * f;
        }
        
        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f
                ? 4.0f * t * t * t
                : 1.0f - 4.0f * (1.0f - t) * (1.0f - t) * (1.0f - t);
        }
        
        // Circular easing
        private static float CircIn(float t) => 1.0f - (float)System.Math.Sqrt(1.0f - t * t);
        
        private static float CircOut(float t)
        {
            t = 1.0f - t;
            return (float)System.Math.Sqrt(1.0f - t * t);
        }
        
        private static float CircInOut(float t)
        {
            return t <= 0.5f
                ? CircIn(2.0f * t) / 2.0f
                : (2.0f - CircIn(2.0f * (1.0f - t))) / 2.0f;
        }
        
        // Back easing (uses cubic bezier approximation)
        // backOut = cubicBezier(0.33, 1.53, 0.69, 0.99)
        private static float BackOut(float t)
        {
            // Approximation using cubic bezier
            return CubicBezier(0.33f, 1.53f, 0.69f, 0.99f, t);
        }
        
        private static float BackIn(float t)
        {
            // Reverse of backOut
            return ReverseEasing(BackOut, t);
        }
        
        private static float BackInOut(float t)
        {
            // Mirror of backIn
            return MirrorEasing(BackIn, t);
        }
        
        // Anticipate easing
        private static float Anticipate(float t)
        {
            t *= 2.0f;
            if (t < 1.0f)
            {
                return 0.5f * BackIn(t);
            }
            else
            {
                return 0.5f * (2.0f - (float)System.Math.Pow(2.0f, -10.0f * (t - 1.0f)));
            }
        }
        
        // Easing modifiers
        private static float ReverseEasing(System.Func<float, float> easing, float t)
        {
            return 1.0f - easing(1.0f - t);
        }
        
        private static float MirrorEasing(System.Func<float, float> easing, float t)
        {
            return t <= 0.5f
                ? easing(2.0f * t) / 2.0f
                : (2.0f - easing(2.0f * (1.0f - t))) / 2.0f;
        }
        
        // Cubic bezier easing
        private const float SubdivisionPrecision = 0.0000001f;
        private const int SubdivisionMaxIterations = 12;
        
        private static float CubicBezier(float x1, float y1, float x2, float y2, float t)
        {
            // Linear gradient check
            if (System.Math.Abs(x1 - y1) < MathUtils.Epsilon && 
                System.Math.Abs(x2 - y2) < MathUtils.Epsilon)
            {
                return t;
            }
            
            // If at start/end, return t without easing
            if (t <= 0.0f) return 0.0f;
            if (t >= 1.0f) return 1.0f;
            
            float tForX = GetTForX(t, x1, x2);
            return CalcBezier(tForX, y1, y2);
        }
        
        private static float CalcBezier(float t, float a1, float a2)
        {
            return (((1.0f - 3.0f * a2 + 3.0f * a1) * t + (3.0f * a2 - 6.0f * a1)) * t + 3.0f * a1) * t;
        }
        
        private static float GetTForX(float x, float x1, float x2)
        {
            return BinarySubdivide(x, 0.0f, 1.0f, x1, x2);
        }
        
        private static float BinarySubdivide(float x, float lowerBound, float upperBound, float mX1, float mX2)
        {
            float currentX;
            float currentT;
            int i = 0;
            
            do
            {
                currentT = lowerBound + (upperBound - lowerBound) / 2.0f;
                currentX = CalcBezier(currentT, mX1, mX2) - x;
                
                if (currentX > 0.0f)
                {
                    upperBound = currentT;
                }
                else
                {
                    lowerBound = currentT;
                }
            }
            while (System.Math.Abs(currentX) > SubdivisionPrecision && ++i < SubdivisionMaxIterations);
            
            return currentT;
        }
    }
}
