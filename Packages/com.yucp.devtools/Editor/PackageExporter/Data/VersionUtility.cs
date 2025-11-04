using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Version increment strategy
    /// </summary>
    public enum VersionIncrementStrategy
    {
        Patch,    // 1.0.0 -> 1.0.1
        Minor,     // 1.0.0 -> 1.1.0
        Major,     // 1.0.0 -> 2.0.0
        Build      // 1.0.0.0 -> 1.0.0.1 (if 4 parts exist)
    }
    
    /// <summary>
    /// Utility for parsing and incrementing version strings using regex
    /// Supports semantic versioning (semver) and common version formats
    /// </summary>
    public static class VersionUtility
    {
        // Regex patterns for common version formats
        private static readonly Regex SemverPattern = new Regex(@"^(\d+)\.(\d+)\.(\d+)(?:-([\w\.-]+))?(?:\+([\w\.-]+))?$", RegexOptions.Compiled);
        private static readonly Regex SimpleVersionPattern = new Regex(@"^(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?$", RegexOptions.Compiled);
        private static readonly Regex DateVersionPattern = new Regex(@"^(\d{4})\.(\d{1,2})\.(\d{1,2})(?:\.(\d+))?$", RegexOptions.Compiled);
        
        /// <summary>
        /// Parse a version string into components
        /// </summary>
        public static bool TryParseVersion(string versionString, out int major, out int minor, out int patch, out int? build, out string prerelease, out string metadata)
        {
            major = 0;
            minor = 0;
            patch = 0;
            build = null;
            prerelease = null;
            metadata = null;
            
            if (string.IsNullOrWhiteSpace(versionString))
                return false;
            
            versionString = versionString.Trim();
            
            // Try semver pattern first (most common)
            var semverMatch = SemverPattern.Match(versionString);
            if (semverMatch.Success)
            {
                major = int.Parse(semverMatch.Groups[1].Value);
                minor = int.Parse(semverMatch.Groups[2].Value);
                patch = int.Parse(semverMatch.Groups[3].Value);
                
                if (semverMatch.Groups[4].Success)
                    prerelease = semverMatch.Groups[4].Value;
                    
                if (semverMatch.Groups[5].Success)
                    metadata = semverMatch.Groups[5].Value;
                    
                return true;
            }
            
            // Try simple version pattern (1.0 or 1.0.0 or 1.0.0.0)
            var simpleMatch = SimpleVersionPattern.Match(versionString);
            if (simpleMatch.Success)
            {
                major = int.Parse(simpleMatch.Groups[1].Value);
                minor = simpleMatch.Groups[2].Success ? int.Parse(simpleMatch.Groups[2].Value) : 0;
                patch = simpleMatch.Groups[3].Success ? int.Parse(simpleMatch.Groups[3].Value) : 0;
                build = simpleMatch.Groups[4].Success ? (int?)int.Parse(simpleMatch.Groups[4].Value) : null;
                return true;
            }
            
            // Try date version pattern (2024.1.1 or 2024.1.1.5)
            var dateMatch = DateVersionPattern.Match(versionString);
            if (dateMatch.Success)
            {
                major = int.Parse(dateMatch.Groups[1].Value);
                minor = int.Parse(dateMatch.Groups[2].Value);
                patch = int.Parse(dateMatch.Groups[3].Value);
                build = dateMatch.Groups[4].Success ? (int?)int.Parse(dateMatch.Groups[4].Value) : null;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Increment a version string using the specified strategy
        /// </summary>
        public static string IncrementVersion(string versionString, VersionIncrementStrategy strategy)
        {
            if (!TryParseVersion(versionString, out int major, out int minor, out int patch, out int? build, out string prerelease, out string metadata))
            {
                Debug.LogWarning($"[VersionUtility] Could not parse version '{versionString}'. Returning unchanged.");
                return versionString;
            }
            
            switch (strategy)
            {
                case VersionIncrementStrategy.Patch:
                    patch++;
                    break;
                    
                case VersionIncrementStrategy.Minor:
                    minor++;
                    patch = 0;
                    break;
                    
                case VersionIncrementStrategy.Major:
                    major++;
                    minor = 0;
                    patch = 0;
                    break;
                    
                case VersionIncrementStrategy.Build:
                    if (build.HasValue)
                        build = build.Value + 1;
                    else
                        build = 1;
                    break;
            }
            
            // Reconstruct version string preserving original format
            if (!string.IsNullOrEmpty(prerelease) || !string.IsNullOrEmpty(metadata))
            {
                // Semver format with prerelease/metadata
                string result = $"{major}.{minor}.{patch}";
                if (!string.IsNullOrEmpty(prerelease))
                    result += $"-{prerelease}";
                if (!string.IsNullOrEmpty(metadata))
                    result += $"+{metadata}";
                return result;
            }
            
            if (build.HasValue)
            {
                return $"{major}.{minor}.{patch}.{build.Value}";
            }
            
            return $"{major}.{minor}.{patch}";
        }
        
        /// <summary>
        /// Validate a version string format
        /// </summary>
        public static bool IsValidVersion(string versionString)
        {
            if (string.IsNullOrWhiteSpace(versionString))
                return false;
            
            return SemverPattern.IsMatch(versionString.Trim()) ||
                   SimpleVersionPattern.IsMatch(versionString.Trim()) ||
                   DateVersionPattern.IsMatch(versionString.Trim());
        }
        
    /// <summary>
    /// Format version string for display
    /// </summary>
    public static string FormatVersion(string versionString)
    {
        if (!TryParseVersion(versionString, out int major, out int minor, out int patch, out int? build, out string prerelease, out string metadata))
        {
            return versionString;
        }
        
        string result = $"{major}.{minor}.{patch}";
        if (build.HasValue)
            result += $".{build.Value}";
        if (!string.IsNullOrEmpty(prerelease))
            result += $"-{prerelease}";
        if (!string.IsNullOrEmpty(metadata))
            result += $"+{metadata}";
            
        return result;
    }
    
    /// <summary>
    /// Bump version using smart version bumper with a specific rule
    /// </summary>
    public static string BumpVersionWithRule(string versionString, string ruleName, VersionBumpOptions options = null)
    {
        var rule = VersionBumpRules.GetRule(ruleName);
        if (rule == null)
        {
            Debug.LogWarning($"[VersionUtility] Unknown rule: {ruleName}");
            return versionString;
        }
        
        return rule.Bump(versionString, options ?? new VersionBumpOptions());
    }
    
    /// <summary>
    /// Convert legacy VersionIncrementStrategy to smart bump options
    /// </summary>
    public static VersionBumpOptions ConvertStrategyToOptions(VersionIncrementStrategy strategy)
    {
        var options = new VersionBumpOptions();
        
        switch (strategy)
        {
            case VersionIncrementStrategy.Major:
                options.Part = "major";
                break;
            case VersionIncrementStrategy.Minor:
                options.Part = "minor";
                break;
            case VersionIncrementStrategy.Patch:
                options.Part = "patch";
                break;
            case VersionIncrementStrategy.Build:
                options.Part = "build";
                break;
        }
        
        return options;
    }
    
    /// <summary>
    /// Increment version using smart bump rules (new approach)
    /// </summary>
    public static string IncrementVersionSmart(string versionString, VersionIncrementStrategy strategy)
    {
        // Try to detect the version format and use the appropriate rule
        var options = ConvertStrategyToOptions(strategy);
        
        // Try semver first
        var semverRule = VersionBumpRules.GetRule("semver");
        if (semverRule != null && semverRule.CanMatch(versionString))
        {
            return semverRule.Bump(versionString, options);
        }
        
        // Try 4-part build version
        if (strategy == VersionIncrementStrategy.Build)
        {
            var buildRule = VersionBumpRules.GetRule("build");
            if (buildRule != null && buildRule.CanMatch(versionString))
            {
                return buildRule.Bump(versionString, options);
            }
        }
        
        // Fall back to legacy implementation
        return IncrementVersion(versionString, strategy);
    }
}
}

