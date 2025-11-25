using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	internal static class ControlPanelBridge
	{
		private const string ControlPanelMenu = "VRChat SDK/Show Control Panel";

		private static VRCSdkControlPanel _panel;
		private static IVRCSdkAvatarBuilderApi _builder;
		private static bool _isAcquiring;

		private static readonly List<Action<BridgeState>> PendingCallbacks = new();
		private static readonly List<TaskCompletionSource<BridgeState>> Awaiters = new();
		private static readonly FieldInfo SdkBuildersField = typeof(VRCSdkControlPanel).GetField("_sdkBuilders", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly FieldInfo SelectedBuilderField = typeof(VRCSdkControlPanel).GetField("_selectedBuilder", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly MethodInfo PopulateBuildersMethod = typeof(VRCSdkControlPanel).GetMethod("PopulateSdkBuilders", BindingFlags.Instance | BindingFlags.NonPublic);

		internal static bool IsReady => _builder != null;

		internal static void EnsureBuilder(Action<BridgeState> onReady = null, bool focusPanel = false)
		{
			if (IsReady)
			{
				onReady?.Invoke(new BridgeState(_panel, _builder));
				if (focusPanel)
					_panel?.Focus();
				return;
			}

			if (onReady != null)
				PendingCallbacks.Add(onReady);

			if (_isAcquiring)
				return;

			_isAcquiring = true;
			EditorApplication.delayCall += () => AcquireBuilder(focusPanel);
		}

		internal static Task<BridgeState> GetStateAsync(bool focusPanel = false)
		{
			if (IsReady)
				return Task.FromResult(new BridgeState(_panel, _builder));

			var tcs = new TaskCompletionSource<BridgeState>();
			Awaiters.Add(tcs);
			EnsureBuilder(null, focusPanel);
			return tcs.Task;
		}

		internal static bool TryGetBuilder(out IVRCSdkAvatarBuilderApi builder)
		{
			builder = _builder;
			return builder != null;
		}

		internal static bool TryGetControlPanelBuilder(out IVRCSdkControlPanelBuilder builder)
		{
			builder = null;
			if (_panel == null)
				return false;

			try
			{
				PopulateBuildersMethod?.Invoke(_panel, null);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				return false;
		}

			var builderArray = SdkBuildersField?.GetValue(_panel) as Array;
			if (builderArray == null || builderArray.Length == 0)
				return false;

			var selected = SelectedBuilderField?.GetValue(_panel) as IVRCSdkControlPanelBuilder;
			if (selected != null && selected.IsValidBuilder(out _))
			{
				builder = selected;
				return true;
			}

			foreach (var candidateObj in builderArray)
			{
				if (candidateObj is IVRCSdkControlPanelBuilder candidate && candidate.IsValidBuilder(out _))
				{
					builder = candidate;
					SelectedBuilderField?.SetValue(_panel, builder);
						break;
				}
			}

			return builder != null;
					}

		private static void AcquireBuilder(bool focusPanel)
		{
			try
			{
				if (_panel == null)
				{
					_panel = Resources.FindObjectsOfTypeAll<VRCSdkControlPanel>().FirstOrDefault();
					if (_panel == null)
			{
						if (!EditorApplication.ExecuteMenuItem(ControlPanelMenu))
				{
							Debug.LogWarning("[AvatarUploader] Unable to open VRChat Control Panel.");
							return;
						}

						_panel = Resources.FindObjectsOfTypeAll<VRCSdkControlPanel>().FirstOrDefault();
			}

					if (_panel != null)
			{
						VRCSdkControlPanel.OnSdkPanelDisable += HandlePanelClosed;
			}
				}

				if (_panel == null)
					return;

				if (!VRCSdkControlPanel.TryGetBuilder(out IVRCSdkAvatarBuilderApi builder))
				{
					Debug.LogWarning("[AvatarUploader] Control Panel did not expose IVRCSdkAvatarBuilderApi yet.");
					return;
				}

				_builder = builder;

				if (focusPanel)
					_panel.Focus();

				var state = new BridgeState(_panel, _builder);

				foreach (var callback in PendingCallbacks.ToArray())
				{
					try { callback?.Invoke(state); }
					catch (Exception ex) { Debug.LogException(ex); }
				}
				PendingCallbacks.Clear();

				foreach (var waiter in Awaiters.ToArray())
				{
					waiter.TrySetResult(state);
				}
				Awaiters.Clear();
			}
			finally
			{
				_isAcquiring = false;
			}
		}

		private static void HandlePanelClosed(object sender, EventArgs e)
		{
			_builder = null;
			_panel = null;
			_isAcquiring = false;

			foreach (var waiter in Awaiters.ToArray())
			{
				waiter.TrySetCanceled();
			}
			Awaiters.Clear();
			PendingCallbacks.Clear();
		}

		internal readonly struct BridgeState
		{
			public readonly VRCSdkControlPanel Panel;
			public readonly IVRCSdkAvatarBuilderApi Builder;

			public BridgeState(VRCSdkControlPanel panel, IVRCSdkAvatarBuilderApi builder)
			{
				Panel = panel;
				Builder = builder;
			}
		}
	}
}

