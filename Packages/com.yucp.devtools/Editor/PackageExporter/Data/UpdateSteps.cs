using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    [Serializable]
    public class UpdateStepList
    {
        public bool enabled = false;
        public List<UpdateStep> steps = new List<UpdateStep>();
        public string archiveSuffix = "_old";
    }

    public enum UpdatePhase
    {
        PreImport,
        Manual,
        PostImport
    }

    public enum UpdateStepType
    {
        BackupAssets,
        MoveAssets,
        CopyAssets,
        DeleteAssets,
        DeleteFolder,
        CreateFolder,
        OpenScene,
        RemoveSceneObjects,
        PromptUser,
        WaitForUser,
        ValidatePresence,
        ValidateVersion,
        RefreshAssetDatabase,
        ReimportAssets,
        ResolveGuidReferences
    }

    public enum UpdateValidationScope
    {
        Assets,
        Files,
        SceneObjects
    }

    public enum UpdateValidationCondition
    {
        Exists,
        Missing,
        CountEquals,
        CountAtLeast,
        CountAtMost,
        ContentContains,
        ContentMatches
    }

    public enum UpdateValidationSeverity
    {
        Block,
        Warn,
        Info
    }

    public enum UpdateValidationMode
    {
        All,
        Any
    }

    [Serializable]
    public class UpdateValidationRule
    {
        public string name = "New Rule";
        public bool enabled = true;
        public UpdateValidationScope scope = UpdateValidationScope.Assets;
        public UpdateValidationCondition condition = UpdateValidationCondition.Exists;
        public UpdateValidationSeverity severity = UpdateValidationSeverity.Block;
        public string selector = "";
        public int expectedCount = 0;
        public string text = "";
        public bool allowSkip = false;
    }

    [Serializable]
    public class UpdateStep
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "New Step";
        public bool enabled = true;
        public UpdatePhase phase = UpdatePhase.PreImport;
        public UpdateStepType type = UpdateStepType.PromptUser;

        // Safety
        public bool allowOverwrite = false;
        public bool allowDelete = false;
        public bool requiresUserConfirm = true;
        public bool reversible = true;

        // Parameters
        public List<string> paths = new List<string>();
        public string query = "";
        public string scenePath = "";
        public string message = "";
        public string versionMin = "";
        public string versionMax = "";
        public string packageNameMatch = "";
        public UpdateValidationMode validationMode = UpdateValidationMode.All;
        public List<UpdateValidationRule> validationRules = new List<UpdateValidationRule>();

        public bool IsDestructive => type == UpdateStepType.DeleteAssets || type == UpdateStepType.DeleteFolder || allowDelete || allowOverwrite;
    }

    internal static class UpdateStepPathUtility
    {
        public static string NormalizeUnityPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.Replace('\\', '/').Trim();
        }

        public static bool IsSafeUnityPath(string unityPath)
        {
            unityPath = NormalizeUnityPath(unityPath);
            if (string.IsNullOrEmpty(unityPath)) return false;
            return unityPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                   unityPath.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                   unityPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryGetAbsolutePath(string inputPath, out string absolutePath)
        {
            absolutePath = null;
            if (string.IsNullOrEmpty(inputPath)) return false;

            string normalized = NormalizeUnityPath(inputPath);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            if (Path.IsPathRooted(normalized))
            {
                string full = Path.GetFullPath(normalized);
                if (!full.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)) return false;
                absolutePath = full;
                return true;
            }

            if (!IsSafeUnityPath(normalized))
            {
                normalized = NormalizeUnityPath(Path.Combine("Assets", normalized));
            }

            if (!IsSafeUnityPath(normalized)) return false;
            absolutePath = Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
            return true;
        }
    }
}
