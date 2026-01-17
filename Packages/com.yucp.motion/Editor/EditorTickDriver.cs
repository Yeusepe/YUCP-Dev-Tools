#if UNITY_EDITOR
using UnityEditor;
using YUCP.Motion.Core;

namespace YUCP.Motion.Editor
{
    /// <summary>
    /// Editor tick driver using EditorApplication.update. Computes delta/now.
    /// Only active when not in play mode.
    /// </summary>
    public class EditorTickDriver : ITickDriver
    {
        private double m_LastTime;
        private bool m_Initialized;
        
        public bool IsActive => !EditorApplication.isPlaying;
        
        public EditorTickDriver()
        {
            m_LastTime = EditorApplication.timeSinceStartup;
            m_Initialized = false;
            EditorApplication.update += OnUpdate;
        }
        
        private void OnUpdate()
        {
            if (IsActive)
            {
                TickSystem.Tick();
            }
        }
        
        public FrameData GetFrameData()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = m_Initialized ? (float)(now - m_LastTime) : 0.016f; // Default to ~60fps on first frame
            m_LastTime = now;
            m_Initialized = true;
            
            return new FrameData(delta, now);
        }
        
        public void Dispose()
        {
            EditorApplication.update -= OnUpdate;
        }
    }
}
#endif

