using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Components
{
    /// <summary>
    /// Generic reusable sidebar component with header, search, scrollable list, and action buttons
    /// </summary>
    public class YucpSidebar
    {
        public VisualElement Root { get; private set; }
        public VisualElement ListContainer { get; private set; }
        public ScrollView ScrollView { get; private set; }
        public TextField SearchField { get; private set; }
        
        private string _headerText;
        private string _searchPlaceholder;
        private bool _showSearch;
        private List<SidebarActionButton> _actionButtons;
        private Func<string, bool> _filterCallback;
        private Action<string> _onSearchChanged;
        private Action _onRefresh;
        
        private string _searchText = "";
        private VisualElement _listContainer;
        private VisualElement _listContainerOverlay;
        
        public class SidebarActionButton
        {
            public string Text { get; set; }
            public string ButtonClass { get; set; } = "yucp-button";
            public Action OnClick { get; set; }
            public bool IsPrimary { get; set; }
            public bool IsDanger { get; set; }
            public bool IsSecondary { get; set; }
        }
        
        public class SidebarConfig
        {
            public string HeaderText { get; set; } = "Items";
            public string SearchPlaceholder { get; set; } = "";
            public bool ShowSearch { get; set; } = true;
            public List<SidebarActionButton> ActionButtons { get; set; } = new List<SidebarActionButton>();
            public Func<string, bool> FilterCallback { get; set; }
            public Action<string> OnSearchChanged { get; set; }
            public Action OnRefresh { get; set; }
        }
        
        public YucpSidebar(SidebarConfig config)
        {
            _headerText = config.HeaderText;
            _searchPlaceholder = config.SearchPlaceholder;
            _showSearch = config.ShowSearch;
            _actionButtons = config.ActionButtons ?? new List<SidebarActionButton>();
            _filterCallback = config.FilterCallback;
            _onSearchChanged = config.OnSearchChanged;
            _onRefresh = config.OnRefresh;
        }
        
        public VisualElement CreateSidebar(bool isOverlay = false)
        {
            var container = new VisualElement();
            container.AddToClassList("yucp-profile-list-container");
            
            // Header
            var header = new Label(_headerText);
            header.AddToClassList("yucp-profile-list-header");
            container.Add(header);
            
            // Search bar
            if (_showSearch)
            {
                var searchContainer = new VisualElement();
                searchContainer.AddToClassList("yucp-profile-search-container");
                
                SearchField = new TextField();
                SearchField.AddToClassList("yucp-input");
                SearchField.AddToClassList("yucp-profile-search-field");
                SearchField.value = _searchText;
                SearchField.RegisterValueChangedCallback(evt =>
                {
                    _searchText = evt.newValue;
                    _onSearchChanged?.Invoke(_searchText);
                });
                searchContainer.Add(SearchField);
                container.Add(searchContainer);
            }
            
            // List scrollview
            ScrollView = new ScrollView();
            ScrollView.AddToClassList("yucp-profile-list-scroll");
            
            // Create and store the appropriate container
            _listContainer = new VisualElement();
            // Always set ListContainer so it's accessible regardless of overlay state
            ListContainer = _listContainer;
            if (isOverlay)
            {
                _listContainerOverlay = _listContainer;
            }
            
            ScrollView.Add(_listContainer);
            container.Add(ScrollView);
            
            // Action buttons section
            if (_actionButtons.Count > 0)
            {
                var actionsSection = new VisualElement();
                actionsSection.AddToClassList("yucp-profile-actions-section");
                
                // Find primary button (first one that is primary)
                var primaryButton = _actionButtons.Find(b => b.IsPrimary);
                if (primaryButton != null)
                {
                    var button = new Button(primaryButton.OnClick) { text = primaryButton.Text };
                    button.AddToClassList("yucp-button");
                    button.AddToClassList("yucp-button-primary");
                    button.style.marginLeft = 0;
                    button.style.marginRight = 0;
                    actionsSection.Add(button);
                }
                
                // Find all non-primary buttons
                var nonPrimaryButtons = new List<SidebarActionButton>();
                for (int i = 0; i < _actionButtons.Count; i++)
                {
                    var btn = _actionButtons[i];
                    if (!btn.IsPrimary)
                    {
                        nonPrimaryButtons.Add(btn);
                    }
                }
                
                // If we have 2+ non-primary buttons, put them all in a row
                // Otherwise, if there's only 1 non-primary button, it goes full width
                if (nonPrimaryButtons.Count >= 2)
                {
                    var secondaryActions = new VisualElement();
                    secondaryActions.AddToClassList("yucp-profile-secondary-actions");
                    secondaryActions.style.width = Length.Percent(100);
                    secondaryActions.style.marginLeft = 0;
                    secondaryActions.style.marginRight = 0;
                    secondaryActions.style.paddingLeft = 0;
                    secondaryActions.style.paddingRight = 0;
                    
                    for (int i = 0; i < nonPrimaryButtons.Count; i++)
                    {
                        var btnConfig = nonPrimaryButtons[i];
                        var btn = new Button(btnConfig.OnClick) { text = btnConfig.Text };
                        btn.AddToClassList("yucp-button");
                        if (btnConfig.IsDanger)
                            btn.AddToClassList("yucp-button-danger");
                        else if (btnConfig.IsSecondary)
                            btn.AddToClassList("yucp-button-secondary");
                        else
                            btn.AddToClassList("yucp-button-action");
                        btn.style.marginLeft = 0;
                        if (i < nonPrimaryButtons.Count - 1)
                        {
                            btn.style.marginRight = 4;
                        }
                        else
                        {
                            btn.style.marginRight = 0;
                            btn.AddToClassList("yucp-profile-action-last-in-row");
                        }
                        secondaryActions.Add(btn);
                    }
                    actionsSection.Add(secondaryActions);
                }
                else if (nonPrimaryButtons.Count == 1)
                {
                    // Single non-primary button goes full width
                    var lastButton = nonPrimaryButtons[0];
                    var button = new Button(lastButton.OnClick) { text = lastButton.Text };
                    button.AddToClassList("yucp-button");
                    if (lastButton.IsSecondary)
                        button.AddToClassList("yucp-button-secondary");
                    else if (lastButton.IsDanger)
                        button.AddToClassList("yucp-button-danger");
                    else
                        button.AddToClassList("yucp-button-action");
                    button.AddToClassList("yucp-profile-action-last");
                    button.style.marginLeft = 0;
                    button.style.marginRight = 0;
                    actionsSection.Add(button);
                }
                
                container.Add(actionsSection);
            }
            
            Root = container;
            return container;
        }
        
        public void UpdateList<T>(List<T> items, Func<T, VisualElement> createItemCallback, Func<T, string> getDisplayName = null)
        {
            UpdateListContainer(_listContainer, items, createItemCallback, getDisplayName);
            if (_listContainerOverlay != null)
            {
                UpdateListContainer(_listContainerOverlay, items, createItemCallback, getDisplayName);
            }
        }
        
        private void UpdateListContainer<T>(VisualElement container, List<T> items, Func<T, VisualElement> createItemCallback, Func<T, string> getDisplayName)
        {
            if (container == null) return;
            
            container.Clear();
            
            // Filter items using search text
            var filteredItems = new List<T>();
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                filteredItems.AddRange(items);
            }
            else
            {
                var searchLower = _searchText.ToLowerInvariant();
                foreach (var item in items)
                {
                    if (item == null) continue;
                    
                    if (_filterCallback != null)
                    {
                        if (_filterCallback(_searchText))
                        {
                            filteredItems.Add(item);
                        }
                    }
                    else if (getDisplayName != null)
                    {
                        var displayName = getDisplayName(item)?.ToLowerInvariant() ?? "";
                        if (displayName.Contains(searchLower))
                        {
                            filteredItems.Add(item);
                        }
                    }
                    else
                    {
                        filteredItems.Add(item);
                    }
                }
            }
            
            if (filteredItems.Count == 0)
            {
                if (items.Count == 0)
                {
                    var emptyLabel = new Label("No items found");
                    emptyLabel.AddToClassList("yucp-label-secondary");
                    emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    emptyLabel.style.paddingTop = 20;
                    emptyLabel.style.paddingBottom = 10;
                    container.Add(emptyLabel);
                    
                    var hintLabel = new Label("Create one using the button below");
                    hintLabel.AddToClassList("yucp-label-small");
                    hintLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    container.Add(hintLabel);
                }
                else
                {
                    var emptyLabel = new Label("No items match your search");
                    emptyLabel.AddToClassList("yucp-label-secondary");
                    emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    emptyLabel.style.paddingTop = 20;
                    emptyLabel.style.paddingBottom = 10;
                    container.Add(emptyLabel);
                }
                return;
            }
            
            foreach (var item in filteredItems)
            {
                var itemElement = createItemCallback(item);
                if (itemElement != null)
                {
                    container.Add(itemElement);
                }
            }
        }
        
        public void ClearSearch()
        {
            _searchText = "";
            if (SearchField != null)
            {
                SearchField.value = "";
            }
        }
        
        public string GetSearchText()
        {
            return _searchText;
        }
    }
}

