using System.IO;
using UnityEditor;
using UnityEngine;
using YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    /// <summary>
    /// Embeds manifest and signature into .unitypackage
    /// </summary>
    public static class SignatureEmbedder
    {
        private const string SigningFolder = "Assets/_Signing";
        private const string ManifestFileName = "PackageManifest.json";
        private const string SignatureFileName = "PackageManifest.sig";

        /// <summary>
        /// Embed manifest and signature as TextAssets in the package
        /// </summary>
        public static void EmbedSigningData(PackageManifest manifest, SignatureData signature)
        {
            // Ensure signing folder exists
            if (!AssetDatabase.IsValidFolder(SigningFolder))
            {
                string parentFolder = "Assets";
                string folderName = "_Signing";
                AssetDatabase.CreateFolder(parentFolder, folderName);
            }

            // Create manifest TextAsset
            string manifestJson = JsonUtility.ToJson(manifest, true);
            string manifestPath = $"{SigningFolder}/{ManifestFileName}";
            File.WriteAllText(manifestPath, manifestJson);
            AssetDatabase.ImportAsset(manifestPath);

            // Create signature TextAsset
            string signatureJson = JsonUtility.ToJson(signature, true);
            string signaturePath = $"{SigningFolder}/{SignatureFileName}";
            File.WriteAllText(signaturePath, signatureJson);
            AssetDatabase.ImportAsset(signaturePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Remove signing data from project (cleanup after export)
        /// </summary>
        public static void RemoveSigningData()
        {
            if (AssetDatabase.IsValidFolder(SigningFolder))
            {
                AssetDatabase.DeleteAsset(SigningFolder);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
