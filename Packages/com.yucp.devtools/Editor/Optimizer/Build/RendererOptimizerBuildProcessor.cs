using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;
using YUCP.DevTools.Editor.Optimizer.Passes;

namespace YUCP.DevTools.Editor.Optimizer.Build
{
    /// <summary>
    /// Applies the optimizer at build time for objects carrying a <see cref="RendererOptimizerMarker"/>.
    /// Avatars: runs shader conversion in place before d4rk's merge (which the existing
    /// AvatarOptimizerPluginData processor configures at a later callback). Worlds: combines static meshes
    /// during the player build only (never during play mode).
    /// </summary>
    public class RendererOptimizerBuildProcessor : IVRCSDKPreprocessAvatarCallback, IProcessSceneWithReport
    {
        // Before AvatarOptimizerPluginProcessor (int.MaxValue - 200) so shader conversion precedes d4rk.
        public int callbackOrder => int.MaxValue - 250;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var marker = avatarRoot.GetComponent<RendererOptimizerMarker>();
            if (marker == null || !marker.applyAtBuild)
                return true;

            try
            {
                ApplyInPlace(avatarRoot, OptimizerMode.Avatar, marker);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Optimizer] Build-time optimization failed for avatar '{avatarRoot.name}': {ex.Message}");
            }
            return true;
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Only act during an actual player/world build, never when entering play mode.
            if (!BuildPipeline.isBuildingPlayer)
                return;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var marker in root.GetComponentsInChildren<RendererOptimizerMarker>(true))
                {
                    if (!marker.applyAtBuild)
                        continue;

                    // Avatars are handled by the avatar preprocess callback.
                    if (marker.GetComponentInChildren<VRCAvatarDescriptor>(true) != null)
                        continue;

                    try
                    {
                        ApplyInPlace(marker.gameObject, OptimizerMode.World, marker);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[YUCP Optimizer] Build-time world optimization failed for '{marker.gameObject.name}': {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Runs the relevant optimizer passes directly on <paramref name="root"/> (no copy — build callbacks
        /// already operate on a temporary build copy). Exposed for testing.
        /// </summary>
        public static void ApplyInPlace(GameObject root, OptimizerMode mode, RendererOptimizerMarker marker)
        {
            var ctx = new OptimizationContext { Root = root, OriginalRoot = root, Mode = mode };
            ctx.Options.convertShaders = marker.convertShaders;
            ctx.Options.targetShaderName = marker.targetShaderName;
            ctx.Options.atlasMaxSize = marker.atlasMaxSize;
            ctx.Options.atlasPadding = marker.atlasPadding;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (!(renderer is ParticleSystemRenderer))
                    ctx.Renderers.Add(renderer);

            if (ctx.Options.convertShaders && !string.IsNullOrEmpty(ctx.Options.targetShaderName))
                ctx.TargetShader = Shader.Find(ctx.Options.targetShaderName);

            var passes = new List<IOptimizationPass> { new ShaderConversionPass() };
            if (mode == OptimizerMode.World && marker.combineStaticMeshes)
                passes.Add(new CustomStaticMergePass());

            foreach (var pass in passes)
                if (pass.CanRun(ctx))
                    pass.Execute(ctx, (f, m) => { });

            foreach (var warning in ctx.Warnings)
                Debug.LogWarning($"[YUCP Optimizer] {warning}");
        }
    }
}
