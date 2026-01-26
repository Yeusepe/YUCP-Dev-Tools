using System;
using System.Collections.Generic;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Settings stored in ModelImporter.userData for derived FBX export.
    /// </summary>
    [Serializable]
    public class DerivedSettings
    {
        /// <summary>
        /// Whether this FBX should be exported as a derived patch.
        /// </summary>
        public bool isDerived;

        /// <summary>
        /// GUIDs of base FBXs that can be used to reconstruct this derived FBX.
        /// </summary>
        public List<string> baseGuids = new List<string>();

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
    }
}
