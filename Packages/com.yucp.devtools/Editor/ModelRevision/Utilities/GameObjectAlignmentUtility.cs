using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace YUCP.DevTools.Editor.ModelRevision
{
    /// <summary>
    /// Utility for aligning GameObject hierarchies between prefabs.
    /// Handles adding, removing, and reparenting GameObjects.
    /// </summary>
    public static class GameObjectAlignmentUtility
    {
        /// <summary>
        /// Adds missing GameObjects from source to target based on diff.
        /// </summary>
        public static List<GameObject> AddMissingObjects(
            List<GameObjectDiff> missing,
            GameObject targetRoot,
            Dictionary<string, string> pathMapping = null)
        {
            var created = new List<GameObject>();

            foreach (var diff in missing.OrderBy(d => d.Path.Count(c => c == '/')))
            {
                var targetPath = pathMapping != null && pathMapping.TryGetValue(diff.Path, out var mapped)
                    ? mapped
                    : diff.Path;

                var newObj = CreateObjectAtPath(targetRoot, targetPath, diff.Object);
                if (newObj != null)
                {
                    created.Add(newObj);
                }
            }

            return created;
        }

        /// <summary>
        /// Removes extra GameObjects from target.
        /// </summary>
        public static void RemoveExtraObjects(List<GameObjectDiff> extra)
        {
            foreach (var diff in extra.OrderByDescending(d => d.Path.Count(c => c == '/')))
            {
                if (diff.Object != null)
                {
                    Undo.DestroyObjectImmediate(diff.Object);
                }
            }
        }

        /// <summary>
        /// Renames objects in target to match source naming.
        /// </summary>
        public static void AlignObjectNames(List<GameObjectMatch> matched, bool useSourceNames)
        {
            foreach (var match in matched)
            {
                if (match.IsFuzzyMatch && useSourceNames)
                {
                    Undo.RecordObject(match.TargetObject, "Rename Object");
                    match.TargetObject.name = match.SourceObject.name;
                }
            }
        }

        /// <summary>
        /// Creates a new child GameObject at the specified path.
        /// </summary>
        public static GameObject CreateObjectAtPath(GameObject root, string path, GameObject template = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                return root;
            }

            var parts = path.Split('/');
            var current = root.transform;

            for (int i = 0; i < parts.Length; i++)
            {
                var childName = parts[i];
                var child = current.Find(childName);

                if (child == null)
                {
                    // Create new child
                    GameObject newChild;

                    if (i == parts.Length - 1 && template != null)
                    {
                        // Use template for final object
                        newChild = new GameObject(childName);
                        CopyTransformData(template.transform, newChild.transform);
                    }
                    else
                    {
                        newChild = new GameObject(childName);
                    }

                    Undo.RegisterCreatedObjectUndo(newChild, "Create Object");
                    newChild.transform.SetParent(current, false);
                    child = newChild.transform;
                }

                current = child;
            }

            return current.gameObject;
        }

        /// <summary>
        /// Moves/reparents a GameObject to match source structure.
        /// </summary>
        public static void ReparentObject(GameObject obj, string newParentPath, GameObject root)
        {
            if (obj == null || root == null) return;

            Transform newParent;
            if (string.IsNullOrEmpty(newParentPath))
            {
                newParent = root.transform;
            }
            else
            {
                var parentObj = CreateObjectAtPath(root, newParentPath);
                newParent = parentObj.transform;
            }

            Undo.SetTransformParent(obj.transform, newParent, "Reparent Object");
        }

        /// <summary>
        /// Aligns transform data (position, rotation, scale) from source to target.
        /// </summary>
        public static void AlignTransform(Transform source, Transform target, TransformAlignmentSettings settings)
        {
            if (source == null || target == null) return;

            Undo.RecordObject(target, "Align Transform");

            if (settings.AlignLocalPosition)
            {
                target.localPosition = source.localPosition + settings.PositionOffset;
            }

            if (settings.AlignLocalRotation)
            {
                target.localRotation = source.localRotation * Quaternion.Euler(settings.RotationOffset);
            }

            if (settings.AlignLocalScale)
            {
                target.localScale = Vector3.Scale(source.localScale, settings.ScaleMultiplier);
            }
        }

        /// <summary>
        /// Aligns all transforms in matched objects.
        /// </summary>
        public static void AlignAllTransforms(List<GameObjectMatch> matched, TransformAlignmentSettings settings)
        {
            foreach (var match in matched)
            {
                AlignTransform(match.SourceObject.transform, match.TargetObject.transform, settings);
            }
        }

        /// <summary>
        /// Gets the relative path of an object from a root.
        /// </summary>
        public static string GetRelativePath(Transform obj, Transform root)
        {
            if (obj == root) return "";

            var path = obj.name;
            var current = obj.parent;

            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        /// <summary>
        /// Finds all transforms matching a name pattern.
        /// </summary>
        public static List<Transform> FindTransformsByName(GameObject root, string namePattern, bool exactMatch = true)
        {
            var result = new List<Transform>();
            var allTransforms = root.GetComponentsInChildren<Transform>(true);

            foreach (var t in allTransforms)
            {
                if (exactMatch)
                {
                    if (t.name == namePattern)
                        result.Add(t);
                }
                else
                {
                    if (t.name.Contains(namePattern))
                        result.Add(t);
                }
            }

            return result;
        }

        #region Private Helpers

        private static void CopyTransformData(Transform source, Transform target)
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }

        #endregion
    }

    /// <summary>
    /// Settings for transform alignment.
    /// </summary>
    [Serializable]
    public class TransformAlignmentSettings
    {
        public bool AlignLocalPosition = true;
        public bool AlignLocalRotation = true;
        public bool AlignLocalScale = true;
        public Vector3 PositionOffset = Vector3.zero;
        public Vector3 RotationOffset = Vector3.zero;
        public Vector3 ScaleMultiplier = Vector3.one;

        public static TransformAlignmentSettings Default => new TransformAlignmentSettings();
    }
}
