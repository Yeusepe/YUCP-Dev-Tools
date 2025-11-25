using System.Collections.Generic;
using System.Linq;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Builds a simple correspondence map (v1 → v2) using name-based heuristics.
	/// Can be extended with multi-signal fusion later.
	/// </summary>
	public static class MapBuilder
	{
		public class Map
		{
			public Dictionary<string, string> meshMap = new Dictionary<string, string>();
			public Dictionary<string, string> materialMap = new Dictionary<string, string>();
			public Dictionary<string, string> blendshapeMap = new Dictionary<string, string>();
		}

		public static Map Build(ManifestBuilder.Manifest v1, ManifestBuilder.Manifest v2, PatchPackage.SeedMaps seeds)
		{
			var map = new Map();

			// Mesh map: by exact name first; fallback to contains/similarity could be added later
			foreach (var m1 in v1.meshes)
			{
				var m2 = v2.meshes.FirstOrDefault(m => m.name == m1.name);
				if (m2 != null)
					map.meshMap[m1.name] = m2.name;
			}

			// Material map: alias seeds first, then exact name
			foreach (var alias in seeds.materialAliases)
			{
				map.materialMap[alias.from] = alias.to;
			}
			foreach (var mat in v1.materials)
			{
				if (!map.materialMap.ContainsKey(mat.name))
				{
					var m2 = v2.materials.FirstOrDefault(m => m.name == mat.name);
					if (m2 != null)
						map.materialMap[mat.name] = m2.name;
				}
			}

			// Blendshape map: alias seeds first, then exact name
			foreach (var alias in seeds.blendshapeAliases)
			{
				map.blendshapeMap[alias.from] = alias.to;
			}
			foreach (var bs in v1.blendshapeNames)
			{
				if (!map.blendshapeMap.ContainsKey(bs) && v2.blendshapeNames.Contains(bs))
					map.blendshapeMap[bs] = bs;
			}

			return map;
		}
	}
}




