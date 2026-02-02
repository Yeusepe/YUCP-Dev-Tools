using System.Collections.Generic;
using UnityEditor;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Tracks asset changes and signals the ExportProfile inspector to rescan when needed.
    /// </summary>
    public class ExportProfileAssetChangeMonitor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            var changed = new List<string>();

            AddRange(changed, importedAssets);
            AddRange(changed, deletedAssets);
            AddRange(changed, movedAssets);
            AddRange(changed, movedFromAssetPaths);

            if (changed.Count == 0)
            {
                return;
            }

            ExportProfileEditor.NotifyAssetsChanged(changed.ToArray());
            YUCPPackageExporterWindow.NotifyAssetsChanged(changed.ToArray());
        }

        private static void AddRange(List<string> output, string[] input)
        {
            if (input == null || input.Length == 0)
            {
                return;
            }

            foreach (var item in input)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    output.Add(item);
                }
            }
        }
    }
}
