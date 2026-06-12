using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RuntimeDef = YUCP.CompanionTutorial.Generated.Source.CompanionTutorialDefinition;
using RuntimeStep = YUCP.CompanionTutorial.Generated.Source.CompanionTutorialStep;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    /// <summary>
    /// The authoring POCO (YUCP.DevTools.Editor.PackageExporter.CompanionTutorialDefinition) and the
    /// injected runtime POCO (YUCP.CompanionTutorial.Generated.Source.CompanionTutorialDefinition) are
    /// distinct types that communicate only through JsonUtility, which binds by field name. These tests
    /// fail if the two ever drift apart.
    /// </summary>
    public class CompanionTutorialSerializationTests
    {
        [Test]
        public void AuthoringDefinition_RoundTripsIntoRuntimeDefinition()
        {
            var authoring = new CompanionTutorialDefinition
            {
                enabled = true,
                title = "My Install Tutorial",
                steps = new List<CompanionTutorialStep>
                {
                    new CompanionTutorialStep
                    {
                        id = "abc123",
                        title = "Open the Inspector",
                        text = "Select the prefab to inspect it.",
                        target = "property:position",
                        targetRect = new Vector4(1, 2, 3, 4),
                        waitFor = "delay:2",
                        mouseAction = "click",
                        overlayMode = "unintrusive",
                        spotlightPadding = new Vector4(10, 8, 10, 8)
                    },
                    new CompanionTutorialStep
                    {
                        title = "Move it",
                        text = "Drag with the gizmo.",
                        target = "gizmo",
                        waitFor = "transformMoved:selected",
                        mouseAction = "drag"
                    }
                }
            };

            string json = JsonUtility.ToJson(authoring);
            RuntimeDef runtime = JsonUtility.FromJson<RuntimeDef>(json);

            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.enabled, Is.True);
            Assert.That(runtime.title, Is.EqualTo("My Install Tutorial"));
            Assert.That(runtime.steps, Is.Not.Null);
            Assert.That(runtime.steps.Count, Is.EqualTo(2));

            RuntimeStep s0 = runtime.steps[0];
            Assert.That(s0.id, Is.EqualTo("abc123"));
            Assert.That(s0.title, Is.EqualTo("Open the Inspector"));
            Assert.That(s0.text, Is.EqualTo("Select the prefab to inspect it."));
            Assert.That(s0.target, Is.EqualTo("property:position"));
            Assert.That(s0.targetRect, Is.EqualTo(new Vector4(1, 2, 3, 4)));
            Assert.That(s0.waitFor, Is.EqualTo("delay:2"));
            Assert.That(s0.mouseAction, Is.EqualTo("click"));
            Assert.That(s0.overlayMode, Is.EqualTo("unintrusive"));
            Assert.That(s0.spotlightPadding, Is.EqualTo(new Vector4(10, 8, 10, 8)));

            RuntimeStep s1 = runtime.steps[1];
            Assert.That(s1.target, Is.EqualTo("gizmo"));
            Assert.That(s1.waitFor, Is.EqualTo("transformMoved:selected"));
            Assert.That(s1.mouseAction, Is.EqualTo("drag"));
        }

        [Test]
        public void RuntimeDefinition_ParsesFromMetadataWrapper()
        {
            // Mirrors how the runner reads the tutorial out of YUCP_PackageInfo.json (wrapped under a
            // "companionTutorial" field).
            string metadataJson =
                "{\"companionTutorial\":{\"enabled\":true,\"title\":\"T\",\"steps\":[{\"title\":\"A\",\"target\":\"center\",\"waitFor\":\"manual\"}]}}";

            var wrapper = JsonUtility.FromJson<MetadataWrapper>(metadataJson);
            Assert.That(wrapper.companionTutorial, Is.Not.Null);
            Assert.That(wrapper.companionTutorial.enabled, Is.True);
            Assert.That(wrapper.companionTutorial.steps.Count, Is.EqualTo(1));
            Assert.That(wrapper.companionTutorial.steps[0].title, Is.EqualTo("A"));
        }

        [System.Serializable]
        private class MetadataWrapper
        {
            public RuntimeDef companionTutorial;
        }
    }
}
