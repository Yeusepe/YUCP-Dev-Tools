using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.Optimizer
{
    /// <summary>
    /// Persists the last-used optimizer options. Mirrors the load-or-create pattern used by
    /// AvatarUploaderSettings so the asset (and its folder) is created on first use.
    /// </summary>
    public class RendererOptimizerSettings : ScriptableObject
    {
        private const string SettingsAssetPath = "Assets/Editor/YUCPOptimizerSettings.asset";

        private static RendererOptimizerSettings _instance;
        public static RendererOptimizerSettings Instance => _instance ??= LoadOrCreateSettings();

        [SerializeField] private OptimizerOptions options = new OptimizerOptions();

        public OptimizerOptions Options => options;

        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        private static RendererOptimizerSettings LoadOrCreateSettings()
        {
            var normalizedPath = SettingsAssetPath.Replace("\\", "/");
            var settings = AssetDatabase.LoadAssetAtPath<RendererOptimizerSettings>(normalizedPath);
            if (settings != null)
                return settings;

            var dir = System.IO.Path.GetDirectoryName(normalizedPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                var segments = dir.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
                var path = segments[0];
                for (int i = 1; i < segments.Length; i++)
                {
                    var next = path + "/" + segments[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(path, segments[i]);
                    path = next;
                }
            }

            settings = CreateInstance<RendererOptimizerSettings>();
            AssetDatabase.CreateAsset(settings, normalizedPath);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
