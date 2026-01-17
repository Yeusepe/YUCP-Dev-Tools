using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.Motion.Samples
{
    /// <summary>
    /// Runtime demo using UIDocument.
    /// </summary>
    public class RuntimeDemo : MonoBehaviour
    {
        private UIDocument m_UIDocument;
        private MotionHandle m_Handle;
        
        private void Start()
        {
            // Initialize motion system
            Motion.Initialize();
            
            m_UIDocument = GetComponent<UIDocument>();
            if (m_UIDocument == null)
            {
                Debug.LogError("RuntimeDemo requires a UIDocument component");
                return;
            }
            
            VisualElement root = m_UIDocument.rootVisualElement;
            
            // Create an animated box
            var box = new VisualElement
            {
                style =
                {
                    width = 100,
                    height = 100,
                    backgroundColor = new Color(1.0f, 0.4f, 0.2f),
                    position = Position.Absolute,
                    left = 100,
                    top = 100
                }
            };
            root.Add(box);
            
            // Attach motion
            m_Handle = Motion.Attach(box);
            
            // Add button to trigger animation
            var button = new Button(() =>
            {
                m_Handle.Animate(new MotionTargets
                {
                    HasX = true,
                    X = 300,
                    HasY = true,
                    Y = 200,
                    HasScaleX = true,
                    ScaleX = 1.5f,
                    HasScaleY = true,
                    ScaleY = 1.5f
                }, new Transition(1.0f, EasingType.EaseInOut));
            })
            {
                text = "Animate Box"
            };
            root.Add(button);
        }
        
        private void OnDestroy()
        {
            m_Handle?.Dispose();
        }
    }
}
