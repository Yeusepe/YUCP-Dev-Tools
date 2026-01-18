#if YUCP_KITBASH_ENABLED
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using YUCP.DevTools.Editor.PackageExporter.Kitbash.GPU;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash.UI
{
    /// <summary>
    /// Custom PreviewSceneStage for Kitbash editing.
    /// Provides full editor takeover like Humanoid Configure.
    /// </summary>
    [Serializable]
    public class KitbashStage : PreviewSceneStage
    {
        // Static reference for accessing from inspector
        private static KitbashStage _current;
        public static KitbashStage Current => _current;
        
        // Target data
        [SerializeField] private string _assetPath;
        [SerializeField] private string _settingsJson;
        
        private ModelImporter _importer;
        private DerivedSettings _settings;
        private GameObject _previewModel;
        private Mesh _targetMesh;
        private int[] _allTriangles;
        private int[] _submeshTriangleOffsets;
        private int _triangleCount;
        
        // Layers (ownership regions)
        private List<OwnershipLayer> _layers = new List<OwnershipLayer>();
        private int _selectedLayerIndex = -1;
        
        // Mesh data
        private int[] _triangleOwnership;
        private Mesh _visualizationMesh;
        private Material _ownershipMaterial;
        private Material[] _originalMaterials; // Cache original materials before visualization
        
        // Paint tool mode
        public enum PaintToolMode { Brush, Bucket }
        public enum FillMode { Connected, UVIsland, Submesh }
        
        private PaintToolMode _paintTool = PaintToolMode.Brush;
        private FillMode _fillMode = FillMode.Connected;
        
        // Painting state
        private bool _paintMode = true;
        private float _brushSize = 0.05f;
        // private float _brushOpacity = 1.0f;
        // private float _brushHardness = 1.0f;
        private bool _needsVisualizationUpdate = false;
        private HashSet<int> _dirtyTriangles = new HashSet<int>();
        private double _lastVisualizationTime = 0;
        private const double LIVE_UPDATE_INTERVAL = 0.05; // 20fps live update
        
        // Paint options
        private bool _mirrorX = false;
        private bool _mirrorY = false;
        private bool _mirrorZ = false;
        private Vector3 _mirrorCenter = Vector3.zero;
        
        // Brush preview state
        private Vector3 _brushHitPos;
        private Vector3 _brushHitNormal;
        private bool _brushHitValid = false;
        
        // Spatial acceleration for painting
        private Vector3[] _triangleCentersWorld;  // Precomputed world-space triangle centers
        private Dictionary<int, List<int>> _spatialGrid;  // Grid cell -> triangle indices
        private float _gridCellSize = 0.1f;
        private bool _spatialCacheValid = false;
        
        // Undo/Redo state
        private Stack<int[]> _undoStack = new Stack<int[]>();
        private Stack<int[]> _redoStack = new Stack<int[]>();
        private bool _isPaintingStroke = false;
        
        public override string assetPath => _assetPath;
        
        [Serializable]
        public class OwnershipLayer
        {
            public string name;
            public string sourceGuid;
            public string sourceName;
            public Color color;
            public bool visible = true;
            public int triangleCount;
        }
        
        // Public API
        public List<OwnershipLayer> Layers => _layers;
        public int SelectedLayerIndex { get => _selectedLayerIndex; set => _selectedLayerIndex = value; }
        public int TotalTriangles => _triangleOwnership?.Length ?? 0;
        public ModelImporter Importer => _importer;
        public DerivedSettings Settings => _settings;
        public GameObject PreviewModel => _previewModel;
        public bool PaintMode { get => _paintMode; set => _paintMode = value; }
        public float BrushSize { get => _brushSize; set => _brushSize = value; }
        public bool MirrorX { get => _mirrorX; set => _mirrorX = value; }
        public bool MirrorY { get => _mirrorY; set => _mirrorY = value; }
        public bool MirrorZ { get => _mirrorZ; set => _mirrorZ = value; }
        
        /// <summary>
        /// Creates and enters a Kitbash stage for the specified FBX.
        /// </summary>
        public static void Enter(ModelImporter importer, DerivedSettings settings)
        {
            var stage = CreateInstance<KitbashStage>();
            stage._assetPath = importer.assetPath;
            stage._settingsJson = JsonUtility.ToJson(settings);
            stage._importer = importer;
            stage._settings = settings;
            
            // Enter the stage
            StageUtility.GoToStage(stage, true);
        }
        
        /// <summary>
        /// Exits the stage back to main.
        /// </summary>
        public static void Exit(bool apply)
        {
            if (_current != null && apply)
            {
                _current.SaveOwnershipData();
            }
            
            StageUtility.GoToMainStage();
        }
        
        protected override bool OnOpenStage()
        {
            if (!base.OnOpenStage())
                return false;
            
            _current = this;
            
            // Restore settings if coming from serialization
            if (_importer == null && !string.IsNullOrEmpty(_assetPath))
            {
                _importer = AssetImporter.GetAtPath(_assetPath) as ModelImporter;
            }
            if (_settings == null && !string.IsNullOrEmpty(_settingsJson))
            {
                _settings = JsonUtility.FromJson<DerivedSettings>(_settingsJson);
            }
            
            // Load the preview model
            if (!LoadPreviewModel())
                return false;
            
            // Initialize layers and ownership
            InitializeLayers();
            InitializeOwnershipData();
            BuildSpatialCache();
            CreateVisualizationMaterial();
            UpdateVisualization();
            
            // Hook into SceneView for painting
            SceneView.duringSceneGui += OnSceneGUI;
            
            // Open the Layers panel
            KitbashWindow.OpenForStage();
            
            return true;
        }
        
        protected override void OnCloseStage()
        {
            _current = null;
            
            // Unhook from SceneView
            SceneView.duringSceneGui -= OnSceneGUI;
            
            // Cleanup
            if (_ownershipMaterial != null)
            {
                DestroyImmediate(_ownershipMaterial);
            }
            if (_visualizationMesh != null)
            {
                DestroyImmediate(_visualizationMesh);
            }
            
            // Cleanup GPU resources
            KitbashRaycast.Cleanup();
            
            // Close the Layers panel
            KitbashWindow.CloseForStage();
            
            base.OnCloseStage();
        }
        
        protected override void OnFirstTimeOpenStageInSceneView(SceneView sceneView)
        {
            // Frame the model
            if (_previewModel != null)
            {
                Selection.activeGameObject = _previewModel;
                sceneView.FrameSelected(false, true);
            }
            
            // Configure scene view for painting
            sceneView.sceneViewState.showFlares = false;
            sceneView.sceneViewState.showFog = false;
            sceneView.sceneViewState.showSkybox = false;
            sceneView.sceneViewState.showImageEffects = false;
            sceneView.sceneViewState.showParticleSystems = false;
            sceneView.sceneLighting = true;
        }
        
        protected override GUIContent CreateHeaderContent()
        {
            string fileName = Path.GetFileNameWithoutExtension(_assetPath);
            return new GUIContent($"Kitbash: {fileName}", EditorGUIUtility.IconContent("Mesh Icon").image);
        }
        
        private bool LoadPreviewModel()
        {
            if (string.IsNullOrEmpty(_assetPath))
            {
                Debug.LogError("[KitbashStage] No asset path specified");
                return false;
            }
            
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_assetPath);
            if (prefab == null)
            {
                Debug.LogError($"[KitbashStage] Could not load: {_assetPath}");
                return false;
            }
            
            _previewModel = Instantiate(prefab);
            _previewModel.name = "PreviewModel";
            SceneManager.MoveGameObjectToScene(_previewModel, scene);
            
            // Get mesh - support both SkinnedMeshRenderer and MeshFilter
            Mesh originalMesh = null;
            var smr = _previewModel.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                originalMesh = smr.sharedMesh;
            }
            else
            {
                var mf = _previewModel.GetComponentInChildren<MeshFilter>();
                if (mf != null) originalMesh = mf.sharedMesh;
            }
            
            // Create a copy of the mesh for painting - never modify imported asset
            if (originalMesh != null)
            {
                _targetMesh = Instantiate(originalMesh);
                _targetMesh.name = originalMesh.name + "_Preview";
                _targetMesh.hideFlags = HideFlags.DontSave;
            }
            
            // Cache original materials BEFORE we replace them with visualization material
            var renderer = _previewModel.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                _originalMaterials = renderer.sharedMaterials;
            }
            
            // Add lighting
            var lightGO = new GameObject("DirectionalLight");
            SceneManager.MoveGameObjectToScene(lightGO, scene);
            lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            
            return true;
        }
        
        private void InitializeLayers()
        {
            _layers.Clear();
            
            // Unknown layer (always first)
            _layers.Add(new OwnershipLayer
            {
                name = "Unknown",
                sourceGuid = null,
                sourceName = "(Unassigned)",
                color = new Color(0.5f, 0.5f, 0.5f, 1f),
                visible = true
            });
            
            // Load from existing recipe if available
            if (!string.IsNullOrEmpty(_settings?.kitbashRecipeGuid))
            {
                string recipePath = AssetDatabase.GUIDToAssetPath(_settings.kitbashRecipeGuid);
                if (!string.IsNullOrEmpty(recipePath))
                {
                    var recipe = AssetDatabase.LoadAssetAtPath<KitbashRecipe>(recipePath);
                    if (recipe?.parts != null)
                    {
                        int colorIdx = 0;
                        foreach (var part in recipe.parts)
                        {
                            string sourcePath = AssetDatabase.GUIDToAssetPath(part.sourceFbxGuid);
                            _layers.Add(new OwnershipLayer
                            {
                                name = !string.IsNullOrEmpty(part.displayName) ? part.displayName : $"Source {_layers.Count}",
                                sourceGuid = part.sourceFbxGuid,
                                sourceName = Path.GetFileNameWithoutExtension(sourcePath),
                                color = KitbashSourceLibrary.GetDefaultColor(colorIdx++),
                                visible = true
                            });
                        }
                    }
                }
            }
            
            // Auto-detect if no layers loaded
            if (_layers.Count == 1)
            {
                AutoDetectLayers();
            }
            
            _selectedLayerIndex = _layers.Count > 1 ? 1 : 0;
        }
        
        private void AutoDetectLayers()
        {
            string folder = Path.GetDirectoryName(_assetPath);
            var fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { folder });
            
            int colorIdx = 0;
            foreach (var guid in fbxGuids.Take(6))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == _assetPath) continue;
                
                _layers.Add(new OwnershipLayer
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    sourceGuid = guid,
                    sourceName = Path.GetFileNameWithoutExtension(path),
                    color = KitbashSourceLibrary.GetDefaultColor(colorIdx++),
                    visible = true
                });
            }
        }
        
        private void BuildTriangleCache()
        {
            if (_targetMesh == null) return;
            
            var all = new List<int>();
            int submeshCount = _targetMesh.subMeshCount;
            _submeshTriangleOffsets = new int[submeshCount];
            int triangleOffset = 0;
            
            for (int s = 0; s < submeshCount; s++)
            {
                _submeshTriangleOffsets[s] = triangleOffset;
                var subTriangles = _targetMesh.GetTriangles(s);
                all.AddRange(subTriangles);
                triangleOffset += subTriangles.Length / 3;
            }
            
            _allTriangles = all.ToArray();
            _triangleCount = _allTriangles.Length / 3;
        }

        private int[] GetAllTriangles()
        {
            if (_allTriangles == null || _allTriangles.Length == 0)
            {
                BuildTriangleCache();
            }
            return _allTriangles;
        }
        
        private void InitializeOwnershipData()
        {
            if (_targetMesh == null) return;
            
            BuildTriangleCache();
            int triCount = _triangleCount;
            _triangleOwnership = new int[triCount];
            
            // Default: all triangles start as Unknown (layer 0)
            for (int i = 0; i < triCount; i++)
            {
                _triangleOwnership[i] = 0;
            }
            
            // Try to load from existing ownership map
            if (!string.IsNullOrEmpty(_settings?.ownershipMapGuid))
            {
                string mapPath = AssetDatabase.GUIDToAssetPath(_settings.ownershipMapGuid);
                if (!string.IsNullOrEmpty(mapPath))
                {
                    var map = AssetDatabase.LoadAssetAtPath<OwnershipMap>(mapPath);
                    if (map != null)
                    {
                        bool legacyLayerIndexing = map.regions.Any(r => r.sourcePartIndex >= _layers.Count - 1) ||
                                                  map.regions.All(r => r.sourcePartIndex == 0);
                        
                        if (map.expectedTriangleCount == triCount)
                        {
                            // Rebuild ownership from regions
                            foreach (var region in map.regions)
                            {
                                if (region.triangleIndices == null) continue;
                                foreach (int triIdx in region.triangleIndices)
                                {
                                    if (triIdx >= 0 && triIdx < triCount)
                                    {
                                        if (legacyLayerIndexing)
                                        {
                                            _triangleOwnership[triIdx] = Mathf.Clamp(region.sourcePartIndex, 0, _layers.Count - 1);
                                        }
                                        else
                                        {
                                            _triangleOwnership[triIdx] = region.sourcePartIndex < 0 ? 0 : region.sourcePartIndex + 1;
                                        }
                                    }
                                }
                            }
                            Debug.Log($"[KitbashStage] Loaded ownership for {triCount} triangles from {mapPath}");
                        }
                        else
                        {
                            // Triangle count mismatch - use reprojection
                            Debug.LogWarning($"[KitbashStage] Triangle count mismatch: map has {map.expectedTriangleCount}, mesh has {triCount}. Attempting reprojection...");
                            
                            // Compute local-space triangle centers and normals for the new mesh
                            var triangles = GetAllTriangles();
                            var vertices = _targetMesh.vertices;
                            var meshNormals = _targetMesh.normals;
                            
                            var centers = new Vector3[triCount];
                            var normals = new Vector3[triCount];
                            
                            for (int i = 0; i < triCount; i++)
                            {
                                int baseIdx = i * 3;
                                Vector3 v0 = vertices[triangles[baseIdx]];
                                Vector3 v1 = vertices[triangles[baseIdx + 1]];
                                Vector3 v2 = vertices[triangles[baseIdx + 2]];
                                
                                centers[i] = (v0 + v1 + v2) / 3f;
                                
                                // Use vertex normals if available, else compute from face
                                if (meshNormals != null && meshNormals.Length > triangles[baseIdx])
                                {
                                    normals[i] = (meshNormals[triangles[baseIdx]] + 
                                                  meshNormals[triangles[baseIdx + 1]] + 
                                                  meshNormals[triangles[baseIdx + 2]]).normalized;
                                }
                                else
                                {
                                    normals[i] = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                                }
                            }
                            
                            // Reproject using sample points
                            _triangleOwnership = map.ReprojectToMeshFast(centers, normals);
                            if (!legacyLayerIndexing)
                            {
                                for (int i = 0; i < _triangleOwnership.Length; i++)
                                {
                                    _triangleOwnership[i] = _triangleOwnership[i] < 0 ? 0 : _triangleOwnership[i] + 1;
                                }
                            }
                            
                            // Count reprojected
                            int reprojected = _triangleOwnership.Count(o => o > 0);
                            Debug.Log($"[KitbashStage] Reprojection complete: {reprojected}/{triCount} triangles reassigned from {map.expectedTriangleCount} original triangles");
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Precomputes triangle centers and builds a spatial grid for fast radius queries.
        /// Called once when mesh is loaded, invalidated if mesh transform changes.
        /// </summary>
        private void BuildSpatialCache()
        {
            if (_targetMesh == null || _previewModel == null) return;
            
            var meshTransform = _previewModel.GetComponentInChildren<Renderer>()?.transform;
            if (meshTransform == null) return;
            
            var triangles = GetAllTriangles();
            if (triangles == null || triangles.Length == 0) return;
            var vertices = _targetMesh.vertices;
            int triCount = _triangleCount;
            
            // Compute world-space triangle centers
            _triangleCentersWorld = new Vector3[triCount];
            for (int i = 0; i < triCount; i++)
            {
                int baseIdx = i * 3;
                Vector3 v0 = meshTransform.TransformPoint(vertices[triangles[baseIdx]]);
                Vector3 v1 = meshTransform.TransformPoint(vertices[triangles[baseIdx + 1]]);
                Vector3 v2 = meshTransform.TransformPoint(vertices[triangles[baseIdx + 2]]);
                _triangleCentersWorld[i] = (v0 + v1 + v2) / 3f;
            }
            
            // Build spatial grid - cell size should be ~2x typical brush size
            _gridCellSize = Mathf.Max(0.05f, _targetMesh.bounds.extents.magnitude / 20f);
            _spatialGrid = new Dictionary<int, List<int>>();
            
            for (int i = 0; i < triCount; i++)
            {
                int cellKey = GetCellKey(_triangleCentersWorld[i]);
                if (!_spatialGrid.TryGetValue(cellKey, out var list))
                {
                    list = new List<int>();
                    _spatialGrid[cellKey] = list;
                }
                list.Add(i);
            }
            
            _spatialCacheValid = true;
            Debug.Log($"[KitbashStage] Built spatial cache: {triCount} triangles, {_spatialGrid.Count} cells, cell size {_gridCellSize:F3}");
        }
        
        private int GetCellKey(Vector3 pos)
        {
            int x = Mathf.FloorToInt(pos.x / _gridCellSize);
            int y = Mathf.FloorToInt(pos.y / _gridCellSize);
            int z = Mathf.FloorToInt(pos.z / _gridCellSize);
            // Pack into single int (works well for typical avatar-sized meshes)
            return (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
        }
        
        private IEnumerable<int> GetTrianglesInRadius(Vector3 center, float radius)
        {
            if (!_spatialCacheValid || _triangleCentersWorld == null)
                yield break;
            
            float radiusSq = radius * radius;
            
            // Check cells that could contain triangles within radius
            int cellRadius = Mathf.CeilToInt(radius / _gridCellSize) + 1;
            int cx = Mathf.FloorToInt(center.x / _gridCellSize);
            int cy = Mathf.FloorToInt(center.y / _gridCellSize);
            int cz = Mathf.FloorToInt(center.z / _gridCellSize);
            
            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                for (int dy = -cellRadius; dy <= cellRadius; dy++)
                {
                    for (int dz = -cellRadius; dz <= cellRadius; dz++)
                    {
                        int cellKey = ((cx + dx) * 73856093) ^ ((cy + dy) * 19349663) ^ ((cz + dz) * 83492791);
                        if (_spatialGrid.TryGetValue(cellKey, out var triList))
                        {
                            foreach (int triIdx in triList)
                            {
                                if ((_triangleCentersWorld[triIdx] - center).sqrMagnitude <= radiusSq)
                                {
                                    yield return triIdx;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        private void CreateVisualizationMaterial()
        {
            // Try custom YUCP vertex color shader first (most reliable)
            var shader = Shader.Find("YUCP/VertexColor");
            
            // Fallback to built-in vertex color shaders
            if (shader == null)
            {
                shader = Shader.Find("Unlit/VertexColor");
            }
            
            if (shader == null)
            {
                shader = Shader.Find("Hidden/Internal-Colored");
            }
            
            if (shader == null)
            {
                shader = Shader.Find("Particles/Standard Unlit");
            }
            
            // Ultimate fallback
            if (shader == null)
            {
                shader = Shader.Find("Standard");
                Debug.LogWarning("[KitbashStage] Could not find vertex color shader, falling back to Standard");
            }
            
            _ownershipMaterial = new Material(shader);
            _ownershipMaterial.hideFlags = HideFlags.DontSave;
            
            // Configure material to use vertex colors (for fallback shaders)
            if (_ownershipMaterial.HasProperty("_Color"))
            {
                _ownershipMaterial.SetColor("_Color", Color.white);
            }
            if (_ownershipMaterial.HasProperty("_MainTex"))
            {
                _ownershipMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
            }
            if (_ownershipMaterial.HasProperty("_ColorMode"))
            {
                _ownershipMaterial.SetFloat("_ColorMode", 2f); // Multiply mode
            }
        }
        
        public void UpdateVisualization()
        {
            if (_targetMesh == null || _triangleOwnership == null) return;
            
            // Create mesh copy with vertex colors
            if (_visualizationMesh == null)
            {
                _visualizationMesh = Instantiate(_targetMesh);
                _visualizationMesh.hideFlags = HideFlags.DontSave;
            }
            
            var triangles = GetAllTriangles();
            var vertexColors = new Color[_targetMesh.vertexCount];
            
            // Initialize gray
            for (int i = 0; i < vertexColors.Length; i++)
            {
                vertexColors[i] = new Color(0.5f, 0.5f, 0.5f);
            }
            
            // Apply layer colors
            for (int tri = 0; tri < _triangleOwnership.Length; tri++)
            {
                int layerIdx = _triangleOwnership[tri];
                if (layerIdx < 0 || layerIdx >= _layers.Count) continue;
                
                var layer = _layers[layerIdx];
                if (!layer.visible) continue;
                
                int baseIdx = tri * 3;
                if (baseIdx + 2 < triangles.Length)
                {
                    vertexColors[triangles[baseIdx]] = layer.color;
                    vertexColors[triangles[baseIdx + 1]] = layer.color;
                    vertexColors[triangles[baseIdx + 2]] = layer.color;
                }
            }
            
            _visualizationMesh.colors = vertexColors;
            
            // Apply to renderers - need to set ALL material slots for multi-submesh meshes
            var smr = _previewModel?.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                smr.sharedMesh = _visualizationMesh;
                // Create array of materials matching submesh count
                int matCount = Mathf.Max(1, _visualizationMesh.subMeshCount);
                var mats = new Material[matCount];
                for (int i = 0; i < matCount; i++)
                    mats[i] = _ownershipMaterial;
                smr.sharedMaterials = mats;
            }
            else
            {
                var mf = _previewModel?.GetComponentInChildren<MeshFilter>();
                var mr = _previewModel?.GetComponentInChildren<MeshRenderer>();
                if (mf != null && mr != null)
                {
                    mf.sharedMesh = _visualizationMesh;
                    // Create array of materials matching submesh count
                    int matCount = Mathf.Max(1, _visualizationMesh.subMeshCount);
                    var mats = new Material[matCount];
                    for (int i = 0; i < matCount; i++)
                        mats[i] = _ownershipMaterial;
                    mr.sharedMaterials = mats;
                }
            }
            
            // Update layer counts
            foreach (var layer in _layers)
                layer.triangleCount = 0;
            foreach (var ownerIdx in _triangleOwnership)
            {
                if (ownerIdx >= 0 && ownerIdx < _layers.Count)
                    _layers[ownerIdx].triangleCount++;
            }
            
            SceneView.RepaintAll();
        }
        
        private void OnSceneGUI(SceneView sceneView)
        {
            // Always draw toolbar when stage is active
            Handles.BeginGUI();
            DrawToolbar(sceneView);
            Handles.EndGUI();
            
            // Paint mode handling
            if (!_paintMode) return;
            
            Event e = Event.current;
            
            // Hotkeys
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.B:
                        _paintTool = PaintToolMode.Brush;
                        e.Use();
                        break;
                    case KeyCode.G:
                        _paintTool = PaintToolMode.Bucket;
                        e.Use();
                        break;
                    case KeyCode.Z:
                        if (e.control || e.command)
                        {
                            if (e.shift)
                                Redo();
                            else
                                Undo();
                            e.Use();
                        }
                        break;
                    case KeyCode.Y:
                        if (e.control || e.command)
                        {
                            Redo();
                            e.Use();
                        }
                        break;
                    case KeyCode.RightBracket:
                        _brushSize *= 1.2f;
                        _brushSize = Mathf.Min(_brushSize, 1f);
                        e.Use();
                        break;
                    case KeyCode.LeftBracket:
                        _brushSize /= 1.2f;
                        _brushSize = Mathf.Max(_brushSize, 0.005f);
                        e.Use();
                        break;
                }
            }
            
            // Pick layer with Alt+Click
            if (e.type == EventType.MouseDown && e.button == 0 && e.alt)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (RaycastTriangle(ray, out int triIndex))
                {
                    _selectedLayerIndex = _triangleOwnership[triIndex];
                    KitbashWindow.Refresh();
                    e.Use();
                }
                return;
            }
            
            // Paint/Bucket on left click
            if (_selectedLayerIndex >= 0 && 
                (e.type == EventType.MouseDown || (_paintTool == PaintToolMode.Brush && e.type == EventType.MouseDrag)) && 
                e.button == 0 && !e.alt && !e.shift && !e.control)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                
                // Record state at start of paint stroke
                if (e.type == EventType.MouseDown && !_isPaintingStroke)
                {
                    RecordStateForUndo();
                    _isPaintingStroke = true;
                    _redoStack.Clear(); // Clear redo stack when new action is performed
                }
                
                if (_paintTool == PaintToolMode.Bucket)
                {
                    // Bucket fill
                    if (RaycastTriangle(ray, out int triIndex))
                    {
                        BucketFill(triIndex);
                        e.Use();
                    }
                }
                else
                {
                    // Brush
                    if (RaycastWithPosition(ray, out Vector3 hitPos, out Vector3 hitNormal, out int centerTri))
                    {
                        PaintTrianglesInRadius(hitPos, _brushSize);
                        e.Use();
                    }
                }
            }
            
            // Apply deferred visualization on mouse up (end of paint stroke)
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                if (_needsVisualizationUpdate)
                {
                    ApplyDeferredVisualization();
                }
                _isPaintingStroke = false;
                
                // Refresh the layers panel to update triangle counts/percentages
                KitbashWindow.Refresh();
            }
            
            // Draw brush cursor (only for brush/eraser mode)
            if (_paintTool != PaintToolMode.Bucket && (e.type == EventType.Repaint || e.type == EventType.Layout))
            {
                DrawBrushCursor(sceneView);
            }
        }
        
        private void DrawToolbar(SceneView sceneView)
        {
            float toolbarWidth = 520;
            float toolbarHeight = 36;
            float margin = 16;
            
            Rect cameraRect = sceneView.camera.pixelRect;
            toolbarWidth = Mathf.Min(toolbarWidth, cameraRect.width - 32);
            
            float x = (cameraRect.width - toolbarWidth) / 2f;
            float y = cameraRect.height - toolbarHeight - margin;
            
            Rect toolbarRect = new Rect(x, y, toolbarWidth, toolbarHeight);
            
            // Dark background
            EditorGUI.DrawRect(toolbarRect, new Color(0.15f, 0.15f, 0.15f, 0.95f));
            // Border
            EditorGUI.DrawRect(new Rect(toolbarRect.x, toolbarRect.y, toolbarRect.width, 1), new Color(0.3f, 0.3f, 0.3f));
            
            Rect contentRect = new Rect(toolbarRect.x + 8, toolbarRect.y + 6, toolbarRect.width - 16, toolbarRect.height - 12);
            
            GUILayout.BeginArea(contentRect);
            using (new EditorGUILayout.HorizontalScope())
            {
                // === Paint Mode Toggle ===
                DrawToolButton("Grid.PaintTool", "Brush (B)", _paintTool == PaintToolMode.Brush, () => _paintTool = PaintToolMode.Brush);
                DrawToolButton("Grid.FillTool", "Bucket (G)", _paintTool == PaintToolMode.Bucket, () => _paintTool = PaintToolMode.Bucket);
                
                DrawVerticalSeparator();
                
                // === Brush Size (only for brush mode) ===
                if (_paintTool == PaintToolMode.Brush)
                {
                    GUILayout.Label("Size:", EditorStyles.miniLabel, GUILayout.Width(28));
                    _brushSize = GUILayout.HorizontalSlider(_brushSize, 0.005f, 0.5f, GUILayout.Width(50));
                    GUILayout.Label($"{_brushSize:F2}", EditorStyles.miniLabel, GUILayout.Width(28));
                    DrawVerticalSeparator();
                }
                
                // === Fill Mode (for bucket) ===
                if (_paintTool == PaintToolMode.Bucket)
                {
                    GUILayout.Label("Fill:", EditorStyles.miniLabel, GUILayout.Width(24));
                    DrawFillModeButton("Connected", FillMode.Connected);
                    DrawFillModeButton("UV Island", FillMode.UVIsland);
                    DrawFillModeButton("Submesh", FillMode.Submesh);
                    DrawVerticalSeparator();
                }
                
                // === Mirror ===
                Color oldBg = GUI.backgroundColor;
                var teal = new Color(0.21f, 0.75f, 0.69f);
                
                GUI.backgroundColor = _mirrorX ? teal : oldBg;
                if (GUILayout.Button("X", EditorStyles.miniButtonLeft, GUILayout.Width(20))) _mirrorX = !_mirrorX;
                GUI.backgroundColor = _mirrorY ? teal : oldBg;
                if (GUILayout.Button("Y", EditorStyles.miniButtonMid, GUILayout.Width(20))) _mirrorY = !_mirrorY;
                GUI.backgroundColor = _mirrorZ ? teal : oldBg;
                if (GUILayout.Button("Z", EditorStyles.miniButtonRight, GUILayout.Width(20))) _mirrorZ = !_mirrorZ;
                GUI.backgroundColor = oldBg;
                
                DrawVerticalSeparator();
                
                // === Layer chips ===
                DrawLayerChips();
                
                GUILayout.FlexibleSpace();
                
                // === Actions ===
                if (GUILayout.Button("Auto", EditorStyles.miniButton, GUILayout.Width(36)))
                {
                    AutoFillFromSubmeshes();
                    KitbashWindow.Refresh();
                }
            }
            GUILayout.EndArea();
            
            // Keyboard hints at bottom
            Rect hintsRect = new Rect(toolbarRect.x, toolbarRect.yMax + 2, toolbarRect.width, 14);
            GUI.Label(hintsRect, "[ ] Size   Ctrl+Z/Y = Undo/Redo   Alt+Click = Pick Layer", 
                new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1,1,1,0.4f) }});
        }
        
        private void DrawToolButton(string icon, string tooltip, bool selected, System.Action onClick)
        {
            var style = selected ? EditorStyles.toolbarButton : EditorStyles.miniButton;
            GUI.backgroundColor = selected ? new Color(0.21f, 0.75f, 0.69f) : Color.white;
            
            var content = EditorGUIUtility.IconContent(icon);
            content.tooltip = tooltip;
            
            if (GUILayout.Button(content, style, GUILayout.Width(26), GUILayout.Height(22)))
            {
                onClick?.Invoke();
            }
            GUI.backgroundColor = Color.white;
        }
        
        private void DrawFillModeButton(string label, FillMode mode)
        {
            bool selected = _fillMode == mode;
            GUI.backgroundColor = selected ? new Color(0.21f, 0.75f, 0.69f) : Color.white;
            
            GUIStyle style = mode == FillMode.Connected ? EditorStyles.miniButtonLeft :
                             mode == FillMode.Submesh ? EditorStyles.miniButtonRight : 
                             EditorStyles.miniButtonMid;
            
            if (GUILayout.Button(label, style, GUILayout.Height(18)))
            {
                _fillMode = mode;
            }
            GUI.backgroundColor = Color.white;
        }
        
        private void DrawVerticalSeparator()
        {
            GUILayout.Space(6);
            var rect = GUILayoutUtility.GetRect(1, 18);
            EditorGUI.DrawRect(rect, new Color(1, 1, 1, 0.15f));
            GUILayout.Space(6);
        }
        
        private void DrawLayerChips()
        {
            // Show up to 4 layers as color chips
            int maxChips = Mathf.Min(4, Layers.Count);
            for (int i = 0; i < maxChips; i++)
            {
                var layer = Layers[i];
                bool isSelected = i == SelectedLayerIndex;
                
                // Chip background
                Color chipBg = isSelected ? new Color(layer.color.r, layer.color.g, layer.color.b, 0.3f) : Color.clear;
                
                var chipStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    padding = new RectOffset(2, 2, 1, 1),
                    margin = new RectOffset(1, 1, 2, 2),
                    fontSize = 9
                };
                
                GUI.backgroundColor = layer.color;
                string label = layer.name.Length > 4 ? layer.name.Substring(0, 4) : layer.name;
                
                if (GUILayout.Button(label, chipStyle, GUILayout.Height(16)))
                {
                    SelectedLayerIndex = i;
                    KitbashWindow.Refresh();
                }
            }
            GUI.backgroundColor = Color.white;
        }
        
        private void PaintTrianglesInRadius(Vector3 center, float radius)
        {
            if (_targetMesh == null || _previewModel == null) return;
            
            var meshTransform = _previewModel.GetComponentInChildren<Renderer>()?.transform;
            if (meshTransform == null) return;
            
            // Ensure spatial cache is built
            if (!_spatialCacheValid)
            {
                BuildSpatialCache();
            }
            
            // Calculate mirror center from mesh bounds
            if (_mirrorCenter == Vector3.zero)
            {
                _mirrorCenter = meshTransform.TransformPoint(_targetMesh.bounds.center);
            }
            
            // Build list of paint centers (original + mirrored)
            var paintCenters = new List<Vector3> { center };
            if (_mirrorX) paintCenters.Add(new Vector3(2 * _mirrorCenter.x - center.x, center.y, center.z));
            if (_mirrorY) paintCenters.Add(new Vector3(center.x, 2 * _mirrorCenter.y - center.y, center.z));
            if (_mirrorZ) paintCenters.Add(new Vector3(center.x, center.y, 2 * _mirrorCenter.z - center.z));
            
            // XY, XZ, YZ combinations
            if (_mirrorX && _mirrorY) paintCenters.Add(new Vector3(2 * _mirrorCenter.x - center.x, 2 * _mirrorCenter.y - center.y, center.z));
            if (_mirrorX && _mirrorZ) paintCenters.Add(new Vector3(2 * _mirrorCenter.x - center.x, center.y, 2 * _mirrorCenter.z - center.z));
            if (_mirrorY && _mirrorZ) paintCenters.Add(new Vector3(center.x, 2 * _mirrorCenter.y - center.y, 2 * _mirrorCenter.z - center.z));
            if (_mirrorX && _mirrorY && _mirrorZ) paintCenters.Add(new Vector3(2 * _mirrorCenter.x - center.x, 2 * _mirrorCenter.y - center.y, 2 * _mirrorCenter.z - center.z));
            
            // Use spatial grid for fast radius query
            var paintedTris = new HashSet<int>();
            foreach (var paintCenter in paintCenters)
            {
                foreach (int triIdx in GetTrianglesInRadius(paintCenter, radius))
                {
                    if (!paintedTris.Contains(triIdx))
                    {
                        PaintTriangle(triIdx);
                        paintedTris.Add(triIdx);
                    }
                }
            }
            
            // Live update during painting (throttled)
            double currentTime = EditorApplication.timeSinceStartup;
            if (_needsVisualizationUpdate && (currentTime - _lastVisualizationTime) >= LIVE_UPDATE_INTERVAL)
            {
                UpdateVisualization();
                _lastVisualizationTime = currentTime;
            }
        }
        
        private void PaintTriangle(int triIndex)
        {
            if (triIndex < 0 || triIndex >= _triangleOwnership.Length) return;
            if (_selectedLayerIndex < 0) return;
            
            int previousOwner = _triangleOwnership[triIndex];
            if (previousOwner != _selectedLayerIndex)
            {
                _triangleOwnership[triIndex] = _selectedLayerIndex;
                _dirtyTriangles.Add(triIndex);
                _needsVisualizationUpdate = true;
            }
        }
        
        /// <summary>
        /// Apply deferred visualization updates (called on MouseUp)
        /// </summary>
        public void ApplyDeferredVisualization()
        {
            if (!_needsVisualizationUpdate) return;
            
            UpdateVisualization();
            _dirtyTriangles.Clear();
            _needsVisualizationUpdate = false;
        }
        
        /// <summary>
        /// Bucket fill starting from a triangle - fills based on current fill mode
        /// </summary>
        private void BucketFill(int startTriIndex)
        {
            if (_targetMesh == null || startTriIndex < 0 || startTriIndex >= _triangleOwnership.Length) return;
            if (_selectedLayerIndex < 0) return;
            
            // Record state for undo at start of bucket fill
            if (!_isPaintingStroke)
            {
                RecordStateForUndo();
                _isPaintingStroke = true;
                _redoStack.Clear();
            }
            
            HashSet<int> toFill = new HashSet<int>();
            
            switch (_fillMode)
            {
                case FillMode.Connected:
                    toFill = FloodFillConnected(startTriIndex);
                    break;
                case FillMode.UVIsland:
                    toFill = FindUVIsland(startTriIndex);
                    break;
                case FillMode.Submesh:
                    toFill = FindSubmeshTriangles(startTriIndex);
                    break;
            }
            
            // Apply fill
            foreach (int triIdx in toFill)
            {
                if (_triangleOwnership[triIdx] != _selectedLayerIndex)
                {
                    _triangleOwnership[triIdx] = _selectedLayerIndex;
                    _dirtyTriangles.Add(triIdx);
                    _needsVisualizationUpdate = true;
                }
            }
            
            // Immediate update for bucket fill
            if (_needsVisualizationUpdate)
            {
                UpdateVisualization();
                _needsVisualizationUpdate = false;
                _isPaintingStroke = false;
            }
            
            if (_selectedLayerIndex >= 0 && _selectedLayerIndex < _layers.Count)
                Debug.Log($"[Kitbash] Bucket fill: {toFill.Count} triangles → {_layers[_selectedLayerIndex].name}");
        }
        
        /// <summary>
        /// Records current ownership state for undo
        /// </summary>
        private void RecordStateForUndo()
        {
            if (_triangleOwnership == null || _triangleOwnership.Length == 0) return;
            
            // Clone the current state
            int[] state = new int[_triangleOwnership.Length];
            System.Array.Copy(_triangleOwnership, state, _triangleOwnership.Length);
            
            _undoStack.Push(state);
            
            // Limit undo stack size to prevent memory issues
            if (_undoStack.Count > 50)
            {
                // Remove oldest entries (reverse order)
                var temp = new Stack<int[]>();
                while (_undoStack.Count > 40)
                    _undoStack.Pop();
            }
        }
        
        /// <summary>
        /// Undo the last painting operation (Ctrl+Z)
        /// </summary>
        public void Undo()
        {
            if (_undoStack.Count == 0) return;
            if (_triangleOwnership == null || _triangleOwnership.Length == 0) return;
            
            // Save current state to redo stack
            int[] currentState = new int[_triangleOwnership.Length];
            System.Array.Copy(_triangleOwnership, currentState, _triangleOwnership.Length);
            _redoStack.Push(currentState);
            
            // Restore previous state
            int[] previousState = _undoStack.Pop();
            System.Array.Copy(previousState, _triangleOwnership, _triangleOwnership.Length);
            
            UpdateVisualization();
            SceneView.RepaintAll();
            
            Debug.Log("[Kitbash] Undo performed");
        }
        
        /// <summary>
        /// Redo the last undone operation (Ctrl+Y or Ctrl+Shift+Z)
        /// </summary>
        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            if (_triangleOwnership == null || _triangleOwnership.Length == 0) return;
            
            // Save current state to undo stack
            int[] currentState = new int[_triangleOwnership.Length];
            System.Array.Copy(_triangleOwnership, currentState, _triangleOwnership.Length);
            _undoStack.Push(currentState);
            
            // Restore next state
            int[] nextState = _redoStack.Pop();
            System.Array.Copy(nextState, _triangleOwnership, _triangleOwnership.Length);
            
            UpdateVisualization();
            SceneView.RepaintAll();
            
            Debug.Log("[Kitbash] Redo performed");
        }
        
        private HashSet<int> FloodFillConnected(int startTri)
        {
            var result = new HashSet<int>();
            var queue = new Queue<int>();
            int startOwner = _triangleOwnership[startTri];
            
            queue.Enqueue(startTri);
            result.Add(startTri);
            
            // Build edge-to-triangle adjacency
            var edgeToTri = BuildEdgeAdjacency();
            
            while (queue.Count > 0)
            {
                int tri = queue.Dequeue();
                
                // Get adjacent triangles
                foreach (int neighbor in GetAdjacentTriangles(tri, edgeToTri))
                {
                    if (!result.Contains(neighbor) && _triangleOwnership[neighbor] == startOwner)
                    {
                        result.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
            
            return result;
        }
        
        private HashSet<int> FindUVIsland(int startTri)
        {
            var result = new HashSet<int>();
            
            // Check if mesh has UVs
            var uvs = _targetMesh.uv;
            if (uvs == null || uvs.Length == 0)
            {
                // Fallback to connected fill
                return FloodFillConnected(startTri);
            }
            
            var triangles = GetAllTriangles();
            var queue = new Queue<int>();
            int startOwner = _triangleOwnership[startTri];
            
            // Build UV-based adjacency (triangles that share UV coordinates)
            var uvToTris = new Dictionary<Vector2, List<int>>();
            int triCount = _triangleCount;
            
            for (int t = 0; t < triCount; t++)
            {
                for (int v = 0; v < 3; v++)
                {
                    int vertIdx = triangles[t * 3 + v];
                    if (vertIdx < uvs.Length)
                    {
                        Vector2 uv = new Vector2(
                            Mathf.Round(uvs[vertIdx].x * 1000) / 1000,
                            Mathf.Round(uvs[vertIdx].y * 1000) / 1000
                        );
                        
                        if (!uvToTris.ContainsKey(uv))
                            uvToTris[uv] = new List<int>();
                        if (!uvToTris[uv].Contains(t))
                            uvToTris[uv].Add(t);
                    }
                }
            }
            
            queue.Enqueue(startTri);
            result.Add(startTri);
            
            while (queue.Count > 0)
            {
                int tri = queue.Dequeue();
                
                // Find neighbors via shared UV vertices
                for (int v = 0; v < 3; v++)
                {
                    int vertIdx = triangles[tri * 3 + v];
                    if (vertIdx < uvs.Length)
                    {
                        Vector2 uv = new Vector2(
                            Mathf.Round(uvs[vertIdx].x * 1000) / 1000,
                            Mathf.Round(uvs[vertIdx].y * 1000) / 1000
                        );
                        
                        if (uvToTris.TryGetValue(uv, out var neighbors))
                        {
                            foreach (int neighbor in neighbors)
                            {
                                if (!result.Contains(neighbor) && _triangleOwnership[neighbor] == startOwner)
                                {
                                    result.Add(neighbor);
                                    queue.Enqueue(neighbor);
                                }
                            }
                        }
                    }
                }
            }
            
            return result;
        }
        
        private HashSet<int> FindSubmeshTriangles(int startTri)
        {
            var result = new HashSet<int>();
            BuildTriangleCache();
            if (_submeshTriangleOffsets == null) return result;
            
            // Find which submesh the start triangle belongs to
            int startSubmesh = -1;
            for (int s = 0; s < _targetMesh.subMeshCount; s++)
            {
                int startIdx = _submeshTriangleOffsets != null && s < _submeshTriangleOffsets.Length
                    ? _submeshTriangleOffsets[s]
                    : 0;
                int subTriCount = _targetMesh.GetTriangles(s).Length / 3;
                
                if (startTri >= startIdx && startTri < startIdx + subTriCount)
                {
                    startSubmesh = s;
                    break;
                }
            }
            
            if (startSubmesh < 0) return result;
            
            // Get all triangles in that submesh with same ownership
            int startOwner = _triangleOwnership[startTri];
            int start = _submeshTriangleOffsets != null && startSubmesh < _submeshTriangleOffsets.Length
                ? _submeshTriangleOffsets[startSubmesh]
                : 0;
            int count = _targetMesh.GetTriangles(startSubmesh).Length / 3;
            
            for (int t = start; t < start + count; t++)
            {
                if (_triangleOwnership[t] == startOwner)
                {
                    result.Add(t);
                }
            }
            
            return result;
        }
        
        private Dictionary<(int, int), List<int>> BuildEdgeAdjacency()
        {
            var edgeToTri = new Dictionary<(int, int), List<int>>();
            var triangles = GetAllTriangles();
            int triCount = _triangleCount;
            
            for (int t = 0; t < triCount; t++)
            {
                int v0 = triangles[t * 3];
                int v1 = triangles[t * 3 + 1];
                int v2 = triangles[t * 3 + 2];
                
                AddEdge(edgeToTri, v0, v1, t);
                AddEdge(edgeToTri, v1, v2, t);
                AddEdge(edgeToTri, v2, v0, t);
            }
            
            return edgeToTri;
        }
        
        private void AddEdge(Dictionary<(int, int), List<int>> edgeToTri, int v0, int v1, int tri)
        {
            var edge = v0 < v1 ? (v0, v1) : (v1, v0);
            if (!edgeToTri.ContainsKey(edge))
                edgeToTri[edge] = new List<int>();
            edgeToTri[edge].Add(tri);
        }
        
        private List<int> GetAdjacentTriangles(int tri, Dictionary<(int, int), List<int>> edgeToTri)
        {
            var result = new List<int>();
            var triangles = GetAllTriangles();
            
            int v0 = triangles[tri * 3];
            int v1 = triangles[tri * 3 + 1];
            int v2 = triangles[tri * 3 + 2];
            
            void CheckEdge(int a, int b)
            {
                var edge = a < b ? (a, b) : (b, a);
                if (edgeToTri.TryGetValue(edge, out var tris))
                {
                    foreach (int t in tris)
                    {
                        if (t != tri && !result.Contains(t))
                            result.Add(t);
                    }
                }
            }
            
            CheckEdge(v0, v1);
            CheckEdge(v1, v2);
            CheckEdge(v2, v0);
            
            return result;
        }
        
        private bool RaycastTriangle(Ray ray, out int triIndex)
        {
            triIndex = -1;
            if (_previewModel == null || _targetMesh == null) return false;
            
            var meshTransform = _previewModel.GetComponentInChildren<Renderer>()?.transform;
            if (meshTransform == null) return false;
            
            // Use GPU-accelerated raycast
            triIndex = KitbashRaycast.FindTriangle(ray, _targetMesh, meshTransform);
            return triIndex >= 0;
        }
        
        private bool RayTriangleIntersect(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0;
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float a = Vector3.Dot(edge1, h);
            if (a > -0.00001f && a < 0.00001f) return false;
            float f = 1.0f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0.0f || u > 1.0f) return false;
            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.direction, q);
            if (v < 0.0f || u + v > 1.0f) return false;
            t = f * Vector3.Dot(edge2, q);
            return t > 0.00001f;
        }
        
        private void DrawBrushCursor(SceneView sceneView)
        {
            // Update brush hit position
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            _brushHitValid = RaycastWithPosition(ray, out _brushHitPos, out _brushHitNormal, out int _);
            
            if (!_brushHitValid) return;
            
            // Check if Ctrl is held for erase mode preview
            bool erasePreview = Event.current.control;
            
            // Draw brush disc like Cloth Inspector
            Color brushColor;
            if (erasePreview)
            {
                // Erase mode: gray with X pattern
                brushColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            }
            else if (_selectedLayerIndex >= 0 && _selectedLayerIndex < _layers.Count)
            {
                brushColor = _layers[_selectedLayerIndex].color;
                brushColor.a = 0.4f;
            }
            else
            {
                brushColor = new Color(1f, 1f, 1f, 0.3f);
            }
            
            Handles.color = brushColor;
            Handles.DrawSolidDisc(_brushHitPos, _brushHitNormal, _brushSize);
            
            // Draw outline
            brushColor.a = 0.8f;
            Handles.color = brushColor;
            Handles.DrawWireDisc(_brushHitPos, _brushHitNormal, _brushSize, 2f);
            
            // Draw mirrored brush ghosts
            if (_mirrorX || _mirrorY || _mirrorZ)
            {
                Color mirrorColor = brushColor;
                mirrorColor.a = 0.2f;
                
                if (_mirrorX)
                {
                    Vector3 mirrorPos = new Vector3(2 * _mirrorCenter.x - _brushHitPos.x, _brushHitPos.y, _brushHitPos.z);
                    Handles.color = mirrorColor;
                    Handles.DrawSolidDisc(mirrorPos, _brushHitNormal, _brushSize);
                    Handles.DrawWireDisc(mirrorPos, _brushHitNormal, _brushSize, 1f);
                }
                if (_mirrorY)
                {
                    Vector3 mirrorPos = new Vector3(_brushHitPos.x, 2 * _mirrorCenter.y - _brushHitPos.y, _brushHitPos.z);
                    Handles.color = mirrorColor;
                    Handles.DrawSolidDisc(mirrorPos, _brushHitNormal, _brushSize);
                    Handles.DrawWireDisc(mirrorPos, _brushHitNormal, _brushSize, 1f);
                }
                if (_mirrorZ)
                {
                    Vector3 mirrorPos = new Vector3(_brushHitPos.x, _brushHitPos.y, 2 * _mirrorCenter.z - _brushHitPos.z);
                    Handles.color = mirrorColor;
                    Handles.DrawSolidDisc(mirrorPos, _brushHitNormal, _brushSize);
                    Handles.DrawWireDisc(mirrorPos, _brushHitNormal, _brushSize, 1f);
                }
            }
            
            // Force repaint for smooth cursor
            sceneView.Repaint();
        }
        
        private bool RaycastWithPosition(Ray ray, out Vector3 hitPos, out Vector3 hitNormal, out int triIndex)
        {
            hitPos = Vector3.zero;
            hitNormal = Vector3.up;
            triIndex = -1;
            
            if (_previewModel == null || _targetMesh == null) return false;
            
            var meshTransform = _previewModel.GetComponentInChildren<Renderer>()?.transform;
            if (meshTransform == null) return false;
            
            // Use MeshCollider-based raycast for stable results
            return KitbashRaycast.FindTriangleWithPosition(ray, _targetMesh, meshTransform, 
                out triIndex, out hitPos, out hitNormal);
        }
        
        public void AddLayer(string name, string sourceGuid, string sourceName, Color color)
        {
            _layers.Add(new OwnershipLayer
            {
                name = name,
                sourceGuid = sourceGuid,
                sourceName = sourceName,
                color = color,
                visible = true
            });
            UpdateVisualization();
        }
        
        public void RemoveLayer(int index)
        {
            if (index <= 0 || index >= _layers.Count) return;

            // Reassign triangles to Unknown
            for (int i = 0; i < _triangleOwnership.Length; i++)
            {
                if (_triangleOwnership[i] == index)
                    _triangleOwnership[i] = 0;
                else if (_triangleOwnership[i] > index)
                    _triangleOwnership[i]--;
            }
            
            _layers.RemoveAt(index);
            if (_selectedLayerIndex >= index) _selectedLayerIndex--;
            
            UpdateVisualization();
        }
        
        /// <summary>
        /// Clears all triangles from a layer, reassigning them to Unknown.
        /// </summary>
        public int ClearLayer(int layerIndex)
        {
            if (layerIndex <= 0 || layerIndex >= _layers.Count) return 0;
            
            int clearedCount = 0;
            for (int i = 0; i < _triangleOwnership.Length; i++)
            {
                if (_triangleOwnership[i] == layerIndex)
                {
                    _triangleOwnership[i] = 0;
                    clearedCount++;
                }
            }
            
            if (clearedCount > 0)
            {
                UpdateVisualization();
            }
            
            return clearedCount;
        }
        
        /// <summary>
        /// Assigns all Unknown (layer 0) triangles to the specified layer.
        /// </summary>
        public int AssignUnknownToLayer(int layerIndex)
        {
            if (layerIndex <= 0 || layerIndex >= _layers.Count) return 0;
            
            int assignedCount = 0;
            for (int i = 0; i < _triangleOwnership.Length; i++)
            {
                if (_triangleOwnership[i] == 0)
                {
                    _triangleOwnership[i] = layerIndex;
                    assignedCount++;
                }
            }
            
            if (assignedCount > 0)
            {
                UpdateVisualization();
            }
            
            return assignedCount;
        }
        
        public void AutoFillFromSubmeshes()
        {
            if (_targetMesh == null || _previewModel == null) return;
            
            BuildTriangleCache();
            
            // Use cached original materials (before visualization was applied)
            var materials = _originalMaterials;
            if (materials == null || materials.Length == 0)
            {
                // Fallback: try to get from renderer (won't work if visualization already applied)
                var renderer = _previewModel.GetComponentInChildren<Renderer>();
                if (renderer != null)
                    materials = renderer.sharedMaterials;
            }
            
            if (materials == null) return;
            
            if (_submeshTriangleOffsets == null) return;
            
            // Assign per submesh using cached offsets
            for (int submeshIdx = 0; submeshIdx < _targetMesh.subMeshCount; submeshIdx++)
            {
                int submeshTriCount = _targetMesh.GetTriangles(submeshIdx).Length / 3;
                int triangleOffset = _submeshTriangleOffsets[submeshIdx];
                
                string matName = submeshIdx < materials.Length && materials[submeshIdx] != null 
                    ? materials[submeshIdx].name 
                    : $"Submesh {submeshIdx}";
                
                // Find or create layer for this material
                int layerIndex = -1;
                for (int i = 0; i < _layers.Count; i++)
                {
                    if (_layers[i].name == matName)
                    {
                        layerIndex = i;
                        break;
                    }
                }
                
                if (layerIndex < 0)
                {
                    Color newColor = KitbashSourceLibrary.GetDefaultColor(_layers.Count);
                    _layers.Add(new OwnershipLayer
                    {
                        name = matName,
                        sourceGuid = null,
                        sourceName = "Auto-Fill",
                        color = newColor,
                        visible = true
                    });
                    layerIndex = _layers.Count - 1;
                }
                
                // Assign ownership for this submesh's triangles
                // Assuming standard Mesh topology where submeshes are contiguous ranges of indices
                for (int t = 0; t < submeshTriCount; t++)
                {
                    int globalTriIdx = triangleOffset + t;
                    if (globalTriIdx < _triangleOwnership.Length)
                    {
                        _triangleOwnership[globalTriIdx] = layerIndex;
                    }
                }
            }
            
            UpdateVisualization();
        }
        
        private void SaveOwnershipData()
        {
            Debug.Log("[KitbashStage] Saving ownership data...");
            
            try
            {
                // 1. Get or create the kitbash assets folder
                string assetFolder = GetOrCreateKitbashFolder();
                
                // 2. Get or create OwnershipMap
                OwnershipMap map = GetOrCreateOwnershipMap(assetFolder);
                if (map == null)
                {
                    Debug.LogError("[KitbashStage] Failed to create OwnershipMap");
                    return;
                }
                
                // 3. Build regions from _triangleOwnership
                map.regions.Clear();
                var trisByLayer = new Dictionary<int, List<int>>();
                for (int i = 0; i < _triangleOwnership.Length; i++)
                {
                    int layerIdx = _triangleOwnership[i];
                    if (!trisByLayer.TryGetValue(layerIdx, out var list))
                    {
                        list = new List<int>();
                        trisByLayer[layerIdx] = list;
                    }
                    list.Add(i);
                }
                
                foreach (var kvp in trisByLayer)
                {
                    var region = new OwnershipRegion
                    {
                        sourcePartIndex = kvp.Key == 0 ? -1 : kvp.Key - 1,
                        triangleIndices = kvp.Value.ToArray(),
                        confidence = 1f,
                        samplePoints = GenerateSamplePoints(kvp.Value)
                    };
                    map.regions.Add(region);
                }
                
                map.expectedTriangleCount = _triangleOwnership.Length;
                map.targetMeshGuid = AssetDatabase.AssetPathToGUID(_assetPath);
                map.targetMeshName = _targetMesh != null ? _targetMesh.name : "";
                EditorUtility.SetDirty(map);
                
                // 4. Get or create Recipe
                KitbashRecipe recipe = GetOrCreateRecipe(assetFolder);
                if (recipe == null)
                {
                    Debug.LogError("[KitbashStage] Failed to create KitbashRecipe");
                    return;
                }
                
                recipe.parts.Clear();
                for (int i = 1; i < _layers.Count; i++) // Skip Unknown layer (index 0)
                {
                    recipe.parts.Add(new KitbashSourcePart
                    {
                        sourceFbxGuid = _layers[i].sourceGuid ?? "",
                        displayName = _layers[i].name,
                        meshPath = ""
                    });
                }
                recipe.ownershipMapGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(map));
                recipe.targetDerivedFbxGuid = AssetDatabase.AssetPathToGUID(_assetPath);
                recipe.ComputeHash();
                EditorUtility.SetDirty(recipe);
                
                // 5. Update settings with GUIDs
                _settings.ownershipMapGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(map));
                _settings.kitbashRecipeGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(recipe));
                
                // 6. Save to importer userData
                _importer.userData = JsonUtility.ToJson(_settings);
                EditorUtility.SetDirty(_importer);
                
                AssetDatabase.SaveAssets();
                
                int assignedTris = _triangleOwnership.Count(t => t > 0);
                Debug.Log($"[KitbashStage] Saved ownership: {assignedTris}/{_triangleOwnership.Length} triangles assigned, {_layers.Count - 1} layers");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KitbashStage] Error saving ownership data: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Gets or creates the folder for kitbash assets next to the derived FBX.
        /// </summary>
        private string GetOrCreateKitbashFolder()
        {
            string dir = Path.GetDirectoryName(_assetPath);
            string name = Path.GetFileNameWithoutExtension(_assetPath);
            string folderName = $"{name}_Kitbash";
            string folderPath = Path.Combine(dir, folderName).Replace("\\", "/");
            
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string parentFolder = dir.Replace("\\", "/");
                AssetDatabase.CreateFolder(parentFolder, folderName);
                Debug.Log($"[KitbashStage] Created kitbash folder: {folderPath}");
            }
            
            return folderPath;
        }
        
        /// <summary>
        /// Gets existing OwnershipMap or creates a new one.
        /// </summary>
        private OwnershipMap GetOrCreateOwnershipMap(string folder)
        {
            // Try to load existing
            if (!string.IsNullOrEmpty(_settings?.ownershipMapGuid))
            {
                string existingPath = AssetDatabase.GUIDToAssetPath(_settings.ownershipMapGuid);
                if (!string.IsNullOrEmpty(existingPath))
                {
                    var existing = AssetDatabase.LoadAssetAtPath<OwnershipMap>(existingPath);
                    if (existing != null)
                    {
                        return existing;
                    }
                }
            }
            
            // Create new
            var map = ScriptableObject.CreateInstance<OwnershipMap>();
            string assetPath = Path.Combine(folder, "OwnershipMap.asset").Replace("\\", "/");
            AssetDatabase.CreateAsset(map, assetPath);
            Debug.Log($"[KitbashStage] Created OwnershipMap: {assetPath}");
            return map;
        }
        
        /// <summary>
        /// Gets existing KitbashRecipe or creates a new one.
        /// </summary>
        private KitbashRecipe GetOrCreateRecipe(string folder)
        {
            // Try to load existing
            if (!string.IsNullOrEmpty(_settings?.kitbashRecipeGuid))
            {
                string existingPath = AssetDatabase.GUIDToAssetPath(_settings.kitbashRecipeGuid);
                if (!string.IsNullOrEmpty(existingPath))
                {
                    var existing = AssetDatabase.LoadAssetAtPath<KitbashRecipe>(existingPath);
                    if (existing != null)
                    {
                        return existing;
                    }
                }
            }
            
            // Create new
            var recipe = ScriptableObject.CreateInstance<KitbashRecipe>();
            string assetPath = Path.Combine(folder, "KitbashRecipe.asset").Replace("\\", "/");
            AssetDatabase.CreateAsset(recipe, assetPath);
            Debug.Log($"[KitbashStage] Created KitbashRecipe: {assetPath}");
            return recipe;
        }
        
        /// <summary>
        /// Generates sample points for a list of triangles in LOCAL space.
        /// Used for reprojection when mesh topology changes.
        /// </summary>
        private SamplePoint[] GenerateSamplePoints(List<int> triangleIndices)
        {
            if (_targetMesh == null || triangleIndices.Count == 0)
                return Array.Empty<SamplePoint>();
            
            var triangles = GetAllTriangles();
            var vertices = _targetMesh.vertices;
            var normals = _targetMesh.normals;
            
            // Sample up to 100 points per region (or all if fewer triangles)
            int sampleCount = Math.Min(triangleIndices.Count, 100);
            int step = Math.Max(1, triangleIndices.Count / sampleCount);
            
            var samples = new List<SamplePoint>();
            for (int i = 0; i < triangleIndices.Count && samples.Count < sampleCount; i += step)
            {
                int triIdx = triangleIndices[i];
                int baseIdx = triIdx * 3;
                
                if (baseIdx + 2 >= triangles.Length) continue;
                
                // Get triangle vertices in LOCAL space
                Vector3 v0 = vertices[triangles[baseIdx]];
                Vector3 v1 = vertices[triangles[baseIdx + 1]];
                Vector3 v2 = vertices[triangles[baseIdx + 2]];
                
                // Triangle center
                Vector3 center = (v0 + v1 + v2) / 3f;
                
                // Get normal (average of vertex normals or compute from triangle)
                Vector3 normal;
                if (normals != null && normals.Length > triangles[baseIdx])
                {
                    normal = (normals[triangles[baseIdx]] + normals[triangles[baseIdx + 1]] + normals[triangles[baseIdx + 2]]).normalized;
                }
                else
                {
                    normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                }
                
                samples.Add(new SamplePoint
                {
                    position = center,
                    normal = normal,
                    nearestTriangleIndex = triIdx
                });
            }
            
            return samples.ToArray();
        }
    }
}
#endif
