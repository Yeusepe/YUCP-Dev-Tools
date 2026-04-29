using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class AliasPackageContractBuilder
    {
        internal const string ContractKind = "alias-v1";
        private const string ImporterPackageName = "com.yucp.importer";
        private const string InstallStrategy = "server-authorized";

        public static JObject Build(ExportProfile profile, IEnumerable<PackageDependency> dependencies)
        {
            string aliasId = profile?.packageId?.Trim();
            if (string.IsNullOrEmpty(aliasId))
            {
                return null;
            }

            var contract = new JObject
            {
                ["kind"] = ContractKind,
                ["aliasId"] = aliasId,
                ["installStrategy"] = InstallStrategy,
                ["importerPackage"] = ImporterPackageName,
            };

            string minImporterVersion = ResolveMinimumImporterVersion(dependencies);
            if (!string.IsNullOrEmpty(minImporterVersion))
            {
                contract["minImporterVersion"] = minImporterVersion;
            }

            List<string> catalogProductIds = profile.GetResolvedLicenseProductIds();
            if (catalogProductIds.Count > 0)
            {
                contract["catalogProductIds"] = new JArray(catalogProductIds);
            }

            string channel = profile.GetResolvedPublishChannel();
            if (!string.IsNullOrEmpty(channel))
            {
                contract["channel"] = channel;
            }

            return contract;
        }

        private static string ResolveMinimumImporterVersion(IEnumerable<PackageDependency> dependencies)
        {
            PackageDependency importerDependency = dependencies?
                .FirstOrDefault(dep =>
                    dep != null &&
                    dep.enabled &&
                    dep.exportMode == DependencyExportMode.Dependency &&
                    string.Equals(dep.packageName, ImporterPackageName, System.StringComparison.OrdinalIgnoreCase));

            if (importerDependency == null)
            {
                return null;
            }

            string minimumVersion = importerDependency.versionMode == DependencyVersionMode.Specific
                ? importerDependency.GetSpecificVersionOrDefault()
                : importerDependency.packageVersion?.Trim();

            return string.IsNullOrEmpty(minimumVersion) ? null : minimumVersion;
        }
    }
}
