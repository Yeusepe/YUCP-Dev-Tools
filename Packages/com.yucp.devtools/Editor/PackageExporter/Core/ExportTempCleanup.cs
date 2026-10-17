using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace YUCP.DevTools.Editor.PackageExporter
{
    [InitializeOnLoad]
    internal static class ExportTempCleanup
    {
        private const string StartupCleanupKey = "YUCP.PackageExporter.StartupTempCleanup";
        private const string IdentityPattern = @"(?:(?<pid>[1-9][0-9]*)_(?<started>[1-9][0-9]*)_)?[a-fA-F0-9]{32}";
        private static readonly Regex PackageFilePattern = new Regex(@"^YUCP_(?:Temp|ContentWithIcon)_" + IdentityPattern + @"\.unitypackage(?:\.tmp)?\z");
        private static readonly Regex DirectoryPattern = new Regex(@"^YUCP_(?:PackageExtract|PackageIcon|Obfuscation)_" + IdentityPattern + @"\z");
        private static readonly Regex TexturePattern = new Regex(@"^YUCP_Texture_" + IdentityPattern + @"\.png\z");
        private static readonly Regex LegacyTexturePattern = new Regex(@"^.+_(?:-?[0-9]+|[a-fA-F0-9]{32})\.png\z");
        private static readonly int ProcessId;
        private static readonly long ProcessStartTicks;

        static ExportTempCleanup()
        {
            using (var process = Process.GetCurrentProcess())
            {
                ProcessId = process.Id;
                ProcessStartTicks = process.StartTime.ToUniversalTime().Ticks;
            }

            if (!SessionState.GetBool(StartupCleanupKey, false))
                EditorApplication.delayCall += CleanupOnStartup;
        }

        internal static string CreatePath(string prefix, string suffix = "", string directory = null)
        {
            return Path.Combine(directory ?? Path.GetTempPath(), $"{prefix}{ProcessId}_{ProcessStartTicks}_{Guid.NewGuid():N}{suffix}");
        }

        private static void CleanupOnStartup()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || PackageBuilder.s_isExporting)
            {
                EditorApplication.delayCall += CleanupOnStartup;
                return;
            }

            SessionState.SetBool(StartupCleanupKey, true);
            CleanupStaleFiles(Path.GetTempPath(), CanCleanLegacyFiles());
            CleanupStaleFiles(Path.GetFullPath("Library/YUCP/PackageExporter/TempAssets"), true, textureCache: true);
            Delete(Path.GetFullPath("Library/YUCP/ConfuserEx"));
        }

        private static bool CanCleanLegacyFiles()
        {
            // Old filenames have no owner; another editor may still be exporting with that version.
            try
            {
                bool otherEditorRunning = false;
                foreach (var process in Process.GetProcessesByName("Unity"))
                {
                    using (process)
                        otherEditorRunning |= process.Id != ProcessId;
                }
                return !otherEditorRunning;
            }
            catch
            {
                return false;
            }
        }

        internal static void CleanupStaleFiles(string directory, bool cleanLegacy, bool textureCache = false)
        {
            try
            {
                if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    return;

                foreach (string path in Directory.GetFileSystemEntries(directory, textureCache ? "*.png" : "YUCP_*"))
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                        string name = Path.GetFileName(path);
                        Match match = (textureCache ? TexturePattern : isDirectory ? DirectoryPattern : PackageFilePattern).Match(name);
                        if (textureCache && isDirectory) continue;

                        if (!match.Success)
                        {
                            if (textureCache && cleanLegacy && !name.StartsWith("YUCP_Texture_", StringComparison.Ordinal) && LegacyTexturePattern.IsMatch(name))
                                Delete(path);
                            continue;
                        }

                        if (!match.Groups["pid"].Success)
                        {
                            if (cleanLegacy) Delete(path);
                        }
                        else if (int.TryParse(match.Groups["pid"].Value, out int processId) &&
                                 long.TryParse(match.Groups["started"].Value, out long startTicks) &&
                                 !IsOwnerRunning(processId, startTicks))
                        {
                            Delete(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageExporter] Could not inspect temporary path '{path}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PackageExporter] Could not scan temporary directory '{directory}': {ex.Message}");
            }
        }

        private static bool IsOwnerRunning(int processId, long startTicks)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                    return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startTicks;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch
            {
                // An inaccessible process is not proof that its export has ended.
                return true;
            }
        }

        // Each path belongs to this operation; never sweep other exports' temporary files.
        internal static void Delete(params string[] paths)
        {
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;

                try
                {
                    if ((File.Exists(path) || Directory.Exists(path)) &&
                        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        continue;
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                    else if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageExporter] Could not delete temporary path '{path}': {ex.Message}");
                }
            }
        }
    }
}
