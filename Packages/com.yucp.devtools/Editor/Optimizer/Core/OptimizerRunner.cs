using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YUCP.Components.Editor.UI;

namespace YUCP.DevTools.Editor.Optimizer
{
    /// <summary>Outcome of an optimizer run.</summary>
    public struct OptimizerResult
    {
        public bool Success;
        public string Error;
        public OptimizationContext Context;
    }

    /// <summary>
    /// Drives an ordered list of <see cref="IOptimizationPass"/> against an <see cref="OptimizationContext"/>,
    /// surfacing progress through <see cref="YUCPProgressWindow"/> and grouping the work as a single undo step.
    /// </summary>
    public static class OptimizerRunner
    {
        public static OptimizerResult Run(OptimizationContext ctx, IReadOnlyList<IOptimizationPass> passes)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (passes == null) throw new ArgumentNullException(nameof(passes));

            YUCPProgressWindow window = null;
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("YUCP Optimize");

            try
            {
                window = YUCPProgressWindow.Create();

                var runnable = passes.Where(p => p.CanRun(ctx)).ToList();
                if (runnable.Count == 0)
                {
                    window.Progress(1f, "Nothing to optimize");
                    return new OptimizerResult { Success = true, Context = ctx };
                }

                for (int i = 0; i < runnable.Count; i++)
                {
                    var pass = runnable[i];
                    int index = i;
                    int total = runnable.Count;

                    window.Progress((float)index / total, $"{pass.Name}...");
                    pass.Execute(ctx, (fraction, message) =>
                    {
                        float overall = (index + Mathf.Clamp01(fraction)) / total;
                        window.Progress(overall, string.IsNullOrEmpty(message) ? pass.Name : message);
                    });
                }

                window.Progress(1f, "Optimization complete");
                return new OptimizerResult { Success = true, Context = ctx };
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return new OptimizerResult { Success = false, Error = ex.Message, Context = ctx };
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
                if (window != null)
                    window.CloseWindow();
            }
        }
    }
}
