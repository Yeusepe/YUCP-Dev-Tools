using System;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Mode for derived FBX export - determines how the base is resolved.
    /// </summary>
    public enum DerivedMode
    {
        /// <summary>
        /// Single base FBX with HDiff binary patch (current behavior).
        /// </summary>
        SingleBaseHdiff = 0,
        
        /// <summary>
        /// Multi-source kitbash with recipe-based synthetic base assembly.
        /// Requires YUCP_KITBASH_ENABLED scripting define.
        /// </summary>
        KitbashRecipeHdiff = 1
    }

    /// <summary>
    /// Settings stored in ModelImporter.userData for derived FBX export.
    /// Unified type used by both UI and PackageBuilder.
    /// </summary>
    [Serializable]
    public class DerivedSettings
    {
        /// <summary>
        /// Whether this FBX should be exported as a derived patch.
        /// </summary>
        public bool isDerived;
        
        /// <summary>
        /// GUID of the base FBX this derives from (SingleBaseHdiff mode).
        /// </summary>
        public string baseGuid;
        
        /// <summary>
        /// User-friendly name for the derived asset.
        /// </summary>
        public string friendlyName;
        
        /// <summary>
        /// Category for organization in UI.
        /// </summary>
        public string category;
        
        /// <summary>
        /// If true, replace all references to original FBX with new one after generation.
        /// </summary>
        public bool overrideOriginalReferences = false;
        
        // ============================================================
        // Kitbash mode fields (requires YUCP_KITBASH_ENABLED)
        // ============================================================
        
        /// <summary>
        /// Export mode - determines how the base FBX is resolved.
        /// </summary>
        public DerivedMode mode = DerivedMode.SingleBaseHdiff;
        
        /// <summary>
        /// GUID of the KitbashRecipe ScriptableObject (KitbashRecipeHdiff mode).
        /// </summary>
        public string kitbashRecipeGuid;
        
        /// <summary>
        /// GUID of the OwnershipMap ScriptableObject (KitbashRecipeHdiff mode).
        /// </summary>
        public string ownershipMapGuid;
    }
}
