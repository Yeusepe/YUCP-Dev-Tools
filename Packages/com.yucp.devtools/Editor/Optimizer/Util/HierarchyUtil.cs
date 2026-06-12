using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.Optimizer.Util
{
    /// <summary>
    /// Helpers for addressing transforms by root-relative path, so references survive the clone step
    /// (the optimizer always operates on a duplicate of the user's selection).
    /// </summary>
    public static class HierarchyUtil
    {
        /// <summary>Returns the "/"-separated path from <paramref name="root"/> to <paramref name="target"/>, or "" if equal/unrelated.</summary>
        public static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || target == root)
                return string.Empty;

            var segments = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            if (current != root)
                return string.Empty; // target is not a descendant of root

            segments.Reverse();
            return string.Join("/", segments);
        }

        /// <summary>Resolves a root-relative path produced by <see cref="GetRelativePath"/> back to a transform.</summary>
        public static Transform FindByPath(Transform root, string relativePath)
        {
            if (root == null)
                return null;
            if (string.IsNullOrEmpty(relativePath))
                return root;

            var current = root;
            foreach (var name in relativePath.Split('/'))
            {
                current = current.Find(name);
                if (current == null)
                    return null;
            }
            return current;
        }
    }
}
