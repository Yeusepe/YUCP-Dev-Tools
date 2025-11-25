using System.Linq;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Basic validator for derived assets before commit.
	/// </summary>
	public static class Validator
	{
		public static bool ValidateMesh(Mesh mesh, out string error)
		{
			error = null;
			if (mesh == null) { error = "Mesh is null"; return false; }

			// NaN check on vertices
			var v = mesh.vertices;
			for (int i = 0; i < v.Length; i++)
			{
				if (float.IsNaN(v[i].x) || float.IsNaN(v[i].y) || float.IsNaN(v[i].z))
				{
					error = $"NaN detected in vertex {i}";
					return false;
				}
			}
			return true;
		}
	}
}




