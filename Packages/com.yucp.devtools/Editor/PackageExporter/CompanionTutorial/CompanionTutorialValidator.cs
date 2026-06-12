using System.Collections.Generic;
using System.Globalization;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Edit/export-time validation for companion tutorials. Findings are advisory — a tutorial never
    /// blocks export; problems are surfaced inline in the authoring UI and appended to the export
    /// warning message so creators don't ship a broken walkthrough.
    /// </summary>
    public static class CompanionTutorialValidator
    {
        public enum Severity { Info, Warning, Error }

        public struct Finding
        {
            public int StepIndex;   // -1 for tutorial-level findings
            public Severity Severity;
            public string Message;

            public Finding(int stepIndex, Severity severity, string message)
            {
                StepIndex = stepIndex;
                Severity = severity;
                Message = message;
            }
        }

        public static List<Finding> Validate(CompanionTutorialDefinition tutorial)
        {
            var findings = new List<Finding>();
            if (tutorial == null || !tutorial.enabled)
                return findings;

            if (tutorial.steps == null || tutorial.steps.Count == 0)
            {
                findings.Add(new Finding(-1, Severity.Warning, "Tutorial is enabled but has no steps."));
                return findings;
            }

            for (int i = 0; i < tutorial.steps.Count; i++)
            {
                var step = tutorial.steps[i];
                if (step == null)
                {
                    findings.Add(new Finding(i, Severity.Error, "Step is null."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(step.title))
                    findings.Add(new Finding(i, Severity.Warning, "Step has no title."));
                if (string.IsNullOrWhiteSpace(step.text))
                    findings.Add(new Finding(i, Severity.Warning, "Step has no body text."));

                ValidateTarget(i, step, findings);
                ValidateWaitFor(i, step, findings);

                if (!System.Array.Exists(CompanionTutorialTokens.MouseActions, m => m == step.mouseAction))
                    findings.Add(new Finding(i, Severity.Warning, $"Unknown mouse action '{step.mouseAction}' (treated as 'none')."));

                string overlayMode = string.IsNullOrWhiteSpace(step.overlayMode) ? "intrusive" : step.overlayMode;
                if (!System.Array.Exists(CompanionTutorialTokens.OverlayModes, o => o == overlayMode))
                    findings.Add(new Finding(i, Severity.Warning, $"Unknown overlay mode '{step.overlayMode}' (treated as 'intrusive')."));

                // Partial manual rect override (only width OR height filled in).
                bool hasW = step.targetRect.z > 1, hasH = step.targetRect.w > 1;
                if (hasW != hasH)
                    findings.Add(new Finding(i, Severity.Info, "targetRect override is partial; both width and height are required to take effect."));
            }

            return findings;
        }

        private static void ValidateTarget(int index, CompanionTutorialStep step, List<Finding> findings)
        {
            // A manual rect override makes the target string optional.
            if (step.targetRect.z > 1 && step.targetRect.w > 1)
                return;

            string target = (step.target ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(target))
            {
                findings.Add(new Finding(index, Severity.Info, "No target set; a centered card will be shown."));
                return;
            }

            CompanionTutorialTokens.SplitPrefix(target, out string prefix, out string selector);

            bool hasColon = target.IndexOf(':') >= 0;
            if (hasColon)
            {
                if (CompanionTutorialTokens.HasKnownPrefix(prefix))
                {
                    if (string.IsNullOrEmpty(selector))
                        findings.Add(new Finding(index, Severity.Error, $"Target '{prefix}:' is missing a selector."));
                }
                else
                {
                    findings.Add(new Finding(index, Severity.Warning,
                        $"Unrecognized target prefix '{prefix}:' — the step will fall back to a centered card."));
                }
            }
            else if (!CompanionTutorialTokens.IsKnownBareTarget(target))
            {
                findings.Add(new Finding(index, Severity.Warning,
                    $"Unrecognized target '{target}' — the step will fall back to a centered card."));
            }
        }

        private static void ValidateWaitFor(int index, CompanionTutorialStep step, List<Finding> findings)
        {
            string wait = (step.waitFor ?? "manual").Trim();
            if (string.IsNullOrEmpty(wait))
                return;

            CompanionTutorialTokens.SplitPrefix(wait, out string verb, out string arg);
            bool hasColon = wait.IndexOf(':') >= 0;

            if (!CompanionTutorialTokens.IsKnownWaitVerb(verb))
            {
                findings.Add(new Finding(index, Severity.Warning,
                    $"Unknown wait condition '{verb}' — the step will require manual advance."));
                return;
            }

            switch (verb.ToLowerInvariant())
            {
                case "delay":
                    if (!hasColon || !double.TryParse(arg, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                        findings.Add(new Finding(index, Severity.Error, "'delay:' requires a number of seconds (e.g. delay:2)."));
                    break;
                case "assetexists":
                    if (!hasColon || string.IsNullOrEmpty(arg))
                        findings.Add(new Finding(index, Severity.Error, "'assetExists:' requires an asset path."));
                    break;
                case "packageinstalled":
                    if (!hasColon || string.IsNullOrEmpty(arg))
                        findings.Add(new Finding(index, Severity.Error, "'packageInstalled:' requires a package name."));
                    break;
                case "componentexists":
                    if (!hasColon || string.IsNullOrEmpty(arg))
                        findings.Add(new Finding(index, Severity.Error, "'componentExists:' requires a type name."));
                    break;
                case "transformmoved":
                    // selector is optional (defaults to the active selection)
                    break;
            }
        }

        /// <summary>Renders findings as a single human-readable block for the export warning message.</summary>
        public static string Summarize(List<Finding> findings)
        {
            if (findings == null || findings.Count == 0)
                return null;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Companion tutorial has issues:");
            foreach (var f in findings)
            {
                string where = f.StepIndex >= 0 ? $"Step {f.StepIndex + 1}" : "Tutorial";
                sb.AppendLine($"  • [{f.Severity}] {where}: {f.Message}");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
