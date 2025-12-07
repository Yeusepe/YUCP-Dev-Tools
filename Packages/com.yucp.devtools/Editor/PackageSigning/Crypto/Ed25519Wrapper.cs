using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageSigning.Crypto
{
    /// <summary>
    /// Ed25519 wrapper for Unity using Chaos.NaCl.Standard
    /// 
    /// Installation:
    /// 1. Download from: https://www.nuget.org/packages/Chaos.NaCl.Standard/1.0.0
    /// 2. Extract the .nupkg file (rename to .zip)
    /// 3. Copy lib/netstandard2.0/Chaos.NaCl.dll to Assets/Plugins/
    /// 4. The wrapper will automatically detect and use it
    /// </summary>
    public static class Ed25519Wrapper
    {
        private const int PublicKeySize = 32;
        private const int PrivateKeySize = 32; // Seed size for Chaos.NaCl
        private const int ExpandedPrivateKeySize = 64; // Expanded private key size
        private const int SignatureSize = 64;

        private static bool _useChaosNaCl = false;
        private static bool _initialized = false;

        static Ed25519Wrapper()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;

            // Try to detect Chaos.NaCl by checking loaded assemblies (similar to Harmony pattern)
            try
            {
                Assembly chaosNaClAssembly = null;
                
                // First, check already loaded assemblies
                Debug.Log("[Ed25519Wrapper] Checking loaded assemblies...");
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var fullName = assembly.FullName;
                    // Check for various possible assembly name patterns
                    if (fullName.StartsWith("Chaos.NaCl") || 
                        fullName.Contains("Chaos.NaCl") ||
                        fullName.StartsWith("ChaosNaCl") ||
                        assembly.GetName().Name == "Chaos.NaCl" ||
                        assembly.GetName().Name == "ChaosNaCl")
                    {
                        chaosNaClAssembly = assembly;
                        Debug.Log($"[Ed25519Wrapper] Found Chaos.NaCl in loaded assemblies: {fullName}");
                        break;
                    }
                }

                // If not found in loaded assemblies, try to load from Plugins folder
                // Check both main project and package locations
                if (chaosNaClAssembly == null)
                {
                    // Get package path
                    string packagePath = "Packages/com.yucp.devtools";
                    string packageFullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", packagePath));
                    
                    // Get the root project folder (parent of Assets)
                    string projectRoot = Path.GetDirectoryName(Application.dataPath);
                    
                    string[] possiblePaths = new[]
                    {
                        // Root Plugins folder (where it actually is!)
                        Path.Combine(projectRoot, "Plugins", "Chaos.NaCl.dll"),
                        // Main project Plugins
                        Path.Combine(Application.dataPath, "Plugins", "Chaos.NaCl.dll"),
                        Path.Combine(Application.dataPath, "Plugins", "x86_64", "Chaos.NaCl.dll"),
                        Path.Combine(Application.dataPath, "Plugins", "x86", "Chaos.NaCl.dll"),
                        // Package Plugins
                        Path.Combine(packageFullPath, "Plugins", "Chaos.NaCl.dll"),
                        Path.Combine(packageFullPath, "Runtime", "Plugins", "Chaos.NaCl.dll"),
                        Path.Combine(packageFullPath, "Editor", "Plugins", "Chaos.NaCl.dll"),
                    };

                    foreach (var dllPath in possiblePaths)
                    {
                        try
                        {
                            if (File.Exists(dllPath))
                            {
                                Debug.Log($"[Ed25519Wrapper] Found DLL at: {dllPath}, attempting to load...");
                                chaosNaClAssembly = Assembly.LoadFrom(dllPath);
                                Debug.Log($"[Ed25519Wrapper] Successfully loaded Chaos.NaCl from {dllPath}, Assembly: {chaosNaClAssembly.FullName}");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[Ed25519Wrapper] Failed to load Chaos.NaCl from {dllPath}: {ex.Message}");
                        }
                    }
                    
                    // If still not found, list all assemblies to help debug
                    if (chaosNaClAssembly == null)
                    {
                        Debug.LogWarning("[Ed25519Wrapper] Chaos.NaCl.dll not found. Listing all loaded assemblies that might be related:");
                        foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                        {
                            var name = assembly.GetName().Name;
                            if (name.Contains("Chaos") || name.Contains("NaCl") || name.Contains("Ed25519"))
                            {
                                Debug.Log($"[Ed25519Wrapper] Found potentially related assembly: {assembly.FullName} at {assembly.Location}");
                            }
                        }
                    }
                }

                // Try to get the Ed25519 type - try multiple approaches
                Type ed25519Type = null;
                
                if (chaosNaClAssembly != null)
                {
                    // Try multiple type name variations
                    ed25519Type = chaosNaClAssembly.GetType("Chaos.NaCl.Ed25519");
                    if (ed25519Type == null)
                    {
                        // Try to find it by searching all types
                        try
                        {
                            var types = chaosNaClAssembly.GetTypes();
                            foreach (var type in types)
                            {
                                if (type.Name == "Ed25519" && type.Namespace == "Chaos.NaCl")
                                {
                                    ed25519Type = type;
                                    break;
                                }
                            }
                        }
                        catch (ReflectionTypeLoadException ex)
                        {
                            // Some types might not be loadable, try the ones that are
                            foreach (var type in ex.Types)
                            {
                                if (type != null && type.Name == "Ed25519" && type.Namespace == "Chaos.NaCl")
                                {
                                    ed25519Type = type;
                                    break;
                                }
                            }
                        }
                    }
                }
                
                // Also try Type.GetType directly (Unity might have loaded it with a different assembly name)
                if (ed25519Type == null)
                {
                    ed25519Type = Type.GetType("Chaos.NaCl.Ed25519, Chaos.NaCl");
                }
                
                // Try searching all loaded assemblies for the type
                if (ed25519Type == null)
                {
                    foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            ed25519Type = assembly.GetType("Chaos.NaCl.Ed25519");
                            if (ed25519Type != null)
                            {
                                Debug.Log($"[Ed25519Wrapper] Found Ed25519 type in assembly: {assembly.FullName}");
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (ed25519Type != null)
                {
                    _useChaosNaCl = true;
                    _initialized = true;
                    UnityEngine.Debug.Log($"[Ed25519Wrapper] Using Chaos.NaCl library, Type: {ed25519Type.FullName}");
                    return;
                }
                else if (chaosNaClAssembly != null)
                {
                    Debug.LogWarning($"[Ed25519Wrapper] Chaos.NaCl assembly loaded ({chaosNaClAssembly.FullName}) but Ed25519 type not found.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Ed25519Wrapper] Error detecting Chaos.NaCl: {ex.Message}\n{ex.StackTrace}");
            }

            // No fallback - require Chaos.NaCl for security
            UnityEngine.Debug.LogError("[Ed25519Wrapper] Chaos.NaCl.Standard not found. Ed25519 operations require Chaos.NaCl.dll to be installed in the Plugins folder.");
            _initialized = true;
        }

        /// <summary>
        /// Generate a new Ed25519 keypair
        /// Returns: (publicKey, privateKey) where privateKey is the 32-byte seed
        /// </summary>
        public static (byte[] publicKey, byte[] privateKey) GenerateKeyPair()
        {
            if (_useChaosNaCl)
            {
                return GenerateKeyPairChaosNaCl();
            }
            else
            {
                throw new InvalidOperationException(
                    "Ed25519 key generation requires Chaos.NaCl.Standard library. " +
                    "Please install Chaos.NaCl.dll in your project's Plugins folder. " +
                    "Download from: https://www.nuget.org/packages/Chaos.NaCl.Standard/1.0.0 " +
                    "and extract lib/netstandard2.0/Chaos.NaCl.dll to Assets/Plugins/");
            }
        }

        /// <summary>
        /// Sign data with private key (32-byte seed)
        /// </summary>
        public static byte[] Sign(byte[] data, byte[] privateKey)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (privateKey == null || privateKey.Length != PrivateKeySize)
                throw new ArgumentException("Invalid private key (must be 32 bytes)", nameof(privateKey));

            if (_useChaosNaCl)
            {
                return SignChaosNaCl(data, privateKey);
            }
            else
            {
                throw new InvalidOperationException(
                    "Ed25519 signing requires Chaos.NaCl.Standard library. " +
                    "Please install Chaos.NaCl.dll in your project's Plugins folder. " +
                    "Download from: https://www.nuget.org/packages/Chaos.NaCl.Standard/1.0.0 " +
                    "and extract lib/netstandard2.0/Chaos.NaCl.dll to Assets/Plugins/");
            }
        }

        /// <summary>
        /// Verify signature with public key
        /// </summary>
        public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (signature == null || signature.Length != SignatureSize)
                throw new ArgumentException("Invalid signature (must be 64 bytes)", nameof(signature));
            if (publicKey == null || publicKey.Length != PublicKeySize)
                throw new ArgumentException("Invalid public key (must be 32 bytes)", nameof(publicKey));

            if (_useChaosNaCl)
            {
                return VerifyChaosNaCl(data, signature, publicKey);
            }
            else
            {
                throw new InvalidOperationException(
                    "Ed25519 verification requires Chaos.NaCl.Standard library. " +
                    "Please install Chaos.NaCl.dll in your project's Plugins folder. " +
                    "Download from: https://www.nuget.org/packages/Chaos.NaCl.Standard/1.0.0 " +
                    "and extract lib/netstandard2.0/Chaos.NaCl.dll to Assets/Plugins/");
            }
        }

        /// <summary>
        /// Get public key from private key (32-byte seed)
        /// </summary>
        public static byte[] GetPublicKey(byte[] privateKey)
        {
            if (privateKey == null || privateKey.Length != PrivateKeySize)
                throw new ArgumentException("Invalid private key (must be 32 bytes)", nameof(privateKey));

            if (_useChaosNaCl)
            {
                return GetPublicKeyChaosNaCl(privateKey);
            }
            else
            {
                throw new InvalidOperationException(
                    "Ed25519 public key derivation requires Chaos.NaCl.Standard library. " +
                    "Please install Chaos.NaCl.dll in your project's Plugins folder. " +
                    "Download from: https://www.nuget.org/packages/Chaos.NaCl.Standard/1.0.0 " +
                    "and extract lib/netstandard2.0/Chaos.NaCl.dll to Assets/Plugins/");
            }
        }

        #region Chaos.NaCl Implementation

        private static (byte[] publicKey, byte[] privateKey) GenerateKeyPairChaosNaCl()
        {
            // Generate 32-byte seed
            byte[] seed = new byte[PrivateKeySize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(seed);
            }

            // Get Ed25519 type via reflection
            var ed25519Type = Type.GetType("Chaos.NaCl.Ed25519, Chaos.NaCl");
            var publicKeyFromSeedMethod = ed25519Type.GetMethod("PublicKeyFromSeed", new[] { typeof(byte[]) });
            
            byte[] publicKey = (byte[])publicKeyFromSeedMethod.Invoke(null, new object[] { seed });

            // Return seed as private key (for storage)
            return (publicKey, seed);
        }

        private static byte[] SignChaosNaCl(byte[] data, byte[] seed)
        {
            // Get Ed25519 type via reflection
            var ed25519Type = Type.GetType("Chaos.NaCl.Ed25519, Chaos.NaCl");
            
            // Expand private key from seed
            var expandedPrivateKeyFromSeedMethod = ed25519Type.GetMethod("ExpandedPrivateKeyFromSeed", new[] { typeof(byte[]) });
            byte[] expandedPrivateKey = (byte[])expandedPrivateKeyFromSeedMethod.Invoke(null, new object[] { seed });

            // Sign with expanded private key
            // Chaos.NaCl.Ed25519.Sign signature: Sign(byte[] message, byte[] expandedPrivateKey)
            var signMethod = ed25519Type.GetMethod("Sign", new[] { typeof(byte[]), typeof(byte[]) });
            byte[] signature = (byte[])signMethod.Invoke(null, new object[] { data, expandedPrivateKey });

            return signature;
        }

        private static bool VerifyChaosNaCl(byte[] data, byte[] signature, byte[] publicKey)
        {
            // Get Ed25519 type via reflection
            var ed25519Type = Type.GetType("Chaos.NaCl.Ed25519, Chaos.NaCl");
            var verifyMethod = ed25519Type.GetMethod("Verify", new[] { typeof(byte[]), typeof(byte[]), typeof(byte[]) });
            
            bool isValid = (bool)verifyMethod.Invoke(null, new object[] { signature, data, publicKey });
            return isValid;
        }

        private static byte[] GetPublicKeyChaosNaCl(byte[] seed)
        {
            // Get Ed25519 type via reflection
            var ed25519Type = Type.GetType("Chaos.NaCl.Ed25519, Chaos.NaCl");
            var publicKeyFromSeedMethod = ed25519Type.GetMethod("PublicKeyFromSeed", new[] { typeof(byte[]) });
            
            byte[] publicKey = (byte[])publicKeyFromSeedMethod.Invoke(null, new object[] { seed });
            return publicKey;
        }

        #endregion

    }
}
