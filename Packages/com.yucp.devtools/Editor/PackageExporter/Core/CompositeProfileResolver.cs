using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Resolves composite export profiles, handles cycle detection, and merges asset lists.
    /// </summary>
    public static class CompositeProfileResolver
    {
        /// <summary>
        /// Profile status for validation
        /// </summary>
        public enum ProfileStatus
        {
            Valid,
            HasErrors,
            Missing,
            Cycle
        }
        
        /// <summary>
        /// Resolve all included profiles recursively, detecting cycles.
        /// </summary>
        /// <param name="root">Root profile to start resolution from</param>
        /// <param name="resolved">Output: Flat list of all included profiles (no duplicates)</param>
        /// <param name="cycles">Output: List of cycle paths found (e.g., "Clothes → Pants → Clothes")</param>
        /// <returns>True if resolution succeeded without cycles</returns>
        public static bool ResolveIncludedProfiles(ExportProfile root, out List<ExportProfile> resolved, out List<string> cycles)
        {
            resolved = new List<ExportProfile>();
            cycles = new List<string>();
            
            if (root == null || !root.HasIncludedProfiles())
            {
                return true;
            }
            
            var visited = new HashSet<ExportProfile>();
            var recursionStack = new HashSet<ExportProfile>();
            var profilePath = new List<string>();
            
            bool hasCycles = false;
            var resolvedList = new List<ExportProfile>();
            var cyclesList = new List<string>();
            
            void ResolveRecursive(ExportProfile profile, List<string> currentPath)
            {
                if (profile == null)
                    return;
                
                // Check for cycle
                if (recursionStack.Contains(profile))
                {
                    // Found a cycle - build cycle path string
                    int cycleStart = currentPath.IndexOf(profile.packageName);
                    if (cycleStart >= 0)
                    {
                        var cyclePath = currentPath.Skip(cycleStart).Concat(new[] { profile.packageName });
                        cyclesList.Add(string.Join(" → ", cyclePath));
                    }
                    else
                    {
                        cyclesList.Add(string.Join(" → ", currentPath) + " → " + profile.packageName);
                    }
                    hasCycles = true;
                    return;
                }
                
                // Check if already visited (prevents duplicate processing)
                if (visited.Contains(profile))
                    return;
                
                // Mark as being processed
                recursionStack.Add(profile);
                visited.Add(profile);
                currentPath.Add(profile.packageName);
                
                // Process included profiles
                var included = profile.GetIncludedProfiles();
                foreach (var includedProfile in included)
                {
                    if (includedProfile != null)
                    {
                        ResolveRecursive(includedProfile, new List<string>(currentPath));
                    }
                }
                
                // Remove from recursion stack
                recursionStack.Remove(profile);
                
                // Add to resolved list (only if not already there)
                if (!resolvedList.Contains(profile))
                {
                    resolvedList.Add(profile);
                }
            }
            
            // Start resolution from root's included profiles
            var rootIncluded = root.GetIncludedProfiles();
            foreach (var includedProfile in rootIncluded)
            {
                if (includedProfile != null)
                {
                    ResolveRecursive(includedProfile, new List<string> { root.packageName });
                }
            }
            
            resolved = resolvedList;
            cycles = cyclesList;
            return !hasCycles;
        }
        
        /// <summary>
        /// Merge asset lists from parent and all included profiles.
        /// Applies each profile's ignore rules, then applies parent's exclude rules last.
        /// </summary>
        /// <param name="parent">Parent profile</param>
        /// <param name="includedProfiles">List of included profiles to merge</param>
        /// <param name="assetSourceMap">Output: Map of asset path → source profile name</param>
        /// <returns>Unified list of assets to export</returns>
        public static List<string> MergeAssetLists(ExportProfile parent, List<ExportProfile> includedProfiles, out Dictionary<string, string> assetSourceMap)
        {
            assetSourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Collect assets from each included profile
            foreach (var includedProfile in includedProfiles)
            {
                if (includedProfile == null)
                    continue;
                
                // Use PackageBuilder's method to collect assets (respects profile's ignore rules)
                List<string> includedAssets = PackageBuilder.CollectAssetsToExport(includedProfile);
                
                // Add to unified list and track source
                foreach (string assetPath in includedAssets)
                {
                    // Normalize path for comparison
                    string normalizedPath = NormalizeAssetPath(assetPath);
                    
                    if (!string.IsNullOrEmpty(normalizedPath))
                    {
                        // Only add if not already present (parent wins on conflicts)
                        if (!allAssets.Contains(normalizedPath))
                        {
                            allAssets.Add(normalizedPath);
                            assetSourceMap[normalizedPath] = includedProfile.packageName;
                        }
                    }
                }
            }
            
            // Collect parent profile's assets
            List<string> parentAssets = PackageBuilder.CollectAssetsToExport(parent);
            foreach (string assetPath in parentAssets)
            {
                string normalizedPath = NormalizeAssetPath(assetPath);
                
                if (!string.IsNullOrEmpty(normalizedPath))
                {
                    // Parent assets always override included assets
                    allAssets.Add(normalizedPath);
                    assetSourceMap[normalizedPath] = parent.packageName;
                }
            }
            
            // Apply parent's exclude rules LAST (so parent can override included profiles)
            var finalAssets = new List<string>();
            foreach (string assetPath in allAssets)
            {
                if (!PackageBuilder.ShouldExcludeAsset(assetPath, parent))
                {
                    finalAssets.Add(assetPath);
                }
            }
            
            return finalAssets;
        }
        
        /// <summary>
        /// Detect asset conflicts (same asset in multiple profiles)
        /// </summary>
        /// <param name="allAssets">List of all assets</param>
        /// <param name="sourceMap">Map of asset path → source profile name</param>
        /// <returns>List of conflict descriptions</returns>
        public static List<string> DetectAssetConflicts(List<string> allAssets, Dictionary<string, string> sourceMap)
        {
            var conflicts = new List<string>();
            var assetCounts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            
            // Count occurrences of each asset
            foreach (var kvp in sourceMap)
            {
                string assetPath = kvp.Key;
                string sourceProfile = kvp.Value;
                
                if (!assetCounts.ContainsKey(assetPath))
                {
                    assetCounts[assetPath] = new List<string>();
                }
                assetCounts[assetPath].Add(sourceProfile);
            }
            
            // Find conflicts (assets in multiple profiles)
            foreach (var kvp in assetCounts)
            {
                if (kvp.Value.Count > 1)
                {
                    conflicts.Add($"{kvp.Key} appears in: {string.Join(", ", kvp.Value)}");
                }
            }
            
            return conflicts;
        }
        
        /// <summary>
        /// Validate included profiles for a given profile
        /// </summary>
        /// <param name="profile">Profile to validate</param>
        /// <param name="errors">Output: List of error messages</param>
        /// <returns>True if validation passed</returns>
        public static bool ValidateIncludedProfiles(ExportProfile profile, out List<string> errors)
        {
            errors = new List<string>();
            
            if (profile == null || !profile.HasIncludedProfiles())
            {
                return true;
            }
            
            // Check for cycles
            List<ExportProfile> resolved;
            List<string> cycles;
            if (!ResolveIncludedProfiles(profile, out resolved, out cycles))
            {
                foreach (string cycle in cycles)
                {
                    errors.Add($"Cycle detected: {cycle}");
                }
            }
            
            // Check for missing profiles
            var includedGuids = profile.GetIncludedProfiles();
            var allProfiles = GetAllExportProfiles();
            var missingProfiles = new List<string>();
            
            // Check for missing profiles by comparing resolved profiles with GUIDs
            var resolvedProfiles = profile.GetIncludedProfiles();
            var resolvedGuids = new HashSet<string>();
            foreach (var resolvedProfile in resolvedProfiles)
            {
                if (resolvedProfile != null)
                {
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(resolvedProfile));
                    if (!string.IsNullOrEmpty(guid))
                    {
                        resolvedGuids.Add(guid);
                    }
                }
            }
            
            // Get GUIDs from profile using reflection
            var profileType = typeof(ExportProfile);
            var guidField = profileType.GetField("includedProfileGuids", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (guidField != null)
            {
                var guids = guidField.GetValue(profile) as List<string>;
                if (guids != null)
                {
                    foreach (string guid in guids)
                    {
                        if (string.IsNullOrEmpty(guid))
                            continue;
                        
                        if (!resolvedGuids.Contains(guid))
                        {
                            missingProfiles.Add(guid);
                        }
                    }
                }
            }
            
            if (missingProfiles.Count > 0)
            {
                errors.Add($"{missingProfiles.Count} included profile(s) are missing or deleted");
            }
            
            // Check for included profiles with validation errors
            foreach (var includedProfile in includedGuids)
            {
                if (includedProfile == null)
                    continue;
                
                if (!includedProfile.Validate(out string validationError))
                {
                    errors.Add($"Included profile '{includedProfile.packageName}' has errors: {validationError}");
                }
            }
            
            return errors.Count == 0;
        }
        
        /// <summary>
        /// Get status of a profile (for UI display)
        /// </summary>
        public static ProfileStatus GetProfileStatus(ExportProfile profile)
        {
            if (profile == null)
                return ProfileStatus.Missing;
            
            // Check if profile exists
            string assetPath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(assetPath))
                return ProfileStatus.Missing;
            
            // Check for validation errors
            if (!profile.Validate(out _))
                return ProfileStatus.HasErrors;
            
            return ProfileStatus.Valid;
        }
        
        /// <summary>
        /// Check if adding a profile would create a cycle
        /// </summary>
        public static bool WouldCreateCycle(ExportProfile parent, ExportProfile candidate)
        {
            if (parent == null || candidate == null)
                return false;
            
            // If candidate is the parent, it's a direct cycle
            if (parent == candidate)
                return true;
            
            // Check if candidate includes parent (directly or indirectly)
            List<ExportProfile> resolved;
            List<string> cycles;
            
            // Temporarily add candidate to parent and check for cycles
            parent.AddIncludedProfile(candidate);
            bool hasCycle = !ResolveIncludedProfiles(parent, out resolved, out cycles);
            parent.RemoveIncludedProfile(candidate);
            
            return hasCycle;
        }
        
        /// <summary>
        /// Get all export profiles in the project
        /// </summary>
        public static List<ExportProfile> GetAllExportProfiles()
        {
            string[] guids = AssetDatabase.FindAssets("t:ExportProfile");
            var profiles = new List<ExportProfile>();
            
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<ExportProfile>(path);
                if (profile != null)
                {
                    profiles.Add(profile);
                }
            }
            
            return profiles;
        }
        
        /// <summary>
        /// Normalize asset path for comparison (handles both Unity-relative and full paths)
        /// </summary>
        private static string NormalizeAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return assetPath;
            
            // If already Unity-relative, return as-is
            if (assetPath.StartsWith("Assets/") || assetPath.StartsWith("Packages/"))
            {
                return assetPath.Replace('\\', '/');
            }
            
            // Convert full path to Unity-relative if possible
            string projectPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            string fullPath = System.IO.Path.GetFullPath(assetPath);
            
            if (fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
            {
                string relative = fullPath.Substring(projectPath.Length);
                if (relative.StartsWith("\\") || relative.StartsWith("/"))
                {
                    relative = relative.Substring(1);
                }
                return relative.Replace('\\', '/');
            }
            
            // Return normalized full path
            return fullPath.Replace('\\', '/');
        }
    }
}

