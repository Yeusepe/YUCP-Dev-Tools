using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using YUCP.DevTools.Editor.Security;

namespace YUCP.DevTools.Editor.PackageSigning.Crypto
{
    /// <summary>
    /// Ed25519 wrapper for Unity using a pinned Chaos.NaCl.Standard binary.
    /// </summary>
    public static class Ed25519Wrapper
    {
        private const int PublicKeySize = 32;
        private const int PrivateKeySize = 32; // Seed size for Chaos.NaCl
        private const int SignatureSize = 64;
        private const string TrustedChaosNaClSha256 = "F442B14191F55536E7B72EC83A056F5ED1C55AAA2F44A0F95F00A4A24A286311";

        private static bool _useChaosNaCl;
        private static bool _initialized;
        private static Assembly _chaosNaClAssembly;
        private static Type _ed25519Type;

        static Ed25519Wrapper()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized)
                return;

            try
            {
                _chaosNaClAssembly = FindTrustedLoadedChaosNaClAssembly();
                if (_chaosNaClAssembly == null)
                {
                    foreach (string dllPath in GetTrustedChaosNaClCandidatePaths())
                    {
                        if (!File.Exists(dllPath))
                            continue;
                        if (!IsTrustedChaosNaClBinary(dllPath))
                        {
                            Debug.LogError($"[Ed25519Wrapper] Refusing to load Chaos.NaCl from untrusted path: {dllPath}");
                            continue;
                        }

                        _chaosNaClAssembly = Assembly.LoadFrom(dllPath);
                        Debug.Log($"[Ed25519Wrapper] Loaded trusted Chaos.NaCl from {dllPath}");
                        break;
                    }
                }

                if (_chaosNaClAssembly != null)
                {
                    _ed25519Type = _chaosNaClAssembly.GetType("Chaos.NaCl.Ed25519", throwOnError: false);
                    if (_ed25519Type == null)
                    {
                        try
                        {
                            _ed25519Type = _chaosNaClAssembly
                                .GetTypes()
                                .FirstOrDefault(type => type?.Namespace == "Chaos.NaCl" && type.Name == "Ed25519");
                        }
                        catch (ReflectionTypeLoadException ex)
                        {
                            _ed25519Type = ex.Types
                                .FirstOrDefault(type => type?.Namespace == "Chaos.NaCl" && type.Name == "Ed25519");
                        }
                    }
                }

                _useChaosNaCl = _ed25519Type != null;
                if (_useChaosNaCl)
                {
                    Debug.Log($"[Ed25519Wrapper] Using trusted Chaos.NaCl type {_ed25519Type.FullName}");
                }
                else
                {
                    Debug.LogError("[Ed25519Wrapper] Trusted Chaos.NaCl.Standard not found. Ed25519 operations require the pinned Chaos.NaCl.dll binary.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ed25519Wrapper] Error loading trusted Chaos.NaCl: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                _initialized = true;
            }
        }

        internal static bool IsTrustedChaosNaClBinary(string dllPath)
        {
            if (string.IsNullOrWhiteSpace(dllPath))
                return false;

            string fullPath = Path.GetFullPath(dllPath);
            bool isTrustedPath = GetTrustedChaosNaClCandidatePaths()
                .Any(candidate => TrustedFileUtility.PathsEqual(candidate, fullPath));

            return isTrustedPath &&
                   TrustedFileUtility.FileMatchesSha256(fullPath, TrustedChaosNaClSha256, out _);
        }

        /// <summary>
        /// Generate a new Ed25519 keypair.
        /// Returns: (publicKey, privateKey) where privateKey is the 32-byte seed.
        /// </summary>
        public static (byte[] publicKey, byte[] privateKey) GenerateKeyPair()
        {
            EnsureAvailable();

            byte[] seed = new byte[PrivateKeySize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(seed);
            }

            MethodInfo publicKeyFromSeedMethod = GetTrustedEd25519Type().GetMethod("PublicKeyFromSeed", new[] { typeof(byte[]) });
            byte[] publicKey = (byte[])publicKeyFromSeedMethod.Invoke(null, new object[] { seed });
            return (publicKey, seed);
        }

        /// <summary>
        /// Sign data with a 32-byte private key seed.
        /// </summary>
        public static byte[] Sign(byte[] data, byte[] privateKey)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (privateKey == null || privateKey.Length != PrivateKeySize)
                throw new ArgumentException("Invalid private key (must be 32 bytes)", nameof(privateKey));

            EnsureAvailable();

            Type ed25519Type = GetTrustedEd25519Type();
            MethodInfo expandedPrivateKeyFromSeedMethod = ed25519Type.GetMethod("ExpandedPrivateKeyFromSeed", new[] { typeof(byte[]) });
            byte[] expandedPrivateKey = (byte[])expandedPrivateKeyFromSeedMethod.Invoke(null, new object[] { privateKey });

            MethodInfo signMethod = ed25519Type.GetMethod("Sign", new[] { typeof(byte[]), typeof(byte[]) });
            return (byte[])signMethod.Invoke(null, new object[] { data, expandedPrivateKey });
        }

