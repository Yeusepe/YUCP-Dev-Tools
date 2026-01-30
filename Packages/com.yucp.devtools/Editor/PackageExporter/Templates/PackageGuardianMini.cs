using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using PackageGuardian.Core.Transactions;

namespace YUCP.PackageGuardian.Mini
{
    /// <summary>
    /// Lightweight Package Guardian for bundling with YUCP packages.
    ///
    /// IMPORTANT: When enabling *.yucp_disabled files we must also move the paired .meta file,
    /// otherwise Unity will regenerate a new .meta (new GUID) and script references will break.
    /// </summary>
    [InitializeOnLoad]
    public class PackageGuardianMini : AssetPostprocessor
    {
        private static bool _hasProcessedThisSession = false;
        private static GuardianTransaction _currentTransaction = null;

        static PackageGuardianMini()
        {
            EditorApplication.delayCall += SafeInitialize;
        }

        private static void SafeInitialize()
        {
            // No-op for now (keep minimal + robust for bundling).
        }

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (_hasProcessedThisSession)
                return;

            bool hasYucpFiles = importedAssets.Any(a =>
                a.EndsWith(".yucp_disabled", StringComparison.OrdinalIgnoreCase) ||
                (a.Contains("Packages") && a.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)));

            if (!hasYucpFiles)
                return;

            _hasProcessedThisSession = true;

            try
            {
                Debug.Log("[Mini Guardian] Import detected - protecting...");
                _currentTransaction = new GuardianTransaction();

                HandleDisabledFiles();
                CleanupOrphanedDisabledMetas();

                _currentTransaction.Commit();
                _currentTransaction = null;

                Debug.Log("[Mini Guardian] Protection complete");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Mini Guardian] Import protection failed: {ex.Message}");
                if (_currentTransaction != null)
                {
                    Debug.LogWarning("[Mini Guardian] Rolling back...");
                    _currentTransaction.Rollback();
                    _currentTransaction = null;
                }
            }
            finally
            {
                EditorApplication.delayCall += () => { _hasProcessedThisSession = false; };
            }
        }

        /// <summary>
        /// Core import protection: enable *.yucp_disabled files and ensure their .meta is moved/restored.
        /// </summary>
        private static void HandleDisabledFiles()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string packagesPath = Path.Combine(projectRoot, "Packages");
            string assetsPath = Path.Combine(projectRoot, "Assets");

            var roots = new[] { packagesPath, assetsPath }.Where(Directory.Exists).ToArray();
            if (roots.Length == 0)
                return;

            var disabledFiles = roots
                .SelectMany(r => Directory.GetFiles(r, "*.yucp_disabled", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (disabledFiles.Length == 0)
                return;

            Debug.Log($"[Mini Guardian] Found {disabledFiles.Length} .yucp_disabled file(s)");

            foreach (var disabledFile in disabledFiles)
            {
                string enabledFile = disabledFile.Substring(0, disabledFile.Length - ".yucp_disabled".Length);
                string disabledMeta = disabledFile + ".meta";
                string enabledMeta = enabledFile + ".meta";

                // Backup all affected files (including meta)
                _currentTransaction.BackupFile(disabledFile);
                _currentTransaction.BackupFile(disabledMeta);
                if (File.Exists(enabledFile))
                    _currentTransaction.BackupFile(enabledFile);
                _currentTransaction.BackupFile(enabledMeta);

                // No conflict: move disabled -> enabled and move meta alongside it
                if (!File.Exists(enabledFile))
                {
                    EnableWithMeta(disabledFile, enabledFile, disabledMeta, enabledMeta);
                    Debug.Log($"[Mini Guardian] Enabled: {Path.GetFileName(enabledFile)}");
                    continue;
                }

                // Conflict: always keep the enabled file to avoid overwriting user content.
                Debug.LogWarning($"[Mini Guardian] Conflict detected for '{Path.GetFileName(enabledFile)}'. Keeping existing file and leaving .yucp_disabled in place.");
                continue;
            }
        }

        private static void EnableWithMeta(string disabledFile, string enabledFile, string disabledMeta, string enabledMeta)
        {
            // Move the file
            _currentTransaction.ExecuteFileOperation(disabledFile, enabledFile, FileOperationType.Move);

            // Move .meta if present (critical for GUID preservation / restoration)
            if (File.Exists(disabledMeta))
            {
                // If an enabled meta already exists (orphan), delete it so move succeeds
                if (File.Exists(enabledMeta))
                    _currentTransaction.ExecuteFileOperation(enabledMeta, null, FileOperationType.Delete);

                _currentTransaction.ExecuteFileOperation(disabledMeta, enabledMeta, FileOperationType.Move);

                // Restore original GUID if stored in userData
                TryRestoreOriginalGuidInMeta(enabledMeta);
            }
        }

        private static void CleanupOrphanedDisabledMetas()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string packagesPath = Path.Combine(projectRoot, "Packages");
                string assetsPath = Path.Combine(projectRoot, "Assets");

                var roots = new[] { packagesPath, assetsPath }.Where(Directory.Exists).ToArray();
                if (roots.Length == 0)
                    return;

                foreach (var meta in roots.SelectMany(r => Directory.GetFiles(r, "*.yucp_disabled.meta", SearchOption.AllDirectories)))
                {
                    var disabled = meta.Substring(0, meta.Length - ".meta".Length);
                    if (!File.Exists(disabled))
                    {
                        try { File.Delete(meta); } catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Reads original GUID from meta userData and rewrites the meta GUID to match it.
        /// Supports formats:
        /// - userData: YUCP_ORIGINAL_GUID=xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
        /// - userData: {"originalGuid":"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"} (legacy)
        /// </summary>
        private static void TryRestoreOriginalGuidInMeta(string metaPath)
        {
            try
            {
                if (!File.Exists(metaPath))
                    return;

                string content = File.ReadAllText(metaPath);
                string originalGuid = ExtractOriginalGuidFromMetaContent(content);
                if (string.IsNullOrEmpty(originalGuid))
                    return;

                // Replace GUID line
                content = Regex.Replace(
                    content,
                    @"guid:\s*([a-f0-9]{32})",
                    $"guid: {originalGuid}",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );

                // Remove token / legacy json from userData to leave it empty (Unity style)
                content = Regex.Replace(
                    content,
                    @"(\s+userData:\s*)(?:['""])?YUCP_ORIGINAL_GUID=[a-f0-9]{32}(?:['""])?\s*$",
                    "$1",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );
                content = Regex.Replace(
                    content,
                    @"(\s+userData:\s*)\{\s*""originalGuid""\s*:\s*""[a-f0-9]{32}""\s*\}\s*$",
                    "$1",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );

                File.WriteAllText(metaPath, content);
            }
            catch { }
        }

        private static string ExtractOriginalGuidFromMetaContent(string metaContent)
        {
            try
            {
                var tokenMatch = Regex.Match(
                    metaContent,
                    @"userData:\s*(?:['""])?YUCP_ORIGINAL_GUID=([a-f0-9]{32})(?:['""])?\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );
                if (tokenMatch.Success)
                    return tokenMatch.Groups[1].Value;

                var legacyMatch = Regex.Match(
                    metaContent,
                    @"userData:\s*(?:['""])?\{\s*""originalGuid""\s*:\s*""([a-f0-9]{32})""\s*\}(?:['""])?\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );
                if (legacyMatch.Success)
                    return legacyMatch.Groups[1].Value;
            }
            catch { }

            return null;
        }
    }
}

