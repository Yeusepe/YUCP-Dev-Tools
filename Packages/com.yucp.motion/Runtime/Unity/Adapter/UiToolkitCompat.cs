using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Motion
{
    /// <summary>
    /// Unity version compatibility layer. Isolates all version-specific constructors and conversions.
    /// Uses compile-time defines, not runtime detection.
    /// </summary>
    public static class UiToolkitCompat
    {
        /// <summary>
        /// Creates a Translate value from pixel coordinates.
        /// </summary>
        public static Translate PxTranslate(float x, float y)
        {
#if UNITY_2021_2_OR_NEWER
            return new Translate(x, y);
#else
            return new Translate(new Length(x, LengthUnit.Pixel), new Length(y, LengthUnit.Pixel));
#endif
        }
        
        /// <summary>
        /// Creates a Rotate value from degrees.
        /// </summary>
        public static Rotate DegRotate(float deg)
        {
#if UNITY_2021_2_OR_NEWER
            return new Rotate(deg);
#else
            return new Rotate(new Angle(deg, AngleUnit.Degree));
#endif
        }
        
        /// <summary>
        /// Creates a Scale value from x and y scale factors.
        /// </summary>
        public static Scale XYScale(float sx, float sy)
        {
#if UNITY_2021_2_OR_NEWER
            return new Scale(new Vector2(sx, sy));
#else
            return new Scale(new Vector2(sx, sy));
#endif
        }
        
        /// <summary>
        /// Creates a Length value in pixels.
        /// </summary>
        public static Length Px(float v)
        {
#if UNITY_2021_2_OR_NEWER
            return new Length(v);
#else
            return new Length(v, LengthUnit.Pixel);
#endif
        }
        
        /// <summary>
        /// Creates an Angle value in degrees.
        /// </summary>
        public static Angle Deg(float v)
        {
#if UNITY_2021_2_OR_NEWER
            return new Angle(v);
#else
            return new Angle(v, AngleUnit.Degree);
#endif
        }
    }
}
