#if YUCP_KITBASH_ENABLED
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash.UI
{
    /// <summary>
    /// Kitbash Layers Window - shadcn/ui styled layer management.
    /// Only appears when KitbashStage is active.
    /// </summary>
    public class KitbashWindow : EditorWindow
    {
        private static KitbashWindow _instance;
        
        private ListView _listView;
        private VisualElement _propsContainer;
        private VisualElement _noSelection;
        private TextField _propName;
        private Button _autoSeedBtn;
        private Button _addBtn;
        
        // --- Static Lifecycle API ---
        
        public static void OpenForStage()
        {
            var wnd = GetWindow<KitbashWindow>();
            wnd.titleContent = new GUIContent("Kitbash Layers", EditorGUIUtility.IconContent("Preset.Context").image);
            wnd.minSize = new Vector2(280, 400);
            _instance = wnd;
            wnd.Show();
            wnd.RefreshFromStage();
        }
        
        public static void CloseForStage()
        {
            if (_instance != null)
            {
                _instance.Close();
                _instance = null;
            }
        }
        
        public static void Refresh()
        {
            if (_instance != null)
                _instance.RefreshFromStage();
        }
        
        private void OnEnable() => _instance = this;
        private void OnDisable() { if (_instance == this) _instance = null; }

        private void CreateGUI()
        {
            // Load UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.devtools/Editor/PackageExporter/Kitbash/UI/KitbashWindow.uxml");
            if (visualTree == null)
            {
                rootVisualElement.Add(new Label("Error: Could not load KitbashWindow.uxml"));
                return;
            }
            visualTree.CloneTree(rootVisualElement);

            // Load USS
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.devtools/Editor/PackageExporter/Kitbash/UI/KitbashWindow.uss");
            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);

            // Query UI elements
            _listView = rootVisualElement.Q<ListView>("layer-list");
            _propsContainer = rootVisualElement.Q("props-container");
            _noSelection = rootVisualElement.Q("no-selection");
            _propName = rootVisualElement.Q<TextField>("prop-name");
            _autoSeedBtn = rootVisualElement.Q<Button>(className: "btn-outline");
            _addBtn = rootVisualElement.Q<Button>(className: "btn-primary");

            // Setup ListView
            _listView.makeItem = MakeLayerRow;
            _listView.bindItem = BindLayerRow;
            _listView.fixedItemHeight = 40;
            _listView.selectionChanged += OnLayerSelected;

            // Bind buttons
            if (_autoSeedBtn != null)
                _autoSeedBtn.clicked += OnAutoSeed;
            if (_addBtn != null)
                _addBtn.clicked += OnAddLayer;
                
            // Bind footer actions
            var fillBtn = rootVisualElement.Query<Button>().Where(b => b.text == "Fill Connected").First();
            var clearBtn = rootVisualElement.Query<Button>().Where(b => b.text == "Clear").First();
            if (fillBtn != null) fillBtn.clicked += OnFillConnected;
            if (clearBtn != null) clearBtn.clicked += OnClearLayer;
            
            // Bind name change
            if (_propName != null)
                _propName.RegisterValueChangedCallback(OnNameChanged);

            // Initial state
            UpdatePropsVisibility(false);
            RefreshFromStage();
        }

        // --- Data Binding ---
        
        private void RefreshFromStage()
        {
            var stage = KitbashStage.Current;
            if (stage == null || _listView == null)
            {
                _listView?.Clear();
                return;
            }

            _listView.itemsSource = stage.Layers;
            _listView.Rebuild();
            
            // Restore selection
            if (stage.SelectedLayerIndex >= 0 && stage.SelectedLayerIndex < stage.Layers.Count)
            {
                _listView.SetSelection(stage.SelectedLayerIndex);
            }
        }

        private VisualElement MakeLayerRow()
        {
            var row = new VisualElement();
            row.AddToClassList("list-row");

            var dot = new VisualElement { name = "dot" };
            dot.AddToClassList("color-dot");
            row.Add(dot);

            var info = new VisualElement();
            info.AddToClassList("row-info");
            row.Add(info);

            var title = new Label { name = "title" };
            title.AddToClassList("row-title");
            info.Add(title);

            var sub = new Label { name = "sub" };
            sub.AddToClassList("row-sub");
            info.Add(sub);

            var percent = new Label { name = "percent" };
            percent.style.width = 40;
            percent.style.unityTextAlign = TextAnchor.MiddleRight;
            percent.style.color = new Color(0.63f, 0.63f, 0.67f);
            percent.style.fontSize = 11;
            row.Add(percent);

            return row;
        }

        private void BindLayerRow(VisualElement element, int index)
        {
            var stage = KitbashStage.Current;
            if (stage == null || index >= stage.Layers.Count) return;

            var layer = stage.Layers[index];

            element.Q("dot").style.backgroundColor = layer.color;
            element.Q<Label>("title").text = layer.name;
            element.Q<Label>("sub").text = layer.sourceName ?? "(No source)";

            int total = stage.TotalTriangles;
            float pct = total > 0 ? (layer.triangleCount * 100f / total) : 0;
            element.Q<Label>("percent").text = $"{pct:F0}%";
        }

        private void OnLayerSelected(IEnumerable<object> selection)
        {
            var stage = KitbashStage.Current;
            if (stage == null) return;

            int idx = _listView.selectedIndex;
            stage.SelectedLayerIndex = idx;

            if (idx >= 0 && idx < stage.Layers.Count)
            {
                _propName.SetValueWithoutNotify(stage.Layers[idx].name);
                UpdatePropsVisibility(true);
            }
            else
            {
                UpdatePropsVisibility(false);
            }
        }

        private void UpdatePropsVisibility(bool hasSelection)
        {
            if (_propsContainer != null)
                _propsContainer.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            if (_noSelection != null)
                _noSelection.style.display = hasSelection ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // --- Actions ---

        private void OnAutoSeed()
        {
            var stage = KitbashStage.Current;
            if (stage == null) return;
            
            stage.AutoFillFromSubmeshes();
            RefreshFromStage();
        }

        private void OnAddLayer()
        {
            var stage = KitbashStage.Current;
            if (stage == null) return;

            stage.Layers.Add(new KitbashStage.OwnershipLayer
            {
                name = $"Layer {stage.Layers.Count}",
                sourceGuid = null,
                sourceName = "(Unassigned)",
                color = KitbashSourceLibrary.GetDefaultColor(stage.Layers.Count),
                visible = true
            });
            
            RefreshFromStage();
            _listView.SetSelection(stage.Layers.Count - 1);
        }

        private void OnFillConnected()
        {
            // TODO: Implement fill connected triangles
            Debug.Log("Fill Connected not yet implemented");
        }

        private void OnClearLayer()
        {
            var stage = KitbashStage.Current;
            if (stage == null || stage.SelectedLayerIndex < 0) return;
            
            stage.ClearLayer(stage.SelectedLayerIndex);
            RefreshFromStage();
        }

        private void OnNameChanged(ChangeEvent<string> evt)
        {
            var stage = KitbashStage.Current;
            if (stage == null) return;

            int idx = stage.SelectedLayerIndex;
            if (idx >= 0 && idx < stage.Layers.Count)
            {
                stage.Layers[idx].name = evt.newValue;
                _listView.RefreshItem(idx);
            }
        }
    }
}
#endif
