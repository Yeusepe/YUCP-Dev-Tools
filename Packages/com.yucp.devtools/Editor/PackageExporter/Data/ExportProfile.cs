using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Configuration profile for Unity package exports with optional obfuscation.
    /// Stores all settings needed to reproducibly export a package.
    /// </summary>
    [CreateAssetMenu(fileName = "New Export Profile", menuName = "YUCP/Export Profile", order = 100)]
    public class ExportProfile : ScriptableObject
    {
        [Header("Profile Settings")]
        [Tooltip("Display name for this export profile")]
        public string profileName = "";

        [Tooltip("Folder name for grouping in the package list")]
        public string folderName = "";
        
        [Header("Package Metadata")]
        [Tooltip("Package file name (without .unitypackage extension)")]
        public string packageName = "MyPackage";
        
        [Tooltip("Package version (e.g., 1.0.0)")]
        public string version = "1.0.0";
        
        [Tooltip("Package ID (assigned by server when signed, can be used to revoke/remove package)")]
        public string packageId = "";
        
        [Tooltip("Gumroad Product ID (for Gumroad integration)")]
        public string gumroadProductId = "";
        
        [Tooltip("Jinxxy Product ID (for Jinxxy integration)")]
        public string jinxxyProductId = "";
        
        [Tooltip("Author name(s) - separate multiple authors with commas")]
        public string author = "";
        
        [Tooltip("Package description")]
        [TextArea(3, 6)]
        public string description = "";
        
        [Header("Official Product Links")]
        [Tooltip("Links to where the product is or will be hosted")]
        public List<ProductLink> productLinks = new List<ProductLink>();
        
        [Header("Package Icon")]
        [Tooltip("Optional PNG icon for the package")]
        public Texture2D icon;
        
        [Header("Package Banner")]
        [Tooltip("Optional banner image for the package (displayed at the top of the exporter window)")]
        public Texture2D banner;
        
        [Header("Export Folders")]
        [Tooltip("List of folder paths to include in the package (relative to project root)")]
        public List<string> foldersToExport = new List<string>();
        
        [Header("Bundled Profiles")]
        [Tooltip("Other export profiles whose assets will be bundled into this package when exported")]
        [SerializeField] private List<string> includedProfileGuids = new List<string>();
        
        [Tooltip("Bundle all assets from bundled profiles into one package")]
        public bool bundleIncludedProfiles = true;
        
        [Tooltip("Also export each bundled profile separately (in addition to the composite package)")]
        public bool alsoExportIncludedSeparately = false;
        
        [Header("Export Inspector")]
        [Tooltip("Discovered assets from export folders (populated by scanning)")]
        public List<DiscoveredAsset> discoveredAssets = new List<DiscoveredAsset>();
        
        [Tooltip("Inspector height in pixels (per-profile preference)")]
        [SerializeField] private float inspectorHeight = 500f;
        
        public float InspectorHeight
        {
            get => inspectorHeight > 0 ? inspectorHeight : 500f; // Default to 500 if not set
            set
            {
                if (inspectorHeight != value)
                {
                    inspectorHeight = Mathf.Max(200f, value); // Enforce minimum
                    UnityEditor.EditorUtility.SetDirty(this);
                }
            }
        }
        
        [Tooltip("Folders to permanently ignore from all exports (like .gitignore)")]
        [SerializeField] private List<string> permanentIgnoreFolders = new List<string>();
        
        [Tooltip("GUIDs for ignored folders (for rename safety)")]
        [SerializeField] private List<string> permanentIgnoreFolderGuids = new List<string>();
        
        public List<string> PermanentIgnoreFolders => permanentIgnoreFolders ??= new List<string>();
        public List<string> PermanentIgnoreFolderGuids => permanentIgnoreFolderGuids ??= new List<string>();
        
        [Tooltip("Cache of last scan results for UI performance")]
        [SerializeField] private bool hasScannedAssets = false;
        
        public bool HasScannedAssets => hasScannedAssets;
        
        public void MarkScanned() => hasScannedAssets = true;
        public void ClearScan() 
        { 
            discoveredAssets.Clear();
            hasScannedAssets = false;
        }
        
        [Header("Unity Export Options")]
        [Tooltip("Include dependencies of selected assets")]
        public bool includeDependencies = false;
        
        [Tooltip("Recursively include all files in selected folders")]
        public bool recurseFolders = true;
        
        [Header("Exclusion Filters")]
        [Tooltip("File patterns to exclude (e.g., *.tmp, *.log)")]
        public List<string> excludeFilePatterns = new List<string>();
        
        [Tooltip("Folder names to exclude (e.g., .git, Temp)")]
        public List<string> excludeFolderNames = new List<string>();
        
        [Header("Package Dependencies")]
        [Tooltip("Configure how dependencies are handled in the export")]
        public List<PackageDependency> dependencies = new List<PackageDependency>();
        
        [Tooltip("Generate/update package.json with dependency information")]
        public bool generatePackageJson = true;
        
        [Header("Assembly Obfuscation")]
        [Tooltip("Enable ConfuserEx obfuscation for selected assemblies")]
        public bool enableObfuscation = false;
        
        [Tooltip("Obfuscation protection level")]
        public ConfuserExPreset obfuscationPreset = ConfuserExPreset.Normal;
        
        [Tooltip("Assembly names to obfuscate (from .asmdef files)")]
        public List<AssemblyObfuscationSettings> assembliesToObfuscate = new List<AssemblyObfuscationSettings>();
        
        [Tooltip("Strip debug symbols from obfuscated assemblies")]
        public bool stripDebugSymbols = true;
        
        [Tooltip("Advanced obfuscation settings for fine-grained control")]
        public ObfuscationSettings advancedObfuscationSettings = new ObfuscationSettings();
        
        [Header("Export Settings")]
        [Tooltip("Default export path (leave empty for Desktop)")]
        public string exportPath = "";
        
        [Tooltip("Custom location to save this export profile (leave empty for default Profiles folder)")]
        public string profileSaveLocation = "";
        
        [Tooltip("Automatically increment version number after each export")]
        public bool autoIncrementVersion = false;
        
        [Tooltip("Version increment strategy (which part to increment)")]
        public VersionIncrementStrategy incrementStrategy = VersionIncrementStrategy.Patch;
        
        [Tooltip("When auto-increment is enabled, also scan and bump @bump directives in export folders")]
        public bool bumpDirectivesInFiles = true;
        
        [Tooltip("Custom version rule to use (leave empty to use default semver)")]
        public CustomVersionRule customVersionRule;
        
        [Header("Tags")]
        [Tooltip("Preset tags selected for this profile")]
        public List<string> presetTags = new List<string>();
        
        [Tooltip("Custom tags added to this profile")]
        public List<string> customTags = new List<string>();

        [Header("Custom Update Steps")]
        [Tooltip("Custom steps to run when updating this package. Steps are only run if enabled.")]
        public UpdateStepList updateSteps = new UpdateStepList();
        
        /// <summary>
        /// Available preset tag options
        /// </summary>
        public static readonly List<string> AvailablePresetTags = new List<string>
        {
            "Production",
            "Beta",
            "Archived",
            "Active",
            "Deprecated",
            "Experimental",
            "Stable",
            "WIP"
        };
        
        [Header("Statistics (Read-only)")]
        [Tooltip("Last successful export timestamp")]
        [SerializeField] private string lastExportTime = "";
        
        [Tooltip("Number of times this profile has been exported")]
        [SerializeField] private int exportCount = 0;
        
        public string LastExportTime => lastExportTime;
        public int ExportCount => exportCount;
        
        /// <summary>
        /// Update statistics after successful export
        /// </summary>
        /// <summary>
        /// Unlink (clear) the packageId
        /// Next export will generate a new one
        /// </summary>
        public void UnlinkPackageId()
        {
            packageId = "";
            UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// Reset export statistics (last export time, export count). Used when cloning as template.
        /// </summary>
        public void ResetExportStats()
        {
            lastExportTime = "";
            exportCount = 0;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        public void RecordExport()
        {
            lastExportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            exportCount++;
            
            if (autoIncrementVersion)
            {
                IncrementVersion();
            }
        }
        
        /// <summary>
        /// Increment the version number using the configured strategy
        /// </summary>
        private void IncrementVersion()
        {
            // Determine which rule to use
            string ruleName = "semver"; // Default
            
            if (customVersionRule != null)
            {
                customVersionRule.RegisterRule();
                ruleName = customVersionRule.ruleName;
            }
            
            // Bump the profile's version using the selected rule
            var options = VersionUtility.ConvertStrategyToOptions(incrementStrategy);
            version = VersionUtility.BumpVersionWithRule(version, ruleName, options);
            
            // Also bump versions in files with @bump directives if enabled
            if (bumpDirectivesInFiles && foldersToExport != null && foldersToExport.Count > 0)
            {
                BumpDirectivesInExportFolders();
            }
        }
        
        /// <summary>
        /// Scan export folders and bump versions according to @bump directives
        /// </summary>
        private void BumpDirectivesInExportFolders()
        {
            try
            {
                var options = VersionUtility.ConvertStrategyToOptions(incrementStrategy);
                var results = ProjectVersionScanner.BumpVersionsInProfile(this, writeBack: true, defaultOptions: options);
                
                if (results.Count > 0)
                {
                    int successful = results.Count(r => r.Success);
                    int failed = results.Count(r => !r.Success);
                    
                    UnityEngine.Debug.Log($"[YUCP] Smart version bump: {successful} successful, {failed} failed");
                    
                    foreach (var result in results.Where(r => r.Success))
                    {
                        UnityEngine.Debug.Log($"[YUCP] {result}");
                    }
                    
                    foreach (var result in results.Where(r => !r.Success))
                    {
                        UnityEngine.Debug.LogWarning($"[YUCP] {result}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[YUCP] Failed to bump directives in files: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get the full output file path for this export
        /// </summary>
        public string GetOutputFilePath()
        {
            string fileName = $"{packageName}_{version}.unitypackage";
            
            if (string.IsNullOrEmpty(exportPath))
            {
                // Default to Desktop
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                return System.IO.Path.Combine(desktop, fileName);
            }
            
            return System.IO.Path.Combine(exportPath, fileName);
        }
        
        /// <summary>
        /// Validate the profile settings
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            errorMessage = "";
            
            if (string.IsNullOrWhiteSpace(packageName))
            {
                errorMessage = "Package name is required";
                return false;
            }
            
            if (string.IsNullOrWhiteSpace(version))
            {
                errorMessage = "Version is required";
                return false;
            }
            
            if (!VersionUtility.IsValidVersion(version))
            {
                errorMessage = $"Invalid version format: '{version}'. Expected format: X.Y.Z (e.g., 1.0.0)";
                return false;
            }
            
            // Allow composite profiles without folders (they get assets from bundled profiles)
            if ((foldersToExport == null || foldersToExport.Count == 0) && !HasIncludedProfiles())
            {
                errorMessage = "At least one folder must be selected for export, or bundle other profiles";
                return false;
            }
            
            // Validate that folders exist
            foreach (string folder in foldersToExport)
            {
                if (!System.IO.Directory.Exists(folder))
                {
                    errorMessage = $"Folder does not exist: {folder}";
                    return false;
                }
            }
            
            if (enableObfuscation && (assembliesToObfuscate == null || assembliesToObfuscate.Count == 0))
            {
                errorMessage = "Obfuscation is enabled but no assemblies are selected";
                return false;
            }
            
            // Validate composite profile (bundled profiles)
            if (HasIncludedProfiles())
            {
                List<string> compositeErrors;
                if (!CompositeProfileResolver.ValidateIncludedProfiles(this, out compositeErrors))
                {
                    errorMessage = "Bundled profiles validation failed: " + string.Join("; ", compositeErrors);
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Get all included profiles (resolves GUIDs to ExportProfile objects)
        /// </summary>
        public List<ExportProfile> GetIncludedProfiles()
        {
            var profiles = new List<ExportProfile>();
            
            if (includedProfileGuids == null || includedProfileGuids.Count == 0)
                return profiles;
            
            foreach (string guid in includedProfileGuids)
            {
                if (string.IsNullOrEmpty(guid))
                    continue;
                
                string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                    continue;
                
                var profile = UnityEditor.AssetDatabase.LoadAssetAtPath<ExportProfile>(assetPath);
                if (profile != null)
                {
                    profiles.Add(profile);
                }
            }
            
            return profiles;
        }
        
        /// <summary>
        /// Add an included profile (prevents duplicates)
        /// </summary>
        public void AddIncludedProfile(ExportProfile profile)
        {
            if (profile == null)
                return;
            
            if (includedProfileGuids == null)
                includedProfileGuids = new List<string>();
            
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(profile));
            if (string.IsNullOrEmpty(guid))
                return;
            
            // Prevent adding self
            string selfGuid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(this));
            if (guid == selfGuid)
                return;
            
            // Prevent duplicates
            if (!includedProfileGuids.Contains(guid))
            {
                includedProfileGuids.Add(guid);
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
        
        /// <summary>
        /// Remove an included profile
        /// </summary>
        public void RemoveIncludedProfile(ExportProfile profile)
        {
            if (profile == null || includedProfileGuids == null)
                return;
            
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(profile));
            if (string.IsNullOrEmpty(guid))
                return;
            
            if (includedProfileGuids.Remove(guid))
            {
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
        
        /// <summary>
        /// Check if this profile has any included profiles
        /// </summary>
        public bool HasIncludedProfiles()
        {
            return includedProfileGuids != null && includedProfileGuids.Count > 0;
        }
        
        /// <summary>
        /// Get all tags (preset + custom)
        /// </summary>
        public List<string> GetAllTags()
        {
            var allTags = new List<string>();
            if (presetTags != null) allTags.AddRange(presetTags);
            if (customTags != null) allTags.AddRange(customTags);
            return allTags;
        }
        
        /// <summary>
        /// Check if profile has a specific tag (preset or custom)
        /// </summary>
        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            return (presetTags != null && presetTags.Contains(tag)) ||
                   (customTags != null && customTags.Contains(tag));
        }
        
        /// <summary>
        /// Add a preset tag
        /// </summary>
        public void AddPresetTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || !AvailablePresetTags.Contains(tag)) return;
            if (presetTags == null) presetTags = new List<string>();
            if (!presetTags.Contains(tag))
            {
                presetTags.Add(tag);
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
        
        /// <summary>
        /// Remove a preset tag
        /// </summary>
        public void RemovePresetTag(string tag)
        {
            if (presetTags != null && presetTags.Remove(tag))
            {
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
        
        /// <summary>
        /// Add a custom tag
        /// </summary>
        public void AddCustomTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            tag = tag.Trim();
            if (customTags == null) customTags = new List<string>();
            if (!customTags.Contains(tag))
            {
                customTags.Add(tag);
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
        
        /// <summary>
        /// Remove a custom tag
        /// </summary>
        public void RemoveCustomTag(string tag)
        {
            if (customTags != null && customTags.Remove(tag))
            {
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
    }
    
    /// <summary>
    /// Settings for obfuscating a single assembly
    /// </summary>
    [Serializable]
    public class AssemblyObfuscationSettings
    {
        [Tooltip("Enable obfuscation for this assembly")]
        public bool enabled = true;
        
        [Tooltip("Assembly name (from .asmdef file)")]
        public string assemblyName = "";
        
        [Tooltip("Path to the .asmdef file (for reference)")]
        public string asmdefPath = "";
        
        public AssemblyObfuscationSettings()
        {
        }
        
        public AssemblyObfuscationSettings(string name, string path)
        {
            assemblyName = name;
            asmdefPath = path;
            enabled = true;
        }
    }
    
    /// <summary>
    /// Represents a discovered asset from export folder scanning
    /// </summary>
    [Serializable]
    public class DiscoveredAsset
    {
        [Tooltip("Asset path relative to project root")]
        public string assetPath;
        
        [Tooltip("Whether this asset is included in export")]
        public bool included = true;
        
        [Tooltip("Whether this is a folder (vs file)")]
        public bool isFolder;
        
        [Tooltip("Source export folder that discovered this asset")]
        public string sourceFolder;
        
        [Tooltip("Asset type (e.g., 'Prefab', 'Script', 'Material')")]
        public string assetType;
        
        [Tooltip("File size in bytes (0 for folders)")]
        public long fileSize;
        
        [Tooltip("Is this asset a dependency of a directly exported asset")]
        public bool isDependency;
        
        [Tooltip("Source profile name for composite profiles")]
        public string sourceProfileName = "";
        
        public DiscoveredAsset()
        {
        }
        
        public DiscoveredAsset(string path, string source, bool isDir = false)
        {
            assetPath = path;
            sourceFolder = source;
            isFolder = isDir;
            included = true;
            isDependency = false;
            
            if (!isDir && File.Exists(path))
            {
                FileInfo fileInfo = new FileInfo(path);
                fileSize = fileInfo.Length;
                
                // Determine asset type from extension
                string ext = Path.GetExtension(path).ToLower();
                assetType = ext switch
                {
                    ".prefab" => "Prefab",
                    ".cs" => "Script",
                    ".shader" => "Shader",
                    ".mat" => "Material",
                    ".png" or ".jpg" or ".jpeg" => "Texture",
                    ".fbx" or ".obj" => "Model",
                    ".unity" => "Scene",
                    ".asmdef" => "Assembly Definition",
                    ".dll" => "Assembly",
                    ".anim" => "Animation",
                    ".controller" => "Animator",
                    ".asset" => "Asset",
                    _ => "File"
                };
            }
            else
            {
                assetType = "Folder";
                fileSize = 0;
            }
        }
        
        public string GetDisplayName()
        {
            return Path.GetFileName(assetPath);
        }
        
        public string GetFolderPath()
        {
            string path = isFolder ? assetPath : Path.GetDirectoryName(assetPath);
            
            // Ensure we return a relative path
            if (string.IsNullOrEmpty(path))
                return path;
            
            // If it's already relative (doesn't start with drive letter), return as-is
            if (!Path.IsPathRooted(path))
                return path.Replace('\\', '/');
            
            // Convert absolute path to relative
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (path.StartsWith(projectPath))
            {
                string relative = path.Substring(projectPath.Length);
                if (relative.StartsWith("\\") || relative.StartsWith("/"))
                {
                    relative = relative.Substring(1);
                }
                return relative.Replace('\\', '/');
            }
            
            return path.Replace('\\', '/');
        }
    }
    
    /// <summary>
    /// Represents an official product link with icon
    /// </summary>
    [Serializable]
    public class ProductLink
    {
        [Tooltip("Display label for the link (optional)")]
        public string label = "";
        
        [Tooltip("Full URL to the product page")]
        public string url = "";
        
        [Tooltip("Custom icon for this link (overrides auto-fetched favicon)")]
        public Texture2D customIcon;
        
        [Tooltip("Cached favicon/icon for the website (auto-fetched)")]
        public Texture2D icon;

        [Tooltip("Editor-only cache path for auto-fetched icons (stored outside Assets)")]
        public string cachedIconPath;
        
        public ProductLink()
        {
        }
        
        public ProductLink(string url, string label = "")
        {
            this.url = url;
            this.label = label;
        }
        
        /// <summary>
        /// Get the icon to display (custom icon takes priority over auto-fetched icon)
        /// </summary>
        public Texture2D GetDisplayIcon()
        {
            if (customIcon != null)
                return customIcon;

            if (icon == null && !string.IsNullOrEmpty(cachedIconPath) && File.Exists(cachedIconPath))
            {
                try
                {
                    byte[] data = File.ReadAllBytes(cachedIconPath);
                    var tex = new Texture2D(2, 2);
                    if (tex.LoadImage(data))
                    {
                        icon = tex;
                    }
                }
                catch { }
            }

            return icon;
        }
    }
}
