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
        private void OnEnable()
        {
            // Initialize Motion system
            global::YUCP.Motion.Motion.Initialize();
            
            LoadProfiles();
            LoadProjectFolders();
            LoadResources();
            
            // Migrate to unified order if needed
            MigrateToUnifiedOrder();
            LoadUnifiedOrder();
            
            // Refresh dependencies on domain reload
            EditorApplication.delayCall += RefreshDependenciesOnDomainReload;
            
            // Register update for gap animation (vFavorites approach)
            EditorApplication.update -= UpdateGapAnimations;
            EditorApplication.update += UpdateGapAnimations;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateGapAnimations;
        }

        private void RefreshDependenciesOnDomainReload()
        {
            // Refresh dependencies for all profiles that have dependencies configured
            if (selectedProfile != null && selectedProfile.dependencies.Count > 0)
            {
                // Silently refresh dependencies to pick up newly installed packages
                ScanProfileDependencies(selectedProfile, silent: true);
            }
        }

        private void LoadResources()
        {
            logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DevTools.png");
            CreateBannerGradientTexture();
            CreateDottedBorderTexture();
        }

        private static bool IsDefaultGridPlaceholder(Texture2D texture)
        {
            if (texture == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(texture);
            return assetPath == DefaultGridPlaceholderPath;
        }

        private static Texture2D GetPlaceholderTexture()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultGridPlaceholderPath);
        }
        [MenuItem("Tools/YUCP/Package Exporter")]
        public static void ShowWindow()
        {
            var window = GetWindow<YUCPPackageExporterWindow>();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DevTools.png");
            window.titleContent = new GUIContent("YUCP Package Exporter", icon);
            window.minSize = new Vector2(800, 700); // Increased default window size
            window.Show();
        }

    }
}
