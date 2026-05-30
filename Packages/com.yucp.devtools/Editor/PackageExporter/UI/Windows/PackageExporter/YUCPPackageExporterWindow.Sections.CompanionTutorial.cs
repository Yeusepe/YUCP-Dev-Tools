using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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
            var helpText = new Label("Optional whole-Unity install tutorial. The overlay is Windows-only, click-through, and advances from wait conditions.");
            helpText.AddToClassList("yucp-help-box-text");
            helpBox.Add(helpText);
            section.Add(helpBox);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 8;
            buttonRow.style.marginBottom = 8;

            var previewButton = new Button(() =>
            {
                if (profile != null)
                    CompanionTutorialRunner.Start(profile.companionTutorial);
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

            var propertyContainer = new IMGUIContainer(() =>
            {
                if (profile == null)
                    return;

                var serializedProfile = new SerializedObject(profile);
                serializedProfile.Update();
                SerializedProperty tutorialProperty = serializedProfile.FindProperty("companionTutorial");
                if (tutorialProperty != null)
                {
                    EditorGUILayout.PropertyField(tutorialProperty, new GUIContent("Tutorial"), true);
                }

                if (serializedProfile.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(profile);
                }
            });
            propertyContainer.style.marginTop = 6;
            section.Add(propertyContainer);

            return section;
        }
    }
}
