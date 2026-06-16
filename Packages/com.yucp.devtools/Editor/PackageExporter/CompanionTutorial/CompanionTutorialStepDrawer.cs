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
            if (CompanionTutorialTokens.WaitNeedsParam(WaitCategoryOf(step)))
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
            var category = CompanionTutorialTokens.ParseTargetCategory(pTarget.stringValue, out string selector);

            int catIndex = (int)category;
            int newCatIndex = EditorGUI.Popup(next(Line), "Target", catIndex, CompanionTutorialTokens.TargetCategoryLabels);
            if (newCatIndex != catIndex)
            {
                category = (CompanionTutorialTokens.TargetCategory)newCatIndex;
                pTarget.stringValue = CompanionTutorialTokens.ComposeTarget(category, CompanionTutorialTokens.DefaultSelectorFor(category));
                selector = CompanionTutorialTokens.DefaultSelectorFor(category);
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
                    string newSelector = EditorGUI.TextField(next(Line), CompanionTutorialTokens.SelectorLabelFor(category), selector);
                    if (newSelector != selector)
                        pTarget.stringValue = CompanionTutorialTokens.ComposeTarget(category, newSelector);
                    break;
                }
            }
        }

        private static void DrawWait(System.Func<float, Rect> next, SerializedProperty pWait)
        {
            var category = CompanionTutorialTokens.ParseWaitCategory(pWait.stringValue, out string arg);
            int catIndex = (int)category;
            int newCatIndex = EditorGUI.Popup(next(Line), "Advance When", catIndex, CompanionTutorialTokens.WaitCategoryLabels);
            if (newCatIndex != catIndex)
            {
                category = (CompanionTutorialTokens.WaitCategory)newCatIndex;
                pWait.stringValue = CompanionTutorialTokens.ComposeWait(category, CompanionTutorialTokens.DefaultWaitArg(category));
                arg = CompanionTutorialTokens.DefaultWaitArg(category);
            }

            if (category == CompanionTutorialTokens.WaitCategory.Custom)
            {
                EditorGUI.PropertyField(next(Line), pWait, new GUIContent("Raw waitFor"));
            }
            else if (CompanionTutorialTokens.WaitNeedsParam(category))
            {
                string label = CompanionTutorialTokens.WaitParamLabel(category);
                string newArg = EditorGUI.TextField(next(Line), label, arg);
                if (newArg != arg)
                    pWait.stringValue = CompanionTutorialTokens.ComposeWait(category, newArg);
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

        // Used by Height(): map the step property to its current target/wait category.
        private static CompanionTutorialTokens.TargetCategory TargetCategoryOf(SerializedProperty step)
        {
            return CompanionTutorialTokens.ParseTargetCategory(step.FindPropertyRelative("target").stringValue, out _);
        }

        private static CompanionTutorialTokens.WaitCategory WaitCategoryOf(SerializedProperty step)
        {
            return CompanionTutorialTokens.ParseWaitCategory(step.FindPropertyRelative("waitFor").stringValue, out _);
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
