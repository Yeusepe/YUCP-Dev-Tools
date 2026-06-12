using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Shared IMGUI layout for a single <see cref="CompanionTutorialStep"/>: dropdowns for target,
    /// waitFor, mouse action and overlay mode (with a "Custom (raw)" escape hatch), an advanced
    /// foldout for the spotlight/rect overrides, and inline validation. Used by both the property
    /// drawer (ExportProfile inspector) and the reorderable list in the exporter window.
    /// </summary>
    internal static class CompanionTutorialStepGUI
    {
        private static readonly HashSet<string> s_expandedAdvanced = new HashSet<string>();

        private static float Line => EditorGUIUtility.singleLineHeight;
        private const float Pad = 2f;
        private const float TextAreaLines = 3f;
        private const float FindingHeight = 34f;

        public static float Height(SerializedProperty step)
        {
            if (step == null)
                return Line;

            float h = 0f;
            h += Row();                       // title
            h += Line * TextAreaLines + Pad;  // text
            h += Row();                       // target category
            if (TargetCategoryOf(step) != CompanionTutorialTokens.TargetCategory.Center &&
                TargetCategoryOf(step) != CompanionTutorialTokens.TargetCategory.Gizmo)
                h += Row();                   // target selector / raw
            h += Row();                       // wait category
            if (WaitNeedsParam(WaitCategoryOf(step)))
                h += Row();                   // wait param
            h += Row();                       // mouse action
            h += Row();                       // overlay mode

            h += FindingHeightFor(step);

            h += Row();                       // advanced foldout
            if (s_expandedAdvanced.Contains(step.propertyPath))
            {
                h += Row(); // spotlightPadding (wideMode = single line)
                h += Row(); // targetRect
            }
            return h;
        }

        private static float Row() => Line + Pad;

        public static void Draw(Rect rect, SerializedProperty step, string headerLabel)
        {
            if (step == null)
                return;

            var pTitle = step.FindPropertyRelative("title");
            var pText = step.FindPropertyRelative("text");
            var pTarget = step.FindPropertyRelative("target");
            var pWait = step.FindPropertyRelative("waitFor");
            var pMouse = step.FindPropertyRelative("mouseAction");
            var pOverlay = step.FindPropertyRelative("overlayMode");
            var pPadding = step.FindPropertyRelative("spotlightPadding");
            var pRect = step.FindPropertyRelative("targetRect");

            float y = rect.y;
            Rect Next(float h)
            {
                var r = new Rect(rect.x, y, rect.width, h);
                y += h + Pad;
                return r;
            }

            // Title
            EditorGUI.PropertyField(Next(Line), pTitle, new GUIContent(headerLabel ?? "Title"));

            // Body text
            var textRect = Next(Line * TextAreaLines);
            EditorGUI.LabelField(new Rect(textRect.x, textRect.y, 60, Line), "Text");
            var textArea = new Rect(textRect.x + 64, textRect.y, textRect.width - 64, textRect.height);
            pText.stringValue = EditorGUI.TextArea(textArea, pText.stringValue);

            // ── Target ──────────────────────────────────────────────────────────────────────
            DrawTarget(Next, pTarget);

            // ── WaitFor ─────────────────────────────────────────────────────────────────────
            DrawWait(Next, pWait);

            // Mouse action
            DrawStringPopup(Next(Line), "Mouse Action", pMouse, CompanionTutorialTokens.MouseActions, "none");

            // Overlay mode
            DrawStringPopup(Next(Line), "Overlay Mode", pOverlay, CompanionTutorialTokens.OverlayModes, "intrusive");

            // Inline validation
            DrawFindings(Next, step);

            // Advanced
            bool expanded = s_expandedAdvanced.Contains(step.propertyPath);
            bool newExpanded = EditorGUI.Foldout(Next(Line), expanded, "Advanced (spotlight / manual rect)", true);
            if (newExpanded != expanded)
            {
                if (newExpanded) s_expandedAdvanced.Add(step.propertyPath);
                else s_expandedAdvanced.Remove(step.propertyPath);
            }
            if (newExpanded)
            {
                bool prevWide = EditorGUIUtility.wideMode;
                EditorGUIUtility.wideMode = true;
                EditorGUI.PropertyField(Next(Line), pPadding, new GUIContent("Spotlight Padding (L,T,R,B)"));
                EditorGUI.PropertyField(Next(Line), pRect, new GUIContent("Manual Rect (x,y,w,h)"));
                EditorGUIUtility.wideMode = prevWide;
            }
        }

        private static void DrawTarget(System.Func<float, Rect> next, SerializedProperty pTarget)
        {
            var category = ParseTargetCategory(pTarget.stringValue, out string selector);

            int catIndex = (int)category;
            int newCatIndex = EditorGUI.Popup(next(Line), "Target", catIndex, CompanionTutorialTokens.TargetCategoryLabels);
            if (newCatIndex != catIndex)
            {
                category = (CompanionTutorialTokens.TargetCategory)newCatIndex;
                pTarget.stringValue = ComposeTarget(category, DefaultSelectorFor(category));
                selector = DefaultSelectorFor(category);
            }

            switch (category)
            {
                case CompanionTutorialTokens.TargetCategory.Center:
                case CompanionTutorialTokens.TargetCategory.Gizmo:
                    break; // no selector
                case CompanionTutorialTokens.TargetCategory.Custom:
                    EditorGUI.PropertyField(next(Line), pTarget, new GUIContent("Raw target"));
                    break;
                case CompanionTutorialTokens.TargetCategory.EditorWindow:
                {
                    int sel = Mathf.Max(0, System.Array.IndexOf(CompanionTutorialTokens.EditorWindowTargets, selector));
                    int newSel = EditorGUI.Popup(next(Line), "Window", sel, CompanionTutorialTokens.EditorWindowTargets);
                    pTarget.stringValue = CompanionTutorialTokens.EditorWindowTargets[Mathf.Clamp(newSel, 0, CompanionTutorialTokens.EditorWindowTargets.Length - 1)];
                    break;
                }
                default:
                {
                    string newSelector = EditorGUI.TextField(next(Line), SelectorLabelFor(category), selector);
                    if (newSelector != selector)
                        pTarget.stringValue = ComposeTarget(category, newSelector);
                    break;
                }
            }
        }

        private static void DrawWait(System.Func<float, Rect> next, SerializedProperty pWait)
        {
            var category = ParseWaitCategory(pWait.stringValue, out string arg);
            int catIndex = (int)category;
            int newCatIndex = EditorGUI.Popup(next(Line), "Advance When", catIndex, CompanionTutorialTokens.WaitCategoryLabels);
            if (newCatIndex != catIndex)
            {
                category = (CompanionTutorialTokens.WaitCategory)newCatIndex;
                pWait.stringValue = ComposeWait(category, DefaultWaitArg(category));
                arg = DefaultWaitArg(category);
            }

            if (category == CompanionTutorialTokens.WaitCategory.Custom)
            {
                EditorGUI.PropertyField(next(Line), pWait, new GUIContent("Raw waitFor"));
            }
            else if (WaitNeedsParam(category))
            {
                string label = WaitParamLabel(category);
                string newArg = EditorGUI.TextField(next(Line), label, arg);
                if (newArg != arg)
                    pWait.stringValue = ComposeWait(category, newArg);
            }
        }

        private static void DrawStringPopup(Rect rect, string label, SerializedProperty prop, string[] options, string fallback)
        {
            string current = string.IsNullOrEmpty(prop.stringValue) ? fallback : prop.stringValue;
            int index = Mathf.Max(0, System.Array.IndexOf(options, current));
            int newIndex = EditorGUI.Popup(rect, label, index, options);
            prop.stringValue = options[Mathf.Clamp(newIndex, 0, options.Length - 1)];
        }

        private static float FindingHeightFor(SerializedProperty step)
        {
            return FindingsForStep(step).Count * FindingHeight;
        }

        private static void DrawFindings(System.Func<float, Rect> next, SerializedProperty step)
        {
            foreach (var f in FindingsForStep(step))
            {
                EditorGUI.HelpBox(next(FindingHeight - Pad), f.Message, ToMessageType(f.Severity));
            }
        }

        private static List<CompanionTutorialValidator.Finding> FindingsForStep(SerializedProperty step)
        {
            // Validate just this step by wrapping it in a one-step definition snapshot.
            var snapshot = SnapshotStep(step);
            var def = new CompanionTutorialDefinition { enabled = true, steps = new List<CompanionTutorialStep> { snapshot } };
            return CompanionTutorialValidator.Validate(def);
        }

        private static CompanionTutorialStep SnapshotStep(SerializedProperty step)
        {
            return new CompanionTutorialStep
            {
                title = step.FindPropertyRelative("title").stringValue,
                text = step.FindPropertyRelative("text").stringValue,
                target = step.FindPropertyRelative("target").stringValue,
                waitFor = step.FindPropertyRelative("waitFor").stringValue,
                mouseAction = step.FindPropertyRelative("mouseAction").stringValue,
                overlayMode = step.FindPropertyRelative("overlayMode").stringValue,
                spotlightPadding = step.FindPropertyRelative("spotlightPadding").vector4Value,
                targetRect = step.FindPropertyRelative("targetRect").vector4Value
            };
        }

        private static MessageType ToMessageType(CompanionTutorialValidator.Severity s)
        {
            switch (s)
            {
                case CompanionTutorialValidator.Severity.Error: return MessageType.Error;
                case CompanionTutorialValidator.Severity.Warning: return MessageType.Warning;
                default: return MessageType.Info;
            }
        }

        // ── Target parse / compose ───────────────────────────────────────────────────────────────

        private static CompanionTutorialTokens.TargetCategory ParseTargetCategory(string target, out string selector)
        {
            CompanionTutorialTokens.SplitPrefix(target, out string prefix, out selector);
            bool hasColon = (target ?? string.Empty).IndexOf(':') >= 0;

            if (!hasColon)
            {
                string t = (target ?? string.Empty).Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(t) || t == "center") { selector = string.Empty; return CompanionTutorialTokens.TargetCategory.Center; }
                if (t == "gizmo" || t == "transform-gizmo") { selector = string.Empty; return CompanionTutorialTokens.TargetCategory.Gizmo; }
                if (System.Array.IndexOf(CompanionTutorialTokens.EditorWindowTargets, t) >= 0) { selector = t; return CompanionTutorialTokens.TargetCategory.EditorWindow; }
                return CompanionTutorialTokens.TargetCategory.Custom;
            }

            switch (prefix.ToLowerInvariant())
            {
                case "toolbar":
                case "topbar": return CompanionTutorialTokens.TargetCategory.Toolbar;
                case "menu":
                case "menubar": return CompanionTutorialTokens.TargetCategory.MenuBar;
                case "hierarchy": return CompanionTutorialTokens.TargetCategory.HierarchyItem;
                case "project": return CompanionTutorialTokens.TargetCategory.ProjectItem;
                case "scene":
                case "object":
                case "gameobject": return CompanionTutorialTokens.TargetCategory.SceneObject;
                case "property":
                case "inspector": return CompanionTutorialTokens.TargetCategory.InspectorProperty;
                case "material":
                case "shader": return CompanionTutorialTokens.TargetCategory.MaterialProperty;
                case "ui": return CompanionTutorialTokens.TargetCategory.UiElement;
                default: return CompanionTutorialTokens.TargetCategory.Custom;
            }
        }

        private static string ComposeTarget(CompanionTutorialTokens.TargetCategory category, string selector)
        {
            selector = (selector ?? string.Empty).Trim();
            switch (category)
            {
                case CompanionTutorialTokens.TargetCategory.Center: return "center";
                case CompanionTutorialTokens.TargetCategory.Gizmo: return "gizmo";
                case CompanionTutorialTokens.TargetCategory.EditorWindow: return string.IsNullOrEmpty(selector) ? "inspector" : selector;
                case CompanionTutorialTokens.TargetCategory.Toolbar: return "toolbar:" + selector;
                case CompanionTutorialTokens.TargetCategory.MenuBar: return "menu:" + selector;
                case CompanionTutorialTokens.TargetCategory.HierarchyItem: return "hierarchy:" + selector;
                case CompanionTutorialTokens.TargetCategory.ProjectItem: return "project:" + selector;
                case CompanionTutorialTokens.TargetCategory.SceneObject: return "scene:" + selector;
                case CompanionTutorialTokens.TargetCategory.InspectorProperty: return "property:" + selector;
                case CompanionTutorialTokens.TargetCategory.MaterialProperty: return "material:" + selector;
                case CompanionTutorialTokens.TargetCategory.UiElement: return "ui:" + selector;
                default: return selector;
            }
        }

        private static string DefaultSelectorFor(CompanionTutorialTokens.TargetCategory category)
        {
            switch (category)
            {
                case CompanionTutorialTokens.TargetCategory.EditorWindow: return "inspector";
                case CompanionTutorialTokens.TargetCategory.Toolbar: return "play";
                case CompanionTutorialTokens.TargetCategory.InspectorProperty: return "position";
                case CompanionTutorialTokens.TargetCategory.HierarchyItem:
                case CompanionTutorialTokens.TargetCategory.SceneObject: return "Main Camera";
                default: return string.Empty;
            }
        }

        private static string SelectorLabelFor(CompanionTutorialTokens.TargetCategory category)
        {
            switch (category)
            {
                case CompanionTutorialTokens.TargetCategory.Toolbar: return "Control (play/layers/…)";
                case CompanionTutorialTokens.TargetCategory.MenuBar: return "Menu name";
                case CompanionTutorialTokens.TargetCategory.HierarchyItem: return "Object name/path";
                case CompanionTutorialTokens.TargetCategory.ProjectItem: return "Asset path/name/guid";
                case CompanionTutorialTokens.TargetCategory.SceneObject: return "Object name";
                case CompanionTutorialTokens.TargetCategory.InspectorProperty: return "Property (position/…)";
                case CompanionTutorialTokens.TargetCategory.MaterialProperty: return "Property (shader/color/…)";
                case CompanionTutorialTokens.TargetCategory.UiElement: return "Element name";
                default: return "Selector";
            }
        }

        // ── WaitFor parse / compose ──────────────────────────────────────────────────────────────

        private static CompanionTutorialTokens.WaitCategory ParseWaitCategory(string waitFor, out string arg)
        {
            CompanionTutorialTokens.SplitPrefix(waitFor, out string verb, out arg);
            switch ((verb ?? string.Empty).ToLowerInvariant())
            {
                case "":
                case "manual": return CompanionTutorialTokens.WaitCategory.Manual;
                case "delay": return CompanionTutorialTokens.WaitCategory.Delay;
                case "selection": return CompanionTutorialTokens.WaitCategory.Selection;
                case "assetexists": return CompanionTutorialTokens.WaitCategory.AssetExists;
                case "packageinstalled": return CompanionTutorialTokens.WaitCategory.PackageInstalled;
                case "componentexists": return CompanionTutorialTokens.WaitCategory.ComponentExists;
                case "transformmoved": return CompanionTutorialTokens.WaitCategory.TransformMoved;
                default: return CompanionTutorialTokens.WaitCategory.Custom;
            }
        }

        private static bool WaitNeedsParam(CompanionTutorialTokens.WaitCategory category)
        {
            switch (category)
            {
                case CompanionTutorialTokens.WaitCategory.Delay:
                case CompanionTutorialTokens.WaitCategory.AssetExists:
                case CompanionTutorialTokens.WaitCategory.PackageInstalled:
                case CompanionTutorialTokens.WaitCategory.ComponentExists:
                case CompanionTutorialTokens.WaitCategory.TransformMoved:
                case CompanionTutorialTokens.WaitCategory.Custom:
                    return true;
                default:
                    return false;
            }
        }

        private static string WaitParamLabel(CompanionTutorialTokens.WaitCategory category)
        {
            switch (category)
            {
                case CompanionTutorialTokens.WaitCategory.Delay: return "Seconds";
                case CompanionTutorialTokens.WaitCategory.AssetExists: return "Asset path";
                case CompanionTutorialTokens.WaitCategory.PackageInstalled: return "Package name";
                case CompanionTutorialTokens.WaitCategory.ComponentExists: return "Type name";
                case CompanionTutorialTokens.WaitCategory.TransformMoved: return "Object (blank = selection)";
                default: return "Value";
            }
        }

        private static string ComposeWait(CompanionTutorialTokens.WaitCategory category, string arg)
        {
            arg = (arg ?? string.Empty).Trim();
            switch (category)
            {
                case CompanionTutorialTokens.WaitCategory.Manual: return "manual";
                case CompanionTutorialTokens.WaitCategory.Selection: return "selection";
                case CompanionTutorialTokens.WaitCategory.Delay: return "delay:" + (string.IsNullOrEmpty(arg) ? "2" : arg);
                case CompanionTutorialTokens.WaitCategory.AssetExists: return "assetExists:" + arg;
                case CompanionTutorialTokens.WaitCategory.PackageInstalled: return "packageInstalled:" + arg;
                case CompanionTutorialTokens.WaitCategory.ComponentExists: return "componentExists:" + arg;
                case CompanionTutorialTokens.WaitCategory.TransformMoved: return "transformMoved:" + arg;
                default: return arg;
            }
        }

        private static string DefaultWaitArg(CompanionTutorialTokens.WaitCategory category)
        {
            switch (category)
            {
                case CompanionTutorialTokens.WaitCategory.Delay: return "2";
                case CompanionTutorialTokens.WaitCategory.TransformMoved: return "selected";
                default: return string.Empty;
            }
        }

        // Used by Height(): map the step property to its current target category.
        private static CompanionTutorialTokens.TargetCategory TargetCategoryOf(SerializedProperty step)
        {
            return ParseTargetCategory(step.FindPropertyRelative("target").stringValue, out _);
        }

        private static CompanionTutorialTokens.WaitCategory WaitCategoryOf(SerializedProperty step)
        {
            return ParseWaitCategory(step.FindPropertyRelative("waitFor").stringValue, out _);
        }
    }

    [CustomPropertyDrawer(typeof(CompanionTutorialStep))]
    internal sealed class CompanionTutorialStepDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return CompanionTutorialStepGUI.Height(property);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            CompanionTutorialStepGUI.Draw(position, property, label != null ? label.text : "Title");
            EditorGUI.EndProperty();
        }
    }
}
