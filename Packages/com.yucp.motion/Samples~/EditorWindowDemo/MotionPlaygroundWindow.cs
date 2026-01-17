#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Motion;
using YUCP.Motion.Core;
using YUCP.Motion.Controllers;

namespace YUCP.Motion.Samples
{
    /// <summary>
    /// EditorWindow playground demonstrating Motion API with controls.
    /// </summary>
    public class MotionPlaygroundWindow : EditorWindow
    {
        [MenuItem("Tools/YUCP/Others/Motion/Motion Playground")]
        public static void ShowWindow()
        {
            GetWindow<MotionPlaygroundWindow>("Motion Playground");
        }
        
        private VisualElement m_Root;
        private MotionHandle m_BoxHandle;
        private UseMotionValue<float> m_XController;
        private UseSpring<float> m_SpringController;
        
        private void CreateGUI()
        {
            // Initialize motion system
            Motion.Initialize();
            
            m_Root = rootVisualElement;
            m_Root.style.paddingLeft = 10;
            m_Root.style.paddingRight = 10;
            m_Root.style.paddingTop = 10;
            m_Root.style.paddingBottom = 10;
            
            // Title
            var title = new Label("Motion Playground")
            {
                style =
                {
                    fontSize = 24,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 20
                }
            };
            m_Root.Add(title);
            
            // Animated box
            var box = new VisualElement
            {
                style =
                {
                    width = 100,
                    height = 100,
                    backgroundColor = new Color(0.2f, 0.6f, 1.0f),
                    marginBottom = 20
                }
            };
            m_Root.Add(box);
            
            m_BoxHandle = Motion.Attach(box);
            
            // Create controllers
            m_XController = new UseMotionValue<float>(0.0f);
            m_SpringController = new UseSpring<float>(0.0f);
            
            // Sync spring to X controller
            m_XController.Value.OnChange(x =>
            {
                m_SpringController.Set(x);
            });
            
            // Sync spring value to box position
            m_SpringController.Value.OnChange(x =>
            {
                m_BoxHandle.Animate(new MotionTargets
                {
                    HasX = true,
                    X = x
                }, new Transition(0.0f)); // Immediate update
            });
            
            // Controls panel
            var controlsPanel = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    gap = 10
                }
            };
            m_Root.Add(controlsPanel);
            
            // Keyframes animation button
            var keyframesButton = new Button(() =>
            {
                m_BoxHandle.Animate(new MotionTargets
                {
                    HasX = true,
                    X = 200,
                    HasY = true,
                    Y = 100,
                    HasScaleX = true,
                    ScaleX = 1.5f,
                    HasScaleY = true,
                    ScaleY = 1.5f,
                    HasRotateDeg = true,
                    RotateDeg = 45.0f
                }, new Transition(1.0f, EasingType.EaseInOut));
            })
            {
                text = "Keyframes Animation"
            };
            controlsPanel.Add(keyframesButton);
            
            // Spring animation button
            var springButton = new Button(() =>
            {
                m_XController.Set(300.0f);
            })
            {
                text = "Spring Animation"
            };
            controlsPanel.Add(springButton);
            
            // Reset button
            var resetButton = new Button(() =>
            {
                m_BoxHandle.Animate(new MotionTargets
                {
                    HasX = true,
                    X = 0,
                    HasY = true,
                    Y = 0,
                    HasScaleX = true,
                    ScaleX = 1.0f,
                    HasScaleY = true,
                    ScaleY = 1.0f,
                    HasRotateDeg = true,
                    RotateDeg = 0.0f
                }, new Transition(0.5f, EasingType.EaseOut));
            })
            {
                text = "Reset"
            };
            controlsPanel.Add(resetButton);
            
            // Easing selector
            var easingLabel = new Label("Easing Type:");
            controlsPanel.Add(easingLabel);
            
            var easingField = new EnumField("Easing", EasingType.EaseOut);
            controlsPanel.Add(easingField);
            
            // Duration slider
            var durationLabel = new Label("Duration: 0.5s");
            controlsPanel.Add(durationLabel);
            
            var durationSlider = new Slider(0.1f, 2.0f)
            {
                value = 0.5f
            };
            durationSlider.RegisterValueChangedCallback(evt =>
            {
                durationLabel.text = $"Duration: {evt.newValue:F2}s";
            });
            controlsPanel.Add(durationSlider);
            
            // Animate with custom settings button
            var customButton = new Button(() =>
            {
                var easing = (EasingType)easingField.value;
                var duration = durationSlider.value;
                
                m_BoxHandle.Animate(new MotionTargets
                {
                    HasX = true,
                    X = UnityEngine.Random.Range(0, 300),
                    HasY = true,
                    Y = UnityEngine.Random.Range(0, 200),
                    HasOpacity = true,
                    Opacity = UnityEngine.Random.Range(0.3f, 1.0f)
                }, new Transition(duration, easing));
            })
            {
                text = "Animate with Settings"
            };
            controlsPanel.Add(customButton);
        }
        
        private void OnDisable()
        {
            m_BoxHandle?.Dispose();
            m_XController?.Dispose();
            m_SpringController?.Dispose();
        }
    }
}
#endif
