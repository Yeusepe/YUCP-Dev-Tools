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
        private VisualElement CreatePackageSigningSection(ExportProfile profile)
        {
            var signingTab = new YUCP.DevTools.Editor.PackageSigning.UI.PackageSigningTab(profile);
            _signingSectionElement = signingTab.CreateUI();
            return _signingSectionElement;
        }

        public void RefreshSigningSection()
        {
            // If a profile is selected, refresh the entire details view to ensure signing section updates
            if (selectedProfile != null)
            {
                UpdateProfileDetails();
            }
            // If no profile selected but signing section exists, just refresh it
            else if (_signingSectionElement != null)
            {
                var parent = _signingSectionElement.parent;
                if (parent != null)
                {
                    var index = parent.IndexOf(_signingSectionElement);
                    _signingSectionElement.RemoveFromHierarchy();
                    
                    var newSigningSection = CreatePackageSigningSection(selectedProfile);
                    parent.Insert(index, newSigningSection);
                }
            }
        }

    }
}
