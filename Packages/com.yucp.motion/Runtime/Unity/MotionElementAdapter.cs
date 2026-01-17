using UnityEngine.UIElements;
using YUCP.Motion.Core;
using YUCP.Motion.Core.Animation;

namespace YUCP.Motion
{
    /// <summary>
    /// Adapter that connects a VisualElement to the MotionValue system and render steps.
    /// Similar to motion-main's view/node system.
    /// </summary>
    public class MotionElementAdapter
    {
        private readonly VisualElement m_Element;
        private readonly MotionValueStyleApplier m_StyleApplier;
        private ProcessCallback m_RenderCallback;
        private bool m_IsRegistered;
        
        public VisualElement Element => m_Element;
        public MotionValueStyleApplier StyleApplier => m_StyleApplier;
        
        /// <summary>
        /// Gets the transform value (composed of X, Y, ScaleX, ScaleY, RotateDeg).
        /// </summary>
        public TransformValue Transform { get; private set; }
        
        public MotionElementAdapter(VisualElement element)
        {
            m_Element = element;
            m_StyleApplier = new MotionValueStyleApplier(element);
            
            // Create TransformValue from individual MotionValues
            Transform = new TransformValue(
                m_StyleApplier.X.Get(),
                m_StyleApplier.Y.Get(),
                m_StyleApplier.ScaleX.Get(),
                m_StyleApplier.ScaleY.Get(),
                m_StyleApplier.RotateDeg.Get()
            );
            
            // Sync TransformValue with individual MotionValues
            SyncTransformValue();
            
            // Register render callback
            RegisterRenderCallback();
        }
        
        /// <summary>
        /// Syncs TransformValue with individual MotionValues.
        /// </summary>
        private void SyncTransformValue()
        {
            // Subscribe TransformValue to individual MotionValues
            m_StyleApplier.X.OnChange(_ => 
            {
                var state = Transform.GetTransformState();
                state.X = m_StyleApplier.X.Get();
                Transform.SetTransform(state);
            });
            
            m_StyleApplier.Y.OnChange(_ => 
            {
                var state = Transform.GetTransformState();
                state.Y = m_StyleApplier.Y.Get();
                Transform.SetTransform(state);
            });
            
            m_StyleApplier.ScaleX.OnChange(_ => 
            {
                var state = Transform.GetTransformState();
                state.ScaleX = m_StyleApplier.ScaleX.Get();
                Transform.SetTransform(state);
            });
            
            m_StyleApplier.ScaleY.OnChange(_ => 
            {
                var state = Transform.GetTransformState();
                state.ScaleY = m_StyleApplier.ScaleY.Get();
                Transform.SetTransform(state);
            });
            
            m_StyleApplier.RotateDeg.OnChange(_ => 
            {
                var state = Transform.GetTransformState();
                state.RotateDeg = m_StyleApplier.RotateDeg.Get();
                Transform.SetTransform(state);
            });
        }
        
        /// <summary>
        /// Registers render callback on the render step.
        /// </summary>
        private void RegisterRenderCallback()
        {
            if (m_IsRegistered)
                return;
            
            // Register with render batcher
            m_RenderCallback = frameData =>
            {
                m_StyleApplier.Apply();
            };
            
            var batcher = FrameLoopManager.Batcher;
            batcher.Schedule(RenderStepOrder.Render, m_RenderCallback, keepAlive: true);
            
            m_IsRegistered = true;
        }
        
        /// <summary>
        /// Reads computed styles from the element.
        /// Called during read step.
        /// </summary>
        public void ReadComputed()
        {
            m_StyleApplier.ReadComputed();
        }
        
        /// <summary>
        /// Applies styles to the element.
        /// Called during render step.
        /// </summary>
        public void Apply()
        {
            m_StyleApplier.Apply();
        }
        
        /// <summary>
        /// Destroys the adapter and cleans up.
        /// </summary>
        public void Destroy()
        {
            if (m_IsRegistered && m_RenderCallback != null)
            {
                var batcher = FrameLoopManager.Batcher;
                batcher.Cancel(m_RenderCallback);
                m_IsRegistered = false;
            }
            
            m_StyleApplier.Destroy();
            Transform.Destroy();
        }
    }
}
