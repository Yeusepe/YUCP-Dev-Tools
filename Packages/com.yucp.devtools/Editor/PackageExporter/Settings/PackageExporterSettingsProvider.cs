using UnityEditor;

namespace YUCP.DevTools.Editor.PackageExporter.Settings
{
    public class PackageExporterSettingsProvider : SettingsProvider
    {
        public PackageExporterSettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
            : base(path, scope) { }

        public override void OnGUI(string searchContext)
        {
            string url = BackstageReleasePublishService.ResolveApiBaseUrl();
            EditorGUI.BeginChangeCheck();
            string configuredUrl = EditorGUILayout.TextField("YUCP API URL", url);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(
                    BackstageReleasePublishService.ApiBaseUrlEditorPref,
                    configuredUrl.Trim().TrimEnd('/'));
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new PackageExporterSettingsProvider("Project/YUCP Package Exporter", SettingsScope.Project);
        }
    }
}
