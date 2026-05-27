using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace YUCP.DevTools.Editor.PackageExporter
{
	internal static class HDiffPatchTrust
	{
		private const string HdiffzSha256 = "11493E1D8947E6DA3CCA4A6C4D03AD55DC973D8766CEB25ECCDCA4811B8C0235";
		private const string HpatchzSha256 = "DBCD3320D0889CA894F1C404097A0B73918E13771A3902BC7F7CB99EAD47400E";
		private const string HdiffinfoSha256 = "28A2785870938EE45F98D5BBE6CFD0F0689980BCF3FFCE703E777FCBD5F51294";
		private const string LinuxHdiffzSha256 = "C0EEA910CE6BCC5F3CBFEC05AE67B7AB20ABD028DD852F73F619D2010719CA47";
		private const string LinuxHpatchzSha256 = "06D5D6BDD98502E54CA99FFB9A40BA83C9061881EE4AA944A29F51AF405A194B";
		private const string LinuxHdiffinfoSha256 = "1D98EE01062E6E300015EE2308027B55164F95BDC442B134BD7C6403BC4BAB4B";

		private static readonly string[] WindowsNativeLibraries =
		{
			"hdiffz.dll",
			"hpatchz.dll",
			"hdiffinfo.dll",
		};

		private static readonly string[] LinuxNativeLibraries =
		{
			"libhdiffz.so",
			"libhpatchz.so",
			"libhdiffinfo.so",
		};

		internal static bool IsTrustedNativeLibrary(string fileName, string path)
		{
			if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(path))
				return false;

			string platformFileName = GetNativeLibraryFileName(fileName) ?? fileName;
			string expectedHash = GetExpectedHash(platformFileName);
			if (string.IsNullOrEmpty(expectedHash))
				return false;

			string normalizedPath = Path.GetFullPath(path);
			string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			bool isTrustedPath = GetCandidatePaths(projectPath, platformFileName)
				.Any(candidate => PathsEqual(candidate, normalizedPath));

			return isTrustedPath &&
				   FileMatchesSha256(normalizedPath, expectedHash, out _);
		}

		internal static string EnsureTrustedCopy(string projectPath, string libraryDir, string fileName, string logPrefix)
		{
			fileName = GetNativeLibraryFileName(fileName) ?? fileName;
			string expectedHash = GetExpectedHash(fileName);
			if (string.IsNullOrEmpty(expectedHash))
				throw new InvalidOperationException($"No pinned hash is configured for {fileName}.");

			Directory.CreateDirectory(libraryDir);
			string destinationPath = Path.Combine(libraryDir, fileName);
			string sourcePath = GetTrustedSource(projectPath, fileName, logPrefix);

			if (sourcePath == null)
			{
				if (FileMatchesSha256(destinationPath, expectedHash, out _))
				{
					return destinationPath;
				}

				throw new FileNotFoundException($"{logPrefix} No trusted copy of {fileName} was found.");
			}

			if (!FileMatchesSha256(destinationPath, expectedHash, out _))
			{
				try
				{
					File.Copy(sourcePath, destinationPath, overwrite: true);
					Debug.Log($"{logPrefix} Copied trusted {fileName} from {sourcePath} to {destinationPath}");
				}
				catch
				{
					if (!FileMatchesSha256(destinationPath, expectedHash, out _))
						throw;

					Debug.Log($"{logPrefix} Using existing pinned {fileName} copy at {destinationPath}");
				}
			}

			EnsureFileMatchesSha256(destinationPath, expectedHash, fileName);
			return destinationPath;
		}

		internal static void PrepareLibraries(string projectPath, string libraryDir, string logPrefix)
		{
			foreach (string fileName in GetCurrentNativeLibraryFileNames())
			{
				EnsureTrustedCopy(projectPath, libraryDir, fileName, logPrefix);
			}
		}

		internal static string[] GetCurrentNativeLibraryFileNames()
		{
			return GetNativeLibraryFileNamesForPlatform(Application.platform);
		}

		internal static string[] GetNativeLibraryFileNamesForPlatform(RuntimePlatform platform)
		{
			if (platform == RuntimePlatform.WindowsEditor)
				return WindowsNativeLibraries.ToArray();
			if (platform == RuntimePlatform.LinuxEditor)
				return LinuxNativeLibraries.ToArray();

			throw new PlatformNotSupportedException($"HDiffPatch native libraries are only supported in the Unity Editor on Windows and Linux x86_64. Current platform: {platform}");
		}

		internal static string GetNativeLibraryFileName(string logicalName)
		{
			return GetNativeLibraryFileNameForPlatform(logicalName, Application.platform);
		}

		internal static string GetNativeLibraryFileNameForPlatform(string logicalName, RuntimePlatform platform)
		{
			if (string.IsNullOrWhiteSpace(logicalName))
				return null;

			if (platform != RuntimePlatform.WindowsEditor && platform != RuntimePlatform.LinuxEditor)
				throw new PlatformNotSupportedException($"HDiffPatch native libraries are only supported in the Unity Editor on Windows and Linux x86_64. Current platform: {platform}");

			string normalized = logicalName.Trim().ToLowerInvariant();
			if (normalized == "hdiffz.dll" || normalized == "libhdiffz.so")
				return normalized;
			if (normalized == "hpatchz.dll" || normalized == "libhpatchz.so")
				return normalized;
			if (normalized == "hdiffinfo.dll" || normalized == "libhdiffinfo.so")
				return normalized;

			if (normalized == "hdiffz")
				return platform == RuntimePlatform.LinuxEditor ? "libhdiffz.so" : "hdiffz.dll";
			if (normalized == "hpatchz")
				return platform == RuntimePlatform.LinuxEditor ? "libhpatchz.so" : "hpatchz.dll";
			if (normalized == "hdiffinfo")
				return platform == RuntimePlatform.LinuxEditor ? "libhdiffinfo.so" : "hdiffinfo.dll";

			return null;
		}

		private static string GetTrustedSource(string projectPath, string fileName, string logPrefix)
		{
			foreach (string candidatePath in GetCandidatePaths(projectPath, fileName))
			{
				if (!File.Exists(candidatePath))
					continue;
				if (IsTrustedNativeLibrary(fileName, candidatePath))
					return candidatePath;

				Debug.LogError($"{logPrefix} Refusing to use untrusted {fileName} from {candidatePath}");
			}

			return null;
		}

		private static string[] GetCandidatePaths(string projectPath, string fileName)
		{
			string relativePluginPath = GetPluginRelativePath(fileName);
			return new[]
			{
				Path.Combine(projectPath, "Packages", "com.yucp.temp", relativePluginPath),
				Path.Combine(projectPath, "Packages", "com.yucp.devtools", relativePluginPath),
			};
		}

		internal static string GetPluginRelativePath(string fileName)
		{
			if (fileName != null && fileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
				return Path.Combine("Plugins", "Linux", "x86_64", fileName);

			return Path.Combine("Plugins", fileName ?? string.Empty);
		}

		private static string GetExpectedHash(string fileName)
		{
			switch (fileName?.ToLowerInvariant())
			{
				case "hdiffz.dll":
					return HdiffzSha256;
				case "hpatchz.dll":
					return HpatchzSha256;
				case "hdiffinfo.dll":
					return HdiffinfoSha256;
				case "libhdiffz.so":
					return LinuxHdiffzSha256;
				case "libhpatchz.so":
					return LinuxHpatchzSha256;
				case "libhdiffinfo.so":
					return LinuxHdiffinfoSha256;
				default:
					return null;
			}
		}

		// Keep trust helpers local so the injected YUCP.PatchRuntime copy stays
		// self-contained and does not depend on authoring-only assemblies.
		private static bool PathsEqual(string left, string right)
		{
			if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
				return false;

			string normalizedLeft = Path.GetFullPath(left)
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string normalizedRight = Path.GetFullPath(right)
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
		}

		private static bool FileMatchesSha256(string path, string expectedHash, out string actualHash)
		{
			actualHash = null;
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
				return false;

			string normalizedExpectedHash = NormalizeHash(expectedHash);
			if (string.IsNullOrEmpty(normalizedExpectedHash))
				return false;

			actualHash = ComputeSha256(path);
			return string.Equals(actualHash, normalizedExpectedHash, StringComparison.OrdinalIgnoreCase);
		}

		private static void EnsureFileMatchesSha256(string path, string expectedHash, string description)
		{
			if (FileMatchesSha256(path, expectedHash, out string actualHash))
				return;

			throw new InvalidDataException(
				$"{description} failed pinned SHA-256 validation. Expected {NormalizeHash(expectedHash)}, got {actualHash ?? "<missing>"}.");
		}

		private static string NormalizeHash(string hash)
		{
			if (string.IsNullOrWhiteSpace(hash))
				return null;

			return hash.Trim().Replace("-", string.Empty).ToUpperInvariant();
		}

		private static string ComputeSha256(string path)
		{
			using var stream = File.OpenRead(path);
			using var sha256 = SHA256.Create();
			byte[] hash = sha256.ComputeHash(stream);
			return BitConverter.ToString(hash).Replace("-", string.Empty);
		}
	}

	/// <summary>
	/// Initializes DLL copying and search path BEFORE HDiffPatchWrapper is accessed.
	/// This ensures DllImport attributes find the DLLs in Library/YUCP/ instead of Plugins/.
	/// </summary>
	[InitializeOnLoad]
	internal static class HDiffPatchDllInitializer
	{
		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool SetDllDirectory(string lpPathName);

		static HDiffPatchDllInitializer()
		{
			try
			{
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				string libraryDir = Path.Combine(projectPath, "Library", "YUCP");
				HDiffPatchTrust.PrepareLibraries(projectPath, libraryDir, "[HDiffPatchDllInitializer]");
				
				if (Application.platform == RuntimePlatform.WindowsEditor)
				{
					// Set DLL directory BEFORE any DllImport attributes try to resolve.
					SetDllDirectory(libraryDir);
					Debug.Log($"[HDiffPatchDllInitializer] Set DLL directory to {libraryDir}");
				}
			}
			catch (PlatformNotSupportedException ex)
			{
				Debug.LogWarning($"[HDiffPatchDllInitializer] {ex.Message}");
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[HDiffPatchDllInitializer] Error initializing: {ex.Message}");
			}
		}
	}
	
	/// <summary>
	/// Wrapper for HDiffPatch DLLs (hdiffz.dll and hpatchz.dll).
	/// Adapted from CocoTools implementation: https://github.com/coco1337/CocoTools
	/// Original HDiffPatch by Sisong: https://github.com/sisong/HDiffPatch
	/// </summary>
	public static class HDiffPatchWrapper
	{
		// Windows API for loading DLLs
		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern IntPtr LoadLibrary(string lpFileName);

		[DllImport("kernel32", SetLastError = true)]
		private static extern bool FreeLibrary(IntPtr hModule);

		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool SetDllDirectory(string lpPathName);

		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
		private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

		private const int RtldNow = 2;

		[DllImport("libdl.so.2", CharSet = CharSet.Ansi)]
		private static extern IntPtr dlopen(string fileName, int flags);

		[DllImport("libdl.so.2", CharSet = CharSet.Ansi)]
		private static extern IntPtr dlsym(IntPtr handle, string symbol);

		[DllImport("libdl.so.2")]
		private static extern int dlclose(IntPtr handle);

		[DllImport("libdl.so.2")]
		private static extern IntPtr dlerror();

		private static bool s_dllsLoaded = false;
		private static IntPtr s_hdiffzHandle = IntPtr.Zero;
		private static IntPtr s_hpatchzHandle = IntPtr.Zero;
		private static IntPtr s_hdiffinfoHandle = IntPtr.Zero;
		private static RegisterDelegateHdiffzNative s_registerDelegateHdiffz;
		private static RegisterErrorDelegateHdiffzNative s_registerErrorDelegateHdiffz;
		private static HDiffUnityNative s_hdiffUnity;
		private static RegisterDelegateHpatchzNative s_registerDelegateHpatchz;
		private static RegisterErrorDelegateHpatchzNative s_registerErrorDelegateHpatchz;
		private static HPatchUnityNative s_hpatchUnity;
		private static HDiffGetInfoNative s_hdiffGetInfo;
		
		/// <summary>
		/// Static constructor to copy DLLs and set search path BEFORE any DllImport usage.
		/// This ensures Windows finds the DLLs in Library/YUCP/ instead of Plugins/.
		/// </summary>
		static HDiffPatchWrapper()
		{
			try
			{
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				string libraryDir = Path.Combine(projectPath, "Library", "YUCP");
				HDiffPatchTrust.PrepareLibraries(projectPath, libraryDir, "[HDiffPatchWrapper]");
				
				if (Application.platform == RuntimePlatform.WindowsEditor)
				{
					// Set DLL directory BEFORE any DllImport attributes try to resolve.
					SetDllDirectory(libraryDir);
					Debug.Log($"[HDiffPatchWrapper] Set DLL directory to {libraryDir} (static constructor)");
				}
			}
			catch (PlatformNotSupportedException ex)
			{
				Debug.LogWarning($"[HDiffPatchWrapper] {ex.Message}");
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[HDiffPatchWrapper] Error in static constructor: {ex.Message}");
			}
		}

		internal static bool IsTrustedNativeLibrary(string fileName, string path)
		{
			return HDiffPatchTrust.IsTrustedNativeLibrary(fileName, path);
		}

		internal static string[] GetNativeLibraryFileNamesForPlatform(RuntimePlatform platform)
		{
			return HDiffPatchTrust.GetNativeLibraryFileNamesForPlatform(platform);
		}

		internal static string GetNativeLibraryFileNameForPlatform(string logicalName, RuntimePlatform platform)
		{
			return HDiffPatchTrust.GetNativeLibraryFileNameForPlatform(logicalName, platform);
		}
		
		/// <summary>
		/// Frees the loaded DLL libraries. Call this before cleanup to release file locks.
		/// </summary>
		public static void FreeDlls()
		{
			try
			{
				if (s_hdiffzHandle != IntPtr.Zero)
				{
					UnloadNativeLibrary(s_hdiffzHandle);
					s_hdiffzHandle = IntPtr.Zero;
					Debug.Log("[HDiffPatchWrapper] Freed hdiffz native library");
				}
				
				if (s_hpatchzHandle != IntPtr.Zero)
				{
					UnloadNativeLibrary(s_hpatchzHandle);
					s_hpatchzHandle = IntPtr.Zero;
					Debug.Log("[HDiffPatchWrapper] Freed hpatchz native library");
				}

				if (s_hdiffinfoHandle != IntPtr.Zero)
				{
					UnloadNativeLibrary(s_hdiffinfoHandle);
					s_hdiffinfoHandle = IntPtr.Zero;
					Debug.Log("[HDiffPatchWrapper] Freed hdiffinfo native library");
				}

				s_registerDelegateHdiffz = null;
				s_registerErrorDelegateHdiffz = null;
				s_hdiffUnity = null;
				s_registerDelegateHpatchz = null;
				s_registerErrorDelegateHpatchz = null;
				s_hpatchUnity = null;
				s_hdiffGetInfo = null;
				
				if (Application.platform == RuntimePlatform.WindowsEditor)
				{
					SetDllDirectory(null);
				}
				
				s_dllsLoaded = false;
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[HDiffPatchWrapper] Error freeing DLLs: {ex.Message}");
			}
		}

		/// <summary>
		/// Ensures the HDiffPatch DLLs are loaded before use.
		/// Copies DLLs to Library/YUCP/ on first use, creating unique copies if needed to avoid file locks.
		/// This is necessary because P/Invoke doesn't automatically search Unity's Packages folder.
		/// </summary>
		private static void EnsureDllsLoaded()
		{
			// If DLLs are already loaded, skip
			if (s_dllsLoaded && s_hdiffzHandle != IntPtr.Zero && s_hpatchzHandle != IntPtr.Zero && s_hdiffinfoHandle != IntPtr.Zero)
				return;

			try
			{
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				string libraryDir = Path.Combine(projectPath, "Library", "YUCP");
				Directory.CreateDirectory(libraryDir);

				if (Application.platform == RuntimePlatform.WindowsEditor)
				{
					SetDllDirectory(libraryDir);
				}

				string hdiffzDest = HDiffPatchTrust.EnsureTrustedCopy(projectPath, libraryDir, "hdiffz", "[HDiffPatchWrapper]");
				string hpatchzDest = HDiffPatchTrust.EnsureTrustedCopy(projectPath, libraryDir, "hpatchz", "[HDiffPatchWrapper]");
				string hdiffinfoDest = HDiffPatchTrust.EnsureTrustedCopy(projectPath, libraryDir, "hdiffinfo", "[HDiffPatchWrapper]");

				if (s_hdiffzHandle == IntPtr.Zero)
					s_hdiffzHandle = LoadNativeLibrary(hdiffzDest);
				if (s_hpatchzHandle == IntPtr.Zero)
					s_hpatchzHandle = LoadNativeLibrary(hpatchzDest);
				if (s_hdiffinfoHandle == IntPtr.Zero)
					s_hdiffinfoHandle = LoadNativeLibrary(hdiffinfoDest);

				BindRuntimeExports();
				s_dllsLoaded = true;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[HDiffPatchWrapper] Error loading DLLs: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
		}

		private static void BindRuntimeExports()
		{
			if (s_hdiffzHandle != IntPtr.Zero)
			{
				if (s_registerDelegateHdiffz == null)
					s_registerDelegateHdiffz = GetRequiredExport<RegisterDelegateHdiffzNative>(s_hdiffzHandle, "RegisterDelegate");
				if (s_registerErrorDelegateHdiffz == null)
					s_registerErrorDelegateHdiffz = GetRequiredExport<RegisterErrorDelegateHdiffzNative>(s_hdiffzHandle, "RegisterErrorDelegate");
				if (s_hdiffUnity == null)
					s_hdiffUnity = GetRequiredExport<HDiffUnityNative>(s_hdiffzHandle, "hdiff_unity");
			}

			if (s_hpatchzHandle != IntPtr.Zero)
			{
				if (s_registerDelegateHpatchz == null)
					s_registerDelegateHpatchz = GetRequiredExport<RegisterDelegateHpatchzNative>(s_hpatchzHandle, "RegisterDelegate");
				if (s_registerErrorDelegateHpatchz == null)
					s_registerErrorDelegateHpatchz = GetRequiredExport<RegisterErrorDelegateHpatchzNative>(s_hpatchzHandle, "RegisterErrorDelegate");
				if (s_hpatchUnity == null)
					s_hpatchUnity = GetRequiredExport<HPatchUnityNative>(s_hpatchzHandle, "hpatch_unity");
			}

			if (s_hdiffinfoHandle != IntPtr.Zero && s_hdiffGetInfo == null)
				s_hdiffGetInfo = GetRequiredExport<HDiffGetInfoNative>(s_hdiffinfoHandle, "hdiff_get_info");
		}

		private static IntPtr LoadNativeLibrary(string path)
		{
			string fullPath = Path.GetFullPath(path);
			if (!File.Exists(fullPath))
				throw new FileNotFoundException($"Native library was not found: {fullPath}");

			IntPtr handle;
			if (Application.platform == RuntimePlatform.WindowsEditor)
			{
				handle = LoadLibrary(fullPath);
				if (handle == IntPtr.Zero)
				{
					int error = Marshal.GetLastWin32Error();
					throw new DllNotFoundException($"Failed to load native library from {fullPath}. Win32 error: {error}");
				}
			}
			else if (Application.platform == RuntimePlatform.LinuxEditor)
			{
				handle = dlopen(fullPath, RtldNow);
				if (handle == IntPtr.Zero)
				{
					throw new DllNotFoundException($"Failed to load native library from {fullPath}. dlerror: {GetDlErrorMessage()}");
				}
			}
			else
			{
				throw new PlatformNotSupportedException($"HDiffPatch native loading is not supported on {Application.platform}. This change supports Windows Editor and Linux x86_64 Editor only.");
			}

			Debug.Log($"[HDiffPatchWrapper] Successfully loaded native library from {fullPath}");
			return handle;
		}

		private static void UnloadNativeLibrary(IntPtr handle)
		{
			if (handle == IntPtr.Zero)
				return;

			if (Application.platform == RuntimePlatform.WindowsEditor)
			{
				FreeLibrary(handle);
			}
			else if (Application.platform == RuntimePlatform.LinuxEditor)
			{
				dlclose(handle);
			}
		}

		private static T GetRequiredExport<T>(IntPtr moduleHandle, string exportName) where T : class
		{
			if (moduleHandle == IntPtr.Zero)
				throw new InvalidOperationException($"Cannot resolve export '{exportName}' because the module handle is not loaded.");

			IntPtr exportHandle;
			if (Application.platform == RuntimePlatform.WindowsEditor)
			{
				exportHandle = GetProcAddress(moduleHandle, exportName);
			}
			else if (Application.platform == RuntimePlatform.LinuxEditor)
			{
				exportHandle = dlsym(moduleHandle, exportName);
			}
			else
			{
				throw new PlatformNotSupportedException($"Native export binding is not supported for '{exportName}' on {Application.platform}.");
			}

			if (exportHandle == IntPtr.Zero)
			{
				int error = Marshal.GetLastWin32Error();
				string detail = Application.platform == RuntimePlatform.LinuxEditor ? GetDlErrorMessage() : error.ToString();
				throw new InvalidOperationException($"Failed to resolve export '{exportName}'. Error: {detail}");
			}

			return (T)(object)Marshal.GetDelegateForFunctionPointer(exportHandle, typeof(T));
		}

		private static string GetDlErrorMessage()
		{
			IntPtr error = dlerror();
			return error == IntPtr.Zero ? "<none>" : Marshal.PtrToStringAnsi(error);
		}

		private static void EnsurePatchExportsLoaded()
		{
			EnsureDllsLoaded();

			if (s_registerDelegateHpatchz == null || s_registerErrorDelegateHpatchz == null || s_hpatchUnity == null)
				throw new InvalidOperationException("hpatchz exports are not available.");
		}

		private static void EnsureDiffExportsLoaded()
		{
			EnsureDllsLoaded();

			if (s_registerDelegateHdiffz == null || s_registerErrorDelegateHdiffz == null || s_hdiffUnity == null)
				throw new InvalidOperationException("hdiffz exports are not available.");
		}

		private static void EnsureDiffInfoExportLoaded()
		{
			EnsureDllsLoaded();

			if (s_hdiffGetInfo == null)
				throw new InvalidOperationException("hdiffinfo exports are not available.");
		}
		#region HDiff (Diff Creation)
		
		/// <summary>
		/// Delegate for HDiff log output.
		/// Adapted from CocoTools CocoDiff.cs
		/// </summary>
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void HDiffzStringOutput(string str);
		
		/// <summary>
		/// Register delegate for HDiff log output.
		/// Adapted from CocoTools CocoDiff.cs
		/// </summary>
		public static void RegisterDelegateHdiffz(HDiffzStringOutput del)
		{
			EnsureDiffExportsLoaded();
			s_registerDelegateHdiffz(del);
		}

		/// <summary>
		/// Register delegate for HDiff error output.
		/// Adapted from CocoTools CocoDiff.cs
		/// </summary>
		public static void RegisterErrorDelegateHdiffz(HDiffzStringOutput del)
		{
			EnsureDiffExportsLoaded();
			s_registerErrorDelegateHdiffz(del);
		}

		/// <summary>
		/// Create a binary diff file from old and new FBX files.
		/// Adapted from CocoTools CocoDiff.cs hdiff_unity function
		/// </summary>
		/// <param name="oldFileName">Path to the original/base FBX file</param>
		/// <param name="newFileName">Path to the modified FBX file</param>
		/// <param name="outDiffFileName">Output path for the .hdiff file</param>
		/// <param name="diffOptions">Array of diff options (e.g., ["-m-6", "-SD", "-c-zstd-21-24", "-d"])</param>
		/// <param name="diffOptionSize">Number of options in the array</param>
		/// <returns>THDiffResult indicating success or error type</returns>
		public static int hdiff_unity(string oldFileName, string newFileName, string outDiffFileName,
			string[] diffOptions, int diffOptionSize)
		{
			EnsureDiffExportsLoaded();
			return s_hdiffUnity(oldFileName, newFileName, outDiffFileName, diffOptions, diffOptionSize);
		}

		#endregion

		#region HPatch (Patch Application)
		
		/// <summary>
		/// Delegate for HPatch log output.
		/// Adapted from CocoTools CocoPatch.cs
		/// </summary>
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void HPatchzStringOutput(string str);

		/// <summary>
		/// Register delegate for HPatch log output.
		/// Adapted from CocoTools CocoPatch.cs
		/// </summary>
		private static void RegisterDelegateHpatchz(HPatchzStringOutput del)
		{
			EnsurePatchExportsLoaded();
			s_registerDelegateHpatchz(del);
		}

		/// <summary>
		/// Register delegate for HPatch error output.
		/// Adapted from CocoTools CocoPatch.cs
		/// </summary>
		private static void RegisterErrorDelegateHpatchz(HPatchzStringOutput del)
		{
			EnsurePatchExportsLoaded();
			s_registerErrorDelegateHpatchz(del);
		}

		/// <summary>
		/// Apply a binary patch to an FBX file.
		/// Adapted from CocoTools CocoPatch.cs hpatch_unity function
		/// </summary>
		/// <param name="optionCount">Number of patch options</param>
		/// <param name="options">Array of patch options (usually empty)</param>
		/// <param name="oldPath">Path to the original/base FBX file</param>
		/// <param name="diffFileName">Path to the .hdiff file</param>
		/// <param name="outNewPath">Output path for the patched FBX file</param>
		/// <returns>THPatchResult indicating success or error type</returns>
		private static int hpatch_unity(int optionCount, string[] options, string oldPath,
			string diffFileName, string outNewPath)
		{
			EnsurePatchExportsLoaded();
			return s_hpatchUnity(optionCount, options, oldPath, diffFileName, outNewPath);
		}

		#endregion

		#region HDiff Info (Patch Header Read)
		
		/// <summary>
		/// Reads compressed diff info (old/new sizes, compression type) from a .hdiff file.
		/// Implemented by hdiffinfo.dll wrapper built from HDiffPatch sources.
		/// </summary>
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void RegisterDelegateHdiffzNative(HDiffzStringOutput del);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void RegisterErrorDelegateHdiffzNative(HDiffzStringOutput del);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		private delegate int HDiffUnityNative(
			string oldFileName,
			string newFileName,
			string outDiffFileName,
			[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] diffOptions,
			int diffOptionSize);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void RegisterDelegateHpatchzNative(HPatchzStringOutput del);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void RegisterErrorDelegateHpatchzNative(HPatchzStringOutput del);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		private delegate int HPatchUnityNative(
			int optionCount,
			[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] options,
			string oldPath,
			string diffFileName,
			string outNewPath);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		private delegate int HDiffGetInfoNative(
			string diffFileName,
			out ulong oldSize,
			out ulong newSize,
			StringBuilder compressType,
			int compressTypeCap);
		
		#endregion

		#region High-Level API

		/// <summary>
		/// Default diff options used by CocoTools.
		/// Adapted from CocoTools CocoDiff.cs DEFAULT_COMMAND
		/// </summary>
		private const string DEFAULT_DIFF_OPTIONS = "-m-6 -SD -c-zstd-21-24 -d";

		// Store callbacks for delegate wrappers
		private static Action<string> s_hdiffLogCallback;
		private static Action<string> s_hdiffErrorCallback;
		private static Action<string> s_hpatchLogCallback;
		private static Action<string> s_hpatchErrorCallback;

		// Wrapper methods that match the delegate signatures
		private static void HDiffLogWrapper(string str)
		{
			s_hdiffLogCallback?.Invoke(str);
		}

		private static void HDiffErrorWrapper(string str)
		{
			s_hdiffErrorCallback?.Invoke(str);
		}

		private static void HPatchLogWrapper(string str)
		{
			s_hpatchLogCallback?.Invoke(str);
		}

		private static void HPatchErrorWrapper(string str)
		{
			s_hpatchErrorCallback?.Invoke(str);
		}

		/// <summary>
		/// Create a binary diff file from base and modified FBX files.
		/// </summary>
		/// <param name="baseFbxPath">Path to the base/original FBX file</param>
		/// <param name="modifiedFbxPath">Path to the modified FBX file</param>
		/// <param name="hdiffOutputPath">Output path for the .hdiff file</param>
		/// <param name="logCallback">Optional callback for log messages</param>
		/// <param name="errorCallback">Optional callback for error messages</param>
		/// <returns>THDiffResult indicating success or error type</returns>
		public static THDiffResult CreateDiff(string baseFbxPath, string modifiedFbxPath, string hdiffOutputPath,
			Action<string> logCallback = null, Action<string> errorCallback = null)
		{
			// Ensure DLLs are loaded before use
			EnsureDllsLoaded();

			s_hdiffLogCallback = logCallback;
			s_hdiffErrorCallback = errorCallback;

			if (logCallback != null)
				RegisterDelegateHdiffz(HDiffLogWrapper);
			if (errorCallback != null)
				RegisterErrorDelegateHdiffz(HDiffErrorWrapper);

			var options = DEFAULT_DIFF_OPTIONS.Split(' ');
			var result = (THDiffResult)hdiff_unity(baseFbxPath, modifiedFbxPath, hdiffOutputPath, options, options.Length);

			// Clear callbacks after use
			s_hdiffLogCallback = null;
			s_hdiffErrorCallback = null;

			return result;
		}

		/// <summary>
		/// Apply a binary patch to a base FBX file.
		/// </summary>
		/// <param name="baseFbxPath">Path to the base/original FBX file</param>
		/// <param name="hdiffPath">Path to the .hdiff file</param>
		/// <param name="outputFbxPath">Output path for the patched FBX file</param>
		/// <param name="logCallback">Optional callback for log messages</param>
		/// <param name="errorCallback">Optional callback for error messages</param>
		/// <returns>THPatchResult indicating success or error type</returns>
		public static THPatchResult ApplyPatch(string baseFbxPath, string hdiffPath, string outputFbxPath,
			Action<string> logCallback = null, Action<string> errorCallback = null)
		{
			EnsurePatchExportsLoaded();

			s_hpatchLogCallback = logCallback;
			s_hpatchErrorCallback = errorCallback;

			if (logCallback != null)
				s_registerDelegateHpatchz(HPatchLogWrapper);
			if (errorCallback != null)
				s_registerErrorDelegateHpatchz(HPatchErrorWrapper);

			var options = new string[0];
			var result = (THPatchResult)s_hpatchUnity(0, options, baseFbxPath, hdiffPath, outputFbxPath);

			// Clear callbacks after use
			s_hpatchLogCallback = null;
			s_hpatchErrorCallback = null;

			return result;
		}
		
		/// <summary>
		/// Reads diff header info from a .hdiff file.
		/// </summary>
		public static bool TryGetDiffInfo(string hdiffPath, out ulong oldSize, out ulong newSize, out string compressType)
		{
			oldSize = 0;
			newSize = 0;
			compressType = string.Empty;
			
			EnsureDiffInfoExportLoaded();
			
			var sb = new StringBuilder(260);
			int result = s_hdiffGetInfo(hdiffPath, out oldSize, out newSize, sb, sb.Capacity);
			if (result != 0)
			{
				return false;
			}
			
			compressType = sb.ToString();
			return true;
		}

		#endregion
	}

	/// <summary>
	/// HDiff result codes.
	/// Adapted from CocoTools CocoDiff.cs THDiffResult enum
	/// </summary>
	public enum THDiffResult : int
	{
		HDIFF_SUCCESS = 0,
		HDIFF_OPTIONS_ERROR,
		HDIFF_OPENREAD_ERROR,
		HDIFF_OPENWRITE_ERROR,
		HDIFF_FILECLOSE_ERROR,
		HDIFF_MEM_ERROR,
		HDIFF_DIFF_ERROR,
		HDIFF_PATCH_ERROR,
		HDIFF_RESAVE_FILEREAD_ERROR,
		HDIFF_RESAVE_DIFFINFO_ERROR,
		HDIFF_RESAVE_COMPRESSTYPE_ERROR,
		HDIFF_RESAVE_ERROR,
		HDIFF_RESAVE_CHECKSUMTYPE_ERROR,
		HDIFF_PATHTYPE_ERROR,
		HDIFF_TEMPPATH_ERROR,
		HDIFF_DELETEPATH_ERROR,
		HDIFF_RENAMEPATH_ERROR,
		DIRDIFF_DIFF_ERROR = 101,
		DIRDIFF_PATCH_ERROR,
		MANIFEST_CREATE_ERROR,
		MANIFEST_TEST_ERROR,
	}

	/// <summary>
	/// HPatch result codes.
	/// Adapted from CocoTools CocoPatch.cs THPatchResult enum
	/// </summary>
	public enum THPatchResult : int
	{
		HPATCH_SUCCESS = 0,
		HPATCH_OPTIONS_ERROR = 1,
		HPATCH_OPENREAD_ERROR,
		HPATCH_OPENWRITE_ERROR,
		HPATCH_FILEREAD_ERROR,
		HPATCH_FILEWRITE_ERROR,
		HPATCH_FILEDATA_ERROR,
		HPATCH_FILECLOSE_ERROR,
		HPATCH_MEM_ERROR,
		HPATCH_HDIFFINFO_ERROR,
		HPATCH_COMPRESSTYPE_ERROR,
		HPATCH_HPATCH_ERROR,
		HPATCH_PATHTYPE_ERROR,
		HPATCH_TEMPPATH_ERROR,
		HPATCH_DELETEPATH_ERROR,
		HPATCH_RENAMEPATH_ERROR,
		HPATCH_SPATCH_ERROR,
		HPATCH_BSPATCH_ERROR,
		HPATCH_VCPATCH_ERROR,
		HPATCH_DECOMPRESSER_OPEN_ERROR = 20,
		HPATCH_DECOMPRESSER_CLOSE_ERROR,
		HPATCH_DECOMPRESSER_MEM_ERROR,
		HPATCH_DECOMPRESSER_DECOMPRESS_ERROR,
		HPATCH_FILEWRITE_NO_SPACE_ERROR,
	}
}
