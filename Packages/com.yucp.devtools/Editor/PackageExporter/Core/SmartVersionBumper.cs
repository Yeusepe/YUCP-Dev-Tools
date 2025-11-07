using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Smart version bumper that scans files for directives and applies version bump rules
    /// Supports directives like: // @bump semver:patch, # @bump dotted_tail, <!-- @bump wordnum -->
    /// </summary>
    public static class SmartVersionBumper
    {
        // Directive pattern: @bump <rule_name>[:<part>] [options]
        private static readonly Regex DirectivePattern = new Regex(
            @"@bump\s+(?<rule>[A-Za-z_][A-Za-z0-9_]*)(?::(?<part>[A-Za-z0-9_]+))?(?:\s+(?<opts>[^\r\n]+))?",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Result of a version bump operation
        /// </summary>
        public class BumpResult
        {
            public string FilePath { get; set; }
            public int LineNumber { get; set; }
            public string OldVersion { get; set; }
            public string NewVersion { get; set; }
            public string RuleName { get; set; }
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }

            public override string ToString()
            {
                if (Success)
                    return $"{Path.GetFileName(FilePath)}:{LineNumber} | {OldVersion} → {NewVersion} ({RuleName})";
                else
                    return $"{Path.GetFileName(FilePath)}:{LineNumber} | ERROR: {ErrorMessage}";
            }
        }

        /// <summary>
        /// Scan and bump versions in a single file using inline directives
        /// </summary>
        /// <param name="filePath">Path to the file to process</param>
        /// <param name="writeBack">If true, write changes back to the file</param>
        /// <param name="defaultOptions">Default options for all bumps</param>
        /// <returns>List of bump results</returns>
        public static List<BumpResult> BumpFileWithDirectives(string filePath, bool writeBack = false, VersionBumpOptions defaultOptions = null)
        {
            var results = new List<BumpResult>();
            
            if (!File.Exists(filePath))
            {
                results.Add(new BumpResult
                {
                    FilePath = filePath,
                    Success = false,
                    ErrorMessage = "File not found"
                });
                return results;
            }

            try
            {
                string[] lines = File.ReadAllLines(filePath);
                bool modified = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    var directiveMatch = DirectivePattern.Match(line);
                    
                    if (!directiveMatch.Success)
                        continue;

                    string ruleName = directiveMatch.Groups["rule"].Value;
                    string part = directiveMatch.Groups["part"].Success ? directiveMatch.Groups["part"].Value : null;
                    string opts = directiveMatch.Groups["opts"].Success ? directiveMatch.Groups["opts"].Value : null;

                    var rule = VersionBumpRules.GetRule(ruleName);
                    if (rule == null)
                    {
                        results.Add(new BumpResult
                        {
                            FilePath = filePath,
                            LineNumber = i + 1,
                            Success = false,
                            RuleName = ruleName,
                            ErrorMessage = $"Unknown rule: {ruleName}"
                        });
                        continue;
                    }

                    // Build options for this bump
                    var options = BuildOptions(defaultOptions, part, opts);

                    // Find the version string on this line (before the directive)
                    string lineBeforeDirective = line.Substring(0, directiveMatch.Index);
                    var versionMatch = rule.Pattern.Match(lineBeforeDirective);
                    
                    if (!versionMatch.Success)
                    {
                        results.Add(new BumpResult
                        {
                            FilePath = filePath,
                            LineNumber = i + 1,
                            Success = false,
                            RuleName = ruleName,
                            ErrorMessage = $"No version found matching rule '{ruleName}' before directive"
                        });
                        continue;
                    }

                    string oldVersion = versionMatch.Value;
                    string newVersion = rule.Bump(oldVersion, options);

                    // Replace only the first occurrence of the version on this line
                    int versionIndex = lineBeforeDirective.IndexOf(oldVersion);
                    string newLine = line.Substring(0, versionIndex) + newVersion + line.Substring(versionIndex + oldVersion.Length);
                    lines[i] = newLine;
                    modified = true;

                    results.Add(new BumpResult
                    {
                        FilePath = filePath,
                        LineNumber = i + 1,
                        OldVersion = oldVersion,
                        NewVersion = newVersion,
                        RuleName = ruleName,
                        Success = true
                    });
                }

                if (modified && writeBack)
                {
                    File.WriteAllLines(filePath, lines);
                }

                return results;
            }
            catch (Exception ex)
            {
                results.Add(new BumpResult
                {
                    FilePath = filePath,
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}"
                });
                return results;
            }
        }

        /// <summary>
        /// Bump all occurrences of a version pattern in a file using a specific rule (ignoring directives)
        /// </summary>
        public static List<BumpResult> BumpFileWithRule(string filePath, string ruleName, bool writeBack = false, bool bumpAll = false, VersionBumpOptions options = null)
        {
            var results = new List<BumpResult>();
            
            if (!File.Exists(filePath))
            {
                results.Add(new BumpResult
                {
                    FilePath = filePath,
                    Success = false,
                    ErrorMessage = "File not found"
                });
                return results;
            }

            var rule = VersionBumpRules.GetRule(ruleName);
            if (rule == null)
            {
                results.Add(new BumpResult
                {
                    FilePath = filePath,
                    Success = false,
                    RuleName = ruleName,
                    ErrorMessage = $"Unknown rule: {ruleName}"
                });
                return results;
            }

            try
            {
                string content = File.ReadAllText(filePath);
                string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                bool modified = false;
                int replacements = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    var matches = rule.Pattern.Matches(line);
                    
                    if (matches.Count == 0)
                        continue;

                    string newLine = line;
                    int offset = 0;

                    foreach (Match match in matches)
                    {
                        string oldVersion = match.Value;
                        string newVersion = rule.Bump(oldVersion, options ?? new VersionBumpOptions());

                        int matchIndex = match.Index + offset;
                        newLine = newLine.Substring(0, matchIndex) + newVersion + newLine.Substring(matchIndex + oldVersion.Length);
                        offset += newVersion.Length - oldVersion.Length;

                        results.Add(new BumpResult
                        {
                            FilePath = filePath,
                            LineNumber = i + 1,
                            OldVersion = oldVersion,
                            NewVersion = newVersion,
                            RuleName = ruleName,
                            Success = true
                        });

                        replacements++;
                        modified = true;

                        if (!bumpAll)
                            break; // Only bump first occurrence per line
                    }

                    lines[i] = newLine;

                    if (!bumpAll && replacements > 0)
                        break; // Only bump first occurrence in file
                }

                if (modified && writeBack)
                {
                    File.WriteAllText(filePath, string.Join(Environment.NewLine, lines));
                }

                return results;
            }
            catch (Exception ex)
            {
                results.Add(new BumpResult
                {
                    FilePath = filePath,
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}"
                });
                return results;
            }
        }

        /// <summary>
        /// Scan multiple files for version directives and bump them
        /// </summary>
        public static List<BumpResult> BumpMultipleFiles(IEnumerable<string> filePaths, bool writeBack = false, VersionBumpOptions defaultOptions = null)
        {
            var allResults = new List<BumpResult>();

            foreach (var filePath in filePaths)
            {
                var results = BumpFileWithDirectives(filePath, writeBack, defaultOptions);
                allResults.AddRange(results);
            }

            return allResults;
        }

        /// <summary>
        /// Find all files in a directory that contain version bump directives
        /// </summary>
        public static List<string> FindFilesWithDirectives(string directory, string searchPattern = "*", bool recursive = true)
        {
            var filesWithDirectives = new List<string>();

            if (!Directory.Exists(directory))
                return filesWithDirectives;

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(directory, searchPattern, searchOption);

            foreach (var file in files)
            {
                try
                {
                    // Skip binary files
                    string ext = Path.GetExtension(file).ToLower();
                    if (ext == ".dll" || ext == ".exe" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".unitypackage")
                        continue;

                    string content = File.ReadAllText(file);
                    if (DirectivePattern.IsMatch(content))
                    {
                        filesWithDirectives.Add(file);
                    }
                }
                catch
                {
                    // Skip files that can't be read
                }
            }

            return filesWithDirectives;
        }

        /// <summary>
        /// Build options from default options, part specification, and option string
        /// </summary>
        private static VersionBumpOptions BuildOptions(VersionBumpOptions defaultOptions, string part, string optString)
        {
            var options = new VersionBumpOptions();

            // Start with defaults if provided
            if (defaultOptions != null)
            {
                options.Part = defaultOptions.Part;
                options.Wrap = defaultOptions.Wrap;
                options.KeepPrerelease = defaultOptions.KeepPrerelease;
                options.KeepMetadata = defaultOptions.KeepMetadata;
            }

            // Override with part if specified
            if (!string.IsNullOrEmpty(part))
            {
                options.Part = part;
            }

            // Parse option string if provided
            if (!string.IsNullOrEmpty(optString))
            {
                var optPairs = optString.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var opt in optPairs)
                {
                    var keyValue = opt.Split('=');
                    if (keyValue.Length == 2)
                    {
                        string key = keyValue[0].ToLower();
                        string value = keyValue[1].ToLower();

                        switch (key)
                        {
                            case "wrap":
                                options.Wrap = value;
                                break;
                            case "keep_pre":
                            case "keep-pre":
                            case "keepprerelease":
                                options.KeepPrerelease = value == "true" || value == "1";
                                break;
                            case "keep_meta":
                            case "keep-meta":
                            case "keepmetadata":
                                options.KeepMetadata = value == "true" || value == "1";
                                break;
                        }
                    }
                }
            }

            return options;
        }

        /// <summary>
        /// Preview what would be bumped without making changes
        /// </summary>
        public static List<BumpResult> PreviewBump(string filePath, VersionBumpOptions defaultOptions = null)
        {
            return BumpFileWithDirectives(filePath, writeBack: false, defaultOptions: defaultOptions);
        }

        /// <summary>
        /// Get a summary report of bump results
        /// </summary>
        public static string GetBumpSummary(List<BumpResult> results)
        {
            var sb = new StringBuilder();
            
            int successful = results.Count(r => r.Success);
            int failed = results.Count(r => !r.Success);

            sb.AppendLine($"Version Bump Summary:");
            sb.AppendLine($"  Successful: {successful}");
            sb.AppendLine($"  Failed: {failed}");
            sb.AppendLine();

            if (successful > 0)
            {
                sb.AppendLine("Successful bumps:");
                foreach (var result in results.Where(r => r.Success))
                {
                    sb.AppendLine($"  {result}");
                }
                sb.AppendLine();
            }

            if (failed > 0)
            {
                sb.AppendLine("Failed bumps:");
                foreach (var result in results.Where(r => !r.Success))
                {
                    sb.AppendLine($"  {result}");
                }
            }

            return sb.ToString();
        }
    }
}




