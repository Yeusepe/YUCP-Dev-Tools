using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace YUCP.PackageGuardian
{
    /// <summary>
    /// YUCP Package Guardian Mini - Lightweight protection for packages without com.yucp.components.
    /// Focuses on critical import protection and compatibility validation.
    /// Auto-upgrades to full Package Guardian when com.yucp.components is detected.
    /// </summary>
    [InitializeOnLoad]
    public class YUCPPackageGuardian : AssetPostprocessor
    {
        private static bool _initialized = false;
        private static bool _hasFullGuardian = false;
        private static HashSet<string> _processedImports = new HashSet<string>();
        
        static YUCPPackageGuardian()
        {
            EditorApplication.delayCall += Initialize;
        }
        
        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            
            // Check for full Package Guardian
            _hasFullGuardian = CheckForFullGuardian();
            
            if (_hasFullGuardian)
            {
                Debug.Log("[YUCP Mini Guardian] Full Package Guardian detected - delegating to main system");
                TriggerFullGuardian();
                ScheduleSelfRemoval();
                return;
            }
            
            Debug.Log("[YUCP Mini Guardian] Active - Lightweight protection enabled");
            PerformStartupValidation();
        }
        
        // ===== ASSET POSTPROCESSOR - SMART CHANGE DETECTION =====
        
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (_hasFullGuardian) return;
            
            // Only process YUCP-related imports
            var relevantFiles = imported.Where(IsRelevantFile).ToArray();
            if (relevantFiles.Length == 0) return;
            
            try
            {
                // Validate only what's changing
                ValidateImportedAssets(relevantFiles);
                HandleDisabledFiles(relevantFiles);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Mini Guardian] Import validation failed: {ex.Message}");
            }
        }
        
        private static bool IsRelevantFile(string path)
        {
            return path.EndsWith(".yucp_disabled") ||
                   path.Contains("YUCP") ||
                   path.EndsWith(".asmdef") ||
                   path.EndsWith("package.json") ||
                   path.Contains("/Editor/") ||
                   path.Contains("/Runtime/");
        }
        
        // ===== SMART VALIDATION - CRITICAL CHECKS ONLY =====
        
        private static void ValidateImportedAssets(string[] files)
        {
            var issues = new List<string>();
            
            // Check 1: Unity version compatibility
            foreach (var file in files.Where(f => f.EndsWith("package.json")))
            {
                var compatibility = CheckUnityCompatibility(file);
                if (!string.IsNullOrEmpty(compatibility))
                    issues.Add(compatibility);
            }
            
            // Check 2: Package conflicts
            var asmdefFiles = files.Where(f => f.EndsWith(".asmdef")).ToArray();
            if (asmdefFiles.Length > 0)
            {
                var conflicts = CheckPackageConflicts(asmdefFiles);
                issues.AddRange(conflicts);
            }
            
            // Check 3: Missing dependencies
            var dependencies = CheckMissingDependencies(files);
            issues.AddRange(dependencies);
            
            // Report issues
            if (issues.Count > 0)
            {
                string message = "Package import detected potential issues:\n\n" + string.Join("\n", issues);
                Debug.LogWarning($"[YUCP Mini Guardian] {message}");
                
                if (issues.Any(i => i.Contains("CRITICAL")))
                {
                    EditorUtility.DisplayDialog("Package Import Warning", message + "\n\nReview Console for details.", "OK");
                }
            }
        }
        
        private static string CheckUnityCompatibility(string packageJsonPath)
        {
            try
            {
                var content = File.ReadAllText(packageJsonPath);
                var unityMatch = Regex.Match(content, @"""unity""\s*:\s*""([^""]+)""");
                
                if (unityMatch.Success)
                {
                    string requiredVersion = unityMatch.Groups[1].Value;
                    string currentVersion = Application.unityVersion;
                    
                    // Parse versions
                    var required = ParseUnityVersion(requiredVersion);
                    var current = ParseUnityVersion(currentVersion);
                    
                    if (current.major < required.major || 
                        (current.major == required.major && current.minor < required.minor))
                    {
                        return $"⚠️ Unity {requiredVersion}+ required (current: {currentVersion})";
                    }
                }
            }
            catch { }
            
            return null;
        }
        
        private static List<string> CheckPackageConflicts(string[] asmdefFiles)
        {
            var issues = new List<string>();
            
            try
            {
                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetName().Name)
                    .ToHashSet();
                
                foreach (var asmdefPath in asmdefFiles)
                {
                    var content = File.ReadAllText(asmdefPath);
                    var nameMatch = Regex.Match(content, @"""name""\s*:\s*""([^""]+)""");
                    
                    if (nameMatch.Success)
                    {
                        string asmName = nameMatch.Groups[1].Value;
                        
                        // Check if assembly with similar name already exists
                        var similar = loadedAssemblies.FirstOrDefault(a => 
                            a.Contains(asmName) || asmName.Contains(a));
                        
                        if (similar != null && similar != asmName)
                        {
                            issues.Add($"⚠️ Possible assembly conflict: {asmName} ↔ {similar}");
                        }
                    }
                }
            }
            catch { }
            
            return issues;
        }
        
        private static List<string> CheckMissingDependencies(string[] files)
        {
            var issues = new List<string>();
            
            foreach (var file in files.Where(f => f.EndsWith("package.json")))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var depsMatch = Regex.Match(content, @"""dependencies""\s*:\s*\{([^}]+)\}");
                    
                    if (depsMatch.Success)
                    {
                        var deps = depsMatch.Groups[1].Value;
                        var depMatches = Regex.Matches(deps, @"""([^""]+)""\s*:\s*""([^""]+)""");
                        
                        string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                        
                        foreach (Match dep in depMatches)
                        {
                            string packageName = dep.Groups[1].Value;
                            string depPath = Path.Combine(packagesPath, packageName);
                            
                            if (!Directory.Exists(depPath) && !IsBuiltInPackage(packageName))
                            {
                                issues.Add($"⚠️ Missing dependency: {packageName}");
                            }
                        }
                    }
                }
                catch { }
            }
            
            return issues;
        }
        
        private static bool IsBuiltInPackage(string packageName)
        {
            return packageName.StartsWith("com.unity.") ||
                   packageName == "com.vrchat.avatars" ||
                   packageName == "com.vrchat.base";
        }
        
        // ===== .YUCP_DISABLED FILE HANDLING =====
        
        private static void HandleDisabledFiles(string[] files)
        {
            var disabledFiles = files.Where(f => f.EndsWith(".yucp_disabled")).ToArray();
            if (disabledFiles.Length == 0) return;
            
            Debug.Log($"[YUCP Mini Guardian] Processing {disabledFiles.Length} disabled file(s)");
            
            foreach (var disabledFile in disabledFiles)
            {
                try
                {
                    string enabledFile = disabledFile.Substring(0, disabledFile.Length - ".yucp_disabled".Length);
                    
                    if (!File.Exists(enabledFile))
                    {
                        // No conflict - just enable
                        File.Move(disabledFile, enabledFile);
                        Debug.Log($"[YUCP Mini Guardian] Enabled: {Path.GetFileName(enabledFile)}");
                    }
                    else
                    {
                        // Conflict detected - use smart resolution
                        if (ShouldReplaceFile(disabledFile, enabledFile))
                        {
                            File.Delete(enabledFile);
                            File.Move(disabledFile, enabledFile);
                            Debug.Log($"[YUCP Mini Guardian] Updated: {Path.GetFileName(enabledFile)}");
                        }
                        else
                        {
                            File.Delete(disabledFile);
                            Debug.Log($"[YUCP Mini Guardian] Removed duplicate: {Path.GetFileName(disabledFile)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YUCP Mini Guardian] Failed to process {Path.GetFileName(disabledFile)}: {ex.Message}");
                }
            }
            
            AssetDatabase.Refresh();
        }
        
        private static bool ShouldReplaceFile(string disabledFile, string enabledFile)
        {
            try
            {
                // Quick heuristics for replacement decision
                var disabledInfo = new FileInfo(disabledFile);
                var enabledInfo = new FileInfo(enabledFile);
                
                // If sizes are identical, assume duplicate
                if (disabledInfo.Length == enabledInfo.Length)
                    return false;
                
                // If disabled is newer and different size, likely an update
                if (disabledInfo.LastWriteTimeUtc > enabledInfo.LastWriteTimeUtc)
                    return true;
                
                // Default: don't replace
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        // ===== STARTUP VALIDATION =====
        
        private static void PerformStartupValidation()
        {
            try
            {
                // Quick health check on startup
                var issues = new List<string>();
                
                // Check for compilation errors
                if (EditorApplication.isCompiling)
                {
                    issues.Add("⚠️ Project is compiling - wait before making changes");
                }
                
                // Check for .yucp_disabled files
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                if (Directory.Exists(packagesPath))
                {
                    var disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                    if (disabledFiles.Length > 0)
                    {
                        issues.Add($"ℹ️ Found {disabledFiles.Length} disabled file(s) - will process on next import");
                    }
                }
                
                // Check for basic package issues
                string manifestPath = Path.Combine(packagesPath, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    issues.Add("🔴 CRITICAL: package manifest.json is missing!");
                }
                
                if (issues.Count > 0)
                {
                    Debug.Log("[YUCP Mini Guardian] Startup check:\n" + string.Join("\n", issues));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Mini Guardian] Startup validation failed: {ex.Message}");
            }
        }
        
        // ===== FULL GUARDIAN DETECTION & UPGRADE =====
        
        private static bool CheckForFullGuardian()
        {
            try
            {
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                string componentsPath = Path.Combine(packagesPath, "com.yucp.components");
                return Directory.Exists(componentsPath);
            }
            catch
            {
                return false;
            }
        }
        
        private static void TriggerFullGuardian()
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name.Contains("packageguardian"));
                    
                if (assembly != null)
                {
                    var serviceType = assembly.GetTypes()
                        .FirstOrDefault(t => t.Name == "ImportProtectionService");
                        
                    if (serviceType != null)
                    {
                        var method = serviceType.GetMethod("HandleDisabledFileConflicts",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        method?.Invoke(null, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Mini Guardian] Could not trigger full guardian: {ex.Message}");
            }
        }
        
        private static void ScheduleSelfRemoval()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    var scriptPath = AssetDatabase.FindAssets("t:Script YUCPPackageGuardian")
                        .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                        .FirstOrDefault(path => path.Contains("YUCPPackageGuardian") && 
                                               !path.Contains("com.yucp.components"));
                    
                    if (!string.IsNullOrEmpty(scriptPath))
                    {
                        Debug.Log($"[YUCP Mini Guardian] Upgrading to full Package Guardian - removing mini version");
                        AssetDatabase.DeleteAsset(scriptPath);
                    }
                }
                catch { }
            };
        }
        
        // ===== UTILITIES =====
        
        private static (int major, int minor, int patch) ParseUnityVersion(string version)
        {
            var match = Regex.Match(version, @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (match.Success)
            {
                return (
                    int.Parse(match.Groups[1].Value),
                    int.Parse(match.Groups[2].Value),
                    match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0
                );
            }
            return (0, 0, 0);
        }
        
        // ===== MENU ITEMS =====
        
        [MenuItem("Tools/YUCP/Mini Guardian - Status")]
        public static void ShowStatus()
        {
            bool hasFull = CheckForFullGuardian();
            
            string message = "YUCP Package Guardian Mini\n\n";
            
            if (hasFull)
            {
                message += "Status: Inactive (Full Guardian Available)\n\n" +
                          "Full Package Guardian is installed.\n" +
                          "Access via: Tools > Package Guardian\n\n" +
                          "This mini version will be removed automatically.";
            }
            else
            {
                message += "Status: Active (Lightweight Protection)\n\n" +
                          "Features:\n" +
                          "• Import change detection\n" +
                          "• Unity compatibility checking\n" +
                          "• Package conflict detection\n" +
                          "• Dependency validation\n" +
                          "• .yucp_disabled file handling\n\n" +
                          "Install com.yucp.components for full features:\n" +
                          "• Comprehensive health monitoring\n" +
                          "• Auto-fix capabilities\n" +
                          "• Transaction rollback\n" +
                          "• Health reports & more!";
            }
            
            EditorUtility.DisplayDialog("YUCP Mini Guardian", message, "OK");
        }
        
        [MenuItem("Tools/YUCP/Mini Guardian - Manual Check")]
        public static void ManualCheck()
        {
            Debug.Log("[YUCP Mini Guardian] Running manual validation...");
            
            try
            {
                PerformStartupValidation();
                
                // Process any pending .yucp_disabled files
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                if (Directory.Exists(packagesPath))
                {
                    var disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                    if (disabledFiles.Length > 0)
                    {
                        HandleDisabledFiles(disabledFiles);
                    }
                    else
                    {
                        Debug.Log("[YUCP Mini Guardian] No issues found - project looks healthy!");
                    }
                }
                
                EditorUtility.DisplayDialog("YUCP Mini Guardian", 
                    "Manual check complete!\n\nSee Console for details.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Mini Guardian] Manual check failed: {ex.Message}");
                EditorUtility.DisplayDialog("YUCP Mini Guardian", 
                    $"Check failed: {ex.Message}", "OK");
            }
        }
    }
}
