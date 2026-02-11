using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace YUCP.DevTools.Editor.ModelRevision
{
    /// <summary>
    /// Utility for mapping bone hierarchies between different armatures.
    /// Supports humanoid bone matching, name-based matching, and path-based matching.
    /// </summary>
    public static class BoneMappingUtility
    {
        /// <summary>
        /// Common bone name suffixes to strip for matching.
        /// </summary>
        private static readonly string[] BoneNameSuffixes = new[]
        {
            "_end", "_End", "_END",
            "_L", "_R", "_l", "_r",
            ".L", ".R", ".l", ".r",
            "_Left", "_Right", "_left", "_right",
            "Left", "Right"
        };

        /// <summary>
        /// Common bone name variations to normalize.
        /// </summary>
        private static readonly Dictionary<string, string[]> BoneNameAliases = new Dictionary<string, string[]>
        {
            { "hips", new[] { "hip", "pelvis", "root" } },
            { "spine", new[] { "spine1", "spine_01" } },
            { "chest", new[] { "spine2", "spine_02", "upperchest" } },
            { "neck", new[] { "neck1" } },
            { "head", new[] { "head1" } },
            { "shoulder", new[] { "clavicle", "collar" } },
            { "upperarm", new[] { "arm", "upper_arm" } },
            { "lowerarm", new[] { "forearm", "lower_arm" } },
            { "hand", new[] { "wrist" } },
            { "upperleg", new[] { "thigh", "upper_leg" } },
            { "lowerleg", new[] { "calf", "shin", "lower_leg" } },
            { "foot", new[] { "ankle" } }
        };

        /// <summary>
        /// Builds a complete bone path mapping between source and target armatures.
        /// </summary>
        public static Dictionary<string, string> BuildBonePathMapping(
            GameObject source,
            GameObject target,
            YUCP.DevTools.BoneMatchingSettings settings = null)
        {
            settings = settings ?? new YUCP.DevTools.BoneMatchingSettings();
            var result = new Dictionary<string, string>();

            var sourceAnimator = source.GetComponent<Animator>();
            var targetAnimator = target.GetComponent<Animator>();

            // First, map humanoid bones if both have animators with humanoid avatars
            if (sourceAnimator != null && targetAnimator != null &&
                sourceAnimator.isHuman && targetAnimator.isHuman)
            {
                var humanoidMapping = BuildHumanoidBoneMapping(sourceAnimator, targetAnimator);
                foreach (var kvp in humanoidMapping)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            // Then map remaining bones by name
            var sourceTransforms = source.GetComponentsInChildren<Transform>(true);
            var targetTransforms = target.GetComponentsInChildren<Transform>(true);

            foreach (var sourceTransform in sourceTransforms)
            {
                var sourcePath = GetBonePath(sourceTransform, source.transform);
                if (result.ContainsKey(sourcePath)) continue;

                // Skip if filtered out
                if (!PassesFilter(sourceTransform.name, settings)) continue;

                // Find matching bone in target
                var targetTransform = FindMatchingBone(
                    sourceTransform.name,
                    target.transform,
                    settings.mode);

                if (targetTransform != null)
                {
                    var targetPath = GetBonePath(targetTransform, target.transform);
                    result[sourcePath] = targetPath;
                }
            }

            return result;
        }

        /// <summary>
        /// Builds a mapping of humanoid bones between two animators.
        /// </summary>
        public static Dictionary<string, string> BuildHumanoidBoneMapping(
            Animator sourceAnimator,
            Animator targetAnimator)
        {
            var result = new Dictionary<string, string>();

            if (sourceAnimator == null || targetAnimator == null) return result;
            if (!sourceAnimator.isHuman || !targetAnimator.isHuman) return result;

            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                var sourceBone = sourceAnimator.GetBoneTransform(bone);
                var targetBone = targetAnimator.GetBoneTransform(bone);

                if (sourceBone != null && targetBone != null)
                {
                    var sourcePath = GetBonePath(sourceBone, sourceAnimator.transform);
                    var targetPath = GetBonePath(targetBone, targetAnimator.transform);
                    result[sourcePath] = targetPath;
                }
            }

            return result;
        }

        /// <summary>
        /// Gets humanoid bone transforms from an animator.
        /// </summary>
        public static Dictionary<HumanBodyBones, Transform> GetHumanoidBones(Animator animator)
        {
            var result = new Dictionary<HumanBodyBones, Transform>();

            if (animator == null || !animator.isHuman) return result;

            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                var transform = animator.GetBoneTransform(bone);
                if (transform != null)
                {
                    result[bone] = transform;
                }
            }

            return result;
        }

        /// <summary>
        /// Finds a matching bone in the target by name.
        /// </summary>
        public static Transform FindMatchingBone(
            string boneName,
            Transform targetRoot,
            YUCP.DevTools.BoneMatchingMode mode)
        {
            if (string.IsNullOrEmpty(boneName) || targetRoot == null) return null;

            var targetTransforms = targetRoot.GetComponentsInChildren<Transform>(true);

            switch (mode)
            {
                case YUCP.DevTools.BoneMatchingMode.NameBased:
                    return FindByNameExact(boneName, targetTransforms) ??
                           FindByNameNormalized(boneName, targetTransforms);

                case YUCP.DevTools.BoneMatchingMode.PathBased:
                    return targetRoot.Find(boneName);

                case YUCP.DevTools.BoneMatchingMode.Hybrid:
                    // Try path first, then name
                    return targetRoot.Find(boneName) ??
                           FindByNameExact(boneName, targetTransforms) ??
                           FindByNameNormalized(boneName, targetTransforms);

                default:
                    return FindByNameExact(boneName, targetTransforms);
            }
        }

        /// <summary>
        /// Resolves a source bone path to target path using mapping.
        /// </summary>
        public static string ResolveBonePath(
            string sourcePath,
            Dictionary<string, string> pathMapping)
        {
            if (string.IsNullOrEmpty(sourcePath) || pathMapping == null)
                return null;

            // Direct mapping
            if (pathMapping.TryGetValue(sourcePath, out var targetPath))
            {
                return targetPath;
            }

            // Try matching by last segment (bone name only)
            var sourceName = sourcePath.Contains("/")
                ? sourcePath.Substring(sourcePath.LastIndexOf('/') + 1)
                : sourcePath;

            foreach (var kvp in pathMapping)
            {
                var mappedName = kvp.Key.Contains("/")
                    ? kvp.Key.Substring(kvp.Key.LastIndexOf('/') + 1)
                    : kvp.Key;

                if (mappedName == sourceName)
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds a transform-to-transform mapping from path mapping.
        /// </summary>
        public static Dictionary<Transform, Transform> BuildTransformMapping(
            GameObject source,
            GameObject target,
            Dictionary<string, string> pathMapping)
        {
            var result = new Dictionary<Transform, Transform>();

            foreach (var kvp in pathMapping)
            {
                var sourceTransform = source.transform.Find(kvp.Key);
                var targetTransform = target.transform.Find(kvp.Value);

                if (sourceTransform != null && targetTransform != null)
                {
                    result[sourceTransform] = targetTransform;
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the bone path relative to a root transform.
        /// </summary>
        public static string GetBonePath(Transform bone, Transform root)
        {
            if (bone == null || root == null) return "";
            if (bone == root) return "";

            var path = bone.name;
            var current = bone.parent;

            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        /// <summary>
        /// Normalizes a bone name for comparison.
        /// </summary>
        public static string NormalizeBoneName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var normalized = name.ToLowerInvariant();

            // Remove common suffixes
            foreach (var suffix in BoneNameSuffixes)
            {
                if (normalized.EndsWith(suffix.ToLowerInvariant()))
                {
                    normalized = normalized.Substring(0, normalized.Length - suffix.Length);
                }
            }

            // Remove underscores and dots
            normalized = normalized.Replace("_", "").Replace(".", "");

            return normalized;
        }

        /// <summary>
        /// Finds all bones that match a filter.
        /// </summary>
        public static List<Transform> FindBonesByFilter(
            Transform root,
            YUCP.DevTools.BoneMatchingSettings settings)
        {
            var result = new List<Transform>();
            var allTransforms = root.GetComponentsInChildren<Transform>(true);

            foreach (var transform in allTransforms)
            {
                if (PassesFilter(transform.name, settings))
                {
                    result.Add(transform);
                }
            }

            return result;
        }

        #region Private Helpers

        private static Transform FindByNameExact(string name, Transform[] transforms)
        {
            foreach (var t in transforms)
            {
                if (t.name == name)
                    return t;
            }
            return null;
        }

        private static Transform FindByNameNormalized(string name, Transform[] transforms)
        {
            var normalizedSearch = NormalizeBoneName(name);

            // First try exact normalized match
            foreach (var t in transforms)
            {
                if (NormalizeBoneName(t.name) == normalizedSearch)
                    return t;
            }

            // Try alias matching
            foreach (var alias in BoneNameAliases)
            {
                if (normalizedSearch.Contains(alias.Key))
                {
                    foreach (var variant in alias.Value)
                    {
                        var variantSearch = normalizedSearch.Replace(alias.Key, variant);
                        foreach (var t in transforms)
                        {
                            if (NormalizeBoneName(t.name) == variantSearch)
                                return t;
                        }
                    }
                }
            }

            return null;
        }

        private static bool PassesFilter(string boneName, YUCP.DevTools.BoneMatchingSettings settings)
        {
            if (settings == null) return true;

            var lowerName = boneName.ToLowerInvariant();

            // Include filter (if set, must match)
            if (!string.IsNullOrEmpty(settings.includeNameFilter))
            {
                var includeFilters = settings.includeNameFilter.Split(',')
                    .Select(f => f.Trim().ToLowerInvariant())
                    .Where(f => !string.IsNullOrEmpty(f));

                bool matches = false;
                foreach (var filter in includeFilters)
                {
                    if (lowerName.Contains(filter))
                    {
                        matches = true;
                        break;
                    }
                }
                if (!matches) return false;
            }

            // Exclude filter
            if (!string.IsNullOrEmpty(settings.excludeNameFilter))
            {
                var excludeFilters = settings.excludeNameFilter.Split(',')
                    .Select(f => f.Trim().ToLowerInvariant())
                    .Where(f => !string.IsNullOrEmpty(f));

                foreach (var filter in excludeFilters)
                {
                    if (lowerName.Contains(filter))
                        return false;
                }
            }

            return true;
        }

        #endregion
    }
}
