using System;
using System.Collections.Generic;
using UnityEngine;

// IMPORTANT: This namespace marker (YUCP.CompanionTutorial.Generated.Source) is swapped to a
// per-export-unique namespace (YUCP.CompanionTutorial.Generated_<guid>) by PackageBuilder when the
// runtime is injected into an exported package. Do not rename it without updating the swap in
// PackageBuilder.TryInjectCompanionRuntime / TryInjectCompanionBootstrap.
namespace YUCP.CompanionTutorial.Generated.Source
{
    // Field-compatible mirror of YUCP.DevTools.Editor.PackageExporter.CompanionTutorialDefinition.
    // The two types never reference each other; they communicate purely through JsonUtility, which
    // binds by field name. Keep the field names/types in sync (guarded by CompanionTutorialSerializationTests).
    [Serializable]
    public class CompanionTutorialDefinition
    {
        public bool enabled = false;
        public string title = "Installation Tutorial";
        public List<CompanionTutorialStep> steps = new List<CompanionTutorialStep>();
    }

    [Serializable]
    public class CompanionTutorialStep
    {
        public string id = Guid.NewGuid().ToString("N");
        public string title = "Step";
        [TextArea(2, 5)]
        public string text = "";
        public string target = "center";
        public Vector4 targetRect = Vector4.zero;
        public string waitFor = "manual";
        public string mouseAction = "none";
        public string overlayMode = "intrusive";
        public Vector4 spotlightPadding = new Vector4(12, 12, 12, 12);
    }
}
