using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    /// <summary>
    /// Exercises PackageBuilder.TryInjectCompanionTutorial directly (it stages asset/pathname/meta
    /// triples into a temp dir, exactly as it does inside a real export). Verifies the source-injection
    /// contract: no raw .exe ships, all tokens are substituted, and each export gets a unique namespace.
    /// </summary>
    public class CompanionTutorialInjectionTests
    {
        private readonly List<string> _tempDirs = new List<string>();

        private const string CompanionDir = "Assets/YUCP_Companion/com.example.demo";
        private const string EditorDir = CompanionDir + "/Editor";
        private const string RunOnceKey = "YUCP.CompanionTutorial.Ran.com.example.demo.1.0.0";
        private const string OverlayBytesPath = EditorDir + "/CompanionOverlay/YUCPCompanionOverlay.bytes";
        private const string NamespaceMarker = "YUCP.CompanionTutorial.Generated.Source";

        // Base64 of {"enabled":true,"title":"T","steps":[]}
        private static readonly string TutorialBase64 = System.Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("{\"enabled\":true,\"title\":\"T\",\"steps\":[]}"));

        [TearDown]
        public void Cleanup()
        {
            foreach (var dir in _tempDirs)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch { /* best effort */ }
            }
            _tempDirs.Clear();
        }

        private Dictionary<string, string> RunInjection(out bool result)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "YUCP_CompanionInjectTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            _tempDirs.Add(tempDir);

            MethodInfo method = typeof(PackageBuilder).GetMethod(
                "TryInjectCompanionTutorial",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Expected PackageBuilder.TryInjectCompanionTutorial to exist.");

            result = (bool)method.Invoke(null, new object[] { tempDir, CompanionDir, TutorialBase64, RunOnceKey });

            // Map destination pathname -> staging folder.
            var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in Directory.GetDirectories(tempDir))
            {
                string pathnameFile = Path.Combine(folder, "pathname");
                string assetFile = Path.Combine(folder, "asset");
                if (File.Exists(pathnameFile) && File.Exists(assetFile))
                    entries[File.ReadAllText(pathnameFile).Trim()] = folder;
            }
            return entries;
        }

        [Test]
        public void Injection_ShipsBytesPayload_NeverExe()
        {
            var entries = RunInjection(out bool result);
            Assert.That(result, Is.True, "Injection should succeed when runtime source + overlay binary are present.");

            Assert.That(entries.Keys.Any(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)), Is.False,
                "No raw .exe may ship inside the package.");
            Assert.That(entries.ContainsKey(OverlayBytesPath), Is.True,
                "Overlay helper must ship as a .bytes TextAsset at the companion path.");
        }

        [Test]
        public void Injection_BootstrapHasNoLeftoverTokensAndMarker()
        {
            var entries = RunInjection(out _);

            string bootstrapKey = entries.Keys.FirstOrDefault(
                p => p.Contains("YUCP_CompanionBootstrap_") && p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
            Assert.That(bootstrapKey, Is.Not.Null, "A bootstrap .cs must be injected.");

            string bootstrap = File.ReadAllText(Path.Combine(entries[bootstrapKey], "asset"));
            Assert.That(bootstrap.Contains("__YUCP_"), Is.False, "All __YUCP_ tokens must be substituted.");
            Assert.That(bootstrap.Contains(NamespaceMarker), Is.False, "Namespace marker must be swapped out.");
            Assert.That(bootstrap.Contains(TutorialBase64), Is.True, "Bootstrap should carry the tutorial JSON inline (Base64).");
            Assert.That(bootstrap.Contains(OverlayBytesPath), Is.True, "Bootstrap should embed the overlay .bytes path.");
            Assert.That(bootstrap.Contains(RunOnceKey), Is.True, "Bootstrap should embed the run-once key.");
        }

        [Test]
        public void Injection_RuntimeSourceUsesUniqueNamespace()
        {
            var entries = RunInjection(out _);

            string runnerKey = entries.Keys.FirstOrDefault(
                p => p.Contains("YUCP_CompanionRuntime_Runner") && p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
            Assert.That(runnerKey, Is.Not.Null, "The runtime runner source must be injected.");

            string runner = File.ReadAllText(Path.Combine(entries[runnerKey], "asset"));
            Assert.That(runner.Contains("namespace " + NamespaceMarker), Is.False,
                "Injected runtime must not keep the shared marker namespace.");
            Assert.That(Regex.IsMatch(runner, @"namespace YUCP\.CompanionTutorial\.Generated_[0-9a-fA-F]{32}"), Is.True,
                "Injected runtime must use a per-export-unique namespace.");
        }

        [Test]
        public void Injection_TwoExportsGetDifferentNamespaces()
        {
            string ns1 = ExtractNamespace(RunInjection(out _));
            string ns2 = ExtractNamespace(RunInjection(out _));

            Assert.That(ns1, Is.Not.Null.And.Not.Empty);
            Assert.That(ns2, Is.Not.Null.And.Not.Empty);
            Assert.That(ns1, Is.Not.EqualTo(ns2), "Each export must get a distinct namespace so two packages never collide.");
        }

        private static string ExtractNamespace(Dictionary<string, string> entries)
        {
            string runnerKey = entries.Keys.FirstOrDefault(
                p => p.Contains("YUCP_CompanionRuntime_Runner") && p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
            if (runnerKey == null)
                return null;
            string runner = File.ReadAllText(Path.Combine(entries[runnerKey], "asset"));
            Match m = Regex.Match(runner, @"YUCP\.CompanionTutorial\.Generated_[0-9a-fA-F]{32}");
            return m.Success ? m.Value : null;
        }

        [Test]
        public void OverlayBinary_ExistsForBytesInjection()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string exe = Path.Combine(projectRoot,
                "Packages", "com.yucp.devtools", "Editor", "PackageExporter",
                "Binaries", "CompanionOverlay", "YUCPCompanionOverlay.exe");
            Assert.That(File.Exists(exe), Is.True, "The overlay binary must exist so it can be shipped as a .bytes payload.");
        }
    }
}
