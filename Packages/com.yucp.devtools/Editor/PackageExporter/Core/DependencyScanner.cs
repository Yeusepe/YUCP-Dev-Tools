using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityPackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Scans the Unity project for installed packages and their versions.
    /// Detects both standard Unity packages and VRChat VPM packages.
    /// </summary>
    public static class DependencyScanner
    {
        private sealed class RepositoryCacheEntry
        {
            public JObject json;
            public DateTime expires;
            public DateTime lastWrite;
            public UnityWebRequest request;
        }

        private static readonly Dictionary<string, RepositoryCacheEntry> RepoJsonCache =
            new Dictionary<string, RepositoryCacheEntry>(StringComparer.Ordinal);
        internal static event Action RepositoriesChanged;

        // Installed versions and the repository list are read on every profile rebuild, once per
        // dependency; both are cached until Unity or the Creator Companion reports a change.
        private static Dictionary<string, string> installedVersions;
        private static List<VpmRepositorySource> repoSources;
        private static DateTime repoSourcesStamp;

        // ponytail: VPM lookups are kept for 30 s and dropped when a repository finishes loading or the
        // repository list changes; enough to make rebuilds cheap without serving stale data for long.
        private static readonly TimeSpan LookupLifetime = TimeSpan.FromSeconds(30);
        private static readonly Dictionary<string, (DateTime expires, bool ok, List<string> versions, string repoName, string repoUrl, string error)> versionLookups =
            new Dictionary<string, (DateTime, bool, List<string>, string, string, string)>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, (DateTime expires, bool ok, string repoName, string repoUrl, bool hasDownloadUrl, List<string> checkedUrls, string error)> repoLookups =
            new Dictionary<string, (DateTime, bool, string, string, bool, List<string>, string)>(StringComparer.OrdinalIgnoreCase);

        private static void ClearLookups()
        {
            versionLookups.Clear();
            repoLookups.Clear();
        }

        static DependencyScanner()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                foreach (var entry in RepoJsonCache.Values)
                    entry.request?.Dispose();
            };
            Events.registeredPackages += _ => installedVersions = null;
            RepositoriesChanged += ClearLookups;
        }

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
            public PackageSource source;

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

        public static List<PackageInfo> ScanInstalledPackages()
        {
            var vpmPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string manifest = Path.Combine(Application.dataPath, "../Packages/vpm-manifest.json");
            if (File.Exists(manifest))
            {
                var locked = JObject.Parse(File.ReadAllText(manifest))["locked"] as JObject;
                if (locked != null)
                {
                    foreach (var package in locked.Properties())
                        vpmPackages.Add(package.Name);
                }
            }

            var packages = new List<PackageInfo>();
            foreach (var package in UnityPackageInfo.GetAllRegisteredPackages())
            {
                packages.Add(new PackageInfo(package.name, package.version, package.displayName,
                    package.description, vpmPackages.Contains(package.name) || HasVpmManifest(package.resolvedPath), package.resolvedPath)
                {
                    source = package.source
                });
            }
            return packages;
        }

        internal static bool HasVpmManifest(string packagePath)
        {
            string manifestPath = Path.Combine(packagePath, "package.json");
            return File.Exists(manifestPath) && JObject.Parse(File.ReadAllText(manifestPath))["vpmDependencies"] is JObject;
        }

        public static string GetInstalledPackageVersion(string packageName)
        {
            if (installedVersions == null)
            {
                installedVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var package in UnityPackageInfo.GetAllRegisteredPackages())
                    if (!installedVersions.ContainsKey(package.name))
                        installedVersions[package.name] = package.version;
            }
            return packageName != null && installedVersions.TryGetValue(packageName, out var version) ? version : "";
        }

        /// <summary>
        /// Convert PackageInfo list to PackageDependency list for export profile
        /// </summary>
        public static List<PackageDependency> ConvertToPackageDependencies(List<PackageInfo> packages)
        {
            var dependencies = new List<PackageDependency>();

            foreach (var pkg in packages)
            {

                if (pkg.source == PackageSource.BuiltIn)
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

        public static void AutoDetectUsedDependencies(ExportProfile profile)
        {
            var folders = new HashSet<string>(StringComparer.Ordinal);
            bool changed = false;
            for (int i = 0; i < profile.foldersToExport.Count; i++)
            {
                string folder = NormalizeAssetDatabaseFolderPath(profile.foldersToExport[i]);
                if (folder == null || !AssetDatabase.IsValidFolder(folder))
                    continue;

                if (profile.foldersToExport[i] != folder)
                {
                    profile.foldersToExport[i] = folder;
                    changed = true;
                }
                folders.Add(folder);
            }

            var usedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (folders.Count > 0)
            {
                string[] assets = AssetDatabase.FindAssets("", folders.ToArray())
                    .Select(AssetDatabase.GUIDToAssetPath).ToArray();
                foreach (string path in AssetDatabase.GetDependencies(assets, true))
                {
                    var package = UnityPackageInfo.FindForAssetPath(path);
                    if (package != null)
                        usedPackages.Add(package.name);
                }
            }

            foreach (var dependency in profile.dependencies)
            {
                if (!dependency.enabled && usedPackages.Contains(dependency.packageName))
                {
                    dependency.enabled = true;
                    changed = true;
                }
            }
            if (changed)
                EditorUtility.SetDirty(profile);
        }

        private static string NormalizeAssetDatabaseFolderPath(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullPath = Path.GetFullPath(Path.Combine(projectRoot, folder.Trim()));
            string relative = FileUtil.GetProjectRelativePath(fullPath.Replace('\\', '/')).TrimEnd('/');
            if (relative == "Assets" || relative.StartsWith("Assets/", StringComparison.Ordinal) ||
                relative.StartsWith("Packages/", StringComparison.Ordinal))
                return relative;
            return null;
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


            if (!string.IsNullOrEmpty(existingPackageJsonPath) && File.Exists(existingPackageJsonPath))
            {
                string existingJson = File.ReadAllText(existingPackageJsonPath);
                packageJson = JObject.Parse(existingJson);
            }
            else
            {

                packageJson = new JObject();
            }


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


            packageJson["unity"] = "2022.3";

            packageJson.Remove("dependencies");
            packageJson.Remove("vpmDependencies");
            packageJson.Remove("vpmRepositories");

            var regular = new JObject();
            var vpm = new JObject();
            var vpmDependencies = new List<PackageDependency>();
            var installed = UnityPackageInfo.GetAllRegisteredPackages();
            var installedVpmPackages = new HashSet<string>(ScanInstalledPackages()
                .Where(package => package.isVpmPackage).Select(package => package.packageName));
            foreach (var dependency in dependencies)
            {
                if (!dependency.enabled || dependency.exportMode != DependencyExportMode.Dependency)
                    continue;
                if (dependency.packageName == (string)packageJson["name"])
                    throw new InvalidOperationException("A package cannot depend on itself.");
                if (string.IsNullOrWhiteSpace(dependency.packageName))
                    throw new InvalidOperationException("A selected dependency has no package name.");

                if (dependency.isVpmDependency || installedVpmPackages.Contains(dependency.packageName) ||
                    !string.IsNullOrWhiteSpace(dependency.vpmRepositoryUrl))
                {
                    if (!SemanticVersioning.Range.TryParse(dependency.GetVpmVersionRequirement(), out _))
                        throw new InvalidOperationException($"Invalid VPM version requirement for {dependency.packageName}.");
                    vpm[dependency.packageName] = dependency.GetVpmVersionRequirement();
                    vpmDependencies.Add(new PackageDependency(dependency.packageName, dependency.packageVersion, dependency.displayName, true)
                    {
                        versionMode = dependency.versionMode,
                        specificVersion = dependency.specificVersion,
                        vpmRepositoryUrl = dependency.vpmRepositoryUrl
                    });
                    continue;
                }

                string version = dependency.versionMode == DependencyVersionMode.Specific
                    ? dependency.GetSpecificVersionOrDefault() : dependency.packageVersion;
                if (!SemanticVersioning.Version.TryParse(version, out _))
                    throw new InvalidOperationException($"{dependency.packageName} requires an exact Unity package version.");
                var package = installed.FirstOrDefault(p => p.name == dependency.packageName);
                if (package != null && (package.source == PackageSource.Git ||
                    package.source == PackageSource.Local || package.source == PackageSource.LocalTarball))
                    throw new InvalidOperationException($"{dependency.packageName} is installed from {package.source}. Bundle it or install its registry release before exporting a dependency.");
                regular[dependency.packageName] = version;
            }
            if (regular.Count > 0)
                packageJson["dependencies"] = regular;
            if (vpm.Count > 0)
            {
                packageJson["vpmDependencies"] = vpm;
                packageJson["vpmRepositories"] = JObject.FromObject(BuildCustomVpmRepositories(vpmDependencies));
            }




            if (packageJson.TryGetValue("yucp", out JToken existingYucp) &&
                existingYucp is JObject existingYucpObject &&
                (string.Equals(
                        (string)existingYucpObject["kind"],
                        "alias-v1",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        (string)existingYucpObject["kind"],
                        "alias-v2",
                        StringComparison.Ordinal)))
            {
                packageJson.Remove("yucp");
            }


            return packageJson.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static bool TryGetVpmDependencyRepoInfo(
            PackageDependency dep,
            out string repoName,
            out string repoUrl,
            out bool hasDownloadUrl,
            out List<string> checkedUrls,
            out string lookupError)
        {
            string key = dep == null ? "" : dep.packageName + "|" + dep.vpmRepositoryUrl + "|" + dep.GetVpmVersionRequirement() + "|" + dep.isVpmDependency;
            if (repoLookups.TryGetValue(key, out var hit) && hit.expires > DateTime.UtcNow)
            {
                (repoName, repoUrl, hasDownloadUrl, lookupError) = (hit.repoName, hit.repoUrl, hit.hasDownloadUrl, hit.error);
                checkedUrls = new List<string>(hit.checkedUrls);
                return hit.ok;
            }
            bool ok = ResolveVpmDependencyRepoInfo(dep, out repoName, out repoUrl, out hasDownloadUrl, out checkedUrls, out lookupError);
            repoLookups[key] = (DateTime.UtcNow + LookupLifetime, ok, repoName, repoUrl, hasDownloadUrl, new List<string>(checkedUrls), lookupError);
            return ok;
        }

        private static bool ResolveVpmDependencyRepoInfo(
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

            if (TryResolveVpmRepository(dep, out var resolvedRepo, out var resolvedJson, out checkedUrls, out lookupError))
            {
                repoName = resolvedRepo.name;
                repoUrl = !string.IsNullOrEmpty(resolvedRepo.url) ? resolvedRepo.url : resolvedRepo.localPath;
                hasDownloadUrl = RepoHasDownloadUrlForPackage(resolvedJson, dep, dep.packageName, out _);
                return true;
            }

            return false;
        }

        public static bool TryGetAvailableVpmVersions(
            PackageDependency dep,
            out List<string> versions,
            out string repoName,
            out string repoUrl,
            out string lookupError)
        {
            string key = dep == null ? "" : dep.packageName + "|" + dep.vpmRepositoryUrl + "|" + dep.isVpmDependency;
            if (versionLookups.TryGetValue(key, out var hit) && hit.expires > DateTime.UtcNow)
            {
                (repoName, repoUrl, lookupError) = (hit.repoName, hit.repoUrl, hit.error);
                versions = new List<string>(hit.versions);
                return hit.ok;
            }
            bool ok = ResolveAvailableVpmVersions(dep, out versions, out repoName, out repoUrl, out lookupError);
            versionLookups[key] = (DateTime.UtcNow + LookupLifetime, ok, new List<string>(versions), repoName, repoUrl, lookupError);
            return ok;
        }

        private static bool ResolveAvailableVpmVersions(
            PackageDependency dep,
            out List<string> versions,
            out string repoName,
            out string repoUrl,
            out string lookupError)
        {
            versions = new List<string>();
            repoName = "";
            repoUrl = "";
            lookupError = "";

            if (dep == null || !dep.isVpmDependency)
            {
                lookupError = "Not a VPM dependency";
                return false;
            }

            if (string.IsNullOrWhiteSpace(dep.packageName))
            {
                lookupError = "Package name is empty";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(dep.vpmRepositoryUrl))
            {
                var customRepo = new VpmRepositorySource
                {
                    name = "Custom",
                    url = dep.vpmRepositoryUrl.Trim(),
                    localPath = null
                };

                if (TryLoadRepoJson(customRepo, out var customJson) &&
                    TryGetRepoVersionsForPackage(customJson, dep.packageName, out versions))
                {
                    repoName = customRepo.name;
                    repoUrl = customRepo.url;
                    return true;
                }
            }

            foreach (var repo in GetVpmRepositorySources())
            {
                if (!TryLoadRepoJson(repo, out var repoJson))
                    continue;

                if (TryGetRepoVersionsForPackage(repoJson, dep.packageName, out versions))
                {
                    repoName = repo.name;
                    repoUrl = !string.IsNullOrEmpty(repo.url) ? repo.url : repo.localPath;
                    return true;
                }
            }

            lookupError = "Repositories are loading or no downloadable versions were found";
            return false;
        }

        private static List<VpmRepositorySource> GetVpmRepositorySources()
        {
            string root = GetCreatorCompanionRoot();
            string settings = string.IsNullOrEmpty(root) ? null : Path.Combine(root, "settings.json");
            var stamp = settings != null && File.Exists(settings) ? File.GetLastWriteTimeUtc(settings) : DateTime.MinValue;
            if (repoSources == null || stamp != repoSourcesStamp)
            {
                repoSources = ReadVpmRepositorySources();
                repoSourcesStamp = stamp;
                ClearLookups();
            }
            return new List<VpmRepositorySource>(repoSources);
        }

        private static List<VpmRepositorySource> ReadVpmRepositorySources()
        {
            var repos = new List<VpmRepositorySource>();
            string creatorCompanionRoot = GetCreatorCompanionRoot();
            string reposRoot = string.IsNullOrEmpty(creatorCompanionRoot)
                ? null
                : Path.Combine(creatorCompanionRoot, "Repos");
            string settingsPath = string.IsNullOrEmpty(creatorCompanionRoot)
                ? null
                : Path.Combine(creatorCompanionRoot, "settings.json");

            repos.Add(new VpmRepositorySource
            {
                name = "VRChat Official",
                url = "https://packages.vrchat.com/official?download",
                localPath = string.IsNullOrEmpty(reposRoot) ? null : Path.Combine(reposRoot, "vrc-official.json")
            });

            repos.Add(new VpmRepositorySource
            {
                name = "VRChat Curated",
                url = "https://packages.vrchat.com/curated?download",
                localPath = string.IsNullOrEmpty(reposRoot) ? null : Path.Combine(reposRoot, "vrc-curated.json")
            });

            try
            {
                if (!string.IsNullOrEmpty(settingsPath) && File.Exists(settingsPath))
                {
                    var settings = JObject.Parse(File.ReadAllText(settingsPath));
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

        private static string GetCreatorCompanionRoot()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    return string.IsNullOrWhiteSpace(localAppData)
                        ? null
                        : Path.Combine(localAppData, "VRChatCreatorCompanion");

                case RuntimePlatform.LinuxEditor:
                    string xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                    string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                    string linuxDataRoot = !string.IsNullOrWhiteSpace(xdgDataHome)
                        ? xdgDataHome
                        : (!string.IsNullOrWhiteSpace(homeDirectory)
                            ? Path.Combine(homeDirectory, ".local", "share")
                            : null);
                    return string.IsNullOrWhiteSpace(linuxDataRoot)
                        ? null
                        : Path.Combine(linuxDataRoot, "VRChatCreatorCompanion");

                case RuntimePlatform.OSXEditor:
                    string macHome = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                    return string.IsNullOrWhiteSpace(macHome)
                        ? null
                        : Path.Combine(macHome, "Library", "Application Support", "VRChatCreatorCompanion");

                default:
                    return null;
            }
        }

        private static bool TryLoadRepoJson(VpmRepositorySource repo, out JObject repoJson)
        {
            repoJson = null;
            if (!string.IsNullOrEmpty(repo.localPath) && File.Exists(repo.localPath))
            {
                DateTime modified = File.GetLastWriteTimeUtc(repo.localPath);
                if (RepoJsonCache.TryGetValue(repo.localPath, out var local) && local.lastWrite == modified)
                {
                    repoJson = local.json;
                    if (repoJson != null)
                        return true;
                }
                else
                {
                    local = new RepositoryCacheEntry { lastWrite = modified };
                    RepoJsonCache[repo.localPath] = local;
                    try
                    {
                        local.json = JObject.Parse(File.ReadAllText(repo.localPath));
                        repoJson = local.json;
                        return true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[DependencyScanner] Cannot read repository {repo.localPath}: {e.Message}");
                    }
                }
            }

            if (!IsValidDownloadUrl(repo.url))
                return false;
            if (RepoJsonCache.TryGetValue(repo.url, out var cached) &&
                (cached.request != null || cached.expires > DateTime.UtcNow))
            {
                repoJson = cached.json;
                return repoJson != null;
            }

            var entry = new RepositoryCacheEntry();
            RepoJsonCache[repo.url] = entry;
            var request = UnityWebRequest.Get(repo.url);
            request.timeout = 15;
            entry.request = request;
            request.SendWebRequest().completed += _ =>
            {
                try
                {
                    if (request.result == UnityWebRequest.Result.Success)
                        entry.json = JObject.Parse(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DependencyScanner] Invalid repository data from {repo.name}: {e.Message}");
                }
                finally
                {
                    entry.expires = DateTime.UtcNow.AddSeconds(entry.json == null ? 30 : 300);
                    entry.request = null;
                    request.Dispose();
                }
                RepositoriesChanged?.Invoke();
            };
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

            string requirement = dep.GetVpmVersionRequirement();
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

        private static bool TryResolveVpmRepository(
            PackageDependency dep,
            out VpmRepositorySource resolvedRepo,
            out JObject resolvedJson,
            out List<string> checkedUrls,
            out string lookupError)
        {
            resolvedRepo = null;
            resolvedJson = null;
            checkedUrls = new List<string>();
            lookupError = "";

            string packageName = dep?.packageName;
            if (string.IsNullOrWhiteSpace(packageName))
            {
                lookupError = "Package name is empty";
                return false;
            }

            var repoSources = GetVpmRepositorySources();
            checkedUrls = repoSources
                .Select(r => !string.IsNullOrEmpty(r.url) ? r.url : r.localPath)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (!string.IsNullOrWhiteSpace(dep.vpmRepositoryUrl))
            {
                string customUrl = dep.vpmRepositoryUrl.Trim();
                checkedUrls.Insert(0, customUrl);

                var customRepo = new VpmRepositorySource
                {
                    name = "Custom",
                    url = customUrl,
                    localPath = null
                };

                if (TryLoadRepoJson(customRepo, out var customJson) &&
                    RepoHasDownloadUrlForPackage(customJson, dep, packageName, out _))
                {
                    resolvedRepo = customRepo;
                    resolvedJson = customJson;
                    return true;
                }
            }

            foreach (var repo in repoSources)
            {
                if (!TryLoadRepoJson(repo, out var repoJson))
                    continue;

                if (RepoHasDownloadUrlForPackage(repoJson, dep, packageName, out _))
                {
                    resolvedRepo = repo;
                    resolvedJson = repoJson;
                    return true;
                }
            }

            lookupError = "Repositories are loading or no matching download URL was found";
            return false;
        }

        private static bool TryGetRepoVersionsForPackage(JObject repoJson, string packageName, out List<string> versions)
        {
            versions = new List<string>();

            var packages = GetRepoPackagesObject(repoJson);
            var packageObj = packages?[packageName] as JObject;
            var versionObjects = packageObj?["versions"] as JObject;
            if (versionObjects == null || versionObjects.Count == 0)
                return false;

            versions = versionObjects.Properties()
                .Where(p => SemanticVersioning.Version.TryParse(p.Name, out _) && IsValidDownloadUrl((p.Value as JObject)?["url"]?.ToString()))
                .Select(p => p.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(v => v, Comparer<string>.Create(CompareVersions))
                .ToList();

            return versions.Count > 0;
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

        private static Dictionary<string, string> BuildCustomVpmRepositories(List<PackageDependency> dependencies)
        {
            var repositories = new Dictionary<string, string>();
            foreach (var dependency in dependencies)
            {
                if (!string.IsNullOrWhiteSpace(dependency.vpmRepositoryUrl))
                {
                    string url = dependency.vpmRepositoryUrl.Trim();
                    if (!IsValidDownloadUrl(url))
                        throw new InvalidOperationException($"Invalid repository URL for {dependency.packageName}.");
                    repositories[dependency.packageName] = url;
                    continue;
                }

                if (!TryGetVpmDependencyRepoInfo(dependency, out _, out string repoUrl, out bool available, out _, out string error) ||
                    !available || !IsValidDownloadUrl(repoUrl))
                    throw new InvalidOperationException($"Cannot resolve {dependency.packageName}: {error} Set its repository URL or wait for repository loading to finish and retry.");
                repositories[dependency.packageName] = repoUrl;
            }
            return repositories;
        }

        internal static bool VersionSatisfiesRequirement(string version, string requirement) =>
            SemanticVersioning.Version.TryParse(version, out var parsedVersion) &&
            SemanticVersioning.Range.TryParse(string.IsNullOrWhiteSpace(requirement) ? "*" : requirement, out var range) &&
            range.IsSatisfied(parsedVersion);

        internal static int CompareVersions(string left, string right)
        {
            bool leftValid = SemanticVersioning.Version.TryParse(left, out var leftVersion);
            bool rightValid = SemanticVersioning.Version.TryParse(right, out var rightVersion);
            if (!leftValid || !rightValid)
                return leftValid.CompareTo(rightValid);
            return leftVersion.CompareTo(rightVersion);
        }
    }
}
