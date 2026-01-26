using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Helpers for reading DerivedSettings from importer userData.
    /// </summary>
    public static class DerivedSettingsUtility
    {
        public static bool TryRead(ModelImporter importer, out DerivedSettings settings)
        {
            settings = null;
            if (importer == null) return false;
            return TryRead(importer.userData, out settings);
        }

        public static bool TryRead(string userDataJson, out DerivedSettings settings)
        {
            settings = null;
            if (string.IsNullOrEmpty(userDataJson)) return false;
            try
            {
                settings = JsonUtility.FromJson<DerivedSettings>(userDataJson);
                return settings != null;
            }
            catch
            {
                settings = null;
                return false;
            }
        }
    }
}
