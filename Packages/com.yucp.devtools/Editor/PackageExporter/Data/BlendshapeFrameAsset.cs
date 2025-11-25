using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Synthesized or replacement blendshape frame payload.
	/// </summary>
	public class BlendshapeFrameAsset : ScriptableObject
	{
		[SerializeField] public string targetMeshName;
		[SerializeField] public string blendshapeName;
		[SerializeField] public float frameWeight = 100f;
		[SerializeField] public Vector3[] deltaVertices;
		[SerializeField] public Vector3[] deltaNormals;
		[SerializeField] public Vector3[] deltaTangents;
	}
}




