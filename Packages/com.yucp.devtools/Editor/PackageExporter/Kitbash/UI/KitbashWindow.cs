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
            public bool IsLastInGroup; // For visual hierarchy
            public bool HasSelectedChild; // If true, one of the children is selected
        }
        
        private List<LayerListItem> _viewItems = new List<LayerListItem>();
        private HashSet<string> _collapsedGroups = new HashSet<string>();
        
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
            wnd.titleContent = new GUIContent("Asset Layers", EditorGUIUtility.IconContent("Preset.Context").image);
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
            _listView.fixedItemHeight = 42; // Taller rows for modern feel
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
            
            // Identify active group
            string activeGroupName = null;
            if (stage.SelectedLayerIndex >= 0 && stage.SelectedLayerIndex < stage.Layers.Count)
            {
                activeGroupName = stage.Layers[stage.SelectedLayerIndex].group;
            }
                
            foreach (var group in grouped)
            {
                bool isGrouped = group.Key != "Ungrouped";
                
                // Never show "Ungrouped" header. Only show headers for actual named groups.
                if (isGrouped)
                {
                    bool isActiveGroup = (activeGroupName == group.Key);
                    _viewItems.Add(new LayerListItem 
                    { 
                        IsHeader = true, 
                        Label = group.Key, 
                        LayerIndex = -1,
                        HasSelectedChild = isActiveGroup
                    });
                }
                
                // If collapsed, skip adding items (effectively hiding them)
                if (isGrouped && _collapsedGroups.Contains(group.Key))
                {
                    continue;
                }
                
                var itemsInGroup = group.ToList();
                for (int i = 0; i < itemsInGroup.Count; i++)
                {
                    var item = itemsInGroup[i];
                    _viewItems.Add(new LayerListItem 
                    { 
                        IsHeader = false, 
                        Label = item.Layer.name, 
                        LayerIndex = item.Index,
                        IsLastInGroup = (i == itemsInGroup.Count - 1) && (group.Key != "Ungrouped")
                    });
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
            row.AddToClassList("kitbash-layer-row");

            // --- Header Mode Elements ---
            var headerContainer = new VisualElement { name = "header-container" };
            headerContainer.AddToClassList("kitbash-layer-header");
            row.Add(headerContainer);
            
            // Interaction handler for folding
            headerContainer.RegisterCallback<ClickEvent>(evt => {
                if (row.userData is LayerListItem item && item.IsHeader) {
                    if (_collapsedGroups.Contains(item.Label))
                        _collapsedGroups.Remove(item.Label);
                    else
                        _collapsedGroups.Add(item.Label);
                        
                    RefreshFromStage();
                    evt.StopPropagation(); 
                }
            });

            var headerArrow = new VisualElement { name = "header-arrow" };
            headerArrow.AddToClassList("kitbash-header-arrow");
            headerContainer.Add(headerArrow);

            var headerIcon = new VisualElement();
            headerIcon.AddToClassList("kitbash-header-icon"); // Folder icon
            headerContainer.Add(headerIcon);

            var headerLabel = new Label();
            headerLabel.name = "header-label";
            headerLabel.AddToClassList("kitbash-header-label");
            headerContainer.Add(headerLabel);

            // --- Layer Mode Elements ---
            var layerContainer = new VisualElement { name = "layer-container" };
            layerContainer.AddToClassList("kitbash-layer-content");
            row.Add(layerContainer);
            
            // 0. Hierarchy Tree Guide (New)
            var treeGuide = new VisualElement { name = "tree-guide" };
            treeGuide.AddToClassList("kitbash-tree-guide");
            var treeLineV = new VisualElement();
            treeLineV.AddToClassList("tree-line-v");
            treeGuide.Add(treeLineV);
            var treeLineH = new VisualElement();
            treeLineH.AddToClassList("tree-line-h");
            treeGuide.Add(treeLineH);
            layerContainer.Add(treeGuide);

            // 1. Visibility Toggle (Leftmost)
            var visBtn = new Button();
            visBtn.name = "vis-btn";
            visBtn.AddToClassList("kitbash-vis-toggle");
            var visIcon = new VisualElement();
            visIcon.name = "vis-icon";
            visIcon.AddToClassList("kitbash-vis-icon");
            visBtn.Add(visIcon);
            layerContainer.Add(visBtn);
            
            // Click Handler for visibility
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

            // 2. Color Bar (Vertical accent)
            var colorBar = new VisualElement { name = "color-bar" };
            colorBar.AddToClassList("kitbash-layer-color-bar");
            layerContainer.Add(colorBar);

            // 3. Info Column (Name + Source/Meta)
            var infoCol = new VisualElement();
            infoCol.AddToClassList("kitbash-layer-info");
            layerContainer.Add(infoCol);

            var nameLabel = new Label { name = "layer-name" };
            nameLabel.AddToClassList("kitbash-layer-name");
            infoCol.Add(nameLabel);

            var metaLabel = new Label { name = "layer-meta" };
            metaLabel.AddToClassList("kitbash-layer-meta");
            infoCol.Add(metaLabel);

            // 4. Stats Badge (Right aligned)
            var statsBadge = new VisualElement();
            statsBadge.AddToClassList("kitbash-layer-stats");
            layerContainer.Add(statsBadge);

            var countLabel = new Label { name = "tri-count" };
            countLabel.AddToClassList("kitbash-layer-count");
            statsBadge.Add(countLabel);

            return row;
        }

        private void BindLayerRow(VisualElement element, int index)
        {
            if (index >= _viewItems.Count) return;
            var item = _viewItems[index];
            element.userData = item;

            var headerContainer = element.Q("header-container");
            var layerContainer = element.Q("layer-container");

            if (item.IsHeader)
            {
                // Show Header, Hide Layer
                headerContainer.style.display = DisplayStyle.Flex;
                layerContainer.style.display = DisplayStyle.None;
                
                element.Q<Label>("header-label").text = item.Label;
                
                // Rotated arrow if expanded
                var arrow = element.Q("header-arrow");
                bool isCollapsed = _collapsedGroups.Contains(item.Label);
                // Class 'collapsed' will rotate -90 or similar
                if (isCollapsed) arrow.AddToClassList("is-collapsed");
                else arrow.RemoveFromClassList("is-collapsed");
                
                // Highlight if child is selected
                if (item.HasSelectedChild) headerContainer.AddToClassList("has-selection");
                else headerContainer.RemoveFromClassList("has-selection");
                
                // Reset interaction styles
                element.AddToClassList("is-header");
                element.RemoveFromClassList("is-layer");
                return;
            }

            // Show Layer, Hide Header
            headerContainer.style.display = DisplayStyle.None;
            layerContainer.style.display = DisplayStyle.Flex;
            
            element.RemoveFromClassList("is-header");
            element.AddToClassList("is-layer");

            var stage = KitbashStage.Current;
            if (stage == null || item.LayerIndex >= stage.Layers.Count) return;
            
            var layer = stage.Layers[item.LayerIndex];

            // 0. Tree Guide Logic
            var treeGuide = element.Q("tree-guide");
            // Only show tree guide if we are part of a named group
            bool isInGroup = !string.IsNullOrEmpty(layer.group) && layer.group != "Ungrouped";
            
            if (isInGroup)
            {
                treeGuide.style.display = DisplayStyle.Flex;
                if (item.IsLastInGroup) treeGuide.AddToClassList("is-last");
                else treeGuide.RemoveFromClassList("is-last");
            }
            else
            {
                treeGuide.style.display = DisplayStyle.None;
            }

            // 1. Visibility
            var visBtn = element.Q<Button>("vis-btn");
            var visIcon = visBtn.Q("vis-icon");
            // Toggle class based on state for styling opacity/icon
            if (layer.visible) 
            {
                visBtn.AddToClassList("is-visible");
                visIcon.style.opacity = 1f;
            }
            else 
            {
                visBtn.RemoveFromClassList("is-visible");
                visIcon.style.opacity = 0.3f;
            }

            // 2. Color Bar
            var colorBar = element.Q("color-bar");
            colorBar.style.backgroundColor = layer.color;

            // 3. Name & Meta
            var nameLabel = element.Q<Label>("layer-name");
            nameLabel.text = layer.name;
            
            var metaLabel = element.Q<Label>("layer-meta");
            // Show source name or "Empty" if 0 tris
            int triCount = layer.triangleCount;
            if (triCount == 0) metaLabel.text = "Empty";
            else metaLabel.text = string.IsNullOrEmpty(layer.sourceName) ? "No Source" : layer.sourceName;

            // 4. Stats
            var countLabel = element.Q<Label>("tri-count");
            // Format: 1.2k or 500
            if (triCount > 1000) countLabel.text = $"{triCount/1000f:F1}k";
            else countLabel.text = triCount.ToString();
            
            // Add tooltip with full count
            countLabel.tooltip = $"{triCount:N0} Triangles";
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
