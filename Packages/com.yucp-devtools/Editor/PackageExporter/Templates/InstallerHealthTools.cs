using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.DirectVpmInstaller
{
    internal static class InstallerHealthTools
    {
        [MenuItem("Tools/YUCP/Validate Install")] 
        private static void ValidateInstall()
        {
            var report = BuildReport(dryRun: true);
            EditorUtility.DisplayDialog("YUCP Validate Install", report, "OK");
        }

        [MenuItem("Tools/YUCP/Repair Install")] 
        private static void RepairInstall()
        {
            var report = BuildReport(dryRun: false);
            EditorUtility.DisplayDialog("YUCP Repair Install", report, "OK");
            AssetDatabase.Refresh();
        }

        private static string BuildReport(bool dryRun)
        {
            var sb = new System.Text.StringBuilder();
            string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");

            // 1) Orphan .yucp_disabled
            int orphanCount = 0;
            try
            {
                foreach (var file in Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories))
                {
                    orphanCount++;
                    sb.AppendLine("Orphan disabled: " + Short(file));
                    if (!dryRun)
                    {
                        try { File.Delete(file); File.Delete(file + ".meta"); } catch { }
                    }
                }
            }
            catch { }

            // 2) Duplicate asmdef names
            var asmNameToFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var asmdef in Directory.GetFiles(Path.GetDirectoryName(Application.dataPath) ?? "", "*.asmdef", SearchOption.AllDirectories))
                {
                    try
                    {
                        var json = File.ReadAllText(asmdef);
                        var match = System.Text.RegularExpressions.Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                        if (match.Success)
                        {
                            var name = match.Groups[1].Value;
                            if (!asmNameToFiles.TryGetValue(name, out var list))
                                list = asmNameToFiles[name] = new List<string>();
                            list.Add(asmdef);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            foreach (var kv in asmNameToFiles.Where(kv => kv.Value.Count > 1))
            {
                sb.AppendLine($"Duplicate asmdef name '{kv.Key}':");
                foreach (var f in kv.Value) sb.AppendLine("  - " + Short(f));
            }

            // 3) Missing meta for scripts (basic)
            int missingMeta = 0;
            foreach (var cs in Directory.GetFiles(packagesPath, "*.cs", SearchOption.AllDirectories))
            {
                var meta = cs + ".meta";
                if (!File.Exists(meta))
                {
                    missingMeta++;
                    sb.AppendLine("Missing meta: " + Short(cs));
                    if (!dryRun)
                    {
                        try { File.WriteAllText(meta, "fileFormatVersion: 2\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"); } catch { }
                    }
                }
            }

            if (orphanCount == 0 && asmNameToFiles.All(kv => kv.Value.Count < 2) && missingMeta == 0)
                sb.AppendLine("No issues detected.");

            return sb.ToString();
        }

        private static string Short(string p) => p.Replace(Path.GetDirectoryName(Application.dataPath) + Path.DirectorySeparatorChar, "");
    }
}





