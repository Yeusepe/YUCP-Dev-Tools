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

		public static void SetAvatarIcon(GameObject avatarRoot, Texture2D icon)
		{
			if (avatarRoot == null || icon == null)
				return;

			var pipeline = GetOrAddPipelineManager(avatarRoot);
			if (pipeline == null)
				return;

			var pmType = pipeline.GetType();
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			var assigned = false;

			foreach (var field in pmType.GetFields(flags))
			{
				if (!typeof(Texture2D).IsAssignableFrom(field.FieldType))
					continue;
				var name = field.Name.ToLowerInvariant();
				if (name.Contains("icon") || name.Contains("thumb") || name.Contains("image"))
				{
					field.SetValue(pipeline, icon);
					assigned = true;
				}
			}

			foreach (var prop in pmType.GetProperties(flags))
			{
				if (!prop.CanWrite || !typeof(Texture2D).IsAssignableFrom(prop.PropertyType))
					continue;
				var name = prop.Name.ToLowerInvariant();
				if (name.Contains("icon") || name.Contains("thumb") || name.Contains("image"))
				{
					prop.SetValue(pipeline, icon);
					assigned = true;
				}
			}

			if (!assigned)
			{
				var method = pmType.GetMethods(flags)
					.FirstOrDefault(m => m.GetParameters().Length == 1 && typeof(Texture2D).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
					                (m.Name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
					                 m.Name.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0 ||
					                 m.Name.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0));
				method?.Invoke(pipeline, new object[] { icon });
			}
		}

		public static string GenerateNewBlueprintId()
		{
			// SDK uses GUID-style IDs when creating new blueprints
			return Guid.NewGuid().ToString();
		}
	}
}


