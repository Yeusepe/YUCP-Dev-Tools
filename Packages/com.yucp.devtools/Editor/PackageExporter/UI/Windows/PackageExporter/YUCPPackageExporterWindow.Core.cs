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
        private void SmoothScrollTo(ScrollView scrollView, VisualElement element)
        {
            if (scrollView == null || element == null) return;


            if (float.IsNaN(element.layout.height) || element.layout.height < 1)
            {
                scrollView.schedule.Execute(() => SmoothScrollTo(scrollView, element)).ExecuteLater(50);
                return;
            }



            float elementY = element.worldBound.y;
            float contentY = scrollView.contentContainer.worldBound.y;






            float relativeY = element.worldBound.y - scrollView.worldBound.y + scrollView.verticalScroller.value;

            float viewportHeight = scrollView.layout.height;
            float elementHeight = element.layout.height;

            float targetOffset = Mathf.Max(0, relativeY - (viewportHeight / 2) + (elementHeight / 2));
            float startOffset = scrollView.verticalScroller.value;


            if (float.IsNaN(targetOffset)) targetOffset = 0;

            float duration = 0.4f;
            double startTime = EditorApplication.timeSinceStartup;

            IVisualElementScheduledItem anim = null;
            anim = scrollView.schedule.Execute(() =>
            {
                if (scrollView == null) return;

                float t = (float)(EditorApplication.timeSinceStartup - startTime) / duration;
                if (t >= 1f)
                {
                    scrollView.verticalScroller.value = targetOffset;
                    return;
                }

                float smoothT = Mathf.SmoothStep(0, 1, t);
                scrollView.verticalScroller.value = Mathf.Lerp(startOffset, targetOffset, smoothT);

                anim.ExecuteLater(10);
            });
        }

        private void OnEnable()
        {
            DependencyScanner.RepositoriesChanged += UpdateProfileDetails;

            global::YUCP.Motion.Motion.Initialize();

            LoadProfiles();
            LoadProjectFolders();
            LoadResources();
            LoadTagColors();


            MigrateToUnifiedOrder();
            LoadUnifiedOrder();


            EditorApplication.delayCall += RefreshDependenciesOnDomainReload;


            EditorApplication.update -= UpdateGapAnimations;
            EditorApplication.update += UpdateGapAnimations;
        }


        private Dictionary<string, Color> _tagColors = new Dictionary<string, Color>();
        private const string TagColorsPrefKey = "YUCP_GlobalTagColors";

        private void LoadTagColors()
        {
            _tagColors.Clear();
            string rawTagColors = EditorPrefs.GetString(TagColorsPrefKey, "");
            if (!string.IsNullOrEmpty(rawTagColors))
            {
                var entries = rawTagColors.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var entry in entries)
                {
                    var parts = entry.Split(':');
                    if (parts.Length == 2 && ColorUtility.TryParseHtmlString("#" + parts[1], out var color))
                    {
                        _tagColors[parts[0]] = color;
                    }
                }
            }


            if (!_tagColors.ContainsKey("Production")) _tagColors["Production"] = new Color(0.29f, 0.87f, 0.5f);
            if (!_tagColors.ContainsKey("Beta")) _tagColors["Beta"] = new Color(0.98f, 0.57f, 0.24f);
            if (!_tagColors.ContainsKey("Archived")) _tagColors["Archived"] = new Color(0.61f, 0.64f, 0.69f);
            if (!_tagColors.ContainsKey("Active")) _tagColors["Active"] = new Color(0.21f, 0.75f, 0.69f);
            if (!_tagColors.ContainsKey("Deprecated")) _tagColors["Deprecated"] = new Color(0.97f, 0.44f, 0.44f);
            if (!_tagColors.ContainsKey("Experimental")) _tagColors["Experimental"] = new Color(0.75f, 0.52f, 0.99f);
            if (!_tagColors.ContainsKey("Stable")) _tagColors["Stable"] = new Color(0.38f, 0.65f, 0.98f);
            if (!_tagColors.ContainsKey("WIP")) _tagColors["WIP"] = new Color(0.98f, 0.75f, 0.14f);
        }

        private void SaveTagColors()
        {
            var colorList = _tagColors.Select(kvp => $"{kvp.Key}:{ColorUtility.ToHtmlStringRGB(kvp.Value)}");
            EditorPrefs.SetString(TagColorsPrefKey, string.Join("|", colorList));
        }

        public Color GetTagColor(string tag)
        {
            if (tag.StartsWith("-")) tag = tag.Substring(1); // excluded-tag search chips
            return _tagColors.TryGetValue(tag, out var color) ? color : new Color(0.21f, 0.75f, 0.69f);
        }

        public void SetTagColor(string tag, Color color)
        {
            _tagColors[tag] = color;
            SaveTagColors();
        }

        public void DeleteGlobalTag(string tag)
        {

            string rawGlobalTags = EditorPrefs.GetString("YUCP_GlobalCustomTags", "");
            var globalTags = !string.IsNullOrEmpty(rawGlobalTags)
                ? rawGlobalTags.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList()
                : new List<string>();

            if (globalTags.Contains(tag))
            {
                globalTags.Remove(tag);
                EditorPrefs.SetString("YUCP_GlobalCustomTags", string.Join("|", globalTags));
            }


            if (_tagColors.ContainsKey(tag))
            {
                _tagColors.Remove(tag);
                SaveTagColors();
            }

            UpdateProfileList();
            UpdateProfileDetails();
        }

        private void ShowTagContextMenu(string tag, VisualElement modalTarget)
        {
            TokenizedTagInput.ShowTagMenu(rootVisualElement, modalTarget, tag, GetTagColor, ApplyTagColor, DeleteGlobalTag);
        }

        /// <summary>Recolors a tag everywhere it is shown, without rebuilding the details panel.</summary>
        private void ApplyTagColor(string tag, Color color)
        {
            SetTagColor(tag, color);
            UpdateProfileList(); // also refreshes the search chips
            rootVisualElement.Query<TokenizedTagInput>().ForEach(input => input.RefreshChips());
        }

        private void OnDisable()
        {
            DependencyScanner.RepositoriesChanged -= UpdateProfileDetails;
            EditorApplication.delayCall -= RefreshDependenciesOnDomainReload;
            EditorApplication.update -= UpdateGapAnimations;
        }

        private void RefreshDependenciesOnDomainReload()
        {

            if (selectedProfile != null && selectedProfile.dependencies.Count > 0)
            {

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
        [MenuItem("Tools/YUCP/Package Exporter/Open", false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<YUCPPackageExporterWindow>();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DevTools.png");
            window.titleContent = new GUIContent("YUCP Package Exporter", icon);
            window.minSize = new Vector2(800, 700);
            window.Show();
            window.Focus();
        }

        public static void OpenListingWebsiteWithFocusCallback()
        {
            WebsiteFocusCallbackService.OpenUrlWithFocusCallback(
                DevToolsListingWebsiteUrl,
                EditorWindow.focusedWindow,
                ShowWindow);
        }

    }
}
