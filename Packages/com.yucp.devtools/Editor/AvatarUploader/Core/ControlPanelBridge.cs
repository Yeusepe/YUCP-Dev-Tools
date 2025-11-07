using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Lightweight bridge that keeps the official VRChat Control Panel alive (off-screen) and allows
	/// us to invoke its logic through reflection while presenting our own UI.
	/// </summary>
	internal static class ControlPanelBridge
	{
		private const string DefaultPanelMenu = "VRChat SDK/Control Panel";
		private static EditorWindow _panelInstance;
		private static Type _panelType;
		private static MethodInfo _onSelectionChange;
		private static MethodInfo _uploadMethod;
		private static MethodInfo _validateMethod;

		public static bool Initialize()
		{
			try
			{
				EnsurePanel();
				return _panelInstance != null;
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to initialise Control Panel bridge: {ex.Message}");
				return false;
			}
		}

		public static bool TryUploadAvatar(AvatarUploadProfile profile, AvatarBuildConfig config, PlatformSwitcher.BuildPlatform platform)
		{
			if (config == null || config.avatarPrefab == null)
				return false;

			var panel = EnsurePanel();
			if (panel == null)
				return false;

			try
			{
				HidePanel(panel);
				Selection.activeObject = config.avatarPrefab;
				InvokeIfAvailable(panel, _onSelectionChange);

				// Let the Control Panel use whichever platform is active.
				switch (platform)
				{
					case PlatformSwitcher.BuildPlatform.PC:
						PlatformSwitcher.SwitchToPC();
						break;
					case PlatformSwitcher.BuildPlatform.Quest:
						PlatformSwitcher.SwitchToQuest();
						break;
				}

				// Apply blueprint information via PipelineManager to match Control Panel behaviour.
				if (!string.IsNullOrEmpty(config.blueprintIdPC) || !string.IsNullOrEmpty(config.blueprintIdQuest))
				{
					var blueprintId = platform == PlatformSwitcher.BuildPlatform.PC ? config.blueprintIdPC : config.blueprintIdQuest;
					if (!string.IsNullOrEmpty(blueprintId))
					{
						BlueprintManager.SetBlueprintId(config.avatarPrefab, blueprintId);
					}
				}

				if (config.avatarIcon != null)
				{
					BlueprintManager.SetAvatarIcon(config.avatarPrefab, config.avatarIcon);
				}

				// The Control Panel typically performs validation before upload; trigger when available.
				InvokeIfAvailable(panel, _validateMethod);

				// Finally trigger the upload routine.
				if (_uploadMethod != null)
				{
					_uploadMethod.Invoke(panel, null);
					return true;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Control Panel upload invocation failed: {ex.Message}");
			}

			return false;
		}

		private static EditorWindow EnsurePanel()
		{
			if (_panelInstance != null)
				return _panelInstance;

			_panelType = LocateControlPanelType();
			if (_panelType == null)
				return null;

			_panelInstance = Resources.FindObjectsOfTypeAll(_panelType).Cast<EditorWindow>().FirstOrDefault();
			if (_panelInstance == null)
			{
				if (!EditorApplication.ExecuteMenuItem(DefaultPanelMenu))
				{
					_panelInstance = ScriptableObject.CreateInstance(_panelType) as EditorWindow;
					_panelInstance?.ShowUtility();
				}
				_panelInstance = Resources.FindObjectsOfTypeAll(_panelType).Cast<EditorWindow>().FirstOrDefault();
			}

			if (_panelInstance != null)
			{
				HidePanel(_panelInstance);
				CacheMethods();
			}

			return _panelInstance;
		}

		private static Type LocateControlPanelType()
		{
			const string preferredName = "VRC.SDKBase.Editor.VRCControlPanel";
			var type = Type.GetType(preferredName);
			if (type != null)
				return type;

			return AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(a =>
				{
					try { return a.GetTypes(); }
					catch (ReflectionTypeLoadException rtl) { return rtl.Types.Where(t => t != null); }
				})
				.FirstOrDefault(t => typeof(EditorWindow).IsAssignableFrom(t) &&
				                     t.FullName != null &&
				                     t.FullName.IndexOf("ControlPanel", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		private static void HidePanel(EditorWindow panel)
		{
			if (panel == null) return;
			panel.minSize = Vector2.one;
			panel.maxSize = Vector2.one;
			panel.position = new Rect(-10000, -10000, 10, 10);
			panel.titleContent = new GUIContent("VUCP-ControlPanel");
		}

		private static void CacheMethods()
		{
			if (_panelType == null)
				return;

			const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

			_onSelectionChange = _panelType.GetMethod("OnSelectionChange", flags);
			_validateMethod = FindMethodContaining("Validate", flags);
			_uploadMethod = FindMethodContaining("Upload", flags);
		}

		private static MethodInfo FindMethodContaining(string token, BindingFlags flags)
		{
			return _panelType?
				.GetMethods(flags)
				.FirstOrDefault(m =>
					m.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 &&
					m.GetParameters().Length == 0);
		}

		private static void InvokeIfAvailable(object target, MethodInfo method)
		{
			if (target == null || method == null)
				return;

			try
			{
				method.Invoke(target, null);
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Control Panel method '{method.Name}' invocation failed: {ex.Message}");
			}
		}
	}
}

