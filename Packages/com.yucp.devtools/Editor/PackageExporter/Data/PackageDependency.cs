using System;

namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Defines how a package dependency should be handled during export.
    /// </summary>
    public enum DependencyExportMode
    {
        /// <summary>
        /// Include the package files directly in the exported package (bundled)
        /// </summary>
        Bundle,
        
        /// <summary>
        /// Add as a dependency in package.json (auto-downloads when users install your package)
        /// </summary>
        Dependency
    }

    /// <summary>
    /// Defines how a VPM dependency version should be resolved at install time.
    /// </summary>
    public enum DependencyVersionMode
    {
        /// <summary>
        /// Resolve the newest available version from the repository at install time.
        /// </summary>
        Latest,

        /// <summary>
        /// Resolve one exact version selected by the creator.
        /// </summary>
        Specific
    }
    
    /// <summary>
    /// Configuration for how to handle a single package dependency during export.
    /// </summary>
    [Serializable]
    public class PackageDependency
    {
        public bool enabled = false;
        public string packageName = "";
        public string packageVersion = "";
        public DependencyVersionMode versionMode = DependencyVersionMode.Latest;
        public string specificVersion = "";
        public string displayName = "";
        public DependencyExportMode exportMode = DependencyExportMode.Dependency;
        public bool isVpmDependency = false; // VRChat packages use vpmDependencies instead
        public string vpmRepositoryUrl = ""; // Optional custom VPM repository index URL
        
        public PackageDependency()
        {
        }
        
        public PackageDependency(string name, string version, string displayName, bool isVpm)
        {
            this.packageName = name;
            this.packageVersion = version;
            this.specificVersion = version;
            this.displayName = displayName;
            this.isVpmDependency = isVpm;
            this.enabled = false;
            this.exportMode = DependencyExportMode.Dependency;
            this.vpmRepositoryUrl = "";
        }

        public string GetSpecificVersionOrDefault()
        {
            if (!string.IsNullOrWhiteSpace(specificVersion))
                return specificVersion.Trim();

            return packageVersion?.Trim() ?? "";
        }

        public string GetVpmVersionRequirement()
        {
            return versionMode == DependencyVersionMode.Latest
                ? ">=0.0.0"
                : GetSpecificVersionOrDefault();
        }

        public string GetVersionLabel()
        {
            return versionMode == DependencyVersionMode.Latest
                ? "latest"
                : GetSpecificVersionOrDefault();
        }
    }
}
