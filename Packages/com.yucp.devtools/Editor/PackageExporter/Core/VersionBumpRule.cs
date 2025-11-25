using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Represents a single version bumping rule with pattern matching and bump logic
    /// </summary>
    public class VersionBumpRule
    {
        public string Name { get; }
        public Regex Pattern { get; }
        private readonly Func<Match, VersionBumpOptions, string> bumpFunc;

        public VersionBumpRule(string name, string pattern, Func<Match, VersionBumpOptions, string> bumpFunction)
        {
            Name = name;
            Pattern = new Regex(pattern, RegexOptions.Compiled);
            bumpFunc = bumpFunction;
        }

        /// <summary>
        /// Attempts to bump a version string using this rule
        /// </summary>
        public string Bump(string input, VersionBumpOptions options = null)
        {
            options = options ?? new VersionBumpOptions();
            var match = Pattern.Match(input);
            
            if (!match.Success)
            {
                Debug.LogWarning($"[VersionBumpRule] Pattern '{Name}' did not match input: {input}");
                return input;
            }
            
            return bumpFunc(match, options);
        }

        /// <summary>
        /// Checks if this rule can match the input
        /// </summary>
        public bool CanMatch(string input)
        {
            return Pattern.IsMatch(input);
        }
    }

    /// <summary>
    /// Options for controlling version bump behavior
    /// </summary>
    public class VersionBumpOptions
    {
        /// <summary>
        /// For semver: "major", "minor", or "patch"
        /// </summary>
        public string Part { get; set; } = "patch";
        
        /// <summary>
        /// Letter wrap policy: "aa" (double) or "a" (single)
        /// </summary>
        public string Wrap { get; set; } = "aa";
        
        /// <summary>
        /// Keep pre-release tag when bumping semver
        /// </summary>
        public bool KeepPrerelease { get; set; } = false;
        
        /// <summary>
        /// Keep build metadata when bumping semver
        /// </summary>
        public bool KeepMetadata { get; set; } = false;
    }

    /// <summary>
    /// Registry of all available version bump rules
    /// </summary>
    public static class VersionBumpRules
    {
        private static readonly System.Collections.Generic.Dictionary<string, VersionBumpRule> rules = 
            new System.Collections.Generic.Dictionary<string, VersionBumpRule>();

        static VersionBumpRules()
        {
            RegisterDefaultRules();
        }

        /// <summary>
        /// Register a new version bump rule
        /// </summary>
        public static void RegisterRule(VersionBumpRule rule)
        {
            rules[rule.Name.ToLowerInvariant()] = rule;
        }

        /// <summary>
        /// Get a rule by name
        /// </summary>
        public static VersionBumpRule GetRule(string name)
        {
            string key = name.ToLowerInvariant();
            return rules.ContainsKey(key) ? rules[key] : null;
        }

        /// <summary>
        /// Get all registered rule names
        /// </summary>
        public static string[] GetRuleNames()
        {
            var names = new string[rules.Count];
            rules.Keys.CopyTo(names, 0);
            return names;
        }

        /// <summary>
        /// Register all default version bump rules
        /// </summary>
        private static void RegisterDefaultRules()
        {
            // 1. Semver: "1.2.9[-pre][+meta]"
            RegisterRule(new VersionBumpRule(
                "semver",
                @"\b(?<maj>\d+)\.(?<min>\d+)\.(?<pat>\d+)(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+(?<meta>[0-9A-Za-z.-]+))?\b",
                (match, opt) =>
                {
                    int major = int.Parse(match.Groups["maj"].Value);
                    int minor = int.Parse(match.Groups["min"].Value);
                    int patch = int.Parse(match.Groups["pat"].Value);
                    string pre = opt.KeepPrerelease && match.Groups["pre"].Success ? match.Groups["pre"].Value : null;
                    string meta = opt.KeepMetadata && match.Groups["meta"].Success ? match.Groups["meta"].Value : null;

                    string part = opt.Part.ToLower();
                    if (part == "major")
                    {
                        major++;
                        minor = 0;
                        patch = 0;
                        if (!opt.KeepPrerelease) pre = null;
                    }
                    else if (part == "minor")
                    {
                        minor++;
                        patch = 0;
                        if (!opt.KeepPrerelease) pre = null;
                    }
                    else // patch
                    {
                        patch++;
                        if (!opt.KeepPrerelease) pre = null;
                    }

                    string result = $"{major}.{minor}.{patch}";
                    if (!string.IsNullOrEmpty(pre)) result += $"-{pre}";
                    if (!string.IsNullOrEmpty(meta)) result += $"+{meta}";
                    return result;
                }
            ));

            // 2. Dotted tail: "1.2.9" or "1.0.a" - increment last atom
            RegisterRule(new VersionBumpRule(
                "dotted_tail",
                @"\b(?<prefix>(?:\d+\.)*)(?<last>[A-Za-z]|\d+)\b",
                (match, opt) =>
                {
                    string prefix = match.Groups["prefix"].Value;
                    string last = match.Groups["last"].Value;
                    
                    if (char.IsDigit(last[0]))
                    {
                        // Numeric - increment with zero padding
                        int value = int.Parse(last);
                        string incremented = (value + 1).ToString().PadLeft(last.Length, '0');
                        return prefix + incremented;
                    }
                    else if (last.Length == 1 && char.IsLetter(last[0]))
                    {
                        // Single letter - bump with wrap policy
                        return prefix + BumpLetter(last[0], opt.Wrap);
                    }
                    
                    return match.Value; // Can't bump
                }
            ));

            // 3. Wordnum: "VERSION1" -> "VERSION2"
            RegisterRule(new VersionBumpRule(
                "wordnum",
                @"\b(?<name>[A-Za-z]+)(?<num>\d+)\b",
                (match, opt) =>
                {
                    string name = match.Groups["name"].Value;
                    string num = match.Groups["num"].Value;
                    int value = int.Parse(num);
                    string incremented = (value + 1).ToString().PadLeft(num.Length, '0');
                    return name + incremented;
                }
            ));

            // 4. Four-part version: "1.0.0.0" - bump build number (4th part)
            RegisterRule(new VersionBumpRule(
                "build",
                @"\b(?<maj>\d+)\.(?<min>\d+)\.(?<pat>\d+)\.(?<bld>\d+)\b",
                (match, opt) =>
                {
                    int major = int.Parse(match.Groups["maj"].Value);
                    int minor = int.Parse(match.Groups["min"].Value);
                    int patch = int.Parse(match.Groups["pat"].Value);
                    int build = int.Parse(match.Groups["bld"].Value);

                    string part = opt.Part.ToLower();
                    if (part == "major")
                    {
                        major++;
                        minor = 0;
                        patch = 0;
                        build = 0;
                    }
                    else if (part == "minor")
                    {
                        minor++;
                        patch = 0;
                        build = 0;
                    }
                    else if (part == "patch")
                    {
                        patch++;
                        build = 0;
                    }
                    else // build
                    {
                        build++;
                    }

                    return $"{major}.{minor}.{patch}.{build}";
                }
            ));

            // 5. CalVer: "2025.11.3" - bump day
            RegisterRule(new VersionBumpRule(
                "calver",
                @"\b(?<year>\d{4})\.(?<month>\d{1,2})\.(?<day>\d{1,2})\b",
                (match, opt) =>
                {
                    string year = match.Groups["year"].Value;
                    string month = match.Groups["month"].Value;
                    string day = match.Groups["day"].Value;
                    
                    int dayNum = int.Parse(day);
                    string incrementedDay = (dayNum + 1).ToString();
                    
                    return $"{year}.{month}.{incrementedDay}";
                }
            ));

            // 6. Simple number: "123" -> "124"
            RegisterRule(new VersionBumpRule(
                "number",
                @"\b(?<num>\d+)\b",
                (match, opt) =>
                {
                    string num = match.Groups["num"].Value;
                    int value = int.Parse(num);
                    return (value + 1).ToString().PadLeft(num.Length, '0');
                }
            ));
        }

        /// <summary>
        /// Helper to bump a single letter with wrap policy
        /// </summary>
        private static string BumpLetter(char ch, string wrapPolicy)
        {
            string alpha = char.IsLower(ch) ? "abcdefghijklmnopqrstuvwxyz" : "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            int index = alpha.IndexOf(ch);
            
            if (index < 25)
            {
                return alpha[index + 1].ToString();
            }
            
            // Wrap at 'z'/'Z'
            return wrapPolicy == "aa" ? new string(alpha[0], 2) : alpha[0].ToString();
        }
    }
}










