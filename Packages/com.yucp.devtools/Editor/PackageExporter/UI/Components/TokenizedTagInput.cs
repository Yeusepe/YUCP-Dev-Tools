using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter.UI.Components
{
    public class TokenizedTagInput : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<TokenizedTagInput> { }

        private readonly VisualElement _chipsContainer;
        private readonly TextField _inputField;
        private readonly ListView _autocompleteList;
        private VisualElement _overlayContainer;
        private VisualElement _externalRoot;
        private bool _isAttachedToExternalRoot = false;

        private List<string> _availableTags = new List<string>();
        private List<string> _selectedTags = new List<string>();
        private List<string> _filteredOptions = new List<string>();

        // Events
        public event Action<IReadOnlyList<string>> OnTagsChanged;
        public event Action<string, Color> OnTagColorChanged;
        public event Action<string> OnTagDeleted;
        
        // Configuration
        public Func<string, Color> TagColorProvider;

        public IReadOnlyList<string> SelectedTags => _selectedTags;

        // Beautiful Color Palette
        private readonly Color[] _presetColors = new[]
        {
            new Color(0.21f, 0.75f, 0.69f), // Default Teal (#36BFB1)
            new Color(0.61f, 0.64f, 0.69f), // Gray (#9ca3af)
            new Color(0.97f, 0.44f, 0.44f), // Red (#f87171)
            new Color(0.98f, 0.57f, 0.24f), // Orange (#fb923c)
            new Color(0.98f, 0.75f, 0.14f), // Amber (#fbbf24)
            new Color(0.29f, 0.87f, 0.5f),  // Green (#4ade80)
            new Color(0.38f, 0.65f, 0.98f), // Blue (#60a5fa)
            new Color(0.51f, 0.55f, 0.97f), // Indigo (#818cf8)
            new Color(0.75f, 0.52f, 0.99f), // Purple (#c084fc)
            new Color(0.96f, 0.45f, 0.71f)  // Pink (#f472b6)
        };

        public TokenizedTagInput()
        {
            AddToClassList("yucp-tokenized-tag-input");

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.devtools/Editor/PackageExporter/UI/Components/TokenizedTagInput.uss");
            if (styleSheet != null) 
            {
                styleSheets.Add(styleSheet);
            }

            _chipsContainer = new VisualElement { name = "chips-container" };
            _chipsContainer.style.flexDirection = FlexDirection.Row;
            _chipsContainer.style.flexWrap = Wrap.Wrap;
            Add(_chipsContainer);

            _inputField = new TextField();
            _inputField.AddToClassList("yucp-tokenized-tag-input-input");
            _inputField.isDelayed = false; // Real-time
            _inputField.RegisterValueChangedCallback(OnInputChanged);
            _inputField.RegisterCallback<KeyDownEvent>(OnKeyDown);
            
            // Manual focus handling
            _inputField.RegisterCallback<FocusOutEvent>(e => {
                this.RemoveFromClassList("focused");
                schedule.Execute(() => HideOverlay()).ExecuteLater(200);
            });
            _inputField.RegisterCallback<FocusInEvent>(e => {
                this.AddToClassList("focused");
                ShowOverlay(); // Show suggestions even if empty
            });

            Add(_inputField);

            // Overlay
            _overlayContainer = new VisualElement();
            _overlayContainer.AddToClassList("yucp-tag-autocomplete-overlay");
            _overlayContainer.style.display = DisplayStyle.None;
            if (styleSheet != null) _overlayContainer.styleSheets.Add(styleSheet);
            Add(_overlayContainer);

            _autocompleteList = new ListView();
            _autocompleteList.makeItem = () => {
                var container = new VisualElement();
                container.AddToClassList("yucp-tag-autocomplete-item");
                container.style.flexDirection = FlexDirection.Row;
                container.style.alignItems = Align.Center;
                container.style.paddingLeft = 8;
                container.style.paddingRight = 8;
                container.style.justifyContent = Justify.SpaceBetween;
                
                var label = new Label();
                label.name = "label";
                label.style.marginRight = 8;
                label.style.flexShrink = 1;
                label.style.overflow = Overflow.Hidden;
                container.Add(label);
                
                var colorDot = new VisualElement();
                colorDot.name = "color-dot";
                colorDot.style.width = 12; // Larger for interaction
                colorDot.style.height = 12;
                colorDot.style.flexShrink = 0;
                
                // Rounded circle explicit properties
                colorDot.style.borderTopLeftRadius = 6;
                colorDot.style.borderTopRightRadius = 6;
                colorDot.style.borderBottomLeftRadius = 6;
                colorDot.style.borderBottomRightRadius = 6;
                
                // Handle click on dot
                colorDot.RegisterCallback<MouseDownEvent>(e => {
                    if (e.button == 0 && colorDot.userData is string tag)
                    {
                        ShowTagContext(tag, colorDot); // Show context menu
                        e.StopPropagation(); // Prevent row selection
                    }
                });
                
                // Delete Btn
                var deleteBtn = new Button();
                deleteBtn.name = "delete-btn";
                deleteBtn.text = "×";
                deleteBtn.style.backgroundColor = Color.clear;
                deleteBtn.style.borderTopWidth = 0;
                deleteBtn.style.borderBottomWidth = 0;
                deleteBtn.style.borderLeftWidth = 0;
                deleteBtn.style.borderRightWidth = 0;
                deleteBtn.style.fontSize = 16;
                deleteBtn.style.marginLeft = 6; // Space between dot and X
                deleteBtn.style.width = 20;
                deleteBtn.style.height = 20;
                deleteBtn.style.paddingLeft = 0;
                deleteBtn.style.paddingRight = 0;
                deleteBtn.style.paddingTop = 0;
                deleteBtn.style.paddingBottom = 0;
                deleteBtn.style.color = new Color(0.6f, 0.6f, 0.6f);
                deleteBtn.tooltip = "Delete Tag Globally";
                
                deleteBtn.RegisterCallback<MouseEnterEvent>(e => deleteBtn.style.color = new Color(0.9f, 0.4f, 0.4f));
                deleteBtn.RegisterCallback<MouseLeaveEvent>(e => deleteBtn.style.color = new Color(0.6f, 0.6f, 0.6f));
                
                var rightGroup = new VisualElement();
                rightGroup.style.flexDirection = FlexDirection.Row;
                rightGroup.style.alignItems = Align.Center;
                rightGroup.name = "right-group";
                rightGroup.Add(colorDot);
                rightGroup.Add(deleteBtn);
                
                container.Add(rightGroup);
                
                return container;
            };
            
            _autocompleteList.bindItem = (e, i) => {
                var container = e as VisualElement;
                if (i >= 0 && i < _filteredOptions.Count && container != null)
                {
                    var tag = _filteredOptions[i];
                    var label = container.Q<Label>("label");
                    var rightGroup = container.Q<VisualElement>("right-group");
                    var colorDot = container.Q<VisualElement>("color-dot"); // Might need to find within rightGroup if hierarchy changed? No, Q searches deep by default? Yes.
                    var deleteBtn = container.Q<Button>("delete-btn");

                    // Pass tag to dot/btn for click handler
                    colorDot.userData = tag;
                    deleteBtn.userData = tag;
                    
                    // Click handling
                    deleteBtn.clickable.clickedWithEventInfo -= OnDeleteBtnClicked;
                    deleteBtn.clickable.clickedWithEventInfo += OnDeleteBtnClicked;
                    
                    if (tag.StartsWith("CREATE:"))
                    {
                        label.text = $"Create tag: \"{tag.Substring(7)}\"";
                        label.AddToClassList("yucp-tag-autocomplete-item-new");
                        rightGroup.style.display = DisplayStyle.None;
                        
                        // No context menu for create
                         container.UnregisterCallback<ContextClickEvent>(OnDropdownContextClick);
                    }
                    else
                    {
                        label.text = tag;
                        label.RemoveFromClassList("yucp-tag-autocomplete-item-new");
                        
                        // Show color dot
                        rightGroup.style.display = DisplayStyle.Flex;
                        var color = TagColorProvider?.Invoke(tag) ?? _presetColors[0];
                        colorDot.style.backgroundColor = color;
                        
                        // Context Menu on Row
                        container.userData = tag; 
                        container.UnregisterCallback<ContextClickEvent>(OnDropdownContextClick);
                        container.RegisterCallback<ContextClickEvent>(OnDropdownContextClick);
                    }
                }
            };
            
            _autocompleteList.selectionType = SelectionType.Single;
            _autocompleteList.itemsSource = _filteredOptions;
            _autocompleteList.fixedItemHeight = 28;
            
            _autocompleteList.selectionChanged += (objs) => {
                 // Create a copy to avoid "Collection was modified" exception if selection triggers list changes
                 var selectedItems = objs.OfType<string>().ToList();
                 if (selectedItems.Count == 0) return;

                 foreach(var tag in selectedItems)
                 {
                     SelectTag(tag);
                 }
                 
                 // Clear selection to reset state - checked to prevent recursion if already cleared
                 if (_autocompleteList.selectedIndex != -1)
                 {
                     _autocompleteList.ClearSelection();
                 }
            };
            
            _overlayContainer.Add(_autocompleteList);
        }

        private void OnDropdownContextClick(ContextClickEvent evt)
        {
            if (evt.currentTarget is VisualElement container && container.userData is string tag)
            {
                ShowTagContext(tag, container);
                evt.StopPropagation();
            }
        }

        private void OnDeleteBtnClicked(EventBase evt)
        {
             if (evt.target is Button btn && btn.userData is string tag)
             {
                 if (EditorUtility.DisplayDialog("Delete Tag", $"Are you sure you want to delete the tag '{tag}' globally? This cannot be undone.", "Delete", "Cancel"))
                 {
                     if (_selectedTags.Contains(tag)) RemoveTag(tag);
                     OnTagDeleted?.Invoke(tag); // Global delete
                 }
                 evt.StopPropagation(); // Stop dropdown from selecting row
             }
        }
        
        public void AttachOverlayToRoot(VisualElement root)
        {
            if (root == null) return;
            _externalRoot = root;
            
            if (_overlayContainer.parent == this)
            {
                _overlayContainer.RemoveFromHierarchy();
            }
            
            if (_overlayContainer.parent != _externalRoot)
            {
                _externalRoot.Add(_overlayContainer);
            }
            
            _isAttachedToExternalRoot = true;
            _overlayContainer.style.position = Position.Absolute;
        }

        public void SetAvailableTags(IEnumerable<string> tags)
        {
            _availableTags = tags != null ? new List<string>(tags) : new List<string>();
        }
        
        public void SetSelectedTags(IEnumerable<string> tags)
        {
             _selectedTags = tags != null ? new List<string>(tags) : new List<string>();
             RefreshChips();
        }

        public void RefreshChips()
        {
            _chipsContainer.Clear();
            foreach(var tag in _selectedTags)
            {
                var chip = new VisualElement();
                chip.AddToClassList("yucp-tag-chip");
                
                // Apply dynamic color
                var baseColor = TagColorProvider?.Invoke(tag) ?? _presetColors[0];
                chip.style.backgroundColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.15f));
                chip.style.borderLeftColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));
                chip.style.borderRightColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));
                chip.style.borderTopColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));
                chip.style.borderBottomColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));
                
                var label = new Label(tag);
                label.AddToClassList("yucp-tag-chip-label");
                label.style.color = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.95f));
                chip.Add(label);

                var closeBtn = new Button(() => RemoveTag(tag));
                closeBtn.text = "×";
                closeBtn.AddToClassList("yucp-tag-chip-remove");
                closeBtn.tooltip = "Remove tag from profile";
                closeBtn.style.color = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.8f));
                // Ensure button is always visible
                closeBtn.style.display = DisplayStyle.Flex;
                closeBtn.style.visibility = Visibility.Visible;
                chip.Add(closeBtn);

                // Context menu handling
                chip.RegisterCallback<ContextClickEvent>(evt => 
                {
                    ShowTagContext(tag, chip);
                    evt.StopPropagation();
                });
                
                _chipsContainer.Add(chip);
            }
        }
        
        private void ShowTagContext(string tag, VisualElement modalTarget)
        {
             if (!_isAttachedToExternalRoot || _externalRoot == null) return;

             var contextMenu = new VisualElement();
             contextMenu.AddToClassList("yucp-popover"); // Use existing popover class
             contextMenu.style.position = Position.Absolute;
             contextMenu.style.width = 180;
             contextMenu.style.paddingLeft = 12;
             contextMenu.style.paddingRight = 12;
             contextMenu.style.paddingTop = 12;
             contextMenu.style.paddingBottom = 12;
             
             // 1. Color Grid
             var colorLabel = new Label("Tag Color");
             colorLabel.AddToClassList("yucp-section-header");
             colorLabel.style.fontSize = 10;
             colorLabel.style.marginBottom = 8;
             contextMenu.Add(colorLabel);

             var colorGrid = new VisualElement();
             colorGrid.style.flexDirection = FlexDirection.Row;
             colorGrid.style.flexWrap = Wrap.Wrap;
             colorGrid.style.marginBottom = 12;

             // Helper to manage user palette
             Func<List<Color>> GetUserPalette = () => {
                 string raw = EditorPrefs.GetString("YUCP_UserPalette", "");
                 if (string.IsNullOrEmpty(raw)) return new List<Color>();
                 return raw.Split('|').Select(s => ColorUtility.TryParseHtmlString("#" + s, out var c) ? c : Color.white).ToList();
             };
             
             Action<Color> AddToUserPalette = (c) => {
                 var list = GetUserPalette();
                 if(list.Any(existing => existing == c)) return; // No dupe
                 list.Insert(0, c); // Add to front
                 if(list.Count > 10) list.RemoveAt(list.Count - 1); // Limit to 10
                 EditorPrefs.SetString("YUCP_UserPalette", string.Join("|", list.Select(col => ColorUtility.ToHtmlStringRGB(col))));
             };

             // 1. Color Grid (Presets + User)
             Action rebuildGrid = null;
             rebuildGrid = () => {
                 colorGrid.Clear();
                 
                 // Standard Presets
                 foreach (var color in _presetColors)
                 {
                     var swatch = new VisualElement();
                     swatch.style.width = 18; swatch.style.height = 18;
                     swatch.style.marginRight = 4; swatch.style.marginBottom = 4;
                     swatch.style.borderTopLeftRadius = 9; swatch.style.borderTopRightRadius = 9;
                     swatch.style.borderBottomLeftRadius = 9; swatch.style.borderBottomRightRadius = 9;
                     swatch.style.backgroundColor = color;
                     
                     // Border
                     swatch.style.borderTopWidth=1; swatch.style.borderBottomWidth=1;
                     swatch.style.borderLeftWidth=1; swatch.style.borderRightWidth=1;
                     swatch.style.borderTopColor=new Color(1,1,1,0.1f); swatch.style.borderBottomColor=new Color(1,1,1,0.1f);
                     swatch.style.borderLeftColor=new Color(1,1,1,0.1f); swatch.style.borderRightColor=new Color(1,1,1,0.1f);
                     
                     swatch.RegisterCallback<MouseDownEvent>(e => {
                         if(e.button == 0) {
                             OnTagColorChanged?.Invoke(tag, color);
                             RefreshChips();
                             if (_autocompleteList != null) _autocompleteList.RefreshItems();
                             contextMenu.RemoveFromHierarchy();
                             e.StopPropagation();
                         }
                     });
                     colorGrid.Add(swatch);
                 }
                 
                 // Separator if custom colors exist
                 var userColors = GetUserPalette();
                 if (userColors.Count > 0)
                 {
                     var sep = new VisualElement();
                     sep.style.width = 4; sep.style.height = 18;
                     colorGrid.Add(sep);
                     
                     foreach (var color in userColors)
                     {
                         var swatch = new VisualElement();
                         swatch.style.width = 18; swatch.style.height = 18;
                         swatch.style.marginRight = 4; swatch.style.marginBottom = 4;
                         swatch.style.borderTopLeftRadius = 9; swatch.style.borderTopRightRadius = 9;
                         swatch.style.borderBottomLeftRadius = 9; swatch.style.borderBottomRightRadius = 9;
                         swatch.style.backgroundColor = color;
                         
                         swatch.style.borderTopWidth=1; swatch.style.borderBottomWidth=1;
                         swatch.style.borderLeftWidth=1; swatch.style.borderRightWidth=1;
                         swatch.style.borderTopColor=new Color(1,1,1,0.1f); swatch.style.borderBottomColor=new Color(1,1,1,0.1f);
                         swatch.style.borderLeftColor=new Color(1,1,1,0.1f); swatch.style.borderRightColor=new Color(1,1,1,0.1f);
                         
                         swatch.RegisterCallback<MouseDownEvent>(e => {
                             if(e.button == 0) {
                                 OnTagColorChanged?.Invoke(tag, color);
                                 RefreshChips();
                                 if (_autocompleteList != null) _autocompleteList.RefreshItems();
                                 contextMenu.RemoveFromHierarchy();
                                 e.StopPropagation();
                             }
                         });
                         colorGrid.Add(swatch);
                     }
                 }
                 
                 // Custom (+) Button
                 var customContainer = new VisualElement();
                 customContainer.style.width = 18; customContainer.style.height = 18;
                 customContainer.style.marginRight = 4; customContainer.style.marginBottom = 4;
                 customContainer.style.justifyContent = Justify.Center;
                 customContainer.style.alignItems = Align.Center;
                 
                 customContainer.style.borderTopLeftRadius = 9; customContainer.style.borderTopRightRadius = 9;
                 customContainer.style.borderBottomLeftRadius = 9; customContainer.style.borderBottomRightRadius = 9;
                 
                 customContainer.style.backgroundColor = new Color(0,0,0,0.1f);
                 customContainer.style.borderTopWidth = 1; customContainer.style.borderBottomWidth = 1;
                 customContainer.style.borderLeftWidth = 1; customContainer.style.borderRightWidth = 1;
                 customContainer.style.borderTopColor=new Color(1,1,1,0.2f); customContainer.style.borderBottomColor=new Color(1,1,1,0.2f);
                 customContainer.style.borderLeftColor=new Color(1,1,1,0.2f); customContainer.style.borderRightColor=new Color(1,1,1,0.2f);
                 
                 var plusLabel = new Label("+");
                 plusLabel.style.color = new Color(1,1,1,0.5f);
                 plusLabel.style.fontSize = 12;
                 plusLabel.style.paddingTop = 0; plusLabel.style.paddingBottom = 0;
                 plusLabel.style.paddingLeft = 0; plusLabel.style.paddingRight = 0;
                 customContainer.Add(plusLabel);
                 
                 customContainer.RegisterCallback<MouseDownEvent>(e => 
                 {
                     if (e.button == 0)
                     {
                         // Switch to Embedded Color Picker View
                         contextMenu.Clear();
                         contextMenu.style.width = 230; // Slightly wider
                         
                         var baseColor = TagColorProvider?.Invoke(tag) ?? Color.white;
                         float currentH, currentS, currentV;
                         Color.RGBToHSV(baseColor, out currentH, out currentS, out currentV);
                         
                         // Better texture gen
                         Texture2D GenerateCusomTexture(int w, int h, Func<int, int, Color> pixelFunc) {
                             var tex = new Texture2D(w, h);
                             var pix = new Color[w * h];
                             for(int y=0; y<h; y++) {
                                 for(int x=0; x<w; x++) {
                                     pix[y*w + x] = pixelFunc(x, y);
                                 }
                             }
                             tex.SetPixels(pix);
                             tex.filterMode = FilterMode.Bilinear;
                             tex.wrapMode = TextureWrapMode.Clamp;
                             tex.Apply();
                             return tex;
                          }
    
                          var root = new VisualElement();
                          root.style.paddingLeft = 8; root.style.paddingRight = 8;
                          root.style.paddingTop = 8; root.style.paddingBottom = 8;
    
                          // 1. Sat/Val Box
                          var svBox = new VisualElement();
                          svBox.style.width = 210; svBox.style.height = 150;
                          svBox.style.marginBottom = 12;
                          svBox.style.borderTopWidth = 1; svBox.style.borderBottomWidth = 1;
                          svBox.style.borderLeftWidth = 1; svBox.style.borderRightWidth = 1;
                          svBox.style.borderTopColor = new Color(0,0,0,0.5f);
                          svBox.style.borderBottomColor = new Color(0,0,0,0.5f);
                          svBox.style.borderLeftColor = new Color(0,0,0,0.5f);
                          svBox.style.borderRightColor = new Color(0,0,0,0.5f);
                          
                          var hueBg = new VisualElement();
                          hueBg.style.flexGrow = 1;
                          svBox.Add(hueBg);
                          
                          var satGrad = new VisualElement();
                          satGrad.style.position = Position.Absolute;
                          satGrad.style.top=0; satGrad.style.bottom=0; satGrad.style.left=0; satGrad.style.right=0;
                          satGrad.style.backgroundImage = GenerateCusomTexture(2, 1, (x,y) => x==0 ? Color.white : new Color(1,1,1,0));
                          svBox.Add(satGrad);
                          
                          var valGrad = new VisualElement();
                          valGrad.style.position = Position.Absolute;
                          valGrad.style.top=0; valGrad.style.bottom=0; valGrad.style.left=0; valGrad.style.right=0;
                          valGrad.style.backgroundImage = GenerateCusomTexture(1, 2, (x,y) => y==1 ? new Color(0,0,0,0) : Color.black);
                          svBox.Add(valGrad);
                          
                          var svHandle = new VisualElement();
                          svHandle.style.width = 12; svHandle.style.height = 12;
                          svHandle.style.position = Position.Absolute;
                          svHandle.style.borderTopLeftRadius = 6; svHandle.style.borderTopRightRadius = 6;
                          svHandle.style.borderBottomLeftRadius = 6; svHandle.style.borderBottomRightRadius = 6;
                          svHandle.style.borderTopWidth=2; svHandle.style.borderBottomWidth=2; // Thicker border
                          svHandle.style.borderLeftWidth=2; svHandle.style.borderRightWidth=2;
                          svHandle.style.borderTopColor=Color.white; svHandle.style.borderBottomColor=Color.white;
                          svHandle.style.borderLeftColor=Color.white; svHandle.style.borderRightColor=Color.white;
                          svHandle.style.backgroundColor = Color.clear;
                          // Shadow for better visibility
                          var shadow = new VisualElement();
                          shadow.style.position = Position.Absolute;
                          shadow.style.top = -1; shadow.style.bottom = -1; shadow.style.left = -1; shadow.style.right = -1;
                          shadow.style.borderTopWidth=1; shadow.style.borderBottomWidth=1;
                          shadow.style.borderLeftWidth=1; shadow.style.borderRightWidth=1;
                          shadow.style.borderTopColor=new Color(0,0,0,0.5f); shadow.style.borderBottomColor=new Color(0,0,0,0.5f);
                          shadow.style.borderLeftColor=new Color(0,0,0,0.5f); shadow.style.borderRightColor=new Color(0,0,0,0.5f);
                          shadow.style.borderTopLeftRadius = 7; shadow.style.borderTopRightRadius = 7;
                          shadow.style.borderBottomLeftRadius = 7; shadow.style.borderBottomRightRadius = 7;
                          svHandle.Add(shadow);
                          svBox.Add(svHandle);
    
                          // 2. Hue Slider
                          var hueSlider = new VisualElement();
                          hueSlider.style.width = 210; hueSlider.style.height = 16;
                          hueSlider.style.marginBottom = 12;
                          hueSlider.style.borderTopWidth=1; hueSlider.style.borderBottomWidth=1;
                          hueSlider.style.borderLeftWidth=1; hueSlider.style.borderRightWidth=1;
                          hueSlider.style.borderTopColor=new Color(0,0,0,0.3f);
                          hueSlider.style.borderBottomColor=new Color(0,0,0,0.3f);
                          hueSlider.style.borderLeftColor=new Color(0,0,0,0.3f);
                          hueSlider.style.borderRightColor=new Color(0,0,0,0.3f);
                          hueSlider.style.borderTopLeftRadius = 3; hueSlider.style.borderTopRightRadius = 3;
                          hueSlider.style.borderBottomLeftRadius = 3; hueSlider.style.borderBottomRightRadius = 3;
                          
                          hueSlider.style.backgroundImage = GenerateCusomTexture(256, 1, (x,y) => Color.HSVToRGB((float)x/255f, 1, 1));
                          
                          var hueHandle = new VisualElement();
                          hueHandle.style.width = 4; hueHandle.style.height = 16;
                          hueHandle.style.position = Position.Absolute;
                          hueHandle.style.backgroundColor = Color.white;
                          hueHandle.style.borderTopWidth=1; hueHandle.style.borderBottomWidth=1;
                          hueHandle.style.borderLeftWidth=1; hueHandle.style.borderRightWidth=1;
                          hueHandle.style.borderTopColor=new Color(0,0,0,0.5f); hueHandle.style.borderBottomColor=new Color(0,0,0,0.5f);
                          hueHandle.style.borderLeftColor=new Color(0,0,0,0.5f); hueHandle.style.borderRightColor=new Color(0,0,0,0.5f);
                          hueSlider.Add(hueHandle);
    
                          // 3. Save Button
                          var saveBtn = new Button(() => {
                              // Save to palette logic
                              Color c = Color.HSVToRGB(currentH, currentS, currentV);
                              AddToUserPalette(c);
                              // Rebuild main menu
                              contextMenu.Clear();
                              // Add grid back
                              var label = new Label("Tag Color");
                              label.AddToClassList("yucp-section-header");
                              label.style.fontSize = 10;
                              label.style.marginBottom = 8;
                              contextMenu.style.width = 180; // Reset width
                              contextMenu.style.paddingLeft = 12; contextMenu.style.paddingRight = 12;
                              contextMenu.Add(label);
                              
                              colorGrid.Clear(); // Clear existing to prevent duplicates when re-adding
                              rebuildGrid(); // Re-run build logic
                              contextMenu.Add(colorGrid);
                              // Separator
                              var div = new VisualElement();
                              div.style.height = 1; div.style.backgroundColor = new Color(1,1,1,0.1f); div.style.marginBottom = 8;
                              contextMenu.Add(div);
                              
                              // Delete Button
                              var deleteBtn = new Button();
                              deleteBtn.text = "Delete Tag Globally";
                              deleteBtn.AddToClassList("yucp-button-danger");
                              deleteBtn.style.fontSize = 10; deleteBtn.style.height = 24; deleteBtn.style.width = Length.Percent(100);
                              deleteBtn.clicked += () => { /* Logic duplicated from closure above? I can't access it easily without refactor. */
                                   // Re-implement delete logic
                                    if (EditorUtility.DisplayDialog("Delete Tag", $"Are you sure you want to delete the tag '{tag}' globally?", "Delete", "Cancel"))
                                     {
                                         OnTagDeleted?.Invoke(tag);
                                         contextMenu.RemoveFromHierarchy(); 
                                     } 
                              };
                              contextMenu.Add(deleteBtn);
                              
                          });
                          saveBtn.text = "Save Color";
                          saveBtn.style.height = 24;
                          saveBtn.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
                          saveBtn.style.color = Color.white;
                          saveBtn.style.borderTopWidth=0; saveBtn.style.borderBottomWidth=0;
                          saveBtn.style.borderLeftWidth=0; saveBtn.style.borderRightWidth=0;
                          saveBtn.style.fontSize = 11;
                          root.Add(saveBtn);
    
                          // Update Logic
                          Action updateColor = () => {
                              Color c = Color.HSVToRGB(currentH, currentS, currentV);
                              OnTagColorChanged?.Invoke(tag, c);
                              
                              hueBg.style.backgroundColor = Color.HSVToRGB(currentH, 1, 1);
                              
                              float handleX = currentS * 210f;
                              float handleY = (1f - currentV) * 150f;
                              svHandle.style.left = Mathf.Clamp(handleX - 6, -6, 204);
                              svHandle.style.top = Mathf.Clamp(handleY - 6, -6, 144);
                              svHandle.style.borderTopColor = currentV > 0.5f ? Color.black : Color.white;
                              
                              hueHandle.style.left = Mathf.Clamp((currentH * 210f) - 2, -2, 208);
                              
                              if (_autocompleteList != null) _autocompleteList.RefreshItems();
                              RefreshChips();
                          };
    
                          // Input Handling
                          svBox.RegisterCallback<MouseDownEvent>(evt => {
                              if(evt.button == 0) {
                                  svBox.CaptureMouse();
                                  currentS = Mathf.Clamp01(evt.localMousePosition.x / 210f);
                                  currentV = 1f - Mathf.Clamp01(evt.localMousePosition.y / 150f);
                                  updateColor();
                              }
                          });
                          svBox.RegisterCallback<MouseMoveEvent>(evt => {
                              if(svBox.HasMouseCapture()) {
                                  currentS = Mathf.Clamp01(evt.localMousePosition.x / 210f);
                                  currentV = 1f - Mathf.Clamp01(evt.localMousePosition.y / 150f);
                                  updateColor();
                              }
                          });
                          svBox.RegisterCallback<MouseUpEvent>(evt => { if(svBox.HasMouseCapture()) svBox.ReleaseMouse(); });
    
                          hueSlider.RegisterCallback<MouseDownEvent>(evt => {
                              if(evt.button == 0) {
                                  hueSlider.CaptureMouse();
                                  currentH = Mathf.Clamp01(evt.localMousePosition.x / 210f);
                                  updateColor();
                              }
                          });
                          hueSlider.RegisterCallback<MouseMoveEvent>(evt => {
                              if(hueSlider.HasMouseCapture()) {
                                  currentH = Mathf.Clamp01(evt.localMousePosition.x / 210f);
                                  updateColor();
                              }
                          });
                          hueSlider.RegisterCallback<MouseUpEvent>(evt => { if(hueSlider.HasMouseCapture()) hueSlider.ReleaseMouse(); });
    
                          root.Add(svBox);
                          root.Add(hueSlider);
                          root.Add(saveBtn);
                          contextMenu.Add(root);
                          
                          updateColor();
                          
                          e.StopPropagation();
                     }
                 });
                 
                 colorGrid.Add(customContainer);
             };
             
             // Initial Build
             rebuildGrid();

             contextMenu.Add(colorGrid);

             // Dividend
             var div = new VisualElement();
             div.style.height = 1;
             div.style.backgroundColor = new Color(1,1,1,0.1f);
             div.style.marginBottom = 8;
             contextMenu.Add(div);

             // 2. Delete Action
             var deleteBtn = new Button(() => 
             {
                 if (EditorUtility.DisplayDialog("Delete Tag", $"Are you sure you want to delete the tag '{tag}' globally? This cannot be undone.", "Delete", "Cancel"))
                 {
                     if (_selectedTags.Contains(tag)) RemoveTag(tag);
                     OnTagDeleted?.Invoke(tag); // Global delete
                     // Dropdown refresh is handled by parent re-setting AvailableTags via update loop that OnTagDeleted usually triggers
                 }
                 contextMenu.RemoveFromHierarchy();
             });
             deleteBtn.text = "Delete Tag Globally";
             deleteBtn.AddToClassList("yucp-button-danger"); // Reusing existing danger style
             deleteBtn.style.fontSize = 10;
             deleteBtn.style.height = 24;
             deleteBtn.style.width = Length.Percent(100);
             contextMenu.Add(deleteBtn);
             
             // Add dismiss overlay
             var overlay = new VisualElement();
             overlay.style.position = Position.Absolute;
             overlay.style.top = 0;
             overlay.style.bottom = 0;
             overlay.style.left = 0;
             overlay.style.right = 0;
             overlay.RegisterCallback<MouseDownEvent>(e => 
             {
                 contextMenu.RemoveFromHierarchy();
                 overlay.RemoveFromHierarchy();
                 e.StopPropagation();
             });

             _externalRoot.Add(overlay);
             _externalRoot.Add(contextMenu);
             
             // Position
             var targetBound = modalTarget.worldBound;
             // Ensure root is valid, sometimes worldBound is zero if not layout yet
             if (_externalRoot.worldBound.width > 0)
             {
                 var rootBound = _externalRoot.worldBound;
                 contextMenu.style.left = targetBound.x - rootBound.x;
                 contextMenu.style.top = targetBound.yMax - rootBound.y + 4;
             }
             else
             {
                 // Fallback
                 contextMenu.style.left = targetBound.x;
                 contextMenu.style.top = targetBound.yMax + 4;
             }
        }

        private void AddTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            
            // Handle "CREATE:" prefix
            if (tag.StartsWith("CREATE:"))
            {
                tag = tag.Substring(7);
            }

            if(!_selectedTags.Contains(tag))
            {
                _selectedTags.Add(tag);
                RefreshChips();
                OnTagsChanged?.Invoke(_selectedTags);
            }
        }
        
        private void RemoveTag(string tag)
        {
             if(_selectedTags.Contains(tag))
            {
                _selectedTags.Remove(tag);
                RefreshChips();
                OnTagsChanged?.Invoke(_selectedTags);
            }
        }
        
        private void SelectTag(string tag)
        {
            AddTag(tag);
            _inputField.value = "";
            ShowOverlay(); // Keep showing suggestions (or could hide)
            _inputField.Focus(); // Keep focus
        }

        private void OnInputChanged(ChangeEvent<string> evt)
        {
            ShowOverlay();
        }
        
        private void FilterOptions()
        {
            string searchText = _inputField.value;
            
            var query = _availableTags
                .Where(t => !_selectedTags.Contains(t));
                
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                query = query.Where(t => t.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            
            _filteredOptions = query.OrderBy(t => t.Length).ThenBy(t => t).ToList();
            
            // If searching and no exact match, add "Create" option
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                bool exactMatch = _filteredOptions.Any(t => string.Equals(t, searchText, StringComparison.OrdinalIgnoreCase));
                if (!exactMatch && !_selectedTags.Contains(searchText)) // Don't suggest creating if already selected
                {
                    _filteredOptions.Insert(0, $"CREATE:{searchText}");
                }
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Backspace && string.IsNullOrEmpty(_inputField.value) && _selectedTags.Count > 0)
            {
                var last = _selectedTags.Last();
                RemoveTag(last);
                evt.StopPropagation(); 
            }
            else if (evt.keyCode == KeyCode.DownArrow)
            {
                if (_overlayContainer.style.display == DisplayStyle.Flex && _filteredOptions.Count > 0)
                {
                    int newIndex = (_autocompleteList.selectedIndex + 1) % _filteredOptions.Count;
                    _autocompleteList.selectedIndex = newIndex;
                    _autocompleteList.ScrollToItem(newIndex);
                    evt.StopPropagation();
                }
            }
            else if (evt.keyCode == KeyCode.UpArrow)
            {
                if (_overlayContainer.style.display == DisplayStyle.Flex && _filteredOptions.Count > 0)
                {
                    int newIndex = _autocompleteList.selectedIndex - 1;
                    if (newIndex < 0) newIndex = _filteredOptions.Count - 1;
                    _autocompleteList.selectedIndex = newIndex;
                    _autocompleteList.ScrollToItem(newIndex);
                    evt.StopPropagation();
                }
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                HideOverlay();
                _inputField.Blur();
                evt.StopPropagation();
            }
             else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                 if (_overlayContainer.style.display == DisplayStyle.Flex && _autocompleteList.selectedIndex >= 0 && _autocompleteList.selectedIndex < _filteredOptions.Count)
                 {
                     SelectTag(_filteredOptions[_autocompleteList.selectedIndex]);
                     evt.StopPropagation();
                 }
                 else if (_overlayContainer.style.display == DisplayStyle.Flex && _filteredOptions.Count > 0)
                 {
                     SelectTag(_filteredOptions[0]);
                     evt.StopPropagation();
                 }
                 else if (!string.IsNullOrWhiteSpace(_inputField.value))
                 {
                     // Optional: auto-create
                 }
            }
        }

        private void ShowOverlay()
        {
            FilterOptions();
            
            if (_filteredOptions.Count == 0 && string.IsNullOrEmpty(_inputField.value))
            {
                 HideOverlay();
                 return;
            }
            
            if (_filteredOptions.Count > 0)
            {
                _autocompleteList.itemsSource = _filteredOptions;
                _autocompleteList.RefreshItems();
                
                float height = Mathf.Min(200, _filteredOptions.Count * 28 + 10);
                _overlayContainer.style.height = height;
                
                _overlayContainer.style.display = DisplayStyle.Flex;
                
                if (_isAttachedToExternalRoot && _externalRoot != null)
                {
                    var myBounds = this.worldBound;
                    var rootBounds = _externalRoot.worldBound;
                    
                    float left = myBounds.x - rootBounds.x;
                    float top = myBounds.yMax - rootBounds.y + 4;
                    float width = myBounds.width;
                    
                    _overlayContainer.style.left = left;
                    _overlayContainer.style.top = top;
                    _overlayContainer.style.width = width;
                }
                
                _overlayContainer.BringToFront();
            }
            else
            {
                HideOverlay();
            }
        }

        private void HideOverlay()
        {
            _overlayContainer.style.display = DisplayStyle.None;
        }

    }
}
