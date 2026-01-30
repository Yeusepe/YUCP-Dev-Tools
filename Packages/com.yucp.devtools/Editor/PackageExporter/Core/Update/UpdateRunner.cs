using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class UpdateRunner
    {
        private static bool _isRunning;

        [Serializable]
        private class UpdateMetadata
        {
            public string packageName;
            public string version;
            public UpdateStepList updateSteps;
        }

        public static bool QueueRunFromMetadata(string metadataPath, string reason, IEnumerable<string> detectedConflicts)
        {
            if (_isRunning)
                return false;

            if (string.IsNullOrEmpty(metadataPath))
            {
                metadataPath = FindDefaultMetadataPath();
            }

            if (string.IsNullOrEmpty(metadataPath) || !File.Exists(metadataPath))
                return false;

            UpdateMetadata metadata;
            try
            {
                string json = File.ReadAllText(metadataPath);
                metadata = JsonUtility.FromJson<UpdateMetadata>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP UpdateRunner] Failed to parse metadata: {ex.Message}");
                return false;
            }

            if (metadata?.updateSteps == null || !metadata.updateSteps.enabled || metadata.updateSteps.steps == null || metadata.updateSteps.steps.Count == 0)
            {
                WarnNoSteps(metadata, reason);
                return false;
            }

            var enabledSteps = metadata.updateSteps.steps.Where(s => s != null && s.enabled).ToList();
            if (enabledSteps.Count == 0)
            {
                WarnNoSteps(metadata, reason);
                return false;
            }

            string summary = BuildStepSummary(enabledSteps);
            int conflictCount = detectedConflicts != null ? detectedConflicts.Count() : 0;
            string title = "Custom Update Steps Detected";
            string body = $"{reason}\n\nPackage: {metadata.packageName}\nVersion: {metadata.version}\n";
            if (conflictCount > 0)
            {
                body += $"Conflicting files detected: {conflictCount}\n";
            }
            body += "\nA safety backup will be created before any changes.\n\n" +
                    "The following steps are configured and will modify your project:\n\n" +
                    summary +
                    "\n\nNo files will be deleted or overwritten unless the creator explicitly enabled it.\n" +
                    "You can cancel now to keep your project unchanged.";

            int choice = EditorUtility.DisplayDialogComplex(title, body, "Run Steps", "Cancel", "Show Details");
            if (choice == 1)
            {
                Debug.Log("[YUCP UpdateRunner] User canceled update steps.");
                return false;
            }
            if (choice == 2)
            {
                EditorUtility.DisplayDialog(title, BuildFullStepDetails(enabledSteps), "Close");
                bool runAfterDetails = EditorUtility.DisplayDialog(title, "Run update steps now?", "Run Steps", "Cancel");
                if (!runAfterDetails)
                    return false;
            }

            _isRunning = true;
            EditorApplication.delayCall += () =>
            {
                try
                {
                    RunSteps(metadata, enabledSteps);
                }
                finally
                {
                    _isRunning = false;
                    EditorUtility.ClearProgressBar();
                }
            };

            return true;
        }

        private static void RunSteps(UpdateMetadata metadata, List<UpdateStep> steps)
        {
            var ordered = OrderStepsByPhase(steps);
            var txn = new UpdateTransaction();
            bool success = false;

            try
            {
                var backupTargets = BuildBackupTargets(ordered);
                if (!PerformDefaultBackup(txn, backupTargets))
                {
                    throw new Exception("Safety backup failed.");
                }

                for (int i = 0; i < ordered.Count; i++)
                {
                    var step = ordered[i];
                    float progress = ordered.Count > 0 ? (float)i / ordered.Count : 0f;
                    EditorUtility.DisplayProgressBar("Running Update Steps", step.name, progress);

                    if (!ExecuteStep(step, metadata, txn))
                    {
                        throw new Exception($"Step failed or canceled: {step.name}");
                    }
                }

                success = true;
                txn.Commit();
                EditorUtility.DisplayDialog("Update Steps Complete",
                    "All update steps finished successfully.\n\nA rollback backup was saved (Tools/YUCP/Others/Installation/Revert Last Package Update).",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP UpdateRunner] Update failed: {ex.Message}");
                txn.Rollback();
                EditorUtility.DisplayDialog("Update Steps Failed", "An update step failed or was canceled. All changes were rolled back to keep your project safe.", "OK");
            }
            finally
            {
                if (!success)
                {
                    AssetDatabase.Refresh();
                }
            }
        }

        private static List<UpdateStep> OrderStepsByPhase(List<UpdateStep> steps)
        {
            var pre = steps.Where(s => s.phase == UpdatePhase.PreImport).ToList();
            var manual = steps.Where(s => s.phase == UpdatePhase.Manual).ToList();
            var post = steps.Where(s => s.phase == UpdatePhase.PostImport).ToList();
            pre.AddRange(manual);
            pre.AddRange(post);
            return pre;
        }

        private static bool ExecuteStep(UpdateStep step, UpdateMetadata metadata, UpdateTransaction txn)
        {
            if (step == null || !step.enabled) return true;
            if (!string.IsNullOrEmpty(step.packageNameMatch) && metadata != null)
            {
                if (!string.Equals(step.packageNameMatch.Trim(), metadata.packageName ?? "", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (step.IsDestructive && step.requiresUserConfirm)
            {
                string warning = $"This step can delete or overwrite files:\n\n{step.name}\n\n" +
                                 "Are you sure you want to continue?";
                if (!EditorUtility.DisplayDialog("Confirm Destructive Step", warning, "Continue", "Cancel"))
                    return false;
            }

            switch (step.type)
            {
                case UpdateStepType.PromptUser:
                case UpdateStepType.WaitForUser:
                    if (!PromptUser(step))
                        return false;
                    return ValidateStepRules(step);
                case UpdateStepType.ValidatePresence:
                    if (!ValidatePresence(step))
                        return false;
                    return ValidateStepRules(step);
                case UpdateStepType.ValidateVersion:
                    if (!ValidateVersion(step, metadata))
                        return false;
                    return ValidateStepRules(step);
                case UpdateStepType.BackupAssets:
                    return BackupAssets(step, txn);
                case UpdateStepType.MoveAssets:
                    if (!MoveAssets(step, txn))
                        return false;
                    return ValidateStepRules(step);
                case UpdateStepType.CopyAssets:
                    if (!CopyAssets(step, txn))
                        return false;
                    return ValidateStepRules(step);
                case UpdateStepType.DeleteAssets:
                    if (!DeleteAssets(step, txn, allowFolders: false))
                        return false;
                    return ValidateStepRules(step);
                case UpdateStepType.DeleteFolder:
                    if (!DeleteAssets(step, txn, allowFolders: true))
                        return false;
                    return ValidateStepRules(step);
                case UpdateStepType.CreateFolder:
                    if (!CreateFolders(step))
                        return false;
                    return ValidateStepRules(step);
                case UpdateStepType.OpenScene:
                    if (!OpenScene(step))
                        return false;
                    return ValidateStepRules(step);
                case UpdateStepType.RemoveSceneObjects:
                    if (!RemoveSceneObjects(step, txn))
                        return false;
                    return ValidateStepRules(step);
                case UpdateStepType.RefreshAssetDatabase:
                    AssetDatabase.Refresh();
                    return ValidateStepRules(step);
                case UpdateStepType.ReimportAssets:
                    if (!ReimportAssets(step))
                        return false;
                    return ValidateStepRules(step);
                case UpdateStepType.ResolveGuidReferences:
                    Debug.LogWarning("[YUCP UpdateRunner] ResolveGuidReferences requires explicit configuration and is not automated yet.");
                    return ValidateStepRules(step);
                default:
                    Debug.LogWarning($"[YUCP UpdateRunner] Unsupported step type: {step.type}");
                    return ValidateStepRules(step);
            }
        }

        private static bool ValidateStepRules(UpdateStep step)
        {
            if (step.validationRules == null || step.validationRules.Count == 0)
                return true;

            var enabledRules = step.validationRules.Where(r => r != null && r.enabled).ToList();
            if (enabledRules.Count == 0)
                return true;

            while (true)
            {
                var results = enabledRules.Select(EvaluateRule).ToList();
                bool allPass = results.All(r => r.passed || r.rule.severity != UpdateValidationSeverity.Block);
                bool anyPass = results.Any(r => r.passed);
                bool modePass = step.validationMode == UpdateValidationMode.All ? allPass : anyPass;

                if (modePass)
                {
                    var warn = results.Where(r => !r.passed && r.rule.severity != UpdateValidationSeverity.Block).ToList();
                    if (warn.Count > 0)
                    {
                        EditorUtility.DisplayDialog("Update Step Warning", BuildRuleReport(warn, "Warnings"), "OK");
                    }
                    return true;
                }

                string report = BuildRuleReport(results.Where(r => !r.passed).ToList(), "Validation failed");
                int choice = EditorUtility.DisplayDialogComplex("Manual Step Validation Failed",
                    report,
                    "Recheck",
                    "Abort",
                    "Skip Step");

                if (choice == 0)
                    continue;
                if (choice == 2)
                {
                    bool allowSkip = results.Any(r => r.rule.allowSkip);
                    if (!allowSkip)
                    {
                        EditorUtility.DisplayDialog("Skip Not Allowed", "This step cannot be skipped because its validation rules do not allow skipping.", "OK");
                        continue;
                    }
                    return true;
                }
                return false;
            }
        }

        private static (UpdateValidationRule rule, bool passed, int count, string detail) EvaluateRule(UpdateValidationRule rule)
        {
            try
            {
                var selector = ParseSelector(rule.selector);
                var targets = ResolveTargets(rule.scope, selector);
                int count = targets.Count;
                bool passed = true;
                string detail = "";

                switch (rule.condition)
                {
                    case UpdateValidationCondition.Exists:
                        passed = count > 0;
                        detail = $"Found {count} item(s).";
                        break;
                    case UpdateValidationCondition.Missing:
                        passed = count == 0;
                        detail = $"Found {count} item(s).";
                        break;
                    case UpdateValidationCondition.CountEquals:
                        passed = count == rule.expectedCount;
                        detail = $"Found {count} item(s), expected {rule.expectedCount}.";
                        break;
                    case UpdateValidationCondition.CountAtLeast:
                        passed = count >= rule.expectedCount;
                        detail = $"Found {count} item(s), expected at least {rule.expectedCount}.";
                        break;
                    case UpdateValidationCondition.CountAtMost:
                        passed = count <= rule.expectedCount;
                        detail = $"Found {count} item(s), expected at most {rule.expectedCount}.";
                        break;
                    case UpdateValidationCondition.ContentContains:
                        passed = rule.scope == UpdateValidationScope.Files && AnyFileContains(targets, rule.text);
                        detail = passed ? "Content match found." : "No content match found.";
                        break;
                    case UpdateValidationCondition.ContentMatches:
                        passed = rule.scope == UpdateValidationScope.Files && AnyFileMatches(targets, rule.text);
                        detail = passed ? "Regex match found." : "No regex match found.";
                        break;
                }

                return (rule, passed, count, detail);
            }
            catch (Exception ex)
            {
                return (rule, false, 0, $"Validation error: {ex.Message}");
            }
        }

        private static bool AnyFileContains(List<string> files, string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var file in files)
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    string content = File.ReadAllText(file);
                    if (content.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static bool AnyFileMatches(List<string> files, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            Regex regex;
            try { regex = new Regex(pattern, RegexOptions.IgnoreCase); }
            catch { return false; }
            foreach (var file in files)
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    string content = File.ReadAllText(file);
                    if (regex.IsMatch(content))
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static string BuildRuleReport(List<(UpdateValidationRule rule, bool passed, int count, string detail)> results, string title)
        {
            var lines = new List<string> { title };
            foreach (var r in results)
            {
                string name = string.IsNullOrEmpty(r.rule.name) ? "Rule" : r.rule.name;
                lines.Add($"- {name}: {r.detail}");
            }
            return string.Join("\n", lines);
        }

        private class Selector
        {
            public string pathContains;
            public string pathStarts;
            public string nameContains;
            public string type;
            public string tag;
            public string component;
            public string extension;
            public string root;
        }

        private static Selector ParseSelector(string selectorText)
        {
            var selector = new Selector();
            if (string.IsNullOrEmpty(selectorText)) return selector;

            var tokens = selectorText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var parts = token.Split(new[] { ':' }, 2);
                if (parts.Length != 2) continue;
                string key = parts[0].Trim().ToLowerInvariant();
                string value = parts[1].Trim().Trim('"');
                switch (key)
                {
                    case "path":
                    case "pathcontains":
                        selector.pathContains = value;
                        break;
                    case "pathstarts":
                        selector.pathStarts = value;
                        break;
                    case "name":
                    case "namecontains":
                        selector.nameContains = value;
                        break;
                    case "type":
                        selector.type = value;
                        break;
                    case "tag":
                        selector.tag = value;
                        break;
                    case "component":
                        selector.component = value;
                        break;
                    case "ext":
                        selector.extension = value.StartsWith(".") ? value : "." + value;
                        break;
                    case "root":
                        selector.root = value;
                        break;
                }
            }
            return selector;
        }

        private static List<string> ResolveTargets(UpdateValidationScope scope, Selector selector)
        {
            switch (scope)
            {
                case UpdateValidationScope.SceneObjects:
                    return ResolveSceneObjects(selector);
                case UpdateValidationScope.Files:
                    return ResolveFiles(selector);
                default:
                    return ResolveAssets(selector);
            }
        }

        private static List<string> ResolveAssets(Selector selector)
        {
            string filter = "";
            if (!string.IsNullOrEmpty(selector.type))
            {
                filter = $"t:{selector.type}";
            }
            string[] searchFolders = null;
            if (!string.IsNullOrEmpty(selector.root))
            {
                searchFolders = new[] { NormalizeUnityPathForSearch(selector.root) };
            }

            var guids = AssetDatabase.FindAssets(filter, searchFolders);
            var results = new List<string>();
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!PassesAssetFilter(path, selector))
                    continue;
                results.Add(path);
            }
            return results;
        }

        private static bool PassesAssetFilter(string path, Selector selector)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!string.IsNullOrEmpty(selector.pathContains) && path.IndexOf(selector.pathContains, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (!string.IsNullOrEmpty(selector.pathStarts) && !path.StartsWith(selector.pathStarts, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(selector.extension) && !path.EndsWith(selector.extension, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(selector.nameContains))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.IndexOf(selector.nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }
            return true;
        }

        private static List<string> ResolveFiles(Selector selector)
        {
            string root = selector.root;
            if (string.IsNullOrEmpty(root))
                root = "Assets";
            if (!UpdateStepPathUtility.TryGetAbsolutePath(root, out string absRoot))
                return new List<string>();

            var files = Directory.Exists(absRoot)
                ? Directory.GetFiles(absRoot, "*", SearchOption.AllDirectories)
                : new string[0];
            var results = new List<string>();
            foreach (var file in files)
            {
                if (!string.IsNullOrEmpty(selector.extension) && !file.EndsWith(selector.extension, StringComparison.OrdinalIgnoreCase))
                    continue;
                string rel = file;
                if (!string.IsNullOrEmpty(selector.pathContains) && rel.IndexOf(selector.pathContains, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!string.IsNullOrEmpty(selector.nameContains))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name.IndexOf(selector.nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                results.Add(file);
            }
            return results;
        }

        private static List<string> ResolveSceneObjects(Selector selector)
        {
            var results = new List<string>();
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
            Type componentType = ResolveType(selector.component);
            foreach (var obj in allObjects)
            {
                if (!string.IsNullOrEmpty(selector.nameContains) &&
                    obj.name.IndexOf(selector.nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!string.IsNullOrEmpty(selector.tag) && !obj.CompareTag(selector.tag))
                    continue;
                if (componentType != null && obj.GetComponent(componentType) == null)
                    continue;
                results.Add(obj.name);
            }
            return results;
        }

        private static Type ResolveType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            var type = Type.GetType(typeName);
            if (type != null) return type;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) return type;
            }
            return null;
        }

        private static string NormalizeUnityPathForSearch(string root)
        {
            root = root.Replace('\\', '/').Trim();
            if (!root.StartsWith("Assets/") && !root.StartsWith("Packages/") && !string.Equals(root, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                root = "Assets/" + root.TrimStart('/');
            }
            return root;
        }

        private static bool PerformDefaultBackup(UpdateTransaction txn, List<string> targets)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Preparing Update", "Creating safety backup...", 0.05f);
                foreach (var path in targets)
                {
                    if (!UpdateStepPathUtility.TryGetAbsolutePath(path, out string absolute))
                        continue;
                    txn.BackupPath(absolute);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP UpdateRunner] Backup failed: {ex.Message}");
                return false;
            }
        }

        private static List<string> BuildBackupTargets(List<UpdateStep> steps)
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var step in steps)
            {
                if (step == null || !step.enabled)
                    continue;

                if (step.paths != null)
                {
                    foreach (var p in step.paths)
                    {
                        if (string.IsNullOrEmpty(p)) continue;
                        targets.Add(p);
                    }
                }

                if (!string.IsNullOrEmpty(step.scenePath))
                {
                    targets.Add(step.scenePath);
                }
            }

            // Add meta files for any file targets
            var withMeta = new List<string>(targets);
            foreach (var p in targets)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                withMeta.Add(p + ".meta");
            }

            return withMeta;
        }

        private static bool PromptUser(UpdateStep step)
        {
            string message = string.IsNullOrEmpty(step.message) ? "Please complete the manual update step, then click Continue." : step.message;
            return EditorUtility.DisplayDialog("Manual Update Step", message, "Continue", "Cancel");
        }

        private static bool ValidatePresence(UpdateStep step)
        {
            foreach (var path in step.paths)
            {
                if (!UpdateStepPathUtility.TryGetAbsolutePath(path, out string absolute))
                {
                    EditorUtility.DisplayDialog("Update Step Failed", $"Invalid path: {path}", "OK");
                    return false;
                }
                if (!File.Exists(absolute) && !Directory.Exists(absolute))
                {
                    EditorUtility.DisplayDialog("Update Step Failed", $"Missing required asset: {path}", "OK");
                    return false;
                }
            }
            return true;
        }

        private static bool ValidateVersion(UpdateStep step, UpdateMetadata metadata)
        {
            if (metadata == null) return true;
            string version = metadata.version ?? "";
            if (string.IsNullOrEmpty(version) && (!string.IsNullOrEmpty(step.versionMin) || !string.IsNullOrEmpty(step.versionMax)))
            {
                EditorUtility.DisplayDialog("Update Step Failed", "Package version is missing; cannot validate version range.", "OK");
                return false;
            }
            if (!string.IsNullOrEmpty(step.versionMin) && CompareVersions(version, step.versionMin) < 0)
            {
                EditorUtility.DisplayDialog("Update Step Failed", $"Version {version} is below minimum {step.versionMin}.", "OK");
                return false;
            }
            if (!string.IsNullOrEmpty(step.versionMax) && CompareVersions(version, step.versionMax) > 0)
            {
                EditorUtility.DisplayDialog("Update Step Failed", $"Version {version} is above maximum {step.versionMax}.", "OK");
                return false;
            }
            return true;
        }

        private static bool BackupAssets(UpdateStep step, UpdateTransaction txn)
        {
            foreach (var path in step.paths)
            {
                if (!UpdateStepPathUtility.TryGetAbsolutePath(path, out string absolute))
                {
                    Debug.LogWarning($"[YUCP UpdateRunner] Invalid backup path: {path}");
                    continue;
                }
                txn.BackupPath(absolute);
                if (File.Exists(absolute))
                {
                    string meta = absolute + ".meta";
                    if (File.Exists(meta))
                        txn.BackupPath(meta);
                }
            }
            return true;
        }

        private static bool MoveAssets(UpdateStep step, UpdateTransaction txn)
        {
            for (int i = 0; i < step.paths.Count; i += 2)
            {
                if (i + 1 >= step.paths.Count) break;
                string src = step.paths[i];
                string dst = step.paths[i + 1];
                if (!UpdateStepPathUtility.TryGetAbsolutePath(src, out string srcAbs) ||
                    !UpdateStepPathUtility.TryGetAbsolutePath(dst, out string dstAbs))
                {
                    Debug.LogWarning("[YUCP UpdateRunner] Invalid move paths.");
                    continue;
                }
                if (File.Exists(dstAbs) || Directory.Exists(dstAbs))
                {
                    if (!step.allowOverwrite)
                    {
                        EditorUtility.DisplayDialog("Update Step Failed", $"Destination exists: {dst}. Overwrite not allowed.", "OK");
                        return false;
                    }
                    txn.BackupPath(dstAbs);
                    if (File.Exists(dstAbs + ".meta"))
                        txn.BackupPath(dstAbs + ".meta");
                }
                txn.BackupPath(srcAbs);
                if (File.Exists(srcAbs + ".meta"))
                    txn.BackupPath(srcAbs + ".meta");
                Directory.CreateDirectory(Path.GetDirectoryName(dstAbs) ?? "");
                if (File.Exists(srcAbs))
                {
                    if (File.Exists(dstAbs))
                        File.Delete(dstAbs);
                    File.Move(srcAbs, dstAbs);
                    string srcMeta = srcAbs + ".meta";
                    string dstMeta = dstAbs + ".meta";
                    if (File.Exists(srcMeta))
                    {
                        if (File.Exists(dstMeta))
                            File.Delete(dstMeta);
                        File.Move(srcMeta, dstMeta);
                    }
                }
                else if (Directory.Exists(srcAbs))
                {
                    if (Directory.Exists(dstAbs))
                        Directory.Delete(dstAbs, true);
                    Directory.Move(srcAbs, dstAbs);
                }
            }
            return true;
        }

        private static bool CopyAssets(UpdateStep step, UpdateTransaction txn)
        {
            for (int i = 0; i < step.paths.Count; i += 2)
            {
                if (i + 1 >= step.paths.Count) break;
                string src = step.paths[i];
                string dst = step.paths[i + 1];
                if (!UpdateStepPathUtility.TryGetAbsolutePath(src, out string srcAbs) ||
                    !UpdateStepPathUtility.TryGetAbsolutePath(dst, out string dstAbs))
                {
                    Debug.LogWarning("[YUCP UpdateRunner] Invalid copy paths.");
                    continue;
                }
                if ((File.Exists(dstAbs) || Directory.Exists(dstAbs)) && !step.allowOverwrite)
                {
                    EditorUtility.DisplayDialog("Update Step Failed", $"Destination exists: {dst}. Overwrite not allowed.", "OK");
                    return false;
                }
                if (File.Exists(dstAbs) || Directory.Exists(dstAbs))
                {
                    txn.BackupPath(dstAbs);
                    if (File.Exists(dstAbs + ".meta"))
                        txn.BackupPath(dstAbs + ".meta");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dstAbs) ?? "");
                if (File.Exists(srcAbs))
                    File.Copy(srcAbs, dstAbs, true);
                else if (Directory.Exists(srcAbs))
                    CopyDirectory(srcAbs, dstAbs);

                if (File.Exists(srcAbs))
                {
                    string srcMeta = srcAbs + ".meta";
                    string dstMeta = dstAbs + ".meta";
                    if (File.Exists(srcMeta))
                    {
                        File.Copy(srcMeta, dstMeta, true);
                    }
                }
            }
            return true;
        }

        private static bool DeleteAssets(UpdateStep step, UpdateTransaction txn, bool allowFolders)
        {
            if (!step.allowDelete)
            {
                Debug.LogWarning($"[YUCP UpdateRunner] Delete step '{step.name}' skipped (allowDelete is false).");
                return true;
            }
            foreach (var path in step.paths)
            {
                if (!UpdateStepPathUtility.TryGetAbsolutePath(path, out string abs))
                {
                    Debug.LogWarning($"[YUCP UpdateRunner] Invalid delete path: {path}");
                    continue;
                }
                if (Directory.Exists(abs))
                {
                    if (!allowFolders)
                    {
                        Debug.LogWarning($"[YUCP UpdateRunner] Skipping folder delete (not allowed): {path}");
                        continue;
                    }
                    txn.BackupPath(abs);
                    Directory.Delete(abs, true);
                    string meta = abs + ".meta";
                    if (File.Exists(meta))
                    {
                        txn.BackupPath(meta);
                        File.Delete(meta);
                    }
                }
                else if (File.Exists(abs))
                {
                    txn.BackupPath(abs);
                    File.Delete(abs);
                    string meta = abs + ".meta";
                    if (File.Exists(meta))
                    {
                        txn.BackupPath(meta);
                        File.Delete(meta);
                    }
                }
            }
            return true;
        }

        private static bool CreateFolders(UpdateStep step)
        {
            foreach (var path in step.paths)
            {
                if (!UpdateStepPathUtility.TryGetAbsolutePath(path, out string abs))
                    continue;
                Directory.CreateDirectory(abs);
            }
            return true;
        }

        private static bool OpenScene(UpdateStep step)
        {
            if (string.IsNullOrEmpty(step.scenePath))
                return true;
            string scenePath = step.scenePath.Replace('\\', '/');
            if (!scenePath.StartsWith("Assets/") && !scenePath.StartsWith("Packages/"))
            {
                scenePath = "Assets/" + scenePath.TrimStart('/');
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            if (File.Exists(scenePath))
            {
                EditorSceneManager.OpenScene(scenePath);
                return true;
            }

            EditorUtility.DisplayDialog("Update Step Failed", $"Scene not found: {scenePath}", "OK");
            return false;
        }

        private static bool RemoveSceneObjects(UpdateStep step, UpdateTransaction txn)
        {
            if (string.IsNullOrEmpty(step.query))
            {
                EditorUtility.DisplayDialog("Update Step Failed", "RemoveSceneObjects requires a query (name or tag).", "OK");
                return false;
            }

            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return false;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string scenePath = EditorSceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(scenePath))
            {
                string sceneAbs = Path.Combine(projectRoot, scenePath.Replace('/', Path.DirectorySeparatorChar));
                txn.BackupPath(sceneAbs);
            }

            string query = step.query.Trim();
            bool useTag = query.StartsWith("tag:", StringComparison.OrdinalIgnoreCase);
            string token = useTag ? query.Substring(4).Trim() : query;

            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
            int removed = 0;
            foreach (var obj in allObjects)
            {
                if (useTag)
                {
                    if (obj.CompareTag(token))
                    {
                        UnityEngine.Object.DestroyImmediate(obj);
                        removed++;
                    }
                }
                else if (obj.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                    removed++;
                }
            }

            Debug.Log($"[YUCP UpdateRunner] Removed {removed} scene object(s) using query '{step.query}'.");
            return true;
        }

        private static bool ReimportAssets(UpdateStep step)
        {
            foreach (var path in step.paths)
            {
                string unityPath = UpdateStepPathUtility.NormalizeUnityPath(path);
                if (!unityPath.StartsWith("Assets/") && !unityPath.StartsWith("Packages/"))
                {
                    unityPath = "Assets/" + unityPath.TrimStart('/');
                }
                AssetDatabase.ImportAsset(unityPath, ImportAssetOptions.ForceUpdate);
            }
            return true;
        }

        private static string FindDefaultMetadataPath()
        {
            try
            {
                string candidate = Path.Combine(Application.dataPath, "YUCP_PackageInfo.json");
                if (File.Exists(candidate)) return candidate;

                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string installedRoot = Path.Combine(projectRoot, "Packages", "yucp.installed-packages");
                if (Directory.Exists(installedRoot))
                {
                    string[] matches = Directory.GetFiles(installedRoot, "YUCP_PackageInfo.json", SearchOption.AllDirectories);
                    if (matches.Length > 0)
                        return matches[0];
                }
            }
            catch { }
            return null;
        }

        private static string BuildStepSummary(List<UpdateStep> steps)
        {
            var lines = new List<string>();
            for (int i = 0; i < steps.Count && i < 12; i++)
            {
                var s = steps[i];
                string destructive = s.IsDestructive ? " [DESTRUCTIVE]" : "";
                lines.Add($"- {s.name} ({s.type}, {s.phase}){destructive}");
            }
            if (steps.Count > 12)
                lines.Add($"...and {steps.Count - 12} more.");
            return string.Join("\n", lines);
        }

        private static string BuildFullStepDetails(List<UpdateStep> steps)
        {
            var lines = new List<string>();
            foreach (var s in steps)
            {
                string destructive = s.IsDestructive ? " [DESTRUCTIVE]" : "";
                lines.Add($"{s.name} ({s.type}, {s.phase}){destructive}");
                if (!string.IsNullOrEmpty(s.message))
                    lines.Add($"  - Message: {s.message}");
                if (s.paths != null && s.paths.Count > 0)
                    lines.Add($"  - Paths: {string.Join(", ", s.paths)}");
                if (!string.IsNullOrEmpty(s.query))
                    lines.Add($"  - Query: {s.query}");
                if (!string.IsNullOrEmpty(s.scenePath))
                    lines.Add($"  - Scene: {s.scenePath}");
            }
            return string.Join("\n", lines);
        }

        private static void WarnNoSteps(UpdateMetadata metadata, string reason)
        {
            string title = "Update Detected (Safe Mode)";
            string body = $"{reason}\n\nNo custom update steps were found or enabled.\n\n" +
                          "Automatic overwrites are disabled to protect your project. " +
                          "The incoming update files were left untouched so you can inspect or configure steps safely.";
            EditorUtility.DisplayDialog(title, body, "OK");
        }

        private static int CompareVersions(string v1, string v2)
        {
            try
            {
                var a = ParseVersion(v1);
                var b = ParseVersion(v2);
                if (a.major != b.major) return a.major.CompareTo(b.major);
                if (a.minor != b.minor) return a.minor.CompareTo(b.minor);
                return a.patch.CompareTo(b.patch);
            }
            catch
            {
                return 0;
            }
        }

        private static (int major, int minor, int patch) ParseVersion(string v)
        {
            v = v.Trim().TrimStart('v', 'V');
            int dashIndex = v.IndexOf('-');
            if (dashIndex > 0) v = v.Substring(0, dashIndex);
            var parts = v.Split('.');
            int major = parts.Length > 0 ? int.Parse(parts[0]) : 0;
            int minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            int patch = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            return (major, minor, patch);
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string dest = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, dest);
            }
        }
    }
}
