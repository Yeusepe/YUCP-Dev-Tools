using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Scans the Unity project for installed packages and their versions.
    /// Detects both standard Unity packages and VRChat VPM packages.
    /// </summary>
    public static class DependencyScanner
    {
        private class VpmRepositorySource
        {
            public string name;
            public string url;
            public string localPath;
        }
        
        public class PackageInfo
        {
            public string packageName;
            public string version;
            public string displayName;
            public string description;
            public bool isVpmPackage;
            public string packagePath;
            
            public PackageInfo(string name, string version, string displayName, string desc, bool isVpm, string path)
            {
                this.packageName = name;
                this.version = version;
                this.displayName = displayName;
                this.description = desc;
                this.isVpmPackage = isVpm;
                this.packagePath = path;
            }
        }
        
        /// <summary>
        /// Scan all installed packages in the project
        /// </summary>
        public static List<PackageInfo> ScanInstalledPackages()
        {
            var packages = new List<PackageInfo>();
            
            // Scan Packages folder for package.json files
            string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
            
            if (!Directory.Exists(packagesPath))
            {
                Debug.LogWarning("[DependencyScanner] Packages directory not found");
                return packages;
            }
            
            // Get all subdirectories in Packages
            string[] packageDirs = Directory.GetDirectories(packagesPath);
            
            foreach (string packageDir in packageDirs)
            {
                string packageJsonPath = Path.Combine(packageDir, "package.json");
                
                if (!File.Exists(packageJsonPath))
                    continue;
                
                try
                {
                    var packageInfo = ParsePackageJson(packageJsonPath);
                    if (packageInfo != null)
                    {
                        packages.Add(packageInfo);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DependencyScanner] Failed to parse {packageJsonPath}: {ex.Message}");
                }
            }
            
            // Also check VPM manifest for VRChat-specific packages
            string vpmManifestPath = Path.Combine(packagesPath, "vpm-manifest.json");
            if (File.Exists(vpmManifestPath))
            {
                try
                {
                    var vpmPackages = ParseVpmManifest(vpmManifestPath);
                    
                    // Mark VPM packages
                    foreach (var pkg in vpmPackages)
                    {
                        var existing = packages.Find(p => p.packageName == pkg.packageName);
                        if (existing != null)
                        {
                            existing.isVpmPackage = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DependencyScanner] Failed to parse VPM manifest: {ex.Message}");
                }
            }
            
            Debug.Log($"[DependencyScanner] Found {packages.Count} installed packages");
            
            return packages;
        }
        
        /// <summary>
        /// Parse a package.json file to extract package information
        /// </summary>
        private static PackageInfo ParsePackageJson(string packageJsonPath)
        {
            string json = File.ReadAllText(packageJsonPath);
            var jsonObj = JObject.Parse(json);
            
            string packageName = jsonObj["name"]?.ToString();
            string version = jsonObj["version"]?.ToString();
            string displayName = jsonObj["displayName"]?.ToString() ?? packageName;
            string description = jsonObj["description"]?.ToString() ?? "";
            
            if (string.IsNullOrEmpty(packageName))
                return null;
            
            string packagePath = Path.GetDirectoryName(packageJsonPath);
            
            return new PackageInfo(packageName, version, displayName, description, false, packagePath);
        }
        
        /// <summary>
        /// Parse VPM manifest to identify VRChat packages
        /// </summary>
        private static List<PackageInfo> ParseVpmManifest(string vpmManifestPath)
        {
            var packages = new List<PackageInfo>();
            
            try
            {
                string json = File.ReadAllText(vpmManifestPath);
                var jsonObj = JObject.Parse(json);
                
                var locked = jsonObj["locked"];
                if (locked != null)
                {
                    foreach (var prop in locked.Children<JProperty>())
                    {
                        string packageName = prop.Name;
                        var versionObj = prop.Value["version"];
                        string version = versionObj?.ToString() ?? "unknown";
                        
                        packages.Add(new PackageInfo(packageName, version, packageName, "", true, ""));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DependencyScanner] Failed to parse VPM manifest: {ex.Message}");
            }
            
            return packages;
        }
        
        /// <summary>
        /// Convert PackageInfo list to PackageDependency list for export profile
        /// </summary>
        public static List<PackageDependency> ConvertToPackageDependencies(List<PackageInfo> packages)
        {
            var dependencies = new List<PackageDependency>();
            
            foreach (var pkg in packages)
            {
                // Skip Unity's built-in packages
                if (pkg.packageName.StartsWith("com.unity."))
                    continue;
                
                var dependency = new PackageDependency(
                    pkg.packageName,
                    pkg.version,
                    pkg.displayName,
                    pkg.isVpmPackage
                );
                
                dependencies.Add(dependency);
            }
            
            return dependencies;
        }
        
        /// <summary>
        /// Scan export folders for used scripts/components and detect which packages they belong to.
        /// Automatically enables dependencies that are actively used in the export.
        /// </summary>
        public static void AutoDetectUsedDependencies(ExportProfile profile)
        {
            Debug.Log("[DependencyScanner] Auto-detecting used dependencies...");
            
            if (profile.foldersToExport.Count == 0)
            {
                Debug.LogWarning("[DependencyScanner] No export folders configured");
                return;
            }
            
            var installedPackages = ScanInstalledPackages();
            var usedPackages = new HashSet<string>();
            
            // Scan all prefabs and scenes in export folders for components
            foreach (string folder in profile.foldersToExport)
            {
                if (!Directory.Exists(folder))
                    continue;
                
                // Find all prefabs and scenes
                string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
                string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { folder });
                
                var allGuids = prefabGuids.Concat(sceneGuids).ToArray();
                
                foreach (string guid in allGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var usedInAsset = DetectPackagesInAsset(assetPath, installedPackages);
                    
                    foreach (var packageName in usedInAsset)
                    {
                        usedPackages.Add(packageName);
                    }
                }
            }
            
            if (usedPackages.Count > 0)
            {
                Debug.Log($"[DependencyScanner] Auto-detected {usedPackages.Count} used packages: {string.Join(", ", usedPackages)}");
                
                // Enable dependencies that are used
                foreach (var dep in profile.dependencies)
                {
                    if (usedPackages.Contains(dep.packageName))
                    {
                        dep.enabled = true;
                        
                        if (dep.isVpmDependency)
                        {
                            dep.exportMode = DependencyExportMode.Dependency;
                        }
                        
                        Debug.Log($"[DependencyScanner] Auto-enabled dependency: {dep.packageName}");
                    }
                    else
                    {
                        // Disable unused dependencies by default
                        dep.enabled = false;
                    }
                }
            }
        }
        
        /// <summary>
        /// Detect which packages are used in a specific asset (prefab or scene)
        /// </summary>
        private static List<string> DetectPackagesInAsset(string assetPath, List<PackageInfo> installedPackages)
        {
            var usedPackages = new List<string>();
            
            try
            {
                // Read the asset file as text to look for script references
                string assetContent = File.ReadAllText(assetPath);
                
                // Look for MonoScript GUIDs in the file
                var guidMatches = System.Text.RegularExpressions.Regex.Matches(assetContent, @"guid:\s*([a-f0-9]{32})");
                
                foreach (System.Text.RegularExpressions.Match match in guidMatches)
                {
                    string guid = match.Groups[1].Value;
                    string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                    
                    if (string.IsNullOrEmpty(scriptPath))
                        continue;
                    
                    // Check which package this script belongs to
                    foreach (var pkg in installedPackages)
                    {
                        if (!string.IsNullOrEmpty(pkg.packagePath) && scriptPath.StartsWith(GetRelativePackagePath(pkg.packagePath)))
                        {
                            if (!usedPackages.Contains(pkg.packageName))
                            {
                                usedPackages.Add(pkg.packageName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DependencyScanner] Failed to scan asset {assetPath}: {ex.Message}");
            }
            
            return usedPackages;
        }
        
        /// <summary>
        /// Convert absolute path to Unity-relative path
        /// </summary>
        private static string GetRelativePackagePath(string absolutePath)
        {
            string projectPath = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            
            if (absolutePath.StartsWith(projectPath))
            {
                string relative = absolutePath.Substring(projectPath.Length);
                if (relative.StartsWith("\\") || relative.StartsWith("/"))
                {
                    relative = relative.Substring(1);
                }
                return relative.Replace('\\', '/');
            }
            
            return absolutePath.Replace('\\', '/');
        }
        
        /// <summary>
        /// Generate package.json content with dependencies
        /// </summary>
        public static string GeneratePackageJson(
            ExportProfile profile,
            List<PackageDependency> dependencies,
            string existingPackageJsonPath = null)
        {
            JObject packageJson;
            
            // Load existing package.json if provided
            if (!string.IsNullOrEmpty(existingPackageJsonPath) && File.Exists(existingPackageJsonPath))
            {
                string existingJson = File.ReadAllText(existingPackageJsonPath);
                packageJson = JObject.Parse(existingJson);
            }
            else
            {
                // Create new package.json
                packageJson = new JObject();
            }
            
            // Set basic fields
            packageJson["name"] = profile.packageName.ToLower().Replace(" ", ".");
            packageJson["displayName"] = profile.packageName;
            packageJson["version"] = profile.version;
            
            if (!string.IsNullOrEmpty(profile.description))
            {
                packageJson["description"] = profile.description;
            }
            
            if (!string.IsNullOrEmpty(profile.author))
            {
                var authorObj = new JObject();
                authorObj["name"] = profile.author;
                packageJson["author"] = authorObj;
            }
            
            // Add unity version requirement
            packageJson["unity"] = "2022.3";
            
            // Build dependencies
            var depsToReference = dependencies.Where(d => d.enabled && d.exportMode == DependencyExportMode.Dependency).ToList();
            
            if (depsToReference.Count > 0)
            {
                // Separate VPM and regular dependencies
                var vpmDeps = depsToReference.Where(d => d.isVpmDependency).ToList();
                var regularDeps = depsToReference.Where(d => !d.isVpmDependency).ToList();
                
                // Add regular dependencies
                if (regularDeps.Count > 0)
                {
                    var depsObj = new JObject();
                    foreach (var dep in regularDeps)
                    {
                        depsObj[dep.packageName] = dep.packageVersion;
                    }
                    packageJson["dependencies"] = depsObj;
                }
                
                // Add VPM dependencies (VRChat packages)
                if (vpmDeps.Count > 0)
                {
                    List<PackageDependency> missingVpmDeps;
                    var resolvedVpmDeps = FilterVpmDependenciesWithDownloadUrl(vpmDeps, out missingVpmDeps);
                    
                    if (missingVpmDeps.Count > 0)
                    {
                        string missingList = string.Join(", ", missingVpmDeps.Select(d => d.packageName));
                        Debug.LogWarning(
                            $"[DependencyScanner] Skipping VPM dependencies with no download URL: {missingList}"
                        );
                    }
                    
                    if (resolvedVpmDeps.Count > 0)
                    {
                        var vpmDepsObj = new JObject();
                        foreach (var dep in resolvedVpmDeps)
                        {
                            // Use >= for VPM dependencies to allow updates
                            vpmDepsObj[dep.packageName] = $">={dep.packageVersion}";
                        }
                        packageJson["vpmDependencies"] = vpmDepsObj;
                        
                        // Only add custom repositories (do not auto-add discovered ones)
                        var customRepos = BuildCustomVpmRepositories(resolvedVpmDeps);
                        if (customRepos.Count > 0)
                        {
                            var vpmReposObj = new JObject();
                            foreach (var repo in customRepos)
                            {
                                vpmReposObj[repo.Key] = repo.Value;
                            }
                            packageJson["vpmRepositories"] = vpmReposObj;
                        }
                    }
                }
            }
            
            // Return formatted JSON
            return packageJson.ToString(Newtonsoft.Json.Formatting.Indented);
        }
        
        private static List<PackageDependency> FilterVpmDependenciesWithDownloadUrl(
            List<PackageDependency> vpmDeps,
            out List<PackageDependency> missingDeps)
        {
            missingDeps = new List<PackageDependency>();
            
            try
            {
                if (vpmDeps == null || vpmDeps.Count == 0)
                    return new List<PackageDependency>();
                
                var repoSources = GetVpmRepositorySources();
                
                foreach (var dep in vpmDeps)
                {
                    string packageName = dep.packageName;
                    if (string.IsNullOrEmpty(packageName))
                    {
                        missingDeps.Add(dep);
                        continue;
                    }
                    
                    bool foundDownloadUrl = false;
                    
                    if (!string.IsNullOrWhiteSpace(dep.vpmRepositoryUrl))
                    {
                        string customUrl = dep.vpmRepositoryUrl.Trim();
                        if (TryLoadRepoJson(new VpmRepositorySource
                            {
                                name = "Custom",
                                url = customUrl,
                                localPath = null
                            }, out var customJson))
                        {
                            if (RepoHasDownloadUrlForPackage(customJson, dep, packageName, out _))
                            {
                                foundDownloadUrl = true;
                            }
                        }
                    }
                    
                    foreach (var repo in repoSources)
                    {
                        if (TryLoadRepoJson(repo, out var repoJson) &&
                            RepoHasDownloadUrlForPackage(repoJson, dep, packageName, out _))
                        {
                            foundDownloadUrl = true;
                            break;
                        }
                    }
                    
                    if (!foundDownloadUrl)
                    {
                        missingDeps.Add(dep);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DependencyScanner] Failed to get required repositories: {ex.Message}");
            }
            
            var missingSet = new HashSet<PackageDependency>(missingDeps);
            return vpmDeps.Where(d => !missingSet.Contains(d)).ToList();
        }

        public static bool TryGetVpmDependencyRepoInfo(
            PackageDependency dep,
            out string repoName,
            out string repoUrl,
            out bool hasDownloadUrl,
            out List<string> checkedUrls,
            out string lookupError)
        {
            repoName = "";
            repoUrl = "";
            hasDownloadUrl = false;
            checkedUrls = new List<string>();
            lookupError = "";
            
            if (dep == null || !dep.isVpmDependency)
            {
                lookupError = "Not a VPM dependency";
                return false;
            }
            
            var repoSources = GetVpmRepositorySources();
            checkedUrls = repoSources
                .Select(r => !string.IsNullOrEmpty(r.url) ? r.url : r.localPath)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            
            if (!string.IsNullOrWhiteSpace(dep.vpmRepositoryUrl))
            {
                var customUrl = dep.vpmRepositoryUrl.Trim();
                checkedUrls.Insert(0, customUrl);
                if (TryLoadRepoJson(new VpmRepositorySource
                    {
                        name = "Custom",
                        url = customUrl,
                        localPath = null
                    }, out var customJson))
                {
                    string packageName = dep.packageName;
                    if (!string.IsNullOrEmpty(packageName) &&
                        RepoHasDownloadUrlForPackage(customJson, dep, packageName, out _))
                    {
                        repoName = "Custom";
                        repoUrl = customUrl;
                        hasDownloadUrl = true;
                        return true;
                    }
                }
            }
            
            string pkgName = dep.packageName;
            if (string.IsNullOrEmpty(pkgName))
            {
                lookupError = "Package name is empty";
                return true;
            }
            
            foreach (var repo in repoSources)
            {
                if (!TryLoadRepoJson(repo, out var repoJson))
                    continue;
                
                if (RepoHasDownloadUrlForPackage(repoJson, dep, pkgName, out _))
                {
                    repoName = repo.name;
                    repoUrl = !string.IsNullOrEmpty(repo.url) ? repo.url : repo.localPath;
                    hasDownloadUrl = true;
                    return true;
                }
            }
            
            return true;
        }

        private static List<VpmRepositorySource> GetVpmRepositorySources()
        {
            var repos = new List<VpmRepositorySource>();
            
            string basePath = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "VRChatCreatorCompanion",
                "Repos"
            );
            
            repos.Add(new VpmRepositorySource
            {
                name = "VRChat Official",
                url = "https://packages.vrchat.com/official?download",
                localPath = Path.Combine(basePath, "vrc-official.json")
            });
            
            repos.Add(new VpmRepositorySource
            {
                name = "VRChat Curated",
                url = "https://packages.vrchat.com/curated?download",
                localPath = Path.Combine(basePath, "vrc-curated.json")
            });
            
            try
            {
                string vccSettingsPath = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "VRChatCreatorCompanion",
                    "settings.json"
                );
                
                if (File.Exists(vccSettingsPath))
                {
                    var settings = JObject.Parse(File.ReadAllText(vccSettingsPath));
                    var userRepos = settings["userRepos"] as JArray;
                    
                    if (userRepos != null)
                    {
                        foreach (var repoObj in userRepos)
                        {
                            var repo = repoObj as JObject;
                            if (repo == null) continue;
                            
                            string localPath = repo["localPath"]?.ToString();
                            string repoUrl = repo["url"]?.ToString();
                            string repoName = repo["name"]?.ToString();
                            string repoId = repo["id"]?.ToString();
                            
                            string name = !string.IsNullOrEmpty(repoName) ? repoName : repoId;
                            if (string.IsNullOrEmpty(name))
                                name = "VCC Repo";
                            
                            repos.Add(new VpmRepositorySource
                            {
                                name = name,
                                url = repoUrl,
                                localPath = localPath
                            });
                        }
                    }
                }
            }
            catch { }
            
            return repos;
        }

        private static bool TryLoadRepoJson(VpmRepositorySource repo, out JObject repoJson)
        {
            repoJson = null;
            
            if (repo != null && !string.IsNullOrEmpty(repo.localPath) && File.Exists(repo.localPath))
            {
                try
                {
                    repoJson = JObject.Parse(File.ReadAllText(repo.localPath));
                    return true;
                }
                catch { }
            }
            
            if (repo != null && !string.IsNullOrEmpty(repo.url))
            {
                try
                {
                    using (var client = new System.Net.WebClient())
                    {
                        string repoData = client.DownloadString(repo.url);
                        repoJson = JObject.Parse(repoData);
                        return true;
                    }
                }
                catch { }
            }
            
            return false;
        }

        private static JObject GetRepoPackagesObject(JObject repoJson)
        {
            var repo = repoJson?["repo"] as JObject;
            var packages = repo?["packages"] as JObject;
            if (packages != null)
                return packages;
            
            return repoJson?["packages"] as JObject;
        }

        private static bool RepoHasDownloadUrlForPackage(
            JObject repoJson,
            PackageDependency dep,
            string packageName,
            out string foundUrl)
        {
            foundUrl = null;
            
            var packages = GetRepoPackagesObject(repoJson);
            var packageObj = packages?[packageName] as JObject;
            var versions = packageObj?["versions"] as JObject;
            
            if (versions == null || versions.Count == 0)
                return false;
            
            string requirement = dep.packageVersion;
            if (!string.IsNullOrEmpty(requirement) && versions[requirement] is JObject exact)
            {
                string exactUrl = exact["url"]?.ToString();
                if (IsValidDownloadUrl(exactUrl))
                {
                    foundUrl = exactUrl;
                    return true;
                }
            }
            
            foreach (var versionProp in versions.Properties())
            {
                string version = versionProp.Name;
                if (!VersionSatisfiesRequirement(version, requirement))
                    continue;
                
                string url = (versionProp.Value as JObject)?["url"]?.ToString();
                if (IsValidDownloadUrl(url))
                {
                    foundUrl = url;
                    return true;
                }
            }
            
            return false;
        }

        private static bool IsValidDownloadUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            }
            
            return false;
        }

        private static Dictionary<string, string> BuildCustomVpmRepositories(List<PackageDependency> vpmDeps)
        {
            var repos = new Dictionary<string, string>();
            
            foreach (var dep in vpmDeps)
            {
                if (dep == null)
                    continue;

                // 1) Respect explicit custom repo URL if provided on the dependency.
                if (!string.IsNullOrWhiteSpace(dep.vpmRepositoryUrl))
                {
                    string url = dep.vpmRepositoryUrl.Trim();
                    if (IsValidDownloadUrl(url))
                    {
                        string key = $"{dep.packageName} (Custom)";
                        if (!repos.ContainsKey(key))
                        {
                            repos[key] = url;
                        }
                    }
                }

                // 2) Otherwise, embed the discovered repo URL (if any) so users without that repo can still install.
                if (TryGetVpmDependencyRepoInfo(
                        dep,
                        out var repoName,
                        out var repoUrl,
                        out var hasDownloadUrl,
                        out _,
                        out _))
                {
                    if (hasDownloadUrl && IsValidDownloadUrl(repoUrl))
                    {
                        string key = string.IsNullOrWhiteSpace(repoName) ? "VPM Repo" : repoName.Trim();
                        if (!repos.ContainsKey(key))
                        {
                            repos[key] = repoUrl;
                        }
                    }
                }
            }
            
            return repos;
        }

        private static bool VersionSatisfiesRequirement(string version, string requirement)
        {
            if (string.IsNullOrEmpty(requirement))
                return true;
            
            string req = requirement.Trim();
            
            if (req.StartsWith(">="))
            {
                string minVersion = req.Substring(2).Trim();
                return CompareVersions(version, minVersion) >= 0;
            }
            
            if (req.StartsWith("^"))
            {
                string baseVersion = req.Substring(1).Trim();
                var baseParts = ParseVersion(baseVersion);
                var parts = ParseVersion(version);
                if (baseParts.major != parts.major)
                    return false;
                
                return CompareVersions(version, baseVersion) >= 0;
            }
            
            if (req.StartsWith("~"))
            {
                string baseVersion = req.Substring(1).Trim();
                var baseParts = ParseVersion(baseVersion);
                var parts = ParseVersion(version);
                if (baseParts.major != parts.major || baseParts.minor != parts.minor)
                    return false;
                
                return CompareVersions(version, baseVersion) >= 0;
            }
            
            return CompareVersions(version, req) >= 0;
        }

        private static int CompareVersions(string version1, string version2)
        {
            var v1 = ParseVersion(version1);
            var v2 = ParseVersion(version2);
            
            if (v1.major != v2.major) return v1.major.CompareTo(v2.major);
            if (v1.minor != v2.minor) return v1.minor.CompareTo(v2.minor);
            return v1.patch.CompareTo(v2.patch);
        }

        private static (int major, int minor, int patch) ParseVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
                return (0, 0, 0);
            
            string normalized = version.Trim().TrimStart('v', 'V');
            int dashIndex = normalized.IndexOf('-');
            if (dashIndex > 0)
                normalized = normalized.Substring(0, dashIndex);
            
            var parts = normalized.Split('.');
            int major = parts.Length > 0 ? SafeParseInt(parts[0]) : 0;
            int minor = parts.Length > 1 ? SafeParseInt(parts[1]) : 0;
            int patch = parts.Length > 2 ? SafeParseInt(parts[2]) : 0;
            return (major, minor, patch);
        }

        private static int SafeParseInt(string value)
        {
            return int.TryParse(value, out var parsed) ? parsed : 0;
        }
    }
}

