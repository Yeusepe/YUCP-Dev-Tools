using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// IMPORTANT: The namespace marker below (YUCP.CompanionTutorial.Generated.Source) is swapped to a
// per-export-unique namespace by PackageBuilder when this runtime is injected into an exported
// package. Do not rename it without updating the swap in PackageBuilder.
namespace YUCP.CompanionTutorial.Generated.Source
{
    internal struct CompanionOverlayFrame
    {
        public string TutorialTitle;
        public string StepTitle;
        public string StepText;
        public string StepCounter;
        public string WaitDescription;
        public Rect TargetRect;
        public bool TargetResolved;
        public bool CanGoBack;
        public bool IsLastStep;
        public Vector4 SpotlightPadding;
        public double StartedAt;
        public string MouseAction;
        public string OverlayMode;
    }

    internal sealed class CompanionOverlayWindow
    {
        // Optional project-relative path to the injected YUCPCompanionOverlay.bytes asset. Set by the
        // runner (which the bootstrap configures via SetHelperPath) so the overlay can be located even
        // though it lands at a package-specific path in the buyer's project. Declared platform-neutral
        // so the runner can assign it unconditionally; only the Windows overlay actually consumes it.
        public static string HelperBytesPathOverride;

#if UNITY_EDITOR_WIN
        private const int WM_CLOSE = 0x0010;
        private const int SW_HIDE = 0;
        private const string OverlayClassName = "YUCPCompanionTutorialOverlay";

        // Dev fallback only: when running an in-editor Preview inside the dev project, the real exe
        // ships in com.yucp.devtools. In exported/end-user projects the overlay is delivered as a
        // .bytes TextAsset (never a raw .exe) and extracted to Temp/ at launch — see ResolveExecutablePath.
        private const string DevHelperExePath = "Packages/com.yucp.devtools/Editor/PackageExporter/Binaries/CompanionOverlay/YUCPCompanionOverlay.exe";
        private const string HelperBytesFileName = "YUCPCompanionOverlay.bytes";
        private const string HelperExeFileName = "YUCPCompanionOverlay.exe";

        private Process m_process;
        private StreamWriter m_input;
        private readonly Queue<string> m_events = new Queue<string>();
        private bool m_warnedMissingHelper;
        private bool m_failed;

        public Action NextRequested;
        public Action PreviousRequested;
        public Action CloseRequested;

        public static void CloseOrphanedNativeWindows()
        {
            CloseOrphanedNativeWindows(IntPtr.Zero);
            KillOrphanedHelpers();
        }

        public void Show()
        {
            EnsureHelperRunning();
        }

        public void Render(CompanionOverlayFrame frame)
        {
            if (!EnsureHelperRunning())
                return;

            DrainEvents();
            if (m_process == null || m_process.HasExited || m_input == null)
                return;

            Rect main = EditorGUIUtility.GetMainWindowPosition();
            Rect target = frame.TargetRect;
            Vector4 padding = frame.SpotlightPadding;
            string line = string.Join("\t", new[]
            {
                "FRAME",
                Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
                Mathf.RoundToInt(main.x).ToString(CultureInfo.InvariantCulture),
                Mathf.RoundToInt(main.y).ToString(CultureInfo.InvariantCulture),
                Mathf.Max(1, Mathf.RoundToInt(main.width)).ToString(CultureInfo.InvariantCulture),
                Mathf.Max(1, Mathf.RoundToInt(main.height)).ToString(CultureInfo.InvariantCulture),
                frame.TargetResolved ? "1" : "0",
                Mathf.RoundToInt(target.x).ToString(CultureInfo.InvariantCulture),
                Mathf.RoundToInt(target.y).ToString(CultureInfo.InvariantCulture),
                Mathf.RoundToInt(target.width).ToString(CultureInfo.InvariantCulture),
                Mathf.RoundToInt(target.height).ToString(CultureInfo.InvariantCulture),
                Mathf.RoundToInt(padding.x).ToString(CultureInfo.InvariantCulture),
                Mathf.RoundToInt(padding.y).ToString(CultureInfo.InvariantCulture),
                Mathf.RoundToInt(padding.z).ToString(CultureInfo.InvariantCulture),
                Mathf.RoundToInt(padding.w).ToString(CultureInfo.InvariantCulture),
                frame.CanGoBack ? "1" : "0",
                frame.IsLastStep ? "1" : "0",
                Encode(frame.TutorialTitle),
                Encode(frame.StepTitle),
                Encode(frame.StepText),
                Encode(frame.StepCounter),
                Encode(frame.WaitDescription),
                Encode(frame.MouseAction),
                Encode(frame.OverlayMode)
            });

            try
            {
                m_input.WriteLine(line);
                m_input.Flush();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Companion Tutorial] Overlay helper stopped accepting frames: {ex.Message}");
                Close();
            }
        }

