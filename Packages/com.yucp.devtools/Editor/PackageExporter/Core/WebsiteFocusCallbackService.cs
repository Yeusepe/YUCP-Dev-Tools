using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class WebsiteFocusCallbackService
    {
        private const string CallbackUrlQueryKey = "unityCallbackUrl";
        private const string StateQueryKey = "unityState";
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, PendingFocusRequest> PendingRequests = new Dictionary<string, PendingFocusRequest>();

        public static void OpenUrlWithFocusCallback(string url, EditorWindow openerWindow, Action fallbackFocusAction)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                Debug.LogError("[YUCP WebsiteFocus] Cannot open an empty website URL.");
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri parsedUrl))
            {
                Debug.LogError($"[YUCP WebsiteFocus] Invalid website URL: {url}");
                return;
            }

            if (!TryCreatePendingRequest(openerWindow, fallbackFocusAction, out PendingFocusRequest request, out string errorMessage))
            {
                Debug.LogWarning($"[YUCP WebsiteFocus] {errorMessage} Opening website without return-focus callback.");
                Application.OpenURL(url);
                return;
            }

            lock (SyncRoot)
            {
                PendingRequests[request.State] = request;
            }

            _ = ListenForCallbackAsync(request);

            string urlWithCallback = AppendQueryParameters(parsedUrl, request.CallbackUrl, request.State);
            Debug.Log($"[YUCP WebsiteFocus] Opening website with focus callback: {urlWithCallback}");
            Application.OpenURL(urlWithCallback);
        }

        private static bool TryCreatePendingRequest(EditorWindow openerWindow, Action fallbackFocusAction, out PendingFocusRequest request, out string errorMessage)
        {
            request = null;
            errorMessage = null;

            int port;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to reserve a loopback port: {ex.Message}";
                return false;
            }
            finally
            {
                try
                {
                    probe.Stop();
                }
                catch (SocketException ex)
                {
                    Debug.LogWarning($"[YUCP WebsiteFocus] Failed to stop loopback port probe cleanly: {ex.Message}");
                }
            }

            var listener = new HttpListener();
            string callbackPrefix = $"http://127.0.0.1:{port}/";

            try
            {
                listener.Prefixes.Add(callbackPrefix);
                listener.Start();
            }
            catch (Exception ex)
            {
                listener.Close();
                errorMessage = $"Failed to start loopback listener on {callbackPrefix}: {ex.Message}";
                return false;
            }

            request = new PendingFocusRequest(
                listener,
                $"{callbackPrefix}focus",
                Guid.NewGuid().ToString("N"),
                openerWindow != null ? new WeakReference<EditorWindow>(openerWindow) : null,
                fallbackFocusAction);

            return true;
        }

        private static async Task ListenForCallbackAsync(PendingFocusRequest request)
        {
            HttpListenerContext context = null;
            bool shouldFocus = false;
            bool timedOut = false;

            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                Task<HttpListenerContext> contextTask = request.Listener.GetContextAsync();
                Task timeoutTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);

                Task finishedTask = await Task.WhenAny(contextTask, timeoutTask);
                timedOut = finishedTask != contextTask;
                timeoutCts.Cancel();

                if (timedOut)
                {
                    Debug.LogWarning("[YUCP WebsiteFocus] Timed out waiting for website focus callback.");
                    return;
                }

                context = await contextTask;
                string returnedState = ParseQueryParameter(context.Request.Url, StateQueryKey);

                if (!string.Equals(returnedState, request.State, StringComparison.Ordinal))
                {
                    Debug.LogWarning("[YUCP WebsiteFocus] Ignoring focus callback due to state mismatch.");
                    await WriteResponseAsync(context.Response, HttpStatusCode.BadRequest, "State mismatch.");
                    return;
                }

                Debug.Log("[YUCP WebsiteFocus] Website focus callback received.");
                await WriteResponseAsync(context.Response, HttpStatusCode.OK, "Unity focus requested.");
                shouldFocus = true;
            }
            catch (HttpListenerException ex)
            {
                if (!timedOut)
                {
                    Debug.LogWarning($"[YUCP WebsiteFocus] Listener stopped before callback completed: {ex.Message}");
                }
            }
            catch (ObjectDisposedException)
            {
                if (!timedOut)
                {
                    Debug.LogWarning("[YUCP WebsiteFocus] Listener was disposed before callback completed.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP WebsiteFocus] Callback listener failed: {ex}");
            }
            finally
            {
                CleanupRequest(request.State, request.Listener);
            }

            if (shouldFocus)
            {
                EditorApplication.delayCall += () => FocusRequestOrigin(request);
            }
        }

        private static void FocusRequestOrigin(PendingFocusRequest request)
        {
            if (request.OpenerWindow != null && request.OpenerWindow.TryGetTarget(out EditorWindow opener) && opener != null)
            {
                opener.Show();
                opener.Focus();
                return;
            }

            request.FallbackFocusAction?.Invoke();
        }

        private static void CleanupRequest(string state, HttpListener listener)
        {
            lock (SyncRoot)
            {
                PendingRequests.Remove(state);
            }

            try
            {
                listener.Stop();
            }
            catch (HttpListenerException ex)
            {
                Debug.LogWarning($"[YUCP WebsiteFocus] Failed to stop loopback listener cleanly: {ex.Message}");
            }
            finally
            {
                listener.Close();
            }
        }

        private static async Task WriteResponseAsync(HttpListenerResponse response, HttpStatusCode statusCode, string body)
        {
            response.StatusCode = (int)statusCode;
            response.ContentType = "text/plain; charset=utf-8";

            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }

        private static string ParseQueryParameter(Uri uri, string key)
        {
            if (uri == null || string.IsNullOrEmpty(key))
            {
                return null;
            }

            string query = uri.Query;
            if (string.IsNullOrEmpty(query))
            {
                return null;
            }

            string trimmed = query.TrimStart('?');
            string[] pairs = trimmed.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string pair in pairs)
            {
                string[] segments = pair.Split(new[] { '=' }, 2);
                string currentKey = Uri.UnescapeDataString(segments[0]);
                if (!string.Equals(currentKey, key, StringComparison.Ordinal))
                {
                    continue;
                }

                string currentValue = segments.Length > 1 ? segments[1] : string.Empty;
                return Uri.UnescapeDataString(currentValue.Replace("+", "%20"));
            }

            return null;
        }

        private static string AppendQueryParameters(Uri url, string callbackUrl, string state)
        {
            var builder = new UriBuilder(url);
            string query = builder.Query;

            query = AppendQueryParameter(query, CallbackUrlQueryKey, callbackUrl);
            query = AppendQueryParameter(query, StateQueryKey, state);

            builder.Query = query;
            return builder.Uri.ToString();
        }

        private static string AppendQueryParameter(string query, string key, string value)
        {
            string normalized = string.IsNullOrEmpty(query) ? string.Empty : query.TrimStart('?');

            if (!string.IsNullOrEmpty(normalized))
            {
                normalized += "&";
            }

            normalized += $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value ?? string.Empty)}";
            return normalized;
        }

        private sealed class PendingFocusRequest
        {
            public PendingFocusRequest(
                HttpListener listener,
                string callbackUrl,
                string state,
                WeakReference<EditorWindow> openerWindow,
                Action fallbackFocusAction)
            {
                Listener = listener;
                CallbackUrl = callbackUrl;
                State = state;
                OpenerWindow = openerWindow;
                FallbackFocusAction = fallbackFocusAction;
            }

            public HttpListener Listener { get; }
            public string CallbackUrl { get; }
            public string State { get; }
            public WeakReference<EditorWindow> OpenerWindow { get; }
            public Action FallbackFocusAction { get; }
        }
    }
}
