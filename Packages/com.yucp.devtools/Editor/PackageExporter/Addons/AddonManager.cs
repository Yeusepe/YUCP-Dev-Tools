using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Addons
{
    /// <summary>
    /// Discovers and manages export addons using TypeCache.
    /// Addons implementing IExportAddon are automatically discovered and sorted by Order.
    /// </summary>
    public static class AddonManager
    {
        private static IExportAddon[] _cachedAddons;
        private static bool _initialized;
        
        /// <summary>
        /// Gets all registered export addons, sorted by Order.
        /// </summary>
        public static IExportAddon[] GetAddons()
        {
            if (!_initialized)
            {
                RefreshAddons();
            }
            return _cachedAddons ?? Array.Empty<IExportAddon>();
        }
        
        /// <summary>
        /// Forces a refresh of the addon cache.
        /// Call this if new addons are added at runtime.
        /// </summary>
        public static void InvalidateCache()
        {
            _cachedAddons = null;
            _initialized = false;
        }
        
        /// <summary>
        /// Refreshes the addon cache by scanning all types implementing IExportAddon.
        /// </summary>
        private static void RefreshAddons()
        {
            _initialized = true;
            
            try
            {
                var types = TypeCache.GetTypesDerivedFrom<IExportAddon>();
                var addons = new List<IExportAddon>();
                
                foreach (var type in types)
                {
                    if (type.IsAbstract || type.IsInterface)
                        continue;
                    
                    try
                    {
                        var addon = Activator.CreateInstance(type) as IExportAddon;
                        if (addon != null)
                        {
                            addons.Add(addon);
                            Debug.Log($"[AddonManager] Discovered addon: {type.Name} (Order: {addon.Order})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[AddonManager] Failed to instantiate addon {type.Name}: {ex.Message}");
                    }
                }
                
                // Sort by Order (ascending)
                _cachedAddons = addons.OrderBy(a => a.Order).ToArray();
                
                if (_cachedAddons.Length > 0)
                {
                    Debug.Log($"[AddonManager] Loaded {_cachedAddons.Length} addon(s)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AddonManager] Failed to discover addons: {ex.Message}");
                _cachedAddons = Array.Empty<IExportAddon>();
            }
        }
        
        /// <summary>
        /// Invokes OnPreBuild on all addons.
        /// </summary>
        public static void InvokePreBuild(PackageBuilderContext ctx)
        {
            foreach (var addon in GetAddons())
            {
                try
                {
                    addon.OnPreBuild(ctx);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AddonManager] {addon.GetType().Name}.OnPreBuild failed: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Invokes OnCollectAssets on all addons.
        /// </summary>
        public static void InvokeCollectAssets(PackageBuilderContext ctx)
        {
            foreach (var addon in GetAddons())
            {
                try
                {
                    addon.OnCollectAssets(ctx);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AddonManager] {addon.GetType().Name}.OnCollectAssets failed: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Invokes OnPreWriteTempPackage on all addons.
        /// </summary>
        public static void InvokePreWriteTempPackage(PackageBuilderContext ctx)
        {
            foreach (var addon in GetAddons())
            {
                try
                {
                    addon.OnPreWriteTempPackage(ctx);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AddonManager] {addon.GetType().Name}.OnPreWriteTempPackage failed: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Invokes OnPostWriteTempPackage on all addons.
        /// </summary>
        public static void InvokePostWriteTempPackage(PackageBuilderContext ctx)
        {
            foreach (var addon in GetAddons())
            {
                try
                {
                    addon.OnPostWriteTempPackage(ctx);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AddonManager] {addon.GetType().Name}.OnPostWriteTempPackage failed: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Attempts to convert a derived FBX using registered addons.
        /// Returns true if any addon handled the conversion.
        /// </summary>
        public static bool TryConvertDerivedFbx(
            PackageBuilderContext ctx,
            string derivedFbxPath,
            DerivedSettings settings,
            out string tempAssetPath)
        {
            tempAssetPath = null;
            
            foreach (var addon in GetAddons())
            {
                try
                {
                    if (addon.TryConvertDerivedFbx(ctx, derivedFbxPath, settings, out tempAssetPath))
                    {
                        Debug.Log($"[AddonManager] {addon.GetType().Name} handled derived FBX conversion for {derivedFbxPath}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AddonManager] {addon.GetType().Name}.TryConvertDerivedFbx failed: {ex.Message}");
                }
            }
            
            return false;
        }
    }
}
