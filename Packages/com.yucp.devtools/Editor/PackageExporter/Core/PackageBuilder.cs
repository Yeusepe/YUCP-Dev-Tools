using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
#endif
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Orchestrates the complete package export process including obfuscation and icon injection.
    /// Handles validation, folder filtering, DLL obfuscation, and final package creation.
    /// </summary>
    public static class PackageBuilder
    {
        internal static bool s_isExporting = false;

        private const string DefaultGridPlaceholderPath = "Packages/com.yucp.devtools/Resources/DefaultGrid.png";
        private const string InstalledPackagesRoot = "Packages/yucp.installed-packages";
        private const string TempPackageRoot = "Packages/com.yucp.temp";
        private const string EmbeddedArtifactsFolderName = "Embedded";
        private const string TempInstallFolderName = "_temp";
        private const string TempPatchExportMarkerKey = "YUCP.PackageBuilder.ExportingTempPatchAssets";
        private const string PrecompiledInstallerRuntimePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Binaries/YUCP.DirectVpmInstaller.Runtime.dll";
        private const string PrecompiledInstallerRuntimeTargetFileName = "YUCP.DirectVpmInstaller.Runtime.dll";

        private static bool IsDefaultGridPlaceholder(Texture2D texture)
        {
            if (texture == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(texture);
            return assetPath == DefaultGridPlaceholderPath;
        }

        public class ExportResult
        {
            public bool success;
            public string errorMessage;
            public string outputPath;
            public float buildTimeSeconds;
            public int filesExported;
            public int assembliesObfuscated;
        }

        private sealed class EmbeddedAsset
        {
            public string sourcePath;
            public string unityPath;
        }

        private sealed class PackageEmbedContext
        {
            public readonly string SafePackageName;
            public readonly bool UsesInstalledPackageWorkspace;
            public readonly string EmbeddedRoot;
            public readonly string TempInstallRoot;
            public readonly string InstallerRoot;
            public readonly List<EmbeddedAsset> Assets = new List<EmbeddedAsset>();
            public readonly HashSet<string> MetadataFallbackAssetPaths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly ICollection<string> temporaryFiles;

            public string IconPath;
            public string BannerPath;
            public readonly Dictionary<ProductLink, string> ProductLinkIconPaths = new Dictionary<ProductLink, string>();
            public readonly List<string> GalleryImagePaths = new List<string>();

            public PackageEmbedContext(ExportProfile profile, ICollection<string> temporaryFiles = null)
            {
                this.temporaryFiles = temporaryFiles;
                string baseName = profile != null ? (profile.packageName ?? profile.profileName ?? profile.name) : "package";
                SafePackageName = MakeSafeFolderName(baseName);
                UsesInstalledPackageWorkspace = ShouldEmbedOptionalYucpMetadata(profile);
                EmbeddedRoot = $"{InstalledPackagesRoot}/{SafePackageName}/{EmbeddedArtifactsFolderName}";
                TempInstallRoot = $"{(UsesInstalledPackageWorkspace ? InstalledPackagesRoot : TempPackageRoot)}/{SafePackageName}/{TempInstallFolderName}";
                InstallerRoot = UsesInstalledPackageWorkspace ? $"{InstalledPackagesRoot}/Editor" : PatchImportPackageInjector.TempPatchEditorRoot;
            }

            public string RegisterTextureForExport(Texture2D texture, string baseName, string subfolder, out string exportAssetPath)
            {
                exportAssetPath = null;
                if (texture == null) return null;




                string cachePath = SaveTextureToLibraryCache(texture, baseName, temporaryFiles);
                if (!string.IsNullOrEmpty(cachePath))
                {
                    return RegisterEmbeddedFile(cachePath, subfolder, baseName);
                }

                string assetPath = AssetDatabase.GetAssetPath(texture);
                if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    exportAssetPath = GetRelativePackagePath(assetPath);
                    if (!string.IsNullOrEmpty(exportAssetPath))
                    {
                        MetadataFallbackAssetPaths.Add(exportAssetPath);
                    }
                    return assetPath;
                }

                return null;
            }

            public string RegisterEmbeddedFile(string sourcePath, string subfolder, string baseName)
            {
                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                    return null;

                string ext = Path.GetExtension(sourcePath);
                if (string.IsNullOrEmpty(ext))
                    ext = ".bin";

                string fileName = $"{SanitizeFileName(baseName)}_{Guid.NewGuid():N}{ext}";
                string unityPath = $"{EmbeddedRoot}/{subfolder}/{fileName}";
                Assets.Add(new EmbeddedAsset { sourcePath = sourcePath, unityPath = unityPath });
                return unityPath;
            }
        }

        private static bool TryInjectInstallerPreflightBootstrap(string tempExtractDir, string installerRoot, string installerScriptPath)
        {
            if (string.IsNullOrEmpty(installerScriptPath) || !File.Exists(installerScriptPath))
            {
                Debug.LogWarning("[PackageBuilder] Could not find DirectVpmInstaller.cs template for InstallerPreflight bootstrap injection");
                return false;
            }

            string installerDir = Path.GetDirectoryName(installerScriptPath);
            if (string.IsNullOrEmpty(installerDir))
            {
                Debug.LogWarning("[PackageBuilder] Could not resolve the DirectVpmInstaller template directory for InstallerPreflight bootstrap injection");
                return false;
            }

            string preflightTemplatePath = Path.Combine(installerDir, "InstallerPreflight.cs");
            if (!File.Exists(preflightTemplatePath))
            {
                Debug.LogWarning("[PackageBuilder] Could not find InstallerPreflight.cs template");
                return false;
            }

            string preflightGuid = Guid.NewGuid().ToString("N");
            string preflightFolder = Path.Combine(tempExtractDir, preflightGuid);
            Directory.CreateDirectory(preflightFolder);

            string preflightFileName = $"YUCP_InstallerPreflight_{preflightGuid}.cs";
            string preflightClassName = $"InstallerPreflight_{preflightGuid}";
            string preflightContent = File.ReadAllText(preflightTemplatePath)
                .Replace("__YUCP_PREFLIGHT_CLASS__", preflightClassName)
                .Replace("__YUCP_PREFLIGHT_FILE__", preflightFileName);

            File.WriteAllText(Path.Combine(preflightFolder, "asset"), preflightContent);
            File.WriteAllText(Path.Combine(preflightFolder, "pathname"), $"{installerRoot}/{preflightFileName}");

            string preflightMeta = "fileFormatVersion: 2\nguid: " + preflightGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            File.WriteAllText(Path.Combine(preflightFolder, "asset.meta"), preflightMeta);
            return true;
        }

        private static bool TryInjectPrecompiledInstallerRuntime(string tempExtractDir, string installerRoot)
        {
            return UnityPackageStagingWriter.TryInjectPrecompiledEditorBinary(
                tempExtractDir,
                PrecompiledInstallerRuntimePath,
                $"{installerRoot}/{PrecompiledInstallerRuntimeTargetFileName}",
                "yucp-precompiled-installer-runtime");
        }

        private static string DeferImportedCode(string assetPath)
        {
            switch (Path.GetExtension(assetPath).ToLowerInvariant())
            {
                case ".cs":
                case ".dll":
                case ".asmdef":
                case ".asmref":
                    return assetPath + ".yucp_disabled";
                default:
                    return assetPath;
            }
        }

        private static bool TryGetPatchRuntimeScriptReference(out string guid, out long localFileId)
        {
            guid = null;
            localFileId = 0;

            Type derivedFbxAssetType = Type.GetType("YUCP.PatchRuntime.DerivedFbxAsset, YUCP.PatchRuntime");
            if (derivedFbxAssetType == null || !typeof(ScriptableObject).IsAssignableFrom(derivedFbxAssetType))
            {
                return false;
            }

            ScriptableObject instance = null;
            try
            {
                instance = ScriptableObject.CreateInstance(derivedFbxAssetType);
                var script = MonoScript.FromScriptableObject(instance);
                return script != null &&
                       AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out guid, out localFileId);
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        private static bool ShouldEmbedOptionalYucpMetadata(ExportProfile profile)
        {
            return profile == null || profile.embedYucpMetadata;
        }

        private static bool ShouldAssignPackageIdForExport(ExportProfile profile)
        {
            return profile != null &&
                   (profile.publishReleaseAfterExport || ShouldEmbedOptionalYucpMetadata(profile));
        }

        /// <summary>
        /// Export a package using the provided profile
        /// </summary>
        public static ExportResult ExportPackage(ExportProfile profile, Action<float, string> progressCallback = null)
        {
            var result = new ExportResult();
            var startTime = DateTime.Now;
            var temporaryFiles = new List<string>();


            s_isExporting = true;
            EditorPrefs.SetBool(TempPatchExportMarkerKey, true);

            try
            {
                Debug.Log($"[PackageBuilder] Starting export for profile '{profile?.profileName ?? profile?.name ?? "<unnamed>"}' package='{profile?.packageName ?? "<missing>"}' version='{profile?.version ?? "<missing>"}'.");
                var originalProgressCallback = progressCallback;
                progressCallback = (progress, status) =>
                {
                    if (!string.IsNullOrEmpty(status))
                        Debug.Log($"[PackageBuilder] Export progress {progress:P0}: {status}");
                    originalProgressCallback?.Invoke(progress, status);
                };


                progressCallback?.Invoke(0.01f, "Saving all project assets...");
                AssetDatabase.SaveAssets();


                progressCallback?.Invoke(0.05f, "Validating export profile...");
                if (!profile.Validate(out string errorMessage))
                {
                    result.success = false;
                    result.errorMessage = errorMessage;
                    return result;
                }


                List<ExportProfile> includedProfiles = null;
                Dictionary<string, string> assetSourceMap = null;

                if (profile.HasIncludedProfiles())
                {
                    progressCallback?.Invoke(0.06f, "Resolving bundled profiles...");

                    List<string> cycles;
                    bool resolved = CompositeProfileResolver.ResolveIncludedProfiles(
                        profile, out includedProfiles, out cycles);

                    if (!resolved || cycles.Count > 0)
                    {
                        result.success = false;
                        result.errorMessage = $"Cycle detected in bundled profiles: {string.Join("; ", cycles)}";
                        return result;
                    }

                    if (includedProfiles == null)
                        includedProfiles = new List<ExportProfile>();


                    if (profile.alsoExportIncludedSeparately && includedProfiles.Count > 0)
                    {
                        progressCallback?.Invoke(0.07f, $"Exporting {includedProfiles.Count} bundled profiles separately...");

                        for (int i = 0; i < includedProfiles.Count; i++)
                        {
                            var bundledProfile = includedProfiles[i];
                            if (bundledProfile == null)
                                continue;

                            float bundledProgress = 0.07f + (i / (float)includedProfiles.Count) * 0.01f;
                            progressCallback?.Invoke(bundledProgress, $"Exporting bundled profile: {bundledProfile.packageName}...");


                            var bundledResult = ExportPackage(bundledProfile, (p, s) =>
                            {

                                float scaledProgress = bundledProgress + (p * 0.01f / includedProfiles.Count);
                                progressCallback?.Invoke(scaledProgress, $"[{i + 1}/{includedProfiles.Count}] {s}");
                            });

                            if (!bundledResult.success)
                            {
                                Debug.LogWarning($"[PackageBuilder] Failed to export bundled profile '{bundledProfile.packageName}': {bundledResult.errorMessage}");
                            }
                        }
                    }

                    progressCallback?.Invoke(0.08f, $"Merging assets from {includedProfiles.Count} bundled profiles...");
                }


                if (profile.enableObfuscation)
                {
                    if (!ConfuserExManager.IsInstalled())
                    {
                        progressCallback?.Invoke(0.1f, "ConfuserEx not found - downloading...");
                    }
                    else
                    {
                        progressCallback?.Invoke(0.1f, "ConfuserEx ready");
                    }

                    if (!ConfuserExManager.EnsureInstalled((progress, status) =>
                    {
                        progressCallback?.Invoke(0.1f + progress * 0.1f, status);
                    }))
                    {
                        result.success = false;
                        result.errorMessage = "Failed to install ConfuserEx";
                        return result;
                    }

                    progressCallback?.Invoke(0.2f, "Obfuscating assemblies...");


                    if (!ConfuserExManager.ObfuscateAssemblies(
                        profile.assembliesToObfuscate,
                        profile.obfuscationPreset,
                        (progress, status) =>
                        {
                            progressCallback?.Invoke(0.2f + progress * 0.3f, status);
                        },
                        profile.advancedObfuscationSettings))
                    {
                        result.success = false;
                        result.errorMessage = "Assembly obfuscation failed";
                        return result;
                    }

                    result.assembliesObfuscated = profile.assembliesToObfuscate.Count(a => a.enabled);
                }


                List<string> assetsToExport;

                if (profile.HasIncludedProfiles() && includedProfiles != null && includedProfiles.Count > 0)
                {

                    progressCallback?.Invoke(0.5f, $"Merging assets from bundled profiles...");
                    assetsToExport = CompositeProfileResolver.MergeAssetLists(
                        profile, includedProfiles, out assetSourceMap);

                    if (assetSourceMap == null)
                        assetSourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    progressCallback?.Invoke(0.52f, $"Merged {assetsToExport.Count} assets from {includedProfiles.Count} bundled profiles");
                }
                else
                {

                    progressCallback?.Invoke(0.5f, $"Collecting assets from {profile.foldersToExport.Count} folders...");
                    assetsToExport = CollectAssetsToExport(profile);
                }



                bool hasPatchAssets = false;


                if (profile.assembliesToObfuscate != null && profile.assembliesToObfuscate.Count > 0)
                {
                    var obfuscatedAsmdefPaths = profile.assembliesToObfuscate
                        .Where(a => a.enabled)
                        .Select(a => Path.GetFullPath(a.asmdefPath).Replace("\\", "/"))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    assetsToExport = assetsToExport.Where(assetPath => {
                        string fullPath = Path.GetFullPath(assetPath).Replace("\\", "/");
                        string extension = Path.GetExtension(fullPath).ToLower();


                        if (extension == ".cs" || extension == ".asmdef")
                        {
                            string fileDir = Path.GetDirectoryName(fullPath).Replace("\\", "/");
                            foreach (var asmdefPath in obfuscatedAsmdefPaths)
                            {
                                string asmdefDir = Path.GetDirectoryName(asmdefPath).Replace("\\", "/");
                                if (fileDir.StartsWith(asmdefDir, StringComparison.OrdinalIgnoreCase))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }).ToList();
                }

                if (assetsToExport.Count == 0)
                {
                    result.success = false;
                    result.errorMessage = "No assets found to export";
                    Debug.LogWarning($"[PackageBuilder] Export stopped: {result.errorMessage}. Profile '{profile?.profileName ?? profile?.name ?? "<unnamed>"}' has {profile?.foldersToExport?.Count ?? 0} folder(s) configured.");
                    return result;
                }

                progressCallback?.Invoke(0.52f, $"Found {assetsToExport.Count} assets in export folders (excluded obfuscated assemblies)");

                int excludedCount = assetsToExport.RemoveAll(assetPath =>
                {
                    string fullPath = Path.GetFullPath(assetPath);
                    if (ShouldExcludeAsset(assetPath, profile))
                    {
                        Debug.LogWarning($"[PackageBuilder] Force-excluding asset in excluded folder: {assetPath}");
                        return true;
                    }
                    return false;
                });

                if (excludedCount > 0)
                {
                    Debug.Log($"[PackageBuilder] Force-excluded {excludedCount} assets that were in excluded folders");
                }


                CollectFilteredDependencies(assetsToExport, profile, progressCallback);



                hasPatchAssets = ConvertDerivedFbxToPatchAssets(assetsToExport, progressCallback, progress: 0.535f, profile: profile);

                progressCallback?.Invoke(0.54f, $"Total assets after dependency collection: {assetsToExport.Count}");

                if (profile.publishReleaseAfterExport &&
                    string.IsNullOrWhiteSpace(profile.packageId))
                {
                    PackageIdManager.AssignPackageId(profile);
                }


                var bundledPackagePaths = new Dictionary<string, string>();
                var bundledDeps = profile.dependencies.Where(d => d.enabled && d.exportMode == DependencyExportMode.Bundle).ToList();
                if (bundledDeps.Count > 0)
                {
                    progressCallback?.Invoke(0.55f, $"Preparing to bundle {bundledDeps.Count} dependencies...");

                    foreach (var dep in bundledDeps)
                    {
                        var depPackageInfo = DependencyScanner.ScanInstalledPackages()
                            .FirstOrDefault(p => p.packageName == dep.packageName);

                        if (depPackageInfo != null && Directory.Exists(depPackageInfo.packagePath))
                        {
                            bundledPackagePaths[dep.packageName] = depPackageInfo.packagePath;
                        }
                        else
                        {
                            Debug.LogWarning($"[PackageBuilder] Bundled package not found: {dep.packageName}");
                        }
                    }
                }


                 string packageJsonContent = null;
                 if (profile.generatePackageJson)
                 {
                     progressCallback?.Invoke(0.58f, "Generating package.json...");

                     packageJsonContent = DependencyScanner.GeneratePackageJson(
                          profile,
                          profile.dependencies.Where(d => d != null && !string.Equals(d.packageName, "com.yucp.importer", StringComparison.OrdinalIgnoreCase)).ToList(),
                          null
                      );
                  }

                bool shouldEmbedYucpMetadata = ShouldEmbedOptionalYucpMetadata(profile);
                var embedContext = new PackageEmbedContext(profile, temporaryFiles);
                if (shouldEmbedYucpMetadata && profile.icon != null && !IsDefaultGridPlaceholder(profile.icon))
                {
                    string exportAssetPath;
                    string iconPath = embedContext.RegisterTextureForExport(profile.icon, profile.packageName ?? "PackageIcon", "Icons", out exportAssetPath);
                    embedContext.IconPath = iconPath;
                    if (!string.IsNullOrEmpty(exportAssetPath) && !assetsToExport.Contains(exportAssetPath))
                    {
                        assetsToExport.Add(exportAssetPath);
                        Debug.Log($"[PackageBuilder] Added icon texture to export: {exportAssetPath}");
                    }
                }

                if (shouldEmbedYucpMetadata && profile.banner != null)
                {
                    string exportAssetPath;
                    string bannerPath = embedContext.RegisterTextureForExport(profile.banner, profile.packageName ?? "PackageBanner", "Banners", out exportAssetPath);
                    embedContext.BannerPath = bannerPath;
                    if (!string.IsNullOrEmpty(exportAssetPath) && !assetsToExport.Contains(exportAssetPath))
                    {
                        assetsToExport.Add(exportAssetPath);
                        Debug.Log($"[PackageBuilder] Added banner texture to export: {exportAssetPath}");
                    }
                }

                if (shouldEmbedYucpMetadata && profile.productLinks != null)
                {
                    foreach (var link in profile.productLinks)
                    {
                        Texture2D iconToAdd = link.customIcon;
                        string exportAssetPath = null;
                        string linkIconPath = null;

                        if (iconToAdd != null)
                        {
                            linkIconPath = embedContext.RegisterTextureForExport(
                                iconToAdd,
                                link.label ?? "ProductLink",
                                "ProductLinks",
                                out exportAssetPath
                            );
                        }
                        if (!string.IsNullOrEmpty(exportAssetPath) && !assetsToExport.Contains(exportAssetPath))
                        {
                            assetsToExport.Add(exportAssetPath);
                            Debug.Log($"[PackageBuilder] Added product link icon to export: {exportAssetPath}");
                        }

                        if (!string.IsNullOrEmpty(linkIconPath))
                        {
                            embedContext.ProductLinkIconPaths[link] = linkIconPath;
                        }
                    }
                }

                if (shouldEmbedYucpMetadata && profile.galleryImages != null)
                {
                    for (int i = 0; i < profile.galleryImages.Count && i < 8; i++)
                    {
                        var galleryImg = profile.galleryImages[i];
                        if (galleryImg == null) continue;
                        string exportAssetPath;
                        string galleryPath = embedContext.RegisterTextureForExport(
                            galleryImg,
                            $"Gallery_{i}",
                            "Gallery",
                            out exportAssetPath
                        );
                        if (!string.IsNullOrEmpty(exportAssetPath) && !assetsToExport.Contains(exportAssetPath))
                        {
                            assetsToExport.Add(exportAssetPath);
                        }
                        if (!string.IsNullOrEmpty(galleryPath))
                        {
                            embedContext.GalleryImagePaths.Add(galleryPath);
                        }
                    }
                }

                result.filesExported = assetsToExport.Count;

                progressCallback?.Invoke(0.6f, "Exporting Unity package...");

                string tempPackagePath = ExportTempCleanup.CreatePath("YUCP_Temp_", ".unitypackage");
                temporaryFiles.Add(tempPackagePath);

                 ExportPackageOptions options = ExportPackageOptions.Default;
                 if (profile.recurseFolders)
                     options |= ExportPackageOptions.Recurse;


                 progressCallback?.Invoke(0.61f, $"Validating {assetsToExport.Count} assets...");

                 var validAssets = new List<string>();
                 int validateCount = 0;
                 int totalToValidate = assetsToExport.Count;
                 foreach (string asset in assetsToExport)
                 {
                     validateCount++;
                     if (validateCount % 50 == 0 || validateCount == totalToValidate)
                     {
                         float t = totalToValidate > 0 ? (float)validateCount / totalToValidate : 1f;
                         progressCallback?.Invoke(0.61f + 0.02f * t, $"Validating assets... ({validateCount}/{totalToValidate})");
                     }

                     string unityPath = GetRelativePackagePath(asset);


                     var loadedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityPath);
                     if (loadedAsset != null)
                     {
                         validAssets.Add(unityPath);
                     }
                     else
                     {


                         if (unityPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                         {
                             string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                             string physicalPath = Path.Combine(projectPath, unityPath.Replace('/', Path.DirectorySeparatorChar));
                             if (File.Exists(physicalPath) || Directory.Exists(physicalPath))
                             {


                                 if (unityPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                                     unityPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                     unityPath.EndsWith(".hdiff", StringComparison.OrdinalIgnoreCase) ||
                                     unityPath.EndsWith(".hdiff.enc", StringComparison.OrdinalIgnoreCase))
                                 {

                                     validAssets.Add(unityPath);
                                 }
                                 else
                                    {

                                        progressCallback?.Invoke(
                                            0.61f + 0.02f * (totalToValidate > 0 ? (float)validateCount / totalToValidate : 1f),
                                            BuildUnityImportOverlayStatus($"Importing asset for validation: {Path.GetFileName(unityPath)}"));
                                        AssetDatabase.ImportAsset(unityPath, ImportAssetOptions.ForceSynchronousImport);
                                        AssetDatabase.Refresh();


                                     loadedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityPath);
                                     if (loadedAsset != null)
                                     {
                                         validAssets.Add(unityPath);
                                     }
                                     else
                                     {
                                         Debug.LogWarning($"[PackageBuilder] Could not load asset (file exists but Unity doesn't recognize it): {unityPath}");
                                     }
                                 }
                             }
                             else
                             {
                                 Debug.LogWarning($"[PackageBuilder] Could not load asset (file does not exist): {unityPath}");
                             }
                         }
                         else
                         {
                             Debug.LogWarning($"[PackageBuilder] Could not load asset: {unityPath}");
                         }
                     }
                 }

                if (validAssets.Count == 0 && bundledPackagePaths.Count == 0)
                {
                    throw new InvalidOperationException("No valid assets found to export. Check that the specified folders contain valid Unity assets.");
                }

                progressCallback?.Invoke(0.63f, $"Validated {validAssets.Count} assets from export folders");

                if (hasPatchAssets)
                {
                    bool hasDerivedFbxAsset = validAssets.Any(p =>
                        p.StartsWith("Packages/com.yucp.temp/Patches/", StringComparison.OrdinalIgnoreCase) &&
                        p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase));
                    bool hasHdiff = validAssets.Any(p =>
                        p.StartsWith("Packages/com.yucp.temp/Patches/", StringComparison.OrdinalIgnoreCase) &&
                        p.EndsWith(".hdiff", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".hdiff.enc", StringComparison.OrdinalIgnoreCase));

                    if (!hasDerivedFbxAsset || !hasHdiff)
                    {
                        Debug.LogWarning("[PackageBuilder] Derived FBX export is enabled, but expected derived FBX artifacts were not validated for export. " +
                                         "This may result in derived FBXs not being recreated on import.");
                    }
                }

                 var finalValidAssets = validAssets;

                progressCallback?.Invoke(0.64f, "Performing final ignore list check...");
                int ignoredRemoved = finalValidAssets.RemoveAll(assetPath =>
                {
                    if (ShouldExcludeAsset(assetPath, profile))
                    {
                        Debug.LogWarning($"[PackageBuilder] Final filter: Removing asset in ignored folder: {assetPath}");
                        return true;
                    }
                    return false;
                });

                 if (ignoredRemoved > 0)
                 {
                     Debug.LogWarning($"[PackageBuilder] Final filter removed {ignoredRemoved} asset(s) that were in ignored folders");
                 }




                if (finalValidAssets.Count == 0)
                 {
                      throw new InvalidOperationException("No valid assets remain for export after final validation.");
                  }

                progressCallback?.Invoke(0.65f, $"Writing package file ({finalValidAssets.Count} assets)... may take a moment.");


                 try
                 {
                     if (finalValidAssets.Count > 0)
                     {
                         AssetDatabase.ExportPackage(
                             finalValidAssets.ToArray(),
                             tempPackagePath,
                             options
                         );
                     }
                     else
                     {
                         CreateEmptyUnityPackage(tempPackagePath);
                     }
                     progressCallback?.Invoke(0.68f, "Package file written; verifying...");
                 }
                 catch (Exception ex)
                 {
                     Debug.LogError($"[PackageBuilder] ExportPackage threw exception: {ex.Message}");
                     throw;
                 }


                 progressCallback?.Invoke(0.685f, BuildUnityImportOverlayStatus("Refreshing Unity after writing the package archive"));
                  AssetDatabase.Refresh();


                 int retryCount = 0;
                 while (!File.Exists(tempPackagePath) && retryCount < 30)
                 {
                     if (retryCount > 0)
                         progressCallback?.Invoke(0.69f, $"Verifying package file... (attempt {retryCount + 1}/30)");
                     System.Threading.Thread.Sleep(200);
                     retryCount++;
                 }

                 progressCallback?.Invoke(0.7f, "Unity package export completed");


                 if (!File.Exists(tempPackagePath))
                 {

                     string tempDir = Path.GetDirectoryName(tempPackagePath);
                     Debug.LogError($"[PackageBuilder] Temp directory: {tempDir}");
                     Debug.LogError($"[PackageBuilder] Temp directory exists: {Directory.Exists(tempDir)}");
                     Debug.LogError($"[PackageBuilder] Temp directory writable: {CheckDirectoryWritable(tempDir)}");

                     throw new FileNotFoundException($"Package export failed - temp file not created after retries: {tempPackagePath}");
                 }
                if (ShouldAssignPackageIdForExport(profile) && string.IsNullOrEmpty(profile.packageId))
                {
                    PackageIdManager.AssignPackageId(profile);
                }


                string packageMetadataJson = shouldEmbedYucpMetadata
                    ? GeneratePackageMetadataJson(profile, embedContext, validAssets)
                    : null;


                if (!string.IsNullOrEmpty(packageJsonContent) || bundledPackagePaths.Count > 0 || !string.IsNullOrEmpty(packageMetadataJson))
                {
                    progressCallback?.Invoke(0.75f, "Injecting package.json, installer, bundled packages, and metadata...");

                    try
                    {

                        var obfuscatedAssemblies = profile.enableObfuscation
                            ? profile.assembliesToObfuscate.Where(a => a.enabled).ToList()
                            : new List<AssemblyObfuscationSettings>();

                        InjectPackageJsonInstallerAndBundles(
                            tempPackagePath,
                            packageJsonContent,
                            bundledPackagePaths,
                            obfuscatedAssemblies,
                            profile,
                            hasPatchAssets,
                            packageMetadataJson,
                            embedContext,
                            progressCallback);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PackageBuilder] Failed to inject package metadata/installer/bundles into '{tempPackagePath}'. Export will stop to avoid producing a package that imports with unresolved .yucp_disabled files.\n{ex}");
                        throw new InvalidOperationException(
                            "Failed to inject YUCP installer/metadata into the package archive. See the Unity Console for the exact archive path and exception details.",
                            ex);
                    }
                }





                string finalOutputPath = profile.GetOutputFilePath();


                string outputDir = Path.GetDirectoryName(finalOutputPath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }


                string contentPackagePath = tempPackagePath;

                if (profile.icon != null && !IsDefaultGridPlaceholder(profile.icon))
                {
                    progressCallback?.Invoke(0.8f, "Adding package icon...");

                    string iconSourcePath = ResolvePackageIconSourcePath(profile.icon, profile.packageName ?? "PackageIcon", temporaryFiles);
                    if (!string.IsNullOrEmpty(iconSourcePath))
                    {

                        string iconTempPath = ExportTempCleanup.CreatePath("YUCP_ContentWithIcon_", ".unitypackage");
                        temporaryFiles.Add(iconTempPath);

                        if (PackageIconInjector.AddIconToPackage(tempPackagePath, iconSourcePath, iconTempPath))
                        {
                            contentPackagePath = iconTempPath;
                        }
                        else
                        {
                            Debug.LogWarning("[PackageBuilder] Failed to add icon, using package without icon");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[PackageBuilder] Could not resolve a PNG source for the package icon");
                    }
                }

                progressCallback?.Invoke(0.86f, "Copying package to output location...");
                File.Copy(contentPackagePath, finalOutputPath, true);

                progressCallback?.Invoke(0.9f, "Cleaning up temporary files...");


                string tempEditorPath = "Packages/com.yucp.temp/Editor";
                if (AssetDatabase.IsValidFolder(tempEditorPath))
                {
                    try
                    {
                        AssetDatabase.DeleteAsset(tempEditorPath);
                        Debug.Log("[PackageBuilder] Cleaned up temp Editor folder");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageBuilder] Error cleaning up temp Editor folder: {ex.Message}");

                        try
                        {
                            string physicalEditorPath = Path.Combine(Application.dataPath, "..", "Packages", "com.yucp.temp", "Editor");
                            if (Directory.Exists(physicalEditorPath))
                            {
                                Directory.Delete(physicalEditorPath, true);
                            }
                        }
                        catch (Exception ex2)
                        {
                            Debug.LogWarning($"[PackageBuilder] Fallback cleanup also failed: {ex2.Message}");
                        }
                    }
                }

                string tempPluginsPath = "Packages/com.yucp.temp/Plugins";
                if (AssetDatabase.IsValidFolder(tempPluginsPath))
                {
                    try
                    {
                        AssetDatabase.DeleteAsset(tempPluginsPath);
                        Debug.Log("[PackageBuilder] Cleaned up temp Plugins folder");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageBuilder] Error cleaning up temp Plugins folder: {ex.Message}");

                        try
                        {
                            string physicalPluginsPath = Path.Combine(Application.dataPath, "..", "Packages", "com.yucp.temp", "Plugins");
                            if (Directory.Exists(physicalPluginsPath))
                            {
                                Directory.Delete(physicalPluginsPath, true);
                            }
                        }
                        catch (Exception ex2)
                        {
                            Debug.LogWarning($"[PackageBuilder] Fallback cleanup also failed: {ex2.Message}");
                        }
                    }
                }


                progressCallback?.Invoke(0.93f, BuildUnityImportOverlayStatus("Refreshing Unity after cleaning temporary export artifacts"));
                AssetDatabase.Refresh();


                if (profile.enableObfuscation)
                {
                    progressCallback?.Invoke(0.95f, "Restoring original assemblies...");
                    ConfuserExManager.RestoreOriginalDlls(profile.assembliesToObfuscate);
                }

                progressCallback?.Invoke(0.98f, "Saving export statistics...");


                profile.RecordExport();
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();

                progressCallback?.Invoke(1.0f, "Export complete!");


                result.success = true;


                try
                {
                    TrustedMilestoneTracker.InvokeStatic("IncrementExportCount");
                }
                catch (System.Exception)
                {

                }
                result.outputPath = finalOutputPath;
                result.buildTimeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;
                Debug.Log($"[PackageBuilder] Export succeeded: '{finalOutputPath}' in {result.buildTimeSeconds:0.###}s. files={validAssets.Count}");

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Export failed for profile '{profile?.profileName ?? profile?.name ?? "<unnamed>"}': {ex.Message}");
                Debug.LogException(ex);


                if (profile.enableObfuscation)
                {
                    try
                    {
                        ConfuserExManager.RestoreOriginalDlls(profile.assembliesToObfuscate);
                    }
                    catch
                    {

                    }
                }

                 result.success = false;
                 result.errorMessage = ex.Message;
                 result.buildTimeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;

                 return result;
            }
            finally
            {
                ExportTempCleanup.Delete(temporaryFiles.ToArray());
                s_isExporting = false;
                EditorPrefs.DeleteKey(TempPatchExportMarkerKey);
            }
        }

        /// <summary>
        /// Replace any FBX assets marked as "derived" with a PatchPackage + sidecars generated via PatchBuilder.
        /// Stores settings in the FBX importer userData JSON.
        /// Returns true if any patch assets were created.
        /// </summary>
        private static bool ConvertDerivedFbxToPatchAssets(List<string> assetsToExport, Action<float, string> progressCallback, float progress, ExportProfile profile = null)
        {
            if (assetsToExport == null || assetsToExport.Count == 0) return false;

            try
            {
                string patchesPath = "Packages/com.yucp.temp/Patches";
                if (AssetDatabase.IsValidFolder(patchesPath))
                {
                    string[] allGuids = AssetDatabase.FindAssets("", new[] { patchesPath });
                    int cleanedCount = 0;
                    foreach (var guid in allGuids)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        string fileName = Path.GetFileNameWithoutExtension(assetPath);

                        if (assetPath.EndsWith(".asset") &&
                            (fileName.StartsWith("MeshDelta_") ||
                             fileName.StartsWith("UVLayer_") ||
                             fileName.StartsWith("Blendshape_") ||
                             fileName.StartsWith("PatchPackage_")))
                        {
                            AssetDatabase.DeleteAsset(assetPath);
                            cleanedCount++;
                        }
                    }
                    if (cleanedCount > 0)
                    {
                        AssetDatabase.Refresh();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PackageBuilder] Failed to clean up old sidecar assets: {ex.Message}");
            }


            var derivedFbxPaths = new List<string>();
            int fbxCount = 0;
            foreach (var assetPath in assetsToExport)
            {
                if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                fbxCount++;


                string normalizedPath = assetPath;
                if (Path.IsPathRooted(normalizedPath))
                {
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    if (normalizedPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedPath = normalizedPath.Substring(projectPath.Length).Replace('\\', '/').TrimStart('/');
                    }
                }

                var importer = AssetImporter.GetAtPath(normalizedPath) as ModelImporter;
                if (importer == null)
                {
                    continue;
                }

                try
                {
                    if (DerivedSettingsUtility.TryRead(importer, out var settings) && settings != null && settings.isDerived)
                    {
                        bool hasBase = settings.baseGuids != null && settings.baseGuids.Any(g => !string.IsNullOrEmpty(g));
                        if (hasBase)
                        {

                            derivedFbxPaths.Add(assetPath);
                        }
                        else
                        {
                            Debug.LogWarning($"[PackageBuilder] FBX {assetPath} is marked as derived but has no base FBX assigned. " +
                                $"Please add at least one base FBX in the ModelImporter inspector. This FBX will NOT be converted to a derived FBX package.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageBuilder] Failed to parse userData for {assetPath}: {ex.Message}");
                }
            }



            if (fbxCount > 0 && derivedFbxPaths.Count == 0)
            {
                Debug.LogWarning($"[PackageBuilder] Found {fbxCount} FBX(s) in export, but none are marked as 'derived'. " +
                    $"If any of these FBXs are modifications of a base FBX, mark them as 'Export as Derived FBX' in the ModelImporter inspector " +
                    $"and assign the base FBX. Otherwise, the full FBX will be exported instead of a derived FBX package.");
            }

            if (derivedFbxPaths.Count == 0) return false;

            progressCallback?.Invoke(progress, $"Converting {derivedFbxPaths.Count} derived FBX file(s) into derived FBX packages...");

            EnsureAuthoringFolder();

            var fbxToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var patchAssetsToAdd = new List<string>();
            int fbxIndex = 0;
            int fbxTotal = derivedFbxPaths.Count;

            foreach (var modifiedPath in derivedFbxPaths)
            {
                fbxIndex++;
                string fbxName = Path.GetFileName(modifiedPath);
                progressCallback?.Invoke(progress, $"Building derived FBX ({fbxIndex}/{fbxTotal}): {fbxName}");


                string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string normalizedModifiedPath = modifiedPath;
                if (Path.IsPathRooted(normalizedModifiedPath))
                {
                    if (normalizedModifiedPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedModifiedPath = normalizedModifiedPath.Substring(projectPath.Length).Replace('\\', '/').TrimStart('/');
                    }
                }

                var importer = AssetImporter.GetAtPath(normalizedModifiedPath) as ModelImporter;
                if (importer == null) continue;

                DerivedSettings settings = null;
                try { DerivedSettingsUtility.TryRead(importer, out settings); } catch {  }
                if (settings == null || !settings.isDerived) continue;


                if (settings.baseGuids == null || settings.baseGuids.Count == 0) continue;

                var policy = new DerivedFbxAsset.Policy();
                var hints = new DerivedFbxAsset.UIHints
                {
                    friendlyName = string.IsNullOrEmpty(settings.friendlyName)
                        ? System.IO.Path.GetFileNameWithoutExtension(modifiedPath)
                        : settings.friendlyName,
                    thumbnail = null,
                    category = settings.category
                };

                bool overrideOriginalReferences = settings.overrideOriginalReferences;

                var seeds = new DerivedFbxAsset.SeedMaps();


                string physicalModifiedPath = Path.IsPathRooted(normalizedModifiedPath)
                    ? normalizedModifiedPath
                    : Path.Combine(projectPath, normalizedModifiedPath.Replace('/', Path.DirectorySeparatorChar));
                string derivedFbxGuid = MetaFileManager.ReadGuid(physicalModifiedPath);

                if (string.IsNullOrEmpty(derivedFbxGuid))
                {
                    Debug.LogWarning($"[PackageBuilder] Could not read GUID from derived FBX .meta file: {normalizedModifiedPath}. A new GUID will be generated on import.");

                    derivedFbxGuid = System.Guid.NewGuid().ToString("N");
                }

                try
                {
                    EnsureAuthoringFolder();
                }
                catch (Exception folderEx)
                {
                    Debug.LogError($"[PackageBuilder] Failed to ensure authoring folder: {folderEx.Message}\n{folderEx.StackTrace}");
                    throw;
                }

                try
                {
                    var baseGuids = new List<string>();
                    var basePaths = new List<string>();
                    foreach (var baseGuid in settings.baseGuids)
                    {
                        if (string.IsNullOrEmpty(baseGuid)) continue;
                        var basePath = AssetDatabase.GUIDToAssetPath(baseGuid);
                        if (string.IsNullOrEmpty(basePath))
                        {
                            Debug.LogError($"[PackageBuilder] Derived FBX has no resolvable Base FBX: {modifiedPath} (GUID: {baseGuid})");
                            baseGuids.Clear();
                            basePaths.Clear();
                            break;
                        }

                        baseGuids.Add(baseGuid);
                        basePaths.Add(basePath);
                    }

                    if (baseGuids.Count == 0)
                    {
                        Debug.LogError($"[PackageBuilder] No valid Base FBX entries for {modifiedPath}. Skipping derived export.");
                        continue;
                    }

                    List<string> basePathFallbacks = BuildDerivedFbxBasePathFallbacks(settings, basePaths);
                    bool exportsBasePathFallbacks = basePathFallbacks.Any(path => !string.IsNullOrEmpty(path));
                    if (exportsBasePathFallbacks)
                    {
                        Debug.LogWarning(
                            $"[PackageBuilder] Derived FBX '{modifiedPath}' will export direct base path fallback(s). " +
                            "Import still uses GUIDs first, but if GUID lookup fails the buyer project must have the base FBX at the exported path. " +
                            "Moving or renaming that base FBX will prevent generation.");
                    }

                    bool builtEntries = PatchBuilder.CreateEncryptedPatchEntries(
                        basePaths,
                        baseGuids,
                        basePathFallbacks,
                        normalizedModifiedPath,
                        hints.friendlyName,
                        out _,
                        out List<DerivedFbxAsset.PatchEntry> entries);

                    if (!builtEntries)
                    {
                        Debug.LogError($"[PackageBuilder] Failed to create derived FBX asset for {modifiedPath}. The FBX will be exported as-is.");
                        continue;
                    }

                    var derivedAsset = ScriptableObject.CreateInstance<DerivedFbxAsset>();
                    derivedAsset.policy = policy;
                    derivedAsset.uiHints = hints;
                    derivedAsset.seedMaps = seeds ?? new DerivedFbxAsset.SeedMaps();
                    derivedAsset.targetFbxName = hints.friendlyName;
                    derivedAsset.entries = entries;
                    derivedAsset.canonicalBaseGuid = baseGuids.FirstOrDefault(g => !string.IsNullOrEmpty(g)) ?? string.Empty;

                    derivedAsset.derivedFbxGuid = derivedFbxGuid ?? string.Empty;
                    derivedAsset.originalDerivedFbxPath = normalizedModifiedPath;
                    derivedAsset.overrideOriginalReferences = overrideOriginalReferences;

                    PatchBuilder.EmbedMetaFile(normalizedModifiedPath, derivedAsset);


                    string fileName = $"DerivedFbxAsset_{derivedFbxGuid.Substring(0, 8)}_{SanitizeFileName(hints.friendlyName)}.asset";
                    string pkgPath = $"Packages/com.yucp.temp/Patches/{fileName}";


                    string physicalAssetPath = Path.Combine(projectPath, pkgPath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(physicalAssetPath))
                    {
                        File.Delete(physicalAssetPath);
                        if (File.Exists(physicalAssetPath + ".meta"))
                        {
                            File.Delete(physicalAssetPath + ".meta");
                        }
                        AssetDatabase.Refresh();
                    }

                    try
                    {
                        AssetDatabase.CreateAsset(derivedAsset, pkgPath);
                    }
                    catch (Exception createEx)
                    {
                        Debug.LogError($"[PackageBuilder] Failed to create DerivedFbxAsset at {pkgPath}: {createEx.Message}\n{createEx.StackTrace}");
                        UnityEngine.Object.DestroyImmediate(derivedAsset);
                        continue;
                    }

                    if (File.Exists(physicalAssetPath))
                    {
                        if (TryGetPatchRuntimeScriptReference(out string derivedFbxAssetScriptGuid, out long derivedFbxAssetScriptFileId))
                        {

                            string assetContent = File.ReadAllText(physicalAssetPath);

                            var guidPattern = new System.Text.RegularExpressions.Regex(@"m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([a-f0-9]{32}),\s*type:\s*\d+\}");
                            if (guidPattern.IsMatch(assetContent))
                            {
                                assetContent = guidPattern.Replace(assetContent, $"m_Script: {{fileID: {derivedFbxAssetScriptFileId}, guid: {derivedFbxAssetScriptGuid}, type: 3}}");
                            }







                            string originalContent = assetContent;


                            assetContent = assetContent.Replace(
                                "YUCP.DevTools.Editor.PackageExporter.DerivedFbxAsset/",
                                "YUCP.PatchRuntime.DerivedFbxAsset/"
                            );
                            assetContent = assetContent.Replace(
                                "YUCP.DevTools.Editor.PackageExporter",
                                "YUCP.PatchRuntime"
                            );

                            assetContent = assetContent.Replace(
                                "com.yucp.devtools.Editor",
                                "YUCP.PatchRuntime"
                            );
                            if (assetContent != originalContent)
                            {
                                Debug.Log($"[PackageBuilder] Fixed namespace references in DerivedFbxAsset at {pkgPath}");
                                File.WriteAllText(physicalAssetPath, assetContent);

                                string verifyContent = File.ReadAllText(physicalAssetPath);
                                if (verifyContent.Contains("YUCP.DevTools.Editor.PackageExporter"))
                                {
                                    Debug.LogWarning($"[PackageBuilder] Namespace fix may not have worked completely. Old namespace still found in {pkgPath}");

                                    verifyContent = verifyContent.Replace("YUCP.DevTools", "YUCP.PatchRuntime");
                                    File.WriteAllText(physicalAssetPath, verifyContent);
                                }


                                AssetDatabase.ImportAsset(pkgPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate | ImportAssetOptions.DontDownloadFromCacheServer);


                                AssetDatabase.Refresh();
                            }
                            else
                            {

                                File.WriteAllText(physicalAssetPath, assetContent);
                            }
                        }
                        else
                        {
                            Debug.LogWarning("[PackageBuilder] Could not resolve the YUCP.PatchRuntime.DerivedFbxAsset script reference - patch assets may not load correctly");
                        }

                        patchAssetsToAdd.Add(pkgPath);


                        if (derivedAsset.entries != null)
                        {
                            foreach (var entry in derivedAsset.entries)
                            {
                                if (entry == null || string.IsNullOrEmpty(entry.hdiffFilePath)) continue;
                                string hdiffPhysicalPath = Path.Combine(projectPath, entry.hdiffFilePath.Replace('/', Path.DirectorySeparatorChar));
                                if (File.Exists(hdiffPhysicalPath))
                                {
                                    patchAssetsToAdd.Add(entry.hdiffFilePath);
                                    Debug.Log($"[PackageBuilder] Added encrypted diff to export: {entry.hdiffFilePath}");
                                }
                                else
                                {
                                    Debug.LogWarning($"[PackageBuilder] Encrypted diff file not found at: {hdiffPhysicalPath}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[PackageBuilder] DerivedFbxAsset.asset was not created at {pkgPath}");
                    }


                    fbxToRemove.Add(modifiedPath);

                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PackageBuilder] Failed to build derived FBX for {modifiedPath}: {ex.Message}\n{ex.StackTrace}");
                    Debug.LogError($"[PackageBuilder] The FBX will be exported as-is instead of being converted to a derived FBX package. " +
                        $"Please check that the base FBX exists and both FBXs have compatible mesh structures.");

                }
            }


            if (fbxToRemove.Count > 0)
            {



                Func<string, string> normalizePath = (p) =>
                {
                    if (string.IsNullOrEmpty(p)) return p;
                    string normalized = p.Replace('\\', '/');
                    if (Path.IsPathRooted(normalized))
                    {
                        string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
                        if (normalized.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                        {
                            normalized = normalized.Substring(projectPath.Length).TrimStart('/');
                        }
                    }
                    return normalized;
                };

                var normalizedToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in fbxToRemove)
                {
                    string normalized = normalizePath(path);
                    normalizedToRemove.Add(normalized);
                }

                int removedCount = 0;
                var toRemove = new List<string>();
                foreach (var path in assetsToExport)
                {
                    string normalized = normalizePath(path);
                    if (normalizedToRemove.Contains(normalized))
                    {
                        toRemove.Add(path);
                    }
                }

                foreach (var path in toRemove)
                {
                    assetsToExport.Remove(path);
                    removedCount++;
                }
            }


            if (patchAssetsToAdd.Count > 0)
            {
                foreach (var patchAsset in patchAssetsToAdd)
                {
                    if (!assetsToExport.Contains(patchAsset))
                    {
                        assetsToExport.Add(patchAsset);
                    }
                }
                return patchAssetsToAdd.Count > 0;
            }

            return false;
        }

        private static List<string> BuildDerivedFbxBasePathFallbacks(DerivedSettings settings, List<string> resolvedBasePaths)
        {
            var fallbacks = new List<string>();
            if (resolvedBasePaths == null)
            {
                return fallbacks;
            }

            for (int i = 0; i < resolvedBasePaths.Count; i++)
            {
                fallbacks.Add(string.Empty);
            }

            if (settings == null || !settings.useBasePathFallback)
            {
                return fallbacks;
            }

            for (int i = 0; i < resolvedBasePaths.Count; i++)
            {
                string configuredPath = settings.basePathFallbacks != null && i < settings.basePathFallbacks.Count
                    ? settings.basePathFallbacks[i]
                    : null;

                string fallbackPath = NormalizeProjectRelativeAssetPath(configuredPath);
                if (string.IsNullOrEmpty(fallbackPath))
                {
                    fallbackPath = NormalizeProjectRelativeAssetPath(resolvedBasePaths[i]);
                }

                fallbacks[i] = fallbackPath;
            }

            return fallbacks;
        }

        private static string NormalizeProjectRelativeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = path.Trim().Replace('\\', '/');
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');

            if (Path.IsPathRooted(normalized))
            {
                string rooted = Path.GetFullPath(normalized).Replace('\\', '/');
                if (!rooted.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                normalized = rooted.Substring(projectRoot.Length).TrimStart('/');
            }

            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return normalized;
        }



        private static void EnsureAuthoringFolder()
        {
            string tempPackagePath = "Packages/com.yucp.temp";
            string patchesPath = $"{tempPackagePath}/Patches";

            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string tempPackagePhysicalPath = Path.Combine(projectPath, tempPackagePath.Replace('/', Path.DirectorySeparatorChar));
            string patchesPhysicalPath = Path.Combine(projectPath, patchesPath.Replace('/', Path.DirectorySeparatorChar));

            bool createdPackage = false;
            bool createdPatches = false;

            if (!Directory.Exists(tempPackagePhysicalPath))
            {
                Directory.CreateDirectory(tempPackagePhysicalPath);
                createdPackage = true;
            }
            if (!Directory.Exists(patchesPhysicalPath))
            {
                Directory.CreateDirectory(patchesPhysicalPath);
                createdPatches = true;
            }


            string packageJsonPath = Path.Combine(tempPackagePhysicalPath, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                string packageJson = @"{
  ""name"": ""com.yucp.temp"",
  ""version"": ""0.0.1"",
  ""displayName"": ""YUCP Temporary Patch Assets"",
  ""description"": ""Temporary folder for YUCP patch assets. Can be safely deleted after reverting patches."",
  ""unity"": ""2019.4"",
  ""hideInEditor"": true
}";
                File.WriteAllText(packageJsonPath, packageJson);
            }


            if (createdPackage)
            {
                CreateMetaFileIfNeeded(tempPackagePhysicalPath);
            }
            if (createdPatches)
            {
                CreateMetaFileIfNeeded(patchesPhysicalPath);
            }

            AssetDatabase.Refresh();

            if (!Directory.Exists(patchesPhysicalPath))
            {
                throw new InvalidOperationException($"Failed to create {patchesPath} folder (physical path does not exist).");
            }
        }

        private static void CreateMetaFileIfNeeded(string physicalPath)
        {
            string metaPath = physicalPath + ".meta";
            if (!File.Exists(metaPath))
            {
                string guid = System.Guid.NewGuid().ToString("N");
                string metaContent = $"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                File.WriteAllText(metaPath, metaContent);
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static string MakeSafeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "package-" + Guid.NewGuid().ToString("N");

            char[] invalid = Path.GetInvalidFileNameChars();
            var safeChars = new char[name.Length];
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    c = '-';
                else if (c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '\"' || c == '<' || c == '>' || c == '|')
                    c = '-';
                else if (Array.IndexOf(invalid, c) >= 0)
                    c = '-';

                safeChars[i] = c;
            }

            string safe = new string(safeChars).Trim('-');
            if (string.IsNullOrEmpty(safe))
                safe = "package-" + Guid.NewGuid().ToString("N");
            return safe;
        }

        /// <summary>
        /// Generate a package.json file for the export
        /// </summary>
        private static void CreateEmptyUnityPackage(string packagePath)
        {
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
            using (var outputStream = File.Create(packagePath))
            using (var gzipStream = new GZipOutputStream(outputStream))
            using (var tarArchive = TarArchive.CreateOutputTarArchive(gzipStream, Encoding.UTF8))
            {
            }
#else
            throw new InvalidOperationException("Creating empty unitypackage archives requires SharpZipLib.");
#endif
        }

        /// <summary>
        /// Save a Texture2D as a temporary asset if it's not already a Unity asset.
        /// Returns the asset path, or null if saving failed.
        /// </summary>
        public static string SaveTextureToLibraryCache(Texture2D texture, string baseName)
        {
            return SaveTextureToLibraryCache(texture, baseName, null);
        }

        private static string SaveTextureToLibraryCache(Texture2D texture, string baseName, ICollection<string> temporaryFiles)
        {
            if (texture == null) return null;

            try
            {
                // Cache outside Assets/ to avoid project residue
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string tempDir = Path.Combine(projectRoot, "Library", "YUCP", "PackageExporter", "TempAssets");
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);

                // Generate unique filename
                string sanitizedName = SanitizeFileName(baseName);
                string filePath = temporaryFiles != null
                    ? ExportTempCleanup.CreatePath("YUCP_Texture_", ".png", tempDir)
                    : Path.Combine(tempDir, $"{sanitizedName}_{texture.GetInstanceID()}.png");

                byte[] pngData = TryEncodeTextureToPng(texture, baseName);

                if (pngData == null || pngData.Length == 0)
                {
                    Debug.LogWarning($"[PackageBuilder] Failed to encode texture to PNG for '{baseName}'");
                    return null;
                }

                // Write PNG file
                temporaryFiles?.Add(filePath);
                File.WriteAllBytes(filePath, pngData);

                // Return the cached file path
                return filePath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Failed to cache texture: {ex.Message}");
                return null;
            }
        }

        private static string ResolvePackageIconSourcePath(Texture2D texture, string baseName, ICollection<string> temporaryFiles = null)
        {
            if (texture == null)
            {
                return null;
            }

            string cachedPngPath = SaveTextureToLibraryCache(texture, baseName, temporaryFiles);
            if (!string.IsNullOrEmpty(cachedPngPath) && File.Exists(cachedPngPath))
            {
                return cachedPngPath;
            }

            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            string fullAssetPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullAssetPath))
            {
                return null;
            }

            return string.Equals(Path.GetExtension(fullAssetPath), ".png", StringComparison.OrdinalIgnoreCase)
                ? fullAssetPath
                : null;
        }

        private static byte[] TryEncodeTextureToPng(Texture2D texture, string baseName)
        {
            try
            {
                return texture.EncodeToPNG();
            }
            catch (Exception directEncodeError)
            {
                if (TryEncodeTextureViaReadableImport(texture, out var importedTexturePng))
                    return importedTexturePng;

                if (TryEncodeTextureViaRenderTexture(texture, out var renderTexturePng))
                    return renderTexturePng;

                Debug.LogWarning($"[PackageBuilder] Failed to encode texture to PNG for '{baseName}': {directEncodeError.Message}");
                return null;
            }
        }

        private static bool TryEncodeTextureViaRenderTexture(Texture2D texture, out byte[] pngData)
        {
            pngData = null;
            Texture2D readableCopy = null;
            RenderTexture renderTexture = null;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                renderTexture = RenderTexture.GetTemporary(
                    Mathf.Max(1, texture.width),
                    Mathf.Max(1, texture.height),
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default);

                Graphics.Blit(texture, renderTexture);
                RenderTexture.active = renderTexture;

                readableCopy = new Texture2D(
                    Mathf.Max(1, texture.width),
                    Mathf.Max(1, texture.height),
                    TextureFormat.RGBA32,
                    false);
                readableCopy.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                readableCopy.Apply(false, false);
                pngData = readableCopy.EncodeToPNG();
                return pngData != null && pngData.Length > 0;
            }
            catch
            {
                pngData = null;
                return false;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }

                if (readableCopy != null)
                {
                    UnityEngine.Object.DestroyImmediate(readableCopy);
                }
            }
        }

        private static bool TryEncodeTextureViaReadableImport(Texture2D texture, out byte[] pngData)
        {
            pngData = null;
            Texture2D readableCopy = null;

            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath))
                return false;

            if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter textureImporter))
                return false;

            bool restoreReadable = !textureImporter.isReadable;

            try
            {
                if (restoreReadable)
                {
                    textureImporter.isReadable = true;
                    textureImporter.SaveAndReimport();
                }

                var readableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (readableTexture == null)
                    return false;

                readableCopy = new Texture2D(
                    Mathf.Max(1, readableTexture.width),
                    Mathf.Max(1, readableTexture.height),
                    TextureFormat.RGBA32,
                    false,
                    !textureImporter.sRGBTexture);
                readableCopy.SetPixels(readableTexture.GetPixels());
                readableCopy.Apply(false, false);

                pngData = readableCopy.EncodeToPNG();
                return pngData != null && pngData.Length > 0;
            }
            catch
            {
                pngData = null;
                return false;
            }
            finally
            {
                if (restoreReadable)
                {
                    try
                    {
                        textureImporter.isReadable = false;
                        textureImporter.SaveAndReimport();
                    }
                    catch (Exception restoreEx)
                    {
                        Debug.LogWarning($"[PackageBuilder] Failed to restore texture readability for '{assetPath}': {restoreEx.Message}");
                    }
                }

                if (readableCopy != null)
                {
                    UnityEngine.Object.DestroyImmediate(readableCopy);
                }
            }
        }

        /// <summary>
        /// Generate YUCP_PackageInfo.json metadata for the export
        /// </summary>
        private static string GeneratePackageMetadataJson(ExportProfile profile, PackageEmbedContext embedContext, List<string> exportedAssets)
        {
            try
            {
                // Create serializable metadata with string paths for icon/banner
                var metadataJson = new PackageMetadataJson
                {
                    packageName = profile.packageName ?? "",
                    version = profile.version ?? "",
                    author = profile.author ?? "",
                    description = profile.description ?? "",
                    productLinks = new List<ProductLinkJson>()
                };

                // Convert product links
                if (profile.productLinks != null)
                {
                    Debug.Log($"[PackageBuilder] Serializing {profile.productLinks.Count} product links");
                    foreach (var link in profile.productLinks)
                    {
                        var linkJson = new ProductLinkJson
                        {
                            label = link.label ?? "",
                            url = link.url ?? ""
                        };

                        string iconPath = null;
                        if (embedContext != null && embedContext.ProductLinkIconPaths.TryGetValue(link, out var embeddedPath))
                        {
                            iconPath = embeddedPath;
                        }
                        else
                        {
                            Texture2D iconToUse = link.customIcon;
                            if (iconToUse != null && embedContext != null)
                            {
                                iconPath = embedContext.RegisterTextureForExport(iconToUse, link.label ?? "ProductLink", "ProductLinks", out var _);
                            }
                            else if (iconToUse != null)
                            {
                                string assetPath = AssetDatabase.GetAssetPath(iconToUse);
                                if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Assets/"))
                                {
                                    iconPath = assetPath;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(iconPath))
                        {
                            linkJson.icon = iconPath;
                            Debug.Log($"[PackageBuilder] Added product link icon path: {iconPath} for link '{link.label}' (source: {(link.customIcon != null ? "customIcon" : "icon")})");
                        }
                        else
                        {
                            Debug.Log($"[PackageBuilder] Product link '{link.label}' has no icon (neither customIcon nor icon)");
                        }

                        metadataJson.productLinks.Add(linkJson);
                    }
                }
                else
                {
                    Debug.Log("[PackageBuilder] No product links to serialize");
                }

                // Get version rule name
                string versionRuleName = "semver";
                if (profile.customVersionRule != null && !string.IsNullOrEmpty(profile.customVersionRule.ruleName))
                {
                    versionRuleName = profile.customVersionRule.ruleName;
                }
                metadataJson.versionRule = versionRuleName;
                metadataJson.versionRuleName = versionRuleName;

                // Add icon path if exists
                if (profile.icon != null && !IsDefaultGridPlaceholder(profile.icon))
                {
                    string iconPath = embedContext != null ? embedContext.IconPath : null;
                    if (string.IsNullOrEmpty(iconPath) && embedContext != null)
                    {
                        iconPath = embedContext.RegisterTextureForExport(profile.icon, profile.packageName ?? "PackageIcon", "Icons", out var _);
                        embedContext.IconPath = iconPath;
                    }
                    if (string.IsNullOrEmpty(iconPath))
                    {
                        string assetPath = AssetDatabase.GetAssetPath(profile.icon);
                        if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Assets/"))
                        {
                            iconPath = assetPath;
                        }
                    }
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        metadataJson.icon = iconPath;
                    }
                }

                // Add banner path if exists
                if (profile.banner != null)
                {
                    string bannerPath = embedContext != null ? embedContext.BannerPath : null;
                    if (string.IsNullOrEmpty(bannerPath) && embedContext != null)
                    {
                        bannerPath = embedContext.RegisterTextureForExport(profile.banner, profile.packageName ?? "PackageBanner", "Banners", out var _);
                        embedContext.BannerPath = bannerPath;
                    }
                    if (string.IsNullOrEmpty(bannerPath))
                    {
                        string assetPath = AssetDatabase.GetAssetPath(profile.banner);
                        if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Assets/"))
                        {
                            bannerPath = assetPath;
                        }
                    }
                    if (!string.IsNullOrEmpty(bannerPath))
                    {
                        metadataJson.banner = bannerPath;
                    }
                }

                // Embed export-time hashes (baseline for update validation)
                if (exportedAssets != null && exportedAssets.Count > 0)
                {
                    metadataJson.fileHashes = BuildExportHashes(exportedAssets);
                }

                // Storefront metadata
                if (!string.IsNullOrEmpty(profile.tagline))
                    metadataJson.tagline = profile.tagline;
                if (profile.category != PackageCategory.None)
                    metadataJson.category = profile.category.ToString();
                if (profile.supportedPlatforms != null && profile.supportedPlatforms.Count > 0)
                    metadataJson.supportedPlatforms = new List<string>(profile.supportedPlatforms);
                if (!string.IsNullOrEmpty(profile.minimumUnityVersion))
                    metadataJson.minimumUnityVersion = profile.minimumUnityVersion;
                if (!string.IsNullOrEmpty(profile.creatorNote))
                    metadataJson.creatorNote = profile.creatorNote;
                if (!string.IsNullOrEmpty(profile.releaseNotes))
                    metadataJson.releaseNotes = profile.releaseNotes;
                // Tags from existing tag system
                var allTags = profile.GetAllTags();
                if (allTags != null && allTags.Count > 0)
                    metadataJson.tags = allTags;

                // Gallery image paths (already embedded by PackageEmbedContext)
                if (embedContext != null && embedContext.GalleryImagePaths.Count > 0)
                    metadataJson.galleryImages = new List<string>(embedContext.GalleryImagePaths);

                // Auto-computed asset statistics
                metadataJson.exportDate = DateTime.UtcNow.ToString("O");
                if (exportedAssets != null && exportedAssets.Count > 0)
                {
                    metadataJson.totalFileCount = exportedAssets.Count;

                    long totalSize = 0;
                    var typeCounts = new Dictionary<string, int>();
                    string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    foreach (var assetPath in exportedAssets)
                    {
                        if (string.IsNullOrEmpty(assetPath)) continue;
                        string abs = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(abs))
                        {
                            try { totalSize += new FileInfo(abs).Length; } catch { }
                        }

                        string ext = Path.GetExtension(assetPath).ToLowerInvariant();
                        string assetType = ext switch
                        {
                            ".prefab" => "Prefab",
                            ".cs" => "Script",
                            ".shader" or ".shadergraph" or ".shadersubgraph" => "Shader",
                            ".mat" => "Material",
                            ".png" or ".jpg" or ".jpeg" or ".tga" or ".psd" or ".gif" or ".bmp" => "Texture",
                            ".fbx" or ".obj" or ".blend" => "Model",
                            ".anim" => "Animation",
                            ".controller" or ".overrideController" => "Animator",
                            ".asset" => "Asset",
                            ".unity" => "Scene",
                            ".dll" => "Assembly",
                            ".mesh" => "Mesh",
                            _ => null
                        };
                        if (assetType != null)
                        {
                            typeCounts.TryGetValue(assetType, out int count);
                            typeCounts[assetType] = count + 1;
                        }
                    }
                    metadataJson.totalFileSize = totalSize;

                    if (typeCounts.Count > 0)
                    {
                        metadataJson.assetBreakdown = new List<AssetBreakdownJson>();
                        foreach (var kvp in typeCounts.OrderByDescending(x => x.Value))
                        {
                            metadataJson.assetBreakdown.Add(new AssetBreakdownJson { type = kvp.Key, count = kvp.Value });
                        }
                    }
                }

                // Serialize to JSON
                return JsonUtility.ToJson(metadataJson, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Failed to generate package metadata: {ex.Message}");
                return null;
            }
        }

        [Serializable]
        private class PackageMetadataJson
        {
            public string packageName;
            public string version;
            public string author;
            public string description;
            public string icon;
            public string banner;
            public List<ProductLinkJson> productLinks;
            public string versionRule;
            public string versionRuleName;
            public List<FileHashJson> fileHashes;
            public string tagline;
            public string category;
            public List<string> supportedPlatforms;
            public string minimumUnityVersion;
            public string creatorNote;
            public string releaseNotes;
            public List<string> galleryImages;
            public List<string> tags;
            public int totalFileCount;
            public long totalFileSize;
            /// <summary>
            /// Counts of the authored project assets selected for export, by type.
            ///
            /// NOT the archive's contents: this is generated before the installer
            /// runtime, bundled packages, and obfuscated assemblies are injected, so
            /// it under-reports (in particular it can show no "Assembly" entry for a
            /// package that ships DLLs). Consumers that need real archive contents
            /// must read the package, not this field.
            /// </summary>
            public List<AssetBreakdownJson> assetBreakdown;
            public string exportDate;
        }

        [Serializable]
        private class ProductLinkJson
        {
            public string label;
            public string url;
            public string icon;
        }

        [Serializable]
        private class FileHashJson
        {
            public string path;
            public string hash;
        }

        [Serializable]
        private class AssetBreakdownJson
        {
            public string type;
            public int count;
        }

        private static List<FileHashJson> BuildExportHashes(List<string> exportedAssets)
        {
            var list = new List<FileHashJson>();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            foreach (var unityPath in exportedAssets)
            {
                if (string.IsNullOrEmpty(unityPath)) continue;
                if (!unityPath.StartsWith("Assets/") && !unityPath.StartsWith("Packages/")) continue;

                string abs = Path.Combine(projectRoot, unityPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(abs)) continue;

                string hash = ComputeFileHash(abs);
                if (string.IsNullOrEmpty(hash)) continue;

                list.Add(new FileHashJson { path = unityPath, hash = hash });
            }

            return list;
        }

        private static string ComputeFileHash(string filePath)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(filePath))
                {
                    var bytes = sha.ComputeHash(fs);
                    return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Convert absolute path to Unity-relative path (Assets/... or Packages/...)
        /// </summary>
        private static string GetRelativePackagePath(string absolutePath)
        {

            if (absolutePath.StartsWith("Assets/") || absolutePath.StartsWith("Packages/"))
            {
                return absolutePath;
            }

            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));


            string normalizedInput = absolutePath.Replace('\\', '/');
            string normalizedProject = projectPath.Replace('\\', '/');

            if (normalizedInput.StartsWith(normalizedProject))
            {
                string relative = normalizedInput.Substring(normalizedProject.Length);

                if (relative.StartsWith("/"))
                {
                    relative = relative.Substring(1);
                }

                return relative;
            }

            return absolutePath;
        }

        /// <summary>
        /// Collect all assets to export using profile settings
        /// </summary>
        internal static List<string> CollectAssetsToExport(ExportProfile profile)
        {
            var assets = new HashSet<string>();

            if (profile.HasScannedAssets && profile.discoveredAssets != null && profile.discoveredAssets.Count > 0)
            {





                var exportedPackages = AssetCollector.GetExportedPackageNames(profile);
                var includedAssets = profile.discoveredAssets
                    .Where(a => a.included && !a.isFolder && (profile.includeDependencies || !a.isDependency))
                    // Scans made before installed-package files were skipped may still list them
                    .Where(a => !a.isDependency || AssetCollector.GetSkippedPackageName(a.assetPath, profile, exportedPackages) == null)
                    .Select(a => GetRelativePackagePath(a.assetPath))
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Where(assetPath => !ShouldExcludeAsset(assetPath, profile))
                    .ToList();


                return includedAssets;
            }

            foreach (string folder in profile.foldersToExport)
            {
                string assetFolder = folder;


                if (Path.IsPathRooted(folder))
                {

                    string currentProjectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string normalizedFolder = folder.Replace('\\', '/');
                    string normalizedProject = currentProjectPath.Replace('\\', '/');

                    if (normalizedFolder.StartsWith(normalizedProject))
                    {

                        assetFolder = normalizedFolder.Substring(normalizedProject.Length + 1);
                    }
                    else
                    {

                        string folderName = Path.GetFileName(folder);
                        string possiblePath = Path.Combine("Assets", folderName);
                        if (AssetDatabase.IsValidFolder(possiblePath))
                        {
                            assetFolder = possiblePath;
                        }
                        else
                        {
                            Debug.LogWarning($"[PackageBuilder] Folder from different project not found in current project: {folder}");
                            continue;
                        }
                    }
                }
                else if (!folder.StartsWith("Assets") && !folder.StartsWith("Packages"))
                {

                    assetFolder = Path.Combine("Assets", folder).Replace('\\', '/');
                }


                if (!assetFolder.StartsWith("Assets") && !assetFolder.StartsWith("Packages"))
                {
                    Debug.LogWarning($"[PackageBuilder] Invalid folder path (must start with Assets or Packages): {assetFolder}");
                    continue;
                }

                if (!AssetDatabase.IsValidFolder(assetFolder))
                {
                    Debug.LogWarning($"[PackageBuilder] Folder not found in AssetDatabase: {assetFolder}");
                    continue;
                }

                AssetDatabase.Refresh();

                if (!AssetDatabase.IsValidFolder(assetFolder))
                {
                    Debug.LogWarning($"[PackageBuilder] Folder not recognized by AssetDatabase: {assetFolder}. Creating meta file...");


                    string metaPath = assetFolder + ".meta";
                    if (!File.Exists(metaPath))
                    {
                        try
                        {

                            string metaContent = $"fileFormatVersion: 2\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                            File.WriteAllText(metaPath, metaContent);
                            AssetDatabase.Refresh();
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"[PackageBuilder] Failed to create meta file for {assetFolder}: {ex.Message}");
                            continue;
                        }
                    }
                }

                string[] guids = AssetDatabase.FindAssets("", new[] { assetFolder });

                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);


                    if (ShouldExcludeAsset(assetPath, profile))
                    {
                        continue;
                    }

                    assets.Add(assetPath);
                }
            }

            return assets.ToList();
        }

        /// <summary>
        /// Check if an asset should be excluded using filters
        /// </summary>
        internal static bool ShouldExcludeAsset(string assetPath, ExportProfile profile)
        {


            string unityLikePath = GetRelativePackagePath(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(unityLikePath) &&
                unityLikePath.StartsWith("Packages/com.yucp.temp/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }


            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullPath;


            if (Path.IsPathRooted(assetPath))
            {

                fullPath = Path.GetFullPath(assetPath);
            }
            else
            {

                fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            }

            return AssetCollector.ShouldIgnoreFile(fullPath, profile);
        }

        /// <summary>
        /// Manually collect and filter dependencies for assets, respecting ignore lists
        /// Recursively collects dependencies up to a maximum depth to catch nested dependencies
        /// </summary>
        private static void CollectFilteredDependencies(List<string> assetsToExport, ExportProfile profile, Action<float, string> progressCallback)
        {
            if (!profile.includeDependencies)
                return;

            progressCallback?.Invoke(0.53f, "Collecting dependencies (respecting ignore list)...");

            var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toProcess = new Queue<string>();


            var explicitlyExcludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profile.HasScannedAssets && profile.discoveredAssets != null)
            {
                foreach (var a in profile.discoveredAssets)
                {
                    if (!a.isFolder && !a.included)
                    {
                        string unityPath = GetRelativePackagePath(a.assetPath);
                        if (!string.IsNullOrEmpty(unityPath))
                        {
                            explicitlyExcludedPaths.Add(unityPath.Replace('\\', '/'));
                        }
                    }
                }
            }
            const int maxDepth = 5;
            var exportedPackages = AssetCollector.GetExportedPackageNames(profile);
            var skippedPackageFiles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);


            foreach (var asset in assetsToExport)
            {
                string unityPath = GetRelativePackagePath(asset);
                if (!string.IsNullOrEmpty(unityPath))
                {
                    processedPaths.Add(unityPath);
                    toProcess.Enqueue(unityPath);
                }
            }


            int depth = 0;
            int itemsAtCurrentDepth = toProcess.Count;
            int processedCount = 0;
            const int progressInterval = 75;

            while (toProcess.Count > 0 && depth < maxDepth)
            {
                if (itemsAtCurrentDepth == 0)
                {
                    depth++;
                    itemsAtCurrentDepth = toProcess.Count;
                }

                string assetPath = toProcess.Dequeue();
                itemsAtCurrentDepth--;
                processedCount++;
                if (processedCount % progressInterval == 0)
                    progressCallback?.Invoke(0.53f, $"Collecting dependencies... ({processedCount} assets processed)");

                try
                {

                    string[] dependencies = AssetDatabase.GetDependencies(assetPath, recursive: false);

                    foreach (string dep in dependencies)
                    {

                        if (dep == assetPath)
                            continue;


                        if (processedPaths.Contains(dep))
                            continue;

                        processedPaths.Add(dep);


                        // Files owned by other installed packages (e.g. VRCSDK3A.dll) would overwrite the buyer's copy
                        string skippedPackage = AssetCollector.GetSkippedPackageName(dep, profile, exportedPackages);
                        if (skippedPackage != null)
                        {
                            skippedPackageFiles[skippedPackage] = skippedPackageFiles.TryGetValue(skippedPackage, out int count) ? count + 1 : 1;
                            continue;
                        }


                        if (ShouldExcludeAsset(dep, profile))
                        {
                            Debug.Log($"[PackageBuilder] Excluding dependency (in ignore list): {dep}");
                            continue;
                        }


                        string depNormalized = dep.Replace('\\', '/');
                        if (explicitlyExcludedPaths.Contains(depNormalized))
                        {
                            Debug.Log($"[PackageBuilder] Excluding dependency (unchecked in Export Inspector): {dep}");
                            continue;
                        }


                        allDependencies.Add(dep);
                        if (depth < maxDepth - 1)
                        {
                            toProcess.Enqueue(dep);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageBuilder] Error getting dependencies for {assetPath}: {ex.Message}");
                }
            }


            foreach (var skipped in skippedPackageFiles)
            {
                Debug.LogWarning($"[PackageBuilder] Not bundling {skipped.Value} file(s) from installed package '{skipped.Key}'. " +
                                 "Buyers use their own copy; list it under Dependencies instead, or turn on 'Bundle Installed Package Files' to force it.");
            }

            if (allDependencies.Count > 0)
            {
                Debug.Log($"[PackageBuilder] Adding {allDependencies.Count} filtered dependencies to export (checked {depth} levels deep)");
                assetsToExport.AddRange(allDependencies);
            }
        }

        /// <summary>
        /// Simple wildcard matching (* and ? support)
        /// </summary>
        private static bool WildcardMatch(string text, string pattern)
        {

            string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Check if a directory is writable
        /// </summary>
        private static bool CheckDirectoryWritable(string directoryPath)
        {
            try
            {
                string testFile = Path.Combine(directoryPath, $"test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildUnityImportOverlayStatus(string phase)
        {
            if (string.IsNullOrWhiteSpace(phase))
            {
                return "Unity may briefly show a generic 'Importing' overlay during this step...";
            }

            return $"{phase}... Unity may briefly show a generic 'Importing' overlay.";
        }

        /// <summary>
        /// Inject package.json, DirectVpmInstaller, and bundled packages into a .unitypackage file
        /// </summary>
        private static void InjectPackageJsonInstallerAndBundles(
            string unityPackagePath,
            string packageJsonContent,
            Dictionary<string, string> bundledPackagePaths,
            List<AssemblyObfuscationSettings> obfuscatedAssemblies,
            ExportProfile profile,
            bool hasPatchAssets,
            string packageMetadataJson,
            PackageEmbedContext embedContext,
            Action<float, string> progressCallback = null)
        {






            string tempExtractDir = ExportTempCleanup.CreatePath("YUCP_PackageExtract_");
            string tempOutputPath = unityPackagePath + ".tmp";

            try
            {

                Directory.CreateDirectory(tempExtractDir);
                progressCallback?.Invoke(0.751f, "Extracting exported package archive...");


#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
                ArchiveExtractionUtility.ExtractUnityPackageSafely(unityPackagePath, tempExtractDir);
#else
                Debug.LogError("[PackageBuilder] ICSharpCode.SharpZipLib not available. Package injection disabled.");
                return;
#endif

                bool shouldInjectInstallerSurface = !string.IsNullOrEmpty(packageJsonContent);
                bool shouldInjectInstallerRuntime = shouldInjectInstallerSurface;

                if (shouldInjectInstallerRuntime)
                {
                    foreach (string pathname in Directory.GetFiles(tempExtractDir, "pathname", SearchOption.AllDirectories))
                    {
                        string assetPath = File.ReadAllText(pathname);
                        string deferredPath = DeferImportedCode(assetPath);
                        if (assetPath != deferredPath)
                            File.WriteAllText(pathname, deferredPath);
                    }
                }


                progressCallback?.Invoke(0.754f, "Injecting package shell metadata...");
                if (shouldInjectInstallerSurface)
                {
                    string packageJsonGuid = Guid.NewGuid().ToString("N");
                    string packageJsonFolder = Path.Combine(tempExtractDir, packageJsonGuid);
                    Directory.CreateDirectory(packageJsonFolder);

                    File.WriteAllText(Path.Combine(packageJsonFolder, "asset"), packageJsonContent);
                    string packageJsonPath = $"{(embedContext != null ? embedContext.TempInstallRoot : "Assets")}/YUCP_TempInstall_{packageJsonGuid}.json";
                    File.WriteAllText(Path.Combine(packageJsonFolder, "pathname"), packageJsonPath);

                    string packageJsonMeta = "fileFormatVersion: 2\nguid: " + packageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(packageJsonFolder, "asset.meta"), packageJsonMeta);
                }


                if (!string.IsNullOrEmpty(packageMetadataJson))
                {
                    string metadataGuid = Guid.NewGuid().ToString("N");
                    string metadataFolder = Path.Combine(tempExtractDir, metadataGuid);
                    Directory.CreateDirectory(metadataFolder);


                    File.WriteAllText(Path.Combine(metadataFolder, "asset"), packageMetadataJson);


                    string metaGuid = Guid.NewGuid().ToString("N");
                    string metaPath = Path.Combine(metadataFolder, "asset.meta");
                    string metaContent = $"fileFormatVersion: 2\nguid: {metaGuid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(metaPath, metaContent);


                    string metadataPath = embedContext != null
                        ? $"{InstalledPackagesRoot}/{embedContext.SafePackageName}/YUCP_PackageInfo.json"
                        : "Assets/YUCP_PackageInfo.json";
                    File.WriteAllText(Path.Combine(metadataFolder, "pathname"), metadataPath);
                }


                if (embedContext != null && embedContext.Assets.Count > 0)
                {
                    progressCallback?.Invoke(0.756f, $"Embedding {embedContext.Assets.Count} package asset(s)...");
                    foreach (var embedded in embedContext.Assets)
                    {
                        if (embedded == null || string.IsNullOrEmpty(embedded.sourcePath) || !File.Exists(embedded.sourcePath))
                            continue;

                        string guid = Guid.NewGuid().ToString("N");
                        string folder = Path.Combine(tempExtractDir, guid);
                        Directory.CreateDirectory(folder);

                        File.Copy(embedded.sourcePath, Path.Combine(folder, "asset"), true);
                        File.WriteAllText(Path.Combine(folder, "pathname"), embedded.unityPath);

                        string metaContent = GenerateMetaForFile(embedded.sourcePath, guid);
                        File.WriteAllText(Path.Combine(folder, "asset.meta"), metaContent);
                    }
                }


                if (embedContext != null && embedContext.UsesInstalledPackageWorkspace)
                {
                    string installedPackageGuid = Guid.NewGuid().ToString("N");
                    string installedPackageFolder = Path.Combine(tempExtractDir, installedPackageGuid);
                    Directory.CreateDirectory(installedPackageFolder);

                    string installedPackageJson = @"{
  ""name"": ""yucp.installed-packages"",
  ""version"": ""0.0.1"",
  ""displayName"": ""YUCP Installed Packages"",
  ""description"": ""Local container for YUCP installer metadata and helper scripts."",
  ""unity"": ""2019.4"",
  ""hideInEditor"": true
}";

                    File.WriteAllText(Path.Combine(installedPackageFolder, "asset"), installedPackageJson);
                    File.WriteAllText(Path.Combine(installedPackageFolder, "pathname"), $"{InstalledPackagesRoot}/package.json");
                    string installedPackageMeta = GenerateMetaForFile("package.json", installedPackageGuid);
                    File.WriteAllText(Path.Combine(installedPackageFolder, "asset.meta"), installedPackageMeta);
                }

                progressCallback?.Invoke(0.758f, "Injecting installer and patch runtime...");

                string installerRoot = embedContext != null ? embedContext.InstallerRoot : "Assets/Editor";
                bool deferInstallerActivation = embedContext != null && shouldInjectInstallerRuntime;
                string installerScriptPath = null;
                string[] foundScripts = AssetDatabase.FindAssets("DirectVpmInstaller t:Script");

                if (foundScripts.Length > 0)
                {
                    installerScriptPath = AssetDatabase.GUIDToAssetPath(foundScripts[0]);
                }

                string installerDir = !string.IsNullOrEmpty(installerScriptPath)
                    ? Path.GetDirectoryName(installerScriptPath)
                    : null;
                if (deferInstallerActivation)
                {
                    TryInjectInstallerPreflightBootstrap(tempExtractDir, installerRoot, installerScriptPath);
                }

                bool usingPrecompiledInstallerRuntime = false;
                if (deferInstallerActivation)
                {
                    usingPrecompiledInstallerRuntime = TryInjectPrecompiledInstallerRuntime(tempExtractDir, installerRoot);
                    if (usingPrecompiledInstallerRuntime)
                    {
                        Debug.Log("[PackageBuilder] Injected precompiled DirectVpmInstaller runtime for embedded installer handoff.");
                    }
                    else
                    {
                        throw new FileNotFoundException($"Required precompiled DirectVpmInstaller runtime not found at {PrecompiledInstallerRuntimePath}. Refusing to fall back to installer source templates.");
                    }
                }

                if (shouldInjectInstallerSurface &&
                    !usingPrecompiledInstallerRuntime &&
                    !string.IsNullOrEmpty(installerScriptPath) &&
                    File.Exists(installerScriptPath))
                {
                    string installerGuid = Guid.NewGuid().ToString("N");
                    string installerFolder = Path.Combine(tempExtractDir, installerGuid);
                    Directory.CreateDirectory(installerFolder);

                    string installerContent = File.ReadAllText(installerScriptPath);
                    File.WriteAllText(Path.Combine(installerFolder, "asset"), installerContent);
                    string installerFileName = $"YUCP_Installer_{installerGuid}.cs";
                    string installerPathname = deferInstallerActivation
                        ? $"{installerRoot}/{installerFileName}.yucp_disabled"
                        : $"{installerRoot}/{installerFileName}";
                    File.WriteAllText(Path.Combine(installerFolder, "pathname"), installerPathname);

                    string installerMeta = "fileFormatVersion: 2\nguid: " + installerGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(installerFolder, "asset.meta"), installerMeta);


                    string asmdefPath = Path.Combine(installerDir, "DirectVpmInstaller.asmdef");

                    if (File.Exists(asmdefPath))
                    {
                        string asmdefGuid = Guid.NewGuid().ToString("N");
                        string asmdefFolder = Path.Combine(tempExtractDir, asmdefGuid);
                        Directory.CreateDirectory(asmdefFolder);

                        string asmdefContent = File.ReadAllText(asmdefPath);


                        try
                        {
                            string uniqueAssemblyName = $"YUCP.DirectVpmInstaller.{installerGuid}";
                            asmdefContent = System.Text.RegularExpressions.Regex.Replace(
                                asmdefContent,
                                "\"name\"\\s*:\\s*\"[^\"]*\"",
                                "\"name\": \"" + uniqueAssemblyName + "\""
                            );
                        }
                        catch {  }
                        File.WriteAllText(Path.Combine(asmdefFolder, "asset"), asmdefContent);
                        string installerAsmdefName = $"YUCP_Installer_{installerGuid}.asmdef";
                        string installerAsmdefPathname = deferInstallerActivation
                            ? $"{installerRoot}/{installerAsmdefName}.yucp_disabled"
                            : $"{installerRoot}/{installerAsmdefName}";
                        File.WriteAllText(Path.Combine(asmdefFolder, "pathname"), installerAsmdefPathname);

                        string asmdefMeta = "fileFormatVersion: 2\nguid: " + asmdefGuid + "\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(asmdefFolder, "asset.meta"), asmdefMeta);

                    }



                    string txnManagerPath = Path.Combine(installerDir, "InstallerTransactionManager.cs");
                    if (File.Exists(txnManagerPath))
                    {
                        string txnManagerGuid = Guid.NewGuid().ToString("N");
                        string txnManagerFolder = Path.Combine(tempExtractDir, txnManagerGuid);
                        Directory.CreateDirectory(txnManagerFolder);

                        string txnManagerContent = File.ReadAllText(txnManagerPath);
                        File.WriteAllText(Path.Combine(txnManagerFolder, "asset"), txnManagerContent);
                        string txnManagerName = $"YUCP_InstallerTxn_{installerGuid}.cs";
                        string txnManagerPathname = deferInstallerActivation
                            ? $"{installerRoot}/{txnManagerName}.yucp_disabled"
                            : $"{installerRoot}/{txnManagerName}";
                        File.WriteAllText(Path.Combine(txnManagerFolder, "pathname"), txnManagerPathname);

                        string txnManagerMeta = "fileFormatVersion: 2\nguid: " + txnManagerGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(txnManagerFolder, "asset.meta"), txnManagerMeta);

                    }
                    else
                    {
                        Debug.LogWarning("[PackageBuilder] Could not find InstallerTransactionManager.cs template - installer will fail to compile!");
                    }


                    string healthToolsScriptPath = Path.Combine(installerDir, "InstallerHealthTools.cs");
                    if (File.Exists(healthToolsScriptPath))
                    {
                        string healthToolsGuid = Guid.NewGuid().ToString("N");
                        string healthToolsFolder = Path.Combine(tempExtractDir, healthToolsGuid);
                        Directory.CreateDirectory(healthToolsFolder);

                        string healthToolsContent = File.ReadAllText(healthToolsScriptPath);
                        File.WriteAllText(Path.Combine(healthToolsFolder, "asset"), healthToolsContent);
                        string healthToolsName = $"YUCP_InstallerHealthTools_{installerGuid}.cs";
                        string healthToolsPathname = deferInstallerActivation
                            ? $"{installerRoot}/{healthToolsName}.yucp_disabled"
                            : $"{installerRoot}/{healthToolsName}";
                        File.WriteAllText(Path.Combine(healthToolsFolder, "pathname"), healthToolsPathname);

                        string healthToolsMeta = "fileFormatVersion: 2\nguid: " + healthToolsGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(healthToolsFolder, "asset.meta"), healthToolsMeta);
                    }
                }
                else if (shouldInjectInstallerSurface && !usingPrecompiledInstallerRuntime)
                {
                    Debug.LogWarning("[PackageBuilder] Could not find DirectVpmInstaller.cs template");
                }


                bool shouldInjectTempPackageShell =
                    hasPatchAssets ||
                    (deferInstallerActivation &&
                     embedContext != null &&
                     !embedContext.UsesInstalledPackageWorkspace);

                if (shouldInjectTempPackageShell)
                {
                    if (hasPatchAssets)
                    {
                        progressCallback?.Invoke(0.70f, "Injecting derived FBX import support...");
                        PatchImportPackageInjector.InjectRequiredPatchImportFiles(
                            tempExtractDir,
                            usingPrecompiledInstallerRuntime,
                            TryInjectPrecompiledInstallerRuntime);
                    }


                    string tempPackageJsonPath = Path.Combine(Application.dataPath, "..", "Packages", "com.yucp.temp", "package.json");
                    string tempPackageJsonGuid = Guid.NewGuid().ToString("N");
                    string tempPackageJsonFolder = Path.Combine(tempExtractDir, tempPackageJsonGuid);
                    Directory.CreateDirectory(tempPackageJsonFolder);

                    string tempPackageJsonContent;
                    if (File.Exists(tempPackageJsonPath))
                    {
                        tempPackageJsonContent = File.ReadAllText(tempPackageJsonPath);
                    }
                    else
                    {

                        tempPackageJsonContent = @"{
  ""name"": ""com.yucp.temp"",
  ""version"": ""0.0.1"",
  ""displayName"": ""YUCP Temporary Patch Assets"",
  ""description"": ""Temporary folder for YUCP patch assets. Can be safely deleted after reverting patches."",
  ""unity"": ""2019.4"",
  ""hideInEditor"": true
}";
                    }

                    File.WriteAllText(Path.Combine(tempPackageJsonFolder, "asset"), tempPackageJsonContent);
                    File.WriteAllText(Path.Combine(tempPackageJsonFolder, "pathname"), "Packages/com.yucp.temp/package.json");

                    string tempPackageJsonMeta = "fileFormatVersion: 2\nguid: " + tempPackageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(tempPackageJsonFolder, "asset.meta"), tempPackageJsonMeta);
                }


                if (bundledPackagePaths.Count > 0)
                {
                    int totalBundledFiles = 0;
                    int packageIndex = 0;

                    foreach (var bundledPackage in bundledPackagePaths)
                    {
                        packageIndex++;
                        string packageName = bundledPackage.Key;
                        string packagePath = bundledPackage.Value;

                        progressCallback?.Invoke(0.75f + (0.05f * packageIndex / bundledPackagePaths.Count),
                            $"Injecting bundled package {packageIndex}/{bundledPackagePaths.Count}: {packageName}...");


                        var obfuscatedAsmdefPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var obfuscatedDllPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var obfuscatedAsm in obfuscatedAssemblies)
                        {

                            if (obfuscatedAsm.asmdefPath.Replace("\\", "/").Contains($"/{packageName}/"))
                            {

                                string normalizedAsmdefPath = Path.GetFullPath(obfuscatedAsm.asmdefPath).Replace("\\", "/");
                                obfuscatedAsmdefPaths.Add(normalizedAsmdefPath);


                                var assemblyInfo = new AssemblyScanner.AssemblyInfo(obfuscatedAsm.assemblyName, obfuscatedAsm.asmdefPath);
                                if (assemblyInfo.exists)
                                {
                                    obfuscatedDllPaths[normalizedAsmdefPath] = assemblyInfo.dllPath;
                                }
                            }
                        }

                        string[] allFiles = Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories);
                        int filesAdded = 0;
                        int filesReplaced = 0;


                        var processedAsmdefDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (string filePath in allFiles)
                        {

                            if (filePath.EndsWith(".meta"))
                                continue;


                            string relativePath = filePath.Substring(packagePath.Length).TrimStart('\\', '/');


                            string fileDir = Path.GetDirectoryName(filePath);
                            string asmdefInDir = obfuscatedAsmdefPaths.FirstOrDefault(asmdefPath =>
                            {
                                string asmdefDir = Path.GetDirectoryName(asmdefPath);
                                return fileDir.Replace("\\", "/").StartsWith(asmdefDir.Replace("\\", "/"));
                            });

                            bool isInObfuscatedAssembly = !string.IsNullOrEmpty(asmdefInDir);

                            string extension = Path.GetExtension(filePath).ToLower();


                            if (extension == ".crproj")
                            {
                                continue;
                            }


                            bool shouldSkipCsFile = false;
                            if (extension == ".cs")
                            {
                                if (isInObfuscatedAssembly)
                                {
                                    shouldSkipCsFile = true;
                                }
                                else
                                {
                                    string fullPath = Path.GetFullPath(filePath).Replace("\\", "/");
                                    foreach (var asmdefPath in obfuscatedAsmdefPaths)
                                    {
                                        string asmdefDir = Path.GetDirectoryName(asmdefPath).Replace("\\", "/");
                                        if (fullPath.StartsWith(asmdefDir + "/", StringComparison.OrdinalIgnoreCase))
                                        {
                                            shouldSkipCsFile = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            if (shouldSkipCsFile)
                            {
                                continue;
                            }


                            if (extension == ".asmdef")
                            {
                                string asmdefFullPath = Path.GetFullPath(filePath).Replace("\\", "/");

                                if (obfuscatedAsmdefPaths.Contains(asmdefFullPath))
                                {


                                    if (!processedAsmdefDirs.Contains(asmdefFullPath))
                                    {
                                        processedAsmdefDirs.Add(asmdefFullPath);

                                        if (obfuscatedDllPaths.TryGetValue(asmdefFullPath, out string dllPath))
                                        {

                                            string dllFileName = Path.GetFileName(dllPath);
                                            string dllRelativePath = Path.Combine(Path.GetDirectoryName(relativePath), dllFileName).Replace("\\", "/");
                                            string dllUnityPathname = $"Packages/{packageName}/{dllRelativePath}";
                                            if (shouldInjectInstallerRuntime)
                                                dllUnityPathname = DeferImportedCode(dllUnityPathname);

                                            string dllGuid = Guid.NewGuid().ToString("N");
                                            string dllMetaContent = GenerateMetaForFile(dllPath, dllGuid);

                                            string dllFolder = Path.Combine(tempExtractDir, dllGuid);
                                            Directory.CreateDirectory(dllFolder);

                                            File.Copy(dllPath, Path.Combine(dllFolder, "asset"), true);
                                            File.WriteAllText(Path.Combine(dllFolder, "pathname"), dllUnityPathname);
                                            File.WriteAllText(Path.Combine(dllFolder, "asset.meta"), dllMetaContent);

                                            filesAdded++;
                                            filesReplaced++;
                                        }
                                        else
                                        {
                                            Debug.LogWarning($"[PackageBuilder] Could not find DLL path for asmdef: {asmdefFullPath}");
                                        }
                                    }

                                    continue;
                                }
                            }

                            string unityPathname = $"Packages/{packageName}/{relativePath.Replace('\\', '/')}";
                            if (shouldInjectInstallerRuntime)
                                unityPathname = DeferImportedCode(unityPathname);




                            string fileGuid = null;
                            string metaContent = null;
                            string originalMetaPath = filePath + ".meta";
                            string originalGuid = null;
                            try
                            {
                                if (File.Exists(originalMetaPath))
                                {
                                    string originalMeta = File.ReadAllText(originalMetaPath);
                                    var guidMatch = System.Text.RegularExpressions.Regex.Match(originalMeta, @"guid:\s*([a-f0-9]{32})");
                                    if (guidMatch.Success)
                                        originalGuid = guidMatch.Groups[1].Value;
                                }
                            }
                            catch {  }

                            if (File.Exists(originalMetaPath))
                            {

                                string originalMeta = File.ReadAllText(originalMetaPath);
                                var guidMatch = System.Text.RegularExpressions.Regex.Match(originalMeta, @"guid:\s*([a-f0-9]{32})");
                                if (guidMatch.Success)
                                {
                                    fileGuid = guidMatch.Groups[1].Value;
                                    metaContent = originalMeta;
                                }
                            }


                            if (string.IsNullOrEmpty(fileGuid))
                            {
                                fileGuid = Guid.NewGuid().ToString("N");
                                metaContent = GenerateMetaForFile(filePath, fileGuid);
                            }

                            string fileFolder = Path.Combine(tempExtractDir, fileGuid);
                            Directory.CreateDirectory(fileFolder);


                            File.Copy(filePath, Path.Combine(fileFolder, "asset"), true);


                            File.WriteAllText(Path.Combine(fileFolder, "pathname"), unityPathname);


                            File.WriteAllText(Path.Combine(fileFolder, "asset.meta"), metaContent);

                            filesAdded++;
                        }

                        totalBundledFiles += filesAdded;

                        if (filesReplaced > 0)
                        {
                        }
                        else
                        {
                        }
                    }

                }


#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
                progressCallback?.Invoke(0.799f, "Repacking final package archive...");

                using (var outputStream = File.Create(tempOutputPath))
                using (var gzipStream = new ICSharpCode.SharpZipLib.GZip.GZipOutputStream(outputStream))
                using (var tarArchive = ICSharpCode.SharpZipLib.Tar.TarArchive.CreateOutputTarArchive(gzipStream, System.Text.Encoding.UTF8))
                {
                    tarArchive.RootPath = tempExtractDir.Replace('\\', '/');
                    if (tarArchive.RootPath.EndsWith("/"))
                        tarArchive.RootPath = tarArchive.RootPath.Remove(tarArchive.RootPath.Length - 1);

                    AddDirectoryFilesToTar(tarArchive, tempExtractDir, true);
                }


                File.Delete(unityPackagePath);
                File.Move(tempOutputPath, unityPackagePath);
#endif
            }
            finally
            {
                ExportTempCleanup.Delete(tempOutputPath, tempExtractDir);
            }
        }

        /// <summary>
        /// Helper to recursively add files to a tar archive
        /// </summary>
        private static void AddDirectoryFilesToTar(object tarArchive, string sourceDirectory, bool recurse)
        {
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
            var archive = tarArchive as ICSharpCode.SharpZipLib.Tar.TarArchive;
            if (archive == null) return;

            var filenames = Directory.GetFiles(sourceDirectory);
            foreach (string filename in filenames)
            {
                var entry = ICSharpCode.SharpZipLib.Tar.TarEntry.CreateEntryFromFile(filename);
                archive.WriteEntry(entry, false);
            }

            if (recurse)
            {
                var directories = Directory.GetDirectories(sourceDirectory);
                foreach (string directory in directories)
                    AddDirectoryFilesToTar(archive, directory, recurse);
            }
#else
            Debug.LogError("[PackageBuilder] ICSharpCode.SharpZipLib not available. Please install the ICSharpCode.SharpZipLib package.");
#endif
        }

        /// <summary>
        /// Generate appropriate .meta file content using file extension
        /// </summary>
        private static string GenerateMetaForFile(string filePath, string guid)
        {
            string extension = Path.GetExtension(filePath).ToLower();


            if (extension == ".cs")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }


            if (extension == ".asmdef")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nAssemblyDefinitionImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }


            if (extension == ".md" || extension == ".txt" || extension == ".json" || extension == ".xml")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }


            if (extension == ".compute")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nComputeShaderImporter:\n  externalObjects: {{}}\n  currentAPIMask: 4\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }


            if (extension == ".shader" || extension == ".cginc" || extension == ".hlsl")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nShaderImporter:\n  externalObjects: {{}}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }


            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nTextureImporter:\n  internalIDToNameTable: []\n  externalObjects: {{}}\n  serializedVersion: 11\n  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 1\n    sRGBTexture: 1\n    linearTexture: 0\n    fadeOut: 0\n    borderMipMap: 0\n    mipMapsPreserveCoverage: 0\n    alphaTestReferenceValue: 0.5\n    mipMapFadeDistanceStart: 1\n    mipMapFadeDistanceEnd: 3\n  bumpmap:\n    convertToNormalMap: 0\n    externalNormalMap: 0\n    heightScale: 0.25\n    normalMapFilter: 0\n  isReadable: 0\n  streamingMipmaps: 0\n  streamingMipmapsPriority: 0\n  grayScaleToAlpha: 0\n  generateCubemap: 6\n  cubemapConvolution: 0\n  seamlessCubemap: 0\n  textureFormat: 1\n  maxTextureSize: 2048\n  textureSettings:\n    serializedVersion: 2\n    filterMode: -1\n    aniso: -1\n    mipBias: -100\n    wrapU: -1\n    wrapV: -1\n    wrapW: -1\n  nPOTScale: 1\n  lightmap: 0\n  compressionQuality: 50\n  spriteMode: 0\n  spriteExtrude: 1\n  spriteMeshType: 1\n  alignment: 0\n  spritePivot: {{x: 0.5, y: 0.5}}\n  spritePixelsToUnits: 100\n  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}\n  spriteGenerateFallbackPhysicsShape: 1\n  alphaUsage: 1\n  alphaIsTransparency: 0\n  spriteTessellationDetail: -1\n  textureType: 0\n  textureShape: 1\n  singleChannelComponent: 0\n  maxTextureSizeSet: 0\n  compressionQualitySet: 0\n  textureFormatSet: 0\n  applyGammaDecoding: 0\n  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 2048\n    resizeAlgorithm: 0\n    textureFormat: -1\n    textureCompression: 1\n    compressionQuality: 50\n    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n    overridden: 0\n    androidETC2FallbackOverride: 0\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n  spriteSheet:\n    serializedVersion: 2\n    sprites: []\n    outline: []\n    physicsShape: []\n    bones: []\n    spriteID:\n    internalID: 0\n    vertices: []\n    indices:\n    edges: []\n    weights: []\n    secondaryTextures: []\n  spritePackingTag:\n  pSDRemoveMatte: 0\n  pSDShowRemoveMatteOption: 0\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }


            if (extension == ".ttf" || extension == ".otf")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nTrueTypeFontImporter:\n  externalObjects: {{}}\n  serializedVersion: 4\n  fontSize: 16\n  forceTextureCase: -2\n  characterSpacing: 0\n  characterPadding: 1\n  includeFontData: 1\n  fontName:\n  fontNames:\n  - \n  fallbackFontReferences: []\n  customCharacters:\n  fontRenderingMode: 0\n  ascentCalculationMode: 1\n  useLegacyBoundsCalculation: 0\n  shouldRoundAdvanceValue: 1\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }


            if (extension == ".uxml" || extension == ".uss")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {{}}\n  serializedVersion: 2\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n  script: {{fileID: 13804, guid: 0000000000000000e000000000000000, type: 0}}\n";
            }


            if (extension == ".svg")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {{}}\n  serializedVersion: 2\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n  script: {{fileID: 11500000, guid: a57477913897c46af91b7aeb59411556, type: 3}}\n  svgType: 0\n  texturedSpriteMeshType: 0\n  svgPixelsPerUnit: 100\n  gradientResolution: 64\n  alignment: 0\n  customPivot: {{x: 0, y: 0}}\n  generatePhysicsShape: 0\n  viewportOptions: 0\n  preserveViewport: 0\n  advancedMode: 0\n  predefinedResolutionIndex: 1\n  targetResolution: 1080\n  resolutionMultiplier: 1\n  stepDistance: 10\n  samplingStepDistance: 100\n  maxCordDeviationEnabled: 0\n  maxCordDeviation: 1\n  maxTangentAngleEnabled: 0\n  maxTangentAngle: 5\n  keepTextureAspectRatio: 1\n  textureSize: 256\n  textureWidth: 256\n  textureHeight: 256\n  wrapMode: 0\n  filterMode: 1\n  sampleCount: 4\n  preserveSVGImageAspect: 0\n  useSVGPixelsPerUnit: 0\n  meshCompression: 0\n  spriteData:\n    name:\n    originalName:\n    pivot: {{x: 0, y: 0}}\n    border: {{x: 0, y: 0, z: 0, w: 0}}\n    rect:\n      serializedVersion: 2\n      x: 0\n      y: 0\n      width: 0\n      height: 0\n    alignment: 0\n    tessellationDetail: 0\n    bones: []\n    spriteID:\n    internalID: 0\n    vertices: []\n    indices:\n    edges: []\n    weights: []\n";
            }


            return $"fileFormatVersion: 2\nguid: {guid}\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
        }

        /// <summary>
        /// Export multiple profiles in sequence
        /// </summary>
        public static List<ExportResult> ExportMultiple(List<ExportProfile> profiles, Action<int, int, float, string> progressCallback = null)
        {
            var results = new List<ExportResult>();

            for (int i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];


                var result = ExportPackage(profile, (progress, status) =>
                {
                    progressCallback?.Invoke(i, profiles.Count, progress, status);
                });

                results.Add(result);

                if (!result.success)
                {
                    Debug.LogError($"[PackageBuilder] Export failed for profile '{profile.name}': {result.errorMessage}");
                }
            }

            return results;
        }

    }
}
