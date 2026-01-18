#if YUCP_KITBASH_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash.UI
{
    /// <summary>
    /// EditorTool for painting ownership regions on meshes.
    /// Provides brush-based painting with suggested parts and hotkeys.
    /// </summary>
    [EditorTool("Ownership Paint", typeof(Renderer))]
    public class OwnershipPaintTool : EditorTool
    {
        // Tool state
        public enum PaintMode
        {
            Paint,
            FillConnected
        }
        
        // Current settings
        public static float BrushSize = 0.1f;
        public static float BrushStrength = 1f;
        public static PaintMode CurrentMode = PaintMode.Paint;
        public static int SelectedSourceIndex = -1;
        public static List<SourceInfo> Sources = new List<SourceInfo>();
        public static List<SourceInfo> SuggestedSources = new List<SourceInfo>();
        public static List<SourceInfo> RecentSources = new List<SourceInfo>();
        
        // Visual state
        private static bool _showOnlyUnknown;
        private static bool _showBoundaries = true;
        
        // Mesh data cache
        private Mesh _targetMesh;
        private int[] _triangles;
        private Vector3[] _vertices;
        private int[] _ownershipData; // Per-triangle ownership (-1 = unknown)
        
        // Hover state
        private int _hoveredTriangle = -1;
        private Vector3 _brushCenter;
        private bool _isPainting;
        
        public struct SourceInfo
        {
            public int index;
            public string name;
            public string guid;
            public Color color;
        }
        
        public override GUIContent toolbarIcon => new GUIContent(
            EditorGUIUtility.IconContent("Grid.PaintTool").image,
            "Ownership Paint Tool\nPaint source ownership regions on mesh."
        );
        
        public override void OnActivated()
        {
            base.OnActivated();
            
            // Get target renderer and mesh
            if (target is Renderer renderer)
            {
                CacheMeshData(renderer);
            }
        }
        
        public override void OnWillBeDeactivated()
        {
            base.OnWillBeDeactivated();
            _targetMesh = null;
            _triangles = null;
            _vertices = null;
        }
        
        private void CacheMeshData(Renderer renderer)
        {
            _targetMesh = null;
            
            if (renderer is SkinnedMeshRenderer smr)
            {
                _targetMesh = smr.sharedMesh;
            }
            else if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    _targetMesh = filter.sharedMesh;
                }
            }
            
            if (_targetMesh != null)
            {
                _triangles = GetAllTriangles(_targetMesh);
                _vertices = _targetMesh.vertices;
                
                // Initialize ownership data (all unknown)
                int triangleCount = _triangles.Length / 3;
                _ownershipData = new int[triangleCount];
                for (int i = 0; i < triangleCount; i++)
                {
                    _ownershipData[i] = -1; // Unknown
                }
            }
        }

        private static int[] GetAllTriangles(Mesh mesh)
        {
            if (mesh == null) return Array.Empty<int>();
            
            var all = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                all.AddRange(mesh.GetTriangles(s));
            }
            return all.ToArray();
        }
        
        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView)) return;
            if (_targetMesh == null) return;
            
            HandleInput(sceneView);
            DrawBrushCursor(sceneView);
            DrawOwnershipOverlay(sceneView);
            
            // Consume mouse events when painting
            if (_isPainting)
            {
                Event.current.Use();
            }
        }
        
        private void HandleInput(SceneView sceneView)
        {
            Event e = Event.current;
            
            // Hotkeys
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.B:
                        CurrentMode = PaintMode.Paint;
                        e.Use();
                        break;
                    case KeyCode.F:
                        CurrentMode = PaintMode.FillConnected;
                        e.Use();
                        break;
                    case KeyCode.H:
                        _showOnlyUnknown = !_showOnlyUnknown;
                        sceneView.Repaint();
                        e.Use();
                        break;
                    // Number keys for quick source selection
                    case KeyCode.Alpha1:
                    case KeyCode.Alpha2:
                    case KeyCode.Alpha3:
                    case KeyCode.Alpha4:
                    case KeyCode.Alpha5:
                    case KeyCode.Alpha6:
                    case KeyCode.Alpha7:
                    case KeyCode.Alpha8:
                    case KeyCode.Alpha9:
                        int index = (int)e.keyCode - (int)KeyCode.Alpha1;
                        if (index < Sources.Count)
                        {
                            SelectedSourceIndex = index;
                            e.Use();
                        }
                        break;
                }
            }
            
            // Alt+Click to pick source under cursor
            if (e.alt && e.type == EventType.MouseDown && e.button == 0)
            {
                PickSourceUnderCursor(sceneView);
                e.Use();
                return;
            }
            
            // Raycast to find hovered triangle
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (RaycastMesh(ray, out int triangleIndex, out Vector3 hitPoint))
            {
                _hoveredTriangle = triangleIndex;
                _brushCenter = hitPoint;
                
                // Update suggested sources based on cursor position
                UpdateSuggestedSources(hitPoint);
                
                // Paint on mouse down/drag
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                {
                    _isPainting = true;
                    ApplyBrush(hitPoint);
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag && e.button == 0 && _isPainting)
                {
                    ApplyBrush(hitPoint);
                    e.Use();
                }
            }
            else
            {
                _hoveredTriangle = -1;
            }
            
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                _isPainting = false;
            }
            
            // Scroll to adjust brush size
            if (e.type == EventType.ScrollWheel && e.shift)
            {
                BrushSize = Mathf.Clamp(BrushSize - e.delta.y * 0.01f, 0.01f, 10f);
                e.Use();
            }
            
            sceneView.Repaint();
        }
        
        private void PickSourceUnderCursor(SceneView sceneView)
        {
            if (_hoveredTriangle < 0 || _hoveredTriangle >= _ownershipData.Length) return;
            
            int owner = _ownershipData[_hoveredTriangle];
            if (owner >= 0 && owner < Sources.Count)
            {
                SelectedSourceIndex = owner;
                AddToRecent(Sources[owner]);
                Debug.Log($"[OwnershipPaintTool] Picked source: {Sources[owner].name}");
            }
        }
        
        private void UpdateSuggestedSources(Vector3 worldPoint)
        {
            // TODO: Compute nearest sources based on bounding boxes
            // For now, just show first few sources
            SuggestedSources.Clear();
            for (int i = 0; i < Math.Min(4, Sources.Count); i++)
            {
                SuggestedSources.Add(Sources[i]);
            }
        }
        
        private void AddToRecent(SourceInfo source)
        {
            RecentSources.RemoveAll(s => s.index == source.index);
            RecentSources.Insert(0, source);
            if (RecentSources.Count > 4)
            {
                RecentSources.RemoveAt(RecentSources.Count - 1);
            }
        }
        
        private bool RaycastMesh(Ray ray, out int triangleIndex, out Vector3 hitPoint)
        {
            triangleIndex = -1;
            hitPoint = Vector3.zero;
            
            if (_targetMesh == null || _triangles == null || _vertices == null) return false;
            
            // Transform ray to local space
            var renderer = target as Renderer;
            if (renderer == null) return false;
            
            Matrix4x4 worldToLocal = renderer.transform.worldToLocalMatrix;
            Ray localRay = new Ray(
                worldToLocal.MultiplyPoint3x4(ray.origin),
                worldToLocal.MultiplyVector(ray.direction).normalized
            );
            
            float closestDistance = float.MaxValue;
            
            // Check each triangle
            for (int i = 0; i < _triangles.Length; i += 3)
            {
                Vector3 v0 = _vertices[_triangles[i]];
                Vector3 v1 = _vertices[_triangles[i + 1]];
                Vector3 v2 = _vertices[_triangles[i + 2]];
                
                if (RayTriangleIntersect(localRay, v0, v1, v2, out float t) && t > 0 && t < closestDistance)
                {
                    closestDistance = t;
                    triangleIndex = i / 3;
                    hitPoint = renderer.transform.TransformPoint(localRay.origin + localRay.direction * t);
                }
            }
            
            return triangleIndex >= 0;
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
        
        private void ApplyBrush(Vector3 center)
        {
            if (SelectedSourceIndex < 0 && CurrentMode == PaintMode.Paint) return;
            if (_targetMesh == null) return;
            
            var renderer = target as Renderer;
            if (renderer == null) return;
            
            int valueToApply = SelectedSourceIndex;
            
            // Find all triangles within brush radius
            float radiusSq = BrushSize * BrushSize;
            
            for (int i = 0; i < _triangles.Length; i += 3)
            {
                // Get triangle center
                Vector3 v0 = renderer.transform.TransformPoint(_vertices[_triangles[i]]);
                Vector3 v1 = renderer.transform.TransformPoint(_vertices[_triangles[i + 1]]);
                Vector3 v2 = renderer.transform.TransformPoint(_vertices[_triangles[i + 2]]);
                Vector3 triCenter = (v0 + v1 + v2) / 3f;
                
                float distSq = (triCenter - center).sqrMagnitude;
                if (distSq <= radiusSq)
                {
                    int triIndex = i / 3;
                    
                    if (CurrentMode == PaintMode.FillConnected)
                    {
                        // TODO: Flood fill connected triangles with same owner
                        _ownershipData[triIndex] = valueToApply;
                    }
                    else
                    {
                        _ownershipData[triIndex] = valueToApply;
                    }
                }
            }
            
            // Track recently used
            if (SelectedSourceIndex >= 0 && SelectedSourceIndex < Sources.Count)
            {
                AddToRecent(Sources[SelectedSourceIndex]);
            }
        }
        
        private void DrawBrushCursor(SceneView sceneView)
        {
            if (_hoveredTriangle < 0) return;
            
            // Draw brush circle
            Handles.color = GetBrushColor();
            Handles.DrawWireDisc(_brushCenter, sceneView.camera.transform.forward, BrushSize);
            
            // Draw inner filled circle
            Color fillColor = Handles.color;
            fillColor.a = 0.2f;
            Handles.color = fillColor;
            Handles.DrawSolidDisc(_brushCenter, sceneView.camera.transform.forward, BrushSize * 0.8f);
        }
        
        private Color GetBrushColor()
        {
            if (SelectedSourceIndex >= 0 && SelectedSourceIndex < Sources.Count)
            {
                return Sources[SelectedSourceIndex].color;
            }
            
            return Color.white;
        }
        
        private void DrawOwnershipOverlay(SceneView sceneView)
        {
            if (_targetMesh == null || _ownershipData == null) return;
            
            var renderer = target as Renderer;
            if (renderer == null) return;
            
            // Draw colored triangles for ownership visualization
            // Note: This is a simplified visualization. In production, you'd use a custom shader.
            
            if (_showBoundaries)
            {
                // Draw boundary edges between different ownership regions
                Dictionary<(int, int), int> edgeToOwner = new Dictionary<(int, int), int>();
                
                for (int i = 0; i < _triangles.Length; i += 3)
                {
                    int triIndex = i / 3;
                    int owner = _ownershipData[triIndex];
                    
                    // Skip if showing only unknown and this isn't unknown
                    if (_showOnlyUnknown && owner >= 0) continue;
                    
                    int v0 = _triangles[i];
                    int v1 = _triangles[i + 1];
                    int v2 = _triangles[i + 2];
                    
                    // Check each edge for boundary
                    CheckBoundaryEdge(renderer, v0, v1, triIndex, owner, edgeToOwner);
                    CheckBoundaryEdge(renderer, v1, v2, triIndex, owner, edgeToOwner);
                    CheckBoundaryEdge(renderer, v2, v0, triIndex, owner, edgeToOwner);
                }
            }
        }
        
        private void CheckBoundaryEdge(Renderer renderer, int v0, int v1, int triIndex, int owner, Dictionary<(int, int), int> edgeToOwner)
        {
            var edge = v0 < v1 ? (v0, v1) : (v1, v0);
            
            if (edgeToOwner.TryGetValue(edge, out int otherOwner))
            {
                if (otherOwner != owner)
                {
                    // Draw boundary line
                    Vector3 p0 = renderer.transform.TransformPoint(_vertices[v0]);
                    Vector3 p1 = renderer.transform.TransformPoint(_vertices[v1]);
                    
                    Handles.color = Color.black;
                    Handles.DrawLine(p0, p1, 2f);
                }
            }
            else
            {
                edgeToOwner[edge] = owner;
            }
        }
    }
    
    // Note: OwnershipPaintToolOverlay removed - functionality consolidated in KitbashStage.DrawToolbar()
}
#endif