        /// <summary>
        /// Verify a signature with a public key.
        /// </summary>
        public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (signature == null || signature.Length != SignatureSize)
                throw new ArgumentException("Invalid signature (must be 64 bytes)", nameof(signature));
            if (publicKey == null || publicKey.Length != PublicKeySize)
                throw new ArgumentException("Invalid public key (must be 32 bytes)", nameof(publicKey));

            EnsureAvailable();

            MethodInfo verifyMethod = GetTrustedEd25519Type().GetMethod("Verify", new[] { typeof(byte[]), typeof(byte[]), typeof(byte[]) });
            return (bool)verifyMethod.Invoke(null, new object[] { signature, data, publicKey });
        }

        /// <summary>
        /// Get a public key from a 32-byte private key seed.
        /// </summary>
        public static byte[] GetPublicKey(byte[] privateKey)
        {
            if (privateKey == null || privateKey.Length != PrivateKeySize)
                throw new ArgumentException("Invalid private key (must be 32 bytes)", nameof(privateKey));

            EnsureAvailable();

            MethodInfo publicKeyFromSeedMethod = GetTrustedEd25519Type().GetMethod("PublicKeyFromSeed", new[] { typeof(byte[]) });
            return (byte[])publicKeyFromSeedMethod.Invoke(null, new object[] { privateKey });
        }

        private static void EnsureAvailable()
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (!_useChaosNaCl || _ed25519Type == null)
            {
                throw new InvalidOperationException(
                    "Ed25519 operations require the pinned Chaos.NaCl.Standard library. " +
                    "Please install the trusted Chaos.NaCl.dll binary in the project's Plugins folder.");
            }
        }

        private static Type GetTrustedEd25519Type()
        {
            EnsureAvailable();
            return _ed25519Type;
        }

        private static Assembly FindTrustedLoadedChaosNaClAssembly()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, "Chaos.NaCl", StringComparison.OrdinalIgnoreCase))
                    continue;

                string location = string.Empty;
                try
                {
                    location = assembly.Location;
                }
                catch
                {
                }

                if (!string.IsNullOrWhiteSpace(location) && IsTrustedChaosNaClBinary(location))
                {
                    return assembly;
                }

                if (string.IsNullOrWhiteSpace(location))
                {
                    Debug.LogWarning("[Ed25519Wrapper] A loaded Chaos.NaCl assembly did not expose its location. Falling back to the pinned on-disk binary.");
                    continue;
                }

                throw new InvalidOperationException(
                    $"An untrusted Chaos.NaCl assembly is already loaded from '{location}'. " +
                    "Refusing to continue with Ed25519 operations.");
            }

            return null;
        }

        private static string[] GetTrustedChaosNaClCandidatePaths()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string packageRoot = Path.Combine(projectRoot, "Packages", "com.yucp.devtools");

            return new[]
            {
                Path.Combine(projectRoot, "Plugins", "Chaos.NaCl.dll"),
                Path.Combine(Application.dataPath, "Plugins", "Chaos.NaCl.dll"),
                Path.Combine(Application.dataPath, "Plugins", "x86_64", "Chaos.NaCl.dll"),
                Path.Combine(Application.dataPath, "Plugins", "x86", "Chaos.NaCl.dll"),
                Path.Combine(packageRoot, "Plugins", "Chaos.NaCl.dll"),
                Path.Combine(packageRoot, "Runtime", "Plugins", "Chaos.NaCl.dll"),
                Path.Combine(packageRoot, "Editor", "Plugins", "Chaos.NaCl.dll"),
            };
        }
    }
}
