using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.DevTools.Editor.Optimizer.ShaderConversion;
using YUCP.DevTools.Editor.Optimizer.Util;

namespace YUCP.DevTools.Editor.Optimizer.UI
{
    /// <summary>
    /// Front-end for the YUCP Renderer Optimizer. Lets the user pick a hierarchy (avatar or world),
    /// choose options, and run the optimization pipeline, then reports before/after footprint and warnings.
    /// </summary>
    public class RendererOptimizerWindow : EditorWindow
    {
        private const string DesignSystemUss = "Packages/com.yucp.devtools/Editor/Styles/YucpDesignSystem.uss";
        private const string IconPath = "Packages/com.yucp.devtools/Resources/DevTools.png";

        private GameObject _target;
        private readonly List<Transform> _exclusions = new List<Transform>();

        private Label _modeBadge;
        private VisualElement _exclusionList;
        private VisualElement _resultsHost;

        [MenuItem("Tools/YUCP/Optimizer")]
        public static void ShowWindow()
        {
            var window = GetWindow<RendererOptimizerWindow>();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            window.titleContent = new GUIContent("YUCP Optimizer", icon);
            window.minSize = new Vector2(420, 560);
            window.Show();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("yucp-window");

            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(DesignSystemUss);
            if (stylesheet != null)
                root.styleSheets.Add(stylesheet);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 14;
            scroll.style.paddingRight = 14;
            scroll.style.paddingTop = 12;
            scroll.style.paddingBottom = 12;
            root.Add(scroll);

            var title = new Label("Renderer Optimizer");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 16;
            title.style.marginBottom = 8;
            scroll.Add(title);

            scroll.Add(new HelpBox(
                "Combines meshes, materials and textures to minimize footprint. Avatars are optimized with " +
                "d4rkAvatarOptimizer (exact appearance). World/static optimization and shader conversion arrive " +
                "in later phases. The original is never modified — an optimized copy is created.",
                HelpBoxMessageType.Info));

            // ---- Target ----
            var targetField = new ObjectField("Target") { objectType = typeof(GameObject), allowSceneObjects = true };
            targetField.style.marginTop = 10;
            targetField.RegisterValueChangedCallback(evt =>
            {
                _target = evt.newValue as GameObject;
                RefreshMode();
            });
            scroll.Add(targetField);

            _modeBadge = new Label("Select a GameObject to begin");
            _modeBadge.style.marginTop = 4;
            _modeBadge.style.marginBottom = 8;
            _modeBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            scroll.Add(_modeBadge);

            var options = RendererOptimizerSettings.Instance.Options;

            // ---- Output ----
            var outputField = new EnumField("Output", options.outputMode);
            outputField.RegisterValueChangedCallback(evt =>
            {
                options.outputMode = (OptimizerOutputMode)evt.newValue;
                RendererOptimizerSettings.Instance.Save();
            });
            scroll.Add(outputField);

            // ---- Shader conversion ----
            var targetProfiles = ShaderProfileRegistry.TargetProfiles();
            var choices = targetProfiles.Select(p => p.DisplayName).ToList();
            if (choices.Count > 0 && string.IsNullOrEmpty(options.targetShaderName))
                options.targetShaderName = targetProfiles[0].TargetShaderName;

            var convertToggle = new Toggle("Convert all materials to one shader") { value = options.convertShaders };
            convertToggle.style.marginTop = 8;
            scroll.Add(convertToggle);

            int currentIndex = System.Math.Max(0, targetProfiles.FindIndex(p => p.TargetShaderName == options.targetShaderName));
            var shaderDropdown = new DropdownField("Target shader", choices, choices.Count > 0 ? currentIndex : -1);
            shaderDropdown.SetEnabled(options.convertShaders);
            shaderDropdown.RegisterValueChangedCallback(evt =>
            {
                var profile = targetProfiles.FirstOrDefault(p => p.DisplayName == evt.newValue);
                if (profile != null)
                {
                    options.targetShaderName = profile.TargetShaderName;
                    RendererOptimizerSettings.Instance.Save();
                }
            });
            scroll.Add(shaderDropdown);

            convertToggle.RegisterValueChangedCallback(evt =>
            {
                options.convertShaders = evt.newValue;
                shaderDropdown.SetEnabled(evt.newValue);
                RendererOptimizerSettings.Instance.Save();
            });

            scroll.Add(new HelpBox(
                "Materials are re-targeted onto the chosen shader using property maps. Any material with " +
                "textures the target can't represent is kept unchanged (and reported), so appearance is preserved.",
                HelpBoxMessageType.None));

            // ---- Avatar (d4rk) options ----
            var avatarFoldout = new Foldout { text = "Avatar merge options (d4rkAvatarOptimizer)", value = true };
            avatarFoldout.style.marginTop = 8;
            scroll.Add(avatarFoldout);

            AddToggle(avatarFoldout, "Auto settings (let d4rk decide)", options.useAutoSettings, v => options.useAutoSettings = v);
            AddToggle(avatarFoldout, "Merge skinned meshes", options.mergeSkinnedMeshes, v => options.mergeSkinnedMeshes = v);
            AddToggle(avatarFoldout, "Merge different-property materials (Windows)", options.mergeDifferentPropertyMaterials, v => options.mergeDifferentPropertyMaterials = v);
            AddToggle(avatarFoldout, "Merge same-dimension textures → array", options.mergeSameDimensionTextures, v => options.mergeSameDimensionTextures = v);
            AddToggle(avatarFoldout, "Include _MainTex in texture merge", options.mergeMainTex, v => options.mergeMainTex = v);
            AddToggle(avatarFoldout, "Write properties as static values", options.writePropertiesAsStaticValues, v => options.writePropertiesAsStaticValues = v);

            // ---- Exclusions ----
            var exclusionFoldout = new Foldout { text = "Exclusions", value = false };
            exclusionFoldout.style.marginTop = 8;
            scroll.Add(exclusionFoldout);

            _exclusionList = new VisualElement();
            exclusionFoldout.Add(_exclusionList);
            RebuildExclusions();

            var addExclusion = new Button(() => { _exclusions.Add(null); RebuildExclusions(); }) { text = "+ Add exclusion" };
            exclusionFoldout.Add(addExclusion);

            // ---- Run ----
            var runButton = new Button(RunOptimize) { text = "Optimize" };
            runButton.style.height = 36;
            runButton.style.marginTop = 14;
            runButton.AddToClassList("yucp-button-primary");
            scroll.Add(runButton);

            // ---- Results ----
            _resultsHost = new VisualElement();
            _resultsHost.style.marginTop = 12;
            scroll.Add(_resultsHost);
        }

