using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Sparse per-vertex deltas to apply to a base mesh. Vertex counts must match base mesh.
	/// </summary>
	public class MeshDeltaAsset : ScriptableObject
	{
		[SerializeField] public string targetMeshName;
		[SerializeField] public int vertexCount;
		[SerializeField] public Vector3[] positionDeltas;
		[SerializeField] public Vector3[] normalDeltas;
		[SerializeField] public Vector3[] tangentDeltas;
	}
}




