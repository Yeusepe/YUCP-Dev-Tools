#if YUCP_KITBASH_ENABLED
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash.UI
{
    /// <summary>
    /// Manages the floating toolbar in the Scene View.
    /// Injected manually to force bottom positioning.
    /// </summary>
    public class KitbashOverlay
    {
        private static KitbashOverlay _instance;
        private VisualElement _root;
        private SceneView _activeView;
        
        private Slider _sizeSlider;
        private Button _brushBtn, _bucketBtn, _lassoBtn;
        private Button _symX, _symY;
        private Button _viewOverlay;
        
        public static void EnsureAttached(SceneView view)
        {
            if (_instance == null) _instance = new KitbashOverlay();
            _instance.Attach(view);
        }
        
        public static void EnsureDetached()
        {
            _instance?.Detach();
        }
        
        private void Attach(SceneView view)
        {
            if (_activeView == view && _root != null && _root.parent != null)
            {
                // Already attached and valid, just sync
                CheckStageActive();
                return;
            }
            
            // Detach from old if needed
            Detach();
            
            _activeView = view;
            CreateContent();
            
            if (_activeView != null && _root != null)
            {
                _activeView.rootVisualElement.Add(_root);
                EditorApplication.update += CheckStageActive;
            }
        }
        
        private void Detach()
        {
            EditorApplication.update -= CheckStageActive;
            
            if (_root != null && _root.parent != null)
            {
                _root.RemoveFromHierarchy();
            }
            _root = null;
            _activeView = null;
        }
        
        private void CreateContent()
        {
            _root = new VisualElement();
            _root.name = "kitbash-toolbar-overlay";
            _root.pickingMode = PickingMode.Ignore; // Let clicks pass through container
            
            // Layout container to center bottom
            _root.style.position = Position.Absolute;
            _root.style.bottom = 16;
            _root.style.left = 0;
            _root.style.right = 0;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.FlexEnd;
            _root.style.flexDirection = FlexDirection.Column;
            
            // Load UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.devtools/Editor/PackageExporter/Kitbash/UI/KitbashToolbar.uxml");
            if (visualTree != null)
            {
                var content = visualTree.CloneTree();
                _root.Add(content);
            }
            else
                _root.Add(new Label("Error loading toolbar"));

            // Load USS
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.devtools/Editor/PackageExporter/Kitbash/UI/KitbashWindow.uss");
            if (styleSheet != null)
                _root.styleSheets.Add(styleSheet);

            // Cache references
            _brushBtn = _root.Q<Button>("tool-brush");
            _bucketBtn = _root.Q<Button>("tool-bucket");
            _lassoBtn = _root.Q<Button>("tool-lasso");
            _sizeSlider = _root.Q<Slider>("brush-size");
            _symX = _root.Q<Button>("sym-x");
            _symY = _root.Q<Button>("sym-y");
            _viewOverlay = _root.Q<Button>("view-overlay");

            // Setup icons
            SetupIcons(_root);
            
            // Setup interactions
            SetupInteractions(_root);
            
            // Initial sync
            CheckStageActive();
        }

        private void SetupIcons(VisualElement root)
        {
            void SetIcon(string buttonName, string iconName, string fallback = "")
            {
                var btn = root.Q<Button>(buttonName);
                if (btn == null) return;
                
                var iconEl = btn.Q(className: "icon");
                if (iconEl == null) return;
                
                var content = EditorGUIUtility.IconContent(iconName);
                if ((content == null || content.image == null) && !string.IsNullOrEmpty(fallback))
                    content = EditorGUIUtility.IconContent(fallback);
                    
                if (content?.image != null)
                    iconEl.style.backgroundImage = content.image as Texture2D;
            }

            SetIcon("tool-brush", "d_Grid.PaintTool", "Grid.PaintTool");
            SetIcon("tool-bucket", "d_Grid.FillTool", "Grid.FillTool");
            SetIcon("tool-lasso", "d_RectTool", "RectTool");
            SetIcon("view-overlay", "d_SceneViewVisibility", "SceneViewVisibility");
        }

        private void SetupInteractions(VisualElement root)
        {
            // Tool selection
            _brushBtn?.RegisterCallback<ClickEvent>(e => SetTool(KitbashStage.PaintToolMode.Brush));
            _bucketBtn?.RegisterCallback<ClickEvent>(e => SetTool(KitbashStage.PaintToolMode.Bucket));
            // Lasso not implemented yet
            
            // Brush size
            _sizeSlider?.RegisterValueChangedCallback(evt => {
                var stage = KitbashStage.Current;
                if (stage != null)
                    stage.BrushSize = Mathf.Lerp(0.01f, 0.5f, evt.newValue);
            });
            
            // Symmetry toggles
            _symX?.RegisterCallback<ClickEvent>(e => {
                var stage = KitbashStage.Current;
                if (stage != null)
                {
                    stage.MirrorX = !stage.MirrorX;
                    UpdateToggle(_symX, stage.MirrorX);
                }
            });
            
            _symY?.RegisterCallback<ClickEvent>(e => {
                var stage = KitbashStage.Current;
                if (stage != null)
                {
                    stage.MirrorY = !stage.MirrorY;
                    UpdateToggle(_symY, stage.MirrorY);
                }
            });
            
            // View overlay toggle
            _viewOverlay?.RegisterCallback<ClickEvent>(e => {
                var stage = KitbashStage.Current;
                if (stage != null)
                {
                    stage.PaintMode = !stage.PaintMode;
                    UpdateToggle(_viewOverlay, stage.PaintMode);
                }
            });
        }

        private void SetTool(KitbashStage.PaintToolMode mode)
        {
            var stage = KitbashStage.Current;
            if (stage == null) return;
            
            stage.ToolMode = mode;
            
            // Update visuals
            SetToolVisuals(mode);
        }

        private void UpdateToggle(Button btn, bool active)
        {
            if (btn == null) return;
            if (active)
                btn.AddToClassList("selected");
            else
                btn.RemoveFromClassList("selected");
        }

        private void SyncFromStage()
        {
            var stage = KitbashStage.Current;
            if (stage == null) return;

            // Sync brush size (reverse lerp)
            float t = Mathf.InverseLerp(0.01f, 0.5f, stage.BrushSize);
            _sizeSlider?.SetValueWithoutNotify(t);
            
            // Sync toggles
            UpdateToggle(_symX, stage.MirrorX);
            UpdateToggle(_symY, stage.MirrorY);
            UpdateToggle(_viewOverlay, stage.PaintMode);
            
            // Sync tool
            SetToolVisuals(stage.ToolMode);
        }
        
        private void SetToolVisuals(KitbashStage.PaintToolMode mode)
        {
            _brushBtn?.RemoveFromClassList("selected");
            _bucketBtn?.RemoveFromClassList("selected");
            _lassoBtn?.RemoveFromClassList("selected");
            
            switch (mode)
            {
                case KitbashStage.PaintToolMode.Brush:
                    _brushBtn?.AddToClassList("selected");
                    break;
                case KitbashStage.PaintToolMode.Bucket:
                    _bucketBtn?.AddToClassList("selected");
                    break;
            }
        }

        private void CheckStageActive()
        {
            bool shouldBeVisible = KitbashStage.Current != null;
            
            if (_root != null)
                _root.style.display = shouldBeVisible ? DisplayStyle.Flex : DisplayStyle.None;
            
            if (shouldBeVisible)
            {
                SyncFromStage();
            }
        }
    }
}
#endif
