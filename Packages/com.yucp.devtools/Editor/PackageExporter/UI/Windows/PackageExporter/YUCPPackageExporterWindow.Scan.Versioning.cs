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

        private void CreateCustomRule(ExportProfile profile)
        {
            string savePath = EditorUtility.SaveFilePanelInProject(
                "Create Custom Version Rule",
                "CustomVersionRule",
                "asset",
                "Create a new custom version rule asset"
            );

            if (string.IsNullOrEmpty(savePath))
                return;

            var customRule = ScriptableObject.CreateInstance<CustomVersionRule>();
            customRule.ruleName = "my_custom_rule";
            customRule.displayName = "My Custom Rule";
            customRule.description = "Custom version bumping rule";
            customRule.regexPattern = @"\b(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\b";
            customRule.ruleType = CustomVersionRule.RuleType.Semver;
            customRule.exampleInput = "1.0.0";
            customRule.exampleOutput = "1.0.1";

            AssetDatabase.CreateAsset(customRule, savePath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = customRule;
            EditorGUIUtility.PingObject(customRule);


            if (profile != null)
            {
                Undo.RecordObject(profile, "Assign Custom Rule");
                profile.customVersionRule = customRule;
                customRule.RegisterRule();
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            }

            EditorUtility.DisplayDialog(
                "Custom Rule Created",
                $"Custom version rule created at:\n{savePath}\n\n" +
                "Edit the asset to configure:\n" +
                "• Rule name\n" +
                "• Regex pattern\n" +
                "• Rule type\n" +
                "• Test with examples",
                "OK"
            );
        }

        private void TestCustomRule(CustomVersionRule rule)
        {
            if (rule == null)
                return;

            string result = rule.TestRule();

            EditorUtility.DisplayDialog(
                $"Test Rule: {rule.displayName}",
                $"Rule Name: {rule.ruleName}\n" +
                $"Type: {rule.ruleType}\n" +
                $"Pattern: {rule.regexPattern}\n\n" +
                $"Example Input: {rule.exampleInput}\n" +
                $"Expected Output: {rule.exampleOutput}\n" +
                $"Actual Output: {result}\n\n" +
                (result == rule.exampleOutput ? "[OK] Test PASSED" : "[X] Test FAILED"),
                "OK"
            );
        }

        private string GetStrategyExplanation(VersionIncrementStrategy strategy)
        {
            return strategy switch
            {
                VersionIncrementStrategy.Major => "Breaking changes: 1.0.0 → 2.0.0",
                VersionIncrementStrategy.Minor => "New features: 1.0.0 → 1.1.0",
                VersionIncrementStrategy.Patch => "Bug fixes: 1.0.0 → 1.0.1",
                VersionIncrementStrategy.Build => "Build number: 1.0.0.0 → 1.0.0.1",
                _ => ""
            };
        }

    }
}
