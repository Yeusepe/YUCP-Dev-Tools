#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.Motion.Samples
{
    /// <summary>
    /// Example MonoBehaviour for Inspector demo.
    /// </summary>
    public class InspectorDemo : MonoBehaviour
    {
        [SerializeField] private float m_TestValue = 1.0f;
    }
    
    /// <summary>
    /// Custom Editor showing Motion API in CreateInspectorGUI.
    /// </summary>
    [CustomEditor(typeof(InspectorDemo))]
    public class InspectorDemoEditor : Editor
    {
        private MotionHandle m_Handle;
        
        public override VisualElement CreateInspectorGUI()
        {
            // Initialize motion system
            Motion.Initialize();
            
            VisualElement root = new VisualElement();
            
            // Create an animated label
            var label = new Label("Animated Label")
            {
                style =
                {
                    fontSize = 20,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    paddingTop = 10,
                    paddingBottom = 10
                }
            };
            root.Add(label);
            
            // Attach motion
            m_Handle = Motion.Attach(label);
            
            // Add button to trigger animation
            var button = new Button(() =>
            {
                m_Handle.Animate(new MotionTargets
                {
                    HasX = true,
                    X = 50,
                    HasOpacity = true,
                    Opacity = 0.5f
                }, new Transition(0.5f, EasingType.EaseOut));
            })
            {
                text = "Animate"
            };
            root.Add(button);
            
            // Default inspector
            var defaultInspector = new IMGUIContainer(() => DrawDefaultInspector());
            root.Add(defaultInspector);
            
            return root;
        }
        
        private void OnDisable()
        {
            m_Handle?.Dispose();
        }
    }
}
#endif
