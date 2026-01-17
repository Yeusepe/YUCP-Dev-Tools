namespace YUCP.Motion.Core
{
    /// <summary>
    /// Transform state value struct (Unity-free). Faster than interfaces in hot path.
    /// </summary>
    public struct TransformState
    {
        public float X;
        public float Y;
        public float ScaleX;
        public float ScaleY;
        public float RotateDeg;
        
        public TransformState(float x, float y, float scaleX = 1.0f, float scaleY = 1.0f, float rotateDeg = 0.0f)
        {
            X = x;
            Y = y;
            ScaleX = scaleX;
            ScaleY = scaleY;
            RotateDeg = rotateDeg;
        }
        
        public static TransformState Identity => new TransformState(0, 0, 1, 1, 0);
        
        public static TransformState Lerp(TransformState a, TransformState b, float t)
        {
            return new TransformState(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.ScaleX + (b.ScaleX - a.ScaleX) * t,
                a.ScaleY + (b.ScaleY - a.ScaleY) * t,
                LerpAngle(a.RotateDeg, b.RotateDeg, t)
            );
        }
        
        /// <summary>
        /// Lerps an angle using shortest path (handles wraparound).
        /// </summary>
        private static float LerpAngle(float a, float b, float t)
        {
            float delta = ((b - a + 180f) % 360f) - 180f;
            return a + delta * t;
        }
    }
}
