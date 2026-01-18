#if YUCP_KITBASH_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash.UI
{
    /// <summary>
    /// Dockable EditorWindow for the Ownership Map tool.
    /// Provides a guided 3-step workflow: Target → Sources → Paint
    /// </summary>
    public class OwnershipMapWindow : EditorWindow
    {
        // Wizard steps
        private enum WizardStep
        {
            Target = 0,
            Sources = 1,
            Paint = 2
        }
        
        // Current state
        private WizardStep _currentStep = WizardStep.Target;
        private Renderer _targetRenderer;
        private Mesh _targetMesh;
        private bool _isEditableCopy;
        
        // Sources state
        private List<SourceEntry> _sources = new List<SourceEntry>();
        private bool _sourcesAutoDetected;
        
        // Ownership state
        private OwnershipMap _ownershipMap;
        private KitbashRecipe _recipe;
        private Vector2 _scrollPosition;
        private Vector2 _sourcesScrollPosition;
        private Vector2 _ownershipScrollPosition;
        
        // Coverage stats
        private int _assignedCount;
        private int _unknownCount;
        private float _coveragePercent;
        
        // UI state
        private bool _diagnosticsFoldout;
        private List<DiagnosticMessage> _diagnostics = new List<DiagnosticMessage>();
        
        // Source entry for UI
        [Serializable]
        private class SourceEntry
        {
            public string guid;
            public string displayName;
            public string path;
            public bool isSelected = true;
            public bool isAutoDetected;
            public Color color;
        }
        
        private struct DiagnosticMessage
        {
            public enum Level { Info, Warning, Error }
            public Level level;
            public string message;
        }
        
        [MenuItem("Tools/YUCP/Ownership Map...", priority = 200)]
        public static void ShowWindow()
        {
            var window = GetWindow<OwnershipMapWindow>();
            window.titleContent = new GUIContent("Ownership Map", EditorGUIUtility.IconContent("Grid.PaintTool").image);
            window.minSize = new Vector2(350, 500);
            window.Show();
        }
        
        /// <summary>
        /// Opens the window with a specific renderer selected.
        /// </summary>
        public static void OpenWithRenderer(Renderer renderer)
        {
            var window = GetWindow<OwnershipMapWindow>();
            window.titleContent = new GUIContent("Ownership Map", EditorGUIUtility.IconContent("Grid.PaintTool").image);
            window.SetTargetRenderer(renderer);
            window.Show();
        }
        
        private void OnEnable()
        {
            UpdateDiagnostics();
        }
        
        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            // Header with breadcrumb
            DrawBreadcrumb();
            
            EditorGUILayout.Space(8);
            
            // Step-specific content
            switch (_currentStep)
            {
                case WizardStep.Target:
                    DrawTargetStep();
                    break;
                case WizardStep.Sources:
                    DrawSourcesStep();
                    break;
                case WizardStep.Paint:
                    DrawPaintStep();
                    break;
            }
            
            EditorGUILayout.Space(8);
            
            // Coverage bar (always visible when we have a target)
            if (_targetRenderer != null)
            {
                DrawCoverageBar();
            }
            
            EditorGUILayout.Space(4);
            
            // Diagnostics (collapsed by default)
            DrawDiagnostics();
            
            EditorGUILayout.EndScrollView();
        }
        
        #region Breadcrumb
        
        private void DrawBreadcrumb()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            // Target step
            DrawBreadcrumbItem("Target", WizardStep.Target, IsTargetComplete());
            
            GUILayout.Label("→", GUILayout.Width(20));
            
            // Sources step
            GUI.enabled = IsTargetComplete();
            DrawBreadcrumbItem("Sources", WizardStep.Sources, IsSourcesComplete());
            GUI.enabled = true;
            
            GUILayout.Label("→", GUILayout.Width(20));
            
            // Paint step
            GUI.enabled = IsSourcesComplete();
            DrawBreadcrumbItem("Paint", WizardStep.Paint, false);
            GUI.enabled = true;
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawBreadcrumbItem(string label, WizardStep step, bool isComplete)
        {
            var style = _currentStep == step ? EditorStyles.toolbarButton : EditorStyles.label;
            
            string prefix = _currentStep == step ? "● " : (isComplete ? "✓ " : "○ ");
            
            if (GUILayout.Button(prefix + label, style, GUILayout.Width(80)))
            {
                if (CanNavigateToStep(step))
                {
                    _currentStep = step;
                }
            }
        }
        
        private bool CanNavigateToStep(WizardStep step)
        {
            switch (step)
            {
                case WizardStep.Target:
                    return true;
                case WizardStep.Sources:
                    return IsTargetComplete();
                case WizardStep.Paint:
                    return IsSourcesComplete();
                default:
                    return false;
            }
        }
        
        private bool IsTargetComplete() => _targetRenderer != null && _targetMesh != null;
        private bool IsSourcesComplete() => _sources.Any(s => s.isSelected);
        
        #endregion
        
        #region Target Step
        
        private void DrawTargetStep()
        {
            DrawStepHeader("Target", IsTargetComplete() ? "✓ Complete" : "Select a renderer");
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Renderer field
            EditorGUI.BeginChangeCheck();
            var newRenderer = EditorGUILayout.ObjectField("Renderer", _targetRenderer, typeof(Renderer), true) as Renderer;
            if (EditorGUI.EndChangeCheck() && newRenderer != _targetRenderer)
            {
                SetTargetRenderer(newRenderer);
            }
            
            // Show mesh info
            if (_targetRenderer != null)
            {
                EditorGUI.indentLevel++;
                
                if (_targetMesh != null)
                {
                    EditorGUILayout.LabelField("Mesh", _targetMesh.name);
                    int triCount = 0;
                    for (int s = 0; s < _targetMesh.subMeshCount; s++)
                    {
                        triCount += _targetMesh.GetTriangles(s).Length / 3;
                    }
                    EditorGUILayout.LabelField("Triangles", triCount + "");
                    EditorGUILayout.LabelField("Vertices", _targetMesh.vertexCount + "");
                }
                else
                {
                    EditorGUILayout.HelpBox("No mesh found on renderer.", MessageType.Warning);
                }
                
                EditorGUI.indentLevel--;
                
                EditorGUILayout.Space(8);
                
                // Make editable copy button
                if (!_isEditableCopy)
                {
                    if (GUILayout.Button("Make Editable Copy"))
                    {
                        MakeEditableCopy();
                    }
                    EditorGUILayout.HelpBox("An editable copy is recommended be making ownership changes.", MessageType.Info);
                }
                else
                {
                    GUI.enabled = false;
                    GUILayout.Button("✓ Editing Copy");
                    GUI.enabled = true;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Select a Renderer (SkinnedMeshRenderer or MeshRenderer) to begin.", MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
            
            // Next button
            EditorGUILayout.Space(8);
            GUI.enabled = IsTargetComplete();
            if (GUILayout.Button("Next: Sources →", GUILayout.Height(28)))
            {
                _currentStep = WizardStep.Sources;
                if (!_sourcesAutoDetected)
                {
                    AutoDetectSources();
                }
            }
            GUI.enabled = true;
        }
        
        private void SetTargetRenderer(Renderer renderer)
        {
            _targetRenderer = renderer;
            _targetMesh = null;
            _isEditableCopy = false;
            
            if (renderer is SkinnedMeshRenderer smr)
            {
                _targetMesh = smr.sharedMesh;
            }
            else if (renderer is MeshRenderer mr)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    _targetMesh = filter.sharedMesh;
                }
            }
            
            // Check if mesh is already an instance (editable)
            if (_targetMesh != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(_targetMesh);
                _isEditableCopy = string.IsNullOrEmpty(assetPath);
            }
            
            // Reset sources when target changes
            _sources.Clear();
            _sourcesAutoDetected = false;
            
            UpdateDiagnostics();
        }
        
        private void MakeEditableCopy()
        {
            if (_targetMesh == null) return;
            
            var meshCopy = Instantiate(_targetMesh);
            meshCopy.name = _targetMesh.name + " (Editable)";
            
            if (_targetRenderer is SkinnedMeshRenderer smr)
            {
                Undo.RecordObject(smr, "Make Editable Mesh Copy");
                smr.sharedMesh = meshCopy;
            }
            else if (_targetRenderer is MeshRenderer)
            {
                var filter = _targetRenderer.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    Undo.RecordObject(filter, "Make Editable Mesh Copy");
                    filter.sharedMesh = meshCopy;
                }
            }
            
            _targetMesh = meshCopy;
            _isEditableCopy = true;
            UpdateDiagnostics();
        }
        
        #endregion
        
        #region Sources Step
        
        private void DrawSourcesStep()
        {
            int selectedCount = _sources.Count(s => s.isSelected);
            DrawStepHeader("Sources", $"{selectedCount} selected");
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Auto-detect button
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto-detect Sources", GUILayout.Width(140)))
            {
                AutoDetectSources();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(4);
            
            // Sources list
            _sourcesScrollPosition = EditorGUILayout.BeginScrollView(_sourcesScrollPosition, GUILayout.MaxHeight(200));
            
            if (_sources.Count == 0)
            {
                EditorGUILayout.HelpBox("No sources detected. Add sources manually or try auto-detect.", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < _sources.Count; i++)
                {
                    DrawSourceEntry(_sources[i], i);
                }
            }
            
            EditorGUILayout.EndScrollView();
            
            // Add source button
            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ Add Source..."))
            {
                ShowAddSourceMenu();
            }
            
            EditorGUILayout.EndVertical();
            
            // Navigation
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("← Back", GUILayout.Width(80)))
            {
                _currentStep = WizardStep.Target;
            }
            
            GUILayout.FlexibleSpace();
            
            GUI.enabled = IsSourcesComplete();
            if (GUILayout.Button("Next: Paint →", GUILayout.Width(100), GUILayout.Height(28)))
            {
                _currentStep = WizardStep.Paint;
                EnsureOwnershipMap();
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawSourceEntry(SourceEntry source, int index)
        {
            EditorGUILayout.BeginHorizontal();
            
            // Checkbox
            source.isSelected = EditorGUILayout.Toggle(source.isSelected, GUILayout.Width(20));
            
            // Color indicator
            var colorRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
            EditorGUI.DrawRect(colorRect, source.color);
            
            // Name
            string suffix = source.isAutoDetected ? " (auto)" : "";
            EditorGUILayout.LabelField(source.displayName + suffix);
            
            // Remove button
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                _sources.RemoveAt(index);
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void AutoDetectSources()
        {
            _sources.Clear();
            _sourcesAutoDetected = true;
            
            if (_targetRenderer == null) return;
            
            // Try to detect sources from materials
            var materials = _targetRenderer.sharedMaterials;
            int colorIndex = 0;
            
            foreach (var mat in materials)
            {
                if (mat == null) continue;
                
                // Check if material references a known source
                string matPath = AssetDatabase.GetAssetPath(mat);
                if (!string.IsNullOrEmpty(matPath))
                {
                    // Try to find FBX in same folder
                    string folder = System.IO.Path.GetDirectoryName(matPath);
                    var fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { folder });
                    
                    foreach (var guid in fbxGuids.Take(3)) // Limit to 3 per material
                    {
                        string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (!_sources.Any(s => s.guid == guid))
                        {
                            _sources.Add(new SourceEntry
                            {
                                guid = guid,
                                displayName = System.IO.Path.GetFileNameWithoutExtension(fbxPath),
                                path = fbxPath,
                                isAutoDetected = true,
                                color = KitbashSourceLibrary.GetDefaultColor(colorIndex++)
                            });
                        }
                    }
                }
            }
            
            // If no sources found from materials, check parent object for FBX references
            if (_sources.Count == 0)
            {
                // Fallback: just add a placeholder prompt
                AddDiagnostic(DiagnosticMessage.Level.Info, "No sources auto-detected. Add sources manually.");
            }
            
            UpdateDiagnostics();
        }
        
        private void ShowAddSourceMenu()
        {
            // Open object picker for FBX files
            EditorGUIUtility.ShowObjectPicker<GameObject>(null, false, "t:Model", 0);
        }
        
        private void OnInspectorUpdate()
        {
            // Handle object picker result
            if (Event.current?.commandName == "ObjectSelectorClosed")
            {
                var picked = EditorGUIUtility.GetObjectPickerObject();
                if (picked != null)
                {
                    string path = AssetDatabase.GetAssetPath(picked);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    
                    if (!string.IsNullOrEmpty(guid) && !_sources.Any(s => s.guid == guid))
                    {
                        _sources.Add(new SourceEntry
                        {
                            guid = guid,
                            displayName = picked.name,
                            path = path,
                            isAutoDetected = false,
                            color = KitbashSourceLibrary.GetDefaultColor(_sources.Count)
                        });
                        Repaint();
                    }
                }
            }
        }
        
        #endregion
        
        #region Paint Step
        
        private void DrawPaintStep()
        {
            DrawStepHeader("Paint", "Assign ownership");
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Ownership list (grouped)
            EditorGUILayout.LabelField("Ownership List", EditorStyles.boldLabel);
            
            _ownershipScrollPosition = EditorGUILayout.BeginScrollView(_ownershipScrollPosition, GUILayout.MaxHeight(250));
            
            var selectedSources = _sources.Where(s => s.isSelected).ToList();
            
            foreach (var source in selectedSources)
            {
                DrawOwnershipRow(source);
            }
            
            // Unknown row
            EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16)), Color.gray);
            EditorGUILayout.LabelField($"Unknown ({_unknownCount} triangles)");
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.Space(8);
            
            // Paint tool button
            if (GUILayout.Button("Open Paint Tool (Scene View)", GUILayout.Height(32)))
            {
                ActivatePaintTool();
            }
            
            EditorGUILayout.Space(4);
            
            // Auto-seed button
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto-seed Ownership"))
            {
                AutoSeedOwnership();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            
            // Navigation
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("← Back", GUILayout.Width(80)))
            {
                _currentStep = WizardStep.Sources;
            }
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Save Recipe", GUILayout.Width(100), GUILayout.Height(28)))
            {
                SaveRecipe();
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawOwnershipRow(SourceEntry source)
        {
            EditorGUILayout.BeginHorizontal();
            
            // Color indicator
            var colorRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
            EditorGUI.DrawRect(colorRect, source.color);
            
            // Name and percentage
            // TODO: Calculate actual percentages from ownership map
            EditorGUILayout.LabelField($"{source.displayName} (0%)");
            
            // Select button
            if (GUILayout.Button("Select", GUILayout.Width(50)))
            {
                SelectSourceForPainting(source);
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void SelectSourceForPainting(SourceEntry source)
        {
            // TODO: Communicate with paint tool to select this source
            Debug.Log($"[OwnershipMapWindow] Selected source for painting: {source.displayName}");
        }
        
        private void ActivatePaintTool()
        {
            // TODO: Activate the OwnershipPaintTool
            Debug.Log("[OwnershipMapWindow] Activating paint tool...");
            
            // Focus scene view
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.Focus();
            }
        }
        
        private void AutoSeedOwnership()
        {
            // TODO: Implement auto-seeding with confidence preview
            Debug.Log("[OwnershipMapWindow] Auto-seeding ownership...");
            EditorUtility.DisplayDialog("Auto-seed", "Auto-seed feature coming soon!\n\nThis will analyze mesh topology and suggest ownership assignments.", "OK");
        }
        
        private void EnsureOwnershipMap()
        {
            if (_ownershipMap != null) return;
            
            // Create a new ownership map instance
            _ownershipMap = CreateInstance<OwnershipMap>();
            _ownershipMap.targetMeshGuid = _targetMesh != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_targetMesh)) : "";
            if (_targetMesh != null)
            {
                int triCount = 0;
                for (int s = 0; s < _targetMesh.subMeshCount; s++)
                {
                    triCount += _targetMesh.GetTriangles(s).Length / 3;
                }
                _ownershipMap.expectedTriangleCount = triCount;
            }
            else
            {
                _ownershipMap.expectedTriangleCount = 0;
            }
            
            UpdateCoverageStats();
        }
        
        private void UpdateCoverageStats()
        {
            if (_ownershipMap == null || _targetMesh == null)
            {
                _assignedCount = 0;
                _unknownCount = 0;
                _coveragePercent = 0;
                return;
            }
            
            var stats = _ownershipMap.GetCoverageStats();
            _assignedCount = stats.assigned;
            _unknownCount = stats.unknown;
            _coveragePercent = stats.percentage;
        }
        
        private void SaveRecipe()
        {
            if (_ownershipMap == null)
            {
                EditorUtility.DisplayDialog("No Data", "No ownership data to save.", "OK");
                return;
            }
            
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Kitbash Recipe",
                "KitbashRecipe",
                "asset",
                "Save the kitbash recipe"
            );
            
            if (string.IsNullOrEmpty(path)) return;
            
            // Create recipe
            _recipe = CreateInstance<KitbashRecipe>();
            _recipe.targetDerivedFbxGuid = _targetMesh != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_targetMesh)) : "";
            
            // Add parts from selected sources
            foreach (var source in _sources.Where(s => s.isSelected))
            {
                _recipe.parts.Add(new KitbashSourcePart
                {
                    sourceFbxGuid = source.guid,
                    displayName = source.displayName
                });
            }
            
            _recipe.ComputeHash();
            
            // Save ownership map alongside recipe
            string ownershipPath = path.Replace(".asset", "_OwnershipMap.asset");
            AssetDatabase.CreateAsset(_ownershipMap, ownershipPath);
            _recipe.ownershipMapGuid = AssetDatabase.AssetPathToGUID(ownershipPath);
            
            // Save recipe
            AssetDatabase.CreateAsset(_recipe, path);
            AssetDatabase.SaveAssets();
            
            // Select the created assets
            Selection.activeObject = _recipe;
            EditorUtility.DisplayDialog("Saved", $"Recipe saved to:\n{path}", "OK");
        }
        
        #endregion
        
        #region Coverage & Diagnostics
        
        private void DrawCoverageBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Coverage", EditorStyles.boldLabel);
            
            // Progress bar
            var rect = GUILayoutUtility.GetRect(100, 20);
            EditorGUI.ProgressBar(rect, _coveragePercent / 100f, $"{_coveragePercent:F1}%  Unknown: {_unknownCount}");
            
            EditorGUILayout.Space(4);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Jump to Unknown"))
            {
                JumpToUnknown();
            }
            if (GUILayout.Button("Highlight Unknown"))
            {
                HighlightUnknown();
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }
        
        private void JumpToUnknown()
        {
            // TODO: Navigate scene view to first unknown region
            Debug.Log("[OwnershipMapWindow] Jumping to unknown region...");
        }
        
        private void HighlightUnknown()
        {
            // TODO: Toggle unknown highlight in scene view
            Debug.Log("[OwnershipMapWindow] Highlighting unknown regions...");
        }
        
        private void DrawDiagnostics()
        {
            if (_diagnostics.Count == 0) return;
            
            _diagnosticsFoldout = EditorGUILayout.Foldout(_diagnosticsFoldout, $"Diagnostics ({_diagnostics.Count})", true);
            
            if (_diagnosticsFoldout)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                foreach (var diag in _diagnostics)
                {
                    MessageType msgType = diag.level switch
                    {
                        DiagnosticMessage.Level.Warning => MessageType.Warning,
                        DiagnosticMessage.Level.Error => MessageType.Error,
                        _ => MessageType.Info
                    };
                    
                    string icon = diag.level switch
                    {
                        DiagnosticMessage.Level.Warning => "⚠",
                        DiagnosticMessage.Level.Error => "✕",
                        _ => "ℹ"
                    };
                    
                    EditorGUILayout.LabelField($"{icon} {diag.message}", EditorStyles.wordWrappedLabel);
                }
                
                EditorGUILayout.EndVertical();
            }
        }
        
        private void UpdateDiagnostics()
        {
            _diagnostics.Clear();
            
            if (_targetMesh != null)
            {
                // Check if mesh has UV4 for ownership data
                if (_targetMesh.uv4 == null || _targetMesh.uv4.Length == 0)
                {
                    AddDiagnostic(DiagnosticMessage.Level.Info, "UV4 will be created for ownership data");
                }
                
                if (_isEditableCopy)
                {
                    AddDiagnostic(DiagnosticMessage.Level.Info, "Editing copy");
                }
            }
            
            if (_unknownCount > 0 && _targetMesh != null)
            {
                float unknownPercent = 100f - _coveragePercent;
                AddDiagnostic(DiagnosticMessage.Level.Warning, $"Unknown {unknownPercent:F0}% (OK, exporter will fallback)");
            }
        }
        
        private void AddDiagnostic(DiagnosticMessage.Level level, string message)
        {
            _diagnostics.Add(new DiagnosticMessage { level = level, message = message });
        }
        
        #endregion
        
        #region Helpers
        
        private void DrawStepHeader(string title, string status)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
        
        #endregion
    }
}
#endif
