using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class PrecompiledInstallerRuntimeTests
    {
        private static Type GetDirectInstallerType()
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "YUCP.DirectVpmInstaller.Runtime", StringComparison.Ordinal));
            Assert.That(assembly, Is.Not.Null, "Expected YUCP.DirectVpmInstaller.Runtime assembly to be loaded.");

            Type type = assembly.GetType("YUCP.DirectVpmInstaller.DirectVpmInstaller", throwOnError: false);
            Assert.That(type, Is.Not.Null, "Expected DirectVpmInstaller type to be available.");
            return type;
        }

        [TestCase("com.test.toon", "com.test.pro", "11.0.27", "com.test.toon", "com.test.pro")]
        [TestCase("com.test.toon", "com.test.pro", "11.0.27", "com.test.other", null)]
        [TestCase("com.test.toon", "com.test.toon", "9.0.0", "com.test.toon", null)]
        [TestCase("com.test.toon", "com.test.pro", null, "com.test.toon", null)]
        public void DirectVpmInstaller_RecognizesInstalledReplacement(string requested, string installed, string version, string legacy, string expected)
        {
            string path = Path.Combine(Path.GetTempPath(), "yucp-replacement-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, new JObject
                {
                    ["name"] = installed,
                    ["version"] = version,
                    ["legacyPackages"] = new JArray(legacy)
                }.ToString());
                MethodInfo method = GetDirectInstallerType().GetMethod("FindInstalledReplacement", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                Assert.That(method.Invoke(null, new object[] { requested, new[] { path, path, path + ".missing" } }), Is.EqualTo(expected));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void DirectVpmInstaller_DoesNotRepeatPromptUntilRetryOrReimport()
        {
            string path = Path.Combine(Path.GetTempPath(), "yucp-prompt-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "{}");
                MethodInfo method = GetDirectInstallerType().GetMethod("TryBeginDependencyPrompt", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                Assert.That(method.Invoke(null, new object[] { path, false }), Is.True);
                Assert.That(method.Invoke(null, new object[] { path, false }), Is.False);
                Assert.That(method.Invoke(null, new object[] { path, true }), Is.True);
                Assert.That(method.Invoke(null, new object[] { path, false }), Is.False);
                File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
                Assert.That(method.Invoke(null, new object[] { path, false }), Is.True);
                Assert.That(File.ReadAllText(path), Is.EqualTo("{}"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void PrecompiledInstallerRuntime_BinaryAndMetaExist()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(
                projectRoot,
                "Packages",
                "com.yucp.devtools",
                "Editor",
                "PackageExporter",
                "Binaries",
                "YUCP.DirectVpmInstaller.Runtime.dll");

            Assert.That(File.Exists(dllPath), Is.True, "Expected the checked-in precompiled installer runtime binary to exist.");
            Assert.That(File.Exists(dllPath + ".meta"), Is.True, "Expected the checked-in precompiled installer runtime binary to have a Unity .meta file.");
        }

        [Test]
        public void DirectVpmInstallerTemplateAsmdef_DoesNotReusePrecompiledRuntimeAssemblyName()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string asmdefPath = Path.Combine(
                projectRoot,
                "Packages",
                "com.yucp.devtools",
                "Editor",
                "PackageExporter",
                "Templates",
                "DirectVpmInstaller.asmdef");

            Assert.That(File.Exists(asmdefPath), Is.True, "Expected the DirectVpmInstaller template asmdef to exist.");

            JObject asmdef = JObject.Parse(File.ReadAllText(asmdefPath));
            string assemblyName = asmdef.Value<string>("name");
            var defineConstraints = asmdef["defineConstraints"] as JArray;

            Assert.That(assemblyName, Is.EqualTo("YUCP.DirectVpmInstaller.Source"));
            Assert.That(assemblyName, Is.Not.EqualTo("YUCP.DirectVpmInstaller.Runtime"));
            Assert.That(asmdef.Value<bool>("autoReferenced"), Is.False, "Template source should not be an active local editor runtime.");
            Assert.That(
                defineConstraints?.Values<string>(),
                Does.Contain("YUCP_COMPILE_INSTALLER_TEMPLATE_SOURCE"),
                "Template source should only compile when explicitly opted in; exported packages use the precompiled DLL.");
        }

        [Test]
        public void PrecompiledInstallerRuntime_CanInjectIntoTempPatchEditorFolder()
        {
            MethodInfo method = typeof(PackageBuilder).GetMethod(
                "TryInjectPrecompiledInstallerRuntime",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            string tempExtractDir = Path.Combine(Path.GetTempPath(), "yucp-precompiled-installer-temp-editor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempExtractDir);

            try
            {
                bool injected = (bool)method.Invoke(null, new object[]
                {
                    tempExtractDir,
                    "Packages/com.yucp.temp/Editor",
                });

                Assert.That(injected, Is.True, "Expected the installer runtime binary to be emitted for temp patch imports.");

                string[] pathnameFiles = Directory.GetFiles(tempExtractDir, "pathname", SearchOption.AllDirectories);
                string matchedPathnameFile = pathnameFiles.FirstOrDefault(pathnameFile =>
                {
                    string pathname = File.ReadAllText(pathnameFile).Replace('\\', '/');
                    return string.Equals(
                        pathname,
                        "Packages/com.yucp.temp/Editor/YUCP.DirectVpmInstaller.Runtime.dll",
                        System.StringComparison.Ordinal);
                });

                Assert.That(matchedPathnameFile, Is.Not.Null, "Expected an emitted installer runtime DLL pathname for the temp patch editor folder.");
            }
            finally
            {
                if (Directory.Exists(tempExtractDir))
                {
                    Directory.Delete(tempExtractDir, true);
                }
            }
        }

        [Test]
        public void PrecompiledInstallerRuntime_StillInjectsInstallerPreflightBootstrap()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string installerScriptPath = Path.Combine(
                projectRoot,
                "Packages",
                "com.yucp.devtools",
                "Editor",
                "PackageExporter",
                "Templates",
                "DirectVpmInstaller.cs");
            string tempExtractDir = Path.Combine(Path.GetTempPath(), "yucp-preflight-bootstrap-" + System.Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(tempExtractDir);

            try
            {
                MethodInfo method = typeof(PackageBuilder).GetMethod(
                    "TryInjectInstallerPreflightBootstrap",
                    BindingFlags.Static | BindingFlags.NonPublic);

                Assert.That(method, Is.Not.Null);
                Assert.That(File.Exists(installerScriptPath), Is.True, "Expected the DirectVpmInstaller template source to exist.");

                bool injected = (bool)method.Invoke(null, new object[]
                {
                    tempExtractDir,
                    "Packages/com.yucp.components/Editor",
                    installerScriptPath,
                });

                Assert.That(injected, Is.True, "Expected the installer preflight bootstrap to be emitted.");

                string[] pathnameFiles = Directory.GetFiles(tempExtractDir, "pathname", SearchOption.AllDirectories);
                string matchedPathnameFile = pathnameFiles.FirstOrDefault(pathnameFile =>
                {
                    string pathname = File.ReadAllText(pathnameFile).Replace('\\', '/');
                    return pathname.StartsWith("Packages/com.yucp.components/Editor/YUCP_InstallerPreflight_", System.StringComparison.Ordinal) &&
                           pathname.EndsWith(".cs", System.StringComparison.Ordinal);
                });

                Assert.That(matchedPathnameFile, Is.Not.Null, "Expected an emitted installer preflight bootstrap pathname.");

                string assetContent = File.ReadAllText(Path.Combine(Path.GetDirectoryName(matchedPathnameFile), "asset"));
                Assert.That(assetContent, Does.Contain("InstallerPreflight_"), "Expected the emitted bootstrap asset to contain a concrete installer preflight class name.");
            }
            finally
            {
                if (Directory.Exists(tempExtractDir))
                {
                    Directory.Delete(tempExtractDir, true);
                }
            }
        }

        [Test]
        public void DirectVpmInstaller_CleanupTemporaryFiles_PrunesEmptyInstalledPackageResidue()
        {
            Type directInstallerType = GetDirectInstallerType();
            MethodInfo method = directInstallerType.GetMethod(
                "CleanupTemporaryFiles",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string installedRoot = Path.Combine(projectRoot, "Packages", "yucp.installed-packages");
            string packageRoot = Path.Combine(installedRoot, "com.test.cleanup");
            string tempRoot = Path.Combine(packageRoot, "_temp");
            string tempJsonPath = Path.Combine(tempRoot, $"YUCP_TempInstall_{Guid.NewGuid():N}.json");
            string staleEmptyRoot = Path.Combine(installedRoot, $"stale-empty-{Guid.NewGuid():N}");
            string staleEmptyMetaPath = staleEmptyRoot + ".meta";
            string metadataPath = Path.Combine(packageRoot, "YUCP_PackageInfo.json");
            string profileFolder = Path.Combine(Application.dataPath, "YUCP", "ExportProfiles");
            string profileSentinel = Path.Combine(profileFolder, "installer-preservation-" + Guid.NewGuid().ToString("N") + ".txt");
            Directory.CreateDirectory(profileFolder);
            File.WriteAllText(profileSentinel, "Authoring files must stay in Assets.");

            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(staleEmptyRoot);
            File.WriteAllText(tempJsonPath, "{\n  \"name\": \"com.test.cleanup\",\n  \"version\": \"1.0.0\"\n}");
            File.WriteAllText(tempJsonPath + ".meta", "fileFormatVersion: 2\nguid: 11111111111111111111111111111111\nTextScriptImporter:\n");
            File.WriteAllText(metadataPath, "{\"packageName\":\"com.test.cleanup\"}");
            File.WriteAllText(metadataPath + ".meta", "fileFormatVersion: 2\nguid: 22222222222222222222222222222222\nTextScriptImporter:\n");
            File.WriteAllText(staleEmptyMetaPath, "fileFormatVersion: 2\nguid: 33333333333333333333333333333333\nfolderAsset: yes\nDefaultImporter:\n");

            try
            {
                method.Invoke(
                    null,
                    new object[] { tempJsonPath, null });

                Assert.That(File.Exists(tempJsonPath), Is.False, "Expected the temp install descriptor to be deleted.");
                Assert.That(File.Exists(tempJsonPath + ".meta"), Is.False, "Expected the temp install descriptor meta file to be deleted.");
                Assert.That(Directory.Exists(staleEmptyRoot), Is.False, "Expected cleanup to prune empty stale package residue.");
                Assert.That(File.Exists(staleEmptyMetaPath), Is.False, "Expected cleanup to delete the stale residue meta file.");
                Assert.That(File.Exists(metadataPath), Is.True, "Expected cleanup to preserve actual package metadata.");
                Assert.That(File.ReadAllText(profileSentinel), Is.EqualTo("Authoring files must stay in Assets."));
            }
            finally
            {
                File.Delete(profileSentinel);
                File.Delete(profileSentinel + ".meta");
                if (Directory.Exists(packageRoot))
                {
                    Directory.Delete(packageRoot, true);
                }

                if (Directory.Exists(staleEmptyRoot))
                {
                    Directory.Delete(staleEmptyRoot, true);
                }

                if (File.Exists(staleEmptyMetaPath))
                {
                    File.Delete(staleEmptyMetaPath);
                }

                if (File.Exists(tempJsonPath))
                {
                    File.Delete(tempJsonPath);
                }

                if (File.Exists(tempJsonPath + ".meta"))
                {
                    File.Delete(tempJsonPath + ".meta");
                }

                if (File.Exists(metadataPath))
                {
                    File.Delete(metadataPath);
                }

                if (File.Exists(metadataPath + ".meta"))
                {
                    File.Delete(metadataPath + ".meta");
                }
            }
        }

        [Test]
        public void DirectVpmInstaller_CreatorCompanionRoot_UsesLinuxXdgDataHome()
        {
            Type directInstallerType = GetDirectInstallerType();
            MethodInfo method = directInstallerType.GetMethod(
                "GetCreatorCompanionRootForPlatform",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            string resolvedRoot = method.Invoke(null, new object[]
            {
                RuntimePlatform.LinuxEditor,
                @"C:\Users\Test\AppData\Local",
                "/home/tester",
                "/var/test-xdg",
            }) as string;

            Assert.That(resolvedRoot, Is.Not.Null);
            Assert.That(resolvedRoot.Replace('\\', '/'), Is.EqualTo("/var/test-xdg/VRChatCreatorCompanion"));
        }

        [Test]
        public void DirectVpmInstaller_CreatorCompanionRoot_FallsBackToLinuxLocalShare()
        {
            Type directInstallerType = GetDirectInstallerType();
            MethodInfo method = directInstallerType.GetMethod(
                "GetCreatorCompanionRootForPlatform",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            string resolvedRoot = method.Invoke(null, new object[]
            {
                RuntimePlatform.LinuxEditor,
                @"C:\Users\Test\AppData\Local",
                "/home/tester",
                null,
            }) as string;

            Assert.That(resolvedRoot, Is.Not.Null);
            Assert.That(resolvedRoot.Replace('\\', '/'), Is.EqualTo("/home/tester/.local/share/VRChatCreatorCompanion"));
        }

        [Test]
        public void DirectVpmInstaller_UpsertUserRepoSetting_UpdatesExistingEntryByUrl()
        {
            Type directInstallerType = GetDirectInstallerType();
            MethodInfo method = directInstallerType.GetMethod(
                "UpsertUserRepoSetting",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            JObject settings = new JObject();

            method.Invoke(null, new object[]
            {
                settings,
                "First Repo Name",
                "https://packages.example.com/index.json",
                @"C:\Cache\repo-a.json",
                null,
            });

            method.Invoke(null, new object[]
            {
                settings,
                "Updated Repo Name",
                "https://packages.example.com/index.json",
                @"C:\Cache\repo-b.json",
                "com.example.repo",
            });

            JArray userRepos = settings["userRepos"] as JArray;
            Assert.That(userRepos, Is.Not.Null);
            Assert.That(userRepos.Count, Is.EqualTo(1));
            Assert.That(userRepos[0]["name"]?.ToString(), Is.EqualTo("Updated Repo Name"));
            Assert.That(userRepos[0]["url"]?.ToString(), Is.EqualTo("https://packages.example.com/index.json"));
            Assert.That(userRepos[0]["localPath"]?.ToString(), Is.EqualTo(@"C:\Cache\repo-b.json"));
            Assert.That(userRepos[0]["id"]?.ToString(), Is.EqualTo("com.example.repo"));
        }

        [Test]
        public void DirectVpmInstaller_UpsertUserRepoSetting_DoesNotDuplicateAcrossUrlSpellings()
        {
            Type directInstallerType = GetDirectInstallerType();
            MethodInfo method = directInstallerType.GetMethod(
                "UpsertUserRepoSetting",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            // How VCC itself stores the built-in source.
            JObject settings = JObject.Parse(@"{
                ""userRepos"": [
                    {
                        ""name"": ""Official"",
                        ""id"": ""com.vrchat.repos.official"",
                        ""url"": ""https://packages.vrchat.com/official"",
                        ""localPath"": ""C:\\Repos\\vrc-official.json""
                    }
                ]
            }");

            // What an exported package declares for the same listing.
            method.Invoke(null, new object[]
            {
                settings,
                "VRChat Official",
                "https://packages.vrchat.com/official?download",
                @"C:\Cache\official-abc123.json",
                null,
            });

            JArray userRepos = settings["userRepos"] as JArray;
            Assert.That(userRepos, Is.Not.Null);
            Assert.That(userRepos.Count, Is.EqualTo(1), "Query-string variant must not create a second source.");

            // A genuinely different listing still gets added.
            method.Invoke(null, new object[]
            {
                settings,
                "Other Repo",
                "https://packages.example.com/index.json",
                @"C:\Cache\other.json",
                null,
            });

            Assert.That(userRepos.Count, Is.EqualTo(2));
        }

        [Test]
        public void DirectVpmInstaller_FriendlyRepositoryLabel_FallsBackToHostForGenericCustomNames()
        {
            Type directInstallerType = GetDirectInstallerType();
            MethodInfo method = directInstallerType.GetMethod(
                "GetFriendlyRepositoryLabel",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            string genericCustomLabel = method.Invoke(null, new object[]
            {
                "com.aiczk.asset-previewer (Custom)",
                "https://packages.aiczk.com/vpm.json",
            }) as string;

            string emptyCustomLabel = method.Invoke(null, new object[]
            {
                "Custom",
                "https://repo.example.com/index.json",
            }) as string;

            Assert.That(genericCustomLabel, Is.EqualTo("packages.aiczk.com"));
            Assert.That(emptyCustomLabel, Is.EqualTo("repo.example.com"));
        }

        [Test]
        public void LegacyShell_InjectionCanSkipOptionalYucpMetadata()
        {
            string packagePath = null;
            ExportProfile profile = null;

            try
            {
                packagePath = CreateUnityPackage(new Dictionary<string, byte[]>
                {
                    ["shell/pathname"] = Encoding.UTF8.GetBytes("Assets/Dummy.txt"),
                    ["shell/asset"] = Encoding.UTF8.GetBytes("dummy"),
                });

                profile = ScriptableObject.CreateInstance<ExportProfile>();
                profile.packageName = "Clean Export";
                profile.embedYucpMetadata = false;

                Type embedContextType = typeof(PackageBuilder).GetNestedType("PackageEmbedContext", BindingFlags.NonPublic);
                Assert.That(embedContextType, Is.Not.Null);
                ConstructorInfo embedContextCtor = embedContextType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(ExportProfile), typeof(ICollection<string>) },
                    modifiers: null);
                Assert.That(embedContextCtor, Is.Not.Null);
                object embedContext = embedContextCtor.Invoke(new object[] { profile, null });
                Assert.That(embedContext, Is.Not.Null);

                MethodInfo method = typeof(PackageBuilder).GetMethod(
                    "InjectPackageJsonInstallerAndBundles",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);

                method.Invoke(null, new object[]
                {
                    packagePath,
                    "{ \"name\": \"com.example.clean-export\" }",
                    new Dictionary<string, string>(),
                    new List<AssemblyObfuscationSettings>(),
                    profile,
                    false,
                    null,
                    embedContext,
                    null,
                });

                string[] pathnames = ReadPackagePathnames(packagePath);
                Assert.That(pathnames, Has.Some.Matches<string>(path => path.StartsWith("Packages/com.yucp.temp/Clean-Export/_temp/YUCP_TempInstall_", StringComparison.Ordinal)));
                Assert.That(pathnames, Has.Some.EqualTo("Packages/com.yucp.temp/package.json"));
                Assert.That(pathnames, Has.Some.EqualTo("Packages/com.yucp.temp/Editor/YUCP.DirectVpmInstaller.Runtime.dll"));
                Assert.That(pathnames, Has.None.EqualTo("Packages/com.yucp.temp/Plugins/hdiffz.dll"));
                Assert.That(pathnames, Has.None.EqualTo("Packages/com.yucp.temp/Plugins/hpatchz.dll"));
                Assert.That(pathnames, Has.None.EqualTo("Packages/com.yucp.temp/Plugins/hdiffinfo.dll"));
                Assert.That(pathnames, Has.None.EqualTo("Packages/com.yucp.temp/Plugins/Linux/x86_64/libhdiffz.so"));
                Assert.That(pathnames, Has.None.EqualTo("Packages/com.yucp.temp/Plugins/Linux/x86_64/libhpatchz.so"));
                Assert.That(pathnames, Has.None.EqualTo("Packages/com.yucp.temp/Plugins/Linux/x86_64/libhdiffinfo.so"));
                Assert.That(pathnames, Has.None.Matches<string>(path => path.EndsWith("YUCP_PackageInfo.json", StringComparison.Ordinal)));
                Assert.That(pathnames, Has.None.Matches<string>(path => path.StartsWith("Packages/yucp.installed-packages/", StringComparison.Ordinal)));
            }
            finally
            {
                DeleteIfPresent(packagePath);
                if (profile != null)
                {
                    UnityEngine.Object.DestroyImmediate(profile);
                }
            }
        }

        [Test]
        public void DirectInstallerRuntime_SearchesTempPackageWorkspace()
        {
            Type directInstallerType = GetDirectInstallerType();
            MethodInfo method = directInstallerType.GetMethod(
                "GetInstallerEditorPaths",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] paths = method.Invoke(null, new object[] { projectRoot }) as string[];

            Assert.That(paths, Is.Not.Null);
            Assert.That(paths, Has.Some.EqualTo(Path.Combine(projectRoot, "Packages", "com.yucp.temp", "Editor")));
        }

        [Test]
        public void PrecompiledInstallerRuntime_ServerFirstKeepsInstallerAndPatchRuntime()
        {
            string packagePath = null;
            ExportProfile profile = null;

            try
            {
                packagePath = CreateUnityPackage(new Dictionary<string, byte[]>
                {
                    ["shell/pathname"] = Encoding.UTF8.GetBytes("Assets/Dummy.txt"),
                    ["shell/asset"] = Encoding.UTF8.GetBytes("dummy"),
                });

                profile = ScriptableObject.CreateInstance<ExportProfile>();
                profile.packageName = "Wasbeer";

                Type embedContextType = typeof(PackageBuilder).GetNestedType("PackageEmbedContext", BindingFlags.NonPublic);
                Assert.That(embedContextType, Is.Not.Null);
                ConstructorInfo embedContextCtor = embedContextType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(ExportProfile), typeof(ICollection<string>) },
                    modifiers: null);
                Assert.That(embedContextCtor, Is.Not.Null);
                object embedContext = embedContextCtor.Invoke(new object[] { profile, null });
                Assert.That(embedContext, Is.Not.Null);

                MethodInfo method = typeof(PackageBuilder).GetMethod(
                    "InjectPackageJsonInstallerAndBundles",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);

                method.Invoke(null, new object[]
                {
                    packagePath,
                    "{ \"name\": \"com.example.server-first\" }",
                    new Dictionary<string, string>(),
                    new List<AssemblyObfuscationSettings>(),
                    profile,
                    true,
                    "{\"packageName\":\"Wasbeer\"}",
                    embedContext,
                    null,
                });

                string[] pathnames = ReadPackagePathnames(packagePath);
                Assert.That(pathnames, Has.Some.Matches<string>(path => path.StartsWith("Packages/yucp.installed-packages/", StringComparison.Ordinal)));
                Assert.That(pathnames, Has.Some.Matches<string>(path => path.Contains("/YUCP_TempInstall_", StringComparison.Ordinal)));
                Assert.That(pathnames, Has.Some.EqualTo("Packages/yucp.installed-packages/Editor/YUCP.DirectVpmInstaller.Runtime.dll"));
                Assert.That(pathnames, Has.Some.Matches<string>(path => path.StartsWith("Packages/com.yucp.temp/", StringComparison.Ordinal)));
                Assert.That(pathnames, Has.Some.EqualTo("Packages/com.yucp.temp/Plugins/hdiffz.dll"));
                Assert.That(pathnames, Has.Some.EqualTo("Packages/com.yucp.temp/Plugins/hpatchz.dll"));
                Assert.That(pathnames, Has.Some.EqualTo("Packages/com.yucp.temp/Plugins/hdiffinfo.dll"));
                Assert.That(pathnames, Has.Some.EqualTo("Packages/com.yucp.temp/Plugins/Linux/x86_64/libhdiffz.so"));
                Assert.That(pathnames, Has.Some.EqualTo("Packages/com.yucp.temp/Plugins/Linux/x86_64/libhpatchz.so"));
                Assert.That(pathnames, Has.Some.EqualTo("Packages/com.yucp.temp/Plugins/Linux/x86_64/libhdiffinfo.so"));
                Assert.That(pathnames, Has.None.EqualTo("Packages/com.yucp.temp/Editor/YUCP.DirectVpmInstaller.Runtime.dll"));
                Assert.That(pathnames, Has.None.Matches<string>(path => path.EndsWith("YUCP_ProtectedPayload.json", StringComparison.Ordinal)));
                Assert.That(pathnames, Has.None.Matches<string>(path => path.EndsWith("YUCP_ProtectedImportIntent.json", StringComparison.Ordinal)));
                Assert.That(pathnames, Has.Some.Matches<string>(path => path.EndsWith("YUCP_PackageInfo.json", StringComparison.Ordinal)));
            }
            finally
            {
                if (packagePath != null)
                {
                    DeleteIfPresent(packagePath);
                }

                if (profile != null)
                {
                    UnityEngine.Object.DestroyImmediate(profile);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExportDefersIncomingCodeUntilTheInstallerCanResolveDependencies(bool embedMetadata)
        {
            string bundle = Path.Combine(Path.GetTempPath(), "yucp-bundle-bootstrap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(bundle);
            string packagePath = null;
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            try
            {
                foreach (string file in new[] { "NeedsVrcFury.cs", "Runtime.asmdef", "Runtime.asmref", "Plugin.dll", "package.json" })
                    File.WriteAllText(Path.Combine(bundle, file), "{}");
                packagePath = CreateUnityPackage(new Dictionary<string, byte[]>
                {
                    ["script/pathname"] = Encoding.UTF8.GetBytes("Assets/NeedsComponents.cs"),
                    ["script/asset"] = Encoding.UTF8.GetBytes("class NeedsComponents : Missing.Dependency {}"),
                    ["prefab/pathname"] = Encoding.UTF8.GetBytes("Assets/Avatar.prefab"),
                    ["prefab/asset"] = Encoding.UTF8.GetBytes("prefab"),
                });
                profile.packageName = "Bootstrap Test";
                profile.embedYucpMetadata = embedMetadata;
                Type contextType = typeof(PackageBuilder).GetNestedType("PackageEmbedContext", BindingFlags.NonPublic);
                object context = Activator.CreateInstance(contextType, new object[] { profile, null });
                typeof(PackageBuilder).GetMethod("InjectPackageJsonInstallerAndBundles", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[]
                    {
                        packagePath, "{ 'name': 'com.example.bootstrap', 'vpmDependencies': { 'com.example.missing': '1.0.0' } }",
                        new Dictionary<string, string> { ["com.example.bundle"] = bundle },
                        new List<AssemblyObfuscationSettings>(), profile, false, null, context, null
                    });
                string[] paths = ReadPackagePathnames(packagePath);
                Assert.That(paths, Does.Contain("Assets/NeedsComponents.cs.yucp_disabled"));
                Assert.That(paths, Does.Contain("Assets/Avatar.prefab"));
                foreach (string file in new[] { "NeedsVrcFury.cs", "Runtime.asmdef", "Runtime.asmref", "Plugin.dll" })
                {
                    Assert.That(paths, Does.Contain("Packages/com.example.bundle/" + file + ".yucp_disabled"));
                    Assert.That(paths, Does.Not.Contain("Packages/com.example.bundle/" + file));
                }
                Assert.That(paths, Does.Contain("Packages/com.example.bundle/package.json"));
                string installerRoot = embedMetadata ? "Packages/yucp.installed-packages/Editor" : "Packages/com.yucp.temp/Editor";
                Assert.That(paths, Does.Contain(installerRoot + "/YUCP.DirectVpmInstaller.Runtime.dll"));
            }
            finally
            {
                DeleteIfPresent(packagePath);
                Directory.Delete(bundle, true);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        private static string CreateUnityPackage(IReadOnlyDictionary<string, byte[]> entries)
        {
            string packagePath = Path.Combine(Path.GetTempPath(), $"yucp-exporter-test-{Guid.NewGuid():N}.unitypackage");
            using var fileStream = File.Create(packagePath);
            using var gzipStream = new GZipStream(fileStream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: false);
            foreach (var entry in entries)
            {
                WriteTarEntry(gzipStream, entry.Key.Replace('\\', '/'), entry.Value ?? Array.Empty<byte>());
            }

            gzipStream.Write(new byte[1024], 0, 1024);
            return packagePath;
        }

        private static string[] ReadPackagePathnames(string packagePath)
        {
            var results = new List<string>();
            using var fileStream = File.OpenRead(packagePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: false);

            while (TryReadTarEntry(gzipStream, out string entryName, out byte[] data))
            {
                if (entryName.EndsWith("/pathname", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(Encoding.UTF8.GetString(data).Trim());
                }
            }

            return results.ToArray();
        }

        private static string ReadPackagedAssetByPathname(string packagePath, string targetPathname)
        {
            using var fileStream = File.OpenRead(packagePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: false);
            var pendingPathnames = new Dictionary<string, string>(StringComparer.Ordinal);
            var pendingAssets = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            while (TryReadTarEntry(gzipStream, out string entryName, out byte[] data))
            {
                string folder = Path.GetDirectoryName(entryName)?.Replace('\\', '/') ?? string.Empty;
                if (entryName.EndsWith("/pathname", StringComparison.OrdinalIgnoreCase))
                {
                    string pathname = Encoding.UTF8.GetString(data).Trim();
                    if (string.Equals(pathname, targetPathname, StringComparison.Ordinal))
                    {
                        if (pendingAssets.TryGetValue(folder, out byte[] assetBytes))
                        {
                            return Encoding.UTF8.GetString(assetBytes);
                        }

                        pendingPathnames[folder] = pathname;
                    }

                    continue;
                }

                if (!entryName.EndsWith("/asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (pendingPathnames.ContainsKey(folder))
                {
                    return Encoding.UTF8.GetString(data);
                }

                pendingAssets[folder] = data;
            }

            Assert.Fail($"Could not find packaged asset for pathname '{targetPathname}'.");
            return null;
        }

        private static bool TryReadTarEntry(Stream stream, out string entryName, out byte[] data)
        {
            entryName = null;
            data = null;

            byte[] header = new byte[512];
            int offset = 0;
            while (offset < header.Length)
            {
                int read = stream.Read(header, offset, header.Length - offset);
                if (read <= 0)
                {
                    return false;
                }

                offset += read;
            }

            if (header.All(b => b == 0))
            {
                return false;
            }

            entryName = ReadNullTerminatedAscii(header, 0, 100);
            long size = ReadOctal(header, 124, 12);
            data = new byte[size];

            int totalRead = 0;
            while (totalRead < size)
            {
                int read = stream.Read(data, totalRead, (int)size - totalRead);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Unexpected end of tar entry.");
                }

                totalRead += read;
            }

            int padding = (int)((512 - (size % 512)) % 512);
            if (padding > 0)
            {
                byte[] paddingBytes = new byte[padding];
                int skipped = 0;
                while (skipped < padding)
                {
                    int read = stream.Read(paddingBytes, skipped, padding - skipped);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException("Unexpected end of tar padding.");
                    }

                    skipped += read;
                }
            }

            return true;
        }

        private static string ReadNullTerminatedAscii(byte[] buffer, int offset, int length)
        {
            int count = 0;
            while (count < length && buffer[offset + count] != 0)
            {
                count++;
            }

            return Encoding.ASCII.GetString(buffer, offset, count).Trim();
        }

        private static long ReadOctal(byte[] buffer, int offset, int length)
        {
            string octal = ReadNullTerminatedAscii(buffer, offset, length).Trim();
            if (string.IsNullOrWhiteSpace(octal))
            {
                return 0;
            }

            return Convert.ToInt64(octal, 8);
        }

        private static void WriteTarEntry(Stream stream, string entryName, byte[] data)
        {
            byte[] header = new byte[512];
            WriteAscii(header, 0, 100, entryName);
            WriteOctal(header, 100, 8, 0644);
            WriteOctal(header, 108, 8, 0);
            WriteOctal(header, 116, 8, 0);
            WriteOctal(header, 124, 12, data.LongLength);
            WriteOctal(header, 136, 12, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            for (int i = 148; i < 156; i++)
            {
                header[i] = 0x20;
            }
            header[156] = (byte)'0';
            WriteAscii(header, 257, 6, "ustar");
            WriteAscii(header, 263, 2, "00");

            int checksum = 0;
            for (int i = 0; i < header.Length; i++)
            {
                checksum += header[i];
            }

            string checksumText = Convert.ToString(checksum, 8).PadLeft(6, '0');
            WriteAscii(header, 148, 6, checksumText);
            header[154] = 0;
            header[155] = 0x20;

            stream.Write(header, 0, header.Length);
            if (data.Length > 0)
            {
                stream.Write(data, 0, data.Length);
            }

            int padding = (int)((512 - (data.Length % 512)) % 512);
            if (padding > 0)
            {
                stream.Write(new byte[padding], 0, padding);
            }
        }

        private static void WriteAscii(byte[] buffer, int offset, int length, string value)
        {
            string normalized = value ?? string.Empty;
            byte[] bytes = Encoding.ASCII.GetBytes(normalized);
            Array.Copy(bytes, 0, buffer, offset, Math.Min(length, bytes.Length));
        }

        private static void WriteOctal(byte[] buffer, int offset, int length, long value)
        {
            string octal = Convert.ToString(value, 8);
            string padded = octal.PadLeft(length - 1, '0');
            WriteAscii(buffer, offset, length - 1, padded);
            buffer[offset + length - 1] = 0;
        }

        private static void DeleteIfPresent(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
