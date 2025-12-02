using System;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// ScriptableObject for defining custom version bump rules
    /// Users can create these assets and register them with the system
    /// </summary>
    [CreateAssetMenu(fileName = "CustomVersionRule", menuName = "YUCP/Custom Version Rule", order = 102)]
    public class CustomVersionRule : ScriptableObject
    {
        [Header("Rule Definition")]
        [Tooltip("Unique name for this rule (use lowercase, no spaces)")]
        public string ruleName = "custom_rule";
        
        [Tooltip("Display name shown in UI")]
        public string displayName = "Custom Rule";
        
        [Tooltip("Description of what this rule does")]
        [TextArea(2, 4)]
        public string description = "Custom version bumping rule";
        
        [Header("Pattern Configuration")]
        [Tooltip("Regex pattern to match version strings")]
        [TextArea(2, 4)]
        public string regexPattern = @"\b(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\b";
        
        [Tooltip("Built-in rule type to base behavior on")]
        public RuleType ruleType = RuleType.Semver;
        
        [Header("Options")]
        [Tooltip("Supports part specification (major/minor/patch)")]
        public bool supportsParts = true;
        
        [Tooltip("Preserve zero padding in numbers")]
        public bool preservePadding = true;
        
        [Tooltip("Example input")]
        public string exampleInput = "1.0.0";
        
        [Tooltip("Example output (for testing)")]
        public string exampleOutput = "1.0.1";
        
        public enum RuleType
        {
            Semver,         // Standard semantic versioning
            DottedTail,     // Increment last dotted component
            WordNum,        // Word followed by number
            Build,          // 4-part version
            CalVer,         // Calendar versioning
            Number,         // Simple number
            Custom          // Custom implementation (requires code)
        }
        
        /// <summary>
        /// Register this custom rule with the version bump system
        /// </summary>
        public void RegisterRule()
        {
            if (string.IsNullOrWhiteSpace(ruleName))
            {
                Debug.LogError($"[CustomVersionRule] Rule name cannot be empty for {name}");
                return;
            }
            
            if (string.IsNullOrWhiteSpace(regexPattern))
            {
                Debug.LogError($"[CustomVersionRule] Regex pattern cannot be empty for {ruleName}");
                return;
            }
            
            try
            {
                // Create the bump function using rule type
                Func<System.Text.RegularExpressions.Match, VersionBumpOptions, string> bumpFunc = ruleType switch
                {
                    RuleType.Semver => CreateSemverBumpFunc(),
                    RuleType.DottedTail => CreateDottedTailBumpFunc(),
                    RuleType.WordNum => CreateWordNumBumpFunc(),
                    RuleType.Build => CreateBuildBumpFunc(),
                    RuleType.CalVer => CreateCalVerBumpFunc(),
                    RuleType.Number => CreateNumberBumpFunc(),
                    RuleType.Custom => CreateCustomBumpFunc(),
                    _ => throw new NotImplementedException($"Rule type {ruleType} not implemented")
                };
                
                var rule = new VersionBumpRule(ruleName, regexPattern, bumpFunc);
                VersionBumpRules.RegisterRule(rule);
                
                Debug.Log($"[CustomVersionRule] Registered rule '{ruleName}' ({displayName})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CustomVersionRule] Failed to register rule '{ruleName}': {ex.Message}");
            }
        }
        
        /// <summary>
        /// Test this rule with the example input
        /// </summary>
        public string TestRule()
        {
            RegisterRule();
            var rule = VersionBumpRules.GetRule(ruleName);
            
            if (rule == null)
            {
                return "ERROR: Rule not found after registration";
            }
            
            try
            {
                var options = new VersionBumpOptions { Part = "patch" };
                return rule.Bump(exampleInput, options);
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }
        
        private Func<System.Text.RegularExpressions.Match, VersionBumpOptions, string> CreateSemverBumpFunc()
        {
            return (match, opt) =>
            {
                int major = int.Parse(match.Groups["major"].Value);
                int minor = int.Parse(match.Groups["minor"].Value);
                int patch = int.Parse(match.Groups["patch"].Value);
                
                string part = opt.Part.ToLower();
                if (part == "major")
                {
                    major++;
                    minor = 0;
                    patch = 0;
                }
                else if (part == "minor")
                {
                    minor++;
                    patch = 0;
                }
                else
                {
                    patch++;
                }
                
                return $"{major}.{minor}.{patch}";
            };
        }
        
        private Func<System.Text.RegularExpressions.Match, VersionBumpOptions, string> CreateDottedTailBumpFunc()
        {
            return (match, opt) =>
            {
                string prefix = match.Groups["prefix"].Success ? match.Groups["prefix"].Value : "";
                string last = match.Groups["last"].Success ? match.Groups["last"].Value : match.Value;
                
                if (string.IsNullOrEmpty(last))
                    return match.Value;
                
                if (char.IsDigit(last[0]))
                {
                    int value = int.Parse(last);
                    string incremented = preservePadding 
                        ? (value + 1).ToString().PadLeft(last.Length, '0')
                        : (value + 1).ToString();
                    return prefix + incremented;
                }
                
                return match.Value;
            };
        }
        
        private Func<System.Text.RegularExpressions.Match, VersionBumpOptions, string> CreateWordNumBumpFunc()
        {
            return (match, opt) =>
            {
                string name = match.Groups["name"].Success ? match.Groups["name"].Value : "";
                string num = match.Groups["num"].Success ? match.Groups["num"].Value : match.Groups[0].Value;
                
                if (string.IsNullOrEmpty(num))
                    return match.Value;
                
                int value = int.Parse(num);
                string incremented = preservePadding
                    ? (value + 1).ToString().PadLeft(num.Length, '0')
                    : (value + 1).ToString();
                return name + incremented;
            };
        }
        
        private Func<System.Text.RegularExpressions.Match, VersionBumpOptions, string> CreateBuildBumpFunc()
        {
            return (match, opt) =>
            {
                int major = int.Parse(match.Groups["major"].Value);
                int minor = int.Parse(match.Groups["minor"].Value);
                int patch = int.Parse(match.Groups["patch"].Value);
                int build = int.Parse(match.Groups["build"].Value);
                
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
                else
                {
                    build++;
                }
                
                return $"{major}.{minor}.{patch}.{build}";
            };
        }
        
        private Func<System.Text.RegularExpressions.Match, VersionBumpOptions, string> CreateCalVerBumpFunc()
        {
            return (match, opt) =>
            {
                string year = match.Groups["year"].Value;
                string month = match.Groups["month"].Value;
                string day = match.Groups["day"].Value;
                
                int dayNum = int.Parse(day);
                string incrementedDay = preservePadding && day.Length > 1
                    ? (dayNum + 1).ToString().PadLeft(day.Length, '0')
                    : (dayNum + 1).ToString();
                
                return $"{year}.{month}.{incrementedDay}";
            };
        }
        
        private Func<System.Text.RegularExpressions.Match, VersionBumpOptions, string> CreateNumberBumpFunc()
        {
            return (match, opt) =>
            {
                string num = match.Groups["num"].Success ? match.Groups["num"].Value : match.Value;
                int value = int.Parse(num);
                
                return preservePadding
                    ? (value + 1).ToString().PadLeft(num.Length, '0')
                    : (value + 1).ToString();
            };
        }
        
        private Func<System.Text.RegularExpressions.Match, VersionBumpOptions, string> CreateCustomBumpFunc()
        {
            // Capture ruleName in closure for runtime check
            string currentRuleName = ruleName;
            
            return (match, opt) =>
            {
                // Special handling for Unity version format
                if (currentRuleName == "unity_version")
                {
                    string year = match.Groups["year"].Success ? match.Groups["year"].Value : "";
                    string major = match.Groups["major"].Success ? match.Groups["major"].Value : "";
                    string patch = match.Groups["patch"].Success ? match.Groups["patch"].Value : "";
                    string suffix = match.Groups["suffix"].Success ? match.Groups["suffix"].Value : "";
                    
                    if (string.IsNullOrEmpty(patch))
                        return match.Value;
                    
                    int patchNum = int.Parse(patch);
                    patchNum++;
                    
                    return $"{year}.{major}.{patchNum}{suffix}";
                }
                
                // For other custom rules, return unchanged (users should override this method)
                return match.Value;
            };
        }
        
        private void OnValidate()
        {
            // Clean up rule name
            ruleName = ruleName.ToLower().Replace(" ", "_").Replace("-", "_");
            
            // Update display name if empty
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = ruleName.Replace("_", " ");
            }
        }
    }
}

