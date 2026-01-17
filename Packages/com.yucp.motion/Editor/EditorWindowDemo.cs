#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.Motion.Editor
{
    /// <summary>
    /// EditorWindow demo showing Motion API usage.
    /// </summary>
    public class EditorWindowDemo : EditorWindow
    {
        [MenuItem("Tools/YUCP/Others/Motion/EditorWindow Demo")]
        public static void Open()
        {
            EditorWindowDemo window = GetWindow<EditorWindowDemo>();
            window.titleContent = new GUIContent("Motion EditorWindow Demo");
            window.Show();
        }
        
        private void CreateGUI()
        {
            // Initialize motion system
            Motion.Initialize();
            
            VisualElement root = rootVisualElement;
            
            // Create a container
            var container = new VisualElement
            {
                style =
                {
                    width = Length.Percent(100),
                    height = Length.Percent(100),
                    paddingTop = 20,
                    paddingBottom = 20,
                    paddingLeft = 20,
                    paddingRight = 20
                }
            };
            root.Add(container);
            
            // Create an animated box
            var box = new VisualElement
            {
                style =
                {
                    width = 100,
                    height = 100,
                    backgroundColor = new Color(0.2f, 0.6f, 1.0f),
                    position = Position.Absolute,
                    left = 50,
                    top = 50
                }
            };
            container.Add(box);
            
            // Attach motion
            var handle = Motion.Attach(box);
            
            // Add buttons to trigger animations
            var button1 = new Button(() =>
            {
                handle.Animate(new MotionTargets
                {
                    HasX = true,
                    X = 200,
                    HasY = true,
                    Y = 100
                });
            })
            {
                text = "Move Right"
            };
            container.Add(button1);
            
            var button2 = new Button(() =>
            {
                handle.Animate(new MotionTargets
                {
                    HasX = true,
                    X = 50,
                    HasY = true,
                    Y = 50
                });
            })
            {
                text = "Reset"
            };
            container.Add(button2);
            
            var button3 = new Button(() =>
            {
                handle.Animate(new MotionTargets
                {
                    HasScaleX = true,
                    ScaleX = 1.5f,
                    HasScaleY = true,
                    ScaleY = 1.5f
                });
            })
            {
                text = "Scale Up"
            };
            container.Add(button3);
        }
    }
}
#endif
