using System;
using System.Collections.Generic;
using UnityEngine;
using YUCP.Components;

namespace YUCP.DevTools
{
    /// <summary>
    /// ScriptableObject that stores the master configuration for model revision management.
    /// Acts as the central hub for managing multiple avatar variants with shared settings.
    /// </summary>
    [BetaWarning("This component is in BETA and may not work as intended. Model revision management is experimental and may require manual configuration.")]
    [CreateAssetMenu(fileName = "ModelRevisionBase", menuName = "YUCP/Model Revision Base", order = 1)]
    public class ModelRevisionBase : ScriptableObject
    {
        [Header("Base Configuration")]
        [Tooltip("Base prefab reference (the original/old version)")]
        public GameObject basePrefab;
        
        [Tooltip("List of registered variant prefabs")]
        public List<GameObject> registeredVariants = new List<GameObject>();
        
        [Tooltip("Source variant to copy settings from (for initial setup)")]
        public GameObject sourceVariant;
        
        [Header("Blendshape Mappings")]
        [Tooltip("Blendshape name mappings (source → target)")]
        public List<BlendshapeMapping> blendshapeMappings = new List<BlendshapeMapping>();
        
        [Header("Component Transfer Settings")]
        [Tooltip("Components to transfer between variants")]
        public List<ComponentMapping> componentMappings = new List<ComponentMapping>();
        
        [Tooltip("Bone matching settings")]
        public BoneMatchingSettings boneMatchingSettings = new BoneMatchingSettings();
        
        [Header("Avatar Descriptor Settings")]
        [Tooltip("Avatar Descriptor settings to synchronize")]
        public AvatarDescriptorSettings avatarDescriptorSettings = new AvatarDescriptorSettings();
        
        [Header("Animation Remapping (Optional)")]
        [Tooltip("Enable animation curve remapping")]
        public bool enableAnimationRemapping = false;
        
        [Tooltip("Animation clip remappings")]
        public List<AnimationRemapping> animationRemappings = new List<AnimationRemapping>();
        
        [Header("Sync Settings")]
        [Tooltip("Settings for the full sync operation")]
        public SyncSettings syncSettings = new SyncSettings();
        
        [Header("GameObject Mappings")]
        [Tooltip("Explicit GameObject path mappings between source and target")]
        public List<GameObjectMapping> gameObjectMappings = new List<GameObjectMapping>();
        
        [Header("Bone Path Cache")]
        [Tooltip("Cached bone path resolutions")]
        public List<BonePathCache> bonePathCache = new List<BonePathCache>();
        
        [Header("Debug Info")]
        [Tooltip("Last modified timestamp")]
        public string lastModified;
        
        [Tooltip("Version of this base configuration")]
        public string version = "1.0.0";
        
