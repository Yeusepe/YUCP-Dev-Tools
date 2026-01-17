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
        [Serializable]
        private class SerializableStringList
        {
            public List<string> items = new List<string>();
        }

        [Serializable]
        private class UnifiedOrderItem
        {
            public bool isFolder;
            public string identifier; // GUID for profiles, folder name for folders
        }

        [Serializable]
        private class UnifiedOrderList
        {
            public List<UnifiedOrderItem> items = new List<UnifiedOrderItem>();
        }

    }
}
