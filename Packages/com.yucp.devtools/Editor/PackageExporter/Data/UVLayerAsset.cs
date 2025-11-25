using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// UV layer payload for add/replace operations.
	/// </summary>
	public class UVLayerAsset : ScriptableObject
	{
		[SerializeField] public string targetMeshName;
		[SerializeField] public int channel;
		[SerializeField] public Vector2[] uvs;
	}
}




