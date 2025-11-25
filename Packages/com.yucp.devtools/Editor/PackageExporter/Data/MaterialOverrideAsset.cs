using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Material parameter overrides (float, color, texture swaps).
	/// </summary>
	public class MaterialOverrideAsset : ScriptableObject
	{
		[SerializeField] public List<FloatParam> floatParams = new List<FloatParam>();
		[SerializeField] public List<ColorParam> colorParams = new List<ColorParam>();
		[SerializeField] public List<TextureParam> textureParams = new List<TextureParam>();

		[Serializable]
		public struct FloatParam { public string name; public float value; }

		[Serializable]
		public struct ColorParam { public string name; public Color value; }

		[Serializable]
		public struct TextureParam { public string name; public Texture texture; }
	}
}




