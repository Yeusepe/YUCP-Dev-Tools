using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;

namespace YUCP.DirectVpmInstaller
{
    [InitializeOnLoad]
    public static class DirectVpmInstaller
    {
            private const int FriendlyWindowsPathLimit = 240;
            private const string RepoTokenVpmDeliveryMode = "repo-token-vpm-v1";
            private const string ZipPackageSourceKind = "zip";
            private const string UnityPackageSourceKind = "unitypackage";
            private const string RepoTokenHeaderName = "X-YUCP-Repo-Token";
        private static readonly HashSet<string> TrustedCommunityRepoHostsWithoutHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "vcc.vrcfury.com"
        };

        [Serializable]
        private sealed class ResolvedPackageDownload
        {
            public string repositoryName;
            public string repositoryUrl;
            public string packageName;
            public string displayName;
            public string downloadUrl;
            public string resolvedVersion;
            public string expectedArchiveHash;
            public string friendlyWarningMessage;
            public string deliveryMode;
            public string deliverySourceKind;
            public Dictionary<string, string> requestHeaders;
        }

        internal static readonly string[] GeneratedInstallerArtifactPatterns = new[]
        {
            "YUCP_InstallerPreflight_*.cs",
            "YUCP_Installer_*.cs",
            "YUCP_Installer_*.asmdef",
            "YUCP_InstallerTxn_*.cs",
            "YUCP_InstallerHealthTools_*.cs",
            "YUCP_FullDomainReload_*.cs"
        };

