using System;
using System.Collections.Generic;

namespace YUCP.Motion.Core
{
    /// <summary>
    /// Batcher that flushes render steps in order.
    /// Similar to motion-main's batcher.ts
    /// </summary>
    public class RenderBatcher
    {
        private const float MAX_ELAPSED_MS = 40.0f;
        private const float DEFAULT_DELTA_MS = 1000.0f / 60.0f; // 60 FPS
        
        private readonly Dictionary<RenderStepOrder, RenderStep> m_Steps;
        private FrameData m_State; // Not readonly so we can modify members
        private bool m_RunNextFrame;
        private bool m_UseDefaultElapsed;
        private readonly Action<Action> m_ScheduleNextBatch;
        private readonly bool m_AllowKeepAlive;
        
        /// <summary>
        /// Gets the frame state.
        /// </summary>
        public FrameData State => m_State;
        
        /// <summary>
        /// Gets the render steps.
        /// </summary>
        public IReadOnlyDictionary<RenderStepOrder, RenderStep> Steps => m_Steps;
        
        /// <summary>
        /// Creates a new render batcher.
        /// </summary>
        public RenderBatcher(Action<Action> scheduleNextBatch, bool allowKeepAlive = true)
        {
            m_ScheduleNextBatch = scheduleNextBatch;
            m_AllowKeepAlive = allowKeepAlive;
            m_State = new FrameData(0.0f, 0.0);
            m_UseDefaultElapsed = true;
            
            // Create render steps
            m_Steps = new Dictionary<RenderStepOrder, RenderStep>();
            foreach (RenderStepOrder order in Enum.GetValues(typeof(RenderStepOrder)))
            {
                m_Steps[order] = new RenderStep(FlagRunNextFrame);
            }
        }
        
        private void FlagRunNextFrame()
        {
            m_RunNextFrame = true;
        }
        
        /// <summary>
        /// Processes all render steps in order.
        /// </summary>
        public void ProcessBatch(double timestamp)
        {
            m_RunNextFrame = false;
            
            // Calculate delta
            if (m_UseDefaultElapsed)
            {
                m_State.Delta = DEFAULT_DELTA_MS;
            }
            else
            {
                float delta = (float)(timestamp - m_State.Now);
                m_State.Delta = Math.Max(Math.Min(delta, MAX_ELAPSED_MS), 1.0f);
            }
            
            m_State.Now = timestamp;
            m_State.IsProcessing = true;
            
            // Process steps in order
            m_Steps[RenderStepOrder.Setup].Process(m_State);
            m_Steps[RenderStepOrder.Read].Process(m_State);
            m_Steps[RenderStepOrder.ResolveKeyframes].Process(m_State);
            m_Steps[RenderStepOrder.PreUpdate].Process(m_State);
            m_Steps[RenderStepOrder.Update].Process(m_State);
            m_Steps[RenderStepOrder.PreRender].Process(m_State);
            m_Steps[RenderStepOrder.Render].Process(m_State);
            m_Steps[RenderStepOrder.PostRender].Process(m_State);
            
            m_State.IsProcessing = false;
            
            if (m_RunNextFrame && m_AllowKeepAlive)
            {
                m_UseDefaultElapsed = false;
                double nextTimestamp = timestamp; // Will be updated on next call
                m_ScheduleNextBatch(() => ProcessBatch(nextTimestamp));
            }
        }
        
        /// <summary>
        /// Wakes the batcher to process next frame.
        /// </summary>
        public void Wake()
        {
            m_RunNextFrame = true;
            m_UseDefaultElapsed = true;
            
            if (!m_State.IsProcessing)
            {
                // Schedule batch processing - timestamp will be provided when called
                m_ScheduleNextBatch(() =>
                {
                    double timestamp = System.DateTime.UtcNow.Ticks / 10000.0;
                    ProcessBatch(timestamp);
                });
            }
        }
        
        /// <summary>
        /// Schedules a process on a specific step.
        /// </summary>
        public ProcessCallback Schedule(RenderStepOrder step, ProcessCallback process, bool keepAlive = false, bool immediate = false)
        {
            if (!m_RunNextFrame)
            {
                Wake();
            }
            
            return m_Steps[step].Schedule(process, keepAlive, immediate);
        }
        
        /// <summary>
        /// Cancels a process from all steps.
        /// </summary>
        public void Cancel(ProcessCallback process)
        {
            foreach (var step in m_Steps.Values)
            {
                step.Cancel(process);
            }
        }
    }
}
