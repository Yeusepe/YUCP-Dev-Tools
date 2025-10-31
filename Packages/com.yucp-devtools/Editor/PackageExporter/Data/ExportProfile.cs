using System;
using System.Collections.Generic;
using System.IO;
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
        [Header("Package Metadata")]
        [Tooltip("Package file name (without .unitypackage extension)")]
        public string packageName = "MyPackage";
        
        [Tooltip("Package version (e.g., 1.0.0)")]
        public string version = "1.0.0";
        
        [Tooltip("Author name")]
        public string author = "";
        
        [Tooltip("Package description")]
        [TextArea(3, 6)]
        public string description = "";
        
        [Header("Package Icon")]
        [Tooltip("Optional PNG icon for the package")]
        public Texture2D icon;
        
        [Header("Export Folders")]
        [Tooltip("List of folder paths to include in the package (relative to project root)")]
        public List<string> foldersToExport = new List<string>();
        
        [Header("Export Inspector")]
        [Tooltip("Discovered assets from export folders (populated by scanning)")]
        public List<DiscoveredAsset> discoveredAssets = new List<DiscoveredAsset>();
        
        [Tooltip("Folders to permanently ignore from all exports (like .gitignore)")]
        public List<string> permanentIgnoreFolders = new List<string>();
        
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
        public bool includeDependencies = true;
        
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
        
        [Header("Export Settings")]
        [Tooltip("Default export path (leave empty for Desktop)")]
        public string exportPath = "";
        
        [Tooltip("Custom location to save this export profile (leave empty for default Profiles folder)")]
        public string profileSaveLocation = "";
        
        [Tooltip("Automatically increment version number after each export")]
        public bool autoIncrementVersion = false;
        
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
        /// Increment the patch version number (e.g., 1.0.0 -> 1.0.1)
        /// </summary>
        private void IncrementVersion()
        {
            try
            {
                string[] parts = version.Split('.');
                if (parts.Length >= 3)
                {
                    if (int.TryParse(parts[2], out int patch))
                    {
                        parts[2] = (patch + 1).ToString();
                        version = string.Join(".", parts);
                    }
                }
            }
            catch
            {
                // If version format is invalid, don't increment
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
            
            if (foldersToExport == null || foldersToExport.Count == 0)
            {
                errorMessage = "At least one folder must be selected for export";
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
            
            return true;
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
            return isFolder ? assetPath : Path.GetDirectoryName(assetPath);
        }
    }
}

