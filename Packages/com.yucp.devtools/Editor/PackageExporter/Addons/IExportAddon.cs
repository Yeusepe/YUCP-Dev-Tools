using System.Collections.Generic;

namespace YUCP.DevTools.Editor.PackageExporter.Addons
{
    /// <summary>
    /// Context object passed to export addons during the build process.
    /// </summary>
    public class PackageBuilderContext
    {
        /// <summary>
        /// The export profile being used for this build.
        /// </summary>
        public ExportProfile Profile { get; set; }
        
        /// <summary>
        /// List of asset paths that will be exported. Addons can add to this list.
        /// </summary>
        public List<string> AssetsToExport { get; set; }
        
        /// <summary>
        /// Path to the temporary package being built.
        /// </summary>
        public string TempPackagePath { get; set; }
        
        /// <summary>
        /// Progress callback for reporting status.
        /// </summary>
        public System.Action<float, string> ProgressCallback { get; set; }
    }
    
    /// <summary>
    /// Interface for export addons that can extend the package export process.
    /// Addons are discovered via TypeCache and invoked in Order sequence.
    /// </summary>
    public interface IExportAddon
    {
        /// <summary>
        /// Order in which this addon runs. Lower values run first.
        /// Default addons should use values 100+.
        /// </summary>
        int Order { get; }
        
        /// <summary>
        /// Called before the build process starts.
        /// </summary>
        void OnPreBuild(PackageBuilderContext ctx) { }
        
        /// <summary>
        /// Called during asset collection phase.
        /// </summary>
        void OnCollectAssets(PackageBuilderContext ctx) { }
        
        /// <summary>
        /// Called just before writing the temporary package.
        /// </summary>
        void OnPreWriteTempPackage(PackageBuilderContext ctx) { }
        
        /// <summary>
        /// Called after the temporary package is written.
        /// </summary>
        void OnPostWriteTempPackage(PackageBuilderContext ctx) { }
        
        /// <summary>
        /// Attempts to convert a derived FBX using addon-specific logic.
        /// Return true if this addon handled the conversion, false to let other addons try.
        /// </summary>
        /// <param name="ctx">Build context</param>
        /// <param name="derivedFbxPath">Path to the derived FBX file</param>
        /// <param name="settings">Derived settings from ModelImporter.userData</param>
        /// <param name="tempAssetPath">Output: path to the generated patch asset if handled</param>
        /// <returns>True if this addon handled the conversion</returns>
        bool TryConvertDerivedFbx(
            PackageBuilderContext ctx,
            string derivedFbxPath,
            DerivedSettings settings,
            out string tempAssetPath);
    }
}
