using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace YUCP.DirectVpmInstaller
{
    /// <summary>
    /// Minimal, self-contained transaction manager for robust enabling of .yucp_disabled files.
    /// Lives in the installer template assembly so it works even when com.yucp.components isn't present.
    /// </summary>
    internal static class InstallerTxn
    {
        private const string TxnDirName = "YUCP";
        private const string TxnPrefix = "InstallerTxn_";
        private const string MarkerPrefix = "install."; // pending | lock | complete | error

        private class TxnState
        {
            public string id;
            public string status; // started | committed
            public List<string> logs = new List<string>();
            public List<Op> ops = new List<Op>();
            public List<ManifestEntry> manifest = new List<ManifestEntry>();
        }

        private struct Op
        {
            public string type; // move | delete | backup
            public string src;
            public string dst;
        }

        private static TxnState _current;
        private static string TxnFolder => Path.Combine(Application.dataPath, "..", "Library", TxnDirName);
        private static string TxnPath => Path.Combine(TxnFolder, TxnPrefix + _current.id + ".json");
        private static string LogPath => Path.Combine(Application.dataPath, "..", "Library", TxnDirName, "installer.log");
        private static string QuarantineRoot => Path.Combine(Application.dataPath, "..", "Packages", ".yucp_quarantine");
        private static string ManifestPath => Path.Combine(TxnFolder, "Manifest_" + _current.id + ".json");
        private static string MarkerPath(string name) => Path.Combine(TxnFolder, MarkerPrefix + name);

        private class ManifestEntry
        {
            public string disabledPath;
            public string enabledPath;
            public string sha256;
        }

        [InitializeOnLoadMethod]
        private static void RecoverUnfinishedOnLoad()
        {
            try
            {
                string root = Path.Combine(Application.dataPath, "..", "Library", TxnDirName);
                if (!Directory.Exists(root)) return;
                foreach (var file in Directory.GetFiles(root, TxnPrefix + "*.json", SearchOption.TopDirectoryOnly))
                {
                    // Simple safe choice: rollback unfinished transactions
                    string json = File.ReadAllText(file);
                    if (json.Contains("\"status\": \"started\""))
                    {
                        // Load minimal state to run rollback
                        var id = Path.GetFileNameWithoutExtension(file).Substring(TxnPrefix.Length);
                        _current = new TxnState { id = id, status = "started" };
                        // Not replaying ops from disk here (keep simple); just delete the txn marker
                        TryDelete(file);
                        _current = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("RecoverUnfinishedOnLoad error: " + ex.Message);
            }
        }

        public static string Begin()
        {
            Directory.CreateDirectory(TxnFolder);
            _current = new TxnState { id = Guid.NewGuid().ToString("N"), status = "started" };
            Persist();
            return _current.id;
        }

        public static void Commit()
        {
            if (_current == null) return;
            _current.status = "committed";
            Persist();
            TryDelete(TxnPath);
            _current = null;
        }

        public static void Rollback()
        {
            if (_current == null) return;
            // reverse ops
            for (int i = _current.ops.Count - 1; i >= 0; i--)
            {
                var op = _current.ops[i];
                try
                {
                    switch (op.type)
                    {
                        case "move":
                            SafeMove(op.dst, op.src, overwrite: true);
                            break;
                        case "delete":
                            // no-op on rollback
                            break;
                        case "backup":
                            // restore backup
                            if (File.Exists(op.src)) SafeMove(op.src, op.dst, overwrite: true);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Rollback op failed: {op.type} {op.src} -> {op.dst}: {ex.Message}");
                }
            }
            TryDelete(TxnPath);
            _current = null;
        }

        public static void EnableDisabledFile(string disabledFile)
        {
            string enabledFile = disabledFile.Substring(0, disabledFile.Length - ".yucp_disabled".Length);
            string disabledMeta = disabledFile + ".meta";
            string enabledMeta = enabledFile + ".meta";

            // Track if enabled file already exists (to preserve its GUID)
            bool enabledFileExisted = File.Exists(enabledFile);
            bool enabledMetaExisted = File.Exists(enabledMeta);

            // If destination exists, compare hashes; if identical, delete disabled variant
            if (enabledFileExisted)
            {
                bool identical = false;
                try
                {
                    identical = string.Equals(HashOf(enabledFile), HashOf(disabledFile), StringComparison.OrdinalIgnoreCase);
                }
                catch { }

                if (!identical)
                {
                    // move destination to quarantine, then replace
                    string qdir = Path.Combine(QuarantineRoot, _current.id);
                    Directory.CreateDirectory(qdir);
                    string backupPath = Path.Combine(qdir, Path.GetFileName(enabledFile));
                    SafeMove(enabledFile, backupPath, overwrite: true);
                    Record("backup", backupPath, enabledFile);
                    
                    // Note: We backup the meta file but will restore it to preserve the original GUID
                    // This maintains references from prefabs and other assets
                    if (enabledMetaExisted)
                    {
                        string backupMetaPath = backupPath + ".meta";
                        SafeMove(enabledMeta, backupMetaPath, overwrite: true);
                        Record("backup", backupMetaPath, enabledMeta);
                    }
                }

                if (!identical)
                {
                    SafeMove(disabledFile, enabledFile, overwrite: true);
                    Record("move", disabledFile, enabledFile);
                }
                else
                {
                    TryDelete(disabledFile);
                    Record("delete", disabledFile, null);
                    // If files are identical and enabled file existed, preserve its meta and delete disabled meta
                    if (File.Exists(disabledMeta))
                    {
                        TryDelete(disabledMeta);
                        Record("delete", disabledMeta, null);
                    }
                    return; // Early return - nothing more to do
                }
            }
            else
            {
                SafeMove(disabledFile, enabledFile, overwrite: false);
                Record("move", disabledFile, enabledFile);
            }

            // meta handling:
            // - If enabled file already existed and had a meta, restore it from backup to preserve the original GUID
            //   This ensures GUIDs remain consistent and prefabs/references don't break
            // - If enabled file didn't exist, extract original GUID from disabled meta's userData and restore it
            if (enabledFileExisted && enabledMetaExisted)
            {
                // Restore the original enabled meta file from backup to preserve the original GUID
                // This maintains references from prefabs and other assets that reference this file
                string qdir = Path.Combine(QuarantineRoot, _current.id);
                string backupPath = Path.Combine(qdir, Path.GetFileName(enabledFile));
                string backupMetaPath = backupPath + ".meta";
                
                if (File.Exists(backupMetaPath))
                {
                    SafeMove(backupMetaPath, enabledMeta, overwrite: true);
                    Record("move", backupMetaPath, enabledMeta);
                }
                
                // Delete the disabled meta since we're using the original enabled meta
                if (File.Exists(disabledMeta))
                {
                    TryDelete(disabledMeta);
                    Record("delete", disabledMeta, null);
                }
            }
            else if (File.Exists(disabledMeta))
            {
                // No existing enabled meta, so we need to restore the original GUID from userData
                string originalGuid = ExtractOriginalGuidFromMeta(disabledMeta);
                
                if (!string.IsNullOrEmpty(originalGuid))
                {
                    // Read the disabled meta file
                    string metaContent = File.ReadAllText(disabledMeta);
                    // Replace the GUID with the original GUID (preserve the format: "guid: GUID_VALUE")
                    metaContent = System.Text.RegularExpressions.Regex.Replace(
                        metaContent, 
                        @"guid:\s*([a-f0-9]{32})", 
                        $"guid: {originalGuid}",
                        System.Text.RegularExpressions.RegexOptions.Multiline
                    );
                    // Remove the original GUID from userData (clean it up, handle with or without newline)
                    metaContent = System.Text.RegularExpressions.Regex.Replace(
                        metaContent,
                        @"(\s+)userData:\s*\{\""originalGuid\"":\""[a-f0-9]{32}\""\}\s*\n",
                        "$1userData:\n",
                        System.Text.RegularExpressions.RegexOptions.Multiline
                    );
                    
                    // Write the restored meta file (ensure directory exists)
                    Directory.CreateDirectory(Path.GetDirectoryName(enabledMeta));
                    File.WriteAllText(enabledMeta, metaContent);
                    Record("move", disabledMeta, enabledMeta);
                    
                    // Delete the disabled meta
                    TryDelete(disabledMeta);
                }
                else
                {
                    // No original GUID stored, just move the disabled meta to enabled (fallback)
                    SafeMove(disabledMeta, enabledMeta, overwrite: true);
                    Record("move", disabledMeta, enabledMeta);
                }
            }

            // Update manifest entry for this file
            try
            {
                _current.manifest.Add(new ManifestEntry
                {
                    disabledPath = disabledFile.Replace('\\', '/'),
                    enabledPath = enabledFile.Replace('\\', '/'),
                    sha256 = SafeHash(disabledFile)
                });
                Persist();
            }
            catch { }
        }

        public static void CleanupOrphanedDisabledFiles()
        {
            try
            {
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                foreach (var file in Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories))
                {
                    TryDelete(file);
                    var meta = file + ".meta";
                    TryDelete(meta);
                }
            }
            catch (Exception ex)
            {
                Log($"CleanupOrphanedDisabledFiles error: {ex.Message}");
            }
        }

        private static void Record(string type, string src, string dst)
        {
            _current.ops.Add(new Op { type = type, src = src, dst = dst });
            Persist();
        }

        private static void Persist()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.AppendFormat("  \"id\": \"{0}\",\n", _current.id);
                sb.AppendFormat("  \"status\": \"{0}\",\n", _current.status);
                sb.Append("  \"ops\": [\n");
                for (int i = 0; i < _current.ops.Count; i++)
                {
                    var op = _current.ops[i];
                    sb.Append("    { ");
                    sb.AppendFormat("\"type\": \"{0}\", \"src\": \"{1}\", \"dst\": \"{2}\"",
                        Escape(op.type), Escape(op.src), Escape(op.dst));
                    sb.Append(" }");
                    if (i < _current.ops.Count - 1) sb.Append(",");
                    sb.Append("\n");
                }
                sb.Append("  ],\n");
                sb.Append("  \"manifest\": [\n");
                for (int i = 0; i < _current.manifest.Count; i++)
                {
                    var m = _current.manifest[i];
                    sb.Append("    { ");
                    sb.AppendFormat("\"disabledPath\": \"{0}\", \"enabledPath\": \"{1}\", \"sha256\": \"{2}\"",
                        Escape(m.disabledPath), Escape(m.enabledPath), Escape(m.sha256));
                    sb.Append(" }");
                    if (i < _current.manifest.Count - 1) sb.Append(",");
                    sb.Append("\n");
                }
                sb.Append("  ]\n");
                sb.Append("}\n");
                File.WriteAllText(TxnPath, sb.ToString());
                File.WriteAllText(ManifestPath, sb.ToString());
            }
            catch (Exception ex)
            {
                Log($"Persist error: {ex.Message}");
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void SafeMove(string src, string dst, bool overwrite)
        {
            RunWithRetries(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (overwrite && File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
            }, $"Move {src} -> {dst}");
        }

        private static void SafeCopy(string src, string dst, bool overwrite)
        {
            RunWithRetries(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst, overwrite);
            }, $"Copy {src} -> {dst}");
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void RunWithRetries(Action action, string label)
        {
            const int attempts = 5;
            int delayMs = 50;
            Exception last = null;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    System.Threading.Thread.Sleep(delayMs);
                    delayMs *= 2;
                }
            }
            throw new IOException($"{label} failed after {attempts} attempts: {last?.Message}");
        }

        private static string HashOf(string file)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(file))
            {
                var bytes = sha.ComputeHash(fs);
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string SafeHash(string file)
        {
            try { return HashOf(file); } catch { return ""; }
        }

        public static bool VerifyManifest()
        {
            try
            {
                foreach (var m in _current.manifest)
                {
                    if (!File.Exists(m.enabledPath)) return false;
                    var h = HashOf(m.enabledPath);
                    if (!string.Equals(h, m.sha256, StringComparison.OrdinalIgnoreCase)) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log("VerifyManifest error: " + ex.Message);
                return false;
            }
        }

        // ===== Coordination Markers (shared with Mini Guardian) =====
        public static void SetMarker(string name)
        {
            try
            {
                Directory.CreateDirectory(TxnFolder);
                string path = MarkerPath(name);
                File.WriteAllText(path, DateTime.UtcNow.ToString("o"));
            }
            catch (Exception ex)
            {
                Log("SetMarker error: " + ex.Message);
            }
        }

        public static void ClearMarker(string name)
        {
            try
            {
                string path = MarkerPath(name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Log("ClearMarker error: " + ex.Message);
            }
        }

        public static bool HasMarker(string name)
        {
            try { return File.Exists(MarkerPath(name)); } catch { return false; }
        }

        public static bool IsMarkerStale(string name, TimeSpan maxAge)
        {
            try
            {
                string path = MarkerPath(name);
                if (!File.Exists(path)) return false;
                var when = File.GetLastWriteTimeUtc(path);
                return (DateTime.UtcNow - when) > maxAge;
            }
            catch { return false; }
        }

        private static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, DateTime.UtcNow.ToString("o") + " " + msg + "\n");
            }
            catch { }
        }
        
        /// <summary>
        /// Extract the original GUID from a .meta file's userData field (stored when bundling .yucp_disabled files)
        /// </summary>
        private static string ExtractOriginalGuidFromMeta(string metaPath)
        {
            try
            {
                if (!File.Exists(metaPath))
                    return null;
                
                string metaContent = File.ReadAllText(metaPath);
                
                // Look for userData with original GUID: userData: {"originalGuid":"..."}
                var userDataMatch = System.Text.RegularExpressions.Regex.Match(
                    metaContent, 
                    @"userData:\s*\{\""originalGuid\"":\""([a-f0-9]{32})\""\}"
                );
                
                if (userDataMatch.Success && userDataMatch.Groups.Count > 1)
                {
                    return userDataMatch.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                Log($"ExtractOriginalGuidFromMeta error: {ex.Message}");
            }
            
            return null;
        }
    }
}


