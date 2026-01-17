using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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

        public event Action<IReadOnlyList<string>> OnTagsChanged;

        public IReadOnlyList<string> SelectedTags => _selectedTags;

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
                
                var label = new Label();
                label.name = "label";
                container.Add(label);
                
                return container;
            };
            
            _autocompleteList.bindItem = (e, i) => {
                var container = e as VisualElement;
                if (i >= 0 && i < _filteredOptions.Count)
                {
                    var tag = _filteredOptions[i];
                    var label = container.Q<Label>("label");
                    
                    if (tag.StartsWith("CREATE:"))
                    {
                        label.text = $"Create tag: \"{tag.Substring(7)}\"";
                        label.AddToClassList("yucp-tag-autocomplete-item-new");
                    }
                    else
                    {
                        label.text = tag;
                        label.RemoveFromClassList("yucp-tag-autocomplete-item-new");
                    }
                }
            };
            
            _autocompleteList.selectionType = SelectionType.Single;
            _autocompleteList.itemsSource = _filteredOptions;
            _autocompleteList.fixedItemHeight = 28;
            
            _autocompleteList.selectionChanged += (objs) => {
                 foreach(var obj in objs)
                 {
                     if(obj is string tag)
                     {
                         SelectTag(tag);
                     }
                 }
                 _autocompleteList.ClearSelection();
            };
            
            _overlayContainer.Add(_autocompleteList);
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

        private void RefreshChips()
        {
            _chipsContainer.Clear();
            foreach(var tag in _selectedTags)
            {
                var chip = new VisualElement();
                chip.AddToClassList("yucp-tag-chip");
                
                var label = new Label(tag);
                label.AddToClassList("yucp-tag-chip-label");
                chip.Add(label);

                var closeBtn = new Button(() => RemoveTag(tag));
                closeBtn.text = "×";
                closeBtn.AddToClassList("yucp-tag-chip-remove");
                chip.Add(closeBtn);

                _chipsContainer.Add(chip);
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
            
            // 1. Filter available tags (exclude selected)
            // 2. Filter by search text (case insensitive)
            
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
            
            // Limit count? User didn't specify. Assuming list is manageable.
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
                     // Use first option if nothing selected
                     SelectTag(_filteredOptions[0]);
                     evt.StopPropagation();
                 }
                 else if (!string.IsNullOrWhiteSpace(_inputField.value))
                 {
                     // Force create/add current valid input if enter pressed
                     // AddTag(_inputField.value); // Maybe? Usually better to use the CREATE proposal. 
                     // If there IS a proposal, it would have been caught above (CREATE option is first).
                 }
            }
        }

        private void ShowOverlay()
        {
            FilterOptions();
            
            if (_filteredOptions.Count == 0 && string.IsNullOrEmpty(_inputField.value))
            {
                // Empty search, no available tags -> hide
                 HideOverlay();
                 return;
            }
            
            // If we have options, show
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
