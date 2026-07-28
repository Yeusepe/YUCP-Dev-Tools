using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class PatchRuntimeCompiler
    {
        private const string SourceRoot = "build-src/YUCP.PatchRuntime";
        private const string TempOutputDir = "Temp/YUCP/PatchRuntimeBuild";
        private const string TempOutputDll = TempOutputDir + "/YUCP.PatchRuntime.dll";
        private const string PackageOutputDll = "Packages/com.yucp.devtools/Editor/PackageExporter/Binaries/YUCP.PatchRuntime.dll";
        private const string SourceRootEnvironmentVariable = "YUCP_PATCH_RUNTIME_SOURCE_ROOT";
        private const string OutputDllEnvironmentVariable = "YUCP_PATCH_RUNTIME_OUTPUT_DLL";

        [MenuItem("Tools/YUCP/Others/Package Exporter/Rebuild Patch Runtime DLL")]
        public static void BuildFromMenu()
        {
            Build(waitForCompletion: true, exitWhenFinished: false);
        }

        public static void BuildFromCommandLine()
        {
            bool success = Build(waitForCompletion: true, exitWhenFinished: true);
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static bool Build(bool waitForCompletion, bool exitWhenFinished)
        {
            string sourceRoot = ResolveSourceRoot();
            string outputDll = ResolveOutputDll();

            string[] scripts = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (scripts.Length == 0)
            {
                Debug.LogError($"[PatchRuntimeCompiler] No patch runtime source files found under {sourceRoot}.");
                return false;
            }

            Directory.CreateDirectory(TempOutputDir);
            string outputDir = Path.GetDirectoryName(outputDll);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var builder = new AssemblyBuilder(TempOutputDll, scripts)
            {
                flags = AssemblyBuilderFlags.EditorAssembly,
                referencesOptions = ReferencesOptions.UseEngineModules,
                excludeReferences = new[] { outputDll }
            };

            bool finished = false;
            bool success = false;
            builder.buildStarted += assemblyPath =>
            {
                Debug.Log($"[PatchRuntimeCompiler] Build started: {assemblyPath}");
            };
            builder.buildFinished += (assemblyPath, messages) =>
            {
                int errorCount = messages.Count(message => message.type == CompilerMessageType.Error);
                int warningCount = messages.Count(message => message.type == CompilerMessageType.Warning);

                foreach (CompilerMessage message in messages)
                {
                    string log = $"[PatchRuntimeCompiler] {message.file}:{message.line} {message.message}";
                    if (message.type == CompilerMessageType.Error)
                    {
                        Debug.LogError(log);
                    }
                    else
                    {
                        Debug.LogWarning(log);
                    }
                }

                success = errorCount == 0 && CopyOutputs(outputDll);
                finished = true;

                Debug.Log(
                    $"[PatchRuntimeCompiler] Build finished. Warnings: {warningCount}, Errors: {errorCount}, Copied: {success}");

                if (exitWhenFinished)
                {
                    EditorApplication.Exit(success ? 0 : 1);
                }
            };

            if (!builder.Build())
            {
                Debug.LogError("[PatchRuntimeCompiler] Unity could not start the patch runtime build because scripts are already compiling.");
                return false;
            }

            if (waitForCompletion)
            {
                while (!finished && builder.status != AssemblyBuilderStatus.Finished)
                {
                    Thread.Sleep(10);
                }
            }

            return success;
        }

        private static string ResolveSourceRoot()
        {
            string configured = Environment.GetEnvironmentVariable(SourceRootEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured) ? SourceRoot : configured;
        }

        private static string ResolveOutputDll()
        {
            string configured = Environment.GetEnvironmentVariable(OutputDllEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured) ? PackageOutputDll : configured;
        }

        private static bool CopyOutputs(string outputDll)
        {
            try
            {
                if (!File.Exists(TempOutputDll))
                {
                    Debug.LogError($"[PatchRuntimeCompiler] Expected build output was not created: {TempOutputDll}");
                    return false;
                }

                File.Copy(TempOutputDll, outputDll, true);

                string tempPdb = Path.ChangeExtension(TempOutputDll, ".pdb");
                string packagePdb = Path.ChangeExtension(outputDll, ".pdb");
                if (File.Exists(tempPdb))
                {
                    File.Copy(tempPdb, packagePdb, true);
                }

                ImportProjectAssetIfNeeded(outputDll);
                if (File.Exists(packagePdb))
                {
                    ImportProjectAssetIfNeeded(packagePdb);
                }
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PatchRuntimeCompiler] Failed to copy patch runtime build output: {ex.Message}");
                return false;
            }
        }

        private static void ImportProjectAssetIfNeeded(string path)
        {
            string normalized = path.Replace('\\', '/');
            if (Path.IsPathRooted(normalized))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
                string fullPath = Path.GetFullPath(normalized).Replace('\\', '/');
                if (!fullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                normalized = fullPath.Substring(projectRoot.Length).TrimStart('/');
            }

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                AssetDatabase.ImportAsset(normalized, ImportAssetOptions.ForceUpdate);
            }
        }
    }
}
