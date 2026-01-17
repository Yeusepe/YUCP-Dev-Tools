using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Motion view adapter that holds the VisualElement and a StyleApplier.
    /// </summary>
    public class MotionViewAdapter : IMotionViewAdapter
    {
        private readonly VisualElement m_Element;
        private readonly StyleApplier m_StyleApplier;
        private ResolvedMotionValues m_ResolvedValues;
        
        public VisualElement Element => m_Element;
        
        public MotionViewAdapter(VisualElement element)
        {
            m_Element = element;
            m_StyleApplier = new StyleApplier(element);
            m_ResolvedValues = m_StyleApplier.ReadInitial();
        }
        
        public void Apply()
        {
            m_StyleApplier.Apply(m_ResolvedValues);
        }
        
        public void ReadInitial(ref ResolvedMotionValues values)
        {
            values = m_StyleApplier.ReadInitial();
        }
        
        /// <summary>
        /// Updates the resolved values from a controller.
        /// </summary>
        public void UpdateFromController(MotionController controller)
        {
            TransformState transform = controller.GetTransform();
            ColorRGBA color = controller.GetColor();
            float opacity = controller.GetOpacity();
            DirtyMask dirty = controller.GetDirtyMask();
            
            m_ResolvedValues = new ResolvedMotionValues(transform, color, opacity, dirty);
        }
    }
}
