using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Scans and manages version bumping across project files
    /// </summary>
    public static class ProjectVersionScanner
    {
        /// <summary>
        /// Default file patterns to scan for version directives
        /// </summary>
        private static readonly string[] DefaultScanPatterns = new[]
        {
            "*.cs",
            "*.txt",
            "*.md",
            "*.json",
            "*.xml",
            "*.asmdef",
            "*.yml",
            "*.yaml"
        };

        /// <summary>
        /// Scan project for all files containing version bump directives
        /// </summary>
        public static List<string> ScanProject(string[] additionalPatterns = null)
        {
            var patterns = DefaultScanPatterns.ToList();
            if (additionalPatterns != null)
            {
                patterns.AddRange(additionalPatterns);
            }

            var allFiles = new List<string>();
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            foreach (var pattern in patterns)
            {
                var files = SmartVersionBumper.FindFilesWithDirectives(projectRoot, pattern, recursive: true);
                allFiles.AddRange(files);
            }

            // Remove duplicates
            return allFiles.Distinct().ToList();
        }

        /// <summary>
        /// Scan specific folders for version directives
        /// </summary>
        public static List<string> ScanFolders(List<string> folders, string[] patterns = null)
        {
            var allFiles = new List<string>();
            patterns = patterns ?? DefaultScanPatterns;

            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                    continue;

                foreach (var pattern in patterns)
                {
                    var files = SmartVersionBumper.FindFilesWithDirectives(folder, pattern, recursive: true);
                    allFiles.AddRange(files);
                }
            }

            return allFiles.Distinct().ToList();
        }

        /// <summary>
        /// Bump versions in export profile's folders
        /// </summary>
        public static List<SmartVersionBumper.BumpResult> BumpVersionsInProfile(
            ExportProfile profile,
            bool writeBack = false,
            VersionBumpOptions defaultOptions = null)
        {
            if (profile == null || profile.foldersToExport == null || profile.foldersToExport.Count == 0)
            {
                Debug.LogWarning("[ProjectVersionScanner] No folders to scan in profile");
                return new List<SmartVersionBumper.BumpResult>();
            }

            // Scan folders for files with directives
            var filesToBump = ScanFolders(profile.foldersToExport);
            
            if (filesToBump.Count == 0)
            {
                return new List<SmartVersionBumper.BumpResult>();
            }


            // Bump all versions
            var results = SmartVersionBumper.BumpMultipleFiles(filesToBump, writeBack, defaultOptions);

            if (writeBack)
            {
                AssetDatabase.Refresh();
            }

            return results;
        }

        /// <summary>
        /// Bump a single version string using a specific rule
        /// </summary>
        public static string BumpVersion(string version, string ruleName = "semver", VersionBumpOptions options = null)
        {
            return VersionUtility.BumpVersionWithRule(version, ruleName, options);
        }

        /// <summary>
        /// Get a preview of what would be bumped in a profile
        /// </summary>
        public static List<SmartVersionBumper.BumpResult> PreviewBumpInProfile(
            ExportProfile profile,
            VersionBumpOptions defaultOptions = null)
        {
            return BumpVersionsInProfile(profile, writeBack: false, defaultOptions: defaultOptions);
        }

        /// <summary>
        /// Bump versions in specific files by rule (no directive needed)
        /// </summary>
        public static List<SmartVersionBumper.BumpResult> BumpFilesWithRule(
            List<string> files,
            string ruleName,
            bool writeBack = false,
            bool bumpAll = false,
            VersionBumpOptions options = null)
        {
            var allResults = new List<SmartVersionBumper.BumpResult>();

            foreach (var file in files)
            {
                var results = SmartVersionBumper.BumpFileWithRule(file, ruleName, writeBack, bumpAll, options);
                allResults.AddRange(results);
            }

            if (writeBack)
            {
                AssetDatabase.Refresh();
            }

            return allResults;
        }

        /// <summary>
        /// Find all ExportProfile assets in the project
        /// </summary>
        public static List<ExportProfile> FindAllProfiles()
        {
            var profiles = new List<ExportProfile>();
            var guids = AssetDatabase.FindAssets("t:ExportProfile");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<ExportProfile>(path);
                if (profile != null)
                {
                    profiles.Add(profile);
                }
            }

            return profiles;
        }

        /// <summary>
        /// Bump versions in all export profiles
        /// </summary>
        public static Dictionary<ExportProfile, List<SmartVersionBumper.BumpResult>> BumpAllProfiles(
            bool writeBack = false,
            VersionBumpOptions defaultOptions = null)
        {
            var results = new Dictionary<ExportProfile, List<SmartVersionBumper.BumpResult>>();
            var profiles = FindAllProfiles();

            foreach (var profile in profiles)
            {
                var profileResults = BumpVersionsInProfile(profile, writeBack, defaultOptions);
                results[profile] = profileResults;
            }

            return results;
        }

        /// <summary>
        /// Get statistics about version directives in project
        /// </summary>
        public static VersionDirectiveStats GetProjectStats()
        {
            var stats = new VersionDirectiveStats();
            var files = ScanProject();
            stats.TotalFilesWithDirectives = files.Count;

            foreach (var file in files)
            {
                var results = SmartVersionBumper.PreviewBump(file);
                stats.TotalDirectives += results.Count;
                stats.DirectivesByRule.TryAdd(file, new Dictionary<string, int>());

                foreach (var result in results)
                {
                    if (result.Success && !string.IsNullOrEmpty(result.RuleName))
                    {
                        if (!stats.RuleUsage.ContainsKey(result.RuleName))
                            stats.RuleUsage[result.RuleName] = 0;
                        
                        stats.RuleUsage[result.RuleName]++;
                    }
                }
            }

            return stats;
        }

        /// <summary>
        /// Statistics about version directives in the project
        /// </summary>
        public class VersionDirectiveStats
        {
            public int TotalFilesWithDirectives { get; set; }
            public int TotalDirectives { get; set; }
            public Dictionary<string, int> RuleUsage { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, Dictionary<string, int>> DirectivesByRule { get; set; } = new Dictionary<string, Dictionary<string, int>>();

            public override string ToString()
            {
                var lines = new List<string>
                {
                    "Project Version Directive Statistics:",
                    $"  Files with directives: {TotalFilesWithDirectives}",
                    $"  Total directives: {TotalDirectives}",
                    "  Rule usage:"
                };

                foreach (var kvp in RuleUsage.OrderByDescending(x => x.Value))
                {
                    lines.Add($"    {kvp.Key}: {kvp.Value}");
                }

                return string.Join("\n", lines);
            }
        }
    }
}









