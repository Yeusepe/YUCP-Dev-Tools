using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Custom editor for CustomVersionRule with test functionality
    /// </summary>
    [CustomEditor(typeof(CustomVersionRule))]
    public class CustomVersionRuleEditor : UnityEditor.Editor
    {
        private string testResult = "";
        private bool testPassed = false;

        public override void OnInspectorGUI()
        {
            var rule = (CustomVersionRule)target;

            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Testing", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Test Rule", GUILayout.Height(30)))
            {
                testResult = rule.TestRule();
                testPassed = testResult == rule.exampleOutput;
                
                Debug.Log($"[CustomVersionRule] Test result: {testResult} (Expected: {rule.exampleOutput})");
            }
            
            if (GUILayout.Button("Register Rule", GUILayout.Height(30)))
            {
                rule.RegisterRule();
                Debug.Log($"[CustomVersionRule] Registered rule '{rule.ruleName}'");
                EditorUtility.DisplayDialog(
                    "Rule Registered",
                    $"Rule '{rule.ruleName}' ({rule.displayName}) has been registered.\n\n" +
                    "You can now use it in export profiles.",
                    "OK"
                );
            }
            
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(testResult))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                EditorGUILayout.LabelField("Test Result:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Input:    {rule.exampleInput}");
                EditorGUILayout.LabelField($"Expected: {rule.exampleOutput}");
                
                var originalColor = GUI.color;
                GUI.color = testPassed ? Color.green : Color.red;
                EditorGUILayout.LabelField($"Actual:   {testResult}", EditorStyles.boldLabel);
                GUI.color = originalColor;
                
                EditorGUILayout.LabelField(testPassed ? "✓ Test PASSED" : "✗ Test FAILED", 
                    new GUIStyle(EditorStyles.label) 
                    { 
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = testPassed ? Color.green : Color.red }
                    });
                
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox(
                "Built-in Rule Types:\n" +
                "• Semver: Standard semantic versioning (MAJOR.MINOR.PATCH)\n" +
                "• DottedTail: Increment last dotted component\n" +
                "• WordNum: Word followed by number (VERSION1)\n" +
                "• Build: 4-part version (MAJOR.MINOR.PATCH.BUILD)\n" +
                "• CalVer: Calendar versioning (YYYY.MM.DD)\n" +
                "• Number: Simple number increment\n" +
                "• Custom: Requires code implementation",
                MessageType.Info
            );

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "Regex Groups:\n" +
                "Use named groups like (?<major>\\d+) to capture parts.\n" +
                "Common groups: major, minor, patch, build, name, num, prefix, last, year, month, day",
                MessageType.Info
            );
        }
    }
}









