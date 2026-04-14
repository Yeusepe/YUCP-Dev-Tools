using System;
using System.Collections.Generic;

namespace YUCP.DevTools.Editor.PackageSigning.Data
{
    public static class SigningTrustDefaults
    {
        public const string PrimaryRootKeyId = "yucp-root";
        public const string LegacyRootKeyId = "yucp-root-2025";
        public const string RootAlgorithm = "Ed25519";
        public const string PinnedRootPublicKeyBase64 = "y+8Zs9/mS1MFZFeF4CFjwqe0nsLW8lCcwmyvBx6H0Zo=";

        private static readonly TrustedRootKey[] s_pinnedTrustedRootKeys =
        {
            new TrustedRootKey
            {
                keyId = PrimaryRootKeyId,
                algorithm = RootAlgorithm,
                publicKeyBase64 = PinnedRootPublicKeyBase64,
            },
            new TrustedRootKey
            {
                keyId = LegacyRootKeyId,
                algorithm = RootAlgorithm,
                publicKeyBase64 = PinnedRootPublicKeyBase64,
            },
        };

        private static readonly HashSet<string> s_trustedServerUrls =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SigningSettings.DefaultServerUrl,
            };

        public static string ResolveTrustedServerUrl(string configuredServerUrl)
        {
            string normalized = SigningSettings.NormalizeConfiguredServerUrl(configuredServerUrl);
            return s_trustedServerUrls.Contains(normalized)
                ? normalized
                : SigningSettings.DefaultServerUrl;
        }

        public static bool TryGetPinnedRootPublicKey(string keyId, string algorithm, out string publicKeyBase64)
        {
            publicKeyBase64 = null;
            if (string.IsNullOrWhiteSpace(keyId) ||
                !string.Equals(algorithm, RootAlgorithm, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var trustedRootKey in s_pinnedTrustedRootKeys)
            {
                if (!string.Equals(trustedRootKey.keyId, keyId.Trim(), StringComparison.Ordinal))
                    continue;

                publicKeyBase64 = trustedRootKey.publicKeyBase64;
                return true;
            }

            return false;
        }

        public static List<TrustedRootKey> CreatePinnedTrustedRootKeys()
        {
            var keys = new List<TrustedRootKey>(s_pinnedTrustedRootKeys.Length);
            foreach (var trustedRootKey in s_pinnedTrustedRootKeys)
            {
                keys.Add(new TrustedRootKey
                {
                    keyId = trustedRootKey.keyId,
                    algorithm = trustedRootKey.algorithm,
                    publicKeyBase64 = trustedRootKey.publicKeyBase64,
                });
            }

            return keys;
        }

        public static List<TrustedRootKey> FilterPinnedTrustedRoots(IEnumerable<TrustedRootKey> trustedKeys)
        {
            var filtered = new List<TrustedRootKey>();
            if (trustedKeys == null)
                return filtered;

            foreach (var trustedKey in trustedKeys)
            {
                if (trustedKey == null)
                    continue;

                if (!TryGetPinnedRootPublicKey(
                        trustedKey.keyId,
                        string.IsNullOrWhiteSpace(trustedKey.algorithm) ? RootAlgorithm : trustedKey.algorithm,
                        out string pinnedPublicKeyBase64))
                {
                    continue;
                }

                if (!string.Equals(
                        trustedKey.publicKeyBase64?.Trim(),
                        pinnedPublicKeyBase64,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                filtered.Add(new TrustedRootKey
                {
                    keyId = trustedKey.keyId.Trim(),
                    algorithm = RootAlgorithm,
                    publicKeyBase64 = pinnedPublicKeyBase64,
                });
            }

            return filtered;
        }
    }
}
