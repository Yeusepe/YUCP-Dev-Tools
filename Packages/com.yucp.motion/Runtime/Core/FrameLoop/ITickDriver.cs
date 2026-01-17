namespace YUCP.Motion.Core
{
    /// <summary>
    /// Abstraction for where ticks come from (Editor vs Runtime).
    /// </summary>
    public interface ITickDriver
    {
        /// <summary>
        /// Gets the current frame data.
        /// </summary>
        FrameData GetFrameData();
        
        /// <summary>
        /// Checks if the driver is active.
        /// </summary>
        bool IsActive { get; }
    }
}
