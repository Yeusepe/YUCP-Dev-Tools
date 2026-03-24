using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class ProtectedContentKeyUtility
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("YUCPKEY1");

        public static string WrapContentKey(byte[] contentKey, byte[] wrappingKey)
        {
            if (contentKey == null || contentKey.Length == 0)
                throw new ArgumentException("Content key is required.", nameof(contentKey));
            if (wrappingKey == null || wrappingKey.Length != 32)
                throw new ArgumentException("Wrapping key must be 32 bytes.", nameof(wrappingKey));

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = wrappingKey;
            aes.GenerateIV();

            byte[] ciphertext;
            using (var ms = new MemoryStream())
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(contentKey, 0, contentKey.Length);
                cs.FlushFinalBlock();
                ciphertext = ms.ToArray();
            }

            byte[] hmacKey = DeriveHmacKey(wrappingKey, "YUCP|keywrap|");
            byte[] hmac;
            using (var h = new HMACSHA256(hmacKey))
            {
                byte[] ivAndCipher = new byte[aes.IV.Length + ciphertext.Length];
                Buffer.BlockCopy(aes.IV, 0, ivAndCipher, 0, aes.IV.Length);
                Buffer.BlockCopy(ciphertext, 0, ivAndCipher, aes.IV.Length, ciphertext.Length);
                hmac = h.ComputeHash(ivAndCipher);
            }

            byte[] blob = new byte[Magic.Length + aes.IV.Length + hmac.Length + ciphertext.Length];
            int offset = 0;
            Buffer.BlockCopy(Magic, 0, blob, offset, Magic.Length);
            offset += Magic.Length;
            Buffer.BlockCopy(aes.IV, 0, blob, offset, aes.IV.Length);
            offset += aes.IV.Length;
            Buffer.BlockCopy(hmac, 0, blob, offset, hmac.Length);
            offset += hmac.Length;
            Buffer.BlockCopy(ciphertext, 0, blob, offset, ciphertext.Length);
            return Convert.ToBase64String(blob);
        }

        public static bool TryUnwrapContentKey(string wrappedContentKey, byte[] wrappingKey, out byte[] contentKey)
        {
            contentKey = null;
            if (string.IsNullOrEmpty(wrappedContentKey) || wrappingKey == null || wrappingKey.Length != 32)
                return false;

            try
            {
                byte[] blob = Convert.FromBase64String(wrappedContentKey);
                int minimumLength = Magic.Length + 16 + 32 + 1;
                if (blob.Length < minimumLength)
                    return false;

                for (int i = 0; i < Magic.Length; i++)
                {
                    if (blob[i] != Magic[i])
                        return false;
                }

                int offset = Magic.Length;
                byte[] iv = new byte[16];
                Buffer.BlockCopy(blob, offset, iv, 0, iv.Length);
                offset += iv.Length;

                byte[] storedHmac = new byte[32];
                Buffer.BlockCopy(blob, offset, storedHmac, 0, storedHmac.Length);
                offset += storedHmac.Length;

                byte[] ciphertext = new byte[blob.Length - offset];
                Buffer.BlockCopy(blob, offset, ciphertext, 0, ciphertext.Length);

                byte[] hmacKey = DeriveHmacKey(wrappingKey, "YUCP|keywrap|");
                using (var h = new HMACSHA256(hmacKey))
                {
                    byte[] ivAndCipher = new byte[iv.Length + ciphertext.Length];
                    Buffer.BlockCopy(iv, 0, ivAndCipher, 0, iv.Length);
                    Buffer.BlockCopy(ciphertext, 0, ivAndCipher, iv.Length, ciphertext.Length);
                    byte[] expectedHmac = h.ComputeHash(ivAndCipher);
                    if (!CryptographicEqual(storedHmac, expectedHmac))
                        return false;
                }

                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = wrappingKey;
                aes.IV = iv;

                using var ms = new MemoryStream(ciphertext);
                using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
                using var output = new MemoryStream();
                cs.CopyTo(output);
                contentKey = output.ToArray();
                return contentKey.Length == 32;
            }
            catch
            {
                contentKey = null;
                return false;
            }
        }

        private static byte[] DeriveHmacKey(byte[] key, string prefix)
        {
            using var sha = SHA256.Create();
            byte[] prefixBytes = Encoding.UTF8.GetBytes(prefix);
            byte[] data = new byte[prefixBytes.Length + key.Length];
            Buffer.BlockCopy(prefixBytes, 0, data, 0, prefixBytes.Length);
            Buffer.BlockCopy(key, 0, data, prefixBytes.Length, key.Length);
            return sha.ComputeHash(data);
        }

        private static bool CryptographicEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
