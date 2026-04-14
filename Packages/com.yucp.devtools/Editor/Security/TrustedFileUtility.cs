using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace YUCP.DevTools.Editor.Security
{
    internal static class TrustedFileUtility
    {
        internal static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            string normalizedLeft = Path.GetFullPath(left)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRight = Path.GetFullPath(right)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool FileMatchesSha256(string path, string expectedHash, out string actualHash)
        {
            actualHash = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            string normalizedExpectedHash = NormalizeHash(expectedHash);
            if (string.IsNullOrEmpty(normalizedExpectedHash))
                return false;

            actualHash = ComputeSha256(path);
            return string.Equals(actualHash, normalizedExpectedHash, StringComparison.OrdinalIgnoreCase);
        }

        internal static void EnsureFileMatchesSha256(string path, string expectedHash, string description)
        {
            if (FileMatchesSha256(path, expectedHash, out string actualHash))
                return;

            throw new InvalidDataException(
                $"{description} failed pinned SHA-256 validation. Expected {NormalizeHash(expectedHash)}, got {actualHash ?? "<missing>"}.");
        }

        internal static bool IsSha256(string hash)
        {
            string normalized = NormalizeHash(hash);
            return normalized != null && normalized.Length == 64 && normalized.All(Uri.IsHexDigit);
        }

        internal static string NormalizeHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return null;

            return hash.Trim().Replace("-", string.Empty).ToUpperInvariant();
        }

        internal static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }
    }
}
