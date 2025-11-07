using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	public class AvatarUploaderSettings : ScriptableObject
	{
		private const string SettingsAssetPath = "Assets/Editor/YUCPAvatarToolsSettings.asset";
		private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("YUCP.AvatarTools.APIKeyEntropy");

		private static AvatarUploaderSettings _instance;
		public static AvatarUploaderSettings Instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = LoadOrCreateSettings();
				}
				return _instance;
			}
		}

		[SerializeField] private bool autoUploadAfterBuild = false;
		[SerializeField] private bool showBuildNotifications = true;
		[SerializeField] private bool showUploadNotifications = true;
		[SerializeField] private bool defaultBuildPC = true;
		[SerializeField] private bool defaultBuildQuest = true;
		[SerializeField] private int maxLogEntries = 1000;
		[SerializeField] private bool saveBuildLogs = true;
		[SerializeField] private bool saveUploadLogs = true;
		[SerializeField] private bool enableParallelBuilds = false;
		[SerializeField] private int maxParallelBuilds = 2;
		[SerializeField] private bool enableBuildCaching = true;
		[SerializeField] private bool enableGalleryIntegration = false;
		[SerializeField] private string apiKeyCipher = string.Empty;

		public bool AutoUploadAfterBuild { get => autoUploadAfterBuild; set => autoUploadAfterBuild = value; }
		public bool ShowBuildNotifications { get => showBuildNotifications; set => showBuildNotifications = value; }
		public bool ShowUploadNotifications { get => showUploadNotifications; set => showUploadNotifications = value; }
		public bool DefaultBuildPC { get => defaultBuildPC; set => defaultBuildPC = value; }
		public bool DefaultBuildQuest { get => defaultBuildQuest; set => defaultBuildQuest = value; }
		public int MaxLogEntries { get => maxLogEntries; set => maxLogEntries = Mathf.Clamp(value, 100, 5000); }
		public bool SaveBuildLogs { get => saveBuildLogs; set => saveBuildLogs = value; }
		public bool SaveUploadLogs { get => saveUploadLogs; set => saveUploadLogs = value; }
		public bool EnableParallelBuilds { get => enableParallelBuilds; set => enableParallelBuilds = value; }
		public int MaxParallelBuilds { get => maxParallelBuilds; set => maxParallelBuilds = Mathf.Clamp(value, 1, 8); }
		public bool EnableBuildCaching { get => enableBuildCaching; set => enableBuildCaching = value; }
		public bool EnableGalleryIntegration { get => enableGalleryIntegration; set => enableGalleryIntegration = value; }

		public bool HasStoredApiKey => !string.IsNullOrEmpty(apiKeyCipher);

		public void ClearApiKey()
		{
			apiKeyCipher = string.Empty;
			MarkDirty();
		}

		public void SetApiKey(string apiKey)
		{
			if (string.IsNullOrEmpty(apiKey))
			{
				apiKeyCipher = string.Empty;
			}
			else
			{
				try
				{
					var data = Encoding.UTF8.GetBytes(apiKey);
					var encrypted = Protect(data, Entropy);
					apiKeyCipher = Convert.ToBase64String(encrypted);
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"[AvatarTools] Unable to store API key securely: {ex.Message}");
					apiKeyCipher = string.Empty;
				}
			}
			MarkDirty();
		}

		public string GetApiKey()
		{
			if (string.IsNullOrEmpty(apiKeyCipher))
				return string.Empty;

			try
			{
				var data = Convert.FromBase64String(apiKeyCipher);
				var decrypted = Unprotect(data, Entropy);
				return Encoding.UTF8.GetString(decrypted);
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarTools] Unable to read stored API key: {ex.Message}");
				return string.Empty;
			}
		}

		public void Save()
		{
			MarkDirty();
		}

		private static AvatarUploaderSettings LoadOrCreateSettings()
		{
			var normalizedPath = SettingsAssetPath.Replace("\\", "/");
			var settings = AssetDatabase.LoadAssetAtPath<AvatarUploaderSettings>(normalizedPath);
			if (settings != null)
				return settings;

			var dir = System.IO.Path.GetDirectoryName(normalizedPath)?.Replace("\\", "/");
			if (!AssetDatabase.IsValidFolder(dir))
			{
				var segments = dir.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);
				var path = segments[0];
				for (int i = 1; i < segments.Length; i++)
				{
					var next = path + "/" + segments[i];
					if (!AssetDatabase.IsValidFolder(next))
					{
						AssetDatabase.CreateFolder(path, segments[i]);
					}
					path = next;
				}
			}

			settings = CreateInstance<AvatarUploaderSettings>();
			AssetDatabase.CreateAsset(settings, normalizedPath);
			AssetDatabase.SaveAssets();
			return settings;
		}

		private void MarkDirty()
		{
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
		}

		private static byte[] Protect(byte[] data, byte[] entropy)
		{
			var dataIn = new DATA_BLOB();
			var entropyBlob = new DATA_BLOB();
			var dataOut = new DATA_BLOB();

			try
			{
				dataIn.cbData = data.Length;
				dataIn.pbData = Marshal.AllocHGlobal(data.Length);
				Marshal.Copy(data, 0, dataIn.pbData, data.Length);

				entropyBlob.cbData = entropy.Length;
				entropyBlob.pbData = Marshal.AllocHGlobal(entropy.Length);
				Marshal.Copy(entropy, 0, entropyBlob.pbData, entropy.Length);

				if (!CryptProtectData(ref dataIn, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, ref dataOut))
					throw new InvalidOperationException("CryptProtectData failed");

				var result = new byte[dataOut.cbData];
				Marshal.Copy(dataOut.pbData, result, 0, dataOut.cbData);
				return result;
			}
			finally
			{
				if (dataIn.pbData != IntPtr.Zero) Marshal.FreeHGlobal(dataIn.pbData);
				if (entropyBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(entropyBlob.pbData);
				if (dataOut.pbData != IntPtr.Zero) Marshal.FreeHGlobal(dataOut.pbData);
			}
		}

		private static byte[] Unprotect(byte[] data, byte[] entropy)
		{
			var dataIn = new DATA_BLOB();
			var entropyBlob = new DATA_BLOB();
			var dataOut = new DATA_BLOB();

			try
			{
				dataIn.cbData = data.Length;
				dataIn.pbData = Marshal.AllocHGlobal(data.Length);
				Marshal.Copy(data, 0, dataIn.pbData, data.Length);

				entropyBlob.cbData = entropy.Length;
				entropyBlob.pbData = Marshal.AllocHGlobal(entropy.Length);
				Marshal.Copy(entropy, 0, entropyBlob.pbData, entropy.Length);

				if (!CryptUnprotectData(ref dataIn, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, ref dataOut))
					throw new InvalidOperationException("CryptUnprotectData failed");

				var result = new byte[dataOut.cbData];
				Marshal.Copy(dataOut.pbData, result, 0, dataOut.cbData);
				return result;
			}
			finally
			{
				if (dataIn.pbData != IntPtr.Zero) Marshal.FreeHGlobal(dataIn.pbData);
				if (entropyBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(entropyBlob.pbData);
				if (dataOut.pbData != IntPtr.Zero) Marshal.FreeHGlobal(dataOut.pbData);
			}
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct DATA_BLOB
		{
			public int cbData;
			public IntPtr pbData;
		}

		[DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, ref DATA_BLOB pOptionalEntropy,
			IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

		[DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, string szDataDescr, ref DATA_BLOB pOptionalEntropy,
			IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);
	}
}

