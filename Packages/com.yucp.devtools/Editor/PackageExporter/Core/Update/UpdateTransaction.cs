using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal sealed class UpdateTransaction
    {
        private readonly string _id;
        private readonly string _root;
        private readonly List<BackupEntry> _backups = new List<BackupEntry>();
        private readonly List<string> _logs = new List<string>();

        [Serializable]
        private class BackupEntry
        {
            public string originalPath;
            public string backupPath;
            public bool isDirectory;
        }

        [Serializable]
        private class BackupManifest
        {
            public List<BackupEntry> entries = new List<BackupEntry>();
        }

        [Serializable]
        private class LastManifest
        {
            public string manifestPath;
            public string timestamp;
        }

        public UpdateTransaction()
        {
            _id = Guid.NewGuid().ToString("N");
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _root = Path.Combine(projectRoot, "Library", "YUCP", "UpdateTxn", _id);
            Directory.CreateDirectory(_root);
        }

        public void Log(string message)
        {
            _logs.Add(message);
        }

        public void BackupPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return;
            if (!File.Exists(absolutePath) && !Directory.Exists(absolutePath)) return;

            if (Directory.Exists(absolutePath))
            {
                string backupDir = Path.Combine(_root, "dirs", Guid.NewGuid().ToString("N"));
                CopyDirectory(absolutePath, backupDir);
                _backups.Add(new BackupEntry
                {
                    originalPath = absolutePath,
                    backupPath = backupDir,
                    isDirectory = true
                });
                return;
            }

            string backupFile = Path.Combine(_root, "files", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(backupFile) ?? _root);
            File.Copy(absolutePath, backupFile, true);
            _backups.Add(new BackupEntry
            {
                originalPath = absolutePath,
                backupPath = backupFile,
                isDirectory = false
            });
        }

        public void Commit()
        {
            WriteLog("Commit");
            WriteManifest();
            CleanupOldTransactions();
        }

        public void Rollback()
        {
            WriteLog("Rollback");
            for (int i = _backups.Count - 1; i >= 0; i--)
            {
                var entry = _backups[i];
                try
                {
                    if (entry.isDirectory)
                    {
                        if (Directory.Exists(entry.originalPath))
                        {
                            Directory.Delete(entry.originalPath, true);
                        }
                        CopyDirectory(entry.backupPath, entry.originalPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(entry.originalPath) ?? "");
                        File.Copy(entry.backupPath, entry.originalPath, true);
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"Rollback failed for {entry.originalPath}: {ex.Message}");
                }
            }
            CleanupSelf();
        }

        private void WriteLog(string message)
        {
            try
            {
                Directory.CreateDirectory(_root);
                File.AppendAllText(Path.Combine(_root, "update.log"),
                    $"{DateTime.UtcNow:O} {message}\n");
                foreach (var line in _logs)
                {
                    File.AppendAllText(Path.Combine(_root, "update.log"),
                        $"{DateTime.UtcNow:O} {line}\n");
                }
                _logs.Clear();
            }
            catch { }
        }

        private void WriteManifest()
        {
            try
            {
                var manifest = new BackupManifest { entries = new List<BackupEntry>(_backups) };
                string manifestPath = Path.Combine(_root, "manifest.json");
                File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));

                string baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "YUCP", "UpdateTxn"));
                Directory.CreateDirectory(baseDir);
                var last = new LastManifest
                {
                    manifestPath = manifestPath,
                    timestamp = DateTime.UtcNow.ToString("O")
                };
                File.WriteAllText(Path.Combine(baseDir, "last.json"), JsonUtility.ToJson(last, true));
            }
            catch { }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string dest = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, dest);
            }
        }

        private void CleanupSelf()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, true);
            }
            catch { }
        }

        private void CleanupOldTransactions()
        {
            try
            {
                string baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "YUCP", "UpdateTxn"));
                if (!Directory.Exists(baseDir)) return;

                string lastPath = Path.Combine(baseDir, "last.json");
                string keepFolder = null;
                if (File.Exists(lastPath))
                {
                    var last = JsonUtility.FromJson<LastManifest>(File.ReadAllText(lastPath));
                    if (last != null && !string.IsNullOrEmpty(last.manifestPath))
                    {
                        keepFolder = Path.GetDirectoryName(last.manifestPath);
                    }
                }

                foreach (var dir in Directory.GetDirectories(baseDir))
                {
                    if (string.IsNullOrEmpty(keepFolder) || !dir.Equals(keepFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            }
            catch { }
        }

        public static bool TryRollbackLast(out string message)
        {
            message = "";
            try
            {
                string baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "YUCP", "UpdateTxn"));
                string lastPath = Path.Combine(baseDir, "last.json");
                if (!File.Exists(lastPath))
                {
                    message = "No update rollback data found.";
                    return false;
                }

                var last = JsonUtility.FromJson<LastManifest>(File.ReadAllText(lastPath));
                if (last == null || string.IsNullOrEmpty(last.manifestPath) || !File.Exists(last.manifestPath))
                {
                    message = "Rollback manifest is missing.";
                    return false;
                }

                var manifest = JsonUtility.FromJson<BackupManifest>(File.ReadAllText(last.manifestPath));
                if (manifest?.entries == null || manifest.entries.Count == 0)
                {
                    message = "Rollback manifest is empty.";
                    return false;
                }

                for (int i = manifest.entries.Count - 1; i >= 0; i--)
                {
                    var entry = manifest.entries[i];
                    if (entry == null) continue;
                    try
                    {
                        if (entry.isDirectory)
                        {
                            if (Directory.Exists(entry.originalPath))
                                Directory.Delete(entry.originalPath, true);
                            CopyDirectory(entry.backupPath, entry.originalPath);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(entry.originalPath) ?? "");
                            File.Copy(entry.backupPath, entry.originalPath, true);
                        }
                    }
                    catch { }
                }

                message = "Rollback complete. Assets have been restored from the last update backup.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Rollback failed: {ex.Message}";
                return false;
            }
        }
    }
}
