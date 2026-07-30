using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class DirectVpmInstallerRuntimeCompiler
    {
        private const string TemplateRoot =
            "Packages/com.yucp.devtools/Editor/PackageExporter/Templates";
        private const string TempOutputDirectory =
            "Temp/YUCP/DirectVpmInstallerRuntimeBuild";
        private const string TempOutputDll =
            TempOutputDirectory + "/YUCP.DirectVpmInstaller.Runtime.dll";
        private const string PackageOutputDll =
            "Packages/com.yucp.devtools/Editor/PackageExporter/Binaries/" +
            "YUCP.DirectVpmInstaller.Runtime.dll";
        private const string OutputEnvironmentVariable =
            "YUCP_DIRECT_VPM_INSTALLER_OUTPUT_DLL";

        private static readonly string[] SourceFileNames =
        {
            "DirectVpmInstaller.cs",
            "FullDomainReload.cs",
            "GuardianTransaction.cs",
            "InstallerHealthTools.cs",
            "InstallerPreflight.cs",
            "InstallerTransactionManager.cs",
            "PackageGuardianMini.cs",
            "YUCPPatchCleanup.cs",
            "YUCPPatchImporter.cs",
        };

        [MenuItem(
            "Tools/YUCP/Others/Package Exporter/" +
            "Rebuild Direct VPM Installer Runtime DLL")]
        public static void BuildFromMenu()
        {
            Build(waitForCompletion: true, exitWhenFinished: false);
        }

        public static void BuildFromCommandLine()
        {
            bool success = Build(
                waitForCompletion: true,
                exitWhenFinished: true);
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static bool Build(
            bool waitForCompletion,
            bool exitWhenFinished)
        {
            string[] scripts = SourceFileNames
                .Select(fileName => Path.Combine(TemplateRoot, fileName))
                .Select(path => path.Replace('\\', '/'))
                .ToArray();
            string[] missing = scripts
                .Where(path => !File.Exists(path))
                .ToArray();
            if (missing.Length > 0)
            {
                Debug.LogError(
                    "[DirectVpmInstallerRuntimeCompiler] Missing source files: " +
                    string.Join(", ", missing));
                return false;
            }

            string outputDll = ResolveOutputDll();
            Directory.CreateDirectory(TempOutputDirectory);
            string outputDirectory = Path.GetDirectoryName(outputDll);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var builder = new AssemblyBuilder(TempOutputDll, scripts)
            {
                flags = AssemblyBuilderFlags.EditorAssembly,
                referencesOptions = ReferencesOptions.UseEngineModules,
                excludeReferences = new[] { outputDll },
            };

            bool finished = false;
            bool success = false;
            builder.buildStarted += assemblyPath =>
            {
                Debug.Log(
                    "[DirectVpmInstallerRuntimeCompiler] Build started: " +
                    assemblyPath);
            };
            builder.buildFinished += (assemblyPath, messages) =>
            {
                int errorCount = messages.Count(
                    message => message.type == CompilerMessageType.Error);
                foreach (CompilerMessage message in messages)
                {
                    string diagnostic =
                        $"[DirectVpmInstallerRuntimeCompiler] {message.file}:" +
                        $"{message.line} {message.message}";
                    if (message.type == CompilerMessageType.Error)
                    {
                        Debug.LogError(diagnostic);
                    }
                    else if (message.type == CompilerMessageType.Warning)
                    {
                        Debug.LogWarning(diagnostic);
                    }
                }

                success = errorCount == 0 && CopyOutput(outputDll);
                finished = true;
                Debug.Log(
                    "[DirectVpmInstallerRuntimeCompiler] Build finished. " +
                    $"Errors: {errorCount}, SHA-256: " +
                    (success ? ComputeSha256(outputDll) : "unavailable"));

                if (exitWhenFinished)
                {
                    EditorApplication.Exit(success ? 0 : 1);
                }
            };

            if (!builder.Build())
            {
                Debug.LogError(
                    "[DirectVpmInstallerRuntimeCompiler] Unity could not " +
                    "start the build because scripts are already compiling.");
                return false;
            }

            if (waitForCompletion)
            {
                while (!finished &&
                    builder.status != AssemblyBuilderStatus.Finished)
                {
                    Thread.Sleep(10);
                }
            }
            return success;
        }

        private static string ResolveOutputDll()
        {
            string configured = Environment.GetEnvironmentVariable(
                OutputEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured)
                ? PackageOutputDll
                : configured;
        }

        private static bool CopyOutput(string outputDll)
        {
            try
            {
                if (!File.Exists(TempOutputDll))
                {
                    Debug.LogError(
                        "[DirectVpmInstallerRuntimeCompiler] Expected build " +
                        "output was not created: " + TempOutputDll);
                    return false;
                }

                File.Copy(TempOutputDll, outputDll, true);
                string tempPdb = Path.ChangeExtension(TempOutputDll, ".pdb");
                string outputPdb = Path.ChangeExtension(outputDll, ".pdb");
                if (File.Exists(tempPdb))
                {
                    File.Copy(tempPdb, outputPdb, true);
                }
                AssetDatabase.ImportAsset(
                    outputDll.Replace('\\', '/'),
                    ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[DirectVpmInstallerRuntimeCompiler] Could not copy the " +
                    "runtime output: " + ex.Message);
                return false;
            }
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return string.Concat(
                    sha256.ComputeHash(stream)
                        .Select(value => value.ToString("x2")));
            }
        }
    }
}