        static DirectVpmInstaller()
        {
            try
            {
                if (HasPendingTempInstallJson())
                {
                    InstallerTxn.SetMarker("scheduled");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to set scheduled installer marker: {ex.Message}");
            }

            EditorApplication.delayCall += CheckAndInstallVpmPackages;
        }

        private static bool HasPendingTempInstallJson()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string installedRoot = Path.Combine(projectRoot, "Packages", "yucp.installed-packages");
                if (Directory.Exists(installedRoot) &&
                    Directory.GetFiles(installedRoot, "YUCP_TempInstall_*.json", SearchOption.AllDirectories).Length > 0)
                {
                    return true;
                }

                return Directory.GetFiles(Application.dataPath, "YUCP_TempInstall_*.json", SearchOption.TopDirectoryOnly).Length > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to probe pending temp install descriptors: {ex.Message}");
                return false;
            }
        }

        internal static string[] GetInstallerEditorPaths(string projectRoot)
        {
            return new[]
            {
                Path.Combine(Application.dataPath, "Editor"),
                Path.Combine(projectRoot, "Packages", "yucp.installed-packages", "Editor")
            };
        }

        private static string DiskPathToAssetPath(string diskPath)
        {
            if (string.IsNullOrWhiteSpace(diskPath))
                return null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(diskPath);
            if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            return fullPath.Substring(projectRoot.Length).Replace('\\', '/');
        }

        private static Dictionary<string, string> LoadTrustedRepositories(JObject vpmRepositories)
        {
            var repositories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (vpmRepositories == null)
                return repositories;

            foreach (var repo in vpmRepositories.Properties())
            {
                string repoUrl = repo.Value.Type == JTokenType.Object
                    ? (repo.Value as JObject)?["url"]?.ToString()
                    : repo.Value?.ToString();
                if (!IsTrustedWebUrl(repoUrl))
                {
                    Debug.LogWarning($"[DirectVpmInstaller] Ignoring untrusted repository URL declared by the package: {repoUrl}");
                    continue;
                }

                string key = string.IsNullOrWhiteSpace(repo.Name) ? repoUrl.Trim() : repo.Name.Trim();
                if (!repositories.ContainsKey(key))
                {
                    repositories[key] = repoUrl.Trim();
                }
            }

            return repositories;
        }

        private static void AddBuiltInRepositories(Dictionary<string, string> repositories)
        {
            const string vrchatOfficialName = "VRChat Official";
            const string vrchatCuratedName = "VRChat Curated";
            const string vrchatOfficialUrl = "https://packages.vrchat.com/official?download";
            const string vrchatCuratedUrl = "https://packages.vrchat.com/curated?download";

            if (!repositories.ContainsKey(vrchatOfficialName))
                repositories[vrchatOfficialName] = vrchatOfficialUrl;
            if (!repositories.ContainsKey(vrchatCuratedName))
                repositories[vrchatCuratedName] = vrchatCuratedUrl;
        }

        private static bool IsTrustedWebUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return false;

            if (uri.Scheme == Uri.UriSchemeHttps)
                return true;

            return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        }

        private static bool IsSafePackageName(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return false;
            if (packageName.Contains("/") || packageName.Contains("\\") || packageName.Contains(":"))
                return false;

            string[] segments = packageName.Split('.');
            if (segments.Length < 2)
                return false;

            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                    return false;

                foreach (char c in segment)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                        return false;
                }
            }

            return true;
        }

        private static string GetPackagesRoot()
        {
            return Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Packages");
        }

        private static bool TryGetInstalledPackageJsonPath(string packageName, out string packageJsonPath)
        {
            packageJsonPath = null;
            if (!IsSafePackageName(packageName))
                return false;

            packageJsonPath = Path.Combine(GetPackagesRoot(), packageName, "package.json");
            return true;
        }

        private static string GetValidatedPackageDestination(string packageName)
        {
            if (!IsSafePackageName(packageName))
                throw new InvalidDataException($"Invalid package name '{packageName}'.");

            string packagesRoot = Path.GetFullPath(GetPackagesRoot())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string destination = Path.GetFullPath(Path.Combine(packagesRoot, packageName));
            if (!destination.StartsWith(packagesRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Package '{packageName}' resolves outside the Packages directory.");

            return destination;
        }

        private static string GetInstallerWorkspaceRoot()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, ".yucp-dvi");
        }

        private static string GetFriendlyRepositoryLabel(string repositoryName, string repositoryUrl)
        {
            if (!string.IsNullOrWhiteSpace(repositoryName))
                return repositoryName.Trim();

            if (Uri.TryCreate(repositoryUrl, UriKind.Absolute, out Uri uri) && !string.IsNullOrWhiteSpace(uri.Host))
                return uri.Host;

            return "This source";
        }

        private static string GetFriendlyPackageLabel(string displayName, string packageName)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName.Trim();

            if (string.IsNullOrWhiteSpace(packageName))
                return "this package";

            string[] segments = packageName.Split('.');
            string leaf = segments.Length > 0 ? segments[segments.Length - 1] : packageName;
            if (string.IsNullOrWhiteSpace(leaf))
                leaf = packageName;

            return char.ToUpperInvariant(leaf[0]) + leaf.Substring(1);
        }

        private static bool AllowsMissingArchiveHash(string repositoryUrl)
        {
            if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out Uri uri))
                return false;

            return TrustedCommunityRepoHostsWithoutHashes.Contains(uri.Host);
        }

        private static string BuildMissingHashWarning(string repositoryName, string repositoryUrl, string displayName, string packageName, string version)
        {
            string repoLabel = GetFriendlyRepositoryLabel(repositoryName, repositoryUrl);
            string packageLabel = GetFriendlyPackageLabel(displayName, packageName);
            return $"{repoLabel} is a trusted community source, but it doesn't publish a security fingerprint for {packageLabel} {version}. We can still install it, but Unity can't double-check that download automatically.";
        }

        private static void AddUniqueMessage(List<string> messages, string message)
        {
            if (messages == null || string.IsNullOrWhiteSpace(message))
                return;

            if (!messages.Contains(message))
                messages.Add(message);
        }

        private static void ShowInstallFailureDialog(List<string> failureMessages)
        {
            if (failureMessages == null || failureMessages.Count == 0)
                return;

            EditorUtility.DisplayDialog(
                "Package setup couldn't finish",
                "We couldn't finish installing everything this package needs.\n\n"
                + string.Join("\n\n", failureMessages.Distinct(StringComparer.Ordinal))
                + "\n\nNothing from the bundled package was turned on yet, so your project was left unchanged.",
                "OK");
        }

        private static void EnsureCreatorFriendlyPathLength(string path, string packageLabel)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor || string.IsNullOrWhiteSpace(path))
                return;

            if (path.Length < FriendlyWindowsPathLimit)
                return;

            throw new IOException(
                $"Windows couldn't unpack {packageLabel} because this project is stored in a very long folder path. Move the project to a shorter folder, such as C:\\Unity\\MyAvatar, and try again.");
        }

        private static string BuildFriendlyInstallFailureMessage(string packageLabel, Exception ex)
        {
            if (ex is WebException)
                return $"We couldn't download {packageLabel}. Please check your internet connection and try the import again.";

            if (ex is PathTooLongException ||
                ex.Message.IndexOf("very long folder path", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return $"{packageLabel} couldn't be unpacked because Windows is hitting its long file path limit for this project folder. Move the project to a shorter path and try again.";
            }

            if (ex is InvalidDataException)
                return $"We downloaded {packageLabel}, but the package contents didn't look valid. The install was stopped to protect your project.";

            return $"We couldn't install {packageLabel}. Please try the import again. If it still fails, send the YUCP installer messages from the Console to support.";
        }

        private static string NormalizeSha256(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return null;

            string normalized = hash.Trim().Replace("-", string.Empty).ToUpperInvariant();
            return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
                ? normalized
                : null;
        }

        private static string ComputeFileSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static string GetValidatedExtractionPath(string extractionRoot, string entryName, string sourceDescription)
        {
            string normalizedRoot = Path.GetFullPath(extractionRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedEntry = entryName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, normalizedEntry));
            if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry '{entryName}' from '{sourceDescription}' escapes '{normalizedRoot}'.");

            return candidate;
        }

        private static void ExtractZipToDirectorySafely(string zipPath, string destinationDirectory, string packageLabel)
        {
            EnsureCreatorFriendlyPathLength(destinationDirectory, packageLabel);
            Directory.CreateDirectory(destinationDirectory);
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName))
                    continue;

                string destinationPath = GetValidatedExtractionPath(destinationDirectory, entry.FullName, zipPath);
                EnsureCreatorFriendlyPathLength(destinationPath, packageLabel);
                bool isDirectory = string.IsNullOrEmpty(entry.Name) &&
                                   (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                                    entry.FullName.EndsWith("\\", StringComparison.Ordinal));
                if (isDirectory)
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                string parentDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDirectory))
                {
                    EnsureCreatorFriendlyPathLength(parentDirectory, packageLabel);
                    Directory.CreateDirectory(parentDirectory);
                }

                using Stream input = entry.Open();
                using Stream output = File.Create(destinationPath);
                input.CopyTo(output);
            }
        }

        private static void ExtractDownloadedPackageToDirectorySafely(
            string downloadPath,
            string sourceKind,
            string destinationDirectory,
            string packageLabel)
        {
            if (string.Equals(sourceKind, UnityPackageSourceKind, StringComparison.Ordinal))
            {
                ExtractUnityPackageToDirectorySafely(downloadPath, destinationDirectory, packageLabel);
                return;
            }

            ExtractZipToDirectorySafely(downloadPath, destinationDirectory, packageLabel);
        }

        private static void ExtractUnityPackageToDirectorySafely(
            string unityPackagePath,
            string destinationDirectory,
            string packageLabel)
        {
            EnsureCreatorFriendlyPathLength(destinationDirectory, packageLabel);
            Directory.CreateDirectory(destinationDirectory);
            using var fileStream = File.OpenRead(unityPackagePath);
            using var gzipStream = new System.IO.Compression.GZipStream(
                fileStream,
                System.IO.Compression.CompressionMode.Decompress);
            byte[] header = new byte[512];
            while (TryReadTarHeader(gzipStream, header))
            {
                string entryName = ReadTarString(header, 0, 100);
                if (string.IsNullOrEmpty(entryName))
                {
                    continue;
                }

                long entrySize = ReadTarOctal(header, 124, 12);
                char entryType = (char)header[156];
                string destinationPath = GetValidatedExtractionPath(
                    destinationDirectory,
                    entryName,
                    unityPackagePath);
                bool isDirectory = entryType == '5' || entryName.EndsWith("/", StringComparison.Ordinal);
                if (isDirectory)
                {
                    EnsureCreatorFriendlyPathLength(destinationPath, packageLabel);
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                EnsureCreatorFriendlyPathLength(destinationPath, packageLabel);
                string parentDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDirectory))
                {
                    EnsureCreatorFriendlyPathLength(parentDirectory, packageLabel);
                    Directory.CreateDirectory(parentDirectory);
                }

                using Stream output = File.Create(destinationPath);
                CopyTarEntryContents(gzipStream, output, entrySize);
                SkipTarPadding(gzipStream, entrySize);
            }
        }

        private static bool TryReadTarHeader(Stream stream, byte[] header)
        {
            int totalRead = 0;
            while (totalRead < header.Length)
            {
                int bytesRead = stream.Read(header, totalRead, header.Length - totalRead);
                if (bytesRead == 0)
                {
                    if (totalRead == 0)
                    {
                        return false;
                    }

                    throw new InvalidDataException("Authorized unitypackage archive ended before the next TAR header was complete.");
                }

                totalRead += bytesRead;
            }

            return header.Any((value) => value != 0);
        }

        private static string ReadTarString(byte[] header, int offset, int length)
        {
            return Encoding.ASCII.GetString(header, offset, length).Trim('\0', ' ');
        }

        private static long ReadTarOctal(byte[] header, int offset, int length)
        {
            string rawValue = ReadTarString(header, offset, length);
            if (string.IsNullOrEmpty(rawValue))
            {
                return 0;
            }

            try
            {
                return Convert.ToInt64(rawValue, 8);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException(
                    $"Authorized unitypackage TAR header contained an invalid size field '{rawValue}'.",
                    ex);
            }
        }

        private static void CopyTarEntryContents(Stream input, Stream output, long bytesToCopy)
        {
            byte[] buffer = new byte[81920];
            long remaining = bytesToCopy;
            while (remaining > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    throw new InvalidDataException("Authorized unitypackage archive ended before a TAR entry was fully read.");
                }

                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static void SkipTarPadding(Stream input, long entrySize)
        {
            long remainder = entrySize % 512;
            if (remainder == 0)
            {
                return;
            }

            long padding = 512 - remainder;
            byte[] buffer = new byte[512];
            while (padding > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, padding));
                if (read == 0)
                {
                    throw new InvalidDataException("Authorized unitypackage archive ended before TAR padding was fully skipped.");
                }
                padding -= read;
            }
        }

        private static bool TryGetExpectedArchiveHash(JObject versionMetadata, out string expectedHash)
        {
            expectedHash = NormalizeSha256(versionMetadata?["zipSHA256"]?.ToString())
                ?? NormalizeSha256(versionMetadata?["sha256"]?.ToString());
            return !string.IsNullOrEmpty(expectedHash);
        }

        private static bool IsRepoTokenVpmDelivery(JObject versionMetadata)
        {
            return string.Equals(
                versionMetadata?["yucpDeliveryMode"]?.ToString(),
                RepoTokenVpmDeliveryMode,
                StringComparison.Ordinal);
        }

        private static Dictionary<string, string> ReadManifestRequestHeaders(JObject versionMetadata)
        {
            if (!(versionMetadata?["headers"] is JObject headersObject))
                return null;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in headersObject.Properties())
            {
                string headerName = property.Name?.Trim();
                string headerValue = property.Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(headerName) || string.IsNullOrWhiteSpace(headerValue))
                    continue;

                headers[headerName] = headerValue;
            }

            return headers.Count > 0 ? headers : null;
        }

        private static bool TryCreateRepoTokenVpmResolution(
            string repositoryName,
            string repositoryUrl,
            string packageName,
            string version,
            JObject versionMetadata,
            out ResolvedPackageDownload resolution)
        {
            resolution = null;

            string sourceKind = versionMetadata?["yucpDeliverySourceKind"]?.ToString();
            string candidateUrl = versionMetadata?["url"]?.ToString();
            Dictionary<string, string> requestHeaders = ReadManifestRequestHeaders(versionMetadata);
            bool hasExpectedArchiveHash = TryGetExpectedArchiveHash(versionMetadata, out string expectedArchiveHash);

            if (!string.Equals(sourceKind, ZipPackageSourceKind, StringComparison.Ordinal) &&
                !string.Equals(sourceKind, UnityPackageSourceKind, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[DirectVpmInstaller] Repository {repositoryName} declared {RepoTokenVpmDeliveryMode} for {packageName}@{version}, but yucpDeliverySourceKind was invalid.");
                return false;
            }

            if (!IsTrustedWebUrl(candidateUrl))
            {
                Debug.LogWarning($"[DirectVpmInstaller] Repository {repositoryName} declared an untrusted manifest package URL for {packageName}@{version}.");
                return false;
            }

            if (!hasExpectedArchiveHash)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Repository {repositoryName} declared {RepoTokenVpmDeliveryMode} for {packageName}@{version}, but no archive hash was provided.");
                return false;
            }

            if (requestHeaders == null ||
                !requestHeaders.TryGetValue(RepoTokenHeaderName, out string repoToken) ||
                string.IsNullOrWhiteSpace(repoToken))
            {
                Debug.LogWarning($"[DirectVpmInstaller] Repository {repositoryName} declared {RepoTokenVpmDeliveryMode} for {packageName}@{version}, but the repo token header was missing.");
                return false;
            }

            resolution = new ResolvedPackageDownload
            {
                repositoryName = repositoryName,
                repositoryUrl = repositoryUrl,
                packageName = packageName,
                displayName = versionMetadata?["displayName"]?.ToString(),
                downloadUrl = candidateUrl,
                resolvedVersion = version,
                expectedArchiveHash = expectedArchiveHash,
                friendlyWarningMessage = null,
                deliveryMode = RepoTokenVpmDeliveryMode,
                deliverySourceKind = sourceKind,
                requestHeaders = requestHeaders,
            };
            return true;
        }

        private static bool TryResolvePackageDownload(
            string packageName,
            string versionRequirement,
            Dictionary<string, string> repositories,
            out ResolvedPackageDownload resolution)
        {
            resolution = null;
            string blockingInvalidServerRepository = null;
            string blockingInvalidServerVersion = null;
            string blockingInvalidServerMode = null;

            foreach (var repo in repositories)
            {
                if (!IsTrustedWebUrl(repo.Value))
                {
                    Debug.LogWarning($"[DirectVpmInstaller] Skipping untrusted repository URL '{repo.Value}'.");
                    continue;
                }

                try
                {
                    using var repoClient = new WebClient();
                    repoClient.Headers.Add(HttpRequestHeader.UserAgent, "VCC/2.3.0");
                    var repoData = JObject.Parse(repoClient.DownloadString(repo.Value));
                    var packages = repoData["packages"] as JObject;
                    var packageData = packages?[packageName] as JObject;
                    var versions = packageData?["versions"] as JObject;

                    if (versions == null)
                        continue;

                    foreach (var versionEntry in versions.Properties())
                    {
                        try
                        {
                            string version = versionEntry.Name;
                            if (!VersionSatisfiesRequirement(version, versionRequirement))
                                continue;

                            JObject versionMetadata = versionEntry.Value as JObject;
                            if (IsRepoTokenVpmDelivery(versionMetadata))
                            {
                                if (TryCreateRepoTokenVpmResolution(
                                        repo.Key,
                                        repo.Value,
                                        packageName,
                                        version,
                                        versionMetadata,
                                        out ResolvedPackageDownload repoTokenResolution))
                                {
                                    if (resolution == null ||
                                        CompareVersions(repoTokenResolution.resolvedVersion, resolution.resolvedVersion) > 0 ||
                                        (CompareVersions(repoTokenResolution.resolvedVersion, resolution.resolvedVersion) == 0 &&
                                         string.IsNullOrEmpty(resolution.deliveryMode)))
                                    {
                                        resolution = repoTokenResolution;
                                    }
                                }
                                else if (blockingInvalidServerVersion == null ||
                                         CompareVersions(version, blockingInvalidServerVersion) > 0)
                                {
                                    blockingInvalidServerRepository = repo.Key;
                                    blockingInvalidServerVersion = version;
                                    blockingInvalidServerMode = RepoTokenVpmDeliveryMode;
                                }

                                continue;
                            }

                            string candidateUrl = versionMetadata?["url"]?.ToString();
                            string declaredPackageName = versionMetadata?["name"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(declaredPackageName) &&
                                !string.Equals(declaredPackageName, packageName, StringComparison.OrdinalIgnoreCase))
                            {
                                Debug.LogWarning($"[DirectVpmInstaller] Repository {repo.Key} returned mismatched package metadata for {packageName}.");
                                continue;
                            }

                            if (!IsTrustedWebUrl(candidateUrl))
                                continue;

                            bool hasHash = TryGetExpectedArchiveHash(versionMetadata, out string candidateHash);
                            if (!hasHash && !AllowsMissingArchiveHash(repo.Value))
                            {
                                Debug.LogWarning($"[DirectVpmInstaller] Repository {repo.Key} did not provide a valid SHA-256 for {packageName}@{version}.");
                                continue;
                            }

                            string displayName = versionMetadata?["displayName"]?.ToString();
                            var candidate = new ResolvedPackageDownload
                            {
                                repositoryName = repo.Key,
                                repositoryUrl = repo.Value,
                                packageName = packageName,
                                displayName = displayName,
                                downloadUrl = candidateUrl,
                                resolvedVersion = version,
                                expectedArchiveHash = hasHash ? candidateHash : null,
                                requestHeaders = ReadManifestRequestHeaders(versionMetadata),
                                friendlyWarningMessage = hasHash
                                    ? null
                                    : BuildMissingHashWarning(repo.Key, repo.Value, displayName, packageName, version)
                            };

                            if (resolution == null ||
                                CompareVersions(candidate.resolvedVersion, resolution.resolvedVersion) > 0 ||
                                (CompareVersions(candidate.resolvedVersion, resolution.resolvedVersion) == 0 &&
                                 string.IsNullOrEmpty(resolution.expectedArchiveHash) &&
                                 !string.IsNullOrEmpty(candidate.expectedArchiveHash)))
                            {
                                resolution = candidate;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DirectVpmInstaller] Failed to check repository {repo.Key}: {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(blockingInvalidServerVersion))
            {
                int comparison = resolution == null
                    ? 1
                    : CompareVersions(blockingInvalidServerVersion, resolution.resolvedVersion);
                if (comparison > 0 ||
                    (comparison == 0 && string.IsNullOrEmpty(resolution.deliveryMode)))
                {
                    Debug.LogWarning(
                        $"[DirectVpmInstaller] Repository {blockingInvalidServerRepository} declared {blockingInvalidServerMode ?? RepoTokenVpmDeliveryMode} for {packageName}@{blockingInvalidServerVersion}, but the companion metadata was invalid. Refusing to fall back to an older or legacy release.");
                    resolution = null;
                    return false;
                }
            }

            return resolution != null;
        }

        private static void MoveDirectoryIntoPlace(string sourceDirectory, string destinationDirectory)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    Directory.Move(sourceDirectory, destinationDirectory);
                    return;
                }
                catch (Exception) when (attempt == 0 && (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory)))
                {
                    if (Directory.Exists(destinationDirectory))
                    {
                        Directory.Delete(destinationDirectory, true);
                    }
                    else if (File.Exists(destinationDirectory))
                    {
                        File.Delete(destinationDirectory);
                    }
                }
            }

            Directory.Move(sourceDirectory, destinationDirectory);
        }

        private static void EnableBundledPackagesAndFinalize(string packageJsonPath, string completionReason)
        {
            Debug.Log($"[DirectVpmInstaller] {completionReason}");

            EditorApplication.LockReloadAssemblies();
            AssetDatabase.DisallowAutoRefresh();
            AssetDatabase.StartAssetEditing();

            try
            {
                string txnId = InstallerTxn.Begin();
                bool enableOk = false;
                try
                {
                    EnableBundledPackagesTransactional();
                    enableOk = true;
                }
                catch (Exception exEnable)
                {
                    Debug.LogError($"[DirectVpmInstaller] Failed while enabling bundled packages: {exEnable.Message}. Rolling back...");
                    InstallerTxn.Rollback();
                    throw;
                }
                finally
                {
                    if (enableOk)
                    {
                        if (!InstallerTxn.VerifyManifest())
                            throw new Exception("Post-install manifest verification failed");
                        InstallerTxn.Commit();
                    }
                }

                InstallerTxn.SetMarker("complete");
                CleanupTemporaryFiles(packageJsonPath);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.AllowAutoRefresh();
                EditorApplication.UnlockReloadAssemblies();

                Debug.Log("[DirectVpmInstaller] Bundled package enable completed. Triggering full domain reload...");
                FullDomainReload.Run(() =>
                {
                    Debug.Log("[DirectVpmInstaller] Bundled package enable complete after domain reload.");
                });
            }
        }
        
        private static void CheckAndInstallVpmPackages()
        {
            // Note: Duplicate import prevention is handled by YUCPImportMonitor (global AssetPostprocessor)
            // in the com.yucp.components package, which runs BEFORE this installer
            
            // Clear stale lock if present (crash recovery)
            try { if (InstallerTxn.HasMarker("lock") && InstallerTxn.IsMarkerStale("lock", TimeSpan.FromMinutes(10))) InstallerTxn.ClearMarker("lock"); } catch { }

            // Clean up any old installer scripts first to prevent duplicate class definitions on re-import
            CleanupInstallerScript();

            // Find any YUCP temp install JSON files
            string[] tempJsonFiles = Array.Empty<string>();
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string installedRoot = Path.Combine(projectRoot, "Packages", "yucp.installed-packages");
                if (Directory.Exists(installedRoot))
                {
                    tempJsonFiles = Directory.GetFiles(installedRoot, "YUCP_TempInstall_*.json", SearchOption.AllDirectories);
                }
            }
            catch { }

            if (tempJsonFiles.Length == 0)
            {
                tempJsonFiles = Directory.GetFiles(Application.dataPath, "YUCP_TempInstall_*.json", SearchOption.TopDirectoryOnly);
            }
            
            if (tempJsonFiles.Length == 0)
            {
                try { InstallerTxn.ClearMarker("scheduled"); } catch { }

                // If a previous install completed, ensure cleanup convergence
                if (InstallerTxn.HasMarker("complete"))
                {
                    CleanupInstallerScript();

                    string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    if (!HasGeneratedInstallerArtifacts(projectRoot))
                    {
                        InstallerTxn.ClearMarker("complete");
                    }
                }
                return;
            }
            
            string packageJsonPath = tempJsonFiles[0]; // Use the first one found
            
            try
            {
                // Signal import coordination
                try { InstallerTxn.ClearMarker("scheduled"); } catch { }
                InstallerTxn.SetMarker("pending");
                InstallerTxn.SetMarker("lock");
                var packageInfo = JObject.Parse(File.ReadAllText(packageJsonPath));
                string bundledPackageName = packageInfo["name"]?.Value<string>();
                string bundledPackageVersion = packageInfo["version"]?.Value<string>();
                // Check if this bundled package is already installed
                if (!string.IsNullOrEmpty(bundledPackageName))
                {
                    string existingPackagePath = Path.Combine(Application.dataPath, "..", "Packages", bundledPackageName);
                    if (Directory.Exists(existingPackagePath))
                    {
                        string existingPackageJson = Path.Combine(existingPackagePath, "package.json");
                        if (File.Exists(existingPackageJson))
                        {
                            try
                            {
                                var existingData = JObject.Parse(File.ReadAllText(existingPackageJson));
                                string existingVersion = existingData["version"]?.Value<string>();
                                
                                if (!string.IsNullOrEmpty(existingVersion) && !string.IsNullOrEmpty(bundledPackageVersion))
                                {
                                    if (CompareVersions(existingVersion, bundledPackageVersion) >= 0)
                                    {
                                        Debug.Log($"[DirectVpmInstaller] Bundled package {bundledPackageName}@{bundledPackageVersion} is already installed (current: {existingVersion}). Skipping extraction.");
                                        
                                        // Still enable any .yucp_disabled files (might be from previous failed install)
                                        string txnIdEarly = InstallerTxn.Begin();
                                        bool enableOkEarly = false;
                                        try
                                        {
                                            EnableBundledPackagesTransactional();
                                            enableOkEarly = true;
                                        }
                                        catch (Exception exEnable)
                                        {
                                            Debug.LogError($"[DirectVpmInstaller] Failed while enabling bundled packages: {exEnable.Message}. Rolling back...");
                                            InstallerTxn.Rollback();
                                            throw;
                                        }
                                        finally
                                        {
                                            if (enableOkEarly)
                                            {
                                                InstallerTxn.Commit();
                                                if (!InstallerTxn.VerifyManifest())
                                                    throw new Exception("Post-install manifest verification failed");
                                            }
                                        }
                                        // Mark install complete and cleanup
                                        InstallerTxn.SetMarker("complete");
                                        CleanupTemporaryFiles(packageJsonPath);
                                        return;
                                    }
                                    else
                                    {
                                        Debug.Log($"[DirectVpmInstaller] Upgrading bundled package {bundledPackageName} from {existingVersion} to {bundledPackageVersion}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[DirectVpmInstaller] Failed to check existing package version: {ex.Message}");
                            }
                        }
                    }
                }
                
                var vpmDependencies = packageInfo["vpmDependencies"] as JObject;
                var vpmRepositories = packageInfo["vpmRepositories"] as JObject;
                
                if (vpmDependencies == null || vpmDependencies.Count == 0)
                {
                    EnableBundledPackagesAndFinalize(packageJsonPath, "No VPM dependencies were declared. Enabling bundled packages directly.");
                    return;
                }
                
                // Seed repository list from the bundled package (if any)
                var repositories = LoadTrustedRepositories(vpmRepositories);
                AddBuiltInRepositories(repositories);

                // Direct (top-level) dependencies for the UI prompt
                var packagesToInstall = new List<Tuple<string, string>>();
                var installWarnings = new List<string>();
                var installFailures = new List<string>();
                var packageDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Work queue + set to install dependencies recursively (transitive closure)
                var installQueue = new Queue<Tuple<string, string>>();
                var plannedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var dep in vpmDependencies.Properties())
                {
                    string packageName = dep.Name;
                    string versionRequirement = dep.Value.ToString();
                    
                    if (ShouldInstallPackage(packageName, versionRequirement, out string installWarning))
                    {
                        var tuple = new Tuple<string, string>(packageName, versionRequirement);
                        packagesToInstall.Add(tuple);
                        if (TryResolvePackageDownload(packageName, versionRequirement, repositories, out ResolvedPackageDownload previewResolution) &&
                            !string.IsNullOrEmpty(previewResolution.friendlyWarningMessage))
                        {
                            AddUniqueMessage(installWarnings, previewResolution.friendlyWarningMessage);
                        }
                        packageDisplayNames[packageName] = previewResolution != null
                            ? GetFriendlyPackageLabel(previewResolution.displayName, packageName)
                            : GetFriendlyPackageLabel(null, packageName);
                        if (plannedPackages.Add(packageName))
                        {
                            installQueue.Enqueue(tuple);
                        }
                    }
                    else if (!string.IsNullOrEmpty(installWarning))
                    {
                        installWarnings.Add(installWarning);
                        Debug.LogWarning($"[DirectVpmInstaller] {installWarning}");
                    }
                }
                
                if (packagesToInstall.Count == 0)
                {
                    if (installWarnings.Count > 0)
                    {
                        EditorUtility.DisplayDialog(
                            "Package setup note",
                            string.Join("\n\n", installWarnings),
                            "Continue"
                        );
                    }

                    EnableBundledPackagesAndFinalize(packageJsonPath, "All VPM dependencies are already installed. Enabling bundled packages directly.");
                    return;
                }
                
                string packageList = string.Join("\n", packagesToInstall.Select(p =>
                {
                    string label = packageDisplayNames.TryGetValue(p.Item1, out string knownLabel)
                        ? knownLabel
                        : GetFriendlyPackageLabel(null, p.Item1);
                    string displayName = string.Equals(label, p.Item1, StringComparison.OrdinalIgnoreCase)
                        ? label
                        : $"{label} ({p.Item1})";
                    return $"  - {displayName}@{FormatVersionRequirementForDisplay(p.Item2)}";
                }));
                bool installsImporter = packagesToInstall.Any(p => string.Equals(p.Item1, "com.yucp.importer", StringComparison.OrdinalIgnoreCase));
                string dialogMessage = installsImporter
                    ? $"This package needs the YUCP Importer and a few creator tools before Unity can open it correctly.\n\nWe'll install:\n\n{packageList}\n\nContinue now?"
                    : $"This package needs a few creator tools before Unity can open it correctly.\n\nWe'll install:\n\n{packageList}\n\nContinue now?";
                if (installWarnings.Count > 0)
                {
                    dialogMessage += "\n\nBefore you continue:\n\n" + string.Join("\n\n", installWarnings);
                }
                bool install = EditorUtility.DisplayDialog(
                    "Install required creator tools",
                    dialogMessage,
                    "Install",
                    "Not now"
                );
                
                if (!install)
                {
                    Debug.LogWarning("[DirectVpmInstaller] Dependencies not installed. Compilation errors may occur.");
                    CleanupTemporaryFiles(packageJsonPath);
                    return;
                }
                
                // Lock assemblies and disable auto-refresh
                EditorApplication.LockReloadAssemblies();
                AssetDatabase.DisallowAutoRefresh();
                AssetDatabase.StartAssetEditing();
                
                try
                {
                    Debug.Log("[DirectVpmInstaller] Installing dependencies with compilation locked (including transitive vpmDependencies)...");
                    
                    // Install all requested packages, then recursively install their vpmDependencies
                    bool allSucceeded = true;
                    while (installQueue.Count > 0)
                    {
                        var package = installQueue.Dequeue();
                        
                        // Double-check we still need this package (it may have been installed as a transitive dependency)
                        if (!ShouldInstallPackage(package.Item1, package.Item2, out string queuedWarning))
                        {
                            if (!string.IsNullOrEmpty(queuedWarning))
                                Debug.LogWarning($"[DirectVpmInstaller] {queuedWarning}");
                            continue;
                        }
                        
                        if (!InstallPackage(package.Item1, package.Item2, repositories, out string friendlyFailureMessage))
                        {
                            allSucceeded = false;
                            AddUniqueMessage(installFailures, friendlyFailureMessage);
                            continue;
                        }
                        
                        // After successful install, read its package.json and enqueue its own vpmDependencies
                        try
                        {
                            EnqueueTransitiveDependencies(package.Item1, repositories, installQueue, plannedPackages);
                        }
                        catch (Exception exDeps)
                        {
                            Debug.LogWarning($"[DirectVpmInstaller] Failed to resolve transitive dependencies for {package.Item1}: {exDeps.Message}");
                        }
                    }
                    
                if (allSucceeded)
                {
                    Debug.Log("[DirectVpmInstaller] Dependencies installed. Enabling bundled packages...");
                    
                    // Enable bundled packages while still locked with transaction/rollback safety
                    string txnId = InstallerTxn.Begin();
                    bool enableOk = false;
                    try
                    {
                        EnableBundledPackagesTransactional();
                        // Post-commit manifest verification happens after commit below
                        enableOk = true;
                    }
                    catch (Exception exEnable)
                    {
                        Debug.LogError($"[DirectVpmInstaller] Failed while enabling bundled packages: {exEnable.Message}. Rolling back...");
                        InstallerTxn.Rollback();
                        throw;
                    }
                    finally
                    {
                        if (enableOk)
                        {
                            // Verify enabled files match expected hashes BEFORE clearing transaction state
                            if (!InstallerTxn.VerifyManifest())
                                throw new Exception("Post-install manifest verification failed");
                            InstallerTxn.Commit();
                        }
                    }
                    
                    // NOTE: Do NOT add bundled packages to vpm-manifest.json - they are local packages, not from repositories
                    // Adding them causes VPM Resolver to try to resolve them from repos, resulting in "package not found" errors
                    
                    // Fix self-references in the installed package's package.json (if it exists)
                    if (!string.IsNullOrEmpty(bundledPackageName))
                    {
                        string installedPackagePath = Path.Combine(Application.dataPath, "..", "Packages", bundledPackageName);
                        string installedPackageJson = Path.Combine(installedPackagePath, "package.json");
                        if (File.Exists(installedPackageJson))
                        {
                            try
                            {
                                string packageJsonContent = File.ReadAllText(installedPackageJson);
                                var packageJson = JObject.Parse(packageJsonContent);
                                bool modified = false;
                                
                                string normalizedPackageName = bundledPackageName.ToLower().Replace(" ", ".");
                                
                                // Check vpmDependencies
                                var vpmDeps = packageJson["vpmDependencies"] as JObject;
                                if (vpmDeps != null)
                                {
                                    var toRemove = new List<string>();
                                    foreach (var dep in vpmDeps.Properties())
                                    {
                                        string depName = dep.Name;
                                        string normalizedDepName = depName.ToLower().Replace(" ", ".");
                                        if (string.Equals(normalizedDepName, normalizedPackageName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            toRemove.Add(depName);
                                        }
                                    }
                                    
                                    foreach (var depName in toRemove)
                                    {
                                        vpmDeps.Remove(depName);
                                        modified = true;
                                        Debug.Log($"[DirectVpmInstaller] Removed self-referential vpmDependency: {depName} from installed package.json");
                                    }
                                    
                                    if (vpmDeps.Count == 0)
                                    {
                                        packageJson.Remove("vpmDependencies");
                                        modified = true;
                                    }
                                }
                                
                                // Check dependencies
                                var deps = packageJson["dependencies"] as JObject;
                                if (deps != null)
                                {
                                    var toRemove = new List<string>();
                                    foreach (var dep in deps.Properties())
                                    {
                                        string depName = dep.Name;
                                        string normalizedDepName = depName.ToLower().Replace(" ", ".");
                                        if (string.Equals(normalizedDepName, normalizedPackageName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            toRemove.Add(depName);
                                        }
                                    }
                                    
                                    foreach (var depName in toRemove)
                                    {
                                        deps.Remove(depName);
                                        modified = true;
                                        Debug.Log($"[DirectVpmInstaller] Removed self-referential dependency: {depName} from installed package.json");
                                    }
                                    
                                    if (deps.Count == 0)
                                    {
                                        packageJson.Remove("dependencies");
                                        modified = true;
                                    }
                                }
                                
                                if (modified)
                                {
                                    File.WriteAllText(installedPackageJson, packageJson.ToString(Newtonsoft.Json.Formatting.Indented));
                                    Debug.Log($"[DirectVpmInstaller] Fixed self-references in installed package.json: {installedPackageJson}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[DirectVpmInstaller] Failed to fix self-references in installed package.json: {ex.Message}");
                            }
                        }
                    }
                    
                    // Clean up temporary files
                    InstallerTxn.SetMarker("complete");
                    CleanupTemporaryFiles(packageJsonPath);
                }
                    else
                    {
                        ShowInstallFailureDialog(installFailures);
                        CleanupTemporaryFiles(packageJsonPath);
                    }
                }
                finally
                {
                    // Unlock everything in one atomic operation
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.AllowAutoRefresh();
                    EditorApplication.UnlockReloadAssemblies();
                    
                    Debug.Log("[DirectVpmInstaller] Unlocked. Triggering full domain reload...");
                    
                    // Force a focus-grade full domain reload (includes UPM resolve, compile, and reload)
                    FullDomainReload.Run(() =>
                    {
                        Debug.Log("[DirectVpmInstaller] Installation complete. Domain fully reloaded with all dependencies functional.");
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DirectVpmInstaller] Error: {ex.Message}");
                try { InstallerTxn.SetMarker("error"); } catch { }
                try
                {
                    if (!string.IsNullOrEmpty(packageJsonPath))
                    {
                        CleanupTemporaryFiles(packageJsonPath);
                    }
                    else
                    {
                        PruneEmptyInstalledPackageResidue();
                    }
                }
                catch (Exception cleanupEx)
                {
                    Debug.LogWarning($"[DirectVpmInstaller] Failed to cleanup after installer error: {cleanupEx.Message}");
                }
            }
            finally
            {
                try { InstallerTxn.ClearMarker("scheduled"); } catch { }
                // Clear coordination markers
                try { InstallerTxn.ClearMarker("lock"); } catch { }
                try { InstallerTxn.ClearMarker("pending"); } catch { }
            }
        }
        
        private static void EnableBundledPackagesTransactional()
        {
            var movedFiles = new List<Tuple<string, string>>(); // Track for potential rollback
            
            try
            {
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                if (!Directory.Exists(packagesPath))
                    return;
                
                // Preflight checks: free space, write permissions
                if (!HasSufficientDiskSpace(packagesPath, 100 * 1024 * 1024L))
                    throw new Exception("Insufficient disk space for installation");
                TestWritePermission(packagesPath);

                // Find all .yucp_disabled files in Packages folder
                string[] disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                // Apply ignore patterns if present
                var ignored = LoadInstallerIgnore(packagesPath);
                if (ignored.Count > 0)
                {
                    disabledFiles = disabledFiles.Where(f => !ignored.Any(p => WildcardMatch(NormalizePath(f), p))).ToArray();
                }
                int enabledCount = 0;
                int skippedCount = 0;
                
                foreach (string disabledFile in disabledFiles)
                {
                    try
                    {
                        InstallerTxn.EnableDisabledFile(disabledFile);
                        enabledCount++;
                    }
                    catch (Exception fileEx)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to process '{Path.GetFileName(disabledFile)}': {fileEx.Message}");
                        // Continue with other files
                    }
                }
                
                if (enabledCount > 0)
                {
                    Debug.Log($"[DirectVpmInstaller] Enabled {enabledCount} bundled package files ({skippedCount} skipped as already up-to-date)");
                }
                else if (skippedCount > 0)
                {
                    Debug.Log($"[DirectVpmInstaller] All {skippedCount} bundled files were already up-to-date");
                }
                
                // Final cleanup pass: remove any remaining .yucp_disabled files
                InstallerTxn.CleanupOrphanedDisabledFiles();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DirectVpmInstaller] Critical error enabling bundled packages: {ex.Message}");
                
                // Attempt rollback
                // Rollback handled by caller via InstallerTxn.Rollback()
                throw;
            }
        }

        private static bool HasSufficientDiskSpace(string pathInDrive, long requiredBytes)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(pathInDrive));
                foreach (var di in DriveInfo.GetDrives())
                {
                    if (string.Equals(di.Name, root, StringComparison.OrdinalIgnoreCase))
                    {
                        return di.AvailableFreeSpace > requiredBytes;
                    }
                }
            }
            catch { }
            return true;
        }

        private static void TestWritePermission(string folder)
        {
            string testFile = null;
            try
            {
                testFile = Path.Combine(folder, $".yucp_write_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "test");
            }
            finally
            {
                try { if (testFile != null && File.Exists(testFile)) File.Delete(testFile); } catch { }
            }
        }

        private static List<string> LoadInstallerIgnore(string packagesPath)
        {
            var patterns = new List<string>();
            try
            {
                var ignorePath = Path.Combine(packagesPath, ".yucpinstallerignore");
                if (File.Exists(ignorePath))
                {
                    foreach (var line in File.ReadAllLines(ignorePath))
                    {
                        var t = (line ?? "").Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        patterns.Add(t.Replace('\\', '/'));
                    }
                }
            }
            catch { }
            return patterns;
        }

        private static string NormalizePath(string p) => (Path.GetFullPath(p).Replace('\\', '/'));

        private static bool WildcardMatch(string text, string pattern)
        {
            // Simple wildcard (*) match, case-insensitive
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(text, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        // Cleanup moved to InstallerTxn
        
        private static void RollbackMovedFiles(List<Tuple<string, string>> movedFiles)
        {
            if (movedFiles == null || movedFiles.Count == 0)
                return;
            
            Debug.LogWarning($"[DirectVpmInstaller] Attempting to rollback {movedFiles.Count} moved files...");
            
            foreach (var move in movedFiles)
            {
                try
                {
                    string originalPath = move.Item1; // .yucp_disabled
                    string newPath = move.Item2;      // enabled path
                    
                    if (File.Exists(newPath) && !File.Exists(originalPath))
                    {
                        File.Move(newPath, originalPath);
                        
                        string newMeta = newPath + ".meta";
                        string originalMeta = originalPath + ".meta";
                        if (File.Exists(newMeta))
                        {
                            File.Move(newMeta, originalMeta);
                        }
                        
                        Debug.Log($"[DirectVpmInstaller] Rolled back: {Path.GetFileName(originalPath)}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DirectVpmInstaller] Failed to rollback file: {ex.Message}");
                }
            }
        }
        
        private static void CleanupTemporaryFiles(string packageJsonPath)
        {
            try
            {
                // Delete the temporary package.json
                if (File.Exists(packageJsonPath))
                {
                    File.Delete(packageJsonPath);
                    string metaPath = packageJsonPath + ".meta";
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                    
                    Debug.Log($"[DirectVpmInstaller] Cleaned up temporary file: {packageJsonPath}");
                }
                
                // Leave generated installer scripts in place until the next domain comes up.
                // They are responsible for performing the post-reload self-cleanup pass.
                
                // Delete all .yucp_disabled files from bundled packages (orphaned after enabling)
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                if (Directory.Exists(packagesPath))
                {
                    string[] disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                    int deletedCount = 0;
                    
                    foreach (string disabledFile in disabledFiles)
                    {
                        File.Delete(disabledFile);
                        string metaPath = disabledFile + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        
                        deletedCount++;
                    }
                    
                    if (deletedCount > 0)
                    {
                        Debug.Log($"[DirectVpmInstaller] Deleted {deletedCount} orphaned .yucp_disabled files");
                    }
                }
                
                // Organize YUCP-generated artifacts (e.g. YUCP_PackageInfo.json) into a local package
                OrganizeYucpArtifacts();
                PruneEmptyInstalledPackageResidue();
                
                // Clean up signing folder if it exists (from signed package imports)
                string signingFolder = Path.Combine(Application.dataPath, "_Signing");
                if (Directory.Exists(signingFolder))
                {
                    try
                    {
                        Directory.Delete(signingFolder, true);
                        string signingMeta = signingFolder + ".meta";
                        if (File.Exists(signingMeta))
                            File.Delete(signingMeta);
                        Debug.Log($"[DirectVpmInstaller] Cleaned up signing folder: {signingFolder}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to cleanup signing folder: {ex.Message}");
                    }
                }

                // Refresh AssetDatabase to reflect file deletions and moves
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to cleanup temporary files: {ex.Message}");
            }
        }

        private static bool HasGeneratedInstallerArtifacts(string projectRoot)
        {
            foreach (string editorPath in GetInstallerEditorPaths(projectRoot))
            {
                if (!Directory.Exists(editorPath))
                    continue;

                foreach (string pattern in GeneratedInstallerArtifactPatterns)
                {
                    if (Directory.GetFiles(editorPath, pattern, SearchOption.TopDirectoryOnly).Length > 0)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Move YUCP-generated artifacts (metadata and helper assets) out of Assets/
        /// into a dedicated local package under Packages/yucp.installed-packages.
        /// This runs regardless of whether com.yucp.components is installed.
        /// </summary>
        private static void OrganizeYucpArtifacts()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string packagesRoot = Path.Combine(projectRoot, "Packages");
                string installedRoot = Path.Combine(packagesRoot, "yucp.installed-packages");
                string exportProfilesDiskPath = Path.Combine(Application.dataPath, "YUCP", "ExportProfiles");

                if (!Directory.Exists(packagesRoot))
                    Directory.CreateDirectory(packagesRoot);
                if (!Directory.Exists(installedRoot))
                    Directory.CreateDirectory(installedRoot);

                // Read package name from YUCP_PackageInfo.json if present
                string metadataDiskPath = Path.Combine(Application.dataPath, "YUCP_PackageInfo.json");
                try
                {
                    if (Directory.Exists(installedRoot))
                    {
                        string[] candidates = Directory.GetFiles(installedRoot, "YUCP_PackageInfo.json", SearchOption.AllDirectories);
                        if (candidates.Length > 0)
                            metadataDiskPath = candidates[0];
                    }
                }
                catch { }
                bool metadataExists = File.Exists(metadataDiskPath);
                bool metadataAlreadyInInstalled = metadataExists &&
                    metadataDiskPath.StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase);
                bool hasExportProfiles = Directory.Exists(exportProfilesDiskPath);

                if (!metadataExists && !hasExportProfiles)
                {
                    return;
                }

                string packageFolderDiskPath = metadataAlreadyInInstalled
                    ? Path.GetDirectoryName(metadataDiskPath)
                    : null;

                if (metadataExists && string.IsNullOrEmpty(packageFolderDiskPath))
                {
                    string packageFolderName = null;
                    try
                    {
                        string json = File.ReadAllText(metadataDiskPath);
                        var meta = JsonUtility.FromJson<YucpPackageMetadata>(json);
                        if (meta != null && !string.IsNullOrEmpty(meta.packageName))
                        {
                            packageFolderName = MakeSafeFolderName(meta.packageName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to read YUCP_PackageInfo.json metadata: {ex.Message}");
                    }
                    if (string.IsNullOrEmpty(packageFolderName))
                    {
                        packageFolderName = "package-" + Guid.NewGuid().ToString("N");
                    }

                    packageFolderDiskPath = Path.Combine(installedRoot, packageFolderName);
                }

                if (string.IsNullOrEmpty(packageFolderDiskPath))
                {
                    packageFolderDiskPath = Path.Combine(installedRoot, "package-" + Guid.NewGuid().ToString("N"));
                }

                if (!Directory.Exists(packageFolderDiskPath))
                {
                    Directory.CreateDirectory(packageFolderDiskPath);
                }

                // Move YUCP_PackageInfo.json into the installed-packages container if it exists (disk-level move)
                if (metadataExists && !metadataAlreadyInInstalled)
                {
                    string targetMetadataDiskPath = Path.Combine(packageFolderDiskPath, "YUCP_PackageInfo.json");
                    try
                    {
                        // Ensure parent exists
                        Directory.CreateDirectory(Path.GetDirectoryName(targetMetadataDiskPath) ?? packageFolderDiskPath);

                        // Delete existing target if it exists
                        if (File.Exists(targetMetadataDiskPath))
                        {
                            File.Delete(targetMetadataDiskPath);
                        }
                        string dstMeta = targetMetadataDiskPath + ".meta";
                        if (File.Exists(dstMeta))
                        {
                            File.Delete(dstMeta);
                        }

                        // Move JSON
                        File.Move(metadataDiskPath, targetMetadataDiskPath);
                        // Move .meta if present
                        string srcMeta = metadataDiskPath + ".meta";
                        if (File.Exists(srcMeta))
                        {
                            File.Move(srcMeta, dstMeta);
                        }

                        Debug.Log($"[DirectVpmInstaller] Moved YUCP_PackageInfo.json to '{targetMetadataDiskPath}'");
                    }
                    catch (Exception moveEx)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to move YUCP_PackageInfo.json to installed-packages: {moveEx.Message}");
                    }
                }

                // Optionally move YUCP/ExportProfiles into the same container if present (disk-level move).
                // This keeps exporter profiles from cluttering the Assets root.
                if (hasExportProfiles)
                {
                    string targetProfilesDiskParent = Path.Combine(packageFolderDiskPath, "YUCP");
                    string targetProfilesDiskPath = Path.Combine(targetProfilesDiskParent, "ExportProfiles");
                    try
                    {
                        Directory.CreateDirectory(targetProfilesDiskParent);
                        
                        // Delete existing target directory if it exists
                        if (Directory.Exists(targetProfilesDiskPath))
                        {
                            Directory.Delete(targetProfilesDiskPath, true);
                        }
                        // Also delete .meta if present
                        string targetMeta = targetProfilesDiskPath + ".meta";
                        if (File.Exists(targetMeta))
                        {
                            File.Delete(targetMeta);
                        }
                        
                        Directory.Move(exportProfilesDiskPath, targetProfilesDiskPath);

                        Debug.Log($"[DirectVpmInstaller] Moved ExportProfiles folder to '{targetProfilesDiskPath}'");
                    }
                    catch (Exception moveEx)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to move ExportProfiles folder to installed-packages: {moveEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] OrganizeYucpArtifacts failed: {ex.Message}");
            }
        }

        private static void PruneEmptyInstalledPackageResidue()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string installedRoot = Path.Combine(projectRoot, "Packages", "yucp.installed-packages");
                if (!Directory.Exists(installedRoot))
                    return;

                string[] directories = Directory.GetDirectories(installedRoot, "*", SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length)
                    .ToArray();

                foreach (string directory in directories)
                {
                    if (Directory.EnumerateFileSystemEntries(directory).Any())
                        continue;

                    Directory.Delete(directory);
                    string metaFile = directory + ".meta";
                    if (File.Exists(metaFile))
                        File.Delete(metaFile);
                }

                TryDeleteEmptyInstalledPackagesEditorFolder(projectRoot);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to prune empty installed package residue: {ex.Message}");
            }
        }

        [Serializable]
        private class YucpPackageMetadata
        {
            public string packageName;
            public string version;
            public string author;
            public string description;
        }

        /// <summary>
        /// Generate a filesystem-safe folder name from a package name.
        /// </summary>
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
        
        private static bool ShouldInstallPackage(string packageName, string versionRequirement, out string warningMessage)
        {
            warningMessage = null;
            if (!IsSafePackageName(packageName))
            {
                warningMessage = $"Skipping invalid package name '{packageName}'.";
                return false;
            }

            string installedVersion = GetInstalledPackageVersion(packageName);
            if (string.IsNullOrEmpty(installedVersion))
                return true;

            string normalizedRequirement = NormalizeRequirement(versionRequirement);
            if (IsExactVersionRequirement(normalizedRequirement))
            {
                int compare = CompareVersions(installedVersion, normalizedRequirement);
                if (compare > 0)
                {
                    warningMessage =
                        $"Requested {packageName}@{normalizedRequirement}, but the project already has newer {installedVersion}. Downgrades are not supported, so the installed version will be kept.";
                    return false;
                }

                return compare < 0;
            }

            return !VersionSatisfiesRequirement(installedVersion, normalizedRequirement);
        }

        private static string GetInstalledPackageVersion(string packageName)
        {
            if (!TryGetInstalledPackageJsonPath(packageName, out string packageJsonPath))
                return null;
            if (!File.Exists(packageJsonPath))
                return null;

            try
            {
                var packageData = JObject.Parse(File.ReadAllText(packageJsonPath));
                return packageData["version"]?.ToString();
            }
            catch
            {
                return null;
            }
        }
        
        private static bool InstallPackage(string packageName, string versionRequirement, Dictionary<string, string> repositories, out string friendlyFailureMessage)
        {
            friendlyFailureMessage = null;
            string tempDownloadPath = null;
            string stagingDirectory = null;
            string packageLabel = GetFriendlyPackageLabel(null, packageName);

            try
            {
                if (!IsSafePackageName(packageName))
                {
                    Debug.LogError($"[DirectVpmInstaller] Refusing to install invalid package name '{packageName}'.");
                    friendlyFailureMessage = $"One of the required package names was invalid ({packageName}).";
                    return false;
                }

                if (!TryResolvePackageDownload(packageName, versionRequirement, repositories, out ResolvedPackageDownload resolution))
                {
                    Debug.LogError($"[DirectVpmInstaller] Package {packageName} not found in any repository");
                    friendlyFailureMessage = $"We couldn't find a compatible download for {packageLabel} in the package sources included with this package.";
                    return false;
                }

                packageLabel = GetFriendlyPackageLabel(resolution.displayName, packageName);
                if (!string.IsNullOrEmpty(resolution.friendlyWarningMessage))
                {
                    Debug.LogWarning($"[DirectVpmInstaller] {resolution.friendlyWarningMessage}");
                }

                string workspaceRoot = GetInstallerWorkspaceRoot();
                string downloadsRoot = Path.Combine(workspaceRoot, "Downloads");
                Directory.CreateDirectory(downloadsRoot);
                string downloadExtension = string.Equals(
                    resolution.deliverySourceKind,
                    UnityPackageSourceKind,
                    StringComparison.Ordinal)
                    ? ".unitypackage"
                    : ".zip";
                tempDownloadPath = Path.Combine(
                    downloadsRoot,
                    $"{MakeSafeFolderName(packageName)}-{resolution.resolvedVersion}{downloadExtension}");
                stagingDirectory = Path.Combine(workspaceRoot, "Staging", Guid.NewGuid().ToString("N"));
                EnsureCreatorFriendlyPathLength(tempDownloadPath, packageLabel);
                EnsureCreatorFriendlyPathLength(stagingDirectory, packageLabel);

                using (var downloadClient = new WebClient())
                {
                    downloadClient.Headers.Add(HttpRequestHeader.UserAgent, "VCC/2.3.0");
                    if (resolution.requestHeaders != null)
                    {
                        foreach (var header in resolution.requestHeaders)
                        {
                            downloadClient.Headers[header.Key] = header.Value;
                        }
                    }
                    downloadClient.DownloadFile(resolution.downloadUrl, tempDownloadPath);
                }

                if (!string.IsNullOrEmpty(resolution.expectedArchiveHash))
                {
                    string actualArchiveHash = NormalizeSha256(ComputeFileSha256(tempDownloadPath));
                    if (!string.Equals(actualArchiveHash, resolution.expectedArchiveHash, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogError($"[DirectVpmInstaller] SHA-256 mismatch for {packageName}@{resolution.resolvedVersion}. Expected {resolution.expectedArchiveHash}, got {actualArchiveHash}.");
                        friendlyFailureMessage = $"We downloaded {packageLabel}, but its security fingerprint didn't match what the source promised. The install was stopped to protect your project.";
                        return false;
                    }
                }

                ExtractDownloadedPackageToDirectorySafely(
                    tempDownloadPath,
                    resolution.deliverySourceKind,
                    stagingDirectory,
                    packageLabel);

                string stagedPackageJsonPath = Path.Combine(stagingDirectory, "package.json");
                if (!File.Exists(stagedPackageJsonPath))
                {
                    Debug.LogError($"[DirectVpmInstaller] Downloaded archive for {packageName}@{resolution.resolvedVersion} did not contain package.json.");
                    friendlyFailureMessage = $"We downloaded {packageLabel}, but the package was incomplete. The install was stopped to protect your project.";
                    return false;
                }

                JObject stagedPackageJson = JObject.Parse(File.ReadAllText(stagedPackageJsonPath));
                string stagedPackageName = stagedPackageJson["name"]?.ToString();
                string stagedPackageVersion = stagedPackageJson["version"]?.ToString();
                if (!string.Equals(stagedPackageName, packageName, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogError($"[DirectVpmInstaller] Downloaded archive name mismatch. Expected {packageName}, got {stagedPackageName ?? "<missing>"}.");
                    friendlyFailureMessage = $"We downloaded {packageLabel}, but the package contents didn't match the package source. The install was stopped to protect your project.";
                    return false;
                }
                if (!string.Equals(stagedPackageVersion, resolution.resolvedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogError($"[DirectVpmInstaller] Downloaded archive version mismatch. Expected {resolution.resolvedVersion}, got {stagedPackageVersion ?? "<missing>"}.");
                    friendlyFailureMessage = $"We downloaded {packageLabel}, but the version inside the package didn't match the package source. The install was stopped to protect your project.";
                    return false;
                }

                string packageDestination = GetValidatedPackageDestination(packageName);
                if (Directory.Exists(packageDestination))
                    Directory.Delete(packageDestination, true);

                MoveDirectoryIntoPlace(stagingDirectory, packageDestination);
                stagingDirectory = null;
                
                // Add to VPM manifest so VCC recognizes it as installed
                // VPM packages from repositories should be in both dependencies and locked
                AddToVpmManifest(packageName, resolution.resolvedVersion, addToDependencies: true);
                
                Debug.Log($"[DirectVpmInstaller] Installed {packageName}@{resolution.resolvedVersion}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DirectVpmInstaller] Failed to install {packageName}: {ex.Message}");
                friendlyFailureMessage = BuildFriendlyInstallFailureMessage(packageLabel, ex);
                return false;
            }
            finally
            {
                try
                {
                if (!string.IsNullOrEmpty(tempDownloadPath) && File.Exists(tempDownloadPath))
                {
                    File.Delete(tempDownloadPath);
                }
                }
                catch
                {
                }

                try
                {
                    if (!string.IsNullOrEmpty(stagingDirectory) && Directory.Exists(stagingDirectory))
                    {
                        Directory.Delete(stagingDirectory, true);
                    }
                }
                catch
                {
                }
            }
        }
        
        private static void AddToVpmManifest(string packageName, string version, bool addToDependencies = true)
        {
            try
            {
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "vpm-manifest.json");
                JObject manifest;
                
                if (File.Exists(manifestPath))
                {
                    manifest = JObject.Parse(File.ReadAllText(manifestPath));
                }
                else
                {
                    // Create new manifest if it doesn't exist
                    manifest = new JObject();
                    manifest["dependencies"] = new JObject();
                    manifest["locked"] = new JObject();
                }
                
                // Add to dependencies section only if this is a VPM package from a repository
                // Bundled packages (local imports) should NOT be in dependencies
                if (addToDependencies)
                {
                    var dependencies = manifest["dependencies"] as JObject;
                    if (dependencies == null)
                    {
                        dependencies = new JObject();
                        manifest["dependencies"] = dependencies;
                    }
                    dependencies[packageName] = new JObject
                    {
                        ["version"] = version
                    };
                }
                else
                {
                    // Remove from dependencies if it exists (bundled packages shouldn't be there)
                    var dependencies = manifest["dependencies"] as JObject;
                    if (dependencies != null && dependencies[packageName] != null)
                    {
                        dependencies.Remove(packageName);
                    }
                }
                
                // Always add to locked section (both VPM and bundled packages are installed)
                var locked = manifest["locked"] as JObject;
                if (locked == null)
                {
                    locked = new JObject();
                    manifest["locked"] = locked;
                }
                locked[packageName] = new JObject
                {
                    ["version"] = version
                };
                
                // Save manifest
                File.WriteAllText(manifestPath, manifest.ToString(Newtonsoft.Json.Formatting.Indented));
                Debug.Log($"[DirectVpmInstaller] Added {packageName}@{version} to vpm-manifest.json (locked{(addToDependencies ? " + dependencies" : " only")})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to update vpm-manifest.json: {ex.Message}");
            }
        }
        
        private static bool VersionSatisfiesRequirement(string installedVersion, string requirement)
        {
            requirement = NormalizeRequirement(requirement);

            if (string.IsNullOrEmpty(requirement) || requirement == "*")
                return true;
            
            if (requirement.StartsWith(">="))
            {
                string minVersion = requirement.Substring(2).Trim();
                return CompareVersions(installedVersion, minVersion) >= 0;
            }
            
            if (requirement.StartsWith("^"))
            {
                string baseVersion = requirement.Substring(1).Trim();
                var baseParts = ParseVersion(baseVersion);
                var installedParts = ParseVersion(installedVersion);
                
                if (baseParts.major != installedParts.major)
                    return false;
                
                return CompareVersions(installedVersion, baseVersion) >= 0;
            }
            
            if (requirement.StartsWith("~"))
            {
                string baseVersion = requirement.Substring(1).Trim();
                var baseParts = ParseVersion(baseVersion);
                var installedParts = ParseVersion(installedVersion);
                
                if (baseParts.major != installedParts.major || baseParts.minor != installedParts.minor)
                    return false;
                
                return CompareVersions(installedVersion, baseVersion) >= 0;
            }
            
            return CompareVersions(installedVersion, requirement) == 0;
        }
        
        private static int CompareVersions(string version1, string version2)
        {
            var v1 = ParseVersion(version1);
            var v2 = ParseVersion(version2);
            
            if (v1.major != v2.major) return v1.major.CompareTo(v2.major);
            if (v1.minor != v2.minor) return v1.minor.CompareTo(v2.minor);
            return v1.patch.CompareTo(v2.patch);
        }
        
        private static (int major, int minor, int patch) ParseVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return (0, 0, 0);

            version = version.Trim().TrimStart('v', 'V');
            int dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
                version = version.Substring(0, dashIndex);
            
            var parts = version.Split('.');
            int major = parts.Length > 0 ? SafeParseVersionPart(parts[0]) : 0;
            int minor = parts.Length > 1 ? SafeParseVersionPart(parts[1]) : 0;
            int patch = parts.Length > 2 ? SafeParseVersionPart(parts[2]) : 0;
            
            return (major, minor, patch);
        }

        private static string NormalizeRequirement(string requirement)
        {
            return string.IsNullOrWhiteSpace(requirement) ? "" : requirement.Trim();
        }

        private static bool IsExactVersionRequirement(string requirement)
        {
            string normalized = NormalizeRequirement(requirement);
            if (string.IsNullOrEmpty(normalized) || normalized == "*")
                return false;

            return !normalized.StartsWith(">=") && !normalized.StartsWith("^") && !normalized.StartsWith("~");
        }

        private static string FormatVersionRequirementForDisplay(string requirement)
        {
            string normalized = NormalizeRequirement(requirement);
            return normalized == ">=0.0.0" ? "latest" : normalized;
        }

        private static int SafeParseVersionPart(string value)
        {
            return int.TryParse(value, out int parsed) ? parsed : 0;
        }
        
        private static void CleanupInstallerScript()
        {
            try
            {
                // Find and delete all YUCP installer scripts
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                int deletedCount = 0;
                
                foreach (string editorPath in GetInstallerEditorPaths(projectRoot))
                {
                    if (!Directory.Exists(editorPath))
                        continue;

                    var generatedFiles = GeneratedInstallerArtifactPatterns
                        .SelectMany(pattern => Directory.GetFiles(editorPath, pattern, SearchOption.TopDirectoryOnly))
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    foreach (string file in generatedFiles)
                    {
                        try
                        {
                            File.Delete(file);
                            string metaFile = file + ".meta";
                            if (File.Exists(metaFile))
                                File.Delete(metaFile);
                            
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[DirectVpmInstaller] Failed to delete installer file '{Path.GetFileName(file)}': {ex.Message}");
                        }
                    }
                }

                TryDeleteEmptyInstalledPackagesEditorFolder(projectRoot);
                
                if (deletedCount > 0)
                {
                    Debug.Log($"[DirectVpmInstaller] Cleaned up {deletedCount} installer script(s) to prevent duplicate assembly errors");
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Error during installer script cleanup: {ex.Message}");
            }
        }

        private static void TryDeleteEmptyInstalledPackagesEditorFolder(string projectRoot)
        {
            try
            {
                string installedEditorPath = Path.Combine(projectRoot, "Packages", "yucp.installed-packages", "Editor");
                if (!Directory.Exists(installedEditorPath))
                    return;

                if (Directory.EnumerateFileSystemEntries(installedEditorPath).Any())
                    return;

                Directory.Delete(installedEditorPath);

                string metaFile = installedEditorPath + ".meta";
                if (File.Exists(metaFile))
                    File.Delete(metaFile);

                Debug.Log($"[DirectVpmInstaller] Removed empty temporary editor folder: {installedEditorPath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to remove empty installed-packages editor folder: {ex.Message}");
            }
        }

        /// <summary>
        /// After installing a package, read its package.json and enqueue any vpmDependencies
        /// that are not yet installed, so that dependencies-of-dependencies are pulled in.
        /// </summary>
        private static void EnqueueTransitiveDependencies(
            string packageName,
            Dictionary<string, string> repositories,
            Queue<Tuple<string, string>> installQueue,
            HashSet<string> plannedPackages)
        {
            if (string.IsNullOrEmpty(packageName))
                return;

            try
            {
                if (!TryGetInstalledPackageJsonPath(packageName, out string packageJsonPath))
                    return;
                if (!File.Exists(packageJsonPath))
                    return;

                var json = JObject.Parse(File.ReadAllText(packageJsonPath));
                var vpmDependencies = json["vpmDependencies"] as JObject;

                if (vpmDependencies == null || vpmDependencies.Count == 0)
                    return;

                foreach (var dep in vpmDependencies.Properties())
                {
                    string depName = dep.Name;
                    string versionRequirement = dep.Value.ToString();

                    // Skip if already installed or if the request would require a downgrade.
                    if (!ShouldInstallPackage(depName, versionRequirement, out string installWarning))
                    {
                        if (!string.IsNullOrEmpty(installWarning))
                            Debug.LogWarning($"[DirectVpmInstaller] {installWarning}");
                        continue;
                    }

                    // Skip if we've already planned to install this package
                    if (!plannedPackages.Add(depName))
                        continue;

                    installQueue.Enqueue(new Tuple<string, string>(depName, versionRequirement));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to read transitive vpmDependencies for {packageName}: {ex.Message}");
            }
        }
        
        [MenuItem("Tools/YUCP/Others/Installation/Install VPM Dependencies")]
        public static void ManualInstallVpmDependencies()
        {
            CheckAndInstallVpmPackages();
        }
    }
}
