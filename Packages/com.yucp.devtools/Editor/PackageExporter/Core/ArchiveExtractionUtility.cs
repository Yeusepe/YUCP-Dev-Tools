using System;
using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class ArchiveExtractionUtility
    {
        internal static void ExtractZipSafely(string zipPath, string extractPath)
        {
            string normalizedExtractRoot = EnsureExtractionRoot(extractPath);
            using var zipStream = new ZipInputStream(File.OpenRead(zipPath));

            ZipEntry entry;
            while ((entry = zipStream.GetNextEntry()) != null)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                string entryPath = GetValidatedExtractionPath(normalizedExtractRoot, entry.Name, zipPath);
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(entryPath);
                    continue;
                }

                string directoryName = Path.GetDirectoryName(entryPath);
                if (!string.IsNullOrEmpty(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }

                using var writer = File.Create(entryPath);
                byte[] buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = zipStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    writer.Write(buffer, 0, bytesRead);
                }
            }
        }

        internal static void ExtractUnityPackageSafely(string unityPackagePath, string extractPath)
        {
            string normalizedExtractRoot = EnsureExtractionRoot(extractPath);
            using var fileStream = File.OpenRead(unityPackagePath);
            using var gzipStream = new GZipInputStream(fileStream);
            using var tarStream = new TarInputStream(gzipStream, Encoding.UTF8);

            TarEntry entry;
            while ((entry = tarStream.GetNextEntry()) != null)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Name))
                    continue;

                string entryPath = GetValidatedExtractionPath(normalizedExtractRoot, entry.Name, unityPackagePath);
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(entryPath);
                    continue;
                }

                string directoryName = Path.GetDirectoryName(entryPath);
                if (!string.IsNullOrEmpty(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }

                using var writer = File.Create(entryPath);
                tarStream.CopyEntryContents(writer);
            }
        }

        private static string EnsureExtractionRoot(string extractPath)
        {
            if (string.IsNullOrWhiteSpace(extractPath))
                throw new ArgumentException("Extraction path cannot be empty.", nameof(extractPath));

            Directory.CreateDirectory(extractPath);
            return Path.GetFullPath(extractPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }

        private static string GetValidatedExtractionPath(string normalizedExtractRoot, string entryName, string archivePath)
        {
            string normalizedEntry = entryName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string candidatePath = Path.GetFullPath(Path.Combine(normalizedExtractRoot, normalizedEntry));

            if (!candidatePath.StartsWith(normalizedExtractRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Archive entry '{entryName}' from '{archivePath}' escapes the extraction root '{normalizedExtractRoot}'.");
            }

            return candidatePath;
        }
    }
}
