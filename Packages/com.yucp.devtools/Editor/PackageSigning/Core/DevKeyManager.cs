using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using YUCP.DevTools.Editor.PackageSigning.Data;
using YUCP.DevTools.Editor.PackageSigning.Crypto;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    /// <summary>
    /// Manages developer Ed25519 keypair generation and storage
    /// </summary>
    public static class DevKeyManager
    {
        private static string DevKeyPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".unitysign",
            "devkey.json"
        );

        private static DevKeyPair _cachedKeyPair;

        /// <summary>
        /// Get or create dev keypair
        /// </summary>
        public static DevKeyPair GetOrCreateDevKey()
        {
            if (_cachedKeyPair != null)
                return _cachedKeyPair;

            // Try to load from disk
            if (File.Exists(DevKeyPath))
            {
                try
                {
                    string json = File.ReadAllText(DevKeyPath);
                    _cachedKeyPair = JsonUtility.FromJson<DevKeyPair>(json);
                    
                    // Decrypt private key if needed
                    if (!string.IsNullOrEmpty(_cachedKeyPair.privateKey))
                    {
                        _cachedKeyPair.privateKey = DecryptPrivateKey(_cachedKeyPair.privateKey);
                    }
                    
                    return _cachedKeyPair;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to load dev key: {ex.Message}");
                }
            }

            // Generate new keypair
            Debug.Log("Generating new Ed25519 dev keypair...");
            var (publicKey, privateKey) = Ed25519Wrapper.GenerateKeyPair();
            
            _cachedKeyPair = new DevKeyPair
            {
                algorithm = "Ed25519",
                publicKey = Convert.ToBase64String(publicKey),
                privateKey = Convert.ToBase64String(privateKey)
            };

            // Save to disk (encrypted)
            SaveDevKey(_cachedKeyPair);

            return _cachedKeyPair;
        }

        /// <summary>
        /// Get public key as base64 string
        /// </summary>
        public static string GetPublicKeyBase64()
        {
            var keyPair = GetOrCreateDevKey();
            return keyPair.publicKey;
        }

        /// <summary>
        /// Sign data with dev private key
        /// </summary>
        public static byte[] SignData(byte[] data)
        {
            var keyPair = GetOrCreateDevKey();
            var privateKey = Convert.FromBase64String(keyPair.privateKey);
            return Ed25519Wrapper.Sign(data, privateKey);
        }

        /// <summary>
        /// Save dev key to disk (with encryption)
        /// </summary>
        private static void SaveDevKey(DevKeyPair keyPair)
        {
            try
            {
                // Ensure directory exists
                string dir = Path.GetDirectoryName(DevKeyPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Encrypt private key before saving
                var keyPairToSave = new DevKeyPair
                {
                    algorithm = keyPair.algorithm,
                    publicKey = keyPair.publicKey,
                    privateKey = EncryptPrivateKey(keyPair.privateKey)
                };

                string json = JsonUtility.ToJson(keyPairToSave, true);
                File.WriteAllText(DevKeyPath, json);
                
                // Set file permissions (Unix-like systems)
                #if UNITY_EDITOR_LINUX || UNITY_EDITOR_OSX
                try
                {
                    System.Diagnostics.Process.Start("chmod", $"600 {DevKeyPath}").WaitForExit();
                }
                catch { }
                #endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save dev key: {ex.Message}");
            }
        }

        /// <summary>
        /// Encrypt private key with machine-specific key
        /// </summary>
        private static string EncryptPrivateKey(string privateKeyBase64)
        {
            try
            {
                byte[] key = GetMachineKey();
                byte[] data = Convert.FromBase64String(privateKeyBase64);
                
                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.Mode = CipherMode.CBC;
                    aes.GenerateIV();
                    
                    using (var encryptor = aes.CreateEncryptor())
                    using (var ms = new MemoryStream())
                    {
                        ms.Write(aes.IV, 0, aes.IV.Length);
                        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                        {
                            cs.Write(data, 0, data.Length);
                        }
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch
            {
                // If encryption fails, return as-is (not ideal but better than nothing)
                return privateKeyBase64;
            }
        }

        /// <summary>
        /// Decrypt private key
        /// </summary>
        private static string DecryptPrivateKey(string encryptedBase64)
        {
            try
            {
                byte[] key = GetMachineKey();
                byte[] encrypted = Convert.FromBase64String(encryptedBase64);
                
                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.Mode = CipherMode.CBC;
                    
                    byte[] iv = new byte[16];
                    Array.Copy(encrypted, 0, iv, 0, 16);
                    aes.IV = iv;
                    
                    using (var decryptor = aes.CreateDecryptor())
                    using (var ms = new MemoryStream(encrypted, 16, encrypted.Length - 16))
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    using (var result = new MemoryStream())
                    {
                        cs.CopyTo(result);
                        return Convert.ToBase64String(result.ToArray());
                    }
                }
            }
            catch
            {
                // If decryption fails, assume it's not encrypted
                return encryptedBase64;
            }
        }

        /// <summary>
        /// Get machine-specific key for encryption
        /// </summary>
        private static byte[] GetMachineKey()
        {
            // Use machine name + user name as key material
            string keyMaterial = $"{Environment.MachineName}_{Environment.UserName}_YUCP_DEV_KEY";
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(keyMaterial));
            }
        }
    }
}
