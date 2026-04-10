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
using YUCP.Importer.Editor.PackageManager;
using PackageVerifierData = YUCP.Importer.Editor.PackageVerifier.Data;
using YUCP.DevTools.Editor.PackageSigning.Core;
using PackageSigningData = YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Orchestrates the complete package export process including obfuscation and icon injection.
    /// Handles validation, folder filtering, DLL obfuscation, and final package creation.
    /// </summary>
    public static class PackageBuilder
    {
        internal static bool s_isExporting = false;
        private static string s_lastSigningFailureMessage;
        
        private const string DefaultGridPlaceholderPath = "Packages/com.yucp.devtools/Resources/DefaultGrid.png";
        private const string InstalledPackagesRoot = "Packages/yucp.installed-packages";
        private const string EmbeddedArtifactsFolderName = "Embedded";
        private const string TempInstallFolderName = "_temp";
        private const string TempPatchExportMarkerKey = "YUCP.PackageBuilder.ExportingTempPatchAssets";
        private const string PrecompiledInstallerRuntimePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Binaries/YUCP.DirectVpmInstaller.Template.dll";
        private const string PrecompiledInstallerRuntimeTargetFileName = "YUCP.DirectVpmInstaller.Template.dll";
        
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
            public string warningMessage;
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
            public readonly string EmbeddedRoot;
            public readonly string TempInstallRoot;
            public readonly List<EmbeddedAsset> Assets = new List<EmbeddedAsset>();
            public readonly HashSet<string> MetadataFallbackAssetPaths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public string IconPath;
            public string BannerPath;
            public readonly Dictionary<ProductLink, string> ProductLinkIconPaths = new Dictionary<ProductLink, string>();
            public readonly List<string> GalleryImagePaths = new List<string>();

            public PackageEmbedContext(ExportProfile profile)
            {
                string baseName = profile != null ? (profile.packageName ?? profile.profileName ?? profile.name) : "package";
                SafePackageName = MakeSafeFolderName(baseName);
                EmbeddedRoot = $"{InstalledPackagesRoot}/{SafePackageName}/{EmbeddedArtifactsFolderName}";
                TempInstallRoot = $"{InstalledPackagesRoot}/{SafePackageName}/{TempInstallFolderName}";
            }

            public string RegisterTextureForExport(Texture2D texture, string baseName, string subfolder, out string exportAssetPath)
            {
                exportAssetPath = null;
                if (texture == null) return null;

                // Always export metadata textures as embedded PNGs when possible. This avoids
                // importer-side decoding failures for source formats like GIF and makes package
                // metadata self-contained instead of depending on original asset paths.
                string cachePath = SaveTextureToLibraryCache(texture, baseName);
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

        private sealed class SigningRequestResult
        {
            public PackageSigningData.SigningResponse response;
            public string rawError;
            public string normalizedError;
            public long responseCode;
        }

        private sealed class ProtectedAssetRegistration
        {
            public string protectedAssetId;
            public string unlockMode;
            public string wrappedContentKey;
            public string contentKeyBase64;
            public string contentHash;
            public string displayName;
        }

        private sealed class ProtectedPayloadPreparedExport
        {
            public ExportProfile profile;
        }

        private sealed class ProtectedPayloadExportArtifact
        {
            public string tempFilePath;
            public ProtectedPayloadDescriptor protectedPayload;
        }

        private sealed class PatchRuntimeInjectedFile
        {
            public string sourcePath;
            public string targetPath;
            public bool rewritePatchRuntimeNamespace = true;
        }

        private static readonly PatchRuntimeInjectedFile[] s_patchRuntimeInjectedFiles = new[]
        {
            new PatchRuntimeInjectedFile
            {
                sourcePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Data/DerivedFbxAsset.cs",
                targetPath = "Packages/com.yucp.temp/Editor/DerivedFbxAsset.cs",
            },
            new PatchRuntimeInjectedFile
            {
                sourcePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Core/MetaFileManager.cs",
                targetPath = "Packages/com.yucp.temp/Editor/MetaFileManager.cs",
            },
            new PatchRuntimeInjectedFile
            {
                sourcePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Core/DerivedFbxBuilder.cs",
                targetPath = "Packages/com.yucp.temp/Editor/DerivedFbxBuilder.cs",
            },
            new PatchRuntimeInjectedFile
            {
                sourcePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Core/EmbeddedTextEncodingUtility.cs",
                targetPath = "Packages/com.yucp.temp/Editor/EmbeddedTextEncodingUtility.cs",
            },
            new PatchRuntimeInjectedFile
            {
                sourcePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Core/ProtectedContentKeyUtility.cs",
                targetPath = "Packages/com.yucp.temp/Editor/ProtectedContentKeyUtility.cs",
            },
            new PatchRuntimeInjectedFile
            {
                sourcePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Core/HDiffPatchWrapper.cs",
                targetPath = "Packages/com.yucp.temp/Editor/HDiffPatchWrapper.cs",
            },
            new PatchRuntimeInjectedFile
            {
                sourcePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Core/ManifestBuilder.cs",
                targetPath = "Packages/com.yucp.temp/Editor/ManifestBuilder.cs",
            },
            new PatchRuntimeInjectedFile
            {
                sourcePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Core/Correspondence/MapBuilder.cs",
                targetPath = "Packages/com.yucp.temp/Editor/MapBuilder.cs",
            },
            new PatchRuntimeInjectedFile
            {
                sourcePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Core/Backup/BackupManager.cs",
                targetPath = "Packages/com.yucp.temp/Editor/BackupManager.cs",
            },
            new PatchRuntimeInjectedFile
            {
                sourcePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Core/Validator.cs",
                targetPath = "Packages/com.yucp.temp/Editor/Validator.cs",
            },
            new PatchRuntimeInjectedFile
            {
                sourcePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Templates/YUCPPatchImporter.cs",
                targetPath = "Packages/com.yucp.temp/Editor/YUCPPatchImporter.cs",
            },
        };

        private static string CreateDeterministicInjectedGuid(string seed)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                string normalizedSeed = (seed ?? string.Empty)
                    .Replace('\\', '/')
                    .ToLowerInvariant();
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(normalizedSeed));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }

        private static string GetPatchRuntimeInjectedGuid(string targetPath)
        {
            return CreateDeterministicInjectedGuid($"yucp-patch-runtime:{targetPath}");
        }

        private static bool TryInjectPrecompiledInstallerRuntime(string tempExtractDir, string installerRoot)
        {
            if (!File.Exists(PrecompiledInstallerRuntimePath))
            {
                return false;
            }

            string dllGuid = CreateDeterministicInjectedGuid("yucp-precompiled-installer-runtime");
            string dllFolder = Path.Combine(tempExtractDir, dllGuid);
            Directory.CreateDirectory(dllFolder);

            File.Copy(PrecompiledInstallerRuntimePath, Path.Combine(dllFolder, "asset"), true);
            File.WriteAllText(
                Path.Combine(dllFolder, "pathname"),
                $"{installerRoot}/{PrecompiledInstallerRuntimeTargetFileName}");

            string metaPath = PrecompiledInstallerRuntimePath + ".meta";
            string metaContent = File.Exists(metaPath)
                ? File.ReadAllText(metaPath)
                : GenerateEditorOnlyDllMeta(dllGuid);
            File.WriteAllText(Path.Combine(dllFolder, "asset.meta"), metaContent);
            return true;
        }

        private static bool IsPatchRuntimeHandledByPrecompiledInstaller(PatchRuntimeInjectedFile patchScript)
        {
            return patchScript != null &&
                   string.Equals(
                       patchScript.targetPath,
                       "Packages/com.yucp.temp/Editor/YUCPPatchImporter.cs",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string GenerateEditorOnlyDllMeta(string guid)
        {
            return
                "fileFormatVersion: 2\n" +
                "guid: " + guid + "\n" +
                "PluginImporter:\n" +
                "  externalObjects: {}\n" +
                "  serializedVersion: 2\n" +
                "  iconMap: {}\n" +
                "  executionOrder: {}\n" +
                "  defineConstraints: []\n" +
                "  isPreloaded: 0\n" +
                "  isOverridable: 0\n" +
                "  isExplicitlyReferenced: 0\n" +
                "  validateReferences: 1\n" +
                "  platformData:\n" +
                "  - first:\n" +
                "      : Any\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        Exclude Editor: 0\n" +
                "        Exclude Linux64: 1\n" +
                "        Exclude OSXUniversal: 1\n" +
                "        Exclude Win: 0\n" +
                "        Exclude Win64: 0\n" +
                "  - first:\n" +
                "      Any: \n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings: {}\n" +
                "  - first:\n" +
                "      Editor: Editor\n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings:\n" +
                "        CPU: AnyCPU\n" +
                "        DefaultValueInitialized: true\n" +
                "        OS: AnyOS\n" +
                "  - first:\n" +
                "      Standalone: Linux64\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        CPU: None\n" +
                "  - first:\n" +
                "      Standalone: OSXUniversal\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        CPU: None\n" +
                "  - first:\n" +
                "      Standalone: Win\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        CPU: None\n" +
                "  - first:\n" +
                "      Standalone: Win64\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        CPU: None\n" +
                "  userData: \n" +
                "  assetBundleName: \n" +
                "  assetBundleVariant: \n";
        }

        private static bool ShouldBuildProtectedPayload(ExportProfile profile)
        {
            return profile != null &&
                   profile.UsesProtectedPayload();
        }

        private static bool UsesProtectedPayloadRuntime(ExportProfile profile)
        {
            return profile != null &&
                   profile.requiresLicenseVerification;
        }

        private static bool AnyProfileUsesProtectedPayloadRuntime(ExportProfile profile)
        {
            if (UsesProtectedPayloadRuntime(profile))
            {
                return true;
            }

            if (profile == null || !profile.HasIncludedProfiles())
            {
                return false;
            }

            foreach (var sub in profile.GetIncludedProfiles())
            {
                if (UsesProtectedPayloadRuntime(sub))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldRequireDerivedFbxServerUnlock(ExportProfile profile)
        {
            return UsesProtectedPayloadRuntime(profile) &&
                   !string.IsNullOrEmpty(profile.packageId);
        }

        private static ExportProfile CreateSyntheticProtectedPayloadProfile(ExportProfile containerProfile)
        {
            var payloadProfile = ScriptableObject.Instantiate(containerProfile);
            payloadProfile.skipProtectedPayloadBuild = true;

            return payloadProfile;
        }

        private static readonly List<ProtectedAssetRegistration> s_pendingProtectedAssetRegistrations = new List<ProtectedAssetRegistration>();
        
        /// <summary>
        /// Export a package using the provided profile
        /// </summary>
        public static ExportResult ExportPackage(ExportProfile profile, Action<float, string> progressCallback = null)
        {
            var result = new ExportResult();
            var startTime = DateTime.Now;
            s_pendingProtectedAssetRegistrations.Clear();
            
            // Set exporting flag
            s_isExporting = true;
            EditorPrefs.SetBool(TempPatchExportMarkerKey, true);
            
            try
            {
                // Save all assets before export
                progressCallback?.Invoke(0.01f, "Saving all project assets...");
                AssetDatabase.SaveAssets();
                
                // Validate profile
                progressCallback?.Invoke(0.05f, "Validating export profile...");
                if (!profile.Validate(out string errorMessage))
                {
                    result.success = false;
                    result.errorMessage = errorMessage;
                    return result;
                }
                
                // Check if this is a composite profile and resolve bundled profiles
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
                    
                    // Export bundled profiles separately if requested
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
                            
                            // Export the bundled profile
                            var bundledResult = ExportPackage(bundledProfile, (p, s) =>
                            {
                                // Scale progress to fit in the allocated range
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
                
                // Handle obfuscation if enabled
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
                
                // Build list of assets to export
                List<string> assetsToExport;
                
                if (profile.HasIncludedProfiles() && includedProfiles != null && includedProfiles.Count > 0)
                {
                    // Merge asset lists from composite profile
                    progressCallback?.Invoke(0.5f, $"Merging assets from bundled profiles...");
                    assetsToExport = CompositeProfileResolver.MergeAssetLists(
                        profile, includedProfiles, out assetSourceMap);
                    
                    if (assetSourceMap == null)
                        assetSourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    
                    progressCallback?.Invoke(0.52f, $"Merged {assetsToExport.Count} assets from {includedProfiles.Count} bundled profiles");
                }
                else
                {
                    // Original behavior: collect from folders only
                    progressCallback?.Invoke(0.5f, $"Collecting assets from {profile.foldersToExport.Count} folders...");
                    assetsToExport = CollectAssetsToExport(profile);
                }
                
                // Note: Derived FBX conversion happens after dependency collection so derived FBXs that are
                // only pulled in as dependencies still get converted into patch assets.
                bool hasPatchAssets = false;
                
                // Exclude .cs and .asmdef files from obfuscated assemblies (DLL will be included instead)
                if (profile.assembliesToObfuscate != null && profile.assembliesToObfuscate.Count > 0)
                {
                    var obfuscatedAsmdefPaths = profile.assembliesToObfuscate
                        .Where(a => a.enabled)
                        .Select(a => Path.GetFullPath(a.asmdefPath).Replace("\\", "/"))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    
                    assetsToExport = assetsToExport.Where(assetPath => {
                        string fullPath = Path.GetFullPath(assetPath).Replace("\\", "/");
                        string extension = Path.GetExtension(fullPath).ToLower();
                        
                        // Check if this file belongs to an obfuscated assembly
                        if (extension == ".cs" || extension == ".asmdef")
                        {
                            string fileDir = Path.GetDirectoryName(fullPath).Replace("\\", "/");
                            foreach (var asmdefPath in obfuscatedAsmdefPaths)
                            {
                                string asmdefDir = Path.GetDirectoryName(asmdefPath).Replace("\\", "/");
                                if (fileDir.StartsWith(asmdefDir, StringComparison.OrdinalIgnoreCase))
                                {
                                    return false; // Exclude this file
                                }
                            }
                        }
                        return true; // Include this file
                    }).ToList();
                }
                
                if (assetsToExport.Count == 0 && !profile.UsesProtectedPayload())
                {
                    result.success = false;
                    result.errorMessage = "No assets found to export";
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
                
                var embedContext = new PackageEmbedContext(profile);
                List<ProtectedPayloadPreparedExport> preparedProtectedPayloads = null;
                List<ProtectedPayloadExportArtifact> protectedPayloadArtifacts = null;

                if (profile.icon != null && !IsDefaultGridPlaceholder(profile.icon))
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

                preparedProtectedPayloads = PrepareProtectedPayloadsForExport(profile);
                bool usesSyntheticLicensedPayload = preparedProtectedPayloads != null &&
                    preparedProtectedPayloads.Count > 0;
                if (usesSyntheticLicensedPayload && string.IsNullOrEmpty(profile.packageId))
                {
                    PackageIdManager.AssignPackageId(profile);
                }

                if (profile.banner != null)
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

                // Add product link icons to export or embed (both customIcon and auto-fetched icon)
                if (profile.productLinks != null)
                {
                    foreach (var link in profile.productLinks)
                    {
                        Texture2D iconToAdd = link.customIcon ?? link.icon;
                        string exportAssetPath;
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
                        else if (!string.IsNullOrEmpty(link.cachedIconPath) && File.Exists(link.cachedIconPath))
                        {
                            exportAssetPath = null;
                            linkIconPath = embedContext.RegisterEmbeddedFile(link.cachedIconPath, "ProductLinks", link.label ?? "ProductLink");
                        }
                        else
                        {
                            exportAssetPath = null;
                        }

                        if (!string.IsNullOrEmpty(exportAssetPath) && !assetsToExport.Contains(exportAssetPath))
                        {
                            assetsToExport.Add(exportAssetPath);
                            Debug.Log($"[PackageBuilder] Added product link icon to export: {exportAssetPath} (source: {(link.customIcon != null ? "customIcon" : "icon")})");
                        }

                        if (!string.IsNullOrEmpty(linkIconPath))
                        {
                            embedContext.ProductLinkIconPaths[link] = linkIconPath;
                        }
                    }
                }

                // Embed gallery images for storefront display
                if (profile.galleryImages != null)
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
                
                // Manually collect dependencies if enabled (respects ignore list)
                CollectFilteredDependencies(assetsToExport, profile, progressCallback);
                
                // Protected-payload exports build their real derived patch assets inside the synthetic payload export.
                // The outer shell should stay metadata-only and must not create a second competing patch authoring pass.
                if (profile.UsesProtectedPayload())
                {
                    hasPatchAssets = false;
                }
                else
                {
                    // Convert derived FBXs after dependencies are collected so derived FBXs referenced only by exported
                    // assets (i.e., not directly in export folders) still get converted into patch artifacts.
                    hasPatchAssets = ConvertDerivedFbxToPatchAssets(assetsToExport, progressCallback, progress: 0.535f, profile: profile);
                }
                
                progressCallback?.Invoke(0.54f, $"Total assets after dependency collection: {assetsToExport.Count}");
                
                // Track bundled dependencies to inject later (AssetDatabase.ExportPackage can't handle files without .meta)
                var bundledPackagePaths = new Dictionary<string, string>(); // packageName -> packagePath
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
                
                 // Generate package.json if needed (will inject later)
                 string packageJsonContent = null;
                 if (profile.generatePackageJson)
                 {
                     progressCallback?.Invoke(0.58f, "Generating package.json...");

                     // Collect all profiles (main + bundled) to check if any emit protected runtime content.
                     bool anyProfileRequiresLicense = AnyProfileUsesProtectedPayloadRuntime(profile);

                     // Build effective dependency list — inject com.yucp.importer when any profile emits protected runtime content.
                     var effectiveDeps = new List<PackageDependency>(profile.dependencies ?? new List<PackageDependency>());
                     if (anyProfileRequiresLicense)
                     {
                          bool alreadyListed = effectiveDeps.Any(d => d.packageName == "com.yucp.importer");
                         if (!alreadyListed)
                         {
                            effectiveDeps.Add(new PackageDependency
                            {
                                packageName = "com.yucp.importer",
                                packageVersion = "0.1.0",
                                specificVersion = "0.1.0",
                                versionMode = DependencyVersionMode.Latest,
                                displayName = "YUCP Importer",
                                enabled = true,
                                exportMode = DependencyExportMode.Dependency,
                                isVpmDependency = true,
                            });
                         }
                     }
                     
                     packageJsonContent = DependencyScanner.GeneratePackageJson(
                         profile,
                         effectiveDeps,
                         null
                     );
                 }
                
                result.filesExported = assetsToExport.Count;                
                // Create temp package path
                progressCallback?.Invoke(0.6f, "Exporting Unity package...");
                
                string tempPackagePath = Path.Combine(Path.GetTempPath(), $"YUCP_Temp_{Guid.NewGuid():N}.unitypackage");
                
                 ExportPackageOptions options = ExportPackageOptions.Default;
                 if (profile.recurseFolders)
                     options |= ExportPackageOptions.Recurse;
                
                 // Convert all assets to Unity-relative paths and validate
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
                     
                     // Try to load the asset
                     var loadedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityPath);
                     if (loadedAsset != null)
                     {
                         validAssets.Add(unityPath);
                     }
                     else
                     {
                         // For Packages paths, check if file exists physically as fallback
                         // (Unity might not have imported it yet, but it exists on disk)
                         if (unityPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                         {
                             string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                             string physicalPath = Path.Combine(projectPath, unityPath.Replace('/', Path.DirectorySeparatorChar));
                             if (File.Exists(physicalPath) || Directory.Exists(physicalPath))
                             {
                                 // For .asset and .cs files in Packages, allow them if file exists
                                 // Unity will export the file itself even if not fully imported
                                 if (unityPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || 
                                     unityPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                     unityPath.EndsWith(".hdiff", StringComparison.OrdinalIgnoreCase) ||
                                     unityPath.EndsWith(".hdiff.enc", StringComparison.OrdinalIgnoreCase))
                                 {
                                     // File exists, add it - Unity will export it
                                     validAssets.Add(unityPath);
                                 }
                                 else
                                 {
                                     // For other files, try importing
                                     AssetDatabase.ImportAsset(unityPath, ImportAssetOptions.ForceSynchronousImport);
                                     AssetDatabase.Refresh();
                                     
                                     // Try loading again
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
                
                if (validAssets.Count == 0 && bundledPackagePaths.Count == 0 && !profile.UsesProtectedPayload())
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

                 if (usesSyntheticLicensedPayload)
                 {
                      Debug.Log("[PackageBuilder] Licensed profile export will emit a bootstrap container shell and move the licensed payload into a synthetic protected blob.");
                      finalValidAssets = finalValidAssets
                          .Where(assetPath => embedContext.MetadataFallbackAssetPaths.Contains(assetPath))
                          .ToList();
                      bundledPackagePaths.Clear();
                 }
                
                // Only specific DerivedFbxAsset files are added
                // are added via patchAssetsToAdd in ConvertDerivedFbxToPatchAssets
                
                if (finalValidAssets.Count == 0 && !profile.UsesProtectedPayload())
                 {
                      throw new InvalidOperationException("No valid assets remain for export after final validation.");
                  }
                 
                progressCallback?.Invoke(0.65f, $"Writing package file ({finalValidAssets.Count} assets)... may take a moment.");
                
                 // Export the package
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
                 
                 // Wait for export to complete - Unity's export is synchronous but file I/O might be async
                 AssetDatabase.Refresh();
                 
                 // Wait for file to be created (with retry and longer delays)
                 int retryCount = 0;
                 while (!File.Exists(tempPackagePath) && retryCount < 30) // Increased to 30 attempts (6 seconds)
                 {
                     if (retryCount > 0)
                         progressCallback?.Invoke(0.69f, $"Verifying package file... (attempt {retryCount + 1}/30)");
                     System.Threading.Thread.Sleep(200); // Wait 200ms
                     retryCount++;
                 }
                 
                 progressCallback?.Invoke(0.7f, "Unity package export completed");
                 
                 // Verify the file was actually created
                 if (!File.Exists(tempPackagePath))
                 {
                     // Check if the temp directory is accessible
                     string tempDir = Path.GetDirectoryName(tempPackagePath);
                     Debug.LogError($"[PackageBuilder] Temp directory: {tempDir}");
                     Debug.LogError($"[PackageBuilder] Temp directory exists: {Directory.Exists(tempDir)}");
                     Debug.LogError($"[PackageBuilder] Temp directory writable: {CheckDirectoryWritable(tempDir)}");
                     
                     throw new FileNotFoundException($"Package export failed - temp file not created after retries: {tempPackagePath}");
                 }
                
                
                if (preparedProtectedPayloads != null && preparedProtectedPayloads.Count > 0)
                {
                    protectedPayloadArtifacts = BuildProtectedPayloadArtifacts(profile, preparedProtectedPayloads, embedContext, progressCallback);
                }

                if (string.IsNullOrEmpty(profile.packageId))
                {
                    PackageIdManager.AssignPackageId(profile);
                }

                ProtectedPayloadDescriptor protectedPayloadDescriptor = protectedPayloadArtifacts?
                    .Select(artifact => artifact?.protectedPayload)
                    .FirstOrDefault(descriptor => descriptor != null);

                // Generate package metadata JSON
                string packageMetadataJson = GeneratePackageMetadataJson(profile, embedContext, validAssets);
                string protectedPayloadJson = protectedPayloadDescriptor != null
                    ? JsonUtility.ToJson(protectedPayloadDescriptor, true)
                    : null;
                
                // Inject package.json, auto-installer, bundled packages, and metadata into the .unitypackage
                if (!string.IsNullOrEmpty(packageJsonContent) || bundledPackagePaths.Count > 0 || !string.IsNullOrEmpty(packageMetadataJson) || !string.IsNullOrEmpty(protectedPayloadJson))
                {
                    progressCallback?.Invoke(0.75f, "Injecting package.json, installer, bundled packages, and metadata...");
                    
                    try
                    {
                        // Pass obfuscated assemblies info so bundled packages can replace source with DLLs
                        var obfuscatedAssemblies = profile.enableObfuscation 
                            ? profile.assembliesToObfuscate.Where(a => a.enabled).ToList() 
                            : new List<AssemblyObfuscationSettings>();
                        
                        InjectPackageJsonInstallerAndBundles(tempPackagePath, packageJsonContent, bundledPackagePaths, obfuscatedAssemblies, profile, hasPatchAssets, packageMetadataJson, protectedPayloadJson, embedContext, progressCallback);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageBuilder] Failed to inject content: {ex.Message}");
                    }
                }
                
                // At this point tempPackagePath contains the full package contents
                // (all assets, package.json/installer/bundles/metadata), but no icon
                // and no signing data yet.
                
                // Get final output path
                string finalOutputPath = profile.GetOutputFilePath();
                
                // Ensure output directory exists
                string outputDir = Path.GetDirectoryName(finalOutputPath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                // We'll produce the final signed package at this path:
                string contentPackagePath = tempPackagePath;
                
                if (profile.icon != null && !IsDefaultGridPlaceholder(profile.icon))
                {
                    progressCallback?.Invoke(0.8f, "Adding package icon...");

                    string iconSourcePath = ResolvePackageIconSourcePath(profile.icon, profile.packageName ?? "PackageIcon");
                    if (!string.IsNullOrEmpty(iconSourcePath))
                    {
                        // Write icon-injected package to a new temp file, which becomes our content package
                        string iconTempPath = Path.Combine(Path.GetTempPath(), $"YUCP_ContentWithIcon_{Guid.NewGuid():N}.unitypackage");

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
                
                // Assign packageId if not already assigned (before signing)
                progressCallback?.Invoke(0.81f, "Assigning package ID...");
                string packageId = PackageIdManager.AssignPackageId(profile);
                if (string.IsNullOrEmpty(packageId))
                {
                    Debug.LogWarning("[PackageBuilder] Failed to assign packageId, continuing without it");
                }
                else
                {
                    Debug.Log($"[PackageBuilder] Using packageId: {packageId}");
                }

                // Sign package if certificate is available, using the fully-prepared contentPackagePath
                bool packageSigned = false;
                bool signingWasRequired = false;
                bool signingBlockedExport = false;
                string signingWarningMessage = null;
                s_lastSigningFailureMessage = null;
                try
                {
                    var signingSettings = GetSigningSettings();
                    if (signingSettings != null && signingSettings.HasValidCertificate())
                    {
                        signingWasRequired = true;
                        progressCallback?.Invoke(0.82f, "Signing package...");
                        packageSigned = SignPackageBeforeExport(
                            contentPackagePath,
                            profile,
                            progressCallback,
                            protectedPayloadArtifacts);
                        if (packageSigned)
                        {
                            progressCallback?.Invoke(0.84f, "Package signed successfully");
                        }
                        else
                        {
                            string signingFailureMessage = s_lastSigningFailureMessage;
                            if (ShouldAllowUnsignedExportAfterSigningFailure(signingFailureMessage))
                            {
                                signingWarningMessage = BuildUnsignedExportWarning(signingFailureMessage);
                                progressCallback?.Invoke(0.84f, "Signing unavailable, exporting unsigned package...");
                                Debug.LogWarning($"[PackageBuilder] {signingWarningMessage}");
                            }
                            else
                            {
                                signingBlockedExport = true;
                                Debug.LogWarning("[PackageBuilder] Package signing failed, canceling export");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageBuilder] Package signing error: {ex.Message}");
                    if (signingWasRequired)
                    {
                        string signingFailureMessage = $"Package signing failed before export completed: {ex.Message}";
                        s_lastSigningFailureMessage = signingFailureMessage;
                        if (ShouldAllowUnsignedExportAfterSigningFailure(signingFailureMessage))
                        {
                            signingWarningMessage = BuildUnsignedExportWarning(signingFailureMessage);
                            progressCallback?.Invoke(0.84f, "Signing unavailable, exporting unsigned package...");
                            Debug.LogWarning($"[PackageBuilder] {signingWarningMessage}");
                        }
                        else
                        {
                            signingBlockedExport = true;
                        }
                    }
                }

                if (signingBlockedExport)
                {
                    progressCallback?.Invoke(0.86f, "Signing requirements blocked export.");
                }
                else
                {
                    // Copy signed (or unsigned) content package to final location
                    progressCallback?.Invoke(0.86f, "Copying package to output location...");
                    File.Copy(contentPackagePath, finalOutputPath, true);
                }
                
                progressCallback?.Invoke(0.9f, "Cleaning up temporary files...");
                
                if (packageSigned)
                {
                    try
                    {
                        SignatureEmbedder.RemoveSigningData();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageBuilder] Failed to clean up signing data: {ex.Message}");
                    }
                }
                
                // Clean up temp package
                if (File.Exists(tempPackagePath))
                {
                    File.Delete(tempPackagePath);
                }

                if (protectedPayloadArtifacts != null)
                {
                    foreach (var artifact in protectedPayloadArtifacts)
                    {
                        if (artifact != null && !string.IsNullOrEmpty(artifact.tempFilePath) && File.Exists(artifact.tempFilePath))
                        {
                            File.Delete(artifact.tempFilePath);
                        }
                    }
                }
                
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
                        // Fallback: try physical deletion
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
                        // Fallback: try physical deletion
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
                
                // Refresh AssetDatabase to reflect deletions
                AssetDatabase.Refresh();
                
                // Restore original DLLs if obfuscation was used
                if (profile.enableObfuscation)
                {
                    progressCallback?.Invoke(0.95f, "Restoring original assemblies...");
                    ConfuserExManager.RestoreOriginalDlls(profile.assembliesToObfuscate);
                }

                if (signingBlockedExport)
                {
                    result.success = false;
                    result.errorMessage = !string.IsNullOrEmpty(s_lastSigningFailureMessage)
                        ? s_lastSigningFailureMessage
                        : "Package signing failed. Resolve the billing or certificate issue and try exporting again.";
                    result.buildTimeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;
                    return result;
                }
                
                progressCallback?.Invoke(0.98f, "Saving export statistics...");
                
                // Update profile statistics
                profile.RecordExport();
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                
                progressCallback?.Invoke(1.0f, "Export complete!");
                
                // Build result
                result.success = true;
                result.warningMessage = signingWarningMessage;
                
                // Track export for milestones
                try
                {
                    System.Type milestoneTrackerType = null;
                    
                    // Try to find the type by searching through all loaded assemblies
                    foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        milestoneTrackerType = assembly.GetType("YUCP.Components.Editor.SupportBanner.MilestoneTracker");
                        if (milestoneTrackerType != null)
                            break;
                    }
                    
                    if (milestoneTrackerType != null)
                    {
                        var incrementMethod = milestoneTrackerType.GetMethod("IncrementExportCount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (incrementMethod != null)
                        {
                            incrementMethod.Invoke(null, null);
                        }
                    }
                }
                catch (System.Exception)
                {
                    // Silently fail milestone tracking
                }
                result.outputPath = finalOutputPath;
                result.buildTimeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;
                
                // Register exported package in ExportedPackageRegistry
                if (!string.IsNullOrEmpty(packageId))
                {
                    try
                    {
                        progressCallback?.Invoke(0.99f, "Registering export...");
                        var exportedRegistry = ExportedPackageRegistry.GetOrCreate();
                        var signingSettings = GetSigningSettings();
                        string publisherId = signingSettings?.publisherId ?? "";
                        
                        // Compute archive hash for registration
                        string archiveSha256 = "";
                        try
                        {
                            using (var sha256 = System.Security.Cryptography.SHA256.Create())
                            using (var stream = File.OpenRead(finalOutputPath))
                            {
                                byte[] hash = sha256.ComputeHash(stream);
                                archiveSha256 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[PackageBuilder] Failed to compute archive hash for registry: {ex.Message}");
                        }
                        
                        exportedRegistry.RegisterExport(
                            packageId,
                            profile.packageName,
                            publisherId,
                            profile.version,
                            archiveSha256,
                            finalOutputPath
                        );
                        
                        Debug.Log($"[PackageBuilder] Registered export: {packageId} v{profile.version}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageBuilder] Failed to register export: {ex.Message}");
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Export failed: {ex.Message}");
                Debug.LogException(ex);
                
                // Restore original DLLs on error
                if (profile.enableObfuscation)
                {
                    try
                    {
                        ConfuserExManager.RestoreOriginalDlls(profile.assembliesToObfuscate);
                    }
                    catch
                    {
                        // Ignore restoration errors
                    }
                }
                
                 result.success = false;
                 result.errorMessage = ex.Message;
                 result.buildTimeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;
                 
                 return result;
            }
            finally
            {
                // Clear exporting flag
                s_isExporting = false;
                EditorPrefs.DeleteKey(TempPatchExportMarkerKey);
                s_pendingProtectedAssetRegistrations.Clear();
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
            
            // Gather derived FBXs in the export set
            var derivedFbxPaths = new List<string>();
            int fbxCount = 0;
            foreach (var assetPath in assetsToExport)
            {
                if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                fbxCount++;
                
                // Normalize path for AssetImporter (prefer relative)
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
                            // Store original path for removal (will normalize during removal)
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
            
            
            // Warn if there are FBXs that might need to be marked as derived
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
                
                // Normalize path for AssetImporter
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
                try { DerivedSettingsUtility.TryRead(importer, out settings); } catch { /* ignore */ }
                if (settings == null || !settings.isDerived) continue;
                
                // Multi-base mode: require at least one base GUID
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
                
                // Read the derived FBX GUID from its .meta file
                string physicalModifiedPath = Path.IsPathRooted(normalizedModifiedPath) 
                    ? normalizedModifiedPath 
                    : Path.Combine(projectPath, normalizedModifiedPath.Replace('/', Path.DirectorySeparatorChar));
                string derivedFbxGuid = MetaFileManager.ReadGuid(physicalModifiedPath);
                
                if (string.IsNullOrEmpty(derivedFbxGuid))
                {
                    Debug.LogWarning($"[PackageBuilder] Could not read GUID from derived FBX .meta file: {normalizedModifiedPath}. A new GUID will be generated on import.");
                    // Generate a fallback identifier from the path
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
                    
                    bool requiresServerUnlock = ShouldRequireDerivedFbxServerUnlock(profile);
                    bool builtEntries;
                    string protectedAssetId = string.Empty;
                    string wrappedContentKey = string.Empty;
                    List<DerivedFbxAsset.PatchEntry> entries;

                    if (requiresServerUnlock)
                    {
                        builtEntries = PatchBuilder.CreateServerProtectedPatchEntries(
                            basePaths,
                            baseGuids,
                            normalizedModifiedPath,
                            hints.friendlyName,
                            out _,
                            out entries,
                            out protectedAssetId,
                            out wrappedContentKey);
                    }
                    else
                    {
                        builtEntries = PatchBuilder.CreateEncryptedPatchEntries(
                            basePaths,
                            baseGuids,
                            normalizedModifiedPath,
                            hints.friendlyName,
                            out _,
                            out entries);
                    }

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

                    // Propagate license gate from the export profile
                    if (ShouldRequireDerivedFbxServerUnlock(profile))
                    {
                        derivedAsset.requiresLicense = true;
                        derivedAsset.licensePackageId = profile.packageId;
                        derivedAsset.requiresServerUnlock = true;
                        derivedAsset.protectedAssetId = protectedAssetId;

                        s_pendingProtectedAssetRegistrations.Add(new ProtectedAssetRegistration
                        {
                            protectedAssetId = protectedAssetId,
                            unlockMode = "wrapped_content_key",
                            wrappedContentKey = wrappedContentKey,
                            displayName = hints.friendlyName
                        });
                    }
                    
                    // Use derived FBX GUID as filename identifier
                    string fileName = $"DerivedFbxAsset_{derivedFbxGuid.Substring(0, 8)}_{SanitizeFileName(hints.friendlyName)}.asset";
                    string pkgPath = $"Packages/com.yucp.temp/Patches/{fileName}";
                    
                    // Delete existing file if it exists (same derived FBX = same patch asset file)
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
                        string derivedFbxAssetScriptGuid =
                            GetPatchRuntimeInjectedGuid("Packages/com.yucp.temp/Editor/DerivedFbxAsset.cs");
                        
                        if (!string.IsNullOrEmpty(derivedFbxAssetScriptGuid))
                        {
                            // Read the .asset file and update the script GUID and namespace references
                            string assetContent = File.ReadAllText(physicalAssetPath);
                            
                            var guidPattern = new System.Text.RegularExpressions.Regex(@"m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([a-f0-9]{32}),\s*type:\s*\d+\}");
                            if (guidPattern.IsMatch(assetContent))
                            {
                                assetContent = guidPattern.Replace(assetContent, $"m_Script: {{fileID: 11500000, guid: {derivedFbxAssetScriptGuid}, type: 3}}");
                            }
                            
                            // Replace namespace references for nested types (e.g., EmbeddedBlendshapeOp, EmbeddedMeshDeltaOp)
                            // Unity serializes nested types in YAML format. The format can be:
                            // - m_Type: YUCP.DevTools.Editor.PackageExporter.DerivedFbxAsset/EmbeddedBlendshapeOp, YUCP.PatchRuntime
                            // - type: YUCP.DevTools.Editor.PackageExporter.DerivedFbxAsset/EmbeddedBlendshapeOp
                            // - YUCP.DevTools.Editor.PackageExporter.DerivedFbxAsset/EmbeddedBlendshapeOp, YUCP.PatchRuntime
                            
                            string originalContent = assetContent;
                            
                            // Use simple string replacement first (most reliable)
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
                                    // Try one more aggressive replacement
                                    verifyContent = verifyContent.Replace("YUCP.DevTools", "YUCP.PatchRuntime");
                                    File.WriteAllText(physicalAssetPath, verifyContent);
                                }
                                
                                // Force Unity to reimport the asset so it recognizes the namespace changes
                                AssetDatabase.ImportAsset(pkgPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate | ImportAssetOptions.DontDownloadFromCacheServer);
                                
                                // Give Unity a moment to process the import
                                AssetDatabase.Refresh();
                            }
                            else
                            {
                                // Even if no changes detected, ensure asset is saved
                                File.WriteAllText(physicalAssetPath, assetContent);
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[PackageBuilder] Could not find DerivedFbxAsset.cs script GUID in temp package - asset may not load correctly");
                        }
                        
                        patchAssetsToAdd.Add(pkgPath);
                        
                        // Add encrypted diff files to export list
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
                    
                    // Mark FBX for removal from export list (binary patch replaces the FBX)
                    fbxToRemove.Add(modifiedPath);
                    
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PackageBuilder] Failed to build derived FBX for {modifiedPath}: {ex.Message}\n{ex.StackTrace}");
                    Debug.LogError($"[PackageBuilder] The FBX will be exported as-is instead of being converted to a derived FBX package. " +
                        $"Please check that the base FBX exists and both FBXs have compatible mesh structures.");
                    // Keep FBX if patch building completely failed - let user export the FBX as-is
                }
            }
            
            // Remove derived FBXs from export list
            if (fbxToRemove.Count > 0)
            {
                
                // Normalize paths for comparison (handle both absolute and relative)
                // Use a helper function to normalize consistently
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
            
            // Add PatchPackage and sidecars to export list
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
        
        // DerivedSettings is now defined in Data/DerivedSettings.cs
        
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
            
            // Create package.json for the temp package
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
            
            // Create .meta files if needed
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

        private static string ComputeFileSha256Hex(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static List<ProtectedPayloadPreparedExport> PrepareProtectedPayloadsForExport(ExportProfile profile)
        {
            var preparedPayloads = new List<ProtectedPayloadPreparedExport>();
            if (ShouldBuildProtectedPayload(profile))
            {
                preparedPayloads.Add(new ProtectedPayloadPreparedExport
                {
                    profile = profile
                });
            }

            return preparedPayloads;
        }

        private static List<ProtectedPayloadExportArtifact> BuildProtectedPayloadArtifacts(
            ExportProfile containerProfile,
            List<ProtectedPayloadPreparedExport> preparedPayloads,
            PackageEmbedContext embedContext,
            Action<float, string> progressCallback = null)
        {
            var artifacts = new List<ProtectedPayloadExportArtifact>();
            if (preparedPayloads == null || preparedPayloads.Count == 0)
                return artifacts;

            string tempOutputDir = Path.Combine(Path.GetTempPath(), $"YUCP_ProtectedPayload_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempOutputDir);

            try
            {
                for (int i = 0; i < preparedPayloads.Count; i++)
                {
                    var prepared = preparedPayloads[i];
                    if (prepared?.profile == null)
                        continue;

                    progressCallback?.Invoke(0.72f + (0.03f * (i + 1) / preparedPayloads.Count),
                        $"Building protected payload {i + 1}/{preparedPayloads.Count}: {prepared.profile.packageName}...");

                    var exportClone = CreateSyntheticProtectedPayloadProfile(prepared.profile);

                    if (string.IsNullOrEmpty(exportClone.packageId))
                    {
                        PackageIdManager.AssignPackageId(exportClone);
                    }

                    string childOutputDir = Path.Combine(tempOutputDir, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(childOutputDir);

                    exportClone.exportPath = childOutputDir;
                    exportClone.autoIncrementVersion = false;

                    var exportResult = ExportPackage(exportClone);
                    string exportedPackageId = exportClone.packageId ?? prepared.profile.packageId ?? "";
                    string exportedPackageName = exportClone.packageName ?? prepared.profile.packageName ?? prepared.profile.profileName ?? "";
                    string exportedVersion = exportClone.version ?? prepared.profile.version ?? "";
                    string exportedAuthor = exportClone.author ?? prepared.profile.author ?? "";
                    string exportedDescription = exportClone.description ?? prepared.profile.description ?? "";
                    string exportedTagline = exportClone.tagline ?? prepared.profile.tagline ?? "";
                    ScriptableObject.DestroyImmediate(exportClone);

                    if (!exportResult.success || string.IsNullOrEmpty(exportResult.outputPath) || !File.Exists(exportResult.outputPath))
                    {
                        throw new InvalidOperationException(
                            $"Failed to export protected payload source package '{prepared.profile.packageName}': {exportResult.errorMessage ?? "missing output"}");
                    }

                    var blobBuild = ProtectedPayloadBlobBuilder.BuildFromUnityPackage(
                        exportResult.outputPath,
                        exportedPackageId,
                        exportedPackageName);
                    string blobAssetPath = embedContext.RegisterEmbeddedFile(
                        blobBuild.blobFilePath,
                        "Protected",
                        exportedPackageName ?? prepared.profile.packageName ?? "ProtectedPayload");
                    if (string.IsNullOrEmpty(blobAssetPath))
                        throw new InvalidOperationException("Failed to register protected payload blob for export.");
                    blobBuild.descriptor.blobAssetPath = blobAssetPath ?? "";

                    s_pendingProtectedAssetRegistrations.Add(new ProtectedAssetRegistration
                    {
                        protectedAssetId = blobBuild.descriptor.protectedAssetId,
                        unlockMode = "content_key_b64",
                        contentKeyBase64 = blobBuild.contentKeyBase64,
                        contentHash = blobBuild.descriptor.ciphertextSha256,
                        displayName = exportedPackageName ?? prepared.profile.packageName ?? prepared.profile.profileName ?? "Protected Payload"
                    });

                    artifacts.Add(new ProtectedPayloadExportArtifact
                    {
                        tempFilePath = exportResult.outputPath,
                        protectedPayload = blobBuild.descriptor.Clone()
                    });
                }

                return artifacts;
            }
            catch
            {
                foreach (var artifact in artifacts)
                {
                    if (artifact != null && !string.IsNullOrEmpty(artifact.tempFilePath) && File.Exists(artifact.tempFilePath))
                        File.Delete(artifact.tempFilePath);
                }

                throw;
            }
        }
        
        /// <summary>
        /// Generate a package.json file for the export
        /// </summary>
        private static string GeneratePackageJson(ExportProfile profile)
        {
            try
            {
                string existingPackageJsonPath = null;
                foreach (string folder in profile.foldersToExport)
                {
                    string testPath = Path.Combine(folder, "package.json");
                    if (File.Exists(testPath))
                    {
                        existingPackageJsonPath = testPath;
                        break;
                    }
                }
                
                // Generate package.json content
                string packageJsonContent = DependencyScanner.GeneratePackageJson(
                    profile,
                    profile.dependencies,
                    existingPackageJsonPath
                );
                
                // If we have an existing package.json, update it in place
                if (!string.IsNullOrEmpty(existingPackageJsonPath))
                {
                    File.WriteAllText(existingPackageJsonPath, packageJsonContent);
                    AssetDatabase.Refresh();
                    return existingPackageJsonPath;
                }
                
                 if (profile.foldersToExport.Count > 0)
                 {
                     string tempPackageJsonPath = Path.Combine(profile.foldersToExport[0], "package.json");
                     
                     // Ensure the file is created with proper permissions and timestamp
                     File.WriteAllText(tempPackageJsonPath, packageJsonContent);
                     
                     // Force file system sync and refresh
                     File.SetLastWriteTime(tempPackageJsonPath, DateTime.Now);
                     AssetDatabase.Refresh();
                     
                     // Wait a moment for Unity to process the file
                     System.Threading.Thread.Sleep(100);
                     AssetDatabase.Refresh();
                     
                     return tempPackageJsonPath;
                 }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Failed to generate package.json: {ex.Message}");
                return null;
            }
        }

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
                string fileName = $"{sanitizedName}_{texture.GetInstanceID()}.png";
                string filePath = Path.Combine(tempDir, fileName);
                
                byte[] pngData = TryEncodeTextureToPng(texture, baseName);

                if (pngData == null || pngData.Length == 0)
                {
                    Debug.LogWarning($"[PackageBuilder] Failed to encode texture to PNG for '{baseName}'");
                    return null;
                }
                
                // Write PNG file
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

        private static string ResolvePackageIconSourcePath(Texture2D texture, string baseName)
        {
            if (texture == null)
            {
                return null;
            }

            string cachedPngPath = SaveTextureToLibraryCache(texture, baseName);
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
                    productLinks = new List<ProductLinkJson>(),
                    updateSteps = profile.updateSteps
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
                            Texture2D iconToUse = link.customIcon ?? link.icon;
                            if (iconToUse != null && embedContext != null)
                            {
                                iconPath = embedContext.RegisterTextureForExport(iconToUse, link.label ?? "ProductLink", "ProductLinks", out var _);
                            }
                            else if (!string.IsNullOrEmpty(link.cachedIconPath) && File.Exists(link.cachedIconPath) && embedContext != null)
                            {
                                iconPath = embedContext.RegisterEmbeddedFile(link.cachedIconPath, "ProductLinks", link.label ?? "ProductLink");
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

                // Embed license requirements for all profiles (main + bundled)
                var licensePackages = new List<LicensePackageJson>();
                void AddLicenseProfile(ExportProfile p)
                {
                    if (p != null && p.requiresLicenseVerification && !string.IsNullOrEmpty(p.packageId))
                    {
                        licensePackages.Add(new LicensePackageJson
                        {
                            packageId = p.packageId,
                            packageName = p.packageName ?? p.packageId,
                            productId = p.GetPrimaryLicenseProductId(),
                            gumroadPermalink = p.gumroadProductId ?? "",
                            jinxxyProductId = p.jinxxyProductId ?? "",
                            discordGuildId = p.licenseDiscordGuildId ?? "",
                            discordRoleId  = p.licenseDiscordRoleId  ?? "",
                            creatorAuthUserId = YUCP.DevTools.Editor.PackageSigning.Core.YucpOAuthService.GetUserId() ?? "",
                        });
                    }
                }
                AddLicenseProfile(profile);
                if (profile.HasIncludedProfiles())
                {
                    foreach (var sub in profile.GetIncludedProfiles())
                        AddLicenseProfile(sub);
                }
                if (licensePackages.Count > 0)
                    metadataJson.licensePackages = licensePackages;

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
            public UpdateStepList updateSteps;
            public List<FileHashJson> fileHashes;
            /// <summary>Per-profile license requirements embedded at export time.</summary>
            public List<LicensePackageJson> licensePackages;
            
            // Storefront metadata
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
            public List<AssetBreakdownJson> assetBreakdown;
            public string exportDate;
        }

        [Serializable]
        private class LicensePackageJson
        {
            public string packageId;
            public string packageName;
            public string productId;
            public string gumroadPermalink;
            public string jinxxyProductId;
            public string discordGuildId;
            public string discordRoleId;
            /// <summary>Creator's YUCP auth user ID — embedded so importer can query Discord entitlements.</summary>
            public string creatorAuthUserId;
        }

        [Serializable]
        private class ProductLinkJson
        {
            public string label;
            public string url;
            public string icon; // Path to custom icon texture
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
            // If already a Unity-relative path, return as-is
            if (absolutePath.StartsWith("Assets/") || absolutePath.StartsWith("Packages/"))
            {
                return absolutePath;
            }
            
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            
            // Normalize both paths for comparison (use forward slashes)
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
                
                // Only include assets that are explicitly marked as included AND not ignored
                var includedAssets = profile.discoveredAssets
                    .Where(a => a.included && !a.isFolder)
                    .Select(a => GetRelativePackagePath(a.assetPath))
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Where(assetPath => !ShouldExcludeAsset(assetPath, profile))
                    .ToList();
                
                
                return includedAssets;
            }
            
            foreach (string folder in profile.foldersToExport)
            {
                string assetFolder = folder;
                
                // Convert absolute path to relative path if needed
                if (Path.IsPathRooted(folder))
                {
                    // This is an absolute path - try to make it relative to current project
                    string currentProjectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string normalizedFolder = folder.Replace('\\', '/');
                    string normalizedProject = currentProjectPath.Replace('\\', '/');
                    
                    if (normalizedFolder.StartsWith(normalizedProject))
                    {
                        // Path is within current project
                        assetFolder = normalizedFolder.Substring(normalizedProject.Length + 1);
                    }
                    else
                    {
                        // Path is from a different project - try to use just the last part
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
                    // Relative path that doesn't start with Assets or Packages
                    assetFolder = Path.Combine("Assets", folder).Replace('\\', '/');
                }
                
                // Ensure we have a valid Unity path format
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
                    
                    // Try to create a .meta file for the folder
                    string metaPath = assetFolder + ".meta";
                    if (!File.Exists(metaPath))
                    {
                        try
                        {
                            // Create a basic .meta file
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
                    
                    // Apply exclusion filters
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
            // Always allow internal patch artifacts; these are required for derived FBX support even if the
            // user excludes most of `Packages/` from export.
            string unityLikePath = GetRelativePackagePath(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(unityLikePath) &&
                unityLikePath.StartsWith("Packages/com.yucp.temp/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            
            // Convert to full path for comprehensive exclusion checking
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullPath;
            
            // Handle both Unity-relative paths (Assets/...) and full paths
            if (Path.IsPathRooted(assetPath))
            {
                // Already a full path
                fullPath = Path.GetFullPath(assetPath);
            }
            else
            {
                // Unity-relative path - combine with project root
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
            
            // Build set of explicitly excluded paths from Export Inspector toggles (user unchecked items)
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
            const int maxDepth = 5; // Prevent infinite loops while still catching nested deps
            
            // Mark original assets as processed and add to queue for dependency collection
            foreach (var asset in assetsToExport)
            {
                string unityPath = GetRelativePackagePath(asset);
                if (!string.IsNullOrEmpty(unityPath))
                {
                    processedPaths.Add(unityPath);
                    toProcess.Enqueue(unityPath);
                }
            }
            
            // Recursively collect dependencies with depth limiting
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
                    // Get dependencies (non-recursive - we handle recursion manually)
                    string[] dependencies = AssetDatabase.GetDependencies(assetPath, recursive: false);
                    
                    foreach (string dep in dependencies)
                    {
                        // Skip self-reference
                        if (dep == assetPath)
                            continue;
                        
                        // Skip if already processed
                        if (processedPaths.Contains(dep))
                            continue;
                        
                        processedPaths.Add(dep);
                        
                        // Check if dependency should be excluded BEFORE adding to queue
                        if (ShouldExcludeAsset(dep, profile))
                        {
                            Debug.Log($"[PackageBuilder] Excluding dependency (in ignore list): {dep}");
                            continue;
                        }
                        
                        // Respect Export Inspector toggles: do not add dependencies the user explicitly unchecked
                        string depNormalized = dep.Replace('\\', '/');
                        if (explicitlyExcludedPaths.Contains(depNormalized))
                        {
                            Debug.Log($"[PackageBuilder] Excluding dependency (unchecked in Export Inspector): {dep}");
                            continue;
                        }
                        
                        // Add to dependencies and queue for further dependency collection
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
            
            // Add filtered dependencies to export list
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
            // Convert wildcard pattern to regex
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
            string protectedPayloadJson,
            PackageEmbedContext embedContext,
            Action<float, string> progressCallback = null)
        {
            // Unity packages are tar.gz archives
            // We need to:
            // 1. Extract the package
            // 2. Add package.json and DirectVpmInstaller.cs as new assets
            // 3. Recompress
            
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"YUCP_PackageExtract_{Guid.NewGuid():N}");
            
            try
            {
                // Create temp directory
                Directory.CreateDirectory(tempExtractDir);
                
                // Extract the .unitypackage (it's a tar.gz)
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
                using (var fileStream = File.OpenRead(unityPackagePath))
                using (var gzipStream = new ICSharpCode.SharpZipLib.GZip.GZipInputStream(fileStream))
                using (var tarArchive = ICSharpCode.SharpZipLib.Tar.TarArchive.CreateInputTarArchive(gzipStream, System.Text.Encoding.UTF8))
                {
                    tarArchive.ExtractContents(tempExtractDir);
                }
#else
                Debug.LogError("[PackageBuilder] ICSharpCode.SharpZipLib not available. Package injection disabled.");
                return;
#endif
                
                // 0. Disable main assets by default to avoid Unity overwriting existing files on import.
                // This only affects already-exported assets (not injected items below).
                bool enableCustomUpdateSteps = profile != null && profile.updateSteps != null && profile.updateSteps.enabled;
                if (enableCustomUpdateSteps)
                {
                    DisablePackageEntriesByDefault(tempExtractDir);
                }

                // 1. Inject package.json (temporary, will be deleted by installer)
                if (!string.IsNullOrEmpty(packageJsonContent))
                {
                    string packageJsonGuid = Guid.NewGuid().ToString("N");
                    string packageJsonFolder = Path.Combine(tempExtractDir, packageJsonGuid);
                    Directory.CreateDirectory(packageJsonFolder);
                    
                    File.WriteAllText(Path.Combine(packageJsonFolder, "asset"), packageJsonContent);
                    // Use a unique path
                    string tempInstallRoot = embedContext != null ? embedContext.TempInstallRoot : "Assets";
                    File.WriteAllText(Path.Combine(packageJsonFolder, "pathname"), $"{tempInstallRoot}/YUCP_TempInstall_{packageJsonGuid}.json");
                    
                    string packageJsonMeta = "fileFormatVersion: 2\nguid: " + packageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(packageJsonFolder, "asset.meta"), packageJsonMeta);
                }

                // 1b. Inject YUCP_PackageInfo.json (permanent metadata)
                if (!string.IsNullOrEmpty(packageMetadataJson))
                {
                    string metadataGuid = Guid.NewGuid().ToString("N");
                    string metadataFolder = Path.Combine(tempExtractDir, metadataGuid);
                    Directory.CreateDirectory(metadataFolder);
                    
                    // Write asset file
                    File.WriteAllText(Path.Combine(metadataFolder, "asset"), packageMetadataJson);
                    
                    // Write .meta file (TextScriptImporter)
                    string metaGuid = Guid.NewGuid().ToString("N");
                    string metaPath = Path.Combine(metadataFolder, "asset.meta");
                    string metaContent = $"fileFormatVersion: 2\nguid: {metaGuid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(metaPath, metaContent);
                    
                    // Write pathname (destination path in project)
                    string metadataPath = embedContext != null
                        ? $"{InstalledPackagesRoot}/{embedContext.SafePackageName}/YUCP_PackageInfo.json"
                        : "Assets/YUCP_PackageInfo.json";
                    File.WriteAllText(Path.Combine(metadataFolder, "pathname"), metadataPath);
                }

                if (!string.IsNullOrEmpty(protectedPayloadJson))
                {
                    string payloadGuid = Guid.NewGuid().ToString("N");
                    string payloadFolder = Path.Combine(tempExtractDir, payloadGuid);
                    Directory.CreateDirectory(payloadFolder);

                    File.WriteAllText(Path.Combine(payloadFolder, "asset"), protectedPayloadJson);
                    string metaPath = Path.Combine(payloadFolder, "asset.meta");
                    string metaContent = $"fileFormatVersion: 2\nguid: {Guid.NewGuid():N}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(metaPath, metaContent);

                    string payloadPath = embedContext != null
                        ? $"{InstalledPackagesRoot}/{embedContext.SafePackageName}/YUCP_ProtectedPayload.json"
                        : "Assets/YUCP_ProtectedPayload.json";
                    File.WriteAllText(Path.Combine(payloadFolder, "pathname"), payloadPath);
                }

                // 1c. Inject embedded assets (icons, banners, etc.)
                if (embedContext != null && embedContext.Assets.Count > 0)
                {
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

                // 1d. Ensure installed-packages container is a valid local package
                if (embedContext != null)
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
                
                // 2a. Inject Mini Package Guardian (permanent protection layer)
                //
                // Requirements:
                // - Must be fully self-contained (must NOT reference YUCP.Components.*), because it is bundled for projects
                //   that don't have com.yucp.components installed.
                // - Must NOT be injected if the exported package.json will install com.yucp.components (avoids conflicts).
                bool hasYucpComponentsDependency = profile.dependencies != null &&
                    profile.dependencies.Any(d => d.enabled && d.packageName == "com.yucp.components");

                bool packageJsonWillInstallYucpComponents =
                    !string.IsNullOrEmpty(packageJsonContent) &&
                    packageJsonContent.IndexOf("\"com.yucp.components\"", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!hasYucpComponentsDependency && !packageJsonWillInstallYucpComponents)
                {
                    string guardianTemplatePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Templates/PackageGuardianMini.cs";
                    string transactionTemplatePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Templates/GuardianTransaction.cs";

                    if (!File.Exists(guardianTemplatePath) || !File.Exists(transactionTemplatePath))
                    {
                        Debug.LogWarning("[PackageBuilder] Mini Guardian templates missing in com.yucp.devtools. Skipping yucp.packageguardian injection.");
                    }
                    else
                    {
                        // 1. Inject GuardianTransaction.cs (core dependency)
                        string transactionGuid = Guid.NewGuid().ToString("N");
                        string transactionFolder = Path.Combine(tempExtractDir, transactionGuid);
                        Directory.CreateDirectory(transactionFolder);

                        string transactionContent = File.ReadAllText(transactionTemplatePath);
                        File.WriteAllText(Path.Combine(transactionFolder, "asset"), transactionContent);
                        File.WriteAllText(Path.Combine(transactionFolder, "pathname"), "Packages/yucp.packageguardian/Editor/Core/Transactions/GuardianTransaction.cs");

                        string transactionMeta = "fileFormatVersion: 2\nguid: " + transactionGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(transactionFolder, "asset.meta"), transactionMeta);

                        // 2. Inject PackageGuardianMini.cs (self-contained implementation)
                        string guardianGuid = Guid.NewGuid().ToString("N");
                        string guardianFolder = Path.Combine(tempExtractDir, guardianGuid);
                        Directory.CreateDirectory(guardianFolder);

                        string guardianContent = File.ReadAllText(guardianTemplatePath);
                        File.WriteAllText(Path.Combine(guardianFolder, "asset"), guardianContent);
                        File.WriteAllText(Path.Combine(guardianFolder, "pathname"), "Packages/yucp.packageguardian/Editor/PackageGuardianMini.cs");

                        string guardianMeta = "fileFormatVersion: 2\nguid: " + guardianGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(guardianFolder, "asset.meta"), guardianMeta);

                        // 3. Create package.json for the guardian package
                        string guardianPackageJsonGuid = Guid.NewGuid().ToString("N");
                        string guardianPackageJsonFolder = Path.Combine(tempExtractDir, guardianPackageJsonGuid);
                        Directory.CreateDirectory(guardianPackageJsonFolder);

                        string guardianPackageJson = @"{
  ""name"": ""yucp.packageguardian"",
  ""displayName"": ""YUCP Package Guardian (Mini)"",
  ""version"": ""2.0.0"",
  ""description"": ""Lightweight import protection for YUCP packages. Resolves .yucp_disabled files and preserves meta GUIDs."",
  ""unity"": ""2019.4""
}";
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "asset"), guardianPackageJson);
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "pathname"), "Packages/yucp.packageguardian/package.json");

                        string guardianPackageJsonMeta = "fileFormatVersion: 2\nguid: " + guardianPackageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(guardianPackageJsonFolder, "asset.meta"), guardianPackageJsonMeta);
                    }
                }
                
                // 2b. Inject DirectVpmInstaller runtime
                string installerRoot = embedContext != null ? $"{InstalledPackagesRoot}/Editor" : "Assets/Editor";
                bool deferInstallerActivation = embedContext != null;
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
                        Debug.LogWarning("[PackageBuilder] Precompiled installer runtime was not available. Falling back to generated installer source injection.");
                    }
                }

                // Try to find the script in the package
                string installerScriptPath = null;
                string[] foundScripts = AssetDatabase.FindAssets("DirectVpmInstaller t:Script");
                
                if (foundScripts.Length > 0)
                {
                    installerScriptPath = AssetDatabase.GUIDToAssetPath(foundScripts[0]);
                }
                
                if (!usingPrecompiledInstallerRuntime &&
                    !string.IsNullOrEmpty(installerScriptPath) &&
                    File.Exists(installerScriptPath))
                {
                    string installerGuid = Guid.NewGuid().ToString("N");
                    string installerFolder = Path.Combine(tempExtractDir, installerGuid);
                    Directory.CreateDirectory(installerFolder);
                    
                    string installerDir = Path.GetDirectoryName(installerScriptPath);
                    if (deferInstallerActivation)
                    {
                        string preflightTemplatePath = Path.Combine(installerDir, "InstallerPreflight.cs");
                        if (File.Exists(preflightTemplatePath))
                        {
                            string preflightGuid = Guid.NewGuid().ToString("N");
                            string preflightFolder = Path.Combine(tempExtractDir, preflightGuid);
                            Directory.CreateDirectory(preflightFolder);

                            string preflightFileName = $"YUCP_InstallerPreflight_{installerGuid}.cs";
                            string preflightClassName = $"InstallerPreflight_{installerGuid}";
                            string preflightContent = File.ReadAllText(preflightTemplatePath)
                                .Replace("__YUCP_PREFLIGHT_CLASS__", preflightClassName)
                                .Replace("__YUCP_PREFLIGHT_FILE__", preflightFileName);

                            File.WriteAllText(Path.Combine(preflightFolder, "asset"), preflightContent);
                            File.WriteAllText(Path.Combine(preflightFolder, "pathname"), $"{installerRoot}/{preflightFileName}");

                            string preflightMeta = "fileFormatVersion: 2\nguid: " + preflightGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                            File.WriteAllText(Path.Combine(preflightFolder, "asset.meta"), preflightMeta);
                        }
                        else
                        {
                            Debug.LogWarning("[PackageBuilder] Could not find InstallerPreflight.cs template");
                        }
                    }

                    string installerContent = File.ReadAllText(installerScriptPath);
                    File.WriteAllText(Path.Combine(installerFolder, "asset"), installerContent);
                    string installerFileName = $"YUCP_Installer_{installerGuid}.cs";
                    string installerPathname = deferInstallerActivation
                        ? $"{installerRoot}/{installerFileName}.yucp_disabled"
                        : $"{installerRoot}/{installerFileName}";
                    File.WriteAllText(Path.Combine(installerFolder, "pathname"), installerPathname);
                    
                    string installerMeta = "fileFormatVersion: 2\nguid: " + installerGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(installerFolder, "asset.meta"), installerMeta);
                    
                    // Also inject the .asmdef to isolate the installer from compilation errors
                    string asmdefPath = Path.Combine(installerDir, "DirectVpmInstaller.asmdef");
                    
                    if (File.Exists(asmdefPath))
                    {
                        string asmdefGuid = Guid.NewGuid().ToString("N");
                        string asmdefFolder = Path.Combine(tempExtractDir, asmdefGuid);
                        Directory.CreateDirectory(asmdefFolder);
                        
                        string asmdefContent = File.ReadAllText(asmdefPath);
                        // Use a unique assembly name for the injected asmdef
                        // with any template asmdef included in com.yucp.components or previous installers
                        try
                        {
                            string uniqueAssemblyName = $"YUCP.DirectVpmInstaller.{installerGuid}";
                            asmdefContent = System.Text.RegularExpressions.Regex.Replace(
                                asmdefContent,
                                "\"name\"\\s*:\\s*\"[^\"]*\"",
                                "\"name\": \"" + uniqueAssemblyName + "\""
                            );
                        }
                        catch { /* best-effort replacement */ }
                        File.WriteAllText(Path.Combine(asmdefFolder, "asset"), asmdefContent);
                        string installerAsmdefName = $"YUCP_Installer_{installerGuid}.asmdef";
                        string installerAsmdefPathname = deferInstallerActivation
                            ? $"{installerRoot}/{installerAsmdefName}.yucp_disabled"
                            : $"{installerRoot}/{installerAsmdefName}";
                        File.WriteAllText(Path.Combine(asmdefFolder, "pathname"), installerAsmdefPathname);
                        
                        string asmdefMeta = "fileFormatVersion: 2\nguid: " + asmdefGuid + "\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                        File.WriteAllText(Path.Combine(asmdefFolder, "asset.meta"), asmdefMeta);
                        
                    }
                    
                    
                    // Also inject InstallerTransactionManager.cs (required dependency for InstallerTxn)
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
                    
                    // 2c. Inject InstallerHealthTools.cs (includes fix for self-referential dependencies)
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
                else if (!usingPrecompiledInstallerRuntime)
                {
                    Debug.LogWarning("[PackageBuilder] Could not find DirectVpmInstaller.cs template");
                }
                
                // 2a. Inject FullDomainReload.cs (helper for installer)
                string fullReloadScriptPath = null;
                string[] foundReloadScripts = AssetDatabase.FindAssets("FullDomainReload t:Script");
                
                if (foundReloadScripts.Length > 0)
                {
                    fullReloadScriptPath = AssetDatabase.GUIDToAssetPath(foundReloadScripts[0]);
                }
                
                if (!usingPrecompiledInstallerRuntime &&
                    !string.IsNullOrEmpty(fullReloadScriptPath) &&
                    File.Exists(fullReloadScriptPath))
                {
                    string reloadGuid = Guid.NewGuid().ToString("N");
                    string reloadFolder = Path.Combine(tempExtractDir, reloadGuid);
                    Directory.CreateDirectory(reloadFolder);
                    
                    string reloadContent = File.ReadAllText(fullReloadScriptPath);
                    File.WriteAllText(Path.Combine(reloadFolder, "asset"), reloadContent);
                    string reloadName = $"YUCP_FullDomainReload_{reloadGuid}.cs";
                    string reloadPathname = deferInstallerActivation
                        ? $"{installerRoot}/{reloadName}.yucp_disabled"
                        : $"{installerRoot}/{reloadName}";
                    File.WriteAllText(Path.Combine(reloadFolder, "pathname"), reloadPathname);
                    
                    string reloadMeta = "fileFormatVersion: 2\nguid: " + reloadGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(reloadFolder, "asset.meta"), reloadMeta);
                    
                }
                else if (!usingPrecompiledInstallerRuntime)
                {
                    Debug.LogWarning("[PackageBuilder] Could not find FullDomainReload.cs template");
                }
                
                // 2c. Inject patch runtime scripts if patch assets are present
                if (hasPatchAssets)
                {
                    progressCallback?.Invoke(0.70f, "Injecting derived FBX runtime scripts...");
                    
                    int injectedPatchScripts = 0;
                    foreach (var patchScript in s_patchRuntimeInjectedFiles)
                    {
                        if (usingPrecompiledInstallerRuntime && IsPatchRuntimeHandledByPrecompiledInstaller(patchScript))
                        {
                            continue;
                        }

                        string sourceScriptPath = null;
                        
                        // First, try direct path (most reliable)
                        if (File.Exists(patchScript.sourcePath))
                        {
                            sourceScriptPath = patchScript.sourcePath;
                        }
                        else
                        {
                            // Fallback: Try to find by filename using AssetDatabase
                            string fileName = Path.GetFileNameWithoutExtension(patchScript.sourcePath);
                            string[] patchFoundScripts = AssetDatabase.FindAssets($"{fileName} t:Script");
                            string normalizedRelativeSourcePath = patchScript.sourcePath
                                .Replace("Packages/com.yucp.devtools/", string.Empty)
                                .Replace("\\", "/");
                            
                            // Filter results to find the exact match
                            foreach (var guid in patchFoundScripts)
                            {
                                string foundPath = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
                                if (string.Equals(foundPath, patchScript.sourcePath, StringComparison.OrdinalIgnoreCase) ||
                                    foundPath.EndsWith(normalizedRelativeSourcePath, StringComparison.OrdinalIgnoreCase))
                                {
                                    sourceScriptPath = foundPath;
                                    break;
                                }
                            }
                            
                            // If still not found, use first result (best effort)
                            if (string.IsNullOrEmpty(sourceScriptPath) && patchFoundScripts.Length > 0)
                            {
                                sourceScriptPath = AssetDatabase.GUIDToAssetPath(patchFoundScripts[0]);
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(sourceScriptPath) && File.Exists(sourceScriptPath))
                        {
                            string scriptGuid = GetPatchRuntimeInjectedGuid(patchScript.targetPath);
                            string targetPath = patchScript.targetPath;
                            string scriptFolder = Path.Combine(tempExtractDir, scriptGuid);
                            Directory.CreateDirectory(scriptFolder);
                            
                            string scriptContent = File.ReadAllText(sourceScriptPath);
                            if (patchScript.rewritePatchRuntimeNamespace)
                            {
                                scriptContent = scriptContent.Replace(
                                    "namespace YUCP.DevTools.Editor.PackageExporter",
                                    "namespace YUCP.PatchRuntime"
                                );
                                scriptContent = scriptContent.Replace(
                                    "using YUCP.DevTools.Editor.PackageExporter",
                                    "using YUCP.PatchRuntime"
                                );
                            }
                            
                            File.WriteAllText(Path.Combine(scriptFolder, "asset"), scriptContent);
                            File.WriteAllText(Path.Combine(scriptFolder, "pathname"), targetPath);
                            
                            string scriptMeta = "fileFormatVersion: 2\nguid: " + scriptGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                            File.WriteAllText(Path.Combine(scriptFolder, "asset.meta"), scriptMeta);
                            
                            injectedPatchScripts++;
                        }
                        else
                        {
                            Debug.LogError($"[PackageBuilder] Could not find patch script: {patchScript.sourcePath}. Tried direct path and AssetDatabase search.");
                        }
                    }
                    
                    // Also inject package.json for com.yucp.temp (required for Unity to recognize it as a package)
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
                        // Create a default package.json if it doesn't exist
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
                    
                    // Create an assembly definition (.asmdef) file for the Editor scripts
                    string asmdefGuid = Guid.NewGuid().ToString("N");
                    string asmdefFolder = Path.Combine(tempExtractDir, asmdefGuid);
                    Directory.CreateDirectory(asmdefFolder);
                    
                    string asmdefContent = @"{
  ""name"": ""YUCP.PatchRuntime"",
  ""rootNamespace"": """",
  ""references"": [
    ""Unity.Formats.Fbx.Editor""
  ],
  ""includePlatforms"": [
    ""Editor""
  ],
  ""excludePlatforms"": [],
  ""allowUnsafeCode"": false,
  ""overrideReferences"": true,
  ""precompiledReferences"": [],
  ""autoReferenced"": true,
  ""defineConstraints"": [],
  ""versionDefines"": [
    {
      ""name"": ""com.unity.formats.fbx"",
      ""expression"": ""4.0.0"",
      ""define"": ""UNITY_FORMATS_FBX""
    }
  ],
  ""noEngineReferences"": false
}";
                    File.WriteAllText(Path.Combine(asmdefFolder, "asset"), asmdefContent);
                    File.WriteAllText(Path.Combine(asmdefFolder, "pathname"), "Packages/com.yucp.temp/Editor/YUCP.PatchRuntime.asmdef");
                    
                    string asmdefMeta = "fileFormatVersion: 2\nguid: " + asmdefGuid + "\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(asmdefFolder, "asset.meta"), asmdefMeta);
                    
                    
                    string[] hdiffDlls = new string[]
                    {
                        "Packages/com.yucp.devtools/Plugins/hdiffz.dll",
                        "Packages/com.yucp.devtools/Plugins/hpatchz.dll",
                        "Packages/com.yucp.devtools/Plugins/hdiffinfo.dll"
                    };
                    
                    foreach (var dllPath in hdiffDlls)
                    {
                        if (File.Exists(dllPath))
                        {
                            string dllGuid = Guid.NewGuid().ToString("N");
                            string dllFolder = Path.Combine(tempExtractDir, dllGuid);
                            Directory.CreateDirectory(dllFolder);
                            
                            string fileName = Path.GetFileName(dllPath);
                            string targetPath = $"Packages/com.yucp.temp/Plugins/{fileName}";
                            
                            File.Copy(dllPath, Path.Combine(dllFolder, "asset"), true);
                            File.WriteAllText(Path.Combine(dllFolder, "pathname"), targetPath);
                            
                            // Copy the .meta file if it exists
                            string metaPath = dllPath + ".meta";
                            if (File.Exists(metaPath))
                            {
                                string metaContent = File.ReadAllText(metaPath);
                                File.WriteAllText(Path.Combine(dllFolder, "asset.meta"), metaContent);
                            }
                            else
                            {
                                // Create a basic .meta file for the DLL
                                string dllMeta = "fileFormatVersion: 2\nguid: " + dllGuid + "\nPluginImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  iconMap: {}\n  executionOrder: {}\n  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n  - first:\n      : Any\n    second:\n      enabled: 0\n  - first:\n      Any: \n    second:\n      enabled: 1\n  - first:\n      Editor: Editor\n    second:\n      enabled: 1\n      settings:\n        CPU: AnyCPU\n        DefaultValueInitialized: true\n        OS: AnyOS\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
                                File.WriteAllText(Path.Combine(dllFolder, "asset.meta"), dllMeta);
                            }
                            
                            Debug.Log($"[PackageBuilder] Copied HDiffPatch DLL to temp package: {fileName}");
                        }
                        else
                        {
                            Debug.LogWarning($"[PackageBuilder] HDiffPatch DLL not found: {dllPath}");
                        }
                    }
                    
                    if (injectedPatchScripts > 0)
                    {
                    }
                    else
                    {
                        Debug.LogWarning("[PackageBuilder] Could not find patch runtime scripts - patch functionality will not work!");
                    }
                }
                
                // 3. Inject bundled packages (ALL files including those without .meta)
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
                        
                        // Build a set of obfuscated assembly names for this package (for quick lookup)
                        var obfuscatedAsmdefPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var obfuscatedDllPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // asmdefPath -> dllPath
                        
                        foreach (var obfuscatedAsm in obfuscatedAssemblies)
                        {
                            // Check if this obfuscated assembly belongs to the current bundled package
                            if (obfuscatedAsm.asmdefPath.Replace("\\", "/").Contains($"/{packageName}/"))
                            {
                                // Normalize path with forward slashes for consistent comparison
                                string normalizedAsmdefPath = Path.GetFullPath(obfuscatedAsm.asmdefPath).Replace("\\", "/");
                                obfuscatedAsmdefPaths.Add(normalizedAsmdefPath);
                                
                                // Get the obfuscated DLL path from Library/ScriptAssemblies
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
                        
                        // Track which asmdef directories have been processed
                        var processedAsmdefDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        
                        foreach (string filePath in allFiles)
                        {
                            // Skip .meta files
                            if (filePath.EndsWith(".meta"))
                                continue;
                            
                            // Calculate the relative path within the package
                            string relativePath = filePath.Substring(packagePath.Length).TrimStart('\\', '/');
                            
                            // Check if this file belongs to an obfuscated assembly
                            string fileDir = Path.GetDirectoryName(filePath);
                            string asmdefInDir = obfuscatedAsmdefPaths.FirstOrDefault(asmdefPath => 
                            {
                                string asmdefDir = Path.GetDirectoryName(asmdefPath);
                                return fileDir.Replace("\\", "/").StartsWith(asmdefDir.Replace("\\", "/"));
                            });
                            
                            bool isInObfuscatedAssembly = !string.IsNullOrEmpty(asmdefInDir);
                            
                            // Check if this is a script file that could cause compilation errors
                            string extension = Path.GetExtension(filePath).ToLower();
                            bool isCompilableScript = extension == ".cs" || extension == ".asmdef";
                            bool shouldDisableScript = isCompilableScript && enableCustomUpdateSteps;
                            
                            // Skip ConfuserEx project files
                            if (extension == ".crproj")
                            {
                                continue;
                            }
                            
                            // Skip .cs files if they belong to an obfuscated assembly (DLL will be added instead)
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
                            
                            // Skip .asmdef if it belongs to an obfuscated assembly (DLL doesn't need it)
                            if (extension == ".asmdef")
                            {
                                string asmdefFullPath = Path.GetFullPath(filePath).Replace("\\", "/");
                                
                                if (obfuscatedAsmdefPaths.Contains(asmdefFullPath))
                                {
                                    
                                    // Add the obfuscated DLL instead (only once per asmdef)
                                    if (!processedAsmdefDirs.Contains(asmdefFullPath))
                                    {
                                        processedAsmdefDirs.Add(asmdefFullPath);
                                        
                                        if (obfuscatedDllPaths.TryGetValue(asmdefFullPath, out string dllPath))
                                        {
                                            // Add the obfuscated DLL
                                            string dllFileName = Path.GetFileName(dllPath);
                                            string dllRelativePath = Path.Combine(Path.GetDirectoryName(relativePath), dllFileName).Replace("\\", "/");
                                            string dllUnityPathname = $"Packages/{packageName}/{dllRelativePath}";
                                            
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
                            if (shouldDisableScript)
                            {
                                unityPathname += ".yucp_disabled";
                            }
                            
                            // GUID handling strategy:
                            // - For .yucp_disabled files: Generate NEW GUID, but store ORIGINAL GUID in meta userData for restoration
                            // - For normal files: Preserve original GUID to maintain references
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
                            catch { /* best-effort */ }
                            
                            if (shouldDisableScript)
                            {
                                // Generate new GUID for disabled files (prevents GUID conflicts on re-import)
                                fileGuid = Guid.NewGuid().ToString("N");
                                // Store original GUID token so the installer/resolver can restore GUID on enable.
                                metaContent = GenerateMetaForFileWithOriginalGuid(filePath, fileGuid, originalGuid);
                            }
                            else if (File.Exists(originalMetaPath))
                            {
                                // Preserve original GUID for non-script files (safe, no renaming occurs)
                                string originalMeta = File.ReadAllText(originalMetaPath);
                                var guidMatch = System.Text.RegularExpressions.Regex.Match(originalMeta, @"guid:\s*([a-f0-9]{32})");
                                if (guidMatch.Success)
                                {
                                    fileGuid = guidMatch.Groups[1].Value;
                                    metaContent = originalMeta;
                                }
                            }
                            
                            // If no GUID found, generate new one
                            if (string.IsNullOrEmpty(fileGuid))
                            {
                                fileGuid = Guid.NewGuid().ToString("N");
                                metaContent = GenerateMetaForFile(filePath, fileGuid);
                            }
                            
                            string fileFolder = Path.Combine(tempExtractDir, fileGuid);
                            Directory.CreateDirectory(fileFolder);
                            
                            // Copy the actual file
                            File.Copy(filePath, Path.Combine(fileFolder, "asset"), true);
                            
                            // Write pathname
                            File.WriteAllText(Path.Combine(fileFolder, "pathname"), unityPathname);
                            
                            // Write .meta
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
                
                // Recompress the package
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
                string tempOutputPath = unityPackagePath + ".tmp";
                
                using (var outputStream = File.Create(tempOutputPath))
                using (var gzipStream = new ICSharpCode.SharpZipLib.GZip.GZipOutputStream(outputStream))
                using (var tarArchive = ICSharpCode.SharpZipLib.Tar.TarArchive.CreateOutputTarArchive(gzipStream, System.Text.Encoding.UTF8))
                {
                    tarArchive.RootPath = tempExtractDir.Replace('\\', '/');
                    if (tarArchive.RootPath.EndsWith("/"))
                        tarArchive.RootPath = tarArchive.RootPath.Remove(tarArchive.RootPath.Length - 1);
                    
                    AddDirectoryFilesToTar(tarArchive, tempExtractDir, true);
                }
                
                // Replace original with new package
                File.Delete(unityPackagePath);
                File.Move(tempOutputPath, unityPackagePath);
#endif
            }
            finally
            {
                // Clean up temp directory
                if (Directory.Exists(tempExtractDir))
                {
                    try
                    {
                        Directory.Delete(tempExtractDir, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }

        private static void DisablePackageEntriesByDefault(string extractDir)
        {
            try
            {
                foreach (string folder in Directory.GetDirectories(extractDir))
                {
                    string pathnameFile = Path.Combine(folder, "pathname");
                    string metaFile = Path.Combine(folder, "asset.meta");

                    if (!File.Exists(pathnameFile) || !File.Exists(metaFile))
                        continue;

                    string pathname = File.ReadAllText(pathnameFile).Trim();
                    if (string.IsNullOrEmpty(pathname))
                        continue;

                    // Skip folders (do not rename folder assets)
                    if (IsFolderMeta(metaFile))
                        continue;

                    // Only apply to project assets
                    if (!pathname.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                        !pathname.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Avoid double-suffixing
                    if (pathname.EndsWith(".yucp_disabled", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string normalizedPath = pathname.Replace('\\', '/');

                    // Keep embedded artifacts readable by the update system and UI
                    if (normalizedPath.IndexOf($"{InstalledPackagesRoot}/", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    // Back-compat: keep legacy metadata file readable
                    if (string.Equals(pathname, "Assets/YUCP_PackageInfo.json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Disable
                    File.WriteAllText(pathnameFile, pathname + ".yucp_disabled");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PackageBuilder] Failed to apply default disable pass: {ex.Message}");
            }
        }

        private static bool IsFolderMeta(string metaPath)
        {
            try
            {
                var content = File.ReadAllText(metaPath);
                return content.IndexOf("folderAsset: yes", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
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
        private static string GenerateMetaForFileWithOriginalGuid(string filePath, string guid, string originalGuid)
        {
            string baseMeta = GenerateMetaForFile(filePath, guid);

            if (string.IsNullOrEmpty(originalGuid))
                return baseMeta;

            // Store original GUID in a Unity-stable string format.
            // Unity expects userData to be a string; inline YAML objects may be dropped.
            string token = $"YUCP_ORIGINAL_GUID={originalGuid}";

            baseMeta = System.Text.RegularExpressions.Regex.Replace(
                baseMeta,
                @"(\s+)userData:\s*\r?\n",
                $"$1userData: {token}\n",
                System.Text.RegularExpressions.RegexOptions.Multiline
            );

            return baseMeta;
        }

        /// <summary>
        /// Generate appropriate .meta file content using file extension
        /// </summary>
        private static string GenerateMetaForFile(string filePath, string guid)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            
            // C# scripts
            if (extension == ".cs")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Assembly definitions
            if (extension == ".asmdef")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nAssemblyDefinitionImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Text files (.md, .txt, .json, etc.)
            if (extension == ".md" || extension == ".txt" || extension == ".json" || extension == ".xml")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Compute shaders
            if (extension == ".compute")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nComputeShaderImporter:\n  externalObjects: {{}}\n  currentAPIMask: 4\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Shader files
            if (extension == ".shader" || extension == ".cginc" || extension == ".hlsl")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nShaderImporter:\n  externalObjects: {{}}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Images
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nTextureImporter:\n  internalIDToNameTable: []\n  externalObjects: {{}}\n  serializedVersion: 11\n  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 1\n    sRGBTexture: 1\n    linearTexture: 0\n    fadeOut: 0\n    borderMipMap: 0\n    mipMapsPreserveCoverage: 0\n    alphaTestReferenceValue: 0.5\n    mipMapFadeDistanceStart: 1\n    mipMapFadeDistanceEnd: 3\n  bumpmap:\n    convertToNormalMap: 0\n    externalNormalMap: 0\n    heightScale: 0.25\n    normalMapFilter: 0\n  isReadable: 0\n  streamingMipmaps: 0\n  streamingMipmapsPriority: 0\n  grayScaleToAlpha: 0\n  generateCubemap: 6\n  cubemapConvolution: 0\n  seamlessCubemap: 0\n  textureFormat: 1\n  maxTextureSize: 2048\n  textureSettings:\n    serializedVersion: 2\n    filterMode: -1\n    aniso: -1\n    mipBias: -100\n    wrapU: -1\n    wrapV: -1\n    wrapW: -1\n  nPOTScale: 1\n  lightmap: 0\n  compressionQuality: 50\n  spriteMode: 0\n  spriteExtrude: 1\n  spriteMeshType: 1\n  alignment: 0\n  spritePivot: {{x: 0.5, y: 0.5}}\n  spritePixelsToUnits: 100\n  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}\n  spriteGenerateFallbackPhysicsShape: 1\n  alphaUsage: 1\n  alphaIsTransparency: 0\n  spriteTessellationDetail: -1\n  textureType: 0\n  textureShape: 1\n  singleChannelComponent: 0\n  maxTextureSizeSet: 0\n  compressionQualitySet: 0\n  textureFormatSet: 0\n  applyGammaDecoding: 0\n  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 2048\n    resizeAlgorithm: 0\n    textureFormat: -1\n    textureCompression: 1\n    compressionQuality: 50\n    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n    overridden: 0\n    androidETC2FallbackOverride: 0\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n  spriteSheet:\n    serializedVersion: 2\n    sprites: []\n    outline: []\n    physicsShape: []\n    bones: []\n    spriteID:\n    internalID: 0\n    vertices: []\n    indices:\n    edges: []\n    weights: []\n    secondaryTextures: []\n  spritePackingTag:\n  pSDRemoveMatte: 0\n  pSDShowRemoveMatteOption: 0\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // Fonts
            if (extension == ".ttf" || extension == ".otf")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nTrueTypeFontImporter:\n  externalObjects: {{}}\n  serializedVersion: 4\n  fontSize: 16\n  forceTextureCase: -2\n  characterSpacing: 0\n  characterPadding: 1\n  includeFontData: 1\n  fontName:\n  fontNames:\n  - \n  fallbackFontReferences: []\n  customCharacters:\n  fontRenderingMode: 0\n  ascentCalculationMode: 1\n  useLegacyBoundsCalculation: 0\n  shouldRoundAdvanceValue: 1\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
            }
            
            // UI Elements (.uxml, .uss)
            if (extension == ".uxml" || extension == ".uss")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {{}}\n  serializedVersion: 2\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n  script: {{fileID: 13804, guid: 0000000000000000e000000000000000, type: 0}}\n";
            }
            
            // SVG files
            if (extension == ".svg")
            {
                return $"fileFormatVersion: 2\nguid: {guid}\nScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {{}}\n  serializedVersion: 2\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n  script: {{fileID: 11500000, guid: a57477913897c46af91b7aeb59411556, type: 3}}\n  svgType: 0\n  texturedSpriteMeshType: 0\n  svgPixelsPerUnit: 100\n  gradientResolution: 64\n  alignment: 0\n  customPivot: {{x: 0, y: 0}}\n  generatePhysicsShape: 0\n  viewportOptions: 0\n  preserveViewport: 0\n  advancedMode: 0\n  predefinedResolutionIndex: 1\n  targetResolution: 1080\n  resolutionMultiplier: 1\n  stepDistance: 10\n  samplingStepDistance: 100\n  maxCordDeviationEnabled: 0\n  maxCordDeviation: 1\n  maxTangentAngleEnabled: 0\n  maxTangentAngle: 5\n  keepTextureAspectRatio: 1\n  textureSize: 256\n  textureWidth: 256\n  textureHeight: 256\n  wrapMode: 0\n  filterMode: 1\n  sampleCount: 4\n  preserveSVGImageAspect: 0\n  useSVGPixelsPerUnit: 0\n  meshCompression: 0\n  spriteData:\n    name:\n    originalName:\n    pivot: {{x: 0, y: 0}}\n    border: {{x: 0, y: 0, z: 0, w: 0}}\n    rect:\n      serializedVersion: 2\n      x: 0\n      y: 0\n      width: 0\n      height: 0\n    alignment: 0\n    tessellationDetail: 0\n    bones: []\n    spriteID:\n    internalID: 0\n    vertices: []\n    indices:\n    edges: []\n    weights: []\n";
            }
            
            // Default for unknown file types
            return $"fileFormatVersion: 2\nguid: {guid}\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
        }
        
        /// <summary>
        /// Sign package before final export - creates manifest, gets signature from server, and embeds it
        /// </summary>
        private static bool SignPackageBeforeExport(
            string packagePath,
            ExportProfile profile,
            Action<float, string> progressCallback,
            List<ProtectedPayloadExportArtifact> protectedPayloadArtifacts = null)
        {
            try
            {
                var settings = GetSigningSettings();
                string resolvedServerUrl = !string.IsNullOrEmpty(profile?.signingServerUrl)
                    ? profile.signingServerUrl
                    : settings?.serverUrl;
                bool shouldAutoManageCertificate =
                    settings != null &&
                    !string.IsNullOrEmpty(resolvedServerUrl) &&
                    YucpOAuthService.IsSignedIn();

                if (shouldAutoManageCertificate &&
                    (!settings.HasValidCertificate() || !IsStoredCertificateForCurrentDevKey(settings)))
                {
                    if (!TryRepairSigningCertificateForExport(
                            resolvedServerUrl,
                            settings,
                            progressCallback,
                            out string repairError,
                            out string rawRepairError))
                    {
                        s_lastSigningFailureMessage = repairError;
                        if (!string.IsNullOrEmpty(rawRepairError))
                        {
                            Debug.LogWarning($"[PackageBuilder] Raw certificate repair failure: {rawRepairError}");
                        }
                        return false;
                    }

                    settings = GetSigningSettings();
                }

                if (settings == null || !settings.HasValidCertificate())
                {
                    return false;
                }

                var certificate = CertificateManager.GetCurrentCertificate();
                if (certificate == null)
                {
                    Debug.LogWarning("[PackageBuilder] No certificate found for signing");
                    return false;
                }

                // Verify dev key matches certificate
                string currentDevPublicKey = DevKeyManager.GetPublicKeyBase64();
                if (certificate.cert.devPublicKey != currentDevPublicKey)
                {
                    string repairError = null;
                    string rawRepairError = null;
                    if (!shouldAutoManageCertificate ||
                        !TryRepairSigningCertificateForExport(
                            resolvedServerUrl,
                            settings,
                            progressCallback,
                            out repairError,
                            out rawRepairError))
                    {
                        Debug.LogError($"[PackageBuilder] Dev public key mismatch! Certificate expects: {certificate.cert.devPublicKey}, Current: {currentDevPublicKey}");
                        Debug.LogError("[PackageBuilder] The certificate was issued for a different dev key. Please regenerate or import the correct certificate.");
                        s_lastSigningFailureMessage = repairError ?? "The stored signing certificate belongs to a different device key. Restore or request a certificate for this machine before exporting again.";
                        if (!string.IsNullOrEmpty(rawRepairError))
                        {
                            Debug.LogWarning($"[PackageBuilder] Raw certificate repair failure: {rawRepairError}");
                        }
                        return false;
                    }

                    settings = GetSigningSettings();
                    certificate = CertificateManager.GetCurrentCertificate();
                    currentDevPublicKey = DevKeyManager.GetPublicKeyBase64();
                    if (certificate == null || certificate.cert.devPublicKey != currentDevPublicKey)
                    {
                        s_lastSigningFailureMessage = "The signing certificate could not be refreshed for the current device.";
                        return false;
                    }
                }

                Debug.Log($"[PackageBuilder] Dev public key matches certificate: {currentDevPublicKey}");

                progressCallback?.Invoke(0.821f, "Computing package hash...");
                
                // Compute archive SHA-256 using canonical content hashing:
                // - Decompress .unitypackage
                // - Enumerate all assets
                // - Ignore Assets/_Signing/*
                // - Hash (pathname UTF8 + 0x00 + asset bytes) in sorted pathname order
                string archiveSha256 = ComputeArchiveHashExcludingSigningData(packagePath);

                progressCallback?.Invoke(0.722f, "Building manifest...");

                // Build manifest (use packageId if available, otherwise fallback to packageName)
                string manifestPackageId = !string.IsNullOrEmpty(profile.packageId) ? profile.packageId : profile.packageName;
                var manifest = YUCP.DevTools.Editor.PackageSigning.Core.ManifestBuilder.BuildManifest(
                    packagePath,
                    manifestPackageId,
                    profile.version,
                    settings.publisherId,
                    settings.vrchatUserId,
                    profile.gumroadProductId,
                    profile.jinxxyProductId
                );
                
                // Override the hash computed by BuildManifest (which also computes it from packagePath)
                // We want to use the hash we just computed to ensure consistency
                manifest.archiveSha256 = archiveSha256;
                if (protectedPayloadArtifacts != null && protectedPayloadArtifacts.Count > 0)
                {
                    manifest.protectedPayloads = protectedPayloadArtifacts
                        .Select(artifact => artifact?.protectedPayload)
                        .Where(descriptor => descriptor != null)
                        .Select(ProtectedPayloadIntegrityUtility.CreateManifestEntry)
                        .Where(entry => entry != null)
                        .ToArray();
                }

                progressCallback?.Invoke(0.723f, "Preparing signing metadata...");

                SigningRequestResult SubmitSigningRequest(
                    PackageSigningData.YucpCertificate activeCertificate,
                    PackageSigningData.SigningSettings activeSettings,
                    bool suppressFailureLog = false)
                {
                    progressCallback?.Invoke(0.724f, "Sending signing request to server...");
                    return SendSigningRequestSynchronously(
                        resolvedServerUrl,
                        activeSettings.GetCertificateJson(),
                        manifest?.packageId ?? "",
                        profile.packageName ?? "",
                        manifest?.archiveSha256 ?? "",
                        manifest?.version ?? "",
                        s_pendingProtectedAssetRegistrations,
                        progressCallback,
                        suppressFailureLog
                    );
                }

                SigningRequestResult signingResult = SubmitSigningRequest(
                    certificate,
                    settings,
                    suppressFailureLog: shouldAutoManageCertificate
                );

                if (signingResult?.response == null)
                {
                    if (shouldAutoManageCertificate && LooksLikeRepairableSigningFailure(signingResult))
                    {
                        if (TryRepairSigningCertificateForExport(
                                resolvedServerUrl,
                                settings,
                                progressCallback,
                                out _,
                                out string rawRepairError))
                        {
                            settings = GetSigningSettings();
                            certificate = CertificateManager.GetCurrentCertificate();
                            if (settings != null &&
                                certificate != null &&
                                settings.HasValidCertificate() &&
                                certificate.cert.devPublicKey == DevKeyManager.GetPublicKeyBase64())
                            {
                                progressCallback?.Invoke(0.724f, "Retrying signing with refreshed certificate...");
                                signingResult = SubmitSigningRequest(certificate, settings);
                            }
                        }
                        else if (!string.IsNullOrEmpty(rawRepairError))
                        {
                            Debug.LogWarning($"[PackageBuilder] Raw certificate repair failure: {rawRepairError}");
                        }
                    }
                }

                if (signingResult?.response == null)
                {
                    string friendlyError = signingResult?.normalizedError ?? "Signing failed before the server returned a signature.";
                    s_lastSigningFailureMessage = friendlyError;
                    Debug.LogWarning($"[PackageBuilder] Package signing failed: {friendlyError}");
                    if (!string.IsNullOrEmpty(signingResult?.rawError))
                    {
                        Debug.LogWarning($"[PackageBuilder] Raw signing failure: {signingResult.rawError}");
                    }
                    return false;
                }

                var signingResponse = signingResult.response;

                if (signingResponse.certificateChain == null || signingResponse.certificateChain.Length == 0)
                {
                    s_lastSigningFailureMessage = "Signing failed because the server did not return a certificate chain.";
                    Debug.LogWarning("[PackageBuilder] Signing response did not include a certificate chain.");
                    return false;
                }

                manifest.certificateChain = signingResponse.certificateChain;

                progressCallback?.Invoke(0.726f, "Signing manifest with device key...");

                PackageSigningData.SignatureData signatureData =
                    CreateLocalManifestSignatureData(manifest, signingResponse);

                progressCallback?.Invoke(0.728f, "Embedding signature in package...");

                SignatureEmbedder.EmbedSigningData(manifest, signatureData);
                
                // Inject signing data into the package
                InjectSigningDataIntoPackage(packagePath);
                s_lastSigningFailureMessage = null;

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Signing error: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private static bool IsStoredCertificateForCurrentDevKey(PackageSigningData.SigningSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.devPublicKey))
            {
                return false;
            }

            try
            {
                string currentDevPublicKey = DevKeyManager.GetPublicKeyBase64();
                return !string.IsNullOrEmpty(currentDevPublicKey) &&
                    string.Equals(settings.devPublicKey, currentDevPublicKey, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeRepairableSigningFailure(SigningRequestResult signingResult)
        {
            string message = $"{signingResult?.normalizedError ?? ""}\n{signingResult?.rawError ?? ""}";
            return message.IndexOf("Certificate has been revoked", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("restore the correct certificate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("Signing authentication failed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldAllowUnsignedExportAfterSigningFailure(string signingFailureMessage)
        {
            if (string.IsNullOrWhiteSpace(signingFailureMessage))
                return false;

            string message = signingFailureMessage.Trim();

            if (message.IndexOf("network error while requesting a package signature", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (message.IndexOf("server returned HTML", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (message.IndexOf("HTTP 5", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (message.StartsWith("HTTP 0", StringComparison.OrdinalIgnoreCase))
                return true;

            string[] availabilityHints =
            {
                "cannot connect",
                "destination host",
                "host unreachable",
                "timed out",
                "timeout",
                "cannot resolve",
                "could not resolve",
                "name resolution",
                "no internet",
                "offline",
                "connection refused",
                "connection failed",
                "failed to connect",
                "temporarily unavailable",
                "service unavailable",
                "gateway timeout",
                "bad gateway",
                "dns"
            };

            return availabilityHints.Any(hint =>
                message.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string BuildUnsignedExportWarning(string signingFailureMessage)
        {
            if (string.IsNullOrWhiteSpace(signingFailureMessage))
                return "Package was exported unsigned because the signing service was unavailable.";

            return $"Package was exported unsigned because signing was unavailable: {signingFailureMessage}";
        }

        private static string AppendAccountCertificatesUrlIfNeeded(
            string message,
            PackageSigningData.SigningSettings settings)
        {
            if (string.IsNullOrEmpty(message) ||
                message.IndexOf("Certificates & Billing", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return message;
            }

            string accountUrl = settings?.GetEffectiveAccountCertificatesUrl();
            if (string.IsNullOrEmpty(accountUrl) ||
                message.IndexOf(accountUrl, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return message;
            }

            return $"{message} Open: {accountUrl}";
        }

        private static bool TryRepairSigningCertificateForExport(
            string resolvedServerUrl,
            PackageSigningData.SigningSettings settings,
            Action<float, string> progressCallback,
            out string friendlyError,
            out string rawError)
        {
            friendlyError = null;
            rawError = null;

            if (settings == null || string.IsNullOrEmpty(resolvedServerUrl))
            {
                friendlyError = "Certificate repair is unavailable because the signing server is not configured.";
                return false;
            }

            string accessToken = YucpOAuthService.GetAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                friendlyError = "Your creator session expired. Sign in again from Package Signing, then retry the export.";
                return false;
            }

            string devPublicKey;
            try
            {
                devPublicKey = DevKeyManager.GetPublicKeyBase64();
            }
            catch (Exception ex)
            {
                friendlyError = $"Could not load the current device key: {ex.Message}";
                rawError = ex.Message;
                return false;
            }

            if (string.IsNullOrEmpty(devPublicKey))
            {
                friendlyError = "Could not load the current device key for this project.";
                return false;
            }

            string publisherName = YucpOAuthService.GetDisplayName();
            if (string.IsNullOrEmpty(publisherName))
            {
                publisherName = settings.publisherName;
            }
            if (string.IsNullOrEmpty(publisherName))
            {
                publisherName = "YUCP Creator";
            }

            var service = new PackageSigningService(resolvedServerUrl);
            progressCallback?.Invoke(0.7242f, "Refreshing signing certificate...");

            var accountState = service.GetCertificateAccountState(accessToken, devPublicKey);
            if (accountState?.billing != null)
            {
                if (!accountState.billing.allowSigning && accountState.currentDeviceKnown)
                {
                    friendlyError = AppendAccountCertificatesUrlIfNeeded(
                        accountState.error ?? "This machine cannot restore its certificate until billing is fixed.",
                        settings);
                    rawError = accountState.error;
                    return false;
                }

                if (!accountState.billing.allowEnrollment && !accountState.currentDeviceKnown)
                {
                    friendlyError = AppendAccountCertificatesUrlIfNeeded(
                        accountState.error ?? "This machine cannot enroll a certificate right now.",
                        settings);
                    rawError = accountState.error;
                    return false;
                }

                if (accountState.deviceCapReachedForCurrentMachine)
                {
                    friendlyError = AppendAccountCertificatesUrlIfNeeded(
                        accountState.error ?? "This plan has no free device slots for this machine.",
                        settings);
                    rawError = accountState.error;
                    return false;
                }
            }

            string certJson = service.RestoreCertificate(accessToken, devPublicKey);
            if (string.IsNullOrEmpty(certJson))
            {
                var requestResult = service.RequestCertificate(accessToken, devPublicKey, publisherName);
                if (!requestResult.success || string.IsNullOrEmpty(requestResult.certJson))
                {
                    rawError = requestResult.error;
                    friendlyError = AppendAccountCertificatesUrlIfNeeded(
                        PackageSigningService.NormalizeCertificateRequestError(
                            requestResult.responseCode,
                            requestResult.error ?? "Certificate request failed.",
                            accountState?.currentDeviceKnown == true),
                        settings);
                    return false;
                }

                certJson = requestResult.certJson;
            }

            var importResult = CertificateManager.ImportAndVerifyFromJson(certJson);
            if (!importResult.valid)
            {
                rawError = importResult.error;
                friendlyError = $"Certificate repair failed: {importResult.error}";
                return false;
            }

            progressCallback?.Invoke(0.7248f, "Signing certificate refreshed.");
            return true;
        }

        /// <summary>
        /// Canonicalize signing payload to match server's expected format
        /// Uses recursive canonicalization like PackageInfoService (server expects this)
        /// </summary>
        private static string CanonicalizeSigningPayload(SigningRequestPayload payload)
        {
            // Use recursive canonicalization to match server's format
            return CanonicalizeJsonRecursive(payload);
        }

        /// <summary>
        /// Recursively canonicalize JSON to match server's format
        /// Sorts keys alphabetically at all levels (like PackageInfoService)
        /// </summary>
        private static string CanonicalizeJsonRecursive(object obj)
        {
            if (obj == null)
            {
                return "null";
            }
            
            var objType = obj.GetType();
            
            // Handle dictionaries (must come before IList check)
            if (obj is System.Collections.IDictionary dict)
            {
                // Skip empty dictionaries (server omits them)
                if (dict.Count == 0)
                {
                    return "{}"; // Return empty object, but caller should skip this field
                }
                
                var items = new List<string>();
                var keys = new List<object>();
                foreach (var key in dict.Keys)
                {
                    keys.Add(key);
                }
                
                // Sort keys alphabetically (convert to string for comparison)
                keys.Sort((a, b) => string.Compare(a?.ToString() ?? "", b?.ToString() ?? "", StringComparison.Ordinal));
                
                foreach (var key in keys)
                {
                    var value = dict[key];
                    var keyStr = EscapeJsonString(key?.ToString() ?? "");
                    var jsonValue = CanonicalizeJsonRecursive(value);
                    items.Add($"\"{keyStr}\":{jsonValue}");
                }
                return "{" + string.Join(",", items) + "}";
            }
            
            // Handle arrays and lists
            if (objType.IsArray)
            {
                var array = (Array)obj;
                var items = new List<string>();
                foreach (var item in array)
                {
                    items.Add(CanonicalizeJsonRecursive(item));
                }
                return "[" + string.Join(",", items) + "]";
            }
            
            if (obj is System.Collections.IList list)
            {
                var items = new List<string>();
                foreach (var item in list)
                {
                    items.Add(CanonicalizeJsonRecursive(item));
                }
                return "[" + string.Join(",", items) + "]";
            }
            
            // Handle objects (serializable classes)
            if (objType.IsClass && !objType.IsPrimitive && objType != typeof(string))
            {
                // Get all serializable fields
                var fields = objType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .Where(f => !f.IsStatic)
                    .OrderBy(f => f.Name)
                    .ToList();
                
                var items = new List<string>();
                foreach (var field in fields)
                {
                    var value = field.GetValue(obj);
                    
                    // Include all values (null becomes "null", empty dicts become "{}")
                    // Server's canonicalizeJson includes all keys, even if null or empty
                    var key = EscapeJsonString(field.Name);
                    var jsonValue = CanonicalizeJsonRecursive(value);
                    items.Add($"\"{key}\":{jsonValue}");
                }
                return "{" + string.Join(",", items) + "}";
            }
            
            // Handle primitives
            if (obj is string str)
            {
                return $"\"{EscapeJsonString(str)}\"";
            }
            
            if (obj is bool b)
            {
                return b ? "true" : "false";
            }
            
            if (obj is int || obj is long || obj is short || obj is byte || obj is uint || obj is ulong || obj is ushort || obj is sbyte)
            {
                return obj.ToString();
            }
            
            if (obj is float || obj is double || obj is decimal)
            {
                return obj.ToString();
            }
            
            // Fallback to JSON serialization
            return JsonUtility.ToJson(obj);
        }

        /// <summary>
        /// Escape JSON string
        /// </summary>
        private static string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }

        private static string BuildSigningProofPayload(
            string certNonce,
            string packageId,
            string contentHash,
            string packageVersion,
            string requestNonce,
            long requestTimestamp)
        {
            return string.Join("\n", new[]
            {
                "yucp-signature-proof-v1",
                certNonce ?? "",
                packageId ?? "",
                contentHash ?? "",
                packageVersion ?? "",
                requestNonce ?? "",
                requestTimestamp.ToString()
            });
        }

        private static PackageSigningData.SigningResponse ParseSigningResponseJson(string responseJson)
        {
            return SigningResponseParser.Parse(responseJson, "PackageBuilder");
        }

        private static PackageSigningData.SignatureData CreateLocalManifestSignatureData(
            PackageSigningData.PackageManifest manifest,
            PackageSigningData.SigningResponse signingResponse)
        {
            if (manifest == null)
                throw new InvalidOperationException("Manifest is required for local signing.");
            if (signingResponse?.certificateChain == null || signingResponse.certificateChain.Length == 0)
                throw new InvalidOperationException("Certificate chain is required for local signing.");

            int certificateIndex = signingResponse.certificateIndex;
            if (certificateIndex < 0 || certificateIndex >= signingResponse.certificateChain.Length)
                certificateIndex = 0;

            string canonicalManifest = CanonicalizeManifestForSignature(manifest);
            byte[] manifestBytes = Encoding.UTF8.GetBytes(canonicalManifest);
            byte[] manifestSignature = DevKeyManager.SignData(manifestBytes);

            return new PackageSigningData.SignatureData
            {
                algorithm = "Ed25519",
                keyId = signingResponse.certificateChain[certificateIndex]?.keyId ?? signingResponse.keyId ?? string.Empty,
                signature = Convert.ToBase64String(manifestSignature),
                certificateIndex = certificateIndex,
            };
        }

        private static string CanonicalizeManifestForSignature(PackageSigningData.PackageManifest manifest)
        {
            if (manifest == null)
                return "null";

            return CanonicalizeManifestValue(manifest);
        }

        private static string CanonicalizeManifestValue(object obj)
        {
            if (obj == null)
                return "null";

            var objType = obj.GetType();

            if (obj is System.Collections.IList list)
            {
                var items = new List<string>();
                foreach (var item in list)
                {
                    items.Add(CanonicalizeManifestValue(item));
                }
                return "[" + string.Join(",", items) + "]";
            }

            if (obj is System.Collections.IDictionary dict)
            {
                var sortedKeys = new List<string>();
                foreach (var key in dict.Keys)
                {
                    sortedKeys.Add(key?.ToString() ?? "");
                }
                sortedKeys.Sort(StringComparer.Ordinal);

                var items = new List<string>();
                foreach (var key in sortedKeys)
                {
                    items.Add($"\"{EscapeJsonString(key)}\":{CanonicalizeManifestValue(dict[key])}");
                }
                return "{" + string.Join(",", items) + "}";
            }

            if (objType.IsClass && objType != typeof(string))
            {
                var items = new List<string>();
                var fields = objType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (fields != null && fields.Length > 0)
                {
                    var sortedFields = new List<System.Reflection.FieldInfo>(fields);
                    sortedFields.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

                    foreach (var field in sortedFields)
                    {
                        var value = field.GetValue(obj);
                        if (value == null &&
                            field.FieldType.IsGenericType &&
                            field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        {
                            var dictType = typeof(Dictionary<,>).MakeGenericType(field.FieldType.GetGenericArguments());
                            value = Activator.CreateInstance(dictType);
                        }

                        items.Add($"\"{field.Name}\":{CanonicalizeManifestValue(value)}");
                    }
                }
                else
                {
                    var properties = objType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    var sortedProps = new List<System.Reflection.PropertyInfo>(properties);
                    sortedProps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

                    foreach (var prop in sortedProps)
                    {
                        var value = prop.GetValue(obj);
                        if (value == null &&
                            prop.PropertyType.IsGenericType &&
                            prop.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        {
                            var dictType = typeof(Dictionary<,>).MakeGenericType(prop.PropertyType.GetGenericArguments());
                            value = Activator.CreateInstance(dictType);
                        }

                        items.Add($"\"{prop.Name}\":{CanonicalizeManifestValue(value)}");
                    }
                }

                return "{" + string.Join(",", items) + "}";
            }

            if (obj is string str)
            {
                return "\"" + EscapeJsonString(str) + "\"";
            }

            if (obj is bool b)
            {
                return b ? "true" : "false";
            }

            if (objType.IsEnum)
            {
                return "\"" + EscapeJsonString(obj.ToString()) + "\"";
            }

            if (obj is IConvertible && !(obj is string))
            {
                return Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture);
            }

            return "\"" + EscapeJsonString(obj?.ToString() ?? string.Empty) + "\"";
        }

        /// <summary>
        /// Send signing request to server synchronously.
        /// Calls POST /v1/signatures (transparency log). The server authenticates via the YUCP
        /// certificate envelope as a Bearer token and records the package hash.
        /// Returns certificate-chain metadata from the server, then the manifest itself is signed
        /// locally with the active device key.
        /// </summary>
        private static SigningRequestResult SendSigningRequestSynchronously(
            string serverUrl,
            string rawCertJson,
            string packageId,
            string packageName,
            string contentHash,
            string packageVersion,
            IReadOnlyList<ProtectedAssetRegistration> protectedAssets,
            Action<float, string> progressCallback,
            bool suppressFailureLog = false)
        {
            try
            {
                if (string.IsNullOrEmpty(rawCertJson))
                {
                    Debug.LogError("[PackageBuilder] No certificate JSON available");
                    return new SigningRequestResult
                    {
                        rawError = "No certificate JSON available.",
                        normalizedError = "No certificate is available for signing. Restore or request a certificate before exporting a signed package.",
                    };
                }

                // POST /v1/signatures
                // Auth: Bearer <base64(raw cert JSON as UTF-8)>
                // The server uses atob() then JSON.parse() so we must base64-encode the raw
                // JSON string exactly as it was stored (which is what the CA signed).
                string certBase64 = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rawCertJson));
                var parsedCert = JsonUtility.FromJson<PackageSigningData.YucpCertificate>(rawCertJson);
                if (string.IsNullOrEmpty(parsedCert?.cert?.nonce))
                {
                    Debug.LogError("[PackageBuilder] Certificate nonce missing");
                    return new SigningRequestResult
                    {
                        rawError = "Certificate nonce missing.",
                        normalizedError = "The stored certificate is incomplete. Restore the correct certificate before exporting again.",
                    };
                }

                string requestNonce = Guid.NewGuid().ToString();
                long requestTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string proofPayload = BuildSigningProofPayload(
                    parsedCert.cert.nonce,
                    packageId,
                    contentHash,
                    packageVersion,
                    requestNonce,
                    requestTimestamp
                );
                byte[] proofSignature = DevKeyManager.SignData(Encoding.UTF8.GetBytes(proofPayload));
                string proofSignatureBase64 = Convert.ToBase64String(proofSignature);

                string bodyJson = $"{{\"packageId\":\"{EscapeJsonString(packageId)}\","
                                + $"\"packageName\":\"{EscapeJsonString(packageName ?? string.Empty)}\","
                                + $"\"contentHash\":\"{EscapeJsonString(contentHash)}\","
                                + $"\"packageVersion\":\"{EscapeJsonString(packageVersion)}\","
                                + $"\"requestNonce\":\"{EscapeJsonString(requestNonce)}\","
                                + $"\"requestTimestamp\":{requestTimestamp},"
                                + $"\"requestSignature\":\"{EscapeJsonString(proofSignatureBase64)}\"";

                if (protectedAssets != null && protectedAssets.Count > 0)
                {
                    var serializedProtectedAssets = protectedAssets
                        .Where(asset =>
                            asset != null &&
                            !string.IsNullOrEmpty(asset.protectedAssetId) &&
                            !string.IsNullOrEmpty(asset.unlockMode) &&
                            ((asset.unlockMode == "wrapped_content_key" && !string.IsNullOrEmpty(asset.wrappedContentKey)) ||
                             (asset.unlockMode == "content_key_b64" && !string.IsNullOrEmpty(asset.contentKeyBase64))))
                        .Select(asset =>
                        {
                            string keyField = asset.unlockMode == "content_key_b64"
                                ? $"\"contentKeyBase64\":\"{EscapeJsonString(asset.contentKeyBase64)}\","
                                : $"\"wrappedContentKey\":\"{EscapeJsonString(asset.wrappedContentKey)}\",";
                            string contentHashField = !string.IsNullOrEmpty(asset.contentHash)
                                ? $"\"contentHash\":\"{EscapeJsonString(asset.contentHash)}\","
                                : string.Empty;
                            return "{"
                                + $"\"protectedAssetId\":\"{EscapeJsonString(asset.protectedAssetId)}\","
                                + $"\"unlockMode\":\"{EscapeJsonString(asset.unlockMode)}\","
                                + keyField
                                + contentHashField
                                + $"\"displayName\":\"{EscapeJsonString(asset.displayName ?? string.Empty)}\""
                                + "}";
                        });
                    bodyJson += ",\"protectedAssets\":[" + string.Join(",", serializedProtectedAssets) + "]";
                }

                bodyJson += "}";
                byte[] requestBytes = System.Text.Encoding.UTF8.GetBytes(bodyJson);

                string url = $"{serverUrl.TrimEnd('/')}/v1/signatures";
                Debug.Log($"[PackageBuilder] Posting to transparency log: {url}");

                using (var request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
                {
                    request.uploadHandler   = new UnityEngine.Networking.UploadHandlerRaw(requestBytes);
                    request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type",    "application/json");
                    request.SetRequestHeader("Authorization",   $"Bearer {certBase64}");
                    request.SetRequestHeader("Accept-Encoding", "identity");
                    request.timeout = 30;

                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        progressCallback?.Invoke(0.725f + operation.progress * 0.002f, "Waiting for server response...");
                        System.Threading.Thread.Sleep(50);
                    }

                    string responseText = request.downloadHandler.text;

                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[PackageBuilder] Transparency log recorded: {responseText}");

                        var response = ParseSigningResponseJson(responseText);
                        if (response == null || response.certificateChain == null || response.certificateChain.Length == 0)
                        {
                            return new SigningRequestResult
                            {
                                rawError = responseText,
                                normalizedError = "Signing server returned an invalid certificate chain response.",
                                responseCode = request.responseCode,
                            };
                        }

                        if (string.IsNullOrEmpty(response.algorithm))
                            response.algorithm = "Ed25519";
                        if (response.certificateIndex < 0 || response.certificateIndex >= response.certificateChain.Length)
                            response.certificateIndex = 0;
                        if (string.IsNullOrEmpty(response.keyId))
                            response.keyId = response.certificateChain[response.certificateIndex]?.keyId ?? "";

                        return new SigningRequestResult
                        {
                            response = response,
                            responseCode = request.responseCode,
                        };
                    }
                    else
                    {
                        string error = string.IsNullOrEmpty(responseText)
                            ? $"HTTP {request.responseCode}: {request.error}"
                            : responseText;

                        if (error.TrimStart().StartsWith("<"))
                            error = $"HTTP {request.responseCode} (server returned HTML — likely wrong URL or auth)";

                        string normalizedError = PackageSigningService.NormalizeSigningError(request.responseCode, error);
                        if (normalizedError.IndexOf("Certificates & Billing", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string accountUrl = GetSigningSettings()?.GetEffectiveAccountCertificatesUrl();
                            if (!string.IsNullOrEmpty(accountUrl))
                            {
                                normalizedError = $"{normalizedError} Open: {accountUrl}";
                            }
                        }

                        if (!suppressFailureLog)
                        {
                            Debug.LogError($"[PackageBuilder] Signing request failed: {normalizedError}");
                        }
                        return new SigningRequestResult
                        {
                            responseCode = request.responseCode,
                            rawError = error,
                            normalizedError = normalizedError,
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Signing request exception: {ex.Message}");
                return new SigningRequestResult
                {
                    rawError = ex.Message,
                    normalizedError = $"Network error while requesting a package signature: {ex.Message}",
                };
            }
        }


        /// <summary>
        /// Inject signing data folder into the package
        /// </summary>
        private static void InjectSigningDataIntoPackage(string packagePath)
        {
            string signingFolder = "Assets/_Signing";
            if (!AssetDatabase.IsValidFolder(signingFolder))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("", new[] { signingFolder });
            if (guids.Length == 0)
            {
                return;
            }

            // Use the same injection mechanism as package.json
            // Extract package, add signing files, repackage
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"YUCP_Signing_{Guid.NewGuid():N}");
            
            try
            {
                Directory.CreateDirectory(tempExtractDir);

                // Extract package (using same approach as PackageIconInjector)
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
                using (Stream inStream = File.OpenRead(packagePath))
                using (Stream gzipStream = new GZipInputStream(inStream))
                {
                    var tarArchive = TarArchive.CreateInputTarArchive(gzipStream, Encoding.UTF8);
                    tarArchive.ExtractContents(tempExtractDir);
                    tarArchive.Close();
                }

                // Remove existing signing files to avoid duplicates
                string[] existingFolders = Directory.GetDirectories(tempExtractDir);
                foreach (string folder in existingFolders)
                {
                    string pathnameFile = Path.Combine(folder, "pathname");
                    if (File.Exists(pathnameFile))
                    {
                        string pathname = File.ReadAllText(pathnameFile).Trim();
                        if (pathname.StartsWith("Assets/_Signing/", StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.Delete(folder, true);
                        }
                    }
                }

                // Add signing files
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
                        continue;

                    string fileName = Path.GetFileName(assetPath);
                    string fileGuid = Guid.NewGuid().ToString("N");
                    string fileFolder = Path.Combine(tempExtractDir, fileGuid);
                    Directory.CreateDirectory(fileFolder);

                    // Copy asset file
                    File.Copy(assetPath, Path.Combine(fileFolder, "asset"), true);

                    File.WriteAllText(Path.Combine(fileFolder, "pathname"), assetPath);

                    // Create .meta file
                    string metaGuid = Guid.NewGuid().ToString("N");
                    string metaContent = GenerateMetaForFile(assetPath, metaGuid);
                    File.WriteAllText(Path.Combine(fileFolder, "asset.meta"), metaContent);
                }

                // Repackage (using same approach as PackageIconInjector)
                string tempPackagePath = packagePath + ".tmp";
                using (Stream outStream = File.Create(tempPackagePath))
                using (Stream gzipStream = new GZipOutputStream(outStream))
                {
                    var tarArchive = TarArchive.CreateOutputTarArchive(gzipStream);
                    
                    // Set root path (case sensitive, must use forward slashes, must not end with slash)
                    tarArchive.RootPath = tempExtractDir.Replace('\\', '/');
                    if (tarArchive.RootPath.EndsWith("/"))
                        tarArchive.RootPath = tarArchive.RootPath.TrimEnd('/');

                    // Add all files from extracted directory
                    var filenames = Directory.GetFiles(tempExtractDir, "*", SearchOption.AllDirectories);
                    foreach (var filename in filenames)
                    {
                        var relativePath = filename.Substring(tempExtractDir.Length);
                        if (relativePath.StartsWith("\\") || relativePath.StartsWith("/"))
                            relativePath = relativePath.Substring(1);
                        relativePath = relativePath.Replace('\\', '/');
                        
                        var tarEntry = TarEntry.CreateEntryFromFile(filename);
                        tarEntry.Name = relativePath;
                        tarArchive.WriteEntry(tarEntry, true);
                    }
                    
                    tarArchive.Close();
                }

                // Replace original with signed version
                File.Delete(packagePath);
                File.Move(tempPackagePath, packagePath);

#else
                Debug.LogWarning("[PackageBuilder] ICSharpCode.SharpZipLib not available - cannot inject signing data. Please install SharpZipLib.");
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Failed to inject signing data: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempExtractDir))
                        Directory.Delete(tempExtractDir, true);
                }
                catch { }
            }
        }

        /// <summary>
        /// Get signing settings
        /// </summary>
        private static PackageSigningData.SigningSettings GetSigningSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<PackageSigningData.SigningSettings>(path);
            }
            return null;
        }

        /// <summary>
        /// Compute canonical archive hash over package contents, excluding signing data.
        /// Hash is SHA-256 over a deterministic stream of:
        ///   UTF8(pathname) + 0x00 + asset-bytes, in sorted pathname order,
        /// ignoring any Assets/_Signing/* entries.
        /// </summary>
        private static string ComputeArchiveHashExcludingSigningData(string packagePath)
        {
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"YUCP_Hash_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempExtractDir);

            try
            {
                using (Stream inStream = File.OpenRead(packagePath))
                using (Stream gzipStream = new GZipInputStream(inStream))
                {
                    var tarArchive = TarArchive.CreateInputTarArchive(gzipStream, Encoding.UTF8);
                    tarArchive.ExtractContents(tempExtractDir);
                    tarArchive.Close();
                }

                // Collect all non-signing assets
                var entries = new System.Collections.Generic.List<(string pathname, string assetPath)>();

                string[] folders = Directory.GetDirectories(tempExtractDir);
                foreach (string folder in folders)
                {
                    string pathnameFile = Path.Combine(folder, "pathname");
                    string assetFile = Path.Combine(folder, "asset");

                    if (!File.Exists(pathnameFile) || !File.Exists(assetFile))
                        continue;

                    string pathname = File.ReadAllText(pathnameFile).Trim().Replace('\\', '/');

                    // Skip signing data
                    if (pathname.StartsWith("Assets/_Signing/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    entries.Add((pathname, assetFile));
                }

                // Sort by pathname for determinism
                entries.Sort((a, b) => string.CompareOrdinal(a.pathname, b.pathname));

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    foreach (var entry in entries)
                    {
                        byte[] pathBytes = Encoding.UTF8.GetBytes(entry.pathname);
                        sha256.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);

                        // Separator byte to avoid path/data ambiguity
                        byte[] sep = new byte[] { 0x00 };
                        sha256.TransformBlock(sep, 0, 1, null, 0);

                        byte[] data = File.ReadAllBytes(entry.assetPath);
                        sha256.TransformBlock(data, 0, data.Length, null, 0);
                    }

                    sha256.TransformFinalBlock(System.Array.Empty<byte>(), 0, 0);
                    return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempExtractDir))
                        Directory.Delete(tempExtractDir, true);
                }
                catch { }
            }
#else
            throw new System.InvalidOperationException("SharpZipLib (TarArchive/GZipInputStream) not available in this Unity version; cannot compute archive hash.");
#endif
        }

        /// <summary>
        /// Signing request payload structure (matches PackageSigningService)
        /// </summary>
        [Serializable]
        private class SigningRequestPayload
        {
            public string publisherId;
            public string vrchatUserId;
            public PackageSigningData.PackageManifest manifest;
            public PackageSigningData.YucpCertificate yucpCert;
            public string timestamp;
            public string nonce;
        }

        /// <summary>
        /// Signing request structure (matches PackageSigningService)
        /// </summary>
        [Serializable]
        private class SigningRequest
        {
            public SigningRequestPayload payload;
            public string devSignature;
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
