namespace YUCP.Motion.Core
{
    /// <summary>
    /// Color value struct (Unity-free). Faster than interfaces in hot path.
    /// </summary>
    public struct ColorRGBA
    {
        public float R;
        public float G;
        public float B;
        public float A;
        
        public ColorRGBA(float r, float g, float b, float a = 1.0f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
        
        public static ColorRGBA Lerp(ColorRGBA a, ColorRGBA b, float t)
        {
            return new ColorRGBA(
                a.R + (b.R - a.R) * t,
                a.G + (b.G - a.G) * t,
                a.B + (b.B - a.B) * t,
                a.A + (b.A - a.A) * t
            );
        }
    }
}
