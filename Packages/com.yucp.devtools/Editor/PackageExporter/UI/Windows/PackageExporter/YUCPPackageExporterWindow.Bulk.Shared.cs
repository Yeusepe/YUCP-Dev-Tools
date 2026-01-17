using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using YUCP.DevTools.Components;
using YUCP.DevTools.Editor.PackageExporter.UI.Components;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
        private void ApplyToAllSelected(System.Action<ExportProfile> action)
        {
            foreach (int index in selectedProfileIndices)
            {
                if (index >= 0 && index < allProfiles.Count)
                {
                    var profile = allProfiles[index];
                    if (profile != null)
                    {
                        action(profile);
                    }
                }
            }
            UpdateProfileDetails(); // Refresh to show changes
        }

    }
}
