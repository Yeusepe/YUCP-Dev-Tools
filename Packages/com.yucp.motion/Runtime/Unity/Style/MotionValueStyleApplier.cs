using UnityEngine.UIElements;
using YUCP.Motion.Core;
using YUCP.Motion.Core.Animation;

namespace YUCP.Motion
{
    /// <summary>
    /// Style applier that works with MotionValues directly.
    /// Integrates with the render step system for efficient updates.
    /// </summary>
    public class MotionValueStyleApplier
    {
        private readonly VisualElement m_Element;
        private readonly StyleApplier m_StyleApplier;
        
        // MotionValues for each property
        private MotionValue<float> m_X;
        private MotionValue<float> m_Y;
        private MotionValue<float> m_ScaleX;
        private MotionValue<float> m_ScaleY;
        private MotionValue<float> m_RotateDeg;
        private MotionValue<float> m_Opacity;
        private MotionValue<ColorRGBA> m_BackgroundColor;
        
        // Cached resolved values
        private ResolvedMotionValues m_ResolvedValues;
        private bool m_IsDirty;
        
        public VisualElement Element => m_Element;
        
        /// <summary>
        /// Gets the X translation MotionValue.
        /// </summary>
        public MotionValue<float> X => m_X ??= new MotionValue<float>(0.0f);
        
        /// <summary>
        /// Gets the Y translation MotionValue.
        /// </summary>
        public MotionValue<float> Y => m_Y ??= new MotionValue<float>(0.0f);
        
        /// <summary>
        /// Gets the X scale MotionValue.
        /// </summary>
        public MotionValue<float> ScaleX => m_ScaleX ??= new MotionValue<float>(1.0f);
        
        /// <summary>
        /// Gets the Y scale MotionValue.
        /// </summary>
        public MotionValue<float> ScaleY => m_ScaleY ??= new MotionValue<float>(1.0f);
        
        /// <summary>
        /// Gets the rotation MotionValue (degrees).
        /// </summary>
        public MotionValue<float> RotateDeg => m_RotateDeg ??= new MotionValue<float>(0.0f);
        
        /// <summary>
        /// Gets the opacity MotionValue.
        /// </summary>
        public MotionValue<float> Opacity => m_Opacity ??= new MotionValue<float>(1.0f);
        
        /// <summary>
        /// Gets the background color MotionValue.
        /// </summary>
        public MotionValue<ColorRGBA> BackgroundColor => m_BackgroundColor ??= new MotionValue<ColorRGBA>(default);
        
        public MotionValueStyleApplier(VisualElement element)
        {
            m_Element = element;
            m_StyleApplier = new StyleApplier(element);
            
            // Read initial values
            m_ResolvedValues = m_StyleApplier.ReadInitial();
            
            // Initialize MotionValues from initial values
            if (m_X == null) m_X = new MotionValue<float>(m_ResolvedValues.Transform.X);
            if (m_Y == null) m_Y = new MotionValue<float>(m_ResolvedValues.Transform.Y);
            if (m_ScaleX == null) m_ScaleX = new MotionValue<float>(m_ResolvedValues.Transform.ScaleX);
            if (m_ScaleY == null) m_ScaleY = new MotionValue<float>(m_ResolvedValues.Transform.ScaleY);
            if (m_RotateDeg == null) m_RotateDeg = new MotionValue<float>(m_ResolvedValues.Transform.RotateDeg);
            if (m_Opacity == null) m_Opacity = new MotionValue<float>(m_ResolvedValues.Opacity);
            if (m_BackgroundColor == null) m_BackgroundColor = new MotionValue<ColorRGBA>(m_ResolvedValues.BackgroundColor);
            
            // Subscribe to changes
            SubscribeToChanges();
        }
        
        /// <summary>
        /// Subscribes to MotionValue changes to mark as dirty.
        /// </summary>
        private void SubscribeToChanges()
        {
            X.OnChange(_ => m_IsDirty = true);
            Y.OnChange(_ => m_IsDirty = true);
            ScaleX.OnChange(_ => m_IsDirty = true);
            ScaleY.OnChange(_ => m_IsDirty = true);
            RotateDeg.OnChange(_ => m_IsDirty = true);
            Opacity.OnChange(_ => m_IsDirty = true);
            BackgroundColor.OnChange(_ => m_IsDirty = true);
        }
        
        /// <summary>
        /// Applies current MotionValue states to the VisualElement.
        /// Called during render step.
        /// </summary>
        public void Apply()
        {
            if (!m_IsDirty)
                return;
            
            // Build resolved values from MotionValues
            var transform = new TransformState
            {
                X = X.Get(),
                Y = Y.Get(),
                ScaleX = ScaleX.Get(),
                ScaleY = ScaleY.Get(),
                RotateDeg = RotateDeg.Get()
            };
            
            var resolved = new ResolvedMotionValues(
                transform,
                BackgroundColor.Get(),
                Opacity.Get(),
                DirtyMask.All
            );
            
            m_StyleApplier.Apply(resolved);
            m_IsDirty = false;
        }
        
        /// <summary>
        /// Reads current computed styles from the element.
        /// </summary>
        public void ReadComputed()
        {
            // Use resolvedStyle if available, fallback to style.value
            // This handles Unity version differences
            var resolved = m_StyleApplier.ReadInitial();
            
            // Update MotionValues to match computed styles
            X.Jump(resolved.Transform.X, false);
            Y.Jump(resolved.Transform.Y, false);
            ScaleX.Jump(resolved.Transform.ScaleX, false);
            ScaleY.Jump(resolved.Transform.ScaleY, false);
            RotateDeg.Jump(resolved.Transform.RotateDeg, false);
            Opacity.Jump(resolved.Opacity, false);
            BackgroundColor.Jump(resolved.BackgroundColor, false);
        }
        
        /// <summary>
        /// Cleans up subscriptions.
        /// </summary>
        public void Destroy()
        {
            m_X?.Destroy();
            m_Y?.Destroy();
            m_ScaleX?.Destroy();
            m_ScaleY?.Destroy();
            m_RotateDeg?.Destroy();
            m_Opacity?.Destroy();
            m_BackgroundColor?.Destroy();
        }
    }
}
