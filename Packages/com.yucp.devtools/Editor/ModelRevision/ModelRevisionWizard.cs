using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Linq;
using System.Collections.Generic;
using YUCP.Components;
using YUCP.DevTools;
using YUCP.DevTools.Editor.ModelRevision;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.DevTools.Editor
{
    /// <summary>
    /// Model Revision Wizard - comprehensive prefab sync tool.
    /// Provides visual diff, mapping, and synchronization between prefabs.
    /// </summary>
    public class ModelRevisionWizard : EditorWindow
    {
        // Target references
        private ModelRevisionVariant _targetVariant;
        private ModelRevisionBase _revisionBase;
        private SerializedObject _serializedBase;
        
        // UI Elements
        private VisualElement _rootElement;
        private VisualElement _tabContainer;
        private VisualElement _contentContainer;
        private List<Button> _tabButtons = new List<Button>();
        private int _selectedTabIndex = 0;
        
        // Comparison results
        private PrefabComparisonResult _comparisonResult;
        private Dictionary<string, string> _bonePathMapping;
        
        // Tab names
        private static readonly string[] TabNames = { "Setup", "Hierarchy", "Components", "Blendshapes", "Bones", "Sync" };
        
        [MenuItem("Tools/YUCP/Others/Development/Model Revision Manager")]
        public static ModelRevisionWizard ShowWindow()
        {
            var window = GetWindow<ModelRevisionWizard>();
            window.titleContent = new GUIContent("Model Revision Manager");
            window.minSize = new Vector2(900, 650);
            window.Show();
            return window;
        }

        public static void OpenWizard(ModelRevisionVariant variant)
        {
            var window = ShowWindow();
            window.SetTargetVariant(variant);
        }

        public void SetTargetVariant(ModelRevisionVariant variant)
        {
            _targetVariant = variant;
            if (_targetVariant != null)
            {
                _revisionBase = _targetVariant.revisionBase;
                if (_revisionBase != null)
                {
                    _serializedBase = new SerializedObject(_revisionBase);
                }
            }
            else
            {
                _revisionBase = null;
                _serializedBase = null;
            }
            RefreshComparisonIfNeeded();
            RefreshUI();
        }

        public void SetTargetBase(ModelRevisionBase revisionBase)
        {
            _revisionBase = revisionBase;
            if (_revisionBase != null)
            {
                _serializedBase = new SerializedObject(_revisionBase);
            }
            _targetVariant = null;
            RefreshUI();
        }

        public void CreateGUI()
        {
            _rootElement = rootVisualElement;
            YUCPUIToolkitHelper.LoadDesignSystemStyles(_rootElement);
            
            // Apply dark background
            _rootElement.style.backgroundColor = new Color(0.14f, 0.14f, 0.14f);
            _rootElement.style.flexGrow = 1;
            
            BuildUI();
        }

        private void BuildUI()
        {
            _rootElement.Clear();
            
            // Header
            var header = CreateHeader();
            _rootElement.Add(header);
            
            // Main container
            var mainContainer = new VisualElement();
            mainContainer.style.flexGrow = 1;
            mainContainer.style.flexDirection = FlexDirection.Row;
            
            // Sidebar with tabs
            var sidebar = CreateSidebar();
            mainContainer.Add(sidebar);
            
            // Content area
            _contentContainer = new VisualElement();
            _contentContainer.style.flexGrow = 1;
            _contentContainer.style.paddingLeft = 20;
            _contentContainer.style.paddingRight = 20;
            _contentContainer.style.paddingTop = 15;
            _contentContainer.style.paddingBottom = 15;
            _contentContainer.style.overflow = Overflow.Hidden;
            
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            _contentContainer.Add(scrollView);
            
            mainContainer.Add(_contentContainer);
            _rootElement.Add(mainContainer);
            
            RefreshUI();
        }

        private VisualElement CreateHeader()
        {
            var header = new VisualElement();
            header.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            header.style.paddingLeft = 20;
            header.style.paddingRight = 20;
            header.style.paddingTop = 15;
            header.style.paddingBottom = 15;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.1f, 0.1f, 0.1f);
            
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.justifyContent = Justify.SpaceBetween;
            titleRow.style.alignItems = Align.Center;
            
            var title = new Label("Model Revision Manager");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            titleRow.Add(title);
            
            // Status badge
            var statusBadge = new Label(_comparisonResult != null 
                ? $"{_comparisonResult.TotalDifferences} differences found" 
                : "No comparison");
            statusBadge.style.fontSize = 11;
            statusBadge.style.paddingLeft = 10;
            statusBadge.style.paddingRight = 10;
            statusBadge.style.paddingTop = 4;
            statusBadge.style.paddingBottom = 4;
            statusBadge.style.borderTopLeftRadius = 12;
            statusBadge.style.borderTopRightRadius = 12;
            statusBadge.style.borderBottomLeftRadius = 12;
            statusBadge.style.borderBottomRightRadius = 12;
            statusBadge.style.backgroundColor = _comparisonResult != null && _comparisonResult.TotalDifferences > 0
                ? new Color(0.886f, 0.647f, 0.29f, 0.3f)
                : new Color(0.212f, 0.749f, 0.694f, 0.3f);
            statusBadge.style.color = _comparisonResult != null && _comparisonResult.TotalDifferences > 0
                ? new Color(0.886f, 0.647f, 0.29f)
                : new Color(0.212f, 0.749f, 0.694f);
            titleRow.Add(statusBadge);
            
            header.Add(titleRow);
            return header;
        }

        private VisualElement CreateSidebar()
        {
            var sidebar = new VisualElement();
            sidebar.style.width = 180;
            sidebar.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            sidebar.style.paddingTop = 15;
            sidebar.style.borderRightWidth = 1;
            sidebar.style.borderRightColor = new Color(0.1f, 0.1f, 0.1f);
            
            _tabButtons.Clear();
            
            for (int i = 0; i < TabNames.Length; i++)
            {
                int tabIndex = i;
                var tabButton = new Button(() => SelectTab(tabIndex)) { text = TabNames[i] };
                tabButton.style.height = 36;
                tabButton.style.marginLeft = 10;
                tabButton.style.marginRight = 10;
                tabButton.style.marginBottom = 5;
                tabButton.style.borderTopLeftRadius = 6;
                tabButton.style.borderTopRightRadius = 6;
                tabButton.style.borderBottomLeftRadius = 6;
                tabButton.style.borderBottomRightRadius = 6;
                tabButton.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
                tabButton.style.color = new Color(0.7f, 0.7f, 0.7f);
                tabButton.style.unityTextAlign = TextAnchor.MiddleLeft;
                tabButton.style.paddingLeft = 15;
                tabButton.style.borderTopWidth = 0;
                tabButton.style.borderBottomWidth = 0;
                tabButton.style.borderLeftWidth = 0;
                tabButton.style.borderRightWidth = 0;
                
                _tabButtons.Add(tabButton);
                sidebar.Add(tabButton);
            }
            
            return sidebar;
        }

        private void SelectTab(int index)
        {
            _selectedTabIndex = index;
            
            // Update button styles
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool isActive = i == index;
                _tabButtons[i].style.backgroundColor = isActive 
                    ? new Color(0.212f, 0.749f, 0.694f, 0.2f) 
                    : new Color(0.2f, 0.2f, 0.2f);
                _tabButtons[i].style.color = isActive 
                    ? new Color(0.212f, 0.749f, 0.694f) 
                    : new Color(0.7f, 0.7f, 0.7f);
            }
            
            RefreshContent();
        }

        private void RefreshUI()
        {
            if (_rootElement == null) return;
            SelectTab(_selectedTabIndex);
        }

        private void RefreshContent()
        {
            if (_contentContainer == null) return;
            
            var scrollView = _contentContainer.Q<ScrollView>();
            if (scrollView == null) return;
            
            scrollView.Clear();
            
            switch (_selectedTabIndex)
            {
                case 0: BuildSetupTab(scrollView); break;
                case 1: BuildHierarchyTab(scrollView); break;
                case 2: BuildComponentsTab(scrollView); break;
                case 3: BuildBlendshapesTab(scrollView); break;
                case 4: BuildBonesTab(scrollView); break;
                case 5: BuildSyncTab(scrollView); break;
            }
        }

        #region Tab Content Builders

        private void BuildSetupTab(VisualElement container)
        {
            // Prefab selection card
            var prefabCard = YUCPUIToolkitHelper.CreateCard("Prefab Selection", "Select the source and target prefabs for synchronization");
            var prefabContent = YUCPUIToolkitHelper.GetCardContent(prefabCard);
            
            // Source prefab
            var sourceRow = new VisualElement();
            sourceRow.style.flexDirection = FlexDirection.Row;
            sourceRow.style.alignItems = Align.Center;
            sourceRow.style.marginBottom = 10;
            
            var sourceLabel = new Label("Source Prefab:");
            sourceLabel.style.width = 120;
            sourceRow.Add(sourceLabel);
            
            var sourceField = new ObjectField();
            sourceField.objectType = typeof(GameObject);
            sourceField.allowSceneObjects = true;
            sourceField.style.flexGrow = 1;
            if (_revisionBase != null)
            {
                sourceField.value = _revisionBase.basePrefab;
            }
            sourceField.RegisterValueChangedCallback(evt =>
            {
                if (_revisionBase != null)
                {
                    Undo.RecordObject(_revisionBase, "Change Source Prefab");
                    _revisionBase.basePrefab = evt.newValue as GameObject;
                    EditorUtility.SetDirty(_revisionBase);
                    RefreshComparisonIfNeeded();
                }
            });
            sourceRow.Add(sourceField);
            prefabContent.Add(sourceRow);
            
            // Target prefab
            var targetRow = new VisualElement();
            targetRow.style.flexDirection = FlexDirection.Row;
            targetRow.style.alignItems = Align.Center;
            targetRow.style.marginBottom = 10;
            
            var targetLabel = new Label("Target Prefab:");
            targetLabel.style.width = 120;
            targetRow.Add(targetLabel);
            
            var targetField = new ObjectField();
            targetField.objectType = typeof(GameObject);
            targetField.allowSceneObjects = true;
            targetField.style.flexGrow = 1;
            if (_targetVariant != null)
            {
                targetField.value = _targetVariant.gameObject;
            }
            targetField.RegisterValueChangedCallback(evt =>
            {
                var go = evt.newValue as GameObject;
                if (go != null)
                {
                    var variant = go.GetComponent<ModelRevisionVariant>();
                    if (variant != null)
                    {
                        SetTargetVariant(variant);
                    }
                    else
                    {
                        if (EditorUtility.DisplayDialog("Add Component?",
                            $"'{go.name}' doesn't have a ModelRevisionVariant component. Add one?",
                            "Add", "Cancel"))
                        {
                            variant = go.AddComponent<ModelRevisionVariant>();
                            if (_revisionBase != null)
                            {
                                variant.revisionBase = _revisionBase;
                            }
                            SetTargetVariant(variant);
                            EditorUtility.SetDirty(go);
                        }
                    }
                }
            });
            targetRow.Add(targetField);
            prefabContent.Add(targetRow);
            
            // Analyze button
            var analyzeButton = YUCPUIToolkitHelper.CreateButton("Analyze Differences", () => RunComparison(), YUCPUIToolkitHelper.ButtonVariant.Primary);
            analyzeButton.style.height = 36;
            analyzeButton.style.marginTop = 10;
            prefabContent.Add(analyzeButton);
            
            container.Add(prefabCard);
            
            // Revision base card
            if (_revisionBase == null)
            {
                var createBaseCard = YUCPUIToolkitHelper.CreateCard("Model Revision Base", "Create or assign a base configuration");
                var createBaseContent = YUCPUIToolkitHelper.GetCardContent(createBaseCard);
                
                createBaseContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "No Model Revision Base assigned. Create or select one to save mappings.",
                    YUCPUIToolkitHelper.MessageType.Warning));
                
                var baseRow = new VisualElement();
                baseRow.style.flexDirection = FlexDirection.Row;
                baseRow.style.marginTop = 10;
                
                var createBaseButton = YUCPUIToolkitHelper.CreateButton("Create New Base", () => CreateNewBase(), YUCPUIToolkitHelper.ButtonVariant.Primary);
                createBaseButton.style.flexGrow = 1;
                createBaseButton.style.marginRight = 5;
                baseRow.Add(createBaseButton);
                
                var selectBaseField = new ObjectField() { objectType = typeof(ModelRevisionBase) };
                selectBaseField.style.flexGrow = 1;
                selectBaseField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is ModelRevisionBase mrb)
                    {
                        SetTargetBase(mrb);
                    }
                });
                baseRow.Add(selectBaseField);
                
                createBaseContent.Add(baseRow);
                container.Add(createBaseCard);
            }
            
            // Quick stats card if we have a comparison
            if (_comparisonResult != null)
            {
                var statsCard = YUCPUIToolkitHelper.CreateCard("Comparison Summary", "Overview of detected differences");
                var statsContent = YUCPUIToolkitHelper.GetCardContent(statsCard);
                
                AddStatRow(statsContent, "Hierarchy Differences", 
                    $"{_comparisonResult.Hierarchy?.Count ?? 0}", 
                    _comparisonResult.Hierarchy?.Count > 0);
                AddStatRow(statsContent, "Component Differences", 
                    $"{_comparisonResult.Components?.Count ?? 0}",
                    _comparisonResult.Components?.Count > 0);
                AddStatRow(statsContent, "Blendshape Differences", 
                    $"{_comparisonResult.Blendshapes?.Count ?? 0}",
                    _comparisonResult.Blendshapes?.Count > 0);
                AddStatRow(statsContent, "Matched GameObjects", 
                    $"{_comparisonResult.Hierarchy?.Matched?.Count ?? 0}",
                    false);
                
                container.Add(statsCard);
            }
        }

        private void AddStatRow(VisualElement container, string label, string value, bool hasIssues)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.paddingTop = 5;
            row.style.paddingBottom = 5;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);
            
            var labelElement = new Label(label);
            labelElement.style.color = new Color(0.7f, 0.7f, 0.7f);
            row.Add(labelElement);
            
            var valueElement = new Label(value);
            valueElement.style.color = hasIssues 
                ? new Color(0.886f, 0.647f, 0.29f) 
                : new Color(0.212f, 0.749f, 0.694f);
            valueElement.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(valueElement);
            
            container.Add(row);
        }

        private void BuildHierarchyTab(VisualElement container)
        {
            if (_comparisonResult == null)
            {
                container.Add(CreateNoComparisonMessage());
                return;
            }
            
            var hierarchy = _comparisonResult.Hierarchy;
            
            // Source-only objects
            if (hierarchy.AddedInSource.Count > 0)
            {
                var sourceCard = YUCPUIToolkitHelper.CreateCard($"Objects in Source Only ({hierarchy.AddedInSource.Count})", 
                    "GameObjects that exist in source but not target");
                var sourceContent = YUCPUIToolkitHelper.GetCardContent(sourceCard);
                
                foreach (var diff in hierarchy.AddedInSource.Take(20))
                {
                    var row = CreateDiffRow(diff.Path, "Add to Target", () => AddToTarget(diff));
                    sourceContent.Add(row);
                }
                
                if (hierarchy.AddedInSource.Count > 20)
                {
                    sourceContent.Add(CreateMoreLabel(hierarchy.AddedInSource.Count - 20));
                }
                
                container.Add(sourceCard);
            }
            
            // Target-only objects
            if (hierarchy.AddedInTarget.Count > 0)
            {
                var targetCard = YUCPUIToolkitHelper.CreateCard($"Objects in Target Only ({hierarchy.AddedInTarget.Count})",
                    "GameObjects that exist in target but not source");
                var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
                
                foreach (var diff in hierarchy.AddedInTarget.Take(20))
                {
                    var row = CreateDiffRow(diff.Path, "Remove", () => RemoveFromTarget(diff), true);
                    targetContent.Add(row);
                }
                
                if (hierarchy.AddedInTarget.Count > 20)
                {
                    targetContent.Add(CreateMoreLabel(hierarchy.AddedInTarget.Count - 20));
                }
                
                container.Add(targetCard);
            }
            
            // Matched objects with fuzzy matching
            var fuzzyMatches = hierarchy.Matched.Where(m => m.IsFuzzyMatch).ToList();
            if (fuzzyMatches.Count > 0)
            {
                var fuzzyCard = YUCPUIToolkitHelper.CreateCard($"Fuzzy Matched ({fuzzyMatches.Count})",
                    "GameObjects matched by name similarity");
                var fuzzyContent = YUCPUIToolkitHelper.GetCardContent(fuzzyCard);
                
                foreach (var match in fuzzyMatches.Take(10))
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.justifyContent = Justify.SpaceBetween;
                    row.style.paddingTop = 5;
                    row.style.paddingBottom = 5;
                    row.style.borderBottomWidth = 1;
                    row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);
                    
                    var pathLabel = new Label($"{match.SourcePath} → {match.TargetPath}");
                    pathLabel.style.color = new Color(0.886f, 0.647f, 0.29f);
                    pathLabel.style.fontSize = 11;
                    row.Add(pathLabel);
                    
                    fuzzyContent.Add(row);
                }
                
                container.Add(fuzzyCard);
            }
            
            // Summary if nothing to show
            if (hierarchy.AddedInSource.Count == 0 && hierarchy.AddedInTarget.Count == 0 && fuzzyMatches.Count == 0)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Hierarchies match! No differences detected.",
                    YUCPUIToolkitHelper.MessageType.Info));
            }
        }

        private void BuildComponentsTab(VisualElement container)
        {
            if (_comparisonResult == null)
            {
                container.Add(CreateNoComparisonMessage());
                return;
            }
            
            if (_comparisonResult.Components.Count == 0)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "No component differences detected.",
                    YUCPUIToolkitHelper.MessageType.Info));
                return;
            }
            
            // Group by type
            var groupedByType = _comparisonResult.Components
                .GroupBy(c => c.ComponentType?.Name ?? "Unknown")
                .OrderByDescending(g => g.Count());
            
            foreach (var group in groupedByType)
            {
                var card = YUCPUIToolkitHelper.CreateCard($"{group.Key} ({group.Count()})", null);
                var content = YUCPUIToolkitHelper.GetCardContent(card);
                
                foreach (var comp in group.Take(10))
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.justifyContent = Justify.SpaceBetween;
                    row.style.alignItems = Align.Center;
                    row.style.paddingTop = 5;
                    row.style.paddingBottom = 5;
                    row.style.borderBottomWidth = 1;
                    row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);
                    
                    var infoContainer = new VisualElement();
                    
                    var pathLabel = new Label(comp.ObjectPath);
                    pathLabel.style.fontSize = 11;
                    pathLabel.style.color = Color.white;
                    infoContainer.Add(pathLabel);
                    
                    var typeLabel = new Label(comp.Type.ToString());
                    typeLabel.style.fontSize = 10;
                    typeLabel.style.color = comp.Type == DiffType.Added 
                        ? new Color(0.212f, 0.749f, 0.694f)
                        : comp.Type == DiffType.Removed 
                            ? new Color(0.886f, 0.29f, 0.29f)
                            : new Color(0.886f, 0.647f, 0.29f);
                    infoContainer.Add(typeLabel);
                    
                    row.Add(infoContainer);
                    
                    var syncButton = new Button(() => SyncComponent(comp)) { text = "Sync" };
                    syncButton.style.height = 22;
                    syncButton.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
                    syncButton.style.color = Color.white;
                    row.Add(syncButton);
                    
                    content.Add(row);
                }
                
                if (group.Count() > 10)
                {
                    content.Add(CreateMoreLabel(group.Count() - 10));
                }
                
                container.Add(card);
            }
        }

        private void BuildBlendshapesTab(VisualElement container)
        {
            if (_comparisonResult == null)
            {
                container.Add(CreateNoComparisonMessage());
                return;
            }
            
            if (_comparisonResult.Blendshapes.Count == 0)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Blendshapes match! No differences detected.",
                    YUCPUIToolkitHelper.MessageType.Info));
                return;
            }
            
            // Auto-map button
            var autoMapCard = YUCPUIToolkitHelper.CreateCard("Auto-Mapping", "Automatically map blendshapes by name similarity");
            var autoMapContent = YUCPUIToolkitHelper.GetCardContent(autoMapCard);
            
            var autoMapButton = YUCPUIToolkitHelper.CreateButton("Auto-Map Blendshapes", () => AutoMapBlendshapes(), YUCPUIToolkitHelper.ButtonVariant.Primary);
            autoMapButton.style.height = 32;
            autoMapContent.Add(autoMapButton);
            container.Add(autoMapCard);
            
            // Source-only blendshapes
            var sourceOnly = _comparisonResult.Blendshapes.Where(b => b.Type == DiffType.Added).ToList();
            if (sourceOnly.Count > 0)
            {
                var sourceCard = YUCPUIToolkitHelper.CreateCard($"Source Only ({sourceOnly.Count})", 
                    "Blendshapes in source that need mapping");
                var sourceContent = YUCPUIToolkitHelper.GetCardContent(sourceCard);
                
                foreach (var bs in sourceOnly.Take(20))
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.paddingTop = 3;
                    row.style.paddingBottom = 3;
                    
                    var label = new Label($"{bs.SourceMeshName}: {bs.SourceBlendshapeName}");
                    label.style.fontSize = 11;
                    label.style.color = new Color(0.8f, 0.8f, 0.8f);
                    row.Add(label);
                    
                    sourceContent.Add(row);
                }
                
                if (sourceOnly.Count > 20)
                {
                    sourceContent.Add(CreateMoreLabel(sourceOnly.Count - 20));
                }
                
                container.Add(sourceCard);
            }
            
            // Target-only blendshapes  
            var targetOnly = _comparisonResult.Blendshapes.Where(b => b.Type == DiffType.Removed).ToList();
            if (targetOnly.Count > 0)
            {
                var targetCard = YUCPUIToolkitHelper.CreateCard($"Target Only ({targetOnly.Count})",
                    "Blendshapes in target that aren't in source");
                var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
                
                foreach (var bs in targetOnly.Take(20))
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.paddingTop = 3;
                    row.style.paddingBottom = 3;
                    
                    var label = new Label($"{bs.TargetMeshName}: {bs.TargetBlendshapeName}");
                    label.style.fontSize = 11;
                    label.style.color = new Color(0.6f, 0.6f, 0.6f);
                    row.Add(label);
                    
                    targetContent.Add(row);
                }
                
                container.Add(targetCard);
            }
            
            // Current mappings
            if (_revisionBase != null && _revisionBase.blendshapeMappings.Count > 0)
            {
                var mappingsCard = YUCPUIToolkitHelper.CreateCard($"Current Mappings ({_revisionBase.blendshapeMappings.Count})", null);
                var mappingsContent = YUCPUIToolkitHelper.GetCardContent(mappingsCard);
                
                foreach (var mapping in _revisionBase.blendshapeMappings.Take(20))
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.justifyContent = Justify.SpaceBetween;
                    row.style.paddingTop = 3;
                    row.style.paddingBottom = 3;
                    row.style.borderBottomWidth = 1;
                    row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);
                    
                    var label = new Label($"{mapping.sourceName} → {mapping.targetName}");
                    label.style.fontSize = 11;
                    label.style.color = new Color(0.212f, 0.749f, 0.694f);
                    row.Add(label);
                    
                    var confLabel = new Label($"{mapping.confidence:P0}");
                    confLabel.style.fontSize = 10;
                    confLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                    row.Add(confLabel);
                    
                    mappingsContent.Add(row);
                }
                
                container.Add(mappingsCard);
            }
        }

        private void BuildBonesTab(VisualElement container)
        {
            if (_revisionBase == null || _revisionBase.basePrefab == null)
            {
                container.Add(CreateNoComparisonMessage());
                return;
            }
            
            // Bone matching settings
            var settingsCard = YUCPUIToolkitHelper.CreateCard("Bone Matching Settings", "Configure how bones are matched between armatures");
            var settingsContent = YUCPUIToolkitHelper.GetCardContent(settingsCard);
            
            if (_serializedBase != null)
            {
                var boneSettingsProp = _serializedBase.FindProperty("boneMatchingSettings");
                if (boneSettingsProp != null)
                {
                    settingsContent.Add(YUCPUIToolkitHelper.CreateField(boneSettingsProp));
                }
            }
            
            var buildMappingButton = YUCPUIToolkitHelper.CreateButton("Build Bone Mapping", () => BuildBoneMapping(), YUCPUIToolkitHelper.ButtonVariant.Primary);
            buildMappingButton.style.height = 32;
            buildMappingButton.style.marginTop = 10;
            settingsContent.Add(buildMappingButton);
            
            container.Add(settingsCard);
            
            // Cached mappings
            if (_revisionBase.bonePathCache.Count > 0)
            {
                var cacheCard = YUCPUIToolkitHelper.CreateCard($"Bone Path Cache ({_revisionBase.bonePathCache.Count})", null);
                var cacheContent = YUCPUIToolkitHelper.GetCardContent(cacheCard);
                
                foreach (var cached in _revisionBase.bonePathCache.Take(20))
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.paddingTop = 3;
                    row.style.paddingBottom = 3;
                    row.style.borderBottomWidth = 1;
                    row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);
                    
                    var label = new Label($"{cached.sourcePath} → {cached.targetPath}");
                    label.style.fontSize = 11;
                    label.style.color = cached.isManualOverride 
                        ? new Color(0.4f, 0.7f, 1f)
                        : new Color(0.7f, 0.7f, 0.7f);
                    row.Add(label);
                    
                    cacheContent.Add(row);
                }
                
                if (_revisionBase.bonePathCache.Count > 20)
                {
                    cacheContent.Add(CreateMoreLabel(_revisionBase.bonePathCache.Count - 20));
                }
                
                container.Add(cacheCard);
            }
        }

        private void BuildSyncTab(VisualElement container)
        {
            // Sync settings
            var settingsCard = YUCPUIToolkitHelper.CreateCard("Sync Settings", "Configure what to synchronize");
            var settingsContent = YUCPUIToolkitHelper.GetCardContent(settingsCard);
            
            if (_serializedBase != null)
            {
                var syncSettingsProp = _serializedBase.FindProperty("syncSettings");
                if (syncSettingsProp != null)
                {
                    settingsContent.Add(YUCPUIToolkitHelper.CreateField(syncSettingsProp));
                }
            }
            else
            {
                settingsContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Assign a Model Revision Base to configure sync settings.",
                    YUCPUIToolkitHelper.MessageType.Info));
            }
            
            container.Add(settingsCard);
            
            // Sync actions
            var actionsCard = YUCPUIToolkitHelper.CreateCard("Sync Actions", "Execute synchronization");
            var actionsContent = YUCPUIToolkitHelper.GetCardContent(actionsCard);
            
            // Full sync button
            var fullSyncButton = YUCPUIToolkitHelper.CreateButton("Sync All (Source → Target)", () => ExecuteFullSync(), YUCPUIToolkitHelper.ButtonVariant.Primary);
            fullSyncButton.style.height = 40;
            actionsContent.Add(fullSyncButton);
            
            // Selective sync buttons
            var selectiveRow = new VisualElement();
            selectiveRow.style.flexDirection = FlexDirection.Row;
            selectiveRow.style.marginTop = 15;
            
            var syncHierarchyBtn = YUCPUIToolkitHelper.CreateButton("Sync Hierarchy", () => SyncHierarchy(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            syncHierarchyBtn.style.flexGrow = 1;
            syncHierarchyBtn.style.marginRight = 5;
            selectiveRow.Add(syncHierarchyBtn);
            
            var syncComponentsBtn = YUCPUIToolkitHelper.CreateButton("Sync Components", () => SyncComponents(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            syncComponentsBtn.style.flexGrow = 1;
            syncComponentsBtn.style.marginLeft = 5;
            selectiveRow.Add(syncComponentsBtn);
            
            actionsContent.Add(selectiveRow);
            
            container.Add(actionsCard);
            
            // Import/Export
            var ioCard = YUCPUIToolkitHelper.CreateCard("Import / Export", "Save or load mapping configurations");
            var ioContent = YUCPUIToolkitHelper.GetCardContent(ioCard);
            
            var ioRow = new VisualElement();
            ioRow.style.flexDirection = FlexDirection.Row;
            
            var exportBtn = YUCPUIToolkitHelper.CreateButton("Export Mappings", () => ExportMappings(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            exportBtn.style.flexGrow = 1;
            exportBtn.style.marginRight = 5;
            ioRow.Add(exportBtn);
            
            var importBtn = YUCPUIToolkitHelper.CreateButton("Import Mappings", () => ImportMappings(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            importBtn.style.flexGrow = 1;
            importBtn.style.marginLeft = 5;
            ioRow.Add(importBtn);
            
            ioContent.Add(ioRow);
            container.Add(ioCard);
        }

        #endregion

        #region Helper Methods

        private VisualElement CreateNoComparisonMessage()
        {
            return YUCPUIToolkitHelper.CreateHelpBox(
                "Run a comparison first from the Setup tab.",
                YUCPUIToolkitHelper.MessageType.Info);
        }

        private VisualElement CreateDiffRow(string path, string buttonText, System.Action onClick, bool isDanger = false)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 5;
            row.style.paddingBottom = 5;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);
            
            var pathLabel = new Label(path);
            pathLabel.style.fontSize = 11;
            pathLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            row.Add(pathLabel);
            
            var button = new Button(onClick) { text = buttonText };
            button.style.height = 22;
            button.style.backgroundColor = isDanger 
                ? new Color(0.6f, 0.2f, 0.2f) 
                : new Color(0.25f, 0.25f, 0.25f);
            button.style.color = Color.white;
            row.Add(button);
            
            return row;
        }

        private Label CreateMoreLabel(int count)
        {
            var label = new Label($"... and {count} more");
            label.style.fontSize = 11;
            label.style.color = new Color(0.5f, 0.5f, 0.5f);
            label.style.unityFontStyleAndWeight = FontStyle.Italic;
            label.style.marginTop = 5;
            return label;
        }

        #endregion

        #region Actions

        private void RefreshComparisonIfNeeded()
        {
            if (_revisionBase != null && _revisionBase.basePrefab != null && 
                _targetVariant != null && _targetVariant.gameObject != null)
            {
                RunComparison();
            }
        }

        private void RunComparison()
        {
            if (_revisionBase == null || _revisionBase.basePrefab == null)
            {
                EditorUtility.DisplayDialog("Missing Source", "Please assign a source prefab first.", "OK");
                return;
            }
            
            if (_targetVariant == null)
            {
                EditorUtility.DisplayDialog("Missing Target", "Please assign a target prefab first.", "OK");
                return;
            }
            
            _comparisonResult = PrefabComparisonUtility.ComparePrefabs(
                _revisionBase.basePrefab,
                _targetVariant.gameObject);
            
            // Also build bone mapping
            _bonePathMapping = BoneMappingUtility.BuildBonePathMapping(
                _revisionBase.basePrefab,
                _targetVariant.gameObject,
                _revisionBase.boneMatchingSettings);
            
            // Cache the mapping
            foreach (var kvp in _bonePathMapping)
            {
                _revisionBase.CacheBonePath(kvp.Key, kvp.Value);
            }
            EditorUtility.SetDirty(_revisionBase);
            
            BuildUI();
            
            Debug.Log($"[ModelRevisionWizard] Comparison complete: {_comparisonResult.TotalDifferences} differences found");
        }

        private void CreateNewBase()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Model Revision Base",
                "ModelRevisionBase",
                "asset",
                "Select a location to save the Model Revision Base");
            
            if (string.IsNullOrEmpty(path)) return;
            
            var newBase = CreateInstance<ModelRevisionBase>();
            AssetDatabase.CreateAsset(newBase, path);
            AssetDatabase.SaveAssets();
            
            SetTargetBase(newBase);
            EditorGUIUtility.PingObject(newBase);
        }

        private void AddToTarget(GameObjectDiff diff)
        {
            if (_targetVariant == null || diff.Object == null) return;
            
            GameObjectAlignmentUtility.CreateObjectAtPath(
                _targetVariant.gameObject, diff.Path, diff.Object);
            
            EditorUtility.SetDirty(_targetVariant.gameObject);
            RunComparison();
        }

        private void RemoveFromTarget(GameObjectDiff diff)
        {
            if (diff.Object == null) return;
            
            if (EditorUtility.DisplayDialog("Remove Object",
                $"Are you sure you want to remove '{diff.Path}' from target?",
                "Remove", "Cancel"))
            {
                Undo.DestroyObjectImmediate(diff.Object);
                RunComparison();
            }
        }

        private void SyncComponent(ComponentDiff diff)
        {
            if (diff.SourceComponent == null || diff.TargetComponent == null) return;
            
            ComponentSyncUtility.SyncProperties(
                diff.SourceComponent,
                diff.TargetComponent,
                _bonePathMapping);
            
            EditorUtility.SetDirty(diff.TargetComponent);
            Debug.Log($"[ModelRevisionWizard] Synced {diff.ComponentType.Name}");
        }

        private void AutoMapBlendshapes()
        {
            if (_revisionBase == null || _comparisonResult == null) return;
            
            var sourceNames = _comparisonResult.Blendshapes
                .Where(b => b.Type == DiffType.Added)
                .Select(b => b.SourceBlendshapeName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .ToList();
            
            var targetNames = BlendshapeMappingUtility.GetAllBlendshapes(_targetVariant.gameObject)
                .Select(b => b.Name)
                .Distinct()
                .ToList();
            
            var mappings = BlendshapeMappingUtility.AutoMapBlendshapes(sourceNames, targetNames);
            
            Undo.RecordObject(_revisionBase, "Auto-Map Blendshapes");
            _revisionBase.blendshapeMappings.Clear();
            _revisionBase.blendshapeMappings.AddRange(mappings);
            EditorUtility.SetDirty(_revisionBase);
            
            RefreshContent();
            Debug.Log($"[ModelRevisionWizard] Auto-mapped {mappings.Count(m => m.status == MappingStatus.Mapped)} blendshapes");
        }

        private void BuildBoneMapping()
        {
            if (_revisionBase == null || _revisionBase.basePrefab == null || _targetVariant == null) return;
            
            _bonePathMapping = BoneMappingUtility.BuildBonePathMapping(
                _revisionBase.basePrefab,
                _targetVariant.gameObject,
                _revisionBase.boneMatchingSettings);
            
            Undo.RecordObject(_revisionBase, "Build Bone Mapping");
            _revisionBase.bonePathCache.Clear();
            foreach (var kvp in _bonePathMapping)
            {
                _revisionBase.CacheBonePath(kvp.Key, kvp.Value);
            }
            EditorUtility.SetDirty(_revisionBase);
            
            RefreshContent();
            Debug.Log($"[ModelRevisionWizard] Built {_bonePathMapping.Count} bone path mappings");
        }

        private void ExecuteFullSync()
        {
            if (_revisionBase == null || _targetVariant == null)
            {
                EditorUtility.DisplayDialog("Missing Data", "Please setup source and target first.", "OK");
                return;
            }
            
            if (!EditorUtility.DisplayDialog("Full Sync",
                "This will sync hierarchy, components, and blendshapes from source to target. Continue?",
                "Sync", "Cancel"))
            {
                return;
            }
            
            SyncHierarchy();
            SyncComponents();
            
            Debug.Log("[ModelRevisionWizard] Full sync complete");
            RunComparison();
        }

        private void SyncHierarchy()
        {
            if (_comparisonResult == null || _targetVariant == null) return;
            
            var settings = _revisionBase?.syncSettings ?? new SyncSettings();
            
            if (settings.addMissingObjects)
            {
                GameObjectAlignmentUtility.AddMissingObjects(
                    _comparisonResult.Hierarchy.AddedInSource,
                    _targetVariant.gameObject);
            }
            
            Debug.Log("[ModelRevisionWizard] Hierarchy synced");
        }

        private void SyncComponents()
        {
            if (_comparisonResult == null) return;
            
            var componentSettings = new ComponentSyncSettings
            {
                SyncPhysBones = _revisionBase?.syncSettings?.syncPhysBones ?? true,
                SyncContacts = _revisionBase?.syncSettings?.syncContacts ?? true,
                SyncVRCFury = _revisionBase?.syncSettings?.syncVRCFury ?? true,
                SyncCustomScripts = _revisionBase?.syncSettings?.syncCustomScripts ?? false
            };
            
            foreach (var match in _comparisonResult.Hierarchy.Matched)
            {
                ComponentSyncUtility.SyncComponents(
                    match.SourceObject,
                    match.TargetObject,
                    componentSettings,
                    _bonePathMapping);
            }
            
            Debug.Log("[ModelRevisionWizard] Components synced");
        }

        private void ExportMappings()
        {
            if (_revisionBase == null)
            {
                EditorUtility.DisplayDialog("No Base", "Please assign a Model Revision Base first.", "OK");
                return;
            }
            
            var path = EditorUtility.SaveFilePanel(
                "Export Mappings",
                "",
                "model_revision_mappings",
                "json");
            
            if (string.IsNullOrEmpty(path)) return;
            
            var export = new MappingExportData
            {
                blendshapeMappings = _revisionBase.blendshapeMappings,
                componentMappings = _revisionBase.componentMappings,
                bonePathCache = _revisionBase.bonePathCache,
                gameObjectMappings = _revisionBase.gameObjectMappings
            };
            
            var json = JsonUtility.ToJson(export, true);
            System.IO.File.WriteAllText(path, json);
            
            Debug.Log($"[ModelRevisionWizard] Exported mappings to {path}");
        }

        private void ImportMappings()
        {
            if (_revisionBase == null)
            {
                EditorUtility.DisplayDialog("No Base", "Please assign a Model Revision Base first.", "OK");
                return;
            }
            
            var path = EditorUtility.OpenFilePanel(
                "Import Mappings",
                "",
                "json");
            
            if (string.IsNullOrEmpty(path)) return;
            
            var json = System.IO.File.ReadAllText(path);
            var import = JsonUtility.FromJson<MappingExportData>(json);
            
            Undo.RecordObject(_revisionBase, "Import Mappings");
            
            if (import.blendshapeMappings != null)
                _revisionBase.blendshapeMappings = import.blendshapeMappings;
            if (import.componentMappings != null)
                _revisionBase.componentMappings = import.componentMappings;
            if (import.bonePathCache != null)
                _revisionBase.bonePathCache = import.bonePathCache;
            if (import.gameObjectMappings != null)
                _revisionBase.gameObjectMappings = import.gameObjectMappings;
            
            EditorUtility.SetDirty(_revisionBase);
            RefreshContent();
            
            Debug.Log($"[ModelRevisionWizard] Imported mappings from {path}");
        }

        #endregion

        private void OnSelectionChange()
        {
            var selectedVariant = Selection.activeGameObject?.GetComponent<ModelRevisionVariant>();
            if (selectedVariant != null && selectedVariant != _targetVariant)
            {
                SetTargetVariant(selectedVariant);
            }
        }

        [System.Serializable]
        private class MappingExportData
        {
            public List<BlendshapeMapping> blendshapeMappings;
            public List<ComponentMapping> componentMappings;
            public List<BonePathCache> bonePathCache;
            public List<GameObjectMapping> gameObjectMappings;
        }
    }
}
