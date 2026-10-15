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
        private static readonly Color[] PresetColors = new[]
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
                
                // Handle click on dot. ListView selects rows on PointerDown (which would add the tag
                // and rebind this row), so the dot has to claim PointerDown, not MouseDown.
                colorDot.RegisterCallback<PointerDownEvent>(e => {
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
                
                deleteBtn.RegisterCallback<PointerDownEvent>(e => e.StopPropagation()); // don't select the row
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
                        var color = TagColorProvider?.Invoke(tag) ?? PresetColors[0];
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

            // The overlay and tag menu live on an external root, so they must follow this element's
            // lifetime; otherwise a rebuilt details panel leaves them stuck open.
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                HideOverlay();
                if (_isAttachedToExternalRoot) _overlayContainer.RemoveFromHierarchy();
                CloseTagMenu();
            });
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (_isAttachedToExternalRoot && _overlayContainer.parent == null) _externalRoot.Add(_overlayContainer);
            });
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
                var baseColor = TagColorProvider?.Invoke(tag) ?? PresetColors[0];
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

            ShowTagMenu(_externalRoot, modalTarget, tag,
                t => TagColorProvider?.Invoke(t) ?? PresetColors[0],
                (t, color) =>
                {
                    OnTagColorChanged?.Invoke(t, color);
                    RefreshChips();
                    _autocompleteList.RefreshItems();
                },
                t =>
                {
                    if (_selectedTags.Contains(t)) RemoveTag(t);
                    OnTagDeleted?.Invoke(t);
                });
        }

        private static Action s_closeTagMenu;

        /// <summary>Closes the tag color popover if one is open.</summary>
        public static void CloseTagMenu()
        {
            var close = s_closeTagMenu;
            s_closeTagMenu = null;
            close?.Invoke();
        }

        /// <summary>
        /// Tag color / delete popover, shared by the profile tag input and the sidebar tag chips.
        /// Only one is open at a time; every exit path goes through <see cref="CloseTagMenu"/> so the
        /// click-catching backdrop never outlives the menu.
        /// </summary>
        public static void ShowTagMenu(VisualElement root, VisualElement anchor, string tag,
            Func<string, Color> getColor, Action<string, Color> onColorChanged, Action<string> onDelete)
        {
            if (root == null) return;
            CloseTagMenu();

            var backdrop = new VisualElement();
            backdrop.style.position = Position.Absolute;
            backdrop.style.top = 0;
            backdrop.style.bottom = 0;
            backdrop.style.left = 0;
            backdrop.style.right = 0;
            backdrop.RegisterCallback<MouseDownEvent>(e =>
            {
                CloseTagMenu();
                e.StopPropagation();
            });

            var menu = new VisualElement();
            menu.AddToClassList("yucp-popover");
            menu.style.position = Position.Absolute;
            menu.style.width = 180;
            menu.style.paddingLeft = 12;
            menu.style.paddingRight = 12;
            menu.style.paddingTop = 12;
            menu.style.paddingBottom = 12;

            s_closeTagMenu = () =>
            {
                backdrop.RemoveFromHierarchy();
                menu.RemoveFromHierarchy();
            };

            var colorLabel = new Label("Tag Color");
            colorLabel.AddToClassList("yucp-section-header");
            colorLabel.style.fontSize = 10;
            colorLabel.style.marginBottom = 8;
            menu.Add(colorLabel);

            var colorGrid = new VisualElement();
            colorGrid.style.flexDirection = FlexDirection.Row;
            colorGrid.style.flexWrap = Wrap.Wrap;
            colorGrid.style.marginBottom = 12;

            VisualElement Swatch(Color color)
            {
                var swatch = CreateCircle(new Color(1, 1, 1, 0.1f));
                swatch.style.backgroundColor = color;
                swatch.RegisterCallback<MouseDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    onColorChanged?.Invoke(tag, color);
                    CloseTagMenu();
                    e.StopPropagation();
                });
                return swatch;
            }

            foreach (var color in PresetColors)
                colorGrid.Add(Swatch(color));

            var userColors = LoadUserPalette();
            if (userColors.Count > 0)
            {
                var sep = new VisualElement();
                sep.style.width = 4;
                sep.style.height = 18;
                colorGrid.Add(sep);
                foreach (var color in userColors)
                    colorGrid.Add(Swatch(color));
            }

            // Custom (+): swap the menu body for an HSV picker that recolors live
            var customButton = CreateCircle(new Color(1, 1, 1, 0.2f));
            customButton.style.backgroundColor = new Color(0, 0, 0, 0.1f);
            customButton.style.justifyContent = Justify.Center;
            customButton.style.alignItems = Align.Center;
            var plusLabel = new Label("+");
            plusLabel.style.color = new Color(1, 1, 1, 0.5f);
            plusLabel.style.fontSize = 12;
            plusLabel.style.paddingTop = 0;
            plusLabel.style.paddingBottom = 0;
            plusLabel.style.paddingLeft = 0;
            plusLabel.style.paddingRight = 0;
            customButton.Add(plusLabel);
            customButton.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != 0) return;
                menu.Clear();
                menu.style.width = 230;
                menu.Add(CreateColorPicker(getColor?.Invoke(tag) ?? PresetColors[0],
                    color => onColorChanged?.Invoke(tag, color),
                    color =>
                    {
                        SaveToUserPalette(color);
                        CloseTagMenu();
                    }));
                e.StopPropagation();
            });
            colorGrid.Add(customButton);
            menu.Add(colorGrid);

            var div = new VisualElement();
            div.style.height = 1;
            div.style.backgroundColor = new Color(1, 1, 1, 0.1f);
            div.style.marginBottom = 8;
            menu.Add(div);

            var deleteBtn = new Button(() =>
            {
                CloseTagMenu();
                if (EditorUtility.DisplayDialog("Delete Tag", $"Are you sure you want to delete the tag '{tag}' globally? This cannot be undone.", "Delete", "Cancel"))
                {
                    onDelete?.Invoke(tag);
                }
            });
            deleteBtn.text = "Delete Tag Globally";
            deleteBtn.AddToClassList("yucp-button-danger");
            deleteBtn.style.fontSize = 10;
            deleteBtn.style.height = 24;
            deleteBtn.style.width = Length.Percent(100);
            menu.Add(deleteBtn);

            root.Add(backdrop);
            root.Add(menu);

            var targetBound = anchor != null ? anchor.worldBound : Rect.zero;
            var rootBound = root.worldBound.width > 0 ? root.worldBound : Rect.zero;
            menu.style.left = targetBound.x - rootBound.x;
            menu.style.top = targetBound.yMax - rootBound.y + 4;
        }

        private static VisualElement CreateColorPicker(Color initial, Action<Color> onChange, Action<Color> onSave)
        {
            const float boxWidth = 210f;
            const float boxHeight = 150f;
            Color.RGBToHSV(initial, out float hue, out float sat, out float val);

            var root = new VisualElement();
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;

            // Saturation (x) / value (y) box over the current hue
            var svBox = new VisualElement();
            svBox.style.width = boxWidth;
            svBox.style.height = boxHeight;
            svBox.style.marginBottom = 12;
            SetBorder(svBox, 1, new Color(0, 0, 0, 0.5f));

            var hueBg = new VisualElement();
            hueBg.style.flexGrow = 1;
            svBox.Add(hueBg);
            svBox.Add(CreateFill(CreateTexture(2, 1, (x, y) => x == 0 ? Color.white : new Color(1, 1, 1, 0))));
            svBox.Add(CreateFill(CreateTexture(1, 2, (x, y) => y == 1 ? new Color(0, 0, 0, 0) : Color.black)));

            var svHandle = new VisualElement();
            svHandle.style.position = Position.Absolute;
            svHandle.style.width = 12;
            svHandle.style.height = 12;
            SetRadius(svHandle, 6);
            svBox.Add(svHandle);

            var hueSlider = new VisualElement();
            hueSlider.style.width = boxWidth;
            hueSlider.style.height = 16;
            hueSlider.style.marginBottom = 12;
            SetBorder(hueSlider, 1, new Color(0, 0, 0, 0.3f));
            SetRadius(hueSlider, 3);
            hueSlider.style.backgroundImage = CreateTexture(256, 1, (x, y) => Color.HSVToRGB(x / 255f, 1, 1));

            var hueHandle = new VisualElement();
            hueHandle.style.position = Position.Absolute;
            hueHandle.style.width = 4;
            hueHandle.style.height = 16;
            hueHandle.style.backgroundColor = Color.white;
            SetBorder(hueHandle, 1, new Color(0, 0, 0, 0.5f));
            hueSlider.Add(hueHandle);

            void Refresh(bool notify)
            {
                hueBg.style.backgroundColor = Color.HSVToRGB(hue, 1, 1);
                svHandle.style.left = Mathf.Clamp(sat * boxWidth - 6, -6, boxWidth - 6);
                svHandle.style.top = Mathf.Clamp((1f - val) * boxHeight - 6, -6, boxHeight - 6);
                SetBorder(svHandle, 2, val > 0.5f ? Color.black : Color.white);
                hueHandle.style.left = Mathf.Clamp(hue * boxWidth - 2, -2, boxWidth - 2);
                if (notify) onChange(Color.HSVToRGB(hue, sat, val));
            }

            RegisterDragArea(svBox, p =>
            {
                sat = Mathf.Clamp01(p.x / boxWidth);
                val = 1f - Mathf.Clamp01(p.y / boxHeight);
                Refresh(true);
            });
            RegisterDragArea(hueSlider, p =>
            {
                hue = Mathf.Clamp01(p.x / boxWidth);
                Refresh(true);
            });

            var saveBtn = new Button(() => onSave(Color.HSVToRGB(hue, sat, val)));
            saveBtn.text = "Save Color";
            saveBtn.style.height = 24;
            saveBtn.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            saveBtn.style.color = Color.white;
            saveBtn.style.borderTopWidth = 0;
            saveBtn.style.borderBottomWidth = 0;
            saveBtn.style.borderLeftWidth = 0;
            saveBtn.style.borderRightWidth = 0;
            saveBtn.style.fontSize = 11;

            root.Add(svBox);
            root.Add(hueSlider);
            root.Add(saveBtn);
            Refresh(false);
            return root;
        }

        private static void RegisterDragArea(VisualElement area, Action<Vector2> onPick)
        {
            area.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != 0) return;
                area.CaptureMouse();
                onPick(e.localMousePosition);
                e.StopPropagation();
            });
            area.RegisterCallback<MouseMoveEvent>(e =>
            {
                if (area.HasMouseCapture()) onPick(e.localMousePosition);
            });
            area.RegisterCallback<MouseUpEvent>(e =>
            {
                if (area.HasMouseCapture()) area.ReleaseMouse();
            });
        }

        private static VisualElement CreateCircle(Color borderColor)
        {
            var circle = new VisualElement();
            circle.style.width = 18;
            circle.style.height = 18;
            circle.style.marginRight = 4;
            circle.style.marginBottom = 4;
            SetRadius(circle, 9);
            SetBorder(circle, 1, borderColor);
            return circle;
        }

        private static VisualElement CreateFill(Texture2D texture)
        {
            var fill = new VisualElement();
            fill.style.position = Position.Absolute;
            fill.style.top = 0;
            fill.style.bottom = 0;
            fill.style.left = 0;
            fill.style.right = 0;
            fill.style.backgroundImage = texture;
            return fill;
        }

        private static Texture2D CreateTexture(int width, int height, Func<int, int, Color> pixel)
        {
            var tex = new Texture2D(width, height);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = pixel(x, y);
            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static void SetBorder(VisualElement element, float width, Color color)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }

        private const string UserPaletteKey = "YUCP_UserPalette";

        private static List<Color> LoadUserPalette()
        {
            string raw = EditorPrefs.GetString(UserPaletteKey, "");
            if (string.IsNullOrEmpty(raw)) return new List<Color>();
            return raw.Split('|').Select(s => ColorUtility.TryParseHtmlString("#" + s, out var c) ? c : Color.white).ToList();
        }

        private static void SaveToUserPalette(Color color)
        {
            var palette = LoadUserPalette();
            string hex = ColorUtility.ToHtmlStringRGB(color);
            if (palette.Any(c => ColorUtility.ToHtmlStringRGB(c) == hex)) return;
            palette.Insert(0, color);
            if (palette.Count > 10) palette.RemoveAt(palette.Count - 1);
            EditorPrefs.SetString(UserPaletteKey, string.Join("|", palette.Select(c => ColorUtility.ToHtmlStringRGB(c))));
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
