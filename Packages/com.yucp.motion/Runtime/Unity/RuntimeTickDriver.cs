using UnityEngine;
using YUCP.Motion.Core;

namespace YUCP.Motion
{
    /// <summary>
    /// Runtime tick driver using hidden MonoBehaviour. Computes delta/now.
    /// </summary>
    internal class RuntimeTickDriver : MonoBehaviour, ITickDriver
    {
        private static RuntimeTickDriver s_Instance;
        private double m_LastTime;
        private bool m_Initialized;
        
        public bool IsActive => isActiveAndEnabled;
        
        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            s_Instance = this;
            DontDestroyOnLoad(gameObject);
            m_LastTime = Time.realtimeSinceStartupAsDouble;
            m_Initialized = false;
        }
        
        private void Update()
        {
            TickSystem.Tick();
        }
        
        public FrameData GetFrameData()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float delta = m_Initialized ? Time.deltaTime : 0.016f; // Default to ~60fps on first frame
            m_LastTime = now;
            m_Initialized = true;
            
            return new FrameData(delta, now);
        }
        
        /// <summary>
        /// Creates or gets the runtime tick driver instance.
        /// </summary>
        public static RuntimeTickDriver GetOrCreate()
        {
            if (s_Instance != null)
                return s_Instance;
            
            GameObject go = new GameObject("[YUCP.Motion.RuntimeTickDriver]");
            go.hideFlags = HideFlags.HideAndDontSave;
            s_Instance = go.AddComponent<RuntimeTickDriver>();
            return s_Instance;
        }
    }
}
