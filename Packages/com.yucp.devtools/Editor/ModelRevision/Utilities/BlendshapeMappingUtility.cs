using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace YUCP.DevTools.Editor.ModelRevision
{
    /// <summary>
    /// Utility for mapping and synchronizing blendshapes between different meshes.
    /// Supports auto-mapping by name similarity and manual overrides.
    /// </summary>
    public static class BlendshapeMappingUtility
    {
        /// <summary>
        /// VRC Viseme blendshape names in order.
        /// </summary>
        public static readonly string[] VisemeNames = new[]
        {
            "vrc.v_sil", "vrc.v_pp", "vrc.v_ff", "vrc.v_th", "vrc.v_dd",
            "vrc.v_kk", "vrc.v_ch", "vrc.v_ss", "vrc.v_nn", "vrc.v_rr",
            "vrc.v_aa", "vrc.v_e", "vrc.v_ih", "vrc.v_oh", "vrc.v_ou"
        };

        /// <summary>
        /// Common viseme name patterns.
        /// </summary>
        private static readonly Dictionary<string, string[]> VisemePatterns = new Dictionary<string, string[]>
        {
            { "vrc.v_sil", new[] { "sil", "silence", "neutral", "rest" } },
            { "vrc.v_pp", new[] { "pp", "m", "b", "p" } },
            { "vrc.v_ff", new[] { "ff", "f", "v" } },
            { "vrc.v_th", new[] { "th" } },
            { "vrc.v_dd", new[] { "dd", "d", "n", "t" } },
            { "vrc.v_kk", new[] { "kk", "k", "g" } },
            { "vrc.v_ch", new[] { "ch", "j", "sh" } },
            { "vrc.v_ss", new[] { "ss", "s", "z" } },
            { "vrc.v_nn", new[] { "nn" } },
            { "vrc.v_rr", new[] { "rr", "r" } },
            { "vrc.v_aa", new[] { "aa", "a" } },
            { "vrc.v_e", new[] { "e" } },
            { "vrc.v_ih", new[] { "ih", "i" } },
            { "vrc.v_oh", new[] { "oh", "o" } },
            { "vrc.v_ou", new[] { "ou", "u" } }
        };

        /// <summary>
        /// Gets all blendshapes from all SkinnedMeshRenderers in a GameObject hierarchy.
        /// </summary>
        public static List<BlendshapeInfo> GetAllBlendshapes(GameObject root)
        {
            var result = new List<BlendshapeInfo>();
            if (root == null) return result;

            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var renderer in renderers)
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;

                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    result.Add(new BlendshapeInfo
                    {
                        Name = mesh.GetBlendShapeName(i),
                        MeshName = renderer.name,
                        Index = i,
                        Renderer = renderer
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Gets blendshape names from a specific SkinnedMeshRenderer.
        /// </summary>
        public static List<string> GetBlendshapeNames(SkinnedMeshRenderer renderer)
        {
            var result = new List<string>();
            if (renderer == null || renderer.sharedMesh == null) return result;

            var mesh = renderer.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                result.Add(mesh.GetBlendShapeName(i));
            }

            return result;
        }

        /// <summary>
        /// Compares blendshapes between source and target GameObjects.
        /// </summary>
        public static BlendshapeComparisonResult CompareBlendshapes(GameObject source, GameObject target)
        {
            var result = new BlendshapeComparisonResult
            {
                ExactMatches = new List<BlendshapeMatch>(),
                SimilarMatches = new List<BlendshapeMatch>(),
                SourceOnly = new List<string>(),
                TargetOnly = new List<string>()
            };

            var sourceBlendshapes = GetAllBlendshapes(source);
            var targetBlendshapes = GetAllBlendshapes(target);

            var sourceNames = sourceBlendshapes.Select(b => b.Name).Distinct().ToList();
            var targetNames = targetBlendshapes.Select(b => b.Name).Distinct().ToList();
            var targetNamesSet = new HashSet<string>(targetNames);
            var matchedTargets = new HashSet<string>();

            foreach (var sourceName in sourceNames)
            {
                if (targetNamesSet.Contains(sourceName))
                {
                    result.ExactMatches.Add(new BlendshapeMatch
                    {
                        SourceName = sourceName,
                        TargetName = sourceName,
                        Confidence = 1.0f,
                        MatchType = BlendshapeMatchType.Exact
                    });
                    matchedTargets.Add(sourceName);
                }
                else
                {
                    // Try to find similar name
                    var bestMatch = FindBestMatch(sourceName, targetNames, matchedTargets);
                    if (bestMatch.HasValue && bestMatch.Value.confidence >= 0.6f)
                    {
                        result.SimilarMatches.Add(new BlendshapeMatch
                        {
                            SourceName = sourceName,
                            TargetName = bestMatch.Value.name,
                            Confidence = bestMatch.Value.confidence,
                            MatchType = BlendshapeMatchType.Similar
                        });
                        matchedTargets.Add(bestMatch.Value.name);
                    }
                    else
                    {
                        result.SourceOnly.Add(sourceName);
                    }
                }
            }

            // Find target-only
            foreach (var targetName in targetNames)
            {
                if (!matchedTargets.Contains(targetName))
                {
                    result.TargetOnly.Add(targetName);
                }
            }

            return result;
        }

        /// <summary>
        /// Auto-maps blendshapes by name similarity.
        /// </summary>
        public static List<YUCP.DevTools.BlendshapeMapping> AutoMapBlendshapes(
            List<string> sourceNames,
            List<string> targetNames,
            float minConfidence = 0.7f)
        {
            var result = new List<YUCP.DevTools.BlendshapeMapping>();
            var usedTargets = new HashSet<string>();

            foreach (var sourceName in sourceNames)
            {
                // First check exact match
                if (targetNames.Contains(sourceName) && !usedTargets.Contains(sourceName))
                {
                    result.Add(new YUCP.DevTools.BlendshapeMapping
                    {
                        sourceName = sourceName,
                        targetName = sourceName,
                        confidence = 1.0f,
                        status = YUCP.DevTools.MappingStatus.Mapped
                    });
                    usedTargets.Add(sourceName);
                    continue;
                }

                // Try similarity matching
                var bestMatch = FindBestMatch(sourceName, targetNames, usedTargets);
                if (bestMatch.HasValue && bestMatch.Value.confidence >= minConfidence)
                {
                    result.Add(new YUCP.DevTools.BlendshapeMapping
                    {
                        sourceName = sourceName,
                        targetName = bestMatch.Value.name,
                        confidence = bestMatch.Value.confidence,
                        status = YUCP.DevTools.MappingStatus.Mapped
                    });
                    usedTargets.Add(bestMatch.Value.name);
                }
                else
                {
                    // No match found
                    result.Add(new YUCP.DevTools.BlendshapeMapping
                    {
                        sourceName = sourceName,
                        targetName = "",
                        confidence = 0f,
                        status = YUCP.DevTools.MappingStatus.MissingInTarget
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Calculates string similarity using Levenshtein distance.
        /// Returns value between 0 (no match) and 1 (identical).
        /// </summary>
        public static float CalculateSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0f;

            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();

            if (a == b) return 1f;

            int distance = LevenshteinDistance(a, b);
            int maxLength = Math.Max(a.Length, b.Length);

            return 1f - (float)distance / maxLength;
        }

        /// <summary>
        /// Checks if a blendshape name is a VRC viseme.
        /// </summary>
        public static bool IsVisemeBlendshape(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            var lower = name.ToLowerInvariant();
            return lower.StartsWith("vrc.v_") || 
                   lower.StartsWith("v_") ||
                   lower.Contains("viseme");
        }

        /// <summary>
        /// Gets the VRC viseme index from a blendshape name.
        /// Returns -1 if not a viseme.
        /// </summary>
        public static int GetVisemeIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;

            var lower = name.ToLowerInvariant();

            for (int i = 0; i < VisemeNames.Length; i++)
            {
                if (lower == VisemeNames[i] || lower.EndsWith(VisemeNames[i].Substring(4))) // Remove "vrc."
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Syncs blendshape values from source to target using mappings.
        /// </summary>
        public static void SyncBlendshapeValues(
            SkinnedMeshRenderer source,
            SkinnedMeshRenderer target,
            List<YUCP.DevTools.BlendshapeMapping> mappings)
        {
            if (source == null || target == null || source.sharedMesh == null || target.sharedMesh == null)
                return;

            var sourceMesh = source.sharedMesh;
            var targetMesh = target.sharedMesh;

            foreach (var mapping in mappings)
            {
                if (mapping.status != YUCP.DevTools.MappingStatus.Mapped)
                    continue;

                int sourceIndex = sourceMesh.GetBlendShapeIndex(mapping.sourceName);
                int targetIndex = targetMesh.GetBlendShapeIndex(mapping.targetName);

                if (sourceIndex >= 0 && targetIndex >= 0)
                {
                    float value = source.GetBlendShapeWeight(sourceIndex);
                    target.SetBlendShapeWeight(targetIndex, value);
                }
            }
        }

        /// <summary>
        /// Creates viseme mappings based on common naming patterns.
        /// </summary>
        public static List<YUCP.DevTools.BlendshapeMapping> CreateVisemeMappings(
            List<string> sourceNames,
            List<string> targetNames)
        {
            var result = new List<YUCP.DevTools.BlendshapeMapping>();

            foreach (var viseme in VisemeNames)
            {
                var sourceMatch = FindVisemeMatch(viseme, sourceNames);
                var targetMatch = FindVisemeMatch(viseme, targetNames);

                if (!string.IsNullOrEmpty(sourceMatch))
                {
                    result.Add(new YUCP.DevTools.BlendshapeMapping
                    {
                        sourceName = sourceMatch,
                        targetName = targetMatch ?? "",
                        confidence = string.IsNullOrEmpty(targetMatch) ? 0f : 1f,
                        status = string.IsNullOrEmpty(targetMatch) 
                            ? YUCP.DevTools.MappingStatus.MissingInTarget 
                            : YUCP.DevTools.MappingStatus.Mapped
                    });
                }
            }

            return result;
        }

        #region Private Helpers

        private static (string name, float confidence)? FindBestMatch(
            string sourceName,
            List<string> targetNames,
            HashSet<string> usedTargets)
        {
            float bestConfidence = 0f;
            string bestMatch = null;

            foreach (var targetName in targetNames)
            {
                if (usedTargets.Contains(targetName)) continue;

                float confidence = CalculateSimilarity(sourceName, targetName);
                if (confidence > bestConfidence)
                {
                    bestConfidence = confidence;
                    bestMatch = targetName;
                }
            }

            if (bestMatch != null && bestConfidence > 0)
            {
                return (bestMatch, bestConfidence);
            }

            return null;
        }

        private static string FindVisemeMatch(string viseme, List<string> names)
        {
            var visemeLower = viseme.ToLowerInvariant();

            // First try exact match
            foreach (var name in names)
            {
                if (name.ToLowerInvariant() == visemeLower)
                    return name;
            }

            // Try pattern matching
            if (VisemePatterns.TryGetValue(viseme, out var patterns))
            {
                foreach (var pattern in patterns)
                {
                    foreach (var name in names)
                    {
                        var nameLower = name.ToLowerInvariant();
                        if (nameLower.Contains(pattern) || nameLower.EndsWith("_" + pattern))
                            return name;
                    }
                }
            }

            return null;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];

            for (int i = 0; i <= a.Length; i++)
                d[i, 0] = i;

            for (int j = 0; j <= b.Length; j++)
                d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[a.Length, b.Length];
        }

        #endregion
    }

    #region Data Classes

    /// <summary>
    /// Information about a blendshape.
    /// </summary>
    public class BlendshapeInfo
    {
        public string Name;
        public string MeshName;
        public int Index;
        public SkinnedMeshRenderer Renderer;
    }

    /// <summary>
    /// Result of comparing blendshapes between two objects.
    /// </summary>
    public class BlendshapeComparisonResult
    {
        public List<BlendshapeMatch> ExactMatches;
        public List<BlendshapeMatch> SimilarMatches;
        public List<string> SourceOnly;
        public List<string> TargetOnly;

        public int TotalMatches => ExactMatches.Count + SimilarMatches.Count;
        public int TotalUnmatched => SourceOnly.Count + TargetOnly.Count;
    }

    /// <summary>
    /// A match between source and target blendshapes.
    /// </summary>
    public class BlendshapeMatch
    {
        public string SourceName;
        public string TargetName;
        public float Confidence;
        public BlendshapeMatchType MatchType;
    }

    /// <summary>
    /// Type of blendshape match.
    /// </summary>
    public enum BlendshapeMatchType
    {
        Exact,
        Similar,
        Manual
    }

    #endregion
}
