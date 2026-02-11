using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace YUCP.DevTools.Editor.ModelRevision
{
    /// <summary>
    /// Utility for synchronizing components and their properties between GameObjects.
    /// Handles copying, property sync, and reference remapping.
    /// </summary>
    public static class ComponentSyncUtility
    {
        /// <summary>
        /// Component types that are typically transferable in VRChat avatars.
        /// </summary>
        public static readonly string[] TransferableTypeNames = new[]
        {
            "VRCPhysBone",
            "VRCPhysBoneCollider",
            "VRCContactSender",
            "VRCContactReceiver",
            "VRCHeadChop",
            "VRCSpatialAudioSource",
            "VRCFury",
            "VRCFuryComponent",
            "Light",
            "AudioSource",
            "ParticleSystem",
            "TrailRenderer",
            "LineRenderer",
            "Cloth"
        };

        /// <summary>
        /// Syncs all components from source to target GameObject.
        /// </summary>
        public static SyncResult SyncComponents(
            GameObject source,
            GameObject target,
            ComponentSyncSettings settings,
            Dictionary<string, string> bonePathMapping = null)
        {
            var result = new SyncResult();

            var sourceComponents = source.GetComponents<Component>()
                .Where(c => c != null && ShouldSyncComponent(c, settings))
                .ToList();

            var targetComponents = target.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform))
                .ToList();

            // Group by type
            var sourceByType = sourceComponents.GroupBy(c => c.GetType()).ToDictionary(g => g.Key, g => g.ToList());
            var targetByType = targetComponents.GroupBy(c => c.GetType()).ToDictionary(g => g.Key, g => g.ToList());

            // Add missing components
            foreach (var kvp in sourceByType)
            {
                if (!targetByType.TryGetValue(kvp.Key, out var targetList))
                {
                    targetList = new List<Component>();
                }

                for (int i = targetList.Count; i < kvp.Value.Count; i++)
                {
                    var copied = CopyComponent(kvp.Value[i], target, bonePathMapping);
                    if (copied != null)
                    {
                        result.ComponentsAdded++;
                    }
                }
            }

            // Sync existing components
            foreach (var kvp in sourceByType)
            {
                if (!targetByType.TryGetValue(kvp.Key, out var targetList)) continue;

                for (int i = 0; i < Math.Min(kvp.Value.Count, targetList.Count); i++)
                {
                    SyncProperties(kvp.Value[i], targetList[i], bonePathMapping);
                    result.ComponentsSynced++;
                }
            }

            return result;
        }

        /// <summary>
        /// Copies a component to a target GameObject with all serialized properties.
        /// </summary>
        public static Component CopyComponent(
            Component source,
            GameObject target,
            Dictionary<string, string> bonePathMapping = null)
        {
            if (source == null || target == null) return null;

            var type = source.GetType();

            // Check if component already exists
            var existing = target.GetComponent(type);

            Component newComponent;
            if (existing != null)
            {
                newComponent = existing;
            }
            else
            {
                newComponent = Undo.AddComponent(target, type);
            }

            if (newComponent == null) return null;

            // Copy all serialized properties
            EditorUtility.CopySerialized(source, newComponent);

            // Remap bone references if needed
            if (bonePathMapping != null && bonePathMapping.Count > 0)
            {
                RemapReferences(newComponent, bonePathMapping, target.transform.root);
            }

            return newComponent;
        }

        /// <summary>
        /// Syncs specific properties from source to target component.
        /// </summary>
        public static void SyncProperties(
            Component source,
            Component target,
            Dictionary<string, string> bonePathMapping = null,
            List<string> propertyPaths = null)
        {
            if (source == null || target == null) return;
            if (source.GetType() != target.GetType()) return;

            var sourceSO = new SerializedObject(source);
            var targetSO = new SerializedObject(target);

            var sourceProp = sourceSO.GetIterator();
            sourceProp.Next(true);

            do
            {
                if (sourceProp.name == "m_Script") continue;
                if (propertyPaths != null && !propertyPaths.Contains(sourceProp.propertyPath)) continue;

                var targetProp = targetSO.FindProperty(sourceProp.propertyPath);
                if (targetProp == null) continue;

                CopySerializedProperty(sourceProp, targetProp);
            }
            while (sourceProp.Next(false));

            targetSO.ApplyModifiedProperties();

            // Remap bone references if needed
            if (bonePathMapping != null && bonePathMapping.Count > 0)
            {
                RemapReferences(target, bonePathMapping, target.transform.root);
            }
        }

        /// <summary>
        /// Remaps all Transform/GameObject references in a component using path mapping.
        /// </summary>
        public static void RemapReferences(
            Component component,
            Dictionary<string, string> pathMapping,
            Transform targetRoot)
        {
            if (component == null || pathMapping == null || targetRoot == null) return;

            var so = new SerializedObject(component);
            var prop = so.GetIterator();
            prop.Next(true);
            bool modified = false;

            do
            {
                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var obj = prop.objectReferenceValue;
                    Transform referencedTransform = null;

                    if (obj is Transform t)
                    {
                        referencedTransform = t;
                    }
                    else if (obj is GameObject go)
                    {
                        referencedTransform = go.transform;
                    }
                    else if (obj is Component c)
                    {
                        referencedTransform = c.transform;
                    }

                    if (referencedTransform != null)
                    {
                        var sourcePath = GetTransformPath(referencedTransform);
                        if (pathMapping.TryGetValue(sourcePath, out var targetPath))
                        {
                            var targetTransform = FindTransformByPath(targetRoot, targetPath);
                            if (targetTransform != null)
                            {
                                if (obj is Transform)
                                {
                                    prop.objectReferenceValue = targetTransform;
                                }
                                else if (obj is GameObject)
                                {
                                    prop.objectReferenceValue = targetTransform.gameObject;
                                }
                                else if (obj is Component)
                                {
                                    var targetComp = targetTransform.GetComponent(obj.GetType());
                                    if (targetComp != null)
                                    {
                                        prop.objectReferenceValue = targetComp;
                                    }
                                }
                                modified = true;
                            }
                        }
                    }
                }
            }
            while (prop.Next(true));

            if (modified)
            {
                so.ApplyModifiedProperties();
            }
        }

        /// <summary>
        /// Gets all serialized properties from a component.
        /// </summary>
        public static List<SerializedPropertyInfo> GetSerializedProperties(Component component)
        {
            var result = new List<SerializedPropertyInfo>();
            if (component == null) return result;

            var so = new SerializedObject(component);
            var prop = so.GetIterator();
            prop.Next(true);

            do
            {
                if (prop.name == "m_Script") continue;

                result.Add(new SerializedPropertyInfo
                {
                    Path = prop.propertyPath,
                    DisplayName = prop.displayName,
                    Type = prop.propertyType,
                    Value = GetPropertyValue(prop),
                    IsReference = prop.propertyType == SerializedPropertyType.ObjectReference
                });
            }
            while (prop.Next(false));

            return result;
        }

        /// <summary>
        /// Checks if a property contains a Transform/GameObject reference.
        /// </summary>
        public static bool IsReferenceProperty(SerializedProperty prop)
        {
            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                return false;

            var obj = prop.objectReferenceValue;
            return obj is Transform || obj is GameObject || obj is Component;
        }

        #region Private Helpers

        private static bool ShouldSyncComponent(Component component, ComponentSyncSettings settings)
        {
            if (component is Transform) return false;

            var typeName = component.GetType().Name;

            if (settings.ExcludedTypes.Contains(typeName))
                return false;

            if (settings.IncludedTypesOnly != null && settings.IncludedTypesOnly.Count > 0)
                return settings.IncludedTypesOnly.Contains(typeName);

            // Check category settings
            if (typeName.StartsWith("VRCPhysBone") && !settings.SyncPhysBones)
                return false;

            if ((typeName.Contains("Contact") || typeName.StartsWith("VRCContact")) && !settings.SyncContacts)
                return false;

            if (typeName.StartsWith("VRCFury") && !settings.SyncVRCFury)
                return false;

            if (component is Renderer && !settings.SyncRenderers)
                return false;

            return true;
        }

        private static void CopySerializedProperty(SerializedProperty source, SerializedProperty target)
        {
            switch (source.propertyType)
            {
                case SerializedPropertyType.Integer:
                    target.intValue = source.intValue;
                    break;
                case SerializedPropertyType.Boolean:
                    target.boolValue = source.boolValue;
                    break;
                case SerializedPropertyType.Float:
                    target.floatValue = source.floatValue;
                    break;
                case SerializedPropertyType.String:
                    target.stringValue = source.stringValue;
                    break;
                case SerializedPropertyType.ObjectReference:
                    target.objectReferenceValue = source.objectReferenceValue;
                    break;
                case SerializedPropertyType.Enum:
                    target.enumValueIndex = source.enumValueIndex;
                    break;
                case SerializedPropertyType.Vector2:
                    target.vector2Value = source.vector2Value;
                    break;
                case SerializedPropertyType.Vector3:
                    target.vector3Value = source.vector3Value;
                    break;
                case SerializedPropertyType.Vector4:
                    target.vector4Value = source.vector4Value;
                    break;
                case SerializedPropertyType.Quaternion:
                    target.quaternionValue = source.quaternionValue;
                    break;
                case SerializedPropertyType.Color:
                    target.colorValue = source.colorValue;
                    break;
                case SerializedPropertyType.Rect:
                    target.rectValue = source.rectValue;
                    break;
                case SerializedPropertyType.Bounds:
                    target.boundsValue = source.boundsValue;
                    break;
                case SerializedPropertyType.AnimationCurve:
                    target.animationCurveValue = source.animationCurveValue;
                    break;
            }
        }

        private static object GetPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;
                case SerializedPropertyType.Boolean:
                    return prop.boolValue;
                case SerializedPropertyType.Float:
                    return prop.floatValue;
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue;
                case SerializedPropertyType.Enum:
                    return prop.enumValueIndex < prop.enumDisplayNames.Length 
                        ? prop.enumDisplayNames[prop.enumValueIndex] 
                        : prop.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2:
                    return prop.vector2Value;
                case SerializedPropertyType.Vector3:
                    return prop.vector3Value;
                default:
                    return $"[{prop.propertyType}]";
            }
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null) return "";

            var path = transform.name;
            var current = transform.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private static Transform FindTransformByPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;

            // Remove root name from path if present
            var rootName = root.name;
            if (path.StartsWith(rootName + "/"))
            {
                path = path.Substring(rootName.Length + 1);
            }
            else if (path == rootName)
            {
                return root;
            }

            return root.Find(path);
        }

        #endregion
    }

    /// <summary>
    /// Settings for component synchronization.
    /// </summary>
    [Serializable]
    public class ComponentSyncSettings
    {
        public bool SyncTransforms = true;
        public bool SyncRenderers = true;
        public bool SyncPhysBones = true;
        public bool SyncContacts = true;
        public bool SyncVRCFury = true;
        public bool SyncCustomScripts = true;
        public List<string> ExcludedTypes = new List<string>();
        public List<string> IncludedTypesOnly = null;

        public static ComponentSyncSettings Default => new ComponentSyncSettings();
    }

    /// <summary>
    /// Result of a component sync operation.
    /// </summary>
    public class SyncResult
    {
        public int ComponentsAdded;
        public int ComponentsSynced;
        public int ComponentsRemoved;
        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();

        public bool HasErrors => Errors.Count > 0;
        public int TotalChanges => ComponentsAdded + ComponentsSynced + ComponentsRemoved;
    }

    /// <summary>
    /// Information about a serialized property.
    /// </summary>
    public class SerializedPropertyInfo
    {
        public string Path;
        public string DisplayName;
        public SerializedPropertyType Type;
        public object Value;
        public bool IsReference;
    }
}