        public void Close()
        {
            DrainEvents();

            try
            {
                if (m_process != null && !m_process.HasExited)
                {
                    m_input?.WriteLine("STOP");
                    m_input?.Flush();
                    if (!m_process.WaitForExit(350))
                        m_process.Kill();
                }
            }
            catch
            {
                // The helper is best-effort UI. Closing Unity must never depend on it responding.
            }
            finally
            {
                m_input?.Dispose();
                m_input = null;
                m_process?.Dispose();
                m_process = null;
                CloseOrphanedNativeWindows(IntPtr.Zero);
            }
        }

        private bool EnsureHelperRunning()
        {
            if (m_failed)
                return false;

            if (m_process != null && !m_process.HasExited)
                return true;

            string helperPath = ResolveExecutablePath();
            if (string.IsNullOrEmpty(helperPath) || !File.Exists(helperPath))
            {
                if (!m_warnedMissingHelper)
                {
                    m_warnedMissingHelper = true;
                    Debug.LogWarning("[YUCP Companion Tutorial] Overlay helper could not be located or extracted.");
                }
                return false;
            }

            CloseOrphanedNativeWindows(IntPtr.Zero);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = helperPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                m_process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                m_process.OutputDataReceived += OnHelperOutput;
                m_process.ErrorDataReceived += OnHelperError;
                m_process.Exited += (sender, args) => m_failed = false;

                if (!m_process.Start())
                    return false;

                m_input = m_process.StandardInput;
                m_input.AutoFlush = true;
                m_process.BeginOutputReadLine();
                m_process.BeginErrorReadLine();
                return true;
            }
            catch (Exception ex)
            {
                m_failed = true;
                Debug.LogWarning($"[YUCP Companion Tutorial] Failed to start overlay helper: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resolves a launchable .exe path. In exported/end-user projects the overlay ships as a
        /// .bytes TextAsset (so no executable ever sits in the package or the visible project tree);
        /// it is extracted to the project's auto-cleaned Temp/ folder before launch. In the dev
        /// project a Preview falls back to the real checked-in .exe.
        /// </summary>
        private string ResolveExecutablePath()
        {
            string bytesPath = LocateHelperBytes();
            if (!string.IsNullOrEmpty(bytesPath) && File.Exists(bytesPath))
            {
                string extracted = ExtractHelperToTemp(bytesPath);
                if (!string.IsNullOrEmpty(extracted))
                    return extracted;
            }

            // Dev/Preview fallback: launch the real exe shipped with com.yucp.devtools.
            string devExe = Path.GetFullPath(DevHelperExePath);
            if (File.Exists(devExe))
                return devExe;

            return null;
        }

        /// <summary>
        /// Finds the injected YUCPCompanionOverlay.bytes on disk. Prefers the path the runner supplied
        /// (HelperBytesPathOverride), then searches the installed-packages container and the asset tree.
        /// </summary>
        private static string LocateHelperBytes()
        {
            if (!string.IsNullOrEmpty(HelperBytesPathOverride))
            {
                string full = Path.GetFullPath(HelperBytesPathOverride);
                if (File.Exists(full))
                    return full;
            }

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string[] searchRoots =
                {
                    Path.Combine(projectRoot, "Packages", "yucp.installed-packages"),
                    Application.dataPath // Assets/
                };

                foreach (string root in searchRoots)
                {
                    if (!Directory.Exists(root))
                        continue;
                    string[] matches = Directory.GetFiles(root, HelperBytesFileName, SearchOption.AllDirectories);
                    if (matches.Length > 0)
                        return matches[0];
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Companion Tutorial] Failed to locate overlay payload: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Writes the .bytes payload to Temp/YUCP/CompanionOverlay/YUCPCompanionOverlay.exe (outside
        /// Assets/, auto-cleaned by Unity, never imported). Rewrites only when missing or size-mismatched
        /// so a running prior instance isn't disturbed; if the file is locked by a live overlay the
        /// existing copy is reused.
        /// </summary>
        private static string ExtractHelperToTemp(string bytesPath)
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string targetDir = Path.Combine(projectRoot, "Temp", "YUCP", "CompanionOverlay");
                Directory.CreateDirectory(targetDir);
                string targetExe = Path.Combine(targetDir, HelperExeFileName);

                var source = new FileInfo(bytesPath);
                bool needsWrite = true;
                if (File.Exists(targetExe))
                {
                    try { needsWrite = new FileInfo(targetExe).Length != source.Length; }
                    catch { needsWrite = false; }
                }

                if (needsWrite)
                {
                    try
                    {
                        File.Copy(bytesPath, targetExe, true);
                    }
                    catch (IOException)
                    {
                        // A previous overlay instance is still holding the exe. Reuse it if present.
                        if (!File.Exists(targetExe))
                            throw;
                    }
                }

                return targetExe;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Companion Tutorial] Failed to extract overlay helper: {ex.Message}");
                return null;
            }
        }

        private void OnHelperOutput(object sender, DataReceivedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Data))
                return;

