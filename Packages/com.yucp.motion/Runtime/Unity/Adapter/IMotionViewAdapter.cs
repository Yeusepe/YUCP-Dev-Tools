using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Unity-facing adapter interface. Keeps core pure.
    /// </summary>
    public interface IMotionViewAdapter
    {
        /// <summary>
        /// Applies the resolved motion values to the view.
        /// </summary>
        void Apply();
        
        /// <summary>
        /// Reads initial values from the view into resolved motion values.
        /// </summary>
        void ReadInitial(ref ResolvedMotionValues values);
    }
}
