using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DirectVpmInstaller
{
    [InitializeOnLoad]
    internal static class __YUCP_PREFLIGHT_CLASS__
    {
        private const string CurrentFileName = "__YUCP_PREFLIGHT_FILE__";
        private static readonly string[] GeneratedInstallerArtifactPatterns =
        {
            "YUCP_InstallerPreflight_*.cs",
            "YUCP_Installer_*.cs",
            "YUCP_Installer_*.asmdef",
            "YUCP_InstallerTxn_*.cs",
            "YUCP_InstallerHealthTools_*.cs",
            "YUCP_FullDomainReload_*.cs"
        };

        static __YUCP_PREFLIGHT_CLASS__()
        {
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string[] editorPaths =
                {
                    Path.Combine(Application.dataPath, "Editor"),
                    Path.Combine(projectRoot, "Packages", "yucp.installed-packages", "Editor")
                };

                int removedCount = RemoveStaleInstallerArtifacts(editorPaths);
                int enabledCount = EnableIncomingInstallerArtifacts(editorPaths);
                bool removedSelf = DeleteSelf(projectRoot);

                if (removedCount > 0 || enabledCount > 0 || removedSelf)
                {
                    Debug.Log($"[YUCP InstallerPreflight] Removed {removedCount} stale artifact(s), enabled {enabledCount} incoming artifact(s).");
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP InstallerPreflight] Preflight cleanup failed: {ex.Message}");
            }
        }

        private static int RemoveStaleInstallerArtifacts(string[] editorPaths)
        {
            int removedCount = 0;

            foreach (string editorPath in editorPaths)
            {
                if (!Directory.Exists(editorPath))
                    continue;

                foreach (string file in GeneratedInstallerArtifactPatterns
                             .SelectMany(pattern => Directory.GetFiles(editorPath, pattern, SearchOption.TopDirectoryOnly))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (string.Equals(Path.GetFileName(file), CurrentFileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        File.Delete(file);
                        string metaPath = file + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        removedCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[YUCP InstallerPreflight] Failed to delete stale artifact '{Path.GetFileName(file)}': {ex.Message}");
                    }
                }
            }

            return removedCount;
        }

        private static int EnableIncomingInstallerArtifacts(string[] editorPaths)
        {
            int enabledCount = 0;

            foreach (string editorPath in editorPaths)
            {
                if (!Directory.Exists(editorPath))
                    continue;

                foreach (string disabledFile in GeneratedInstallerArtifactPatterns
                             .SelectMany(pattern => Directory.GetFiles(editorPath, pattern + ".yucp_disabled", SearchOption.TopDirectoryOnly))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string enabledFile = disabledFile.Substring(0, disabledFile.Length - ".yucp_disabled".Length);
                    string disabledMeta = disabledFile + ".meta";
                    string enabledMeta = enabledFile + ".meta";

                    try
                    {
                        if (File.Exists(enabledFile))
                            File.Delete(enabledFile);
                        if (File.Exists(enabledMeta))
                            File.Delete(enabledMeta);

                        File.Move(disabledFile, enabledFile);
                        if (File.Exists(disabledMeta))
                            File.Move(disabledMeta, enabledMeta);
                        enabledCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[YUCP InstallerPreflight] Failed to enable '{Path.GetFileName(disabledFile)}': {ex.Message}");
                    }
                }
            }

            return enabledCount;
        }

        private static bool DeleteSelf(string projectRoot)
        {
            string[] candidates =
            {
                Path.Combine(projectRoot, "Packages", "yucp.installed-packages", "Editor", CurrentFileName),
                Path.Combine(Application.dataPath, "Editor", CurrentFileName)
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!File.Exists(candidate))
                        continue;

                    File.Delete(candidate);
                    string metaPath = candidate + ".meta";
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YUCP InstallerPreflight] Failed to delete bootstrap '{Path.GetFileName(candidate)}': {ex.Message}");
                }
            }

            return false;
        }
    }
}
