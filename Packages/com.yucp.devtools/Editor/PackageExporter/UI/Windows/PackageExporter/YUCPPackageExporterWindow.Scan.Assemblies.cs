using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using YUCP.DevTools.Components;
using YUCP.DevTools.Editor.PackageExporter.UI.Components;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
        private void ScanAllAssemblies(ExportProfile profile)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Scanning Assemblies", "Initializing...", 0f);
                
                var foundAssemblies = new List<AssemblyScanner.AssemblyInfo>();
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Scanning {profile.foldersToExport.Count} export folders...", 0.2f);
                var folderAssemblies = AssemblyScanner.ScanFolders(profile.foldersToExport);
                foundAssemblies.AddRange(folderAssemblies);
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Found {folderAssemblies.Count} assemblies in export folders", 0.5f);
                
                int bundledDepsCount = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Bundle);
                
                if (bundledDepsCount > 0)
                {
                    EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Scanning {bundledDepsCount} bundled dependencies...", 0.6f);
                }
                
                var dependencyAssemblies = AssemblyScanner.ScanVpmPackages(profile.dependencies);
                foundAssemblies.AddRange(dependencyAssemblies);
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Found {dependencyAssemblies.Count} assemblies in bundled dependencies", 0.8f);
                
                if (foundAssemblies.Count == 0)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("No Assemblies Found", 
                        "No .asmdef files were found in export folders or enabled dependencies.", 
                        "OK");
                    return;
                }
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", "Processing assembly list...", 0.9f);
                
                profile.assembliesToObfuscate.Clear();
                
                foreach (var assemblyInfo in foundAssemblies)
                {
                    var settings = new AssemblyObfuscationSettings(assemblyInfo.assemblyName, assemblyInfo.asmdefPath);
                    settings.enabled = assemblyInfo.exists;
                    profile.assembliesToObfuscate.Add(settings);
                }
                
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                
                int existingCount = foundAssemblies.Count(a => a.exists);
                EditorUtility.DisplayDialog("Scan Complete", 
                    $"Found {foundAssemblies.Count} assemblies ({existingCount} compiled)\n\nFrom export folders: {folderAssemblies.Count}\nFrom bundled dependencies: {dependencyAssemblies.Count}", 
                    "OK");
                
                UpdateProfileDetails();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

    }
}
