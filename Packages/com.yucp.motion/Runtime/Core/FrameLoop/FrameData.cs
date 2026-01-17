namespace YUCP.Motion.Core
{
    /// <summary>
    /// Frame data containing delta time and current time.
    /// Similar to motion-main's FrameData.
    /// </summary>
    public struct FrameData
    {
        public float Delta;
        public double Now;
        public bool IsProcessing;
        
        public FrameData(float delta, double now, bool isProcessing = false)
        {
            Delta = delta;
            Now = now;
            IsProcessing = isProcessing;
        }
    }
}
