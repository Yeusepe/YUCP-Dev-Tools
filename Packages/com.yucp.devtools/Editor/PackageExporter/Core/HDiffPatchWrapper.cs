using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Initializes DLL copying and search path BEFORE HDiffPatchWrapper is accessed.
	/// This ensures DllImport attributes find the DLLs in Library/YUCP/ instead of Plugins/.
	/// </summary>
	[InitializeOnLoad]
	internal static class HDiffPatchDllInitializer
	{
#if UNITY_EDITOR_WIN
		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool SetDllDirectory(string lpPathName);
#endif

		static HDiffPatchDllInitializer()
		{
#if UNITY_EDITOR_WIN
			try
			{
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				string libraryDir = Path.Combine(projectPath, "Library", "YUCP");
				
				// Ensure directory exists
				Directory.CreateDirectory(libraryDir);
				
				// Source locations to check
				string[] hdiffzSources = new string[]
				{
					Path.Combine(projectPath, "Packages", "com.yucp.temp", "Plugins", "hdiffz.dll"),
					Path.Combine(projectPath, "Packages", "com.yucp.devtools", "Plugins", "hdiffz.dll")
				};
				
				string[] hpatchzSources = new string[]
				{
					Path.Combine(projectPath, "Packages", "com.yucp.temp", "Plugins", "hpatchz.dll"),
					Path.Combine(projectPath, "Packages", "com.yucp.devtools", "Plugins", "hpatchz.dll")
				};
				
				string[] hdiffinfoSources = new string[]
				{
					Path.Combine(projectPath, "Packages", "com.yucp.temp", "Plugins", "hdiffinfo.dll"),
					Path.Combine(projectPath, "Packages", "com.yucp.devtools", "Plugins", "hdiffinfo.dll")
				};
				string hdiffzDest = Path.Combine(libraryDir, "hdiffz.dll");
				string hpatchzDest = Path.Combine(libraryDir, "hpatchz.dll");
				string hdiffinfoDest = Path.Combine(libraryDir, "hdiffinfo.dll");
				
				// Copy hdiffz.dll if source exists and destination doesn't or is older
				foreach (var source in hdiffzSources)
				{
					if (File.Exists(source))
					{
						try
						{
							if (!File.Exists(hdiffzDest) || File.GetLastWriteTime(source) > File.GetLastWriteTime(hdiffzDest))
							{
								File.Copy(source, hdiffzDest, overwrite: true);
								Debug.Log($"[HDiffPatchDllInitializer] Copied hdiffz.dll to {hdiffzDest}");
							}
							break;
						}
						catch (Exception ex)
						{
							Debug.LogWarning($"[HDiffPatchDllInitializer] Could not copy hdiffz.dll: {ex.Message}");
						}
					}
				}
				
				// Copy hpatchz.dll if source exists and destination doesn't or is older
				foreach (var source in hpatchzSources)
				{
					if (File.Exists(source))
					{
						try
						{
							if (!File.Exists(hpatchzDest) || File.GetLastWriteTime(source) > File.GetLastWriteTime(hpatchzDest))
							{
								File.Copy(source, hpatchzDest, overwrite: true);
								Debug.Log($"[HDiffPatchDllInitializer] Copied hpatchz.dll to {hpatchzDest}");
							}
							break;
						}
						catch (Exception ex)
						{
							Debug.LogWarning($"[HDiffPatchDllInitializer] Could not copy hpatchz.dll: {ex.Message}");
						}
					}
				}
				
				// Copy hdiffinfo.dll if source exists and destination doesn't or is older
				foreach (var source in hdiffinfoSources)
				{
					if (File.Exists(source))
					{
						try
						{
							if (!File.Exists(hdiffinfoDest) || File.GetLastWriteTime(source) > File.GetLastWriteTime(hdiffinfoDest))
							{
								File.Copy(source, hdiffinfoDest, overwrite: true);
								Debug.Log($"[HDiffPatchDllInitializer] Copied hdiffinfo.dll to {hdiffinfoDest}");
							}
							break;
						}
						catch (Exception ex)
						{
							Debug.LogWarning($"[HDiffPatchDllInitializer] Could not copy hdiffinfo.dll: {ex.Message}");
						}
					}
				}
				
				// Set DLL directory BEFORE any DllImport attributes try to resolve
				SetDllDirectory(libraryDir);
				Debug.Log($"[HDiffPatchDllInitializer] Set DLL directory to {libraryDir}");
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[HDiffPatchDllInitializer] Error initializing: {ex.Message}");
			}
#endif
		}
	}
	
	/// <summary>
	/// Wrapper for HDiffPatch DLLs (hdiffz.dll and hpatchz.dll).
	/// Adapted from CocoTools implementation: https://github.com/coco1337/CocoTools
	/// Original HDiffPatch by Sisong: https://github.com/sisong/HDiffPatch
	/// </summary>
	public static class HDiffPatchWrapper
	{
#if UNITY_EDITOR_WIN
		// Windows API for loading DLLs
		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern IntPtr LoadLibrary(string lpFileName);

		[DllImport("kernel32", SetLastError = true)]
		private static extern bool FreeLibrary(IntPtr hModule);

		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool SetDllDirectory(string lpPathName);
#endif

		private static bool s_dllsLoaded = false;
		private static IntPtr s_hdiffzHandle = IntPtr.Zero;
		private static IntPtr s_hpatchzHandle = IntPtr.Zero;
		
		/// <summary>
		/// Static constructor to copy DLLs and set search path BEFORE any DllImport usage.
		/// This ensures Windows finds the DLLs in Library/YUCP/ instead of Plugins/.
		/// </summary>
		static HDiffPatchWrapper()
		{
#if UNITY_EDITOR_WIN
			try
			{
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				string libraryDir = Path.Combine(projectPath, "Library", "YUCP");
				
				// Ensure directory exists
				Directory.CreateDirectory(libraryDir);
				
				// Source locations to check
				string[] hdiffzSources = new string[]
				{
					Path.Combine(projectPath, "Packages", "com.yucp.temp", "Plugins", "hdiffz.dll"),
					Path.Combine(projectPath, "Packages", "com.yucp.devtools", "Plugins", "hdiffz.dll")
				};
				
				string[] hpatchzSources = new string[]
				{
					Path.Combine(projectPath, "Packages", "com.yucp.temp", "Plugins", "hpatchz.dll"),
					Path.Combine(projectPath, "Packages", "com.yucp.devtools", "Plugins", "hpatchz.dll")
				};
				
				string[] hdiffinfoSources = new string[]
				{
					Path.Combine(projectPath, "Packages", "com.yucp.temp", "Plugins", "hdiffinfo.dll"),
					Path.Combine(projectPath, "Packages", "com.yucp.devtools", "Plugins", "hdiffinfo.dll")
				};
				
				string hdiffzDest = Path.Combine(libraryDir, "hdiffz.dll");
				string hpatchzDest = Path.Combine(libraryDir, "hpatchz.dll");
				string hdiffinfoDest = Path.Combine(libraryDir, "hdiffinfo.dll");
				
				// Copy hdiffz.dll if source exists and destination doesn't or is older
				foreach (var source in hdiffzSources)
				{
					if (File.Exists(source))
					{
						try
						{
							if (!File.Exists(hdiffzDest) || File.GetLastWriteTime(source) > File.GetLastWriteTime(hdiffzDest))
							{
								File.Copy(source, hdiffzDest, overwrite: true);
								Debug.Log($"[HDiffPatchWrapper] Copied hdiffz.dll to {hdiffzDest} (static constructor)");
							}
							break;
						}
						catch (Exception ex)
						{
							Debug.LogWarning($"[HDiffPatchWrapper] Could not copy hdiffz.dll in static constructor: {ex.Message}");
						}
					}
				}
				
				// Copy hpatchz.dll if source exists and destination doesn't or is older
				foreach (var source in hpatchzSources)
				{
					if (File.Exists(source))
					{
						try
						{
							if (!File.Exists(hpatchzDest) || File.GetLastWriteTime(source) > File.GetLastWriteTime(hpatchzDest))
							{
								File.Copy(source, hpatchzDest, overwrite: true);
								Debug.Log($"[HDiffPatchWrapper] Copied hpatchz.dll to {hpatchzDest} (static constructor)");
							}
							break;
						}
						catch (Exception ex)
						{
							Debug.LogWarning($"[HDiffPatchWrapper] Could not copy hpatchz.dll in static constructor: {ex.Message}");
						}
					}
				}
				
				// Copy hdiffinfo.dll if source exists and destination doesn't or is older
				foreach (var source in hdiffinfoSources)
				{
					if (File.Exists(source))
					{
						try
						{
							if (!File.Exists(hdiffinfoDest) || File.GetLastWriteTime(source) > File.GetLastWriteTime(hdiffinfoDest))
							{
								File.Copy(source, hdiffinfoDest, overwrite: true);
								Debug.Log($"[HDiffPatchWrapper] Copied hdiffinfo.dll to {hdiffinfoDest} (static constructor)");
							}
							break;
						}
						catch (Exception ex)
						{
							Debug.LogWarning($"[HDiffPatchWrapper] Could not copy hdiffinfo.dll in static constructor: {ex.Message}");
						}
					}
				}
				
				// Set DLL directory BEFORE any DllImport attributes try to resolve
				SetDllDirectory(libraryDir);
				Debug.Log($"[HDiffPatchWrapper] Set DLL directory to {libraryDir} (static constructor)");
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[HDiffPatchWrapper] Error in static constructor: {ex.Message}");
			}
#endif
		}
		
		/// <summary>
		/// Frees the loaded DLL libraries. Call this before cleanup to release file locks.
		/// </summary>
		public static void FreeDlls()
		{
#if UNITY_EDITOR_WIN
			try
			{
				if (s_hdiffzHandle != IntPtr.Zero)
				{
					FreeLibrary(s_hdiffzHandle);
					s_hdiffzHandle = IntPtr.Zero;
					Debug.Log("[HDiffPatchWrapper] Freed hdiffz.dll");
				}
				
				if (s_hpatchzHandle != IntPtr.Zero)
				{
					FreeLibrary(s_hpatchzHandle);
					s_hpatchzHandle = IntPtr.Zero;
					Debug.Log("[HDiffPatchWrapper] Freed hpatchz.dll");
				}
				
				// Reset DLL directory to default
				SetDllDirectory(null);
				
				s_dllsLoaded = false;
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[HDiffPatchWrapper] Error freeing DLLs: {ex.Message}");
			}
#endif
		}

		/// <summary>
		/// Ensures the HDiffPatch DLLs are loaded before use.
		/// Copies DLLs to Library/YUCP/ on first use, creating unique copies if needed to avoid file locks.
		/// This is necessary because P/Invoke doesn't automatically search Unity's Packages folder.
		/// </summary>
		private static void EnsureDllsLoaded()
		{
			// If DLLs are already loaded, skip
			if (s_dllsLoaded && s_hdiffzHandle != IntPtr.Zero && s_hpatchzHandle != IntPtr.Zero)
				return;

#if UNITY_EDITOR_WIN
			try
			{
				string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				
				// Destination: Library/YUCP/ (outside project, won't be locked by Unity)
				string libraryDir = Path.Combine(projectPath, "Library", "YUCP");
				string hdiffzDest = Path.Combine(libraryDir, "hdiffz.dll");
				string hpatchzDest = Path.Combine(libraryDir, "hpatchz.dll");
				string hdiffinfoDest = Path.Combine(libraryDir, "hdiffinfo.dll");
				
				// Source locations to check (in order of preference)
				string[] hdiffzSources = new string[]
				{
					Path.Combine(projectPath, "Packages", "com.yucp.temp", "Plugins", "hdiffz.dll"),
					Path.Combine(projectPath, "Packages", "com.yucp.devtools", "Plugins", "hdiffz.dll")
				};
				
				string[] hpatchzSources = new string[]
				{
					Path.Combine(projectPath, "Packages", "com.yucp.temp", "Plugins", "hpatchz.dll"),
					Path.Combine(projectPath, "Packages", "com.yucp.devtools", "Plugins", "hpatchz.dll")
				};
				
				string[] hdiffinfoSources = new string[]
				{
					Path.Combine(projectPath, "Packages", "com.yucp.temp", "Plugins", "hdiffinfo.dll"),
					Path.Combine(projectPath, "Packages", "com.yucp.devtools", "Plugins", "hdiffinfo.dll")
				};
				
				// Ensure Library/YUCP directory exists
				Directory.CreateDirectory(libraryDir);
				
				// Convert to absolute paths
				hdiffzDest = Path.GetFullPath(hdiffzDest);
				hpatchzDest = Path.GetFullPath(hpatchzDest);
				hdiffinfoDest = Path.GetFullPath(hdiffinfoDest);
				
				// Get the Library/YUCP directory for SetDllDirectory
				string dllDir = Path.GetDirectoryName(hdiffzDest);
				
				// Set DLL directory FIRST before copying/loading
				SetDllDirectory(dllDir);
				
				// Find source hdiffz.dll
				string sourceHdiffz = null;
				foreach (var source in hdiffzSources)
				{
					if (File.Exists(source))
					{
						sourceHdiffz = source;
						break;
					}
				}
				
				// Copy hdiffz.dll only if destination doesn't exist or source is newer
				// If copy fails due to file lock, use existing copy if available
				if (sourceHdiffz != null)
				{
					bool needsCopy = !File.Exists(hdiffzDest);
					if (!needsCopy && File.Exists(sourceHdiffz))
					{
						try
						{
							var sourceTime = File.GetLastWriteTime(sourceHdiffz);
							var destTime = File.GetLastWriteTime(hdiffzDest);
							needsCopy = sourceTime > destTime;
						}
						catch
						{
							needsCopy = false;
						}
					}
					
					if (needsCopy)
					{
						try
						{
							File.Copy(sourceHdiffz, hdiffzDest, overwrite: true);
							Debug.Log($"[HDiffPatchWrapper] Copied hdiffz.dll from {sourceHdiffz} to {hdiffzDest}");
							
							// If source is in temp Plugins, try to delete it immediately
							if (sourceHdiffz.Contains("com.yucp.temp"))
							{
								try
								{
									File.SetAttributes(sourceHdiffz, FileAttributes.Normal);
									File.Delete(sourceHdiffz);
									Debug.Log($"[HDiffPatchWrapper] Deleted source hdiffz.dll from Plugins to prevent DllImport from using it");
								}
								catch
								{
									// Ignore - will be cleaned up later
								}
							}
						}
						catch (Exception ex)
						{
							// If copy fails due to file lock, check if existing copy is usable
							if (File.Exists(hdiffzDest))
							{
								Debug.Log($"[HDiffPatchWrapper] Could not copy hdiffz.dll (file locked), using existing copy: {hdiffzDest}");
							}
							else
							{
								Debug.LogWarning($"[HDiffPatchWrapper] Could not copy hdiffz.dll: {ex.Message}");
							}
						}
					}
					else if (File.Exists(hdiffzDest))
					{
						Debug.Log($"[HDiffPatchWrapper] Using existing hdiffz.dll copy: {hdiffzDest}");
					}
				}
				else
				{
					if (!File.Exists(hdiffzDest))
					{
						Debug.LogError($"[HDiffPatchWrapper] hdiffz.dll not found in any source location");
					}
				}
				
				// Find source hpatchz.dll
				string sourceHpatchz = null;
				foreach (var source in hpatchzSources)
				{
					if (File.Exists(source))
					{
						sourceHpatchz = source;
						break;
					}
				}
				
				// Find source hdiffinfo.dll
				string sourceHdiffinfo = null;
				foreach (var source in hdiffinfoSources)
				{
					if (File.Exists(source))
					{
						sourceHdiffinfo = source;
						break;
					}
				}
				
				// Copy hpatchz.dll only if destination doesn't exist or source is newer
				// If copy fails due to file lock, use existing copy if available
				if (sourceHpatchz != null)
				{
					bool needsCopy = !File.Exists(hpatchzDest);
					if (!needsCopy && File.Exists(sourceHpatchz))
					{
						try
						{
							var sourceTime = File.GetLastWriteTime(sourceHpatchz);
							var destTime = File.GetLastWriteTime(hpatchzDest);
							needsCopy = sourceTime > destTime;
						}
						catch
						{
							needsCopy = false;
						}
					}
					
					if (needsCopy)
					{
						try
						{
							File.Copy(sourceHpatchz, hpatchzDest, overwrite: true);
							Debug.Log($"[HDiffPatchWrapper] Copied hpatchz.dll from {sourceHpatchz} to {hpatchzDest}");
							
							// If source is in temp Plugins, try to delete it immediately
							if (sourceHpatchz.Contains("com.yucp.temp"))
							{
								try
								{
									File.SetAttributes(sourceHpatchz, FileAttributes.Normal);
									File.Delete(sourceHpatchz);
									Debug.Log($"[HDiffPatchWrapper] Deleted source hpatchz.dll from Plugins to prevent DllImport from using it");
								}
								catch
								{
									// Ignore - will be cleaned up later
								}
							}
						}
						catch (Exception ex)
						{
							// If copy fails due to file lock, check if existing copy is usable
							if (File.Exists(hpatchzDest))
							{
								Debug.Log($"[HDiffPatchWrapper] Could not copy hpatchz.dll (file locked), using existing copy: {hpatchzDest}");
							}
							else
							{
								Debug.LogWarning($"[HDiffPatchWrapper] Could not copy hpatchz.dll: {ex.Message}");
							}
						}
					}
					else if (File.Exists(hpatchzDest))
					{
						Debug.Log($"[HDiffPatchWrapper] Using existing hpatchz.dll copy: {hpatchzDest}");
					}
				}
				else
				{
					if (!File.Exists(hpatchzDest))
					{
						Debug.LogError($"[HDiffPatchWrapper] hpatchz.dll not found in any source location");
					}
				}
				
				// Copy hdiffinfo.dll only if destination doesn't exist or source is newer
				if (sourceHdiffinfo != null)
				{
					bool needsCopy = !File.Exists(hdiffinfoDest);
					if (!needsCopy && File.Exists(sourceHdiffinfo))
					{
						try
						{
							var sourceTime = File.GetLastWriteTime(sourceHdiffinfo);
							var destTime = File.GetLastWriteTime(hdiffinfoDest);
							needsCopy = sourceTime > destTime;
						}
						catch
						{
							needsCopy = false;
						}
					}
					
					if (needsCopy)
					{
						try
						{
							File.Copy(sourceHdiffinfo, hdiffinfoDest, overwrite: true);
							Debug.Log($"[HDiffPatchWrapper] Copied hdiffinfo.dll from {sourceHdiffinfo} to {hdiffinfoDest}");
						}
						catch (Exception ex)
						{
							if (File.Exists(hdiffinfoDest))
							{
								Debug.Log($"[HDiffPatchWrapper] Could not copy hdiffinfo.dll (file locked), using existing copy: {hdiffinfoDest}");
							}
							else
							{
								Debug.LogWarning($"[HDiffPatchWrapper] Could not copy hdiffinfo.dll: {ex.Message}");
							}
						}
					}
					else if (File.Exists(hdiffinfoDest))
					{
						Debug.Log($"[HDiffPatchWrapper] Using existing hdiffinfo.dll copy: {hdiffinfoDest}");
					}
				}
				else
				{
					if (!File.Exists(hdiffinfoDest))
					{
						Debug.LogError($"[HDiffPatchWrapper] hdiffinfo.dll not found in any source location");
					}
				}
				
				// Explicitly load DLLs from Library/YUCP/ using full paths
				// Only load if not already loaded
				if (s_hdiffzHandle == IntPtr.Zero && File.Exists(hdiffzDest))
				{
					// Use full absolute path
					string fullHdiffzPath = Path.GetFullPath(hdiffzDest);
					s_hdiffzHandle = LoadLibrary(fullHdiffzPath);
					if (s_hdiffzHandle == IntPtr.Zero)
					{
						int error = Marshal.GetLastWin32Error();
						Debug.LogError($"[HDiffPatchWrapper] Failed to load hdiffz.dll from {fullHdiffzPath}. Error: {error}");
					}
					else
					{
						Debug.Log($"[HDiffPatchWrapper] Successfully loaded hdiffz.dll from {fullHdiffzPath}");
					}
				}
				else if (!File.Exists(hdiffzDest))
				{
					Debug.LogError($"[HDiffPatchWrapper] hdiffz.dll not found at: {hdiffzDest}");
				}

				if (s_hpatchzHandle == IntPtr.Zero && File.Exists(hpatchzDest))
				{
					// Use full absolute path
					string fullHpatchzPath = Path.GetFullPath(hpatchzDest);
					s_hpatchzHandle = LoadLibrary(fullHpatchzPath);
					if (s_hpatchzHandle == IntPtr.Zero)
					{
						int error = Marshal.GetLastWin32Error();
						Debug.LogError($"[HDiffPatchWrapper] Failed to load hpatchz.dll from {fullHpatchzPath}. Error: {error}");
					}
					else
					{
						Debug.Log($"[HDiffPatchWrapper] Successfully loaded hpatchz.dll from {fullHpatchzPath}");
					}
				}
				else if (!File.Exists(hpatchzDest))
				{
					Debug.LogError($"[HDiffPatchWrapper] hpatchz.dll not found at: {hpatchzDest}");
				}

				s_dllsLoaded = true;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[HDiffPatchWrapper] Error loading DLLs: {ex.Message}\n{ex.StackTrace}");
			}
#else
			// On non-Windows platforms, Unity should handle DLL loading automatically
			s_dllsLoaded = true;
#endif
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
		[DllImport("hdiffz", EntryPoint = "RegisterDelegate")]
		public static extern void RegisterDelegateHdiffz(HDiffzStringOutput del);

		/// <summary>
		/// Register delegate for HDiff error output.
		/// Adapted from CocoTools CocoDiff.cs
		/// </summary>
		[DllImport("hdiffz", EntryPoint = "RegisterErrorDelegate")]
		public static extern void RegisterErrorDelegateHdiffz(HDiffzStringOutput del);

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
		[DllImport("hdiffz")]
		public static extern int hdiff_unity(string oldFileName, string newFileName, string outDiffFileName,
			string[] diffOptions, int diffOptionSize);

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
		[DllImport("hpatchz", EntryPoint = "RegisterDelegate")]
		public static extern void RegisterDelegateHpatchz(HPatchzStringOutput del);

		/// <summary>
		/// Register delegate for HPatch error output.
		/// Adapted from CocoTools CocoPatch.cs
		/// </summary>
		[DllImport("hpatchz", EntryPoint = "RegisterErrorDelegate")]
		public static extern void RegisterErrorDelegateHpatchz(HPatchzStringOutput del);

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
		[DllImport("hpatchz")]
		public static extern int hpatch_unity(int optionCount, string[] options, string oldPath,
			string diffFileName, string outNewPath);

		#endregion

		#region HDiff Info (Patch Header Read)
		
		/// <summary>
		/// Reads compressed diff info (old/new sizes, compression type) from a .hdiff file.
		/// Implemented by hdiffinfo.dll wrapper built from HDiffPatch sources.
		/// </summary>
		[DllImport("hdiffinfo", EntryPoint = "hdiff_get_info", CharSet = CharSet.Ansi)]
		public static extern int hdiff_get_info(string diffFileName,
			out ulong oldSize, out ulong newSize, StringBuilder compressType, int compressTypeCap);
		
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
			// Ensure DLLs are loaded before use
			EnsureDllsLoaded();

			s_hpatchLogCallback = logCallback;
			s_hpatchErrorCallback = errorCallback;

			if (logCallback != null)
				RegisterDelegateHpatchz(HPatchLogWrapper);
			if (errorCallback != null)
				RegisterErrorDelegateHpatchz(HPatchErrorWrapper);

			var options = new string[0];
			var result = (THPatchResult)hpatch_unity(0, options, baseFbxPath, hdiffPath, outputFbxPath);

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
			
			EnsureDllsLoaded();
			
			var sb = new StringBuilder(260);
			int result = hdiff_get_info(hdiffPath, out oldSize, out newSize, sb, sb.Capacity);
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
