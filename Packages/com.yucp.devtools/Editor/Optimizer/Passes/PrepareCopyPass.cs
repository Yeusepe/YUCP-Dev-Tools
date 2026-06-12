using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using YUCP.DevTools.Editor.Optimizer.Util;

namespace YUCP.DevTools.Editor.Optimizer.Passes
{
    /// <summary>
    /// Duplicates the selected hierarchy so the original is never mutated, re-points the context at the
    /// copy, re-gathers renderers, and resolves exclusions onto the copy. Mirrors d4rkAvatarOptimizer's
    /// "Create Optimized Copy" instantiate flow.
    /// </summary>
    public class PrepareCopyPass : IOptimizationPass
    {
        public string Name => "Preparing working copy";

        public bool CanRun(OptimizationContext ctx) => ctx.OriginalRoot != null;

        public void Execute(OptimizationContext ctx, Action<float, string> report)
        {
            var original = ctx.OriginalRoot;
            var originalTransform = original.transform;

            report(0.2f, "Duplicating hierarchy...");
            var copy = UnityEngine.Object.Instantiate(original);
            copy.name = original.name + " (Optimized)";

            // Keep the copy in the same scene and hierarchy slot as the original.
            if (originalTransform.parent != null)
            {
                copy.transform.SetParent(originalTransform.parent, false);
                copy.transform.localPosition = originalTransform.localPosition;
                copy.transform.localRotation = originalTransform.localRotation;
                copy.transform.localScale = originalTransform.localScale;
            }
            else if (original.scene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(copy, original.scene);
            }

            Undo.RegisterCreatedObjectUndo(copy, "YUCP Optimize");

            ctx.Root = copy;

            report(0.7f, "Re-binding renderers...");
            ctx.Renderers.Clear();
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;
                ctx.Renderers.Add(renderer);
            }

            // Resolve exclusion paths against the copy.
            ctx.Exclusions.Clear();
            foreach (var path in ctx.ExclusionPaths)
            {
                var t = HierarchyUtil.FindByPath(copy.transform, path);
                if (t != null)
                    ctx.Exclusions.Add(t);
            }

            report(1f, "Working copy ready");
        }
    }
}
