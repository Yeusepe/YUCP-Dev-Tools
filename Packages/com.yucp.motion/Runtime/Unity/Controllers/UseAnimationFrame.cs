using System;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// UseAnimationFrame controller - registers a callback on a render step.
    /// Similar to motion-main's useAnimationFrame hook.
    /// </summary>
    public class UseAnimationFrame : IDisposable
    {
        private readonly ProcessCallback m_Callback;
        private readonly RenderStepOrder m_Step;
        private ProcessCallback m_RegisteredCallback;
        private bool m_Disposed;
        
        /// <summary>
        /// Creates a new UseAnimationFrame controller.
        /// </summary>
        public UseAnimationFrame(Action<FrameData> callback, RenderStepOrder step = RenderStepOrder.Update)
        {
            m_Callback = frameData => callback(frameData);
            m_Step = step;
            
            // Register with render batcher
            var batcher = FrameLoopManager.Batcher;
            m_RegisteredCallback = batcher.Schedule(m_Step, m_Callback, keepAlive: true);
        }
        
        public void Dispose()
        {
            if (m_Disposed)
                return;
            
            m_Disposed = true;
            
            // Cancel callback from render batcher
            if (m_RegisteredCallback != null)
            {
                var batcher = FrameLoopManager.Batcher;
                batcher.Cancel(m_RegisteredCallback);
                m_RegisteredCallback = null;
            }
        }
    }
}
