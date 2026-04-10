using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using YUCP.DevTools.Editor.PackageSigning.Core;
using YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class CertificateTrustSyncTests
    {
        [Test]
        public void ParseTrustedRootKeys_PreservesServerAdvertisedKeyIds()
        {
            const string jwksJson = "{"
                + "\"keys\":["
                + "{"
                + "\"kty\":\"OKP\","
                + "\"crv\":\"Ed25519\","
                + "\"kid\":\"yucp-root\","
                + "\"x\":\"server-key\""
                + "},"
                + "{"
                + "\"kty\":\"OKP\","
                + "\"crv\":\"Ed25519\","
                + "\"kid\":\"yucp-root-2025\","
                + "\"x\":\"legacy-key\""
                + "}"
                + "]"
                + "}";

            var parseMethod = typeof(PackageSigningService).GetMethod(
                "ParseTrustedRootKeys",
                BindingFlags.Static | BindingFlags.NonPublic
            );

            Assert.That(parseMethod, Is.Not.Null, "Expected PackageSigningService.ParseTrustedRootKeys to exist.");

            var parsed = parseMethod.Invoke(null, new object[] { jwksJson }) as IEnumerable;
            Assert.That(parsed, Is.Not.Null);

            var entries = new List<object>();
            foreach (var entry in parsed)
            {
                entries.Add(entry);
            }

            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(GetStringMember(entries[0], "keyId"), Is.EqualTo("yucp-root"));
            Assert.That(GetStringMember(entries[0], "publicKeyBase64"), Is.EqualTo("server-key"));
            Assert.That(GetStringMember(entries[1], "keyId"), Is.EqualTo("yucp-root-2025"));
            Assert.That(GetStringMember(entries[1], "publicKeyBase64"), Is.EqualTo("legacy-key"));
        }

        [Test]
        public void TryGetTrustedRootPublicKey_UsesServerFetchedKeyIdInsteadOfBakedValue()
        {
            var settings = ScriptableObject.CreateInstance<SigningSettings>();
            try
            {
                var trustedKeyType = typeof(SigningSettings).Assembly.GetType(
                    "YUCP.DevTools.Editor.PackageSigning.Data.TrustedRootKey"
                );
                Assert.That(trustedKeyType, Is.Not.Null, "Expected TrustedRootKey to exist.");

                var trustedKeys = CreateTrustedKeyList(trustedKeyType,
                    ("yucp-root", "Ed25519", "server-key"),
                    ("yucp-root-2025", "Ed25519", "legacy-key")
                );

                var storeMethod = typeof(SigningSettings).GetMethod(
                    "SetTrustedRootKeysForServer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
                Assert.That(storeMethod, Is.Not.Null, "Expected SigningSettings.SetTrustedRootKeysForServer to exist.");
                storeMethod.Invoke(settings, new object[] { "https://api.creators.yucp.club", trustedKeys });

                var tryGetMethod = typeof(SigningSettings).GetMethod(
                    "TryGetTrustedRootPublicKey",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
                Assert.That(tryGetMethod, Is.Not.Null, "Expected SigningSettings.TryGetTrustedRootPublicKey to exist.");

                var args = new object[] { "yucp-root", "Ed25519", null };
                var found = (bool)tryGetMethod.Invoke(settings, args);

                Assert.That(found, Is.True);
                Assert.That(args[2], Is.EqualTo("server-key"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        private static IList CreateTrustedKeyList(Type trustedKeyType, params (string keyId, string algorithm, string publicKeyBase64)[] values)
        {
            var listType = typeof(List<>).MakeGenericType(trustedKeyType);
            var list = (IList)Activator.CreateInstance(listType);

            foreach (var value in values)
            {
                var key = Activator.CreateInstance(trustedKeyType);
                SetStringMember(key, "keyId", value.keyId);
                SetStringMember(key, "algorithm", value.algorithm);
                SetStringMember(key, "publicKeyBase64", value.publicKeyBase64);
                list.Add(key);
            }

            return list;
        }

        private static string GetStringMember(object target, string memberName)
        {
            var field = target.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                return field.GetValue(target) as string;
            }

            var property = target.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null)
            {
                return property.GetValue(target) as string;
            }

            Assert.Fail($"Expected member '{memberName}' on {target.GetType().FullName}.");
            return null;
        }

        private static void SetStringMember(object target, string memberName, string value)
        {
            var field = target.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            var property = target.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null)
            {
                property.SetValue(target, value);
                return;
            }

            Assert.Fail($"Expected writable member '{memberName}' on {target.GetType().FullName}.");
        }
    }
}
