using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter.UI.Components
{
    public class TokenizedSearchField : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<TokenizedSearchField> { }

        public class SearchProposal
        {
            public string Label;
            public string Value;
            public bool IsTag;
        }

        private readonly VisualElement _chipsContainer;
        private readonly TextField _inputField;
        private Button _clearButton; // New clear button
        private readonly ListView _autocompleteList;
        private VisualElement _overlayContainer;
        private VisualElement _externalRoot; // External root for overlay attachment
        private bool _isAttachedToExternalRoot = false;

        private List<SearchProposal> _allOptions = new List<SearchProposal>();
        private List<string> _activeTags = new List<string>();
        private List<SearchProposal> _filteredOptions = new List<SearchProposal>();

        public event Action<string> OnTagAdded;
        public event Action<string> OnTagRemoved;
        public event Action<string> OnSearchValueChanged;

        public string value
        {
            get => _inputField.value;
            set 
            {
                _inputField.value = value;
                // Ensure clear button state is updated when set programmatically
                if (_clearButton != null)
                   _clearButton.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        public TokenizedSearchField()
        {
            AddToClassList("yucp-tokenized-field");

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.devtools/Editor/PackageExporter/UI/Components/TokenizedSearchField.uss");
            if (styleSheet != null) 
            {
                styleSheets.Add(styleSheet);
            }

            _chipsContainer = new VisualElement { name = "chips-container" };
            _chipsContainer.style.flexDirection = FlexDirection.Row;
            _chipsContainer.style.flexWrap = Wrap.Wrap;
            Add(_chipsContainer);

            _inputField = new TextField();
            _inputField.AddToClassList("yucp-tokenized-field-input");
            _inputField.isDelayed = false; // Real-time
            
            // Remove default border/background from inner Unity TextField to avoid double-border
            // We must target the inner input element specifically
            _inputField.RegisterCallback<AttachToPanelEvent>(evt => 
            {
                var inputElement = _inputField.Q("unity-text-input");
                if (inputElement != null) 
                {
                    inputElement.style.borderTopWidth = 0;
                    inputElement.style.borderBottomWidth = 0;
                    inputElement.style.borderLeftWidth = 0;
                    inputElement.style.borderRightWidth = 0;
                    inputElement.style.backgroundColor = Color.clear;
                    inputElement.style.marginLeft = 0;
                    inputElement.style.marginRight = 0;
                }
            });
            
            _inputField.style.flexGrow = 1;
            _inputField.style.marginLeft = 0;
            _inputField.style.marginRight = 0;
            _inputField.style.backgroundColor = Color.clear;
            _inputField.style.borderTopWidth = 0;
            _inputField.style.borderBottomWidth = 0;
            _inputField.style.borderLeftWidth = 0;
            _inputField.style.borderRightWidth = 0;
            
            _inputField.RegisterValueChangedCallback(OnInputChanged);
            _inputField.RegisterCallback<KeyDownEvent>(OnKeyDown);
            
            // Handle focus loss to close overlay (delayed to allow click)
            _inputField.RegisterCallback<FocusOutEvent>(e => {
                this.RemoveFromClassList("focused");
                schedule.Execute(() => HideOverlay()).ExecuteLater(200);
            });
            _inputField.RegisterCallback<FocusInEvent>(e => {
                this.AddToClassList("focused");
                if(!string.IsNullOrEmpty(_inputField.value)) ShowOverlay(); 
            });

            Add(_inputField);

            // Clear Button (New)
            _clearButton = new Button(() => {
                // Clear text
                _inputField.value = ""; 
                
                // Programmatic value change does NOT trigger callback, so we must invoke manually
                OnSearchValueChanged?.Invoke("");
                
                // Update UI state
                _clearButton.style.display = DisplayStyle.None;
                HideOverlay();
                
                // Focus back to input
                _inputField.Focus();
            });
            _clearButton.text = "×"; // Multiplication sign for nicer X
            _clearButton.AddToClassList("yucp-search-clear"); // Reuse existing class or new one
            _clearButton.style.display = DisplayStyle.None; // Hidden by default
            
            // Inline style for clear button if class is missing context
            _clearButton.style.backgroundColor = Color.clear;
            _clearButton.style.borderTopWidth = 0;
            _clearButton.style.borderBottomWidth = 0;
            _clearButton.style.borderLeftWidth = 0;
            _clearButton.style.borderRightWidth = 0;
            _clearButton.style.fontSize = 14;
            _clearButton.style.color = new Color(0.6f, 0.6f, 0.6f);
            _clearButton.style.width = 20;
            // Removed invalid cursor setting

            Add(_clearButton);

            // Overlay
            _overlayContainer = new VisualElement();
            _overlayContainer.AddToClassList("yucp-autocomplete-overlay");
            _overlayContainer.style.display = DisplayStyle.None;
            // Add stylesheet to overlay so it retains style when reparented
            if (styleSheet != null) _overlayContainer.styleSheets.Add(styleSheet);
            Add(_overlayContainer);

            _autocompleteList = new ListView();
            _autocompleteList.makeItem = () => {
                var container = new VisualElement();
                container.AddToClassList("yucp-autocomplete-item");
                
                var label = new Label();
                label.name = "label";
                container.Add(label);
                
                var typeLabel = new Label();
                typeLabel.name = "type";
                typeLabel.AddToClassList("yucp-autocomplete-item-type");
                container.Add(typeLabel);
                
                return container;
            };
            
            _autocompleteList.bindItem = (e, i) => {
                var container = e as VisualElement;
                if (i >= 0 && i < _filteredOptions.Count)
                {
                    var item = _filteredOptions[i];
                    container.Q<Label>("label").text = item.Label;
                    container.Q<Label>("type").text = item.IsTag ? "Tag" : "Profile";
                }
            };
            
            _autocompleteList.selectionType = SelectionType.Single;
            _autocompleteList.itemsSource = _filteredOptions;
            _autocompleteList.fixedItemHeight = 28;
            
            // Use specific selection logic
            _autocompleteList.selectionChanged += (objs) => {
                 foreach(var obj in objs)
                 {
                     if(obj is SearchProposal p)
                     {
                         SelectProposal(p);
                     }
                 }
                 _autocompleteList.ClearSelection();
            };
            
            _overlayContainer.Add(_autocompleteList);
        }

        /// <summary>
        /// Attaches the autocomplete overlay to an external root element.
        /// This solves layering issues where the dropdown appears behind other elements.
        /// </summary>
        public void AttachOverlayToRoot(VisualElement root)
        {
            if (root == null) return;
            _externalRoot = root;
            
            // Remove from current parent (self) if already added
            if (_overlayContainer.parent == this)
            {
                _overlayContainer.RemoveFromHierarchy();
            }
            
            // Add to external root
            if (_overlayContainer.parent != _externalRoot)
            {
                _externalRoot.Add(_overlayContainer);
            }
            
            _isAttachedToExternalRoot = true;
            _overlayContainer.style.position = Position.Absolute;
        }

        public void SetAvailableOptions(List<SearchProposal> options)
        {
            _allOptions = options ?? new List<SearchProposal>();
        }
        
        public void SetActiveTags(List<string> tags)
        {
             _activeTags = new List<string>(tags);
             RefreshChips();
        }

        public Func<string, Color> TagColorProvider;

        private void RefreshChips()
        {
            _chipsContainer.Clear();
            foreach(var tag in _activeTags)
            {
                var chip = new VisualElement();
                chip.AddToClassList("yucp-chip");
                
                if (TagColorProvider != null)
                {
                    var baseColor = TagColorProvider(tag);
                    chip.style.backgroundColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.15f));
                    chip.style.borderLeftColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));
                    chip.style.borderRightColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));
                    chip.style.borderTopColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));
                    chip.style.borderBottomColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));
                    
                    // We need to find the label and button after adding to style them too, or just add them now.
                    
                    var label = new Label(tag);
                    label.AddToClassList("yucp-chip-label");
                    label.style.color = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.95f));
                    chip.Add(label);
    
                    var closeBtn = new Button(() => RemoveTag(tag));
                    closeBtn.text = "×";
                    closeBtn.AddToClassList("yucp-chip-remove");
                    closeBtn.style.color = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.8f));
                    chip.Add(closeBtn);
                }
                else
                {
                    // Default behavior
                    var label = new Label(tag);
                    label.AddToClassList("yucp-chip-label");
                    chip.Add(label);
    
                    var closeBtn = new Button(() => RemoveTag(tag));
                    closeBtn.text = "×";
                    closeBtn.AddToClassList("yucp-chip-remove");
                    chip.Add(closeBtn);
                }

                _chipsContainer.Add(chip);
            }
        }

        public void AddTag(string tag)
        {
            if(!_activeTags.Contains(tag))
            {
                _activeTags.Add(tag);
                RefreshChips();
                OnTagAdded?.Invoke(tag);
            }
        }
        
        public void RemoveTag(string tag)
        {
             if(_activeTags.Contains(tag))
            {
                _activeTags.Remove(tag);
                RefreshChips();
                OnTagRemoved?.Invoke(tag);
            }
        }
        
        private void SelectProposal(SearchProposal p)
        {
            if (p.IsTag)
            {
                AddTag(p.Value);
                _inputField.value = ""; // Clear input after picking tag
            }
            else
            {
                // Is a Profile/Name - fill text
                _inputField.value = p.Value;
                // Don't clear, assume user wants to search for this name
                // Explicitly invoke update as programmatic value set might not trigger callback
                OnSearchValueChanged?.Invoke(p.Value);
                // Also hide overlay immediately
                HideOverlay();
            }
            // HideOverlay() is called by caller or here
            HideOverlay();
        }

        private void OnInputChanged(ChangeEvent<string> evt)
        {
            OnSearchValueChanged?.Invoke(evt.newValue);
            
            string searchText = evt.newValue;
            
            // Update clear button
            if (_clearButton != null)
                _clearButton.style.display = string.IsNullOrEmpty(searchText) ? DisplayStyle.None : DisplayStyle.Flex;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                HideOverlay();
                return;
            }

            // Filter options: Case insensitive contains
            // Exclude tags that are already active
            _filteredOptions = _allOptions
                .Where(p => 
                    p.Label.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 && 
                    (!p.IsTag || !_activeTags.Contains(p.Value))
                )
                .OrderBy(p => p.Label.Length) // Simple relevance
                .ToList();

            if (_filteredOptions.Count > 0)
            {
                _autocompleteList.itemsSource = _filteredOptions;
                _autocompleteList.RefreshItems();
                
                // Adjust height
                float height = Mathf.Min(250, _filteredOptions.Count * 28 + 10);
                _overlayContainer.style.height = height;
                
                ShowOverlay();
            }
            else
            {
                HideOverlay();
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Backspace && string.IsNullOrEmpty(_inputField.value) && _activeTags.Count > 0)
            {
                // Remove last tag
                var last = _activeTags.Last();
                RemoveTag(last);
                evt.StopPropagation(); 
            }
            else if (evt.keyCode == KeyCode.DownArrow)
            {
                if (_overlayContainer.style.display == DisplayStyle.Flex && _filteredOptions.Count > 0)
                {
                    // Focus list or select next
                    int newIndex = (_autocompleteList.selectedIndex + 1) % _filteredOptions.Count;
                    _autocompleteList.selectedIndex = newIndex;
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
                 // If overlay is open and has selection, pick it
                 if (_overlayContainer.style.display == DisplayStyle.Flex && _autocompleteList.selectedIndex >= 0 && _autocompleteList.selectedIndex < _filteredOptions.Count)
                 {
                     SelectProposal(_filteredOptions[_autocompleteList.selectedIndex]);
                     evt.StopPropagation();
                 }
                 else if (_overlayContainer.style.display == DisplayStyle.Flex && _filteredOptions.Count > 0)
                 {
                     // Fallback: pick first if visible
                     SelectProposal(_filteredOptions[0]);
                     evt.StopPropagation();
                 }
            }
        }

        private void ShowOverlay()
        {
            _overlayContainer.style.display = DisplayStyle.Flex;
            
            // If attached to external root, position relative to this element's world bounds
            if (_isAttachedToExternalRoot && _externalRoot != null)
            {
                var myBounds = this.worldBound;
                var rootBounds = _externalRoot.worldBound;
                
                // Position below the search field
                float left = myBounds.x - rootBounds.x;
                float top = myBounds.yMax - rootBounds.y + 4; // 4px gap
                float width = myBounds.width;
                
                _overlayContainer.style.left = left;
                _overlayContainer.style.top = top;
                _overlayContainer.style.width = width;
            }
            
            _overlayContainer.BringToFront();
        }

        private void HideOverlay()
        {
            _overlayContainer.style.display = DisplayStyle.None;
        }

    }
}
