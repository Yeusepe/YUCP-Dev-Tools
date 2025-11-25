using System;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	[Serializable]
	public class AvatarGalleryImage
	{
		public string id;
		public string fileId;
		public string url;
		[NonSerialized] public Texture2D thumbnail;
	}
}





