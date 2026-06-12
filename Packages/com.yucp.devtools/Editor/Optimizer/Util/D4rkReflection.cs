using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace YUCP.DevTools.Editor.Optimizer.Util
{
    /// <summary>
    /// Thin reflection wrapper around the optional d4rkAvatarOptimizer package. The optimizer assembly is
    /// not referenced directly (it may be absent), so we resolve its type, configure its <c>settings</c>,
    /// and invoke <c>Optimize()</c> dynamically — mirroring the existing AvatarOptimizerPluginProcessor.
    /// </summary>
    public static class D4rkReflection
    {
        private const string OptimizerTypeName = "d4rkAvatarOptimizer";

        private static bool _checked;
        private static Type _optimizerType;
        private static Type _settingsType;

        public static bool IsInstalled
        {
            get
            {
                EnsureResolved();
                return _optimizerType != null;
            }
        }

        private static void EnsureResolved()
        {
            if (_checked) return;
            _checked = true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(OptimizerTypeName);
                if (type != null)
                {
                    _optimizerType = type;
                    _settingsType = type.GetNestedType("Settings");
                    break;
                }
            }
        }

        /// <summary>
        /// Adds (or reuses) a d4rkAvatarOptimizer component on <paramref name="target"/> and applies the
        /// merge/atlas settings derived from <paramref name="options"/> plus the exclusion list.
        /// Returns the configured component, or null if d4rk is not installed.
        /// </summary>
        public static Component AddAndConfigure(GameObject target, OptimizerOptions options, IList<Transform> exclusions)
        {
            EnsureResolved();
            if (_optimizerType == null)
                return null;

            var optimizer = target.GetComponent(_optimizerType) ?? target.AddComponent(_optimizerType);

            // DoAutoSettings lives on the component; false means our explicit toggles below are honored.
            SetComponentField(optimizer, "DoAutoSettings", options.useAutoSettings);

            var settingsField = _optimizerType.GetField("settings", BindingFlags.Public | BindingFlags.Instance);
            if (settingsField != null && _settingsType != null)
            {
                object settings = settingsField.GetValue(optimizer);
                if (settings != null)
                {
                    SetSettingsField(settings, "MergeSkinnedMeshes", options.mergeSkinnedMeshes);
                    SetSettingsField(settings, "MergeDifferentPropertyMaterials", options.mergeDifferentPropertyMaterials);
                    SetSettingsField(settings, "MergeSameDimensionTextures", options.mergeSameDimensionTextures);
                    SetSettingsField(settings, "MergeMainTex", options.mergeMainTex);
                    SetSettingsField(settings, "WritePropertiesAsStaticValues", options.writePropertiesAsStaticValues);
                    // Settings may be a value type; write it back so changes persist on the component.
                    settingsField.SetValue(optimizer, settings);
                }
            }

            if (exclusions != null && exclusions.Count > 0)
            {
                var excludeField = _optimizerType.GetField("ExcludeTransforms", BindingFlags.Public | BindingFlags.Instance);
                excludeField?.SetValue(optimizer, new List<Transform>(exclusions));
            }

            return optimizer;
        }

        /// <summary>Invokes d4rkAvatarOptimizer.Optimize() on the given component.</summary>
        public static void Optimize(Component optimizer)
        {
            if (optimizer == null)
                throw new ArgumentNullException(nameof(optimizer));

            var method = _optimizerType.GetMethod("Optimize", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                throw new MissingMethodException(OptimizerTypeName, "Optimize");

            method.Invoke(optimizer, null);
        }

        private static void SetComponentField(Component optimizer, string fieldName, object value)
        {
            try
            {
                var field = _optimizerType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                field?.SetValue(optimizer, value);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Optimizer] Failed to set d4rk field '{fieldName}': {ex.Message}");
            }
        }

        private static void SetSettingsField(object settings, string fieldName, object value)
        {
            try
            {
                var field = _settingsType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                field?.SetValue(settings, value);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Optimizer] Failed to set d4rk setting '{fieldName}': {ex.Message}");
            }
        }
    }
}
