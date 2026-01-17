#if YUCP_KITBASH_ENABLED
using UnityEngine;
using UnityEditor;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash.GPU
{
    /// <summary>
    /// Stable raycast for triangle picking using Unity's MeshCollider.
    /// Much more reliable than GPU compute shader for interactive picking.
    /// </summary>
    public static class KitbashRaycast
    {
        private static MeshCollider _collider;
        private static GameObject _colliderObject;
        private static Mesh _cachedMesh;
        
        /// <summary>
        /// Find which triangle a ray hits. Returns -1 if no hit.
        /// Uses MeshCollider for stable results.
        /// </summary>
        public static int FindTriangle(Ray ray, Mesh mesh, Transform meshTransform, float maxDistance = 1000f)
        {
            // Ensure collider is set up
            EnsureCollider(mesh, meshTransform);
            
            if (_collider == null) return -1;
            
            // Raycast against the mesh collider
            if (_collider.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                return hit.triangleIndex;
            }
            
            return -1;
        }
        
        /// <summary>
        /// Find which triangle a ray hits and get the hit position.
        /// </summary>
        public static bool FindTriangleWithPosition(Ray ray, Mesh mesh, Transform meshTransform, 
            out int triIndex, out Vector3 hitPos, out Vector3 hitNormal, float maxDistance = 1000f)
        {
            triIndex = -1;
            hitPos = Vector3.zero;
            hitNormal = Vector3.up;
            
            EnsureCollider(mesh, meshTransform);
            
            if (_collider == null) return false;
            
            if (_collider.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                triIndex = hit.triangleIndex;
                hitPos = hit.point;
                hitNormal = hit.normal;
                return true;
            }
            
            return false;
        }
        
        private static void EnsureCollider(Mesh mesh, Transform meshTransform)
        {
            // Check if we need to recreate collider
            bool needsRecreate = _colliderObject == null || 
                                 _collider == null || 
                                 _cachedMesh != mesh;
            
            if (needsRecreate)
            {
                Cleanup();
                
                // Create hidden collider object
                _colliderObject = new GameObject("_KitbashRaycastCollider");
                _colliderObject.hideFlags = HideFlags.HideAndDontSave;
                
                // Match transform
                _colliderObject.transform.position = meshTransform.position;
                _colliderObject.transform.rotation = meshTransform.rotation;
                _colliderObject.transform.localScale = meshTransform.lossyScale;
                
                // Add mesh collider
                _collider = _colliderObject.AddComponent<MeshCollider>();
                _collider.sharedMesh = mesh;
                _cachedMesh = mesh;
            }
            else if (_colliderObject != null && meshTransform != null)
            {
                // Update transform to match
                _colliderObject.transform.position = meshTransform.position;
                _colliderObject.transform.rotation = meshTransform.rotation;
                _colliderObject.transform.localScale = meshTransform.lossyScale;
            }
        }
        
        /// <summary>
        /// Release resources.
        /// </summary>
        public static void Cleanup()
        {
            if (_colliderObject != null)
            {
                Object.DestroyImmediate(_colliderObject);
                _colliderObject = null;
            }
            _collider = null;
            _cachedMesh = null;
        }
        
        // Keep these for API compatibility
        public static void Initialize() { }
        public static bool IsGPUAvailable() => false;
    }
}
#endif
