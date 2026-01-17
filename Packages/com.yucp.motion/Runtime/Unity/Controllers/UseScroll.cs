using System;
using UnityEngine.UIElements;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Scroll motion values returned by UseScroll.
    /// </summary>
    public struct ScrollMotionValues
    {
        public MotionValue<float> ScrollX;
        public MotionValue<float> ScrollY;
        public MotionValue<float> ScrollXProgress;
        public MotionValue<float> ScrollYProgress;
    }
    
    /// <summary>
    /// UseScroll controller - tracks scroll position and progress.
    /// Similar to motion-main's useScroll hook.
    /// </summary>
    public class UseScroll : IDisposable
    {
        private readonly ScrollView m_ScrollView;
        private readonly ScrollMotionValues m_Values;
        private System.Func<bool> m_UnsubscribeX;
        private System.Func<bool> m_UnsubscribeY;
        private bool m_Disposed;
        
        /// <summary>
        /// Gets the scroll motion values.
        /// </summary>
        public ScrollMotionValues Values => m_Values;
        
        /// <summary>
        /// Creates a new UseScroll controller.
        /// </summary>
        public UseScroll(ScrollView scrollView)
        {
            m_ScrollView = scrollView ?? throw new ArgumentNullException(nameof(scrollView));
            
            // Create motion values
            m_Values = new ScrollMotionValues
            {
                ScrollX = new MotionValue<float>(0.0f),
                ScrollY = new MotionValue<float>(0.0f),
                ScrollXProgress = new MotionValue<float>(0.0f),
                ScrollYProgress = new MotionValue<float>(0.0f)
            };
            
            // Subscribe to scroll events
            SubscribeToScroll();
        }
        
        private void SubscribeToScroll()
        {
            // Subscribe to scroll changed event
            m_ScrollView.RegisterCallback<GeometryChangedEvent>(OnScrollChanged);
            
            // Initial update
            UpdateScrollValues();
        }
        
        private void OnScrollChanged(GeometryChangedEvent evt)
        {
            UpdateScrollValues();
        }
        
        private void UpdateScrollValues()
        {
            if (m_ScrollView == null)
                return;
            
            // Get scroll position
            float scrollX = m_ScrollView.horizontalScroller.value;
            float scrollY = m_ScrollView.verticalScroller.value;
            
            // Calculate progress (0-1)
            float scrollXProgress = 0.0f;
            float scrollYProgress = 0.0f;
            
            if (m_ScrollView.horizontalScroller.highValue > 0.0f)
            {
                scrollXProgress = scrollX / m_ScrollView.horizontalScroller.highValue;
            }
            
            if (m_ScrollView.verticalScroller.highValue > 0.0f)
            {
                scrollYProgress = scrollY / m_ScrollView.verticalScroller.highValue;
            }
            
            // Update motion values
            m_Values.ScrollX.Set(scrollX);
            m_Values.ScrollY.Set(scrollY);
            m_Values.ScrollXProgress.Set(scrollXProgress);
            m_Values.ScrollYProgress.Set(scrollYProgress);
        }
        
        public void Dispose()
        {
            if (m_Disposed)
                return;
            
            m_Disposed = true;
            
            m_ScrollView?.UnregisterCallback<GeometryChangedEvent>(OnScrollChanged);
            
            m_Values.ScrollX.Destroy();
            m_Values.ScrollY.Destroy();
            m_Values.ScrollXProgress.Destroy();
            m_Values.ScrollYProgress.Destroy();
        }
    }
}
