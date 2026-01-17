namespace YUCP.Motion.Core
{
    /// <summary>
    /// Bitmask for tracking which properties are dirty (need updates).
    /// </summary>
    [System.Flags]
    public enum DirtyMask : byte
    {
        None = 0,
        Transform = 1 << 0,  // x, y, scale, rotate
        Paint = 1 << 1,      // opacity, background color
        Layout = 1 << 2,     // layout-related properties
        All = Transform | Paint | Layout
    }
}
