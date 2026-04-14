using System.IO;
using UnityEditor;
using UnityEngine;
using YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    public static class SigningSettingsLocator
    {
        public const string CanonicalAssetPath = "Assets/YUCP/SigningSettings.asset";

        public static SigningSettings Load()
        {
            var canonical = LoadAtPath(CanonicalAssetPath);
            if (canonical != null)
                return canonical;

            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length == 0)
                return null;

            if (guids.Length == 1)
                return LoadAtPath(AssetDatabase.GUIDToAssetPath(guids[0]));

            Debug.LogWarning(
                $"[YUCP] Multiple SigningSettings assets were found. Move the trusted asset to '{CanonicalAssetPath}' or delete the duplicates.");
            return null;
        }

        public static SigningSettings GetOrCreate()
        {
            var settings = Load();
            if (settings != null)
                return settings;

            string directory = Path.GetDirectoryName(CanonicalAssetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            settings = ScriptableObject.CreateInstance<SigningSettings>();
            AssetDatabase.CreateAsset(settings, CanonicalAssetPath);
            AssetDatabase.SaveAssets();
            return Load();
        }

        private static SigningSettings LoadAtPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            var settings = AssetDatabase.LoadAssetAtPath<SigningSettings>(assetPath);
            if (settings != null && settings.NormalizeServerConfiguration())
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            return settings;
        }
    }
}
