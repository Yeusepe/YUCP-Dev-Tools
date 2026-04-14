using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class TrustedMilestoneTracker
    {
        private const string MilestoneTrackerAssemblyName = "com.yucp.components.Editor";
        private const string MilestoneTrackerTypeName = "YUCP.Components.Editor.SupportBanner.MilestoneTracker";
        private const string MilestoneTrackerAssemblyQualifiedTypeName = MilestoneTrackerTypeName + ", " + MilestoneTrackerAssemblyName;

        internal static Type GetTrustedMilestoneTrackerType(IEnumerable<Assembly> loadedAssemblies = null)
        {
            if (loadedAssemblies == null)
            {
                Type trackerType = Type.GetType(MilestoneTrackerAssemblyQualifiedTypeName, throwOnError: false);
                return trackerType ?? GetTrustedMilestoneTrackerType(AppDomain.CurrentDomain.GetAssemblies());
            }

            Assembly trustedAssembly = loadedAssemblies.FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.GetName().Name, MilestoneTrackerAssemblyName, StringComparison.Ordinal));

            return trustedAssembly?.GetType(MilestoneTrackerTypeName, throwOnError: false);
        }

        internal static void InvokeStatic(string methodName)
        {
            if (string.IsNullOrWhiteSpace(methodName))
                return;

            Type trackerType = GetTrustedMilestoneTrackerType();
            MethodInfo method = trackerType?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            method?.Invoke(null, null);
        }
    }
}
