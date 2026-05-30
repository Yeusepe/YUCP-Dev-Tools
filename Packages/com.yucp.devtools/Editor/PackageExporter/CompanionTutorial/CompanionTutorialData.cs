using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
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

        // Examples: toolbar, toolbar:play, menu:yucp, material:shader, inspector, project,
        // hierarchy:Main Camera, scene:Main Camera, center, ui:element-name
        public string target = "center";

        // Optional screen-local rect override: x, y, width, height relative to the Unity main window.
        public Vector4 targetRect = Vector4.zero;

        // Optional wait condition. Examples: manual, delay:2, selection, assetExists:Assets/Foo.asset,
        // packageInstalled:com.example.package, componentExists:Namespace.Type, Assembly
        public string waitFor = "manual";

        // Optional visual-only pointer action. Examples: none, click, doubleClick, rightClick, drag
        public string mouseAction = "none";

        // Overlay style. intrusive darkens/highlights Unity; unintrusive shows only cursor and popup.
        public string overlayMode = "intrusive";

        public Vector4 spotlightPadding = new Vector4(12, 12, 12, 12);
    }
}
