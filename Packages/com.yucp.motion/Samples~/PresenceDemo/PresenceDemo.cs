#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.Motion.Samples
{
    /// <summary>
    /// AnimatePresence demo showing exit animations.
    /// </summary>
    public class PresenceDemo : EditorWindow
    {
        [MenuItem("Tools/YUCP/Others/Motion/Motion Presence Demo")]
        public static void ShowWindow()
        {
            GetWindow<PresenceDemo>("Presence Demo");
        }
        
        private AnimatePresenceContainer m_PresenceContainer;
        private List<VisualElement> m_Elements = new List<VisualElement>();
        private int m_NextId = 1;
        
        private void CreateGUI()
        {
            Motion.Initialize();
            
            var root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;
            
            // Title
            var title = new Label("AnimatePresence Demo")
            {
                style =
                {
                    fontSize = 20,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 20
                }
            };
            root.Add(title);
            
            // Create presence container
            m_PresenceContainer = new AnimatePresenceContainer();
            m_PresenceContainer.style.flexDirection = FlexDirection.Column;
            m_PresenceContainer.style.gap = 10;
            root.Add(m_PresenceContainer);
            
            // Controls
            var controlsPanel = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    gap = 10,
                    marginTop = 20
                }
            };
            root.Add(controlsPanel);
            
            // Add button
            var addButton = new Button(AddItem)
            {
                text = "Add Item"
            };
            controlsPanel.Add(addButton);
            
            // Remove button
            var removeButton = new Button(RemoveItem)
            {
                text = "Remove Last Item"
            };
            controlsPanel.Add(removeButton);
            
            // Clear button
            var clearButton = new Button(ClearAll)
            {
                text = "Clear All"
            };
            controlsPanel.Add(clearButton);
            
            // Add initial items
            for (int i = 0; i < 3; i++)
            {
                AddItem();
            }
        }
        
        private void AddItem()
        {
            var item = new VisualElement
            {
                style =
                {
                    width = 200,
                    height = 50,
                    backgroundColor = new Color(
                        UnityEngine.Random.Range(0.3f, 1.0f),
                        UnityEngine.Random.Range(0.3f, 1.0f),
                        UnityEngine.Random.Range(0.3f, 1.0f)
                    ),
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 5,
                    paddingBottom = 5
                }
            };
            
            var label = new Label($"Item {m_NextId++}");
            item.Add(label);
            
            // Define exit animation
            var exitAnimation = new MotionTargets
            {
                HasX = true,
                X = -300,
                HasOpacity = true,
                Opacity = 0.0f
            };
            
            m_PresenceContainer.AddChildWithPresence(item, exitAnimation);
            m_Elements.Add(item);
        }
        
        private void RemoveItem()
        {
            if (m_Elements.Count == 0)
                return;
            
            var lastItem = m_Elements[m_Elements.Count - 1];
            m_Elements.RemoveAt(m_Elements.Count - 1);
            
            m_PresenceContainer.RemoveChildWithPresence(lastItem);
        }
        
        private void ClearAll()
        {
            while (m_Elements.Count > 0)
            {
                RemoveItem();
            }
        }
        
        private void OnDisable()
        {
            m_PresenceContainer?.Cleanup();
            m_Elements.Clear();
        }
    }
}
#endif
