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

        // ── Target parse / compose ───────────────────────────────────────────────────────────────
        // Authoring helpers shared by every editor surface (IMGUI step drawer + UI Toolkit step card)
        // so the dropdown ↔ raw-string mapping can never drift between them.

        public static TargetCategory ParseTargetCategory(string target, out string selector)
        {
            SplitPrefix(target, out string prefix, out selector);
            bool hasColon = (target ?? string.Empty).IndexOf(':') >= 0;

            if (!hasColon)
            {
                string t = (target ?? string.Empty).Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(t) || t == "center") { selector = string.Empty; return TargetCategory.Center; }
                if (t == "gizmo" || t == "transform-gizmo") { selector = string.Empty; return TargetCategory.Gizmo; }
                if (Array.IndexOf(EditorWindowTargets, t) >= 0) { selector = t; return TargetCategory.EditorWindow; }
                selector = (target ?? string.Empty).Trim();
                return TargetCategory.Custom;
            }

            switch (prefix.ToLowerInvariant())
            {
                case "toolbar":
                case "topbar": return TargetCategory.Toolbar;
                case "menu":
                case "menubar": return TargetCategory.MenuBar;
                case "hierarchy": return TargetCategory.HierarchyItem;
                case "project": return TargetCategory.ProjectItem;
                case "scene":
                case "object":
                case "gameobject": return TargetCategory.SceneObject;
                case "property":
                case "inspector": return TargetCategory.InspectorProperty;
                case "material":
                case "shader": return TargetCategory.MaterialProperty;
                case "ui": return TargetCategory.UiElement;
                default:
                    selector = (target ?? string.Empty).Trim();
                    return TargetCategory.Custom;
            }
        }

        public static string ComposeTarget(TargetCategory category, string selector)
        {
            selector = (selector ?? string.Empty).Trim();
            switch (category)
            {
                case TargetCategory.Center: return "center";
                case TargetCategory.Gizmo: return "gizmo";
                case TargetCategory.EditorWindow: return string.IsNullOrEmpty(selector) ? "inspector" : selector;
                case TargetCategory.Toolbar: return "toolbar:" + selector;
                case TargetCategory.MenuBar: return "menu:" + selector;
                case TargetCategory.HierarchyItem: return "hierarchy:" + selector;
                case TargetCategory.ProjectItem: return "project:" + selector;
                case TargetCategory.SceneObject: return "scene:" + selector;
                case TargetCategory.InspectorProperty: return "property:" + selector;
                case TargetCategory.MaterialProperty: return "material:" + selector;
                case TargetCategory.UiElement: return "ui:" + selector;
                default: return selector;
            }
        }

        public static string DefaultSelectorFor(TargetCategory category)
        {
            switch (category)
            {
                case TargetCategory.EditorWindow: return "inspector";
                case TargetCategory.Toolbar: return "play";
                case TargetCategory.InspectorProperty: return "position";
                case TargetCategory.HierarchyItem:
                case TargetCategory.SceneObject: return "Main Camera";
                default: return string.Empty;
            }
        }

        public static string SelectorLabelFor(TargetCategory category)
        {
            switch (category)
            {
                case TargetCategory.Toolbar: return "Control (play/layers/…)";
                case TargetCategory.MenuBar: return "Menu name";
                case TargetCategory.HierarchyItem: return "Object name/path";
                case TargetCategory.ProjectItem: return "Asset path/name/guid";
                case TargetCategory.SceneObject: return "Object name";
                case TargetCategory.InspectorProperty: return "Property (position/…)";
                case TargetCategory.MaterialProperty: return "Property (shader/color/…)";
                case TargetCategory.UiElement: return "Element name";
                default: return "Selector";
            }
        }

        public static bool TargetNeedsSelector(TargetCategory category)
        {
            return category != TargetCategory.Center && category != TargetCategory.Gizmo;
        }

        // ── waitFor parse / compose ──────────────────────────────────────────────────────────────

        public static WaitCategory ParseWaitCategory(string waitFor, out string arg)
        {
            SplitPrefix(waitFor, out string verb, out arg);
            switch ((verb ?? string.Empty).ToLowerInvariant())
            {
                case "":
                case "manual": return WaitCategory.Manual;
                case "delay": return WaitCategory.Delay;
                case "selection": return WaitCategory.Selection;
                case "assetexists": return WaitCategory.AssetExists;
                case "packageinstalled": return WaitCategory.PackageInstalled;
                case "componentexists": return WaitCategory.ComponentExists;
                case "transformmoved": return WaitCategory.TransformMoved;
                default:
                    arg = (waitFor ?? string.Empty).Trim();
                    return WaitCategory.Custom;
            }
        }

        public static bool WaitNeedsParam(WaitCategory category)
        {
            switch (category)
            {
                case WaitCategory.Delay:
                case WaitCategory.AssetExists:
                case WaitCategory.PackageInstalled:
                case WaitCategory.ComponentExists:
                case WaitCategory.TransformMoved:
                case WaitCategory.Custom:
                    return true;
                default:
                    return false;
            }
        }

        public static string WaitParamLabel(WaitCategory category)
        {
            switch (category)
            {
                case WaitCategory.Delay: return "Seconds";
                case WaitCategory.AssetExists: return "Asset path";
                case WaitCategory.PackageInstalled: return "Package name";
                case WaitCategory.ComponentExists: return "Type name";
                case WaitCategory.TransformMoved: return "Object (blank = selection)";
                default: return "Value";
            }
        }

        public static string ComposeWait(WaitCategory category, string arg)
        {
            arg = (arg ?? string.Empty).Trim();
            switch (category)
            {
                case WaitCategory.Manual: return "manual";
                case WaitCategory.Selection: return "selection";
                case WaitCategory.Delay: return "delay:" + (string.IsNullOrEmpty(arg) ? "2" : arg);
                case WaitCategory.AssetExists: return "assetExists:" + arg;
                case WaitCategory.PackageInstalled: return "packageInstalled:" + arg;
                case WaitCategory.ComponentExists: return "componentExists:" + arg;
                case WaitCategory.TransformMoved: return "transformMoved:" + arg;
                default: return arg;
            }
        }

        public static string DefaultWaitArg(WaitCategory category)
        {
            switch (category)
            {
                case WaitCategory.Delay: return "2";
                case WaitCategory.TransformMoved: return "selected";
                default: return string.Empty;
            }
        }
    }
}
