using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	public static class BlueprintManager
	{
		private static Type FindPipelineManagerType()
		{
			// Try common full name first
			var t = Type.GetType("VRC.Core.PipelineManager, VRCSDKBase");
			if (t != null) return t;
			// Search loaded assemblies as fallback
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					var candidate = asm.GetTypes().FirstOrDefault(x => x.Name == "PipelineManager");
					if (candidate != null) return candidate;
				}
				catch (ReflectionTypeLoadException)
				{
					// ignore
				}
			}
			return null;
		}

		public static Component GetOrAddPipelineManager(GameObject avatarRoot)
		{
			if (avatarRoot == null) return null;
			var pmType = FindPipelineManagerType();
			if (pmType == null) return null;
			var existing = avatarRoot.GetComponent(pmType);
			if (existing == null)
			{
				existing = avatarRoot.AddComponent(pmType);
			}
			// Try set contentType = Avatar (usually 0)
			var ctField = pmType.GetField("contentType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			var ctProp = pmType.GetProperty("contentType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			var enumType = pmType.GetNestedType("ContentType", BindingFlags.Public | BindingFlags.NonPublic);
			object avatarEnum = null;
			if (enumType != null)
			{
				avatarEnum = Enum.GetValues(enumType).Cast<object>().FirstOrDefault(); // first value is Avatar per SDK
			}
			if (avatarEnum != null)
			{
				if (ctField != null) ctField.SetValue(existing, avatarEnum);
				if (ctProp != null && ctProp.CanWrite) ctProp.SetValue(existing, avatarEnum);
			}
			return existing;
		}

		public static void SetBlueprintId(GameObject avatarRoot, string blueprintId)
		{
			var pipeline = GetOrAddPipelineManager(avatarRoot);
			if (pipeline == null) return;
			var pmType = pipeline.GetType();
			var idField = pmType.GetField("blueprintId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			var idProp = pmType.GetProperty("blueprintId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (idField != null) idField.SetValue(pipeline, blueprintId);
			if (idProp != null && idProp.CanWrite) idProp.SetValue(pipeline, blueprintId);
		}

		public static string GenerateNewBlueprintId()
		{
			// SDK uses GUID-style IDs when creating new blueprints
			return Guid.NewGuid().ToString();
		}
	}
}


