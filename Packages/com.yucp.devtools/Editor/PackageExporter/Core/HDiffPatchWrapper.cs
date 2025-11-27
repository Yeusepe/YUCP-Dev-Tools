using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
	/// <summary>
	/// Wrapper for HDiffPatch DLLs (hdiffz.dll and hpatchz.dll).
	/// Based on CocoTools implementation: https://github.com/coco1337/CocoTools
	/// Original HDiffPatch by Sisong: https://github.com/sisong/HDiffPatch
	/// </summary>
	public static class HDiffPatchWrapper
	{
		#region HDiff (Diff Creation)
		
		/// <summary>
		/// Delegate for HDiff log output.
		/// Based on CocoTools CocoDiff.cs
		/// </summary>
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void HDiffzStringOutput(string str);
		
		/// <summary>
		/// Register delegate for HDiff log output.
		/// Based on CocoTools CocoDiff.cs
		/// </summary>
		[DllImport("hdiffz", EntryPoint = "RegisterDelegate")]
		public static extern void RegisterDelegateHdiffz(HDiffzStringOutput del);

		/// <summary>
		/// Register delegate for HDiff error output.
		/// Based on CocoTools CocoDiff.cs
		/// </summary>
		[DllImport("hdiffz", EntryPoint = "RegisterErrorDelegate")]
		public static extern void RegisterErrorDelegateHdiffz(HDiffzStringOutput del);

		/// <summary>
		/// Create a binary diff file from old and new FBX files.
		/// Based on CocoTools CocoDiff.cs hdiff_unity function
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
		/// Based on CocoTools CocoPatch.cs
		/// </summary>
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void HPatchzStringOutput(string str);

		/// <summary>
		/// Register delegate for HPatch log output.
		/// Based on CocoTools CocoPatch.cs
		/// </summary>
		[DllImport("hpatchz", EntryPoint = "RegisterDelegate")]
		public static extern void RegisterDelegateHpatchz(HPatchzStringOutput del);

		/// <summary>
		/// Register delegate for HPatch error output.
		/// Based on CocoTools CocoPatch.cs
		/// </summary>
		[DllImport("hpatchz", EntryPoint = "RegisterErrorDelegate")]
		public static extern void RegisterErrorDelegateHpatchz(HPatchzStringOutput del);

		/// <summary>
		/// Apply a binary patch to an FBX file.
		/// Based on CocoTools CocoPatch.cs hpatch_unity function
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

		#region High-Level API

		/// <summary>
		/// Default diff options used by CocoTools.
		/// Based on CocoTools CocoDiff.cs DEFAULT_COMMAND
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

		#endregion
	}

	/// <summary>
	/// HDiff result codes.
	/// Based on CocoTools CocoDiff.cs THDiffResult enum
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
	/// Based on CocoTools CocoPatch.cs THPatchResult enum
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

