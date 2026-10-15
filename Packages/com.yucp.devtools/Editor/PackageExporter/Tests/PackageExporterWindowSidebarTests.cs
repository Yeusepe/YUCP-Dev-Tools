using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class PackageExporterWindowSidebarTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void ProfileMatchesFilters_ExcludesNegatedTagsAndText()
        {
            var window = ScriptableObject.CreateInstance<YUCPPackageExporterWindow>();
            var beta = ScriptableObject.CreateInstance<ExportProfile>();
            var stable = ScriptableObject.CreateInstance<ExportProfile>();

            try
            {
                beta.packageName = "Avatar Beta";
                beta.customTags = new List<string> { "Beta" };
                stable.packageName = "Avatar Stable";
                stable.customTags = new List<string> { "Stable" };

                bool Matches(ExportProfile p) => (bool)typeof(YUCPPackageExporterWindow)
                    .GetMethod("ProfileMatchesFilters", Private)
                    .Invoke(window, new object[] { p });
                void Set(string field, object value) => typeof(YUCPPackageExporterWindow)
                    .GetField(field, Private).SetValue(window, value);

                Set("selectedFilterTags", new List<string> { "-Beta" });
                Assert.That(Matches(beta), Is.False);
                Assert.That(Matches(stable), Is.True);

                Set("selectedFilterTags", new List<string>());
                Set("_currentSearchText", "-stable");
                Assert.That(Matches(beta), Is.True);
                Assert.That(Matches(stable), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(beta);
                Object.DestroyImmediate(stable);
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SelectDisplayedRange_FollowsOnScreenOrderNotListOrder()
        {
            var window = ScriptableObject.CreateInstance<YUCPPackageExporterWindow>();

            try
            {
                // allProfiles indices as they appear on screen
                typeof(YUCPPackageExporterWindow).GetField("_displayedProfileIndices", Private)
                    .SetValue(window, new List<int> { 3, 0, 2, 1 });

                typeof(YUCPPackageExporterWindow).GetMethod("SelectDisplayedRange", Private)
                    .Invoke(window, new object[] { 0, 1 });

                var selected = (HashSet<int>)typeof(YUCPPackageExporterWindow)
                    .GetField("selectedProfileIndices", Private).GetValue(window);
                Assert.That(selected, Is.EquivalentTo(new[] { 0, 2, 1 }));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
    }
}
