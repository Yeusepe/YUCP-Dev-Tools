using System;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Central definitions of the companion-tutorial "magic string" vocabulary (target / waitFor /
    /// mouseAction / overlayMode). Shared by the step drawer, the validator, and the docs so the three
    /// can never drift. Mirrors the tokens understood by CompanionTutorialRunner.
    /// </summary>
    internal static class CompanionTutorialTokens
    {
        // ── Target categories (authoring dropdown) ───────────────────────────────────────────────
        public enum TargetCategory
        {
            Center,
            EditorWindow,   // bare token, e.g. "inspector", "hierarchy", "console"
            Toolbar,        // toolbar:<selector>
            MenuBar,        // menu:<selector>
            HierarchyItem,  // hierarchy:<selector>
            ProjectItem,    // project:<selector>
            SceneObject,    // scene:<selector>
            InspectorProperty, // property:<selector>
            MaterialProperty,  // material:<selector>
            Gizmo,          // gizmo
            UiElement,      // ui:<selector>
            Custom          // raw passthrough
        }

        public static readonly string[] TargetCategoryLabels =
        {
            "Centered card (no target)",
            "Editor window",
            "Toolbar control",
            "Menu bar item",
            "Hierarchy object",
            "Project asset",
            "Scene object",
            "Inspector property",
            "Material property",
            "Transform gizmo",
            "UI Toolkit element",
            "Custom (raw string)"
        };

        // Bare targets that resolve to a whole editor window / region (no prefix).
        public static readonly string[] EditorWindowTargets =
        {
            "inspector", "hierarchy", "project", "scene", "game", "console", "animation", "animator"
        };

        // Recognized "prefix:" forms.
        public static readonly string[] KnownTargetPrefixes =
        {
            "toolbar", "topbar", "menu", "menubar", "material", "shader",
            "property", "inspector", "hierarchy", "project", "scene", "object", "gameobject", "ui"
        };

        // Recognized bare (prefix-less) targets.
        public static readonly string[] KnownBareTargets =
        {
            "center", "gizmo", "transform-gizmo",
            "menu", "menu-bar", "toolbar", "main-toolbar",
            "inspector", "inspector-window", "hierarchy", "scene-hierarchy",
            "project", "project-browser", "scene", "scene-view", "game", "game-view",
            "console", "animation", "animator"
        };

        // Common selector suggestions per category (advisory only; any string is allowed).
        public static readonly string[] ToolbarSelectors = { "play", "pause", "step", "layers", "layout" };
        public static readonly string[] InspectorPropertySelectors =
        {
            "position", "rotation", "scale", "shader", "color", "mainTex", "metallic", "smoothness"
        };

        // ── waitFor categories ───────────────────────────────────────────────────────────────────
        public enum WaitCategory
        {
            Manual,             // manual
            Delay,              // delay:<seconds>
            Selection,          // selection
            AssetExists,        // assetExists:<path>
            PackageInstalled,   // packageInstalled:<name>
            ComponentExists,    // componentExists:<type>
            TransformMoved,     // transformMoved:<selector>
            Custom
        }

        public static readonly string[] WaitCategoryLabels =
        {
            "Manual (Next button)",
            "Delay (seconds)",
            "Any selection change",
            "Asset exists at path",
            "Package installed",
            "Component exists in scene",
            "Selected transform moves",
            "Custom (raw string)"
        };

        public static readonly Dictionary<WaitCategory, string> WaitPrefixes = new Dictionary<WaitCategory, string>
        {
            { WaitCategory.Manual, "manual" },
            { WaitCategory.Delay, "delay:" },
            { WaitCategory.Selection, "selection" },
            { WaitCategory.AssetExists, "assetExists:" },
            { WaitCategory.PackageInstalled, "packageInstalled:" },
            { WaitCategory.ComponentExists, "componentExists:" },
            { WaitCategory.TransformMoved, "transformMoved:" }
        };

        public static readonly string[] KnownWaitVerbs =
        {
            "manual", "delay", "selection", "assetExists", "packageInstalled", "componentExists", "transformMoved"
        };

        // ── mouse action / overlay mode ──────────────────────────────────────────────────────────
        public static readonly string[] MouseActions = { "none", "click", "doubleClick", "rightClick", "drag" };
        public static readonly string[] OverlayModes = { "intrusive", "unintrusive" };

        // ── Helpers ──────────────────────────────────────────────────────────────────────────────

        public static void SplitPrefix(string target, out string prefix, out string selector)
        {
            target = (target ?? string.Empty).Trim();
            int colon = target.IndexOf(':');
            if (colon >= 0)
            {
                prefix = target.Substring(0, colon).Trim();
                selector = target.Substring(colon + 1).Trim();
            }
            else
            {
                prefix = target;
                selector = string.Empty;
            }
        }

        public static bool HasKnownPrefix(string prefix)
        {
            return !string.IsNullOrEmpty(prefix) &&
                   KnownTargetPrefixes.Any(p => string.Equals(p, prefix, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsKnownBareTarget(string target)
        {
            return !string.IsNullOrEmpty(target) &&
                   KnownBareTargets.Any(t => string.Equals(t, target, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetWaitVerb(string waitFor)
        {
            SplitPrefix(waitFor, out string prefix, out _);
            return prefix;
        }

        public static bool IsKnownWaitVerb(string verb)
        {
            return !string.IsNullOrEmpty(verb) &&
                   KnownWaitVerbs.Any(v => string.Equals(v, verb, StringComparison.OrdinalIgnoreCase));
        }
    }
}