        private void OnValidate()
        {
            lastModified = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        
        /// <summary>
        /// Register a variant prefab with this base
        /// </summary>
        public void RegisterVariant(GameObject variant)
        {
            if (variant == null) return;
            
            if (!registeredVariants.Contains(variant))
            {
                registeredVariants.Add(variant);
                Debug.Log($"[ModelRevisionBase] Registered variant: {variant.name}");
            }
        }
        
        /// <summary>
        /// Unregister a variant prefab from this base
        /// </summary>
        public void UnregisterVariant(GameObject variant)
        {
            if (variant == null) return;
            
            if (registeredVariants.Contains(variant))
            {
                registeredVariants.Remove(variant);
                Debug.Log($"[ModelRevisionBase] Unregistered variant: {variant.name}");
            }
        }
        
        /// <summary>
        /// Get blendshape mapping for a source name
        /// </summary>
        public BlendshapeMapping GetBlendshapeMapping(string sourceName)
        {
            return blendshapeMappings.Find(m => m.sourceName == sourceName);
        }
        
        /// <summary>
        /// Add or update a blendshape mapping
        /// </summary>
        public void SetBlendshapeMapping(string sourceName, string targetName, float confidence = 1f, bool isManual = false)
        {
            var existing = GetBlendshapeMapping(sourceName);
            if (existing != null)
            {
                existing.targetName = targetName;
                existing.confidence = confidence;
                existing.isManualOverride = isManual;
                existing.status = MappingStatus.Mapped;
            }
            else
            {
                blendshapeMappings.Add(new BlendshapeMapping
                {
                    sourceName = sourceName,
                    targetName = targetName,
                    confidence = confidence,
                    isManualOverride = isManual,
                    status = MappingStatus.Mapped
                });
            }
        }
        
        /// <summary>
        /// Get component mapping for a component type
        /// </summary>
        public ComponentMapping GetComponentMapping(string componentType)
        {
            return componentMappings.Find(m => m.componentType == componentType);
        }
        
        /// <summary>
        /// Add or update a component mapping
        /// </summary>
        public void SetComponentMapping(string componentType, string sourceBonePath, string targetBonePath, bool enabled = true)
        {
            var existing = GetComponentMapping(componentType);
            if (existing != null)
            {
                existing.sourceBonePath = sourceBonePath;
                existing.targetBonePath = targetBonePath;
                existing.enabled = enabled;
            }
            else
            {
                componentMappings.Add(new ComponentMapping
                {
                    componentType = componentType,
                    sourceBonePath = sourceBonePath,
                    targetBonePath = targetBonePath,
                    enabled = enabled
                });
            }
        }
        
        /// <summary>
        /// Get a cached bone path mapping
        /// </summary>
        public string GetCachedBonePath(string sourcePath)
        {
            var cached = bonePathCache.Find(c => c.sourcePath == sourcePath);
            return cached?.targetPath;
        }
        
        /// <summary>
        /// Cache a bone path resolution
        /// </summary>
        public void CacheBonePath(string sourcePath, string targetPath, float confidence = 1f, bool isManual = false)
        {
            var existing = bonePathCache.Find(c => c.sourcePath == sourcePath);
            if (existing != null)
            {
                existing.targetPath = targetPath;
                existing.confidence = confidence;
                existing.isManualOverride = isManual;
            }
            else
            {
                bonePathCache.Add(new BonePathCache
                {
                    sourcePath = sourcePath,
                    targetPath = targetPath,
                    confidence = confidence,
                    isManualOverride = isManual
                });
            }
        }
        
        /// <summary>
        /// Get a GameObject mapping by source path
        /// </summary>
        public GameObjectMapping GetGameObjectMapping(string sourcePath)
        {
            return gameObjectMappings.Find(m => m.sourcePath == sourcePath);
        }
        
        /// <summary>
        /// Add or update a GameObject mapping
        /// </summary>
        public void SetGameObjectMapping(string sourcePath, string targetPath, bool syncTransform = true, bool syncComponents = true)
        {
            var existing = GetGameObjectMapping(sourcePath);
            if (existing != null)
            {
                existing.targetPath = targetPath;
                existing.syncTransform = syncTransform;
                existing.syncComponents = syncComponents;
            }
            else
            {
                gameObjectMappings.Add(new GameObjectMapping
                {
                    sourcePath = sourcePath,
                    targetPath = targetPath,
                    syncTransform = syncTransform,
                    syncComponents = syncComponents,
                    enabled = true
                });
            }
        }
        
        /// <summary>
        /// Clear all mappings and reset to default state
        /// </summary>
        public void ResetMappings()
        {
            blendshapeMappings.Clear();
            componentMappings.Clear();
            animationRemappings.Clear();
            gameObjectMappings.Clear();
            bonePathCache.Clear();
            
            Debug.Log($"[ModelRevisionBase] Reset all mappings for: {name}");
        }
        
        /// <summary>
        /// Validate the base configuration
        /// </summary>
        public List<string> ValidateConfiguration()
        {
            var issues = new List<string>();
            
            if (basePrefab == null)
            {
                issues.Add("Base prefab is not assigned");
            }
            
            if (registeredVariants.Count == 0)
            {
                issues.Add("No variants registered");
            }
            
            if (sourceVariant == null && registeredVariants.Count > 0)
            {
                issues.Add("Source variant not selected");
            }
            
            // Check for duplicate variant references
            var duplicates = new List<GameObject>();
            for (int i = 0; i < registeredVariants.Count; i++)
            {
                for (int j = i + 1; j < registeredVariants.Count; j++)
                {
                    if (registeredVariants[i] == registeredVariants[j])
                    {
                        duplicates.Add(registeredVariants[i]);
                    }
                }
            }
            
            if (duplicates.Count > 0)
            {
                issues.Add($"Duplicate variant references found: {string.Join(", ", duplicates.ConvertAll(d => d.name))}");
            }
            
            return issues;
        }
        
        /// <summary>
        /// Get all blendshape names from the base prefab
        /// </summary>
        public List<string> GetBaseBlendshapeNames()
        {
            var blendshapeNames = new List<string>();
            
            if (basePrefab == null) return blendshapeNames;
            
            var skinnedMeshRenderers = basePrefab.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh != null)
                {
                    for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                    {
                        string name = smr.sharedMesh.GetBlendShapeName(i);
                        if (!blendshapeNames.Contains(name))
                        {
                            blendshapeNames.Add(name);
                        }
                    }
                }
            }
            
            return blendshapeNames;
        }
        
        /// <summary>
        /// Get all blendshape names from a variant prefab
        /// </summary>
        public List<string> GetVariantBlendshapeNames(GameObject variant)
        {
            var blendshapeNames = new List<string>();
            
            if (variant == null) return blendshapeNames;
            
            var skinnedMeshRenderers = variant.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh != null)
                {
                    for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                    {
                        string name = smr.sharedMesh.GetBlendShapeName(i);
                        if (!blendshapeNames.Contains(name))
                        {
                            blendshapeNames.Add(name);
                        }
                    }
                }
            }
            
            return blendshapeNames;
        }
    }
}
