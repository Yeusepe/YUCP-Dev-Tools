using System;
using System.Collections.Generic;

namespace YUCP.Motion.Core
{
    /// <summary>
    /// Process callback for render steps.
    /// </summary>
    public delegate void ProcessCallback(FrameData frameData);
    
    /// <summary>
    /// Render step that processes callbacks in order.
    /// Similar to motion-main's render-step.ts
    /// </summary>
    public class RenderStep
    {
        private HashSet<ProcessCallback> m_ThisFrame = new HashSet<ProcessCallback>();
        private HashSet<ProcessCallback> m_NextFrame = new HashSet<ProcessCallback>();
        private readonly HashSet<ProcessCallback> m_KeepAlive = new HashSet<ProcessCallback>();
        private bool m_IsProcessing;
        private bool m_FlushNextFrame;
        private FrameData m_LatestFrameData;
        private readonly Action m_RunNextFrame;
        
        /// <summary>
        /// Creates a new render step.
        /// </summary>
        public RenderStep(Action runNextFrame)
        {
            m_RunNextFrame = runNextFrame;
        }
        
        /// <summary>
        /// Schedules a process to run on the next frame.
        /// </summary>
        public ProcessCallback Schedule(ProcessCallback callback, bool keepAlive = false, bool immediate = false)
        {
            bool addToCurrentFrame = immediate && m_IsProcessing;
            var queue = addToCurrentFrame ? m_ThisFrame : m_NextFrame;
            
            if (keepAlive)
            {
                m_KeepAlive.Add(callback);
            }
            
            if (!queue.Contains(callback))
            {
                queue.Add(callback);
            }
            
            return callback;
        }
        
        /// <summary>
        /// Cancels a scheduled callback.
        /// </summary>
        public void Cancel(ProcessCallback callback)
        {
            m_NextFrame.Remove(callback);
            m_KeepAlive.Remove(callback);
        }
        
        /// <summary>
        /// Processes all scheduled callbacks.
        /// </summary>
        public void Process(FrameData frameData)
        {
            m_LatestFrameData = frameData;
            
            // If already processing, mark for flush next frame
            if (m_IsProcessing)
            {
                m_FlushNextFrame = true;
                return;
            }
            
            m_IsProcessing = true;
            
            // Swap frames to avoid GC
            var temp = m_ThisFrame;
            m_ThisFrame.Clear();
            m_ThisFrame = m_NextFrame;
            m_NextFrame = temp;
            
            // Execute this frame's callbacks
            foreach (var callback in m_ThisFrame)
            {
                if (m_KeepAlive.Contains(callback))
                {
                    Schedule(callback);
                    m_RunNextFrame?.Invoke();
                }
                
                callback(m_LatestFrameData);
            }
            
            // Clear this frame
            m_ThisFrame.Clear();
            
            m_IsProcessing = false;
            
            // Flush next frame if needed
            if (m_FlushNextFrame)
            {
                m_FlushNextFrame = false;
                Process(frameData);
            }
        }
        
        /// <summary>
        /// Gets the number of scheduled callbacks.
        /// </summary>
        public int Count => m_NextFrame.Count;
    }
}
