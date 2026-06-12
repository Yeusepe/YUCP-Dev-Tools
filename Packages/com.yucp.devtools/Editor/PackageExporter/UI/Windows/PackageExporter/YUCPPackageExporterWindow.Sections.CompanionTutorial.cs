using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.CompanionTutorial.Generated.Source;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
        private VisualElement CreateCompanionTutorialSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.name = "section-companion-tutorial";

            var header = CreateCollapsibleHeader("Companion Tutorial",
                () => showCompanionTutorial,
                value => { showCompanionTutorial = value; },
                () => UpdateProfileDetails());
            section.Add(header);

            if (!showCompanionTutorial)
                return section;

            var helpBox = new VisualElement();
            helpBox.AddToClassList("yucp-help-box");
            var helpText = new Label("Optional whole-Unity install tutorial that auto-plays once after a buyer imports the package. The overlay is Windows-only and click-through; steps advance from the chosen wait condition. Use the dropdowns to target Unity UI without memorizing strings.");
            helpText.AddToClassList("yucp-help-box-text");
            helpBox.Add(helpText);
            section.Add(helpBox);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 8;
            buttonRow.style.marginBottom = 8;

            var previewButton = new Button(() =>
            {
                if (profile != null && profile.companionTutorial != null)
                    CompanionTutorialRunner.QueueRunFromJson(JsonUtility.ToJson(profile.companionTutorial));
            })
            {
                text = "Preview"
            };
            previewButton.AddToClassList("yucp-button");
            previewButton.SetEnabled(profile != null && profile.companionTutorial != null && profile.companionTutorial.enabled);
            buttonRow.Add(previewButton);

            var demoButton = new Button(CompanionTutorialRunner.StartDemo)
            {
                text = "Run Demo"
            };
            demoButton.AddToClassList("yucp-button");
            demoButton.style.marginLeft = 8;
            buttonRow.Add(demoButton);

            var stopButton = new Button(CompanionTutorialRunner.Stop)
            {
                text = "Stop"
            };
            stopButton.AddToClassList("yucp-button");
            stopButton.style.marginLeft = 8;
            buttonRow.Add(stopButton);

            section.Add(buttonRow);

            section.Add(BuildCompanionTutorialEditor(profile, previewButton));

            return section;
        }

        private VisualElement BuildCompanionTutorialEditor(ExportProfile profile, Button previewButton)
        {
            // State captured by the IMGUIContainer; lives as long as the section is shown.
            SerializedObject serializedProfile = profile != null ? new SerializedObject(profile) : null;
            SerializedProperty tutorialProp = serializedProfile?.FindProperty("companionTutorial");
            SerializedProperty enabledProp = tutorialProp?.FindPropertyRelative("enabled");
            SerializedProperty titleProp = tutorialProp?.FindPropertyRelative("title");
            SerializedProperty stepsProp = tutorialProp?.FindPropertyRelative("steps");

            ReorderableList list = null;
            if (stepsProp != null)
            {
                list = new ReorderableList(serializedProfile, stepsProp, true, true, true, true);
                list.drawHeaderCallback = r => EditorGUI.LabelField(r, "Steps");
                list.elementHeightCallback = index =>
                {
                    var step = stepsProp.GetArrayElementAtIndex(index);
                    return CompanionTutorialStepGUI.Height(step) + EditorGUIUtility.singleLineHeight + 12f;
                };
                list.drawElementCallback = (rect, index, active, focused) =>
                {
                    var step = stepsProp.GetArrayElementAtIndex(index);
                    float line = EditorGUIUtility.singleLineHeight;

                    var headerRect = new Rect(rect.x, rect.y + 2f, rect.width, line);
                    EditorGUI.LabelField(new Rect(headerRect.x, headerRect.y, headerRect.width - 120, line), $"Step {index + 1}", EditorStyles.boldLabel);

                    var testRect = new Rect(headerRect.xMax - 116, headerRect.y, 112, line);
                    if (GUI.Button(testRect, new GUIContent("Test from here", "Preview the tutorial starting at this step")))
                    {
                        serializedProfile.ApplyModifiedProperties();
                        if (profile.companionTutorial != null)
                            CompanionTutorialRunner.QueueRunFromJson(JsonUtility.ToJson(profile.companionTutorial), index);
                    }

                    var bodyRect = new Rect(rect.x, rect.y + line + 6f, rect.width, rect.height - line - 8f);
                    CompanionTutorialStepGUI.Draw(bodyRect, step, "Title");
                };
                list.onAddCallback = l =>
                {
                    int insert = stepsProp.arraySize;
                    stepsProp.InsertArrayElementAtIndex(insert);
                    var added = stepsProp.GetArrayElementAtIndex(insert);
                    added.FindPropertyRelative("id").stringValue = System.Guid.NewGuid().ToString("N");
                    added.FindPropertyRelative("title").stringValue = $"Step {insert + 1}";
                    added.FindPropertyRelative("text").stringValue = "";
                    added.FindPropertyRelative("target").stringValue = "center";
                    added.FindPropertyRelative("waitFor").stringValue = "manual";
                    added.FindPropertyRelative("mouseAction").stringValue = "none";
                    added.FindPropertyRelative("overlayMode").stringValue = "intrusive";
                    added.FindPropertyRelative("spotlightPadding").vector4Value = new Vector4(12, 12, 12, 12);
                    added.FindPropertyRelative("targetRect").vector4Value = Vector4.zero;
                };
            }

            var container = new IMGUIContainer(() =>
            {
                if (serializedProfile == null)
                    return;

                serializedProfile.Update();

                EditorGUILayout.PropertyField(enabledProp, new GUIContent("Enable tutorial"));
                using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
                {
                    EditorGUILayout.PropertyField(titleProp, new GUIContent("Tutorial title"));

                    // Duplicate-step row (the ReorderableList already provides add/remove/reorder).
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(list == null || list.index < 0 || list.index >= stepsProp.arraySize))
                        {
                            if (GUILayout.Button("Duplicate selected step", GUILayout.Width(180)))
                                DuplicateStep(stepsProp, list.index);
                        }
                    }

                    list?.DoLayoutList();

                    DrawCompanionValidationSummary(profile);
                }

                if (serializedProfile.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(profile);
                    previewButton?.SetEnabled(profile.companionTutorial != null && profile.companionTutorial.enabled);
                }
            });
            container.style.marginTop = 6;
            return container;
        }

        private static void DuplicateStep(SerializedProperty stepsProp, int index)
        {
            if (stepsProp == null || index < 0 || index >= stepsProp.arraySize)
                return;

            stepsProp.InsertArrayElementAtIndex(index);
            var copy = stepsProp.GetArrayElementAtIndex(index + 1);
            // Give the duplicate a fresh id so the two steps aren't confused by tooling.
            copy.FindPropertyRelative("id").stringValue = System.Guid.NewGuid().ToString("N");
        }

        private static void DrawCompanionValidationSummary(ExportProfile profile)
        {
            if (profile?.companionTutorial == null || !profile.companionTutorial.enabled)
                return;

            List<CompanionTutorialValidator.Finding> findings = CompanionTutorialValidator.Validate(profile.companionTutorial);
            if (findings.Count == 0)
            {
                EditorGUILayout.HelpBox("Tutorial looks good.", MessageType.Info);
                return;
            }

            int errors = 0, warnings = 0;
            foreach (var f in findings)
            {
                if (f.Severity == CompanionTutorialValidator.Severity.Error) errors++;
                else if (f.Severity == CompanionTutorialValidator.Severity.Warning) warnings++;
            }

            string summary = $"Tutorial has {errors} error(s) and {warnings} warning(s). Per-step details appear above.";
            EditorGUILayout.HelpBox(summary, errors > 0 ? MessageType.Error : MessageType.Warning);
        }
    }
}
