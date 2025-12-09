using System;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Advanced obfuscation configuration settings for fine-grained control.
    /// Allows customization of individual protections and Unity-specific compatibility options.
    /// </summary>
    [Serializable]
    public class ObfuscationSettings
    {
        [Tooltip("Preserve names for reflection-dependent code (GetType, GetMethod, etc.)")]
        public bool preserveReflection = false;
        
        [Tooltip("Enable IL2CPP-specific optimizations and compatibility settings")]
        public bool il2cppCompatible = true;
        
        [Tooltip("Enable control flow obfuscation (restructures method logic)")]
        public bool enableControlFlow = true;
        
        [Tooltip("Enable reference proxy protection (adds indirection layers)")]
        public bool enableReferenceProxy = true;
        
        [Tooltip("Enable constants encryption (encrypts string literals and numbers)")]
        public bool enableConstants = true;
        
        [Tooltip("Enable resource protection (encrypts embedded resources)")]
        public bool enableResources = true;
        
        [Tooltip("Enable invalid metadata protection (adds junk metadata to confuse decompilers)")]
        public bool enableInvalidMetadata = true;
        
        [Tooltip("Control flow obfuscation intensity (0-100, higher = more obfuscation)")]
        [Range(0, 100)]
        public int controlFlowIntensity = 100;
        
        [Tooltip("Reference proxy depth (1-10, higher = more indirection)")]
        [Range(1, 10)]
        public int referenceProxyDepth = 5;
        
        /// <summary>
        /// Rename mode for symbol obfuscation
        /// </summary>
        public enum RenameMode
        {
            Letters,
            Unicode,
            Sequential
        }
        
        [Tooltip("Rename mode for symbol obfuscation")]
        public RenameMode renameMode = RenameMode.Unicode;
        
        /// <summary>
        /// Get default settings for maximum obfuscation
        /// </summary>
        public static ObfuscationSettings GetMaximum()
        {
            return new ObfuscationSettings
            {
                preserveReflection = false,
                il2cppCompatible = true,
                enableControlFlow = true,
                enableReferenceProxy = true,
                enableConstants = true,
                enableResources = true,
                enableInvalidMetadata = true,
                controlFlowIntensity = 100,
                referenceProxyDepth = 5,
                renameMode = RenameMode.Unicode
            };
        }
        
        /// <summary>
        /// Get default settings for reflection-preserving obfuscation
        /// </summary>
        public static ObfuscationSettings GetReflectionPreserving()
        {
            return new ObfuscationSettings
            {
                preserveReflection = true,
                il2cppCompatible = true,
                enableControlFlow = true,
                enableReferenceProxy = false,
                enableConstants = true,
                enableResources = true,
                enableInvalidMetadata = false,
                controlFlowIntensity = 60,
                referenceProxyDepth = 3,
                renameMode = RenameMode.Letters
            };
        }
    }
}























