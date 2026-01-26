using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Scans export folders and collects all assets that will be included in the package.
    /// Provides detailed analysis for the Export Inspector UI.
    /// </summary>
    public static class AssetCollector
    {
        /// <summary>
        /// Scan all export folders and collect discovered assets with dependencies
        /// </summary>
        public static List<DiscoveredAsset> ScanExportFolders(ExportProfile profile, bool includeDependencies = true, string sourceProfileName = null)
        {
            var discoveredAssets = new List<DiscoveredAsset>();
            var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // First pass: collect all assets from export folders
            foreach (string folder in profile.foldersToExport)
            {
                if (string.IsNullOrWhiteSpace(folder))
                    continue;
                
                string fullPath = Path.GetFullPath(folder);
                
                if (!Directory.Exists(fullPath))
                {
                    Debug.LogWarning($"[AssetCollector] Folder does not exist: {fullPath}");
                    continue;
                }
                
                // Check if the export folder itself should be ignored
                string folderName = Path.GetFileName(fullPath);
                if (ShouldIgnoreFolder(fullPath, folderName, profile))
                {
                    Debug.Log($"[AssetCollector] Skipping excluded export folder: {fullPath}");
                    continue;
                }
                
                ScanFolder(fullPath, folder, profile, discoveredAssets, processedPaths, sourceProfileName);
            }
            
            // Second pass: collect dependencies if requested
            if (includeDependencies && profile.includeDependencies)
            {
                CollectDependencies(discoveredAssets, profile, processedPaths, sourceProfileName);
            }
            
            // Third pass: Post-process to remove any excluded items that might have slipped through
            discoveredAssets.RemoveAll(asset =>
            {
                if (asset.isFolder)
                {
                    string folderName = Path.GetFileName(asset.assetPath);
                    if (ShouldIgnoreFolder(asset.assetPath, folderName, profile))
                    {
                        Debug.Log($"[AssetCollector] Removing excluded folder from discovered assets: {asset.assetPath}");
                        return true;
                    }
                }
                else
                {
                    if (ShouldIgnoreFile(asset.assetPath, profile))
                    {
                        Debug.Log($"[AssetCollector] Removing excluded file from discovered assets: {asset.assetPath}");
                        return true;
                    }
                }
                return false;
            });
            
            // Sort by path for better UI display
            discoveredAssets.Sort((a, b) => string.Compare(a.assetPath, b.assetPath, StringComparison.OrdinalIgnoreCase));
            
            
            return discoveredAssets;
        }
        
        /// <summary>
        /// Recursively scan a folder and add all assets
        /// </summary>
        private static void ScanFolder(string folderPath, string sourceFolder, ExportProfile profile, 
            List<DiscoveredAsset> discoveredAssets, HashSet<string> processedPaths, string sourceProfileName = null)
        {
            // Check if this folder should be ignored
            string folderName = Path.GetFileName(folderPath);
            if (ShouldIgnoreFolder(folderPath, folderName, profile))
            {
                return;
            }
            
            // Add the folder itself
            if (!processedPaths.Contains(folderPath))
            {
                processedPaths.Add(folderPath);
                var asset = new DiscoveredAsset(folderPath, sourceFolder, isDir: true);
                if (!string.IsNullOrEmpty(sourceProfileName))
                    asset.sourceProfileName = sourceProfileName;
                discoveredAssets.Add(asset);
            }
            
            try
            {
                // Get all files in this folder
                string[] files = Directory.GetFiles(folderPath);
                foreach (string file in files)
                {
                    if (ShouldIgnoreFile(file, profile))
                        continue;
                    
                    if (!processedPaths.Contains(file))
                    {
                        processedPaths.Add(file);
                        var asset = new DiscoveredAsset(file, sourceFolder, isDir: false);
                        if (!string.IsNullOrEmpty(sourceProfileName))
                            asset.sourceProfileName = sourceProfileName;
                        discoveredAssets.Add(asset);
                    }
                }
                
                // Recursively scan subfolders if enabled
                if (profile.recurseFolders)
                {
                    string[] subdirs = Directory.GetDirectories(folderPath);
                    foreach (string subdir in subdirs)
                    {
                        ScanFolder(subdir, sourceFolder, profile, discoveredAssets, processedPaths, sourceProfileName);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetCollector] Error scanning folder {folderPath}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Collect dependencies for all discovered assets
        /// </summary>
        private static void CollectDependencies(List<DiscoveredAsset> discoveredAssets, ExportProfile profile, 
            HashSet<string> processedPaths, string sourceProfileName = null)
        {
            // Get list of direct assets (not folders, not already marked as dependencies)
            var directAssets = discoveredAssets
                .Where(a => !a.isFolder && !a.isDependency)
                .Select(a => a.assetPath)
                .ToList();
            
            if (directAssets.Count == 0)
                return;
            
            // Use Unity's AssetDatabase to get dependencies
            var allDependencies = new HashSet<string>();
            
            foreach (string assetPath in directAssets)
            {
                try
                {
                    // Convert to Unity-relative path (Assets/...)
                    string unityPath = GetUnityRelativePath(assetPath);
                    
                    if (string.IsNullOrEmpty(unityPath) || !AssetDatabase.IsValidFolder(Path.GetDirectoryName(unityPath)))
                        continue;
                    
                    // Get dependencies for this asset
                    string[] dependencies = AssetDatabase.GetDependencies(unityPath, recursive: false);
                    
                    foreach (string dep in dependencies)
                    {
                        // Skip self-reference
                        if (dep == unityPath)
                            continue;
                        
                        // Convert Unity-relative path to full path
                        string fullDepPath;
                        if (Path.IsPathRooted(dep))
                        {
                            // Already a full path
                            fullDepPath = Path.GetFullPath(dep);
                        }
                        else
                        {
                            // Unity-relative path - convert to full path
                            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                            fullDepPath = Path.GetFullPath(Path.Combine(projectRoot, dep.Replace('/', Path.DirectorySeparatorChar)));
                        }
                        
                        // Skip if already processed
                        if (processedPaths.Contains(fullDepPath))
                            continue;
                        
                        // Skip if in an ignored folder - check before adding
                        if (ShouldIgnoreFile(fullDepPath, profile))
                        {
                            Debug.Log($"[AssetCollector] Excluding dependency in ignored folder: {dep} ({fullDepPath})");
                            continue;
                        }
                        
                        allDependencies.Add(fullDepPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AssetCollector] Error getting dependencies for {assetPath}: {ex.Message}");
                }
            }
            
            // Add dependencies to discovered assets
            foreach (string depPath in allDependencies)
            {
                    if (!processedPaths.Contains(depPath))
                    {
                        processedPaths.Add(depPath);
                        var depAsset = new DiscoveredAsset(depPath, "[Dependency]", isDir: false);
                        depAsset.isDependency = true;
                        if (!string.IsNullOrEmpty(sourceProfileName))
                            depAsset.sourceProfileName = sourceProfileName;
                        discoveredAssets.Add(depAsset);
                    }
            }
            
        }
        
        /// <summary>
        /// Check if a folder should be ignored (public method for use by PackageBuilder and others)
        /// </summary>
        public static bool ShouldIgnoreFolder(string folderPath, string folderName, ExportProfile profile)
        {
            // Check .yucpignore files first (highest priority)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (YucpIgnoreHandler.ShouldIgnore(folderPath, projectRoot))
                return true;
            
            // Check permanent ignore list (using both path and GUID for rename safety)
            if (profile.PermanentIgnoreFolders != null && profile.PermanentIgnoreFolders.Count > 0)
            {
                string folderGuid = AssetDatabase.AssetPathToGUID(GetUnityRelativePath(folderPath));
                
                for (int i = 0; i < profile.PermanentIgnoreFolders.Count; i++)
                {
                    string ignorePath = profile.PermanentIgnoreFolders[i];
                    
                    // Normalize paths for comparison
                    string fullIgnorePath;
                    // Check if it's a Unity-relative path (starts with Assets/ or Packages/)
                    if (ignorePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || 
                        ignorePath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                    {
                        // Convert Unity-relative path to full path
                        fullIgnorePath = Path.GetFullPath(Path.Combine(projectRoot, ignorePath.Replace('/', Path.DirectorySeparatorChar)));
                    }
                    else
                    {
                        // Treat as full path
                        fullIgnorePath = Path.GetFullPath(ignorePath);
                    }
                    
                    string fullFolderPath = Path.GetFullPath(folderPath);
                    
                    // Normalize path separators for comparison
                    fullIgnorePath = fullIgnorePath.Replace("\\", "/");
                    fullFolderPath = fullFolderPath.Replace("\\", "/");
                    
                    // Check by path (handles subfolders)
                    if (fullFolderPath.Equals(fullIgnorePath, StringComparison.OrdinalIgnoreCase) ||
                        fullFolderPath.StartsWith(fullIgnorePath + "/", StringComparison.OrdinalIgnoreCase))
                        return true;
                    
                    // Check by GUID (handles renamed folders)
                    if (i < profile.PermanentIgnoreFolderGuids.Count)
                    {
                        string ignoreGuid = profile.PermanentIgnoreFolderGuids[i];
                        if (!string.IsNullOrEmpty(ignoreGuid) && !string.IsNullOrEmpty(folderGuid) && 
                            ignoreGuid.Equals(folderGuid, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            
            // Check exclude folder names (supports both folder names and full paths)
            if (profile.excludeFolderNames != null && profile.excludeFolderNames.Count > 0)
            {
                string fullFolderPath = Path.GetFullPath(folderPath);
                string unityRelativePath = GetUnityRelativePath(folderPath);
                
                foreach (string excludePattern in profile.excludeFolderNames)
                {
                    if (string.IsNullOrWhiteSpace(excludePattern))
                        continue;
                    
                    // Check if this is a path (contains path separator)
                    if (excludePattern.Contains(Path.DirectorySeparatorChar) || excludePattern.Contains('/') || excludePattern.Contains('\\'))
                    {
                        string fullExcludePath;
                        
                        // Check if it's a Unity-relative path (starts with Assets/ or Packages/)
                        if (excludePattern.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || 
                            excludePattern.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                        {
                            // Convert Unity-relative path to full path
                            fullExcludePath = Path.GetFullPath(Path.Combine(projectRoot, excludePattern.Replace('/', Path.DirectorySeparatorChar)));
                        }
                        else
                        {
                            // Treat as full path
                            fullExcludePath = Path.GetFullPath(excludePattern);
                        }
                        
                        // Check if folder path matches or is a subfolder of exclude path
                        if (fullFolderPath.Equals(fullExcludePath, StringComparison.OrdinalIgnoreCase) ||
                            fullFolderPath.StartsWith(fullExcludePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                            fullFolderPath.StartsWith(fullExcludePath + "/", StringComparison.OrdinalIgnoreCase) ||
                            fullFolderPath.StartsWith(fullExcludePath + "\\", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                        
                        // Also check Unity-relative path if we have one
                        if (!string.IsNullOrEmpty(unityRelativePath))
                        {
                            string excludeUnityPath = excludePattern.Replace('\\', '/');
                            if (unityRelativePath.Equals(excludeUnityPath, StringComparison.OrdinalIgnoreCase) ||
                                unityRelativePath.StartsWith(excludeUnityPath + "/", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                    else
                    {
                        // Treat as folder name - check if folder name matches
                        if (folderName.Equals(excludePattern, StringComparison.OrdinalIgnoreCase))
                            return true;
                        
                        // Also check if the name appears anywhere in the path
                        string[] pathParts = fullFolderPath.Split(Path.DirectorySeparatorChar, '/', '\\');
                        if (pathParts.Any(part => part.Equals(excludePattern, StringComparison.OrdinalIgnoreCase)))
                            return true;
                        
                        // Check in Unity-relative path as well
                        if (!string.IsNullOrEmpty(unityRelativePath))
                        {
                            string[] unityPathParts = unityRelativePath.Split('/');
                            if (unityPathParts.Any(part => part.Equals(excludePattern, StringComparison.OrdinalIgnoreCase)))
                                return true;
                        }
                    }
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if a file should be ignored (public method for use by PackageBuilder and others)
        /// </summary>
        public static bool ShouldIgnoreFile(string filePath, ExportProfile profile)
        {
            string fileName = Path.GetFileName(filePath);
            string extension = Path.GetExtension(filePath);
            
            // Check .yucpignore files first (highest priority)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (YucpIgnoreHandler.ShouldIgnore(filePath, projectRoot))
                return true;
            
            // Ignore .meta files (Unity handles these)
            if (extension == ".meta")
                return true;
            
            // NOTE: We do NOT exclude derived FBX files here during collection
            // They need to be collected so ConvertDerivedFbxToPatchAssets can convert them to PatchPackages
            // They will be removed from the export list during conversion, and a final safety check ensures
            // no derived FBX files remain in the export package
            
            // Check if file is in an ignored folder
            string folderPath = Path.GetDirectoryName(filePath);
            string folderName = Path.GetFileName(folderPath);
            if (ShouldIgnoreFolder(folderPath, folderName, profile))
                return true;
            
            // Check exclude file patterns
            if (profile.excludeFilePatterns != null)
            {
                foreach (string pattern in profile.excludeFilePatterns)
                {
                    if (IsFileMatchingPattern(fileName, pattern))
                        return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if a filename matches a pattern (supports * wildcards) (public method for reuse)
        /// </summary>
        public static bool IsFileMatchingPattern(string fileName, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;
            
            // Simple wildcard matching
            if (pattern.StartsWith("*"))
            {
                string suffix = pattern.Substring(1);
                return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            }
            else if (pattern.EndsWith("*"))
            {
                string prefix = pattern.Substring(0, pattern.Length - 1);
                return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            else if (pattern.Contains("*"))
            {
                // More complex wildcard - use regex-like matching
                string[] parts = pattern.Split('*');
                int currentIndex = 0;
                
                foreach (string part in parts)
                {
                    int foundIndex = fileName.IndexOf(part, currentIndex, StringComparison.OrdinalIgnoreCase);
                    if (foundIndex < 0)
                        return false;
                    currentIndex = foundIndex + part.Length;
                }
                
                return true;
            }
            else
            {
                // Exact match
                return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            }
        }
        
        /// <summary>
        /// Convert full path to Unity-relative path (Assets/... or Packages/...) (public method for reuse)
        /// </summary>
        public static string GetUnityRelativePath(string fullPath)
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            fullPath = Path.GetFullPath(fullPath);
            
            if (fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = fullPath.Substring(projectPath.Length + 1);
                return relativePath.Replace("\\", "/");
            }
            
            return null;
        }
        
        /// <summary>
        /// Check if an FBX file is marked as derived (should be converted to PatchPackage instead of exported)
        /// Handles both Unity-relative paths and full paths.
        /// Returns false if the file is not an FBX, cannot be loaded, or is not marked as derived.
        /// </summary>
        public static bool IsDerivedFbx(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;
            
            if (!filePath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return false;
            
            string unityPath = null;
            
            // Try to convert to Unity-relative path
            try
            {
                unityPath = GetUnityRelativePath(filePath);
                
                // If conversion failed, try using the path directly if it's already a Unity-relative path
                if (string.IsNullOrEmpty(unityPath))
                {
                    string normalizedPath = filePath.Replace('\\', '/');
                    if (normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || 
                        normalizedPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                    {
                        unityPath = normalizedPath;
                    }
                    else
                    {
                        // Cannot determine Unity path - assume not derived to be safe
                        return false;
                    }
                }
            }
            catch
            {
                // If path conversion fails, assume not derived to be safe
                return false;
            }
            
            // Get ModelImporter for this FBX
            ModelImporter importer = null;
            try
            {
                importer = AssetImporter.GetAtPath(unityPath) as ModelImporter;
                if (importer == null)
                    return false;
            }
            catch
            {
                // If we can't get the importer, assume not derived to be safe
                return false;
            }
            
            // Check userData for derived settings
            try
            {
                if (DerivedSettingsUtility.TryRead(importer, out var settings) && settings != null && settings.isDerived)
                    return true;
            }
            catch
            {
                // If JSON parsing fails, assume it's not derived (safe default)
            }
            
            return false;
        }
        
        /// <summary>
        /// Get summary statistics for discovered assets
        /// </summary>
        public static string GetAssetSummary(List<DiscoveredAsset> assets)
        {
            if (assets == null || assets.Count == 0)
                return "No assets discovered";
            
            int totalAssets = assets.Count;
            int folders = assets.Count(a => a.isFolder);
            int files = assets.Count(a => !a.isFolder);
            int dependencies = assets.Count(a => a.isDependency);
            int included = assets.Count(a => a.included);
            int excluded = totalAssets - included;
            long totalSize = assets.Where(a => !a.isFolder).Sum(a => a.fileSize);
            
            return $"Total: {totalAssets} ({files} files, {folders} folders)\n" +
                   $"Dependencies: {dependencies}\n" +
                   $"Included: {included} | Excluded: {excluded}\n" +
                   $"Total Size: {FormatBytes(totalSize)}";
        }
        
        /// <summary>
        /// Format bytes to human-readable size
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
