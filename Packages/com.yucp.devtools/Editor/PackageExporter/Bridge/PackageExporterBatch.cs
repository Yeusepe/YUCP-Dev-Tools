using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using YUCP.DevTools.Editor.PackageExporter;

namespace YUCP.DevTools.Editor.PackageExporter.Bridge
{
    /// <summary>
    /// Batch/CLI entry point for exporting a package via Unity -executeMethod (e.g. in CI or GitHub Actions).
    /// Requires environment variable: YUCP_EXPORT_PROFILE_PATH — asset path to an ExportProfile (e.g. Assets/YUCP/ExportProfiles/MyProfile.asset).
    /// Optional: YUCP_EXPORT_OUTPUT_FILE — path to a file where the exported .unitypackage path will be written on success.
    /// Usage: Unity -batchmode -quit -projectPath &lt;path&gt; -executeMethod YUCP.DevTools.Editor.PackageExporter.Bridge.PackageExporterBatch.Export
    /// </summary>
    public static class PackageExporterBatch
    {
        public static void Export()
        {
            string profilePath = Environment.GetEnvironmentVariable("YUCP_EXPORT_PROFILE_PATH");
            if (string.IsNullOrWhiteSpace(profilePath))
            {
                Console.WriteLine("[YUCP-Export] Error: YUCP_EXPORT_PROFILE_PATH environment variable is not set.");
                EditorApplication.Exit(1);
                return;
            }

            var profile = AssetDatabase.LoadAssetAtPath<ExportProfile>(profilePath);
            if (profile == null)
            {
                Console.WriteLine($"[YUCP-Export] Error: Could not load ExportProfile at '{profilePath}'.");
                EditorApplication.Exit(1);
                return;
            }

            var result = PackageBuilder.ExportPackage(profile, (progress, status) =>
            {
                Console.WriteLine($"[YUCP-Export] {progress:P0} - {status}");
            });

            if (result.success)
            {
                string outputFile = Environment.GetEnvironmentVariable("YUCP_EXPORT_OUTPUT_FILE");
                if (!string.IsNullOrWhiteSpace(outputFile))
                {
                    try
                    {
                        File.WriteAllText(outputFile, result.outputPath ?? "");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[YUCP-Export] Warning: Could not write output path to '{outputFile}': {ex.Message}");
                    }
                }
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[YUCP-Export] Error: {result.errorMessage}");
                EditorApplication.Exit(1);
            }
        }
    }
}
