using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using YUCP.DevTools.Components;
using YUCP.DevTools.Editor.PackageExporter.UI.Components;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
        private VisualElement CreateCustomRuleEditor(ExportProfile profile)
        {
            var rule = profile.customVersionRule;
            if (rule == null) return new VisualElement();
            
            var editorContainer = new VisualElement();
            editorContainer.style.backgroundColor = new UnityEngine.UIElements.StyleColor(new Color(0.25f, 0.3f, 0.35f, 0.3f));
            editorContainer.style.borderTopWidth = 1;
            editorContainer.style.borderBottomWidth = 1;
            editorContainer.style.borderLeftWidth = 1;
            editorContainer.style.borderRightWidth = 1;
            editorContainer.style.borderTopColor = new UnityEngine.UIElements.StyleColor(new Color(0.3f, 0.4f, 0.5f));
            editorContainer.style.borderBottomColor = new UnityEngine.UIElements.StyleColor(new Color(0.3f, 0.4f, 0.5f));
            editorContainer.style.borderLeftColor = new UnityEngine.UIElements.StyleColor(new Color(0.3f, 0.4f, 0.5f));
            editorContainer.style.borderRightColor = new UnityEngine.UIElements.StyleColor(new Color(0.3f, 0.4f, 0.5f));
            editorContainer.style.borderTopLeftRadius = 4;
            editorContainer.style.borderTopRightRadius = 4;
            editorContainer.style.borderBottomLeftRadius = 4;
            editorContainer.style.borderBottomRightRadius = 4;
            editorContainer.style.paddingTop = 10;
            editorContainer.style.paddingBottom = 10;
            editorContainer.style.paddingLeft = 10;
            editorContainer.style.paddingRight = 10;
            editorContainer.style.marginTop = 8;
            editorContainer.style.marginBottom = 8;
            
            var title = new Label($"Editing: {rule.displayName}");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 12;
            title.style.marginBottom = 8;
            editorContainer.Add(title);
            
            // Rule Name
            var ruleNameRow = CreateFormRow("Rule Name", tooltip: "Identifier used in @bump directives (lowercase, no spaces)");
            var ruleNameField = new TextField { value = rule.ruleName };
            ruleNameField.AddToClassList("yucp-input");
            ruleNameField.AddToClassList("yucp-form-field");
            ruleNameField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Rule Name");
                rule.ruleName = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            ruleNameRow.Add(ruleNameField);
            editorContainer.Add(ruleNameRow);
            
            // Display Name
            var displayNameRow = CreateFormRow("Display Name", tooltip: "Human-readable name");
            var displayNameField = new TextField { value = rule.displayName };
            displayNameField.AddToClassList("yucp-input");
            displayNameField.AddToClassList("yucp-form-field");
            displayNameField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Display Name");
                rule.displayName = evt.newValue;
                EditorUtility.SetDirty(rule);
                UpdateProfileDetails(); // Refresh title
            });
            displayNameRow.Add(displayNameField);
            editorContainer.Add(displayNameRow);
            
            // Description
            var descLabel = new Label("Description");
            descLabel.AddToClassList("yucp-label");
            descLabel.style.marginTop = 8;
            descLabel.style.marginBottom = 4;
            editorContainer.Add(descLabel);
            
            var descField = new TextField { value = rule.description, multiline = true };
            descField.AddToClassList("yucp-input");
            descField.AddToClassList("yucp-input-multiline");
            descField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Description");
                rule.description = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            editorContainer.Add(descField);
            
            // Regex Pattern
            var patternLabel = new Label("Regex Pattern");
            patternLabel.AddToClassList("yucp-label");
            patternLabel.style.marginTop = 8;
            patternLabel.style.marginBottom = 4;
            editorContainer.Add(patternLabel);
            
            var patternField = new TextField { value = rule.regexPattern, multiline = true };
            patternField.AddToClassList("yucp-input");
            patternField.AddToClassList("yucp-input-multiline");
            patternField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Regex Pattern");
                rule.regexPattern = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            editorContainer.Add(patternField);
            
            // Rule Type
            var ruleTypeRow = CreateFormRow("Rule Type", tooltip: "Base behavior for this rule");
            var ruleTypeField = new EnumField(rule.ruleType);
            ruleTypeField.AddToClassList("yucp-dropdown");
            ruleTypeField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Rule Type");
                rule.ruleType = (CustomVersionRule.RuleType)evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            ruleTypeRow.Add(ruleTypeField);
            editorContainer.Add(ruleTypeRow);
            
            // Options row
            var optionsRow = new VisualElement();
            optionsRow.style.flexDirection = FlexDirection.Row;
            optionsRow.style.marginTop = 8;
            
            var supportsPartsToggle = new Toggle("Supports Parts") { value = rule.supportsParts };
            supportsPartsToggle.AddToClassList("yucp-toggle");
            supportsPartsToggle.tooltip = "Whether the rule understands major/minor/patch";
            supportsPartsToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Supports Parts");
                rule.supportsParts = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            optionsRow.Add(supportsPartsToggle);
            
            var preservePaddingToggle = new Toggle("Preserve Padding") { value = rule.preservePadding };
            preservePaddingToggle.AddToClassList("yucp-toggle");
            preservePaddingToggle.tooltip = "Keep zero padding in numbers (007 → 008)";
            preservePaddingToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Preserve Padding");
                rule.preservePadding = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            optionsRow.Add(preservePaddingToggle);
            
            editorContainer.Add(optionsRow);
            
            // Test section
            var testLabel = new Label("Test");
            testLabel.AddToClassList("yucp-label");
            testLabel.style.marginTop = 12;
            testLabel.style.marginBottom = 4;
            editorContainer.Add(testLabel);
            
            var testRow = new VisualElement();
            testRow.style.flexDirection = FlexDirection.Row;
            testRow.style.marginBottom = 4;
            
            var inputLabel = new Label("Input:");
            inputLabel.style.width = 60;
            inputLabel.style.marginRight = 4;
            testRow.Add(inputLabel);
            
            var exampleInputField = new TextField { value = rule.exampleInput };
            exampleInputField.AddToClassList("yucp-input");
            exampleInputField.style.flexGrow = 1;
            exampleInputField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Example Input");
                rule.exampleInput = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            testRow.Add(exampleInputField);
            
            editorContainer.Add(testRow);
            
            var expectedRow = new VisualElement();
            expectedRow.style.flexDirection = FlexDirection.Row;
            expectedRow.style.marginBottom = 8;
            
            var expectedLabel = new Label("Expected:");
            expectedLabel.style.width = 60;
            expectedLabel.style.marginRight = 4;
            expectedRow.Add(expectedLabel);
            
            var exampleOutputField = new TextField { value = rule.exampleOutput };
            exampleOutputField.AddToClassList("yucp-input");
            exampleOutputField.style.flexGrow = 1;
            exampleOutputField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(rule, "Change Example Output");
                rule.exampleOutput = evt.newValue;
                EditorUtility.SetDirty(rule);
            });
            expectedRow.Add(exampleOutputField);
            
            editorContainer.Add(expectedRow);
            
            // Action buttons
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.FlexEnd;
            buttonRow.style.marginTop = 8;
            
            var testBtn = new Button(() => TestCustomRule(rule)) { text = "Test Rule" };
            testBtn.AddToClassList("yucp-button");
            testBtn.tooltip = "Test the rule with the example input";
            buttonRow.Add(testBtn);
            
            var registerBtn = new Button(() => 
            {
                rule.RegisterRule();
                EditorUtility.DisplayDialog("Rule Registered", $"Rule '{rule.ruleName}' has been registered and is ready to use.", "OK");
            }) { text = "Save & Register" };
            registerBtn.AddToClassList("yucp-button");
            registerBtn.tooltip = "Register this rule so it can be used";
            buttonRow.Add(registerBtn);
            
            var selectBtn = new Button(() => 
            {
                Selection.activeObject = rule;
                EditorGUIUtility.PingObject(rule);
            }) { text = "Select Asset" };
            selectBtn.AddToClassList("yucp-button");
            selectBtn.tooltip = "Select the rule asset in the project";
            buttonRow.Add(selectBtn);
            
            editorContainer.Add(buttonRow);
            
            return editorContainer;
        }

    }
}
