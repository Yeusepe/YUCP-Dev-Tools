using System;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public enum VersionIncrementStrategy
    {
        Patch,
        Minor,
        Major,
        Build
    }

    public static class VersionUtility
    {
        public static bool TryParseVersion(string text, out int major, out int minor, out int patch,
            out int? build, out string prerelease, out string metadata)
        {
            major = minor = patch = 0;
            build = null;
            prerelease = metadata = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();
            if (SemanticVersioning.Version.TryParse(text, out var semver))
            {
                major = semver.Major;
                minor = semver.Minor;
                patch = semver.Patch;
                prerelease = semver.PreRelease;
                metadata = semver.Build;
                return true;
            }

            if (!text.Contains("."))
                text += ".0";
            if (!Version.TryParse(text, out var numeric))
                return false;
            major = numeric.Major;
            minor = numeric.Minor;
            patch = Math.Max(0, numeric.Build);
            if (numeric.Revision >= 0)
                build = numeric.Revision;
            return true;
        }

        public static bool IsValidVersion(string text) =>
            TryParseVersion(text, out _, out _, out _, out _, out _, out _);

        public static string BumpVersionWithRule(string version, string ruleName, VersionBumpOptions options = null)
        {
            var rule = VersionBumpRules.GetRule(ruleName);
            if (rule == null)
            {
                Debug.LogWarning($"[VersionUtility] Unknown rule: {ruleName}");
                return version;
            }
            return rule.Bump(version, options ?? new VersionBumpOptions());
        }

        public static VersionBumpOptions ConvertStrategyToOptions(VersionIncrementStrategy strategy)
        {
            var options = new VersionBumpOptions();
            switch (strategy)
            {
                case VersionIncrementStrategy.Major: options.Part = "major"; break;
                case VersionIncrementStrategy.Minor: options.Part = "minor"; break;
                case VersionIncrementStrategy.Patch: options.Part = "patch"; break;
                case VersionIncrementStrategy.Build: options.Part = "build"; break;
            }
            return options;
        }
    }
}
