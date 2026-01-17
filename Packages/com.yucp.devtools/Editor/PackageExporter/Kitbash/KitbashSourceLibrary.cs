#if YUCP_KITBASH_ENABLED
using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash
{
    /// <summary>
    /// Entry in KitbashSourceLibrary representing one allowed source FBX.
    /// </summary>
    [Serializable]
    public class KitbashSourceEntry
    {
        /// <summary>
        /// GUID of the source FBX asset.
        /// </summary>
        public string guid;
        
        /// <summary>
        /// User-friendly display name for the source.
        /// </summary>
        public string displayName;
        
        /// <summary>
        /// Tags for categorization (e.g., "head", "torso", "hair").
        /// </summary>
        public string[] tags = Array.Empty<string>();
        
        /// <summary>
        /// Optional: specific mesh paths within the FBX to include.
        /// Empty means include all meshes.
        /// </summary>
        public string[] meshPaths = Array.Empty<string>();
        
        /// <summary>
        /// Color used for visualization in the ownership map tool.
        /// </summary>
        public Color color = Color.white;
    }

    /// <summary>
    /// Registry of allowed source FBXs for kitbash recipes.
    /// Provides a consistent list of sources that can be referenced across multiple recipes.
    /// </summary>
    [CreateAssetMenu(menuName = "YUCP/Kitbash/Source Library", fileName = "KitbashSourceLibrary")]
    public class KitbashSourceLibrary : ScriptableObject
    {
        /// <summary>
        /// List of registered source FBXs.
        /// </summary>
        public List<KitbashSourceEntry> sources = new List<KitbashSourceEntry>();
        
        /// <summary>
        /// Finds a source by GUID.
        /// </summary>
        public KitbashSourceEntry FindByGuid(string guid)
        {
            return sources.Find(s => s.guid == guid);
        }
        
        /// <summary>
        /// Finds sources by tag.
        /// </summary>
        public List<KitbashSourceEntry> FindByTag(string tag)
        {
            return sources.FindAll(s => Array.IndexOf(s.tags, tag) >= 0);
        }
        
        /// <summary>
        /// Gets a deterministic color for a source index.
        /// Used for visualization when no custom color is set.
        /// </summary>
        public static Color GetDefaultColor(int index)
        {
            // Use HSV to generate distinct colors
            float hue = (index * 0.618033988749895f) % 1f; // Golden ratio for good distribution
            return Color.HSVToRGB(hue, 0.7f, 0.9f);
        }
    }
}
#endif
