using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace YUCP.DevTools.Editor.ModelRevision
{
    /// <summary>
    /// Compares two prefab hierarchies and identifies differences in GameObjects, components, and properties.
    /// </summary>
    public static class PrefabComparisonUtility
    {
        /// <summary>
        /// Performs a full comparison between source and target prefabs.
        /// </summary>
        public static PrefabComparisonResult ComparePrefabs(GameObject source, GameObject target)
        {
            if (source == null || target == null)
            {
                return new PrefabComparisonResult
                {
                    IsValid = false,
                    ErrorMessage = source == null ? "Source is null" : "Target is null"
                };
            }

            var result = new PrefabComparisonResult
            {
                IsValid = true,
                SourceRoot = source,
                TargetRoot = target,
                Hierarchy = CompareHierarchy(source, target),
                Components = new List<ComponentDiff>(),
                Blendshapes = new List<BlendshapeDiff>(),
                Properties = new List<PropertyDiff>()
            };

            // Gather component differences from matched objects
            foreach (var match in result.Hierarchy.Matched)
            {
                var componentDiffs = CompareComponents(match.SourceObject, match.TargetObject);
                result.Components.AddRange(componentDiffs);
                match.ComponentDifferences = componentDiffs;
            }

            // Compare blendshapes
            result.Blendshapes = CompareBlendshapes(source, target);

            return result;
        }

        /// <summary>
        /// Compares GameObject hierarchies between source and target.
        /// </summary>
        public static HierarchyDiff CompareHierarchy(GameObject source, GameObject target)
        {
            var result = new HierarchyDiff
            {
                AddedInSource = new List<GameObjectDiff>(),
                AddedInTarget = new List<GameObjectDiff>(),
                Matched = new List<GameObjectMatch>()
            };

            var sourceObjects = GetAllChildren(source);
            var targetObjects = GetAllChildren(target);

            var sourcePaths = sourceObjects.ToDictionary(o => GetRelativePath(o, source), o => o);
            var targetPaths = targetObjects.ToDictionary(o => GetRelativePath(o, target), o => o);

            // Find matched and source-only
            foreach (var kvp in sourcePaths)
            {
                if (targetPaths.TryGetValue(kvp.Key, out var targetObj))
                {
                    result.Matched.Add(new GameObjectMatch
                    {
                        SourcePath = kvp.Key,
                        TargetPath = kvp.Key,
                        SourceObject = kvp.Value,
                        TargetObject = targetObj,
                        ComponentDifferences = new List<ComponentDiff>()
                    });
                }
                else
                {
                    // Try fuzzy match by name
                    var fuzzyMatch = FindMatchingObjectByName(kvp.Value.name, target);
                    if (fuzzyMatch != null)
                    {
                        var fuzzyPath = GetRelativePath(fuzzyMatch, target);
                        result.Matched.Add(new GameObjectMatch
                        {
                            SourcePath = kvp.Key,
                            TargetPath = fuzzyPath,
                            SourceObject = kvp.Value,
                            TargetObject = fuzzyMatch,
                            IsFuzzyMatch = true,
                            ComponentDifferences = new List<ComponentDiff>()
                        });
                    }
                    else
                    {
                        result.AddedInSource.Add(new GameObjectDiff
                        {
                            Path = kvp.Key,
                            Name = kvp.Value.name,
                            Object = kvp.Value,
                            Components = kvp.Value.GetComponents<Component>().ToList()
                        });
                    }
                }
            }

            // Find target-only (excluding fuzzy matches)
            var matchedTargetPaths = result.Matched.Select(m => m.TargetPath).ToHashSet();
            foreach (var kvp in targetPaths)
            {
                if (!matchedTargetPaths.Contains(kvp.Key) && !sourcePaths.ContainsKey(kvp.Key))
                {
                    result.AddedInTarget.Add(new GameObjectDiff
                    {
                        Path = kvp.Key,
                        Name = kvp.Value.name,
                        Object = kvp.Value,
                        Components = kvp.Value.GetComponents<Component>().ToList()
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Compares components between two GameObjects.
        /// </summary>
        public static List<ComponentDiff> CompareComponents(GameObject source, GameObject target)
        {
            var result = new List<ComponentDiff>();
            var sourcePath = source.name;

            var sourceComponents = source.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform))
                .ToList();
            var targetComponents = target.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform))
                .ToList();

            // Group by type
            var sourceByType = sourceComponents.GroupBy(c => c.GetType()).ToDictionary(g => g.Key, g => g.ToList());
            var targetByType = targetComponents.GroupBy(c => c.GetType()).ToDictionary(g => g.Key, g => g.ToList());

            // Find components only in source
            foreach (var kvp in sourceByType)
            {
                if (!targetByType.ContainsKey(kvp.Key))
                {
                    foreach (var comp in kvp.Value)
                    {
                        result.Add(new ComponentDiff
                        {
                            ObjectPath = sourcePath,
                            ComponentType = kvp.Key,
                            Type = DiffType.Added,
                            SourceComponent = comp,
                            TargetComponent = null,
                            PropertyDifferences = new List<PropertyDiff>()
                        });
                    }
                }
            }

            // Find components only in target
            foreach (var kvp in targetByType)
            {
                if (!sourceByType.ContainsKey(kvp.Key))
                {
                    foreach (var comp in kvp.Value)
                    {
                        result.Add(new ComponentDiff
                        {
                            ObjectPath = sourcePath,
                            ComponentType = kvp.Key,
                            Type = DiffType.Removed,
                            SourceComponent = null,
                            TargetComponent = comp,
                            PropertyDifferences = new List<PropertyDiff>()
                        });
                    }
                }
            }

            // Compare matching components
            foreach (var kvp in sourceByType)
            {
                if (targetByType.TryGetValue(kvp.Key, out var targetList))
                {
                    // For simplicity, match by index
                    for (int i = 0; i < Math.Min(kvp.Value.Count, targetList.Count); i++)
                    {
                        var propDiffs = CompareComponentProperties(kvp.Value[i], targetList[i]);
                        if (propDiffs.Count > 0)
                        {
                            result.Add(new ComponentDiff
                            {
                                ObjectPath = sourcePath,
                                ComponentType = kvp.Key,
                                Type = DiffType.Modified,
                                SourceComponent = kvp.Value[i],
                                TargetComponent = targetList[i],
                                PropertyDifferences = propDiffs
                            });
                        }
                    }

                    // Extra in source
                    for (int i = targetList.Count; i < kvp.Value.Count; i++)
                    {
                        result.Add(new ComponentDiff
                        {
                            ObjectPath = sourcePath,
                            ComponentType = kvp.Key,
                            Type = DiffType.Added,
                            SourceComponent = kvp.Value[i],
                            TargetComponent = null,
                            PropertyDifferences = new List<PropertyDiff>()
                        });
                    }

                    // Extra in target
                    for (int i = kvp.Value.Count; i < targetList.Count; i++)
                    {
                        result.Add(new ComponentDiff
                        {
                            ObjectPath = sourcePath,
                            ComponentType = kvp.Key,
                            Type = DiffType.Removed,
                            SourceComponent = null,
                            TargetComponent = targetList[i],
                            PropertyDifferences = new List<PropertyDiff>()
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Compares serialized properties between two components.
        /// </summary>
        public static List<PropertyDiff> CompareComponentProperties(Component source, Component target)
        {
            var result = new List<PropertyDiff>();

            if (source == null || target == null || source.GetType() != target.GetType())
                return result;

            var sourceSO = new SerializedObject(source);
            var targetSO = new SerializedObject(target);

            var sourceProp = sourceSO.GetIterator();
            sourceProp.Next(true);

            do
            {
                if (sourceProp.name == "m_Script") continue;

                var targetProp = targetSO.FindProperty(sourceProp.propertyPath);
                if (targetProp == null) continue;

                if (!SerializedPropertyEqual(sourceProp, targetProp))
                {
                    result.Add(new PropertyDiff
                    {
                        PropertyPath = sourceProp.propertyPath,
                        PropertyDisplayName = sourceProp.displayName,
                        SourceValue = GetPropertyValue(sourceProp),
                        TargetValue = GetPropertyValue(targetProp),
                        RequiresBoneRemapping = IsTransformReference(sourceProp)
                    });
                }
            }
            while (sourceProp.Next(false));

            return result;
        }

        /// <summary>
        /// Compares blendshapes between source and target prefabs.
        /// </summary>
        public static List<BlendshapeDiff> CompareBlendshapes(GameObject source, GameObject target)
        {
            var result = new List<BlendshapeDiff>();

            var sourceRenderers = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var targetRenderers = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            foreach (var sourceRenderer in sourceRenderers)
            {
                var sourceMesh = sourceRenderer.sharedMesh;
                if (sourceMesh == null) continue;

                // Try to find matching renderer in target
                var targetRenderer = targetRenderers.FirstOrDefault(r => r.name == sourceRenderer.name);
                if (targetRenderer == null)
                {
                    // Try by mesh name
                    targetRenderer = targetRenderers.FirstOrDefault(r => 
                        r.sharedMesh != null && r.sharedMesh.name == sourceMesh.name);
                }

                if (targetRenderer == null || targetRenderer.sharedMesh == null)
                {
                    // All source blendshapes are missing in target
                    for (int i = 0; i < sourceMesh.blendShapeCount; i++)
                    {
                        result.Add(new BlendshapeDiff
                        {
                            SourceMeshName = sourceRenderer.name,
                            SourceBlendshapeName = sourceMesh.GetBlendShapeName(i),
                            TargetMeshName = null,
                            TargetBlendshapeName = null,
                            Type = DiffType.Added
                        });
                    }
                    continue;
                }

                var targetMesh = targetRenderer.sharedMesh;
                var sourceNames = Enumerable.Range(0, sourceMesh.blendShapeCount)
                    .Select(i => sourceMesh.GetBlendShapeName(i))
                    .ToHashSet();
                var targetNames = Enumerable.Range(0, targetMesh.blendShapeCount)
                    .Select(i => targetMesh.GetBlendShapeName(i))
                    .ToHashSet();

                // Source only
                foreach (var name in sourceNames.Except(targetNames))
                {
                    result.Add(new BlendshapeDiff
                    {
                        SourceMeshName = sourceRenderer.name,
                        SourceBlendshapeName = name,
                        TargetMeshName = targetRenderer.name,
                        TargetBlendshapeName = null,
                        Type = DiffType.Added
                    });
                }

                // Target only
                foreach (var name in targetNames.Except(sourceNames))
                {
                    result.Add(new BlendshapeDiff
                    {
                        SourceMeshName = sourceRenderer.name,
                        SourceBlendshapeName = null,
                        TargetMeshName = targetRenderer.name,
                        TargetBlendshapeName = name,
                        Type = DiffType.Removed
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Finds a matching GameObject in target by name.
        /// </summary>
        public static GameObject FindMatchingObject(GameObject sourceObj, GameObject targetRoot, MatchingMode mode)
        {
            if (sourceObj == null || targetRoot == null) return null;

            switch (mode)
            {
                case MatchingMode.ByPath:
                    var path = GetRelativePath(sourceObj, sourceObj.transform.root.gameObject);
                    return FindObjectByPath(targetRoot, path);

                case MatchingMode.ByName:
                    return FindMatchingObjectByName(sourceObj.name, targetRoot);

                case MatchingMode.Smart:
                    // Try path first, then name
                    var byPath = FindMatchingObject(sourceObj, targetRoot, MatchingMode.ByPath);
                    return byPath ?? FindMatchingObject(sourceObj, targetRoot, MatchingMode.ByName);

                default:
                    return null;
            }
        }

        #region Private Helpers

        private static List<GameObject> GetAllChildren(GameObject root)
        {
            var result = new List<GameObject>();
            GetAllChildrenRecursive(root.transform, result);
            return result;
        }

        private static void GetAllChildrenRecursive(Transform parent, List<GameObject> result)
        {
            foreach (Transform child in parent)
            {
                result.Add(child.gameObject);
                GetAllChildrenRecursive(child, result);
            }
        }

        private static string GetRelativePath(GameObject obj, GameObject root)
        {
            if (obj == root) return "";

            var path = obj.name;
            var current = obj.transform.parent;

            while (current != null && current.gameObject != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private static GameObject FindObjectByPath(GameObject root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;

            var transform = root.transform.Find(path);
            return transform?.gameObject;
        }

        private static GameObject FindMatchingObjectByName(string name, GameObject root)
        {
            if (root.name == name) return root;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child.gameObject;
            }

            return null;
        }

        private static bool SerializedPropertyEqual(SerializedProperty a, SerializedProperty b)
        {
            if (a.propertyType != b.propertyType) return false;

            switch (a.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return a.intValue == b.intValue;
                case SerializedPropertyType.Boolean:
                    return a.boolValue == b.boolValue;
                case SerializedPropertyType.Float:
                    return Mathf.Approximately(a.floatValue, b.floatValue);
                case SerializedPropertyType.String:
                    return a.stringValue == b.stringValue;
                case SerializedPropertyType.ObjectReference:
                    return a.objectReferenceValue == b.objectReferenceValue;
                case SerializedPropertyType.Enum:
                    return a.enumValueIndex == b.enumValueIndex;
                case SerializedPropertyType.Vector2:
                    return a.vector2Value == b.vector2Value;
                case SerializedPropertyType.Vector3:
                    return a.vector3Value == b.vector3Value;
                case SerializedPropertyType.Vector4:
                    return a.vector4Value == b.vector4Value;
                case SerializedPropertyType.Quaternion:
                    return a.quaternionValue == b.quaternionValue;
                case SerializedPropertyType.Color:
                    return a.colorValue == b.colorValue;
                default:
                    return true; // Skip complex types for now
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
                    return prop.enumDisplayNames[prop.enumValueIndex];
                case SerializedPropertyType.Vector2:
                    return prop.vector2Value;
                case SerializedPropertyType.Vector3:
                    return prop.vector3Value;
                case SerializedPropertyType.Vector4:
                    return prop.vector4Value;
                case SerializedPropertyType.Quaternion:
                    return prop.quaternionValue;
                case SerializedPropertyType.Color:
                    return prop.colorValue;
                default:
                    return $"[{prop.propertyType}]";
            }
        }

        private static bool IsTransformReference(SerializedProperty prop)
        {
            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                return false;

            var obj = prop.objectReferenceValue;
            return obj is Transform || obj is GameObject;
        }

        #endregion
    }

    #region Data Classes

    /// <summary>
    /// Result of comparing two prefabs.
    /// </summary>
    public class PrefabComparisonResult
    {
        public bool IsValid;
        public string ErrorMessage;
        public GameObject SourceRoot;
        public GameObject TargetRoot;
        public HierarchyDiff Hierarchy;
        public List<ComponentDiff> Components;
        public List<BlendshapeDiff> Blendshapes;
        public List<PropertyDiff> Properties;

        public int TotalDifferences => 
            (Hierarchy?.Count ?? 0) + 
            (Components?.Count ?? 0) + 
            (Blendshapes?.Count ?? 0) + 
            (Properties?.Count ?? 0);

        public bool HasDifferences => TotalDifferences > 0;
    }

    /// <summary>
    /// Differences in GameObject hierarchy.
    /// </summary>
    public class HierarchyDiff
    {
        public List<GameObjectDiff> AddedInSource;
        public List<GameObjectDiff> AddedInTarget;
        public List<GameObjectMatch> Matched;

        public int Count => (AddedInSource?.Count ?? 0) + (AddedInTarget?.Count ?? 0);
    }

    /// <summary>
    /// A GameObject that exists only in source or target.
    /// </summary>
    public class GameObjectDiff
    {
        public string Path;
        public string Name;
        public GameObject Object;
        public List<Component> Components;
    }

    /// <summary>
    /// A matched GameObject pair between source and target.
    /// </summary>
    public class GameObjectMatch
    {
        public string SourcePath;
        public string TargetPath;
        public GameObject SourceObject;
        public GameObject TargetObject;
        public bool IsFuzzyMatch;
        public List<ComponentDiff> ComponentDifferences;
    }

    /// <summary>
    /// Difference in a component.
    /// </summary>
    public class ComponentDiff
    {
        public string ObjectPath;
        public Type ComponentType;
        public DiffType Type;
        public Component SourceComponent;
        public Component TargetComponent;
        public List<PropertyDiff> PropertyDifferences;

        public string DisplayName => ComponentType?.Name ?? "Unknown";
    }

    /// <summary>
    /// Difference in a serialized property.
    /// </summary>
    public class PropertyDiff
    {
        public string PropertyPath;
        public string PropertyDisplayName;
        public object SourceValue;
        public object TargetValue;
        public bool RequiresBoneRemapping;
    }

    /// <summary>
    /// Difference in blendshapes.
    /// </summary>
    public class BlendshapeDiff
    {
        public string SourceMeshName;
        public string SourceBlendshapeName;
        public string TargetMeshName;
        public string TargetBlendshapeName;
        public DiffType Type;
    }

    /// <summary>
    /// Type of difference.
    /// </summary>
    public enum DiffType
    {
        Added,      // Exists in source but not target
        Removed,    // Exists in target but not source
        Modified    // Exists in both but differs
    }

    /// <summary>
    /// Mode for matching GameObjects.
    /// </summary>
    public enum MatchingMode
    {
        ByPath,
        ByName,
        ByIndex,
        Smart
    }

    #endregion
}
