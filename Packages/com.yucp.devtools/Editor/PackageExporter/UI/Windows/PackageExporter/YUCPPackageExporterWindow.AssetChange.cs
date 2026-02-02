using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
        private const double AssetChangeDebounceSeconds = 0.75;
        private bool _assetChangeScanQueued;
        private double _assetChangeNextScanAt;
        private readonly HashSet<int> _pendingProfileScanIds = new HashSet<int>();

        internal static void NotifyAssetsChanged(string[] changedPaths)
        {
            if (changedPaths == null || changedPaths.Length == 0)
            {
                return;
            }

            var windows = Resources.FindObjectsOfTypeAll<YUCPPackageExporterWindow>();
            if (windows == null || windows.Length == 0)
            {
                return;
            }

            foreach (var window in windows)
            {
                if (window == null)
                {
                    continue;
                }

                window.HandleAssetChanges(changedPaths);
            }
        }

        private void HandleAssetChanges(string[] changedPaths)
        {
            if (changedPaths == null || changedPaths.Length == 0)
            {
                return;
            }

            if (EditorApplication.isCompiling)
            {
                return;
            }

            var profilesToCheck = new List<ExportProfile>();
            if (selectedProfile != null)
            {
                profilesToCheck.Add(selectedProfile);
            }

            if (selectedProfileIndices != null && allProfiles != null && selectedProfileIndices.Count > 1)
            {
                foreach (int index in selectedProfileIndices)
                {
                    if (index >= 0 && index < allProfiles.Count)
                    {
                        var profile = allProfiles[index];
                        if (profile != null && !profilesToCheck.Contains(profile))
                        {
                            profilesToCheck.Add(profile);
                        }
                    }
                }
            }

            if (profilesToCheck.Count == 0)
            {
                return;
            }

            bool anyRelevant = false;
            foreach (var profile in profilesToCheck)
            {
                if (profile == null || profile.foldersToExport == null || profile.foldersToExport.Count == 0)
                {
                    continue;
                }

                if (!IsChangeRelevant(profile, changedPaths))
                {
                    continue;
                }

                profile.ClearScan();
                EditorUtility.SetDirty(profile);
                anyRelevant = true;
                _pendingProfileScanIds.Add(profile.GetInstanceID());
            }

            if (anyRelevant)
            {
                QueueAssetChangeScan();
            }
        }

        private void QueueAssetChangeScan()
        {
            _assetChangeNextScanAt = EditorApplication.timeSinceStartup + AssetChangeDebounceSeconds;
            if (_assetChangeScanQueued)
            {
                return;
            }

            _assetChangeScanQueued = true;
            EditorApplication.update += ProcessQueuedAssetChangeScan;
        }

        private void ProcessQueuedAssetChangeScan()
        {
            if (EditorApplication.timeSinceStartup < _assetChangeNextScanAt)
            {
                return;
            }

            EditorApplication.update -= ProcessQueuedAssetChangeScan;
            _assetChangeScanQueued = false;

            if (_pendingProfileScanIds.Count == 0)
            {
                return;
            }

            bool anyScanned = false;
            var pendingIds = _pendingProfileScanIds.ToList();
            _pendingProfileScanIds.Clear();

            foreach (int id in pendingIds)
            {
                var profile = FindProfileByInstanceId(id);
                if (profile == null)
                {
                    continue;
                }

                bool shouldScan = showExportInspector && IsProfileVisibleInInspector(profile);
                if (shouldScan)
                {
                    ScanAssetsForInspector(profile, silent: true);
                    anyScanned = true;
                }
            }

            if (anyScanned)
            {
                UpdateProfileDetails();
                Repaint();
            }
        }

        private ExportProfile FindProfileByInstanceId(int id)
        {
            if (selectedProfile != null && selectedProfile.GetInstanceID() == id)
            {
                return selectedProfile;
            }

            if (allProfiles != null)
            {
                foreach (var profile in allProfiles)
                {
                    if (profile != null && profile.GetInstanceID() == id)
                    {
                        return profile;
                    }
                }
            }

            return null;
        }

        private bool IsProfileVisibleInInspector(ExportProfile profile)
        {
            if (profile == null)
            {
                return false;
            }

            if (selectedProfile != null && selectedProfile == profile)
            {
                return true;
            }

            if (selectedProfileIndices != null && allProfiles != null)
            {
                foreach (int index in selectedProfileIndices)
                {
                    if (index >= 0 && index < allProfiles.Count && allProfiles[index] == profile)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsChangeRelevant(ExportProfile profile, string[] changedPaths)
        {
            if (profile == null || profile.foldersToExport == null || changedPaths == null)
            {
                return false;
            }

            var normalizedFolders = new List<string>();
            foreach (var folder in profile.foldersToExport)
            {
                string normalized = NormalizeUnityPath(folder);
                if (!string.IsNullOrEmpty(normalized))
                {
                    normalized = normalized.TrimEnd('/') + "/";
                    normalizedFolders.Add(normalized);
                }
            }

            if (normalizedFolders.Count == 0)
            {
                return false;
            }

            foreach (var changedPath in changedPaths)
            {
                if (string.IsNullOrWhiteSpace(changedPath))
                {
                    continue;
                }

                string normalizedChanged = NormalizeUnityPath(changedPath);
                if (string.IsNullOrEmpty(normalizedChanged))
                {
                    continue;
                }

                foreach (var folder in normalizedFolders)
                {
                    string folderWithoutSlash = folder.TrimEnd('/');
                    if (normalizedChanged.Equals(folderWithoutSlash, StringComparison.OrdinalIgnoreCase) ||
                        normalizedChanged.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string NormalizeUnityPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string normalized = path.Replace('\\', '/').Trim();

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            if (!Path.IsPathRooted(normalized))
            {
                return null;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            string fullPath = Path.GetFullPath(normalized).Replace('\\', '/');

            if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                string relative = fullPath.Substring(projectRoot.Length).TrimStart('/');
                return relative;
            }

            return null;
        }
    }
}
