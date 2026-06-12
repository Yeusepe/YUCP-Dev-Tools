using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.Optimizer
{
    /// <summary>
    /// Mutable state threaded through the optimization passes for a single run.
    /// Passes read and mutate this object in order; <see cref="OptimizerRunner"/> owns its lifetime.
    /// </summary>
    public class OptimizationContext
    {
        /// <summary>The hierarchy currently being operated on. Re-pointed to the working copy by the prepare pass.</summary>
        public GameObject Root;

        /// <summary>The original, user-selected hierarchy. Never mutated; disabled/kept for comparison.</summary>
        public GameObject OriginalRoot;

        /// <summary>Auto-detected by <see cref="Passes.SelectionPass"/>.</summary>
        public OptimizerMode Mode;

        /// <summary>Renderers under <see cref="Root"/> that are in scope for optimization.</summary>
        public readonly List<Renderer> Renderers = new List<Renderer>();

        /// <summary>Transforms (under the current <see cref="Root"/>) excluded from optimization.</summary>
        public readonly List<Transform> Exclusions = new List<Transform>();

        /// <summary>Root-relative paths of excluded transforms, so exclusions survive the clone step.</summary>
        public readonly List<string> ExclusionPaths = new List<string>();

        public OptimizerOptions Options = new OptimizerOptions();

        /// <summary>Resolved from <see cref="OptimizerOptions.targetShaderName"/> when shader conversion is enabled.</summary>
        public Shader TargetShader;

        public OptimizerStats Before;
        public OptimizerStats After;

        /// <summary>Non-fatal issues surfaced to the user (e.g. materials skipped during shader conversion).</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>Assets generated during the run (meshes/materials/textures) that the output stage may persist.</summary>
        public readonly List<Object> GeneratedAssets = new List<Object>();

        public void AddWarning(string message)
        {
            Warnings.Add(message);
            Debug.LogWarning($"[YUCP Optimizer] {message}");
        }
    }
}
