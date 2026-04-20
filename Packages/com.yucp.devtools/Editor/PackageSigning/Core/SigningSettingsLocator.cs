using System.IO;
using UnityEditor;
using UnityEngine;
using YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    public static class SigningSettingsLocator
    {
        public const string CanonicalAssetPath = "Assets/YUCP/SigningSettings.asset";

        private static bool _cacheInitialized;
        private static string _cachedAssetPath;
        private static SigningSettings _cachedSettings;

        static SigningSettingsLocator()
        {
            EditorApplication.projectChanged += ClearCache;
            AssemblyReloadEvents.beforeAssemblyReload += ClearCache;
        }

        public static SigningSettings Load()
        {
            if (TryGetCached(out SigningSettings cached))
                return cached;

            var canonical = LoadAtPath(CanonicalAssetPath);
            if (canonical != null)
                return Cache(CanonicalAssetPath, canonical);

            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length == 0)
            {
                Cache(null, null);
                return null;
            }

            if (guids.Length == 1)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                return Cache(assetPath, LoadAtPath(assetPath));
            }

            Debug.LogWarning(
                $"[YUCP] Multiple SigningSettings assets were found. Move the trusted asset to '{CanonicalAssetPath}' or delete the duplicates.");
            Cache(null, null);
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

        private static bool TryGetCached(out SigningSettings settings)
        {
            if (!_cacheInitialized)
            {
                settings = null;
                return false;
            }

            if (string.IsNullOrEmpty(_cachedAssetPath))
            {
                settings = null;
                return true;
            }

            if (_cachedSettings == null)
                _cachedSettings = AssetDatabase.LoadAssetAtPath<SigningSettings>(_cachedAssetPath);

            if (_cachedSettings == null)
            {
                ClearCache();
                settings = null;
                return false;
            }

            if (_cachedSettings.NormalizeServerConfiguration())
            {
                EditorUtility.SetDirty(_cachedSettings);
                AssetDatabase.SaveAssets();
            }

            settings = _cachedSettings;
            return true;
        }

        private static SigningSettings Cache(string assetPath, SigningSettings settings)
        {
            _cacheInitialized = true;
            _cachedAssetPath = assetPath;
            _cachedSettings = settings;
            return settings;
        }

        private static void ClearCache()
        {
            _cacheInitialized = false;
            _cachedAssetPath = null;
            _cachedSettings = null;
        }
    }
}
