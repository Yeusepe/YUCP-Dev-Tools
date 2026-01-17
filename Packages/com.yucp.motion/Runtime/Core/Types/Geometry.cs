namespace YUCP.Motion.Core
{
    /// <summary>
    /// 2D point (Unity-free).
    /// </summary>
    public struct Point
    {
        public float X;
        public float Y;
        
        public Point(float x, float y)
        {
            X = x;
            Y = y;
        }
        
        public static Point Zero => new Point(0.0f, 0.0f);
    }
    
    /// <summary>
    /// Axis with min and max bounds (nullable for partial constraints).
    /// </summary>
    public struct Axis
    {
        public float? Min;
        public float? Max;
        
        public Axis(float? min, float? max)
        {
            Min = min;
            Max = max;
        }
    }
    
    /// <summary>
    /// 2D box defined by x and y axes.
    /// </summary>
    public struct Box
    {
        public Axis X;
        public Axis Y;
        
        public Box(Axis x, Axis y)
        {
            X = x;
            Y = y;
        }
    }
    
    /// <summary>
    /// Bounding box with top, right, bottom, left edges.
    /// </summary>
    public struct BoundingBox
    {
        public float Top;
        public float Right;
        public float Bottom;
        public float Left;
        
        public BoundingBox(float top, float right, float bottom, float left)
        {
            Top = top;
            Right = right;
            Bottom = bottom;
            Left = left;
        }
    }
    
    /// <summary>
    /// Axis delta for transform calculations.
    /// </summary>
    public struct AxisDelta
    {
        public float Translate;
        public float Scale;
        public float Origin;
        public float OriginPoint;
        
        public AxisDelta(float translate, float scale, float origin, float originPoint)
        {
            Translate = translate;
            Scale = scale;
            Origin = origin;
            OriginPoint = originPoint;
        }
    }
    
    /// <summary>
    /// 2D delta for transform calculations.
    /// </summary>
    public struct Delta
    {
        public AxisDelta X;
        public AxisDelta Y;
        
        public Delta(AxisDelta x, AxisDelta y)
        {
            X = x;
            Y = y;
        }
    }
}