        private void RefreshMode()
        {
            if (_target == null)
            {
                _modeBadge.text = "Select a GameObject to begin";
                return;
            }

            bool isAvatar = _target.GetComponentInChildren<VRCAvatarDescriptor>(true) != null;

            if (isAvatar)
                _modeBadge.text = "Detected: Avatar — will use d4rkAvatarOptimizer";
            else
                _modeBadge.text = "Detected: World/Static — custom pipeline (pending later phase)";
        }

        private void RebuildExclusions()
        {
            _exclusionList.Clear();
            for (int i = 0; i < _exclusions.Count; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;

                var field = new ObjectField { objectType = typeof(Transform), allowSceneObjects = true, value = _exclusions[i] };
                field.style.flexGrow = 1;
                field.RegisterValueChangedCallback(evt => _exclusions[index] = evt.newValue as Transform);
                row.Add(field);

                var remove = new Button(() => { _exclusions.RemoveAt(index); RebuildExclusions(); }) { text = "✕" };
                row.Add(remove);

                _exclusionList.Add(row);
            }
        }

        private void RunOptimize()
        {
            if (_target == null)
            {
                EditorUtility.DisplayDialog("YUCP Optimizer", "Select a target GameObject first.", "OK");
                return;
            }

            var options = RendererOptimizerSettings.Instance.Options;
            RendererOptimizerSettings.Instance.Save();

            var ctx = new OptimizationContext { Root = _target, Options = options };
            foreach (var t in _exclusions)
                if (t != null)
                    ctx.Exclusions.Add(t);

            if (options.convertShaders && !string.IsNullOrEmpty(options.targetShaderName))
                ctx.TargetShader = Shader.Find(options.targetShaderName);

            var passes = OptimizerPipeline.BuildPasses(ctx);
            var result = OptimizerRunner.Run(ctx, passes);

            ShowResults(result);
        }

        private void ShowResults(OptimizerResult result)
        {
            _resultsHost.Clear();

            if (!result.Success)
            {
                _resultsHost.Add(new HelpBox($"Optimization failed: {result.Error}", HelpBoxMessageType.Error));
                return;
            }

            var ctx = result.Context;

            var header = new Label("Results");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 4;
            _resultsHost.Add(header);

            _resultsHost.Add(new Label($"Before:  {ctx.Before}"));
            _resultsHost.Add(new Label($"After:   {ctx.After}"));

            long savedBytes = ctx.Before.textureBytes - ctx.After.textureBytes;
            int savedRenderers = ctx.Before.rendererCount - ctx.After.rendererCount;
            int savedMaterials = ctx.Before.materialCount - ctx.After.materialCount;
            var delta = new Label($"Saved:   {savedRenderers} renderers, {savedMaterials} materials, " +
                                  $"~{(savedBytes / (1024.0 * 1024.0)):0.0} MB textures");
            delta.style.unityFontStyleAndWeight = FontStyle.Bold;
            delta.style.marginTop = 4;
            _resultsHost.Add(delta);

            foreach (var warning in ctx.Warnings)
                _resultsHost.Add(new HelpBox(warning, HelpBoxMessageType.Warning));
        }

        private static void AddToggle(VisualElement parent, string label, bool initial, System.Action<bool> setter)
        {
            var toggle = new Toggle(label) { value = initial };
            toggle.RegisterValueChangedCallback(evt =>
            {
                setter(evt.newValue);
                RendererOptimizerSettings.Instance.Save();
            });
            parent.Add(toggle);
        }
    }
}
