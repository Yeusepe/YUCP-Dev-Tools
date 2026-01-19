#if YUCP_KITBASH_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
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
        
        // ViewModel for the ListView
        private class LayerListItem
        {
            public bool IsHeader;
            public string Label;
            public int LayerIndex; // -1 if header
        }
        
        private List<LayerListItem> _viewItems = new List<LayerListItem>();
        
        private ListView _listView;
        private VisualElement _propsContainer;
        private VisualElement _noSelection;
        private VisualElement _fillSettings;
        
        private TextField _propName;
        private Button _propSourceBtn;
        private Button _autoSeedBtn;
        private Button _clearLayerBtn; 
        private Button _addBtn;
        private TextField _searchField;
        
        // Fill Mode Buttons
        private Button _fillConnectedBtn;
        private Button _fillUvBtn;
        private Button _fillSubmeshBtn;
        
        // Navigation & UV
        private Button _backBtn;
        private VisualElement _uvSettings;
        private DropdownField _uvDropdown;
        
        // --- Static Lifecycle API ---
        
        public static void OpenForStage()
        {
            var wnd = GetWindow<KitbashWindow>();
            wnd.titleContent = new GUIContent("Kitbash Layers", EditorGUIUtility.IconContent("Preset.Context").image);
            wnd.minSize = new Vector2(280, 500);
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
            _fillSettings = rootVisualElement.Q("fill-settings");
            
            _propName = rootVisualElement.Q<TextField>("prop-name");
            _propSourceBtn = rootVisualElement.Q<Button>("prop-source-btn");
            _autoSeedBtn = rootVisualElement.Q<Button>("auto-seed-btn");
            _clearLayerBtn = rootVisualElement.Q<Button>("clear-layer-btn");
            _addBtn = rootVisualElement.Q<Button>("add-layer-btn");
            _searchField = rootVisualElement.Q<TextField>("search-input");
            
            // Fill Buttons
            _fillConnectedBtn = rootVisualElement.Q<Button>("fill-connected");
            _fillUvBtn = rootVisualElement.Q<Button>("fill-uv");
            _fillSubmeshBtn = rootVisualElement.Q<Button>("fill-submesh");
            
            // Navigation & UV
            _backBtn = rootVisualElement.Q<Button>("back-btn");
            _uvSettings = rootVisualElement.Q("uv-settings");
            _uvDropdown = rootVisualElement.Q<DropdownField>("uv-channel-dropdown");

            // Setup ListView
            _listView.makeItem = MakeLayerRow;
            _listView.bindItem = BindLayerRow;
            _listView.fixedItemHeight = 36;
            _listView.selectionChanged += OnLayerSelected;
            // Native drag and drop reordering
            _listView.reorderable = true;
            _listView.itemIndexChanged += OnLayerReordered;

            // Bind buttons
            if (_backBtn != null) _backBtn.clicked += OnBack;
            if (_addBtn != null) _addBtn.clicked += OnAddLayer;
            if (_autoSeedBtn != null) _autoSeedBtn.clicked += OnAutoSeed;
            if (_clearLayerBtn != null) _clearLayerBtn.clicked += OnClearLayer;
            if (_propSourceBtn != null) _propSourceBtn.clicked += OnSelectSource;
            
            // Bind Fill Mode
            if (_fillConnectedBtn != null) _fillConnectedBtn.clicked += () => SetFillMode(KitbashStage.FillMode.Connected);
            if (_fillUvBtn != null) _fillUvBtn.clicked += () => SetFillMode(KitbashStage.FillMode.UVIsland);
            if (_fillSubmeshBtn != null) _fillSubmeshBtn.clicked += () => SetFillMode(KitbashStage.FillMode.Submesh);

            // Bind properties
            if (_propName != null)
                _propName.RegisterValueChangedCallback(OnNameChanged);
                
            if (_uvDropdown != null)
                _uvDropdown.RegisterValueChangedCallback(OnUVChannelChanged);

            // Initial state
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

            // Build View Items (Grouping)
            _viewItems.Clear();
            var grouped = stage.Layers
                .Select((layer, index) => new { Layer = layer, Index = index })
                .GroupBy(x => x.Layer.group ?? "Ungrouped"); // Group by new 'group' field
                
            foreach (var group in grouped)
            {
                // Add Header (skip for single unknown layer or if desired)
                if (group.Key != "Ungrouped" || stage.Layers.Count > 1) 
                {
                    _viewItems.Add(new LayerListItem { IsHeader = true, Label = group.Key, LayerIndex = -1 });
                }
                
                // Add Items
                foreach (var item in group)
                {
                    _viewItems.Add(new LayerListItem { IsHeader = false, Label = item.Layer.name, LayerIndex = item.Index });
                }
            }

            _listView.itemsSource = _viewItems;
            _listView.Rebuild();
            
            // Restore selection
            if (stage.SelectedLayerIndex >= 0 && stage.SelectedLayerIndex < stage.Layers.Count)
            {
                // Find the view item corresponding to this index
                int viewIdx = _viewItems.FindIndex(x => !x.IsHeader && x.LayerIndex == stage.SelectedLayerIndex);
                if (viewIdx >= 0) _listView.SetSelection(viewIdx);
                else _listView.ClearSelection();
            }
            
            // Update Fill Mode UI
            UpdateFillModeUI(stage.CurrentFillMode);
            
            // Update UV Dropdown
            if (_uvDropdown != null)
                _uvDropdown.SetValueWithoutNotify($"UV{stage.UVChannel}");
            
            // Update Props Visibility
            UpdatePropsVisibility(stage.SelectedLayerIndex >= 0);
        }

        private VisualElement MakeLayerRow()
        {
            var row = new VisualElement();
            row.AddToClassList("list-row");

            // Header Element (Hidden by default)
            var header = new Label();
            header.name = "header-idx"; // Using unique name
            header.AddToClassList("field-label"); // Reuse label style
            header.style.display = DisplayStyle.None;
            header.style.paddingLeft = 4;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(header);

            // Layer Container (Visible by default)
            var layerContainer = new VisualElement();
            layerContainer.name = "layer-container";
            layerContainer.style.flexDirection = FlexDirection.Row;
            layerContainer.style.flexGrow = 1;
            layerContainer.style.alignItems = Align.Center;
            row.Add(layerContainer);

            // Visibility Toggle
            var visBtn = new Button();
            visBtn.name = "vis-btn";
            visBtn.AddToClassList("row-visibility-btn");
            var visIcon = new VisualElement();
            visIcon.AddToClassList("icon-eye-small");
            visBtn.Add(visIcon);
            layerContainer.Add(visBtn);
            
            // Persistent Listener using userData
            visBtn.clicked += () => {
                if (row.userData is LayerListItem item && !item.IsHeader)
                {
                    var stage = KitbashStage.Current;
                    if (stage != null && item.LayerIndex < stage.Layers.Count)
                    {
                        var l = stage.Layers[item.LayerIndex];
                        l.visible = !l.visible;
                        stage.UpdateVisualization();
                        Refresh();
                    }
                }
            };

            // Color Swatch
            var swatch = new VisualElement { name = "swatch" };
            swatch.AddToClassList("row-color-swatch");
            layerContainer.Add(swatch);

            // Content
            var content = new VisualElement();
            content.AddToClassList("row-content");
            layerContainer.Add(content);

            var title = new Label { name = "title" };
            title.AddToClassList("row-title");
            content.Add(title);

            var sub = new Label { name = "sub" };
            sub.AddToClassList("row-sub");
            content.Add(sub);

            // Stats
            var stats = new VisualElement();
            stats.AddToClassList("row-stats");
            layerContainer.Add(stats);
            
            var percent = new Label { name = "percent" };
            percent.AddToClassList("row-percent");
            stats.Add(percent);

            return row;
        }

        private void BindLayerRow(VisualElement element, int index)
        {
            if (index >= _viewItems.Count) return;
            var item = _viewItems[index];

            // Update userData for listeners
            element.userData = item;
            
            var header = element.Q<Label>("header-idx");
            var container = element.Q("layer-container");

            if (item.IsHeader)
            {
                header.text = item.Label.ToUpper();
                header.style.display = DisplayStyle.Flex;
                container.style.display = DisplayStyle.None;
                
                // Style fix for header row
                element.style.backgroundColor = new Color(0,0,0,0.2f); // Darker background for header
                return;
            }

            // It's a layer
            header.style.display = DisplayStyle.None;
            container.style.display = DisplayStyle.Flex;
            element.style.backgroundColor = StyleKeyword.Null; // Reset bg

            var stage = KitbashStage.Current;
            if (stage == null || item.LayerIndex >= stage.Layers.Count) return;
            
            var layer = stage.Layers[item.LayerIndex];
            
            // Indent layer
            container.style.paddingLeft = 16; 

            // Bind Visibility
            var visBtn = element.Q<Button>("vis-btn");
            visBtn.RemoveFromClassList("visible");
            if (layer.visible) visBtn.AddToClassList("visible");
            // Listener is already attached in MakeLayerRow and uses element.userData

            // Bind Data
            var swatch = element.Q("swatch");
            swatch.style.backgroundColor = layer.color;
            
            var title = element.Q<Label>("title");
            title.text = layer.name;
            title.style.color = layer.visible ? new Color(0.98f, 0.98f, 0.98f) : new Color(0.5f, 0.5f, 0.5f);

            element.Q<Label>("sub").text = layer.sourceName ?? "(No source)";

            int total = stage.TotalTriangles;
            float pct = total > 0 ? (layer.triangleCount * 100f / total) : 0;
            element.Q<Label>("percent").text = $"{pct:F0}%";
        }

        private void OnLayerSelected(IEnumerable<object> selection)
        {
            var stage = KitbashStage.Current;
            if (stage == null) return;

            // Get selected view item
            var obj = _listView.selectedItem as LayerListItem;
            if (obj == null || obj.IsHeader)
            {
                // Deselect if header clicked, or handle group selection
                if (obj != null && obj.IsHeader) _listView.ClearSelection();
                
                UpdatePropsVisibility(false);
                return;
            }

            int layerIdx = obj.LayerIndex;
            stage.SelectedLayerIndex = layerIdx;

            if (layerIdx >= 0 && layerIdx < stage.Layers.Count)
            {
                var layer = stage.Layers[layerIdx];
                _propName.SetValueWithoutNotify(layer.name);
                if (_propSourceBtn != null) 
                    _propSourceBtn.text = string.IsNullOrEmpty(layer.sourceName) ? "Select Source..." : layer.sourceName;
                UpdatePropsVisibility(true);
            }
        }
        
        private void OnLayerReordered(int oldIndex, int newIndex)
        {
             // Disable reordering for now as it conflicts with grouping logic complexity
             // Ideally we implement reordering WITHIN groups or move groups
             // Rebuilding will reset order anyway based on groups
             RefreshFromStage();
        }

        private void UpdatePropsVisibility(bool hasSelection)
        {
            if (_propsContainer != null)
                _propsContainer.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            if (_noSelection != null)
                _noSelection.style.display = hasSelection ? DisplayStyle.None : DisplayStyle.Flex;
        }
        
        // --- Fill Mode ---
        
        private void SetFillMode(KitbashStage.FillMode mode)
        {
            var stage = KitbashStage.Current;
            if (stage == null) return;
            
            stage.CurrentFillMode = mode;
            UpdateFillModeUI(mode);
        }
        
        private void UpdateFillModeUI(KitbashStage.FillMode mode)
        {
            _fillConnectedBtn?.RemoveFromClassList("selected");
            _fillUvBtn?.RemoveFromClassList("selected");
            _fillSubmeshBtn?.RemoveFromClassList("selected");
            
            switch (mode)
            {
                case KitbashStage.FillMode.Connected: _fillConnectedBtn?.AddToClassList("selected"); break;
                case KitbashStage.FillMode.UVIsland: _fillUvBtn?.AddToClassList("selected"); break;
                case KitbashStage.FillMode.Submesh: _fillSubmeshBtn?.AddToClassList("selected"); break;
            }
            
            if (_uvSettings != null)
            {
                _uvSettings.style.display = (mode == KitbashStage.FillMode.UVIsland) 
                    ? DisplayStyle.Flex 
                    : DisplayStyle.None;
            }
        }

        // --- Actions ---

        private void OnAutoSeed()
        {
            var stage = KitbashStage.Current;
            if (stage == null) return;
            
            stage.AutoDetectLayers();
            RefreshFromStage(); // Refresh triangle counts
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

        private void OnClearLayer()
        {
            var stage = KitbashStage.Current;
            if (stage == null || stage.SelectedLayerIndex < 0) return;
            
            stage.ClearLayer(stage.SelectedLayerIndex);
            stage.UpdateVisualization();
            RefreshFromStage(); // Update counts
        }
        
        private void OnSelectSource()
        {
            // Simple dropdown or object picker placeholder
             var stage = KitbashStage.Current;
             if (stage == null || stage.SelectedLayerIndex < 0) return;
             
             // TODO: Real object picker
             Debug.Log("Source picking not implemented in this demo.");
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
        
        private void OnBack()
        {
            KitbashStage.Exit(false);
        }
        
        private void OnUVChannelChanged(ChangeEvent<string> evt)
        {
            var stage = KitbashStage.Current;
            if (stage == null) return;
            
            // Parse "UV0", "UV1", etc.
            string val = evt.newValue;
            if (val.StartsWith("UV") && int.TryParse(val.Substring(2), out int channel))
            {
                stage.UVChannel = channel;
            }
        }
    }
}
#endif
