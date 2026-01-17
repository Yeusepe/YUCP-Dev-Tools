#if YUCP_KITBASH_ENABLED
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash.UI
{
    /// <summary>
    /// Kitbash Toolbar Overlay - appears in Scene View only when KitbashStage is active.
    /// </summary>
    [Overlay(typeof(SceneView), "Kitbash Toolbar", false)] // defaultDisplay = false
    public class KitbashOverlay : Overlay
    {
        private Slider _sizeSlider;
        private Button _brushBtn, _bucketBtn, _lassoBtn;
        private Button _symX, _symY;
        private Button _viewOverlay;
        
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            
            // Load UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.devtools/Editor/PackageExporter/Kitbash/UI/KitbashToolbar.uxml");
            if (visualTree != null)
                visualTree.CloneTree(root);
            else
                root.Add(new Label("Error loading toolbar"));

            // Load USS
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.devtools/Editor/PackageExporter/Kitbash/UI/KitbashWindow.uss");
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            // Cache references
            _brushBtn = root.Q<Button>("tool-brush");
            _bucketBtn = root.Q<Button>("tool-bucket");
            _lassoBtn = root.Q<Button>("tool-lasso");
            _sizeSlider = root.Q<Slider>("brush-size");
            _symX = root.Q<Button>("sym-x");
            _symY = root.Q<Button>("sym-y");
            _viewOverlay = root.Q<Button>("view-overlay");

            // Setup icons
            SetupIcons(root);
            
            // Setup interactions
            SetupInteractions(root);
            
            // Initial sync from stage
            SyncFromStage();

            return root;
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

            SetIcon("tool-brush", "d_Brush Icon", "Brush Icon");
            SetIcon("tool-bucket", "d_FloodFill Icon", "FloodFill Icon");
            SetIcon("tool-lasso", "d_Lasso Icon", "RectTransformBlueprint");
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
            
            // Update stage (need public setter - for now just visual)
            // stage.ToolMode = mode; // TODO: Add public setter
            
            // Update visuals
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
            
            // Sync tool (default to brush)
            SetTool(KitbashStage.PaintToolMode.Brush);
        }

        // --- Visibility Control ---
        
        public override void OnCreated()
        {
            base.OnCreated();
            // Subscribe to stage changes
            EditorApplication.update += CheckStageActive;
        }

        public override void OnWillBeDestroyed()
        {
            EditorApplication.update -= CheckStageActive;
            base.OnWillBeDestroyed();
        }

        private void CheckStageActive()
        {
            bool shouldBeVisible = KitbashStage.Current != null;
            if (displayed != shouldBeVisible)
            {
                displayed = shouldBeVisible;
            }
        }
    }
}
#endif
