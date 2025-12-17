using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Version information for an exported package
    /// </summary>
    [Serializable]
    public class ExportedPackageVersion
    {
        public string version;
        public string archiveSha256;
        public string exportDate;
        public string exportPath;
    }

    /// <summary>
    /// Information about an exported package/app
    /// </summary>
    [Serializable]
    public class ExportedPackageInfo
    {
        public string packageId;
        public string packageName;
        public string publisherId;
        public List<ExportedPackageVersion> versions = new List<ExportedPackageVersion>();
        public string latestVersion;
        public bool hasUpdateAvailable;
    }

    /// <summary>
    /// Registry for tracking exported packages/apps
    /// Stored as ScriptableObject at Assets/YUCP/ExportedPackageRegistry.asset
    /// </summary>
    [CreateAssetMenu(fileName = "ExportedPackageRegistry", menuName = "YUCP/Exported Package Registry", order = 2)]
    public class ExportedPackageRegistry : ScriptableObject
    {
        [SerializeField]
        private List<ExportedPackageInfo> _exportedPackages = new List<ExportedPackageInfo>();

        private Dictionary<string, ExportedPackageInfo> _packageById = new Dictionary<string, ExportedPackageInfo>();
        private bool _dictionaryBuilt = false;

        private void OnEnable()
        {
            BuildDictionary();
        }

        /// <summary>
        /// Build lookup dictionary from serialized list
        /// </summary>
        private void BuildDictionary()
        {
            _packageById.Clear();

            foreach (var package in _exportedPackages)
            {
                if (package == null || string.IsNullOrEmpty(package.packageId))
                    continue;

                _packageById[package.packageId] = package;
            }

            _dictionaryBuilt = true;
        }

        /// <summary>
        /// Register or update an exported package version
        /// </summary>
        public void RegisterExport(string packageId, string packageName, string publisherId, string version, string archiveSha256, string exportPath)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                Debug.LogError("[ExportedPackageRegistry] Cannot register export without packageId");
                return;
            }

            if (!_dictionaryBuilt)
                BuildDictionary();

            ExportedPackageInfo packageInfo;
            
            if (_packageById.TryGetValue(packageId, out packageInfo))
            {
                // Update existing package
                packageInfo.packageName = packageName;
                packageInfo.publisherId = publisherId;
            }
            else
            {
                // Create new package entry
                packageInfo = new ExportedPackageInfo
                {
                    packageId = packageId,
                    packageName = packageName,
                    publisherId = publisherId,
                    versions = new List<ExportedPackageVersion>()
                };
                _exportedPackages.Add(packageInfo);
                _packageById[packageId] = packageInfo;
            }

            // Add version if it doesn't exist
            var existingVersion = packageInfo.versions.FirstOrDefault(v => v.version == version);
            if (existingVersion == null)
            {
                var newVersion = new ExportedPackageVersion
                {
                    version = version,
                    archiveSha256 = archiveSha256,
                    exportDate = DateTime.UtcNow.ToString("O"),
                    exportPath = exportPath
                };
                packageInfo.versions.Add(newVersion);
            }
            else
            {
                // Update existing version
                existingVersion.archiveSha256 = archiveSha256;
                existingVersion.exportDate = DateTime.UtcNow.ToString("O");
                existingVersion.exportPath = exportPath;
            }

            // Update latest version
            if (string.IsNullOrEmpty(packageInfo.latestVersion) || 
                CompareVersions(version, packageInfo.latestVersion) > 0)
            {
                packageInfo.latestVersion = version;
            }

            Save();
        }

        /// <summary>
        /// Get exported package by packageId
        /// </summary>
        public ExportedPackageInfo GetPackage(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                return null;

            if (!_dictionaryBuilt)
                BuildDictionary();

            _packageById.TryGetValue(packageId, out ExportedPackageInfo package);
            return package;
        }

        /// <summary>
        /// Get all exported packages
        /// </summary>
        public List<ExportedPackageInfo> GetAllPackages()
        {
            return new List<ExportedPackageInfo>(_exportedPackages.Where(p => p != null));
        }

        /// <summary>
        /// Compare two version strings (simple semantic version comparison)
        /// Returns: >0 if v1 > v2, 0 if equal, <0 if v1 < v2
        /// </summary>
        private int CompareVersions(string v1, string v2)
        {
            if (v1 == v2) return 0;
            if (string.IsNullOrEmpty(v1)) return -1;
            if (string.IsNullOrEmpty(v2)) return 1;

            // Simple split by dots and compare
            var parts1 = v1.Split('.');
            var parts2 = v2.Split('.');

            int maxLength = Math.Max(parts1.Length, parts2.Length);
            for (int i = 0; i < maxLength; i++)
            {
                int part1 = i < parts1.Length && int.TryParse(parts1[i], out int p1) ? p1 : 0;
                int part2 = i < parts2.Length && int.TryParse(parts2[i], out int p2) ? p2 : 0;

                if (part1 != part2)
                    return part1.CompareTo(part2);
            }

            return 0;
        }

        /// <summary>
        /// Save registry to disk
        /// </summary>
        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Get or create the registry instance
        /// </summary>
        public static ExportedPackageRegistry GetOrCreate()
        {
            string registryPath = "Assets/YUCP/ExportedPackageRegistry.asset";
            
            // Ensure YUCP directory exists
            string directory = Path.GetDirectoryName(registryPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            var registry = AssetDatabase.LoadAssetAtPath<ExportedPackageRegistry>(registryPath);
            
            if (registry == null)
            {
                registry = CreateInstance<ExportedPackageRegistry>();
                AssetDatabase.CreateAsset(registry, registryPath);
                AssetDatabase.SaveAssets();
            }

            return registry;
        }

        /// <summary>
        /// Load registry from disk
        /// </summary>
        public static ExportedPackageRegistry Load()
        {
            string registryPath = "Assets/YUCP/ExportedPackageRegistry.asset";
            return AssetDatabase.LoadAssetAtPath<ExportedPackageRegistry>(registryPath);
        }
    }
}





















