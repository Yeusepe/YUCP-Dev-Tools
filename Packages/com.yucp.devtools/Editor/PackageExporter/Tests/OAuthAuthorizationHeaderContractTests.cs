using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    /// <summary>
    /// The exporter's OAuth client registers with DPoP-bound access tokens, and
    /// the server rejects such a token presented with the Bearer scheme (RFC 9449
    /// §7.1). Every API request must therefore go through
    /// YucpOAuthService.ApplyAuthHeaders, which picks the correct scheme and
    /// attaches the proof. A hand-rolled Authorization header compiles, signs in,
    /// and then fails every API call with a 401 at runtime — this test makes that
    /// mistake fail at test time instead.
    /// </summary>
    public class OAuthAuthorizationHeaderContractTests
    {
        /// <summary>Files allowed to set an Authorization header themselves.</summary>
        private static readonly string[] AllowedSourceFiles =
        {
            // Owns ApplyAuthHeaders and the deliberate Bearer fallbacks for
            // sessions that were never DPoP-bound.
            "YucpOAuthService.cs",
            // Sends the certificate envelope to /v1/signatures; that credential
            // is not an OAuth access token and has no DPoP binding.
            "PackageBuilder.cs",
        };

        [Test]
        public void EveryOAuthRequestUsesApplyAuthHeaders()
        {
            string packageRoot = PackageInfo.FindForAssembly(
                typeof(OAuthAuthorizationHeaderContractTests).Assembly).resolvedPath;
            string editorRoot = Path.Combine(packageRoot, "Editor");

            var offenders = new List<string>();
            foreach (string sourcePath in Directory.GetFiles(editorRoot, "*.cs", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(sourcePath);
                if (AllowedSourceFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(sourcePath);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("SetRequestHeader(\"Authorization\""))
                    {
                        offenders.Add($"{fileName}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }

            Assert.That(
                offenders,
                Is.Empty,
                "Set OAuth headers via YucpOAuthService.ApplyAuthHeaders instead of " +
                "building an Authorization header by hand; a DPoP-bound token sent " +
                "as Bearer is rejected by the server:\n" + string.Join("\n", offenders));
        }
    }
}
