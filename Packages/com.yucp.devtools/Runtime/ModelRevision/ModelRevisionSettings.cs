using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools
{
    [Serializable]
    public class BlendshapeMapping
    {
        public string sourceName;
        public string targetName;
        public float confidence = 1f;
        public bool isManualOverride = false;
        public MappingStatus status = MappingStatus.Unknown;
        public string notes = "";
    }

    [Serializable]
    public class ComponentMapping
    {
        public string componentType;
        public string sourceBonePath;
        public string targetBonePath;
        public bool enabled = true;
        public string notes = "";
    }

    [Serializable]
    public class BoneMatchingSettings
    {
        public BoneMatchingMode mode = BoneMatchingMode.NameBased;
        public bool ignoreHumanoidBones = true;
        public string includeNameFilter = "";
        public string excludeNameFilter = "";
        public float maxDistance = 0.1f;
    }

    [Serializable]
    public class AvatarDescriptorSettings
    {
        public bool transferVisemes = true;
        public bool transferEyeLook = true;
        public bool transferViewPosition = true;
        public bool transferCollision = true;
        public bool transferAnimationOverrides = true;
        public bool transferExpressionsMenu = true;
        public bool transferExpressionParameters = true;
        
        [Tooltip("Viseme blendshape mappings")]
        public List<BlendshapeMapping> visemeMappings = new List<BlendshapeMapping>();
        
        [Tooltip("Eye look bone mappings")]
        public List<ComponentMapping> eyeLookMappings = new List<ComponentMapping>();
    }

    [Serializable]
    public class AnimationRemapping
    {
        public string clipPath;
        public bool enabled = true;
        public List<BlendshapeMapping> curveMappings = new List<BlendshapeMapping>();
        public string notes = "";
    }

    /// <summary>
    /// Mapping between GameObjects in source and target prefabs.
    /// </summary>
    [Serializable]
    public class GameObjectMapping
    {
        public string sourcePath;
        public string targetPath;
        public bool syncTransform = true;
        public bool syncComponents = true;
        public bool enabled = true;
    }

    /// <summary>
    /// Rules for syncing a specific component type.
    /// </summary>
    [Serializable]
    public class ComponentSyncRule
    {
        public string componentTypeName;
        public bool enabled = true;
        public List<string> excludedProperties = new List<string>();
        public List<string> includedPropertiesOnly = new List<string>();
        public bool remapBoneReferences = true;
    }

    /// <summary>
    /// Individual property mapping for component sync.
    /// </summary>
    [Serializable]
    public class PropertyMapping
    {
        public string propertyPath;
        public bool enabled = true;
        public string sourceValue;
        public string targetValue;
        public bool requiresRemapping;
    }

    /// <summary>
    /// Settings for the full sync operation.
    /// </summary>
    [Serializable]
    public class SyncSettings
    {
        [Header("Hierarchy Sync")]
        public bool syncHierarchy = true;
        public bool addMissingObjects = true;
        public bool removeExtraObjects = false;
        public bool alignObjectNames = false;

        [Header("Component Sync")]
        public bool syncComponents = true;
        public bool syncPhysBones = true;
        public bool syncContacts = true;
        public bool syncVRCFury = true;
        public bool syncCustomScripts = false;

        [Header("Transform Sync")]
        public bool syncTransforms = false;
        public bool syncPosition = true;
        public bool syncRotation = true;
        public bool syncScale = true;

        [Header("Blendshape Sync")]
        public bool syncBlendshapes = true;
        public float autoMapConfidenceThreshold = 0.7f;

        [Header("Build Settings")]
        public bool applyAtBuildTime = true;

        [Header("Component Rules")]
        public List<ComponentSyncRule> componentRules = new List<ComponentSyncRule>();
    }

    /// <summary>
    /// Bone path cache entry.
    /// </summary>
    [Serializable]
    public class BonePathCache
    {
        public string sourcePath;
        public string targetPath;
        public float confidence = 1f;
        public bool isManualOverride = false;
    }

    public enum MappingStatus
    {
        Unknown,
        Mapped,
        MissingInTarget,
        Conflict,
        ManualOverride
    }

    public enum BoneMatchingMode
    {
        NameBased,
        PathBased,
        Hybrid
    }

    public enum SyncDirection
    {
        SourceToTarget,
        TargetToSource,
        Bidirectional
    }
}
