using System;
using System.IO;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class PatchImportPackageInjector
    {
        internal const string TempPackageRoot = "Packages/com.yucp.temp";
        internal const string TempPatchEditorRoot = TempPackageRoot + "/Editor";

        internal const string PrecompiledPatchRuntimePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Binaries/YUCP.PatchRuntime.dll";
        internal const string PatchImporterTemplatePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Templates/YUCPPatchImporter.cs";
        internal const string PatchCleanupTemplatePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Templates/YUCPPatchCleanup.cs";

        private const string PrecompiledPatchRuntimeTargetPath = TempPatchEditorRoot + "/YUCP.PatchRuntime.dll";
        private const string PrecompiledPatchRuntimeSeed = "yucp-precompiled-patch-runtime";

        private static readonly NativeLibrary[] NativeLibraries =
        {
            new NativeLibrary("Packages/com.yucp.devtools/Plugins/hdiffz.dll", TempPackageRoot + "/Plugins/hdiffz.dll"),
            new NativeLibrary("Packages/com.yucp.devtools/Plugins/hpatchz.dll", TempPackageRoot + "/Plugins/hpatchz.dll"),
            new NativeLibrary("Packages/com.yucp.devtools/Plugins/hdiffinfo.dll", TempPackageRoot + "/Plugins/hdiffinfo.dll"),
            new NativeLibrary("Packages/com.yucp.devtools/Plugins/Linux/x86_64/libhdiffz.so", TempPackageRoot + "/Plugins/Linux/x86_64/libhdiffz.so"),
            new NativeLibrary("Packages/com.yucp.devtools/Plugins/Linux/x86_64/libhpatchz.so", TempPackageRoot + "/Plugins/Linux/x86_64/libhpatchz.so"),
            new NativeLibrary("Packages/com.yucp.devtools/Plugins/Linux/x86_64/libhdiffinfo.so", TempPackageRoot + "/Plugins/Linux/x86_64/libhdiffinfo.so")
        };

        internal static void InjectRequiredPatchImportFiles(
            string tempExtractDir,
            bool usingPrecompiledInstallerRuntime,
            Func<string, string, bool> tryInjectInstallerRuntime)
        {
            if (!TryInjectPrecompiledPatchRuntime(tempExtractDir))
            {
                throw new FileNotFoundException($"Required patch runtime binary not found at {PrecompiledPatchRuntimePath}.");
            }

            if (!TryInjectPatchProcessingScripts(tempExtractDir))
            {
                throw new FileNotFoundException($"Required patch importer scripts not found at {PatchImporterTemplatePath} and {PatchCleanupTemplatePath}.");
            }

            if (!usingPrecompiledInstallerRuntime &&
                (tryInjectInstallerRuntime == null || !tryInjectInstallerRuntime(tempExtractDir, TempPatchEditorRoot)))
            {
                throw new FileNotFoundException($"Required patch installer runtime could not be injected into {TempPatchEditorRoot}.");
            }

            InjectNativeLibraries(tempExtractDir);
        }

        internal static bool TryInjectPrecompiledPatchRuntime(string tempExtractDir)
        {
            return UnityPackageStagingWriter.TryInjectPrecompiledEditorBinary(
                tempExtractDir,
                PrecompiledPatchRuntimePath,
                PrecompiledPatchRuntimeTargetPath,
                PrecompiledPatchRuntimeSeed);
        }

        internal static bool TryInjectPatchProcessingScripts(string tempExtractDir)
        {
            if (!File.Exists(PatchImporterTemplatePath) || !File.Exists(PatchCleanupTemplatePath))
            {
                return false;
            }

            UnityPackageStagingWriter.WriteTextAsset(
                tempExtractDir,
                File.ReadAllText(PatchImporterTemplatePath),
                TempPatchEditorRoot + "/YUCPPatchImporter.cs",
                UnityPackageStagingWriter.MonoImporterMeta());

            UnityPackageStagingWriter.WriteTextAsset(
                tempExtractDir,
                File.ReadAllText(PatchCleanupTemplatePath),
                TempPatchEditorRoot + "/YUCPPatchCleanup.cs",
                UnityPackageStagingWriter.MonoImporterMeta());

            return true;
        }

        private static void InjectNativeLibraries(string tempExtractDir)
        {
            foreach (NativeLibrary nativeLibrary in NativeLibraries)
            {
                if (!File.Exists(nativeLibrary.SourcePath))
                {
                    Debug.LogWarning($"[PatchImportPackageInjector] HDiffPatch native library not found: {nativeLibrary.SourcePath}");
                    continue;
                }

                InjectNativeLibrary(tempExtractDir, nativeLibrary);
            }
        }

        private static void InjectNativeLibrary(string tempExtractDir, NativeLibrary nativeLibrary)
        {
            string guid = Guid.NewGuid().ToString("N");
            string metaPath = nativeLibrary.SourcePath + ".meta";
            string metaContent = File.Exists(metaPath)
                ? File.ReadAllText(metaPath)
                : GenerateNativePluginMeta(guid, nativeLibrary.SourcePath);

            UnityPackageStagingWriter.WriteFileAsset(
                tempExtractDir,
                nativeLibrary.SourcePath,
                nativeLibrary.TargetPath,
                guid,
                metaContent);

            Debug.Log($"[PatchImportPackageInjector] Copied HDiffPatch native library to temp package: {nativeLibrary.TargetPath}");
        }

        private static string GenerateNativePluginMeta(string guid, string sourcePath)
        {
            string fileName = Path.GetFileName(sourcePath);
            string fallbackOs = fileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ? "Linux" : "AnyOS";
            string fallbackCpu = fileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ? "x86_64" : "AnyCPU";

            return "fileFormatVersion: 2\nguid: " + guid + "\nPluginImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  iconMap: {}\n  executionOrder: {}\n  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n  - first:\n      : Any\n    second:\n      enabled: 0\n  - first:\n      Any: \n    second:\n      enabled: 0\n  - first:\n      Editor: Editor\n    second:\n      enabled: 1\n      settings:\n        CPU: " + fallbackCpu + "\n        DefaultValueInitialized: true\n        OS: " + fallbackOs + "\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
        }

        private sealed class NativeLibrary
        {
            public readonly string SourcePath;
            public readonly string TargetPath;

            public NativeLibrary(string sourcePath, string targetPath)
            {
                SourcePath = sourcePath;
                TargetPath = targetPath;
            }
        }
    }
}
