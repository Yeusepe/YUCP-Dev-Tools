using System;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Manages packageId assignment for ExportProfile
    /// Auto-generates packageId on first export, allows unlinking to generate new one
    /// </summary>
    public static class PackageIdManager
    {
        /// <summary>
        /// Assign a packageId to the profile if it doesn't have one
        /// Returns the packageId (existing or newly generated)
        /// </summary>
        public static string AssignPackageId(ExportProfile profile)
        {
            if (profile == null)
            {
                Debug.LogError("[PackageIdManager] ExportProfile is null");
                return null;
            }

            // If packageId already exists, return it
            if (!string.IsNullOrEmpty(profile.packageId))
            {
                return profile.packageId;
            }

            // Generate new packageId
            string newPackageId = GenerateNewPackageId();
            profile.packageId = newPackageId;
            
            // Mark profile as dirty so it saves
            EditorUtility.SetDirty(profile);
            
            Debug.Log($"[PackageIdManager] Generated new packageId: {newPackageId} for profile: {profile.profileName}");
            
            return newPackageId;
        }

        /// <summary>
        /// Get the packageId from the profile
        /// </summary>
        public static string GetPackageId(ExportProfile profile)
        {
            if (profile == null)
                return null;

            return string.IsNullOrEmpty(profile.packageId) ? null : profile.packageId;
        }

        /// <summary>
        /// Unlink (clear) the packageId from the profile
        /// Next export will generate a new one
        /// </summary>
        public static void UnlinkPackageId(ExportProfile profile)
        {
            if (profile == null)
            {
                Debug.LogError("[PackageIdManager] ExportProfile is null");
                return;
            }

            string oldPackageId = profile.packageId;
            profile.packageId = "";
            
            // Mark profile as dirty so it saves
            EditorUtility.SetDirty(profile);
            
            Debug.Log($"[PackageIdManager] Unlinked packageId: {oldPackageId} from profile: {profile.profileName}");
        }

        /// <summary>
        /// Generate a new UUID/GUID format packageId
        /// Format: lowercase UUID without dashes (32 hex characters) for compactness
        /// </summary>
        public static string GenerateNewPackageId()
        {
            return Guid.NewGuid().ToString("N").ToLowerInvariant();
        }
    }
}






































