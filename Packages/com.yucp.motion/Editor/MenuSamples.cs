#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace YUCP.Motion.Editor
{
    /// <summary>
    /// Menu items for easy demo access.
    /// </summary>
    public static class MenuSamples
    {
        [MenuItem("Tools/YUCP/Others/Motion/Open Inspector Demo")]
        public static void OpenInspectorDemo()
        {
            // This would open a demo window or scene
            Debug.Log("Inspector Demo - Use a custom Editor with CreateInspectorGUI");
        }
        
        [MenuItem("Tools/YUCP/Others/Motion/Open EditorWindow Demo")]
        public static void OpenEditorWindowDemo()
        {
            EditorWindowDemo.Open();
        }
        
        [MenuItem("Tools/YUCP/Others/Motion/Open Reorder Demo")]
        public static void OpenReorderDemo()
        {
            SimpleReorderSample.Open();
        }
        
        [MenuItem("Tools/YUCP/Others/Motion/Open Runtime Demo")]
        public static void OpenRuntimeDemo()
        {
            // This would open a demo scene
            Debug.Log("Runtime Demo - Create a scene with UIDocument and use Motion.Attach");
        }
    }
}
#endif