            lock (m_events)
            {
                m_events.Enqueue(args.Data.Trim());
            }
        }

        private static void OnHelperError(object sender, DataReceivedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                Debug.LogWarning($"[YUCP Companion Tutorial] Overlay helper: {args.Data}");
        }

        private void DrainEvents()
        {
            while (true)
            {
                string command;
                lock (m_events)
                {
                    if (m_events.Count == 0)
                        return;
                    command = m_events.Dequeue();
                }

                if (string.Equals(command, "NEXT", StringComparison.OrdinalIgnoreCase))
                    NextRequested?.Invoke();
                else if (string.Equals(command, "BACK", StringComparison.OrdinalIgnoreCase))
                    PreviousRequested?.Invoke();
                else if (string.Equals(command, "CLOSE", StringComparison.OrdinalIgnoreCase))
                    CloseRequested?.Invoke();
            }
        }

        private static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static void KillOrphanedHelpers()
        {
            foreach (var process in Process.GetProcessesByName("YUCPCompanionOverlay"))
            {
                try
                {
                    if (process.Id == Process.GetCurrentProcess().Id)
                        continue;
                    if (!process.HasExited)
                        process.Kill();
                }
                catch
                {
                    // Best-effort stale overlay cleanup.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void CloseOrphanedNativeWindows(IntPtr except)
        {
            EnumWindows((hwnd, lParam) =>
            {
                if (hwnd == except)
                    return true;

                StringBuilder className = new StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);
                if (className.ToString() == OverlayClassName)
                {
                    PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    ShowWindow(hwnd, SW_HIDE);
                }

                return true;
            }, IntPtr.Zero);
        }

        private delegate bool EnumWindowsDelegate(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsDelegate lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
#else
        private bool m_warned;

        public Action NextRequested;
        public Action PreviousRequested;
        public Action CloseRequested;

        public static void CloseOrphanedNativeWindows()
        {
        }

        public void Show()
        {
            if (m_warned)
                return;

            m_warned = true;
            Debug.LogWarning("[YUCP Companion Tutorial] Whole-editor overlay is currently Windows-only.");
        }

        public void Render(CompanionOverlayFrame frame)
        {
            Show();
        }

        public void Close()
        {
        }
#endif
    }
}
