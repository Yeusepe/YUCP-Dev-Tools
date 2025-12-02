using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Builds a simple correspondence map (v1 → v2) using name heuristics.
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

		public static Map Build(ManifestBuilder.Manifest v1, ManifestBuilder.Manifest v2, object seeds)
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
			if (seeds != null)
			{
				var materialAliasesField = seeds.GetType().GetField("materialAliases");
				if (materialAliasesField != null)
				{
					var materialAliases = materialAliasesField.GetValue(seeds) as System.Collections.IEnumerable;
					if (materialAliases != null)
					{
						foreach (var alias in materialAliases)
						{
							var fromField = alias.GetType().GetField("from");
							var toField = alias.GetType().GetField("to");
							if (fromField != null && toField != null)
							{
								var from = fromField.GetValue(alias)?.ToString();
								var to = toField.GetValue(alias)?.ToString();
								if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
									map.materialMap[from] = to;
							}
						}
					}
				}
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
			if (seeds != null)
			{
				var blendshapeAliasesField = seeds.GetType().GetField("blendshapeAliases");
				if (blendshapeAliasesField != null)
				{
					var blendshapeAliases = blendshapeAliasesField.GetValue(seeds) as System.Collections.IEnumerable;
					if (blendshapeAliases != null)
					{
						foreach (var alias in blendshapeAliases)
						{
							var fromField = alias.GetType().GetField("from");
							var toField = alias.GetType().GetField("to");
							if (fromField != null && toField != null)
							{
								var from = fromField.GetValue(alias)?.ToString();
								var to = toField.GetValue(alias)?.ToString();
								if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
									map.blendshapeMap[from] = to;
							}
						}
					}
				}
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




