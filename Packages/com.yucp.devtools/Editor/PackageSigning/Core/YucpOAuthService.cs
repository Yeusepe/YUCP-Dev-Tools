using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    public static class YucpOAuthService
    {
        public const string ClientId = "yucp-unity-editor";

        private const string KeyToken = "YUCP_OAuth_AccessToken";
        private const string KeyExpiry = "YUCP_OAuth_TokenExpiry";
        private const string KeyUserId = "YUCP_OAuth_UserId";
        private const string KeyDisplayName = "YUCP_OAuth_DisplayName";
        private const string KeySessionVersion = "YUCP_OAuth_SessionVersion";
        private const string CurrentSessionVersion = "2";
        private const int AccessTokenSkewSeconds = 60;
        private static readonly byte[] SessionEntropy = Encoding.UTF8.GetBytes("YUCP.UnityEditor.Session.v2");
        private static readonly object SessionLock = new object();
        private static Task _backgroundRefreshTask;

#if UNITY_EDITOR_WIN
        private const int CryptProtectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob pDataIn,
            string szDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DataBlob pDataIn,
            StringBuilder ppszDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out DataBlob pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);
#endif

        [Serializable]
        private class OAuthSessionV2
        {
            public int storageVersion = 2;
            public string accessToken;
            public long accessTokenExpiresAt;
            public string refreshToken;
            public long refreshTokenExpiresAt;
            public string userId;
            public string displayName;
            public string scope;
        }

        public static bool IsSignedIn()
        {
            return TryGetActiveSession(out _);
        }

        public static string GetAccessToken()
        {
            return TryGetActiveSession(out OAuthSessionV2 session) && HasUsableAccessToken(session)
                ? session.accessToken
                : null;
        }

        public static string GetUserId()
        {
            if (TryGetCachedSession(out OAuthSessionV2 session) && !string.IsNullOrEmpty(session.userId))
            {
                return session.userId;
            }

            string userId = EditorPrefs.GetString(KeyUserId, null);
            return string.IsNullOrEmpty(userId) ? null : userId;
        }

        public static string GetDisplayName()
        {
            if (TryGetCachedSession(out OAuthSessionV2 session) && !string.IsNullOrEmpty(session.displayName))
            {
                return session.displayName;
            }

            string name = EditorPrefs.GetString(KeyDisplayName, null);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        public static void TryBeginBackgroundRefresh(string serverUrl, Action onStateChanged = null)
        {
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            if (!TryGetCachedSession(out OAuthSessionV2 session) || HasUsableAccessToken(session) || string.IsNullOrEmpty(session.refreshToken))
            {
                return;
            }

            lock (SessionLock)
            {
                if (_backgroundRefreshTask != null && !_backgroundRefreshTask.IsCompleted)
                {
                    return;
                }

                _backgroundRefreshTask = RefreshInBackgroundAsync(serverUrl, onStateChanged);
            }
        }

        public static async Task<string> GetValidAccessTokenAsync(string serverUrl)
        {
            if (TryGetCachedSession(out OAuthSessionV2 session))
            {
                if (HasUsableAccessToken(session))
                {
                    PersistPresenceHints(session);
                    return session.accessToken;
                }

                if (!string.IsNullOrEmpty(session.refreshToken))
                {
                    string refreshedAccessToken = await RefreshAccessTokenAsync(serverUrl, session);
                    if (!string.IsNullOrEmpty(refreshedAccessToken))
                    {
                        return refreshedAccessToken;
                    }
                }
            }

            if (TryGetLegacyAccessToken(out string legacyToken, out long legacyExpiry))
            {
                var legacySession = new OAuthSessionV2
                {
                    storageVersion = 1,
                    accessToken = legacyToken,
                    accessTokenExpiresAt = legacyExpiry,
                    userId = EditorPrefs.GetString(KeyUserId, null),
                    displayName = EditorPrefs.GetString(KeyDisplayName, null),
                };
                PersistPresenceHints(legacySession);
                return legacyToken;
            }

            return null;
        }

        public static void SignOut()
        {
            ClearPersistentSession();
            ClearLegacyKeys();
            EditorPrefs.DeleteKey(KeySessionVersion);
        }

        public static async Task SignInAsync(string serverUrl, Action onSuccess, Action<string> onError)
        {
            Debug.Log("[YUCP OAuth] SignInAsync started");
            try
            {
                byte[] verifierBytes = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(verifierBytes);
                }
                string codeVerifier = Base64UrlEncode(verifierBytes);

                byte[] hashBytes;
                using (var sha = SHA256.Create())
                {
                    hashBytes = sha.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
                }
                string codeChallenge = Base64UrlEncode(hashBytes);

                byte[] stateBytes = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(stateBytes);
                }
                string state = Base64UrlEncode(stateBytes);

                int port;
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                Debug.Log($"[YUCP OAuth] Using loopback port {port}");

                string redirectUri = $"http://127.0.0.1:{port}/callback";
                string authUrl = BuildAuthUrl(serverUrl, codeChallenge, state, redirectUri);
                Debug.Log($"[YUCP OAuth] Auth URL: {authUrl}");

                var httpListener = new HttpListener();
                httpListener.Prefixes.Add($"http://127.0.0.1:{port}/");
                httpListener.Start();
                Debug.Log($"[YUCP OAuth] HttpListener started on http://127.0.0.1:{port}/");

                Application.OpenURL(authUrl);
                Debug.Log("[YUCP OAuth] Browser opened, waiting for callback...");

                HttpListenerContext context = null;
                string authCode = null;
                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
                    {
                        Task<HttpListenerContext> contextTask = httpListener.GetContextAsync();
                        Task timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);

                        Task finished = await Task.WhenAny(contextTask, timeoutTask);
                        cts.Cancel();

                        if (finished != contextTask)
                        {
                            Debug.LogWarning("[YUCP OAuth] Timed out waiting for browser callback.");
                            httpListener.Stop();
                            onError?.Invoke("Sign-in timed out after 2 minutes. Please try again.");
                            return;
                        }

                        context = await contextTask;
                        Debug.Log($"[YUCP OAuth] Callback received: {context.Request.Url}");
                    }

                    var qp = ParseQueryString(context.Request.Url?.Query ?? "");
                    Debug.Log($"[YUCP OAuth] Callback params: {string.Join(", ", qp.Keys)}");

                    if (qp.TryGetValue("error", out string callbackError))
                    {
                        string desc = qp.TryGetValue("error_description", out string errorDescription)
                            ? Uri.UnescapeDataString(errorDescription)
                            : callbackError;
                        string msg = $"Authorization error: {desc}";
                        Debug.LogError($"[YUCP OAuth] {msg}");
                        await SendErrorRedirectAsync(context, serverUrl, msg);
                        onError?.Invoke(msg);
                        return;
                    }

                    if (!qp.TryGetValue("state", out string returnedState) || returnedState != state)
                    {
                        const string msg = "State mismatch — possible CSRF. Please try again.";
                        Debug.LogError($"[YUCP OAuth] {msg}");
                        await SendErrorRedirectAsync(context, serverUrl, msg);
                        onError?.Invoke(msg);
                        return;
                    }

                    if (!qp.TryGetValue("code", out authCode) || string.IsNullOrEmpty(authCode))
                    {
                        const string msg = "No authorization code received from server.";
                        Debug.LogError($"[YUCP OAuth] {msg}");
                        await SendErrorRedirectAsync(context, serverUrl, msg);
                        onError?.Invoke(msg);
                        return;
                    }

                    Debug.Log($"[YUCP OAuth] Auth code received (length {authCode.Length}), sending success page to browser.");
                    await SendSuccessPageAsync(context);
                }
                finally
                {
                    try { httpListener.Stop(); } catch { }
                    Debug.Log("[YUCP OAuth] HttpListener stopped.");
                }

                Debug.Log($"[YUCP OAuth] Exchanging auth code at {serverUrl.TrimEnd('/')}/api/auth/oauth2/token");
                var form = new WWWForm();
                form.AddField("grant_type", "authorization_code");
                form.AddField("client_id", ClientId);
                form.AddField("code", authCode);
                form.AddField("code_verifier", codeVerifier);
                form.AddField("redirect_uri", redirectUri);

                using var tokenReq = UnityWebRequest.Post($"{serverUrl.TrimEnd('/')}/api/auth/oauth2/token", form);
                tokenReq.SetRequestHeader("Accept", "application/json");
                tokenReq.SetRequestHeader("Accept-Encoding", "identity");

                var op = tokenReq.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }

                string tokenJson = tokenReq.downloadHandler.text;
                Debug.Log($"[YUCP OAuth] Token response {tokenReq.responseCode}: {tokenJson}");

                if (tokenReq.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Token exchange failed ({tokenReq.responseCode}): {tokenReq.error} — {tokenJson}");
                    return;
                }

                OAuthSessionV2 session = BuildSessionFromTokenResponse(tokenJson, null);
                if (session == null || string.IsNullOrEmpty(session.accessToken))
                {
                    onError?.Invoke($"No access_token in server response: {tokenJson}");
                    return;
                }

                PersistSession(session);
                Debug.Log($"[YUCP OAuth] Access token obtained (length {session.accessToken.Length}).");
                Debug.Log($"[YUCP OAuth] Signed in as '{session.displayName}' (sub={session.userId}).");

                var signingService = new PackageSigningService(serverUrl);
                await signingService.FetchAndCacheRootPublicKeyAsync();

                Debug.Log("[YUCP OAuth] Sign-in complete.");
                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP OAuth] Unhandled exception: {ex}");
                onError?.Invoke($"Sign-in error: {ex.Message}");
            }
        }

        private static async Task RefreshInBackgroundAsync(string serverUrl, Action onStateChanged)
        {
            try
            {
                await GetValidAccessTokenAsync(serverUrl);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Background refresh failed: {ex.Message}");
            }
            finally
            {
                if (onStateChanged != null)
                {
                    EditorApplication.delayCall += () => onStateChanged();
                }
            }
        }

        private static async Task<string> RefreshAccessTokenAsync(string serverUrl, OAuthSessionV2 currentSession)
        {
            if (currentSession == null || string.IsNullOrEmpty(currentSession.refreshToken) || string.IsNullOrEmpty(serverUrl))
            {
                return null;
            }

            var form = new WWWForm();
            form.AddField("grant_type", "refresh_token");
            form.AddField("client_id", ClientId);
            form.AddField("refresh_token", currentSession.refreshToken);

            using var tokenReq = UnityWebRequest.Post($"{serverUrl.TrimEnd('/')}/api/auth/oauth2/token", form);
            tokenReq.SetRequestHeader("Accept", "application/json");
            tokenReq.SetRequestHeader("Accept-Encoding", "identity");

            var operation = tokenReq.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            string tokenJson = tokenReq.downloadHandler?.text ?? string.Empty;
            if (tokenReq.result != UnityWebRequest.Result.Success)
            {
                if (IsInvalidGrantResponse(tokenReq.responseCode, tokenJson))
                {
                    Debug.LogWarning("[YUCP OAuth] Refresh token rejected by server. Clearing local session.");
                    SignOut();
                }

                return null;
            }

            OAuthSessionV2 refreshedSession = BuildSessionFromTokenResponse(tokenJson, currentSession);
            if (refreshedSession == null || string.IsNullOrEmpty(refreshedSession.accessToken))
            {
                return null;
            }

            PersistSession(refreshedSession);
            return refreshedSession.accessToken;
        }

        private static OAuthSessionV2 BuildSessionFromTokenResponse(string tokenJson, OAuthSessionV2 previousSession)
        {
            string accessToken = ExtractJsonString(tokenJson, "access_token");
            if (string.IsNullOrEmpty(accessToken))
            {
                return null;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long accessTokenExpiresAt = ResolveExpiryTimestamp(tokenJson, "expires_in", "expires_at", now + 3600 - AccessTokenSkewSeconds);

            string refreshToken = ExtractJsonString(tokenJson, "refresh_token");
            if (string.IsNullOrEmpty(refreshToken))
            {
                refreshToken = previousSession?.refreshToken;
            }

            long refreshTokenExpiresAt = ResolveRefreshExpiryTimestamp(tokenJson, previousSession?.refreshTokenExpiresAt ?? 0);
            string scope = ExtractJsonString(tokenJson, "scope");
            if (string.IsNullOrEmpty(scope))
            {
                scope = previousSession?.scope;
            }

            string userId = ParseJwtClaim(accessToken, "sub");
            if (string.IsNullOrEmpty(userId))
            {
                userId = previousSession?.userId;
            }

            string displayName = ParseJwtClaim(accessToken, "name");
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = previousSession?.displayName;
            }

            return new OAuthSessionV2
            {
                storageVersion = 2,
                accessToken = accessToken,
                accessTokenExpiresAt = accessTokenExpiresAt,
                refreshToken = refreshToken,
                refreshTokenExpiresAt = refreshTokenExpiresAt,
                userId = userId,
                displayName = displayName,
                scope = scope,
            };
        }

        private static long ResolveExpiryTimestamp(string tokenJson, string expiresInKey, string expiresAtKey, long fallback)
        {
            string expiresAtRaw = ExtractJsonValue(tokenJson, expiresAtKey);
            if (long.TryParse(expiresAtRaw, out long absoluteExpiry) && absoluteExpiry > 0)
            {
                return absoluteExpiry;
            }

            string expiresInRaw = ExtractJsonValue(tokenJson, expiresInKey);
            if (int.TryParse(expiresInRaw, out int expiresInSeconds) && expiresInSeconds > 0)
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds - AccessTokenSkewSeconds;
            }

            return fallback;
        }

        private static long ResolveRefreshExpiryTimestamp(string tokenJson, long previousValue)
        {
            string refreshExpiresAtRaw = ExtractJsonValue(tokenJson, "refresh_token_expires_at");
            if (long.TryParse(refreshExpiresAtRaw, out long absoluteExpiry) && absoluteExpiry > 0)
            {
                return absoluteExpiry;
            }

            string refreshExpiresInRaw = ExtractJsonValue(tokenJson, "refresh_token_expires_in");
            if (int.TryParse(refreshExpiresInRaw, out int expiresInSeconds) && expiresInSeconds > 0)
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds;
            }

            return previousValue;
        }

        private static bool TryGetActiveSession(out OAuthSessionV2 session)
        {
            if (TryGetCachedSession(out session))
            {
                if (HasUsableAccessToken(session) || IsRefreshableSession(session))
                {
                    PersistPresenceHints(session);
                    return true;
                }
            }

            if (TryGetLegacyAccessToken(out string legacyToken, out long legacyExpiry))
            {
                session = new OAuthSessionV2
                {
                    storageVersion = 1,
                    accessToken = legacyToken,
                    accessTokenExpiresAt = legacyExpiry,
                    userId = EditorPrefs.GetString(KeyUserId, null),
                    displayName = EditorPrefs.GetString(KeyDisplayName, null),
                };
                PersistPresenceHints(session);
                return true;
            }

            session = null;
            return false;
        }

        private static bool TryGetCachedSession(out OAuthSessionV2 session)
        {
            session = LoadPersistentSession();
            return session != null;
        }

        private static bool TryGetLegacyAccessToken(out string token, out long expiry)
        {
            token = null;
            expiry = 0;

            if (!EditorPrefs.HasKey(KeyToken) || !EditorPrefs.HasKey(KeyExpiry))
            {
                return false;
            }

            token = EditorPrefs.GetString(KeyToken, string.Empty);
            expiry = EditorPrefs.GetInt(KeyExpiry, 0);
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            return expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + AccessTokenSkewSeconds;
        }

        private static bool HasUsableAccessToken(OAuthSessionV2 session)
        {
            return session != null
                && !string.IsNullOrEmpty(session.accessToken)
                && session.accessTokenExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + AccessTokenSkewSeconds;
        }

        private static bool IsRefreshableSession(OAuthSessionV2 session)
        {
            if (session == null || string.IsNullOrEmpty(session.refreshToken))
            {
                return false;
            }

            if (session.refreshTokenExpiresAt <= 0)
            {
                return SupportsProtectedSessionStorage();
            }

            return SupportsProtectedSessionStorage() && session.refreshTokenExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static void PersistSession(OAuthSessionV2 session)
        {
            if (session == null)
            {
                return;
            }

            PersistPresenceHints(session);
            ClearLegacyKeys();

            if (!SupportsProtectedSessionStorage())
            {
                if (HasUsableAccessToken(session))
                {
                    EditorPrefs.SetString(KeyToken, session.accessToken);
                    EditorPrefs.SetInt(KeyExpiry, (int)session.accessTokenExpiresAt);
                }
                return;
            }

            string sessionJson = JsonUtility.ToJson(session);
            byte[] sessionBytes = Encoding.UTF8.GetBytes(sessionJson);

 #if UNITY_EDITOR_WIN
            byte[] protectedBytes = ProtectForCurrentUser(sessionBytes);
            string sessionPath = GetSessionFilePath();
            string sessionDir = Path.GetDirectoryName(sessionPath);
            if (!string.IsNullOrEmpty(sessionDir))
            {
                Directory.CreateDirectory(sessionDir);
            }

            string tempPath = sessionPath + ".tmp";
            File.WriteAllBytes(tempPath, protectedBytes);
            if (File.Exists(sessionPath))
            {
                File.Delete(sessionPath);
            }
            File.Move(tempPath, sessionPath);
#endif
        }

        private static OAuthSessionV2 LoadPersistentSession()
        {
            if (!SupportsProtectedSessionStorage())
            {
                return null;
            }

            try
            {
                string sessionPath = GetSessionFilePath();
                if (!File.Exists(sessionPath))
                {
                    return null;
                }

#if UNITY_EDITOR_WIN
                byte[] protectedBytes = File.ReadAllBytes(sessionPath);
                byte[] sessionBytes = UnprotectForCurrentUser(protectedBytes);
                string sessionJson = Encoding.UTF8.GetString(sessionBytes);
                var session = JsonUtility.FromJson<OAuthSessionV2>(sessionJson);
                if (session == null || session.storageVersion < 2)
                {
                    ClearPersistentSession();
                    return null;
                }

                return session;
#else
                return null;
#endif
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to read persistent session: {ex.Message}");
                ClearPersistentSession();
                return null;
            }
        }

        private static void ClearPersistentSession()
        {
            if (!SupportsProtectedSessionStorage())
            {
                return;
            }

            try
            {
                string sessionPath = GetSessionFilePath();
                if (File.Exists(sessionPath))
                {
                    File.Delete(sessionPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to clear persistent session: {ex.Message}");
            }
        }

        private static void PersistPresenceHints(OAuthSessionV2 session)
        {
            if (session == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(session.userId))
            {
                EditorPrefs.SetString(KeyUserId, session.userId);
            }

            if (!string.IsNullOrEmpty(session.displayName))
            {
                EditorPrefs.SetString(KeyDisplayName, session.displayName);
            }

            EditorPrefs.SetString(KeySessionVersion, CurrentSessionVersion);
        }

        private static void ClearLegacyKeys()
        {
            EditorPrefs.DeleteKey(KeyToken);
            EditorPrefs.DeleteKey(KeyExpiry);
            EditorPrefs.DeleteKey(KeyUserId);
            EditorPrefs.DeleteKey(KeyDisplayName);
        }

        private static bool SupportsProtectedSessionStorage()
        {
#if UNITY_EDITOR_WIN
            return true;
#else
            return false;
#endif
        }

        private static string GetSessionFilePath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "YUCP", "Auth", "unity-oauth-session-v2.dat");
        }

#if UNITY_EDITOR_WIN
        private static byte[] ProtectForCurrentUser(byte[] data)
        {
            return RunCryptOperation(data, true);
        }

        private static byte[] UnprotectForCurrentUser(byte[] data)
        {
            return RunCryptOperation(data, false);
        }

        private static byte[] RunCryptOperation(byte[] data, bool protect)
        {
            if (data == null || data.Length == 0)
            {
                return Array.Empty<byte>();
            }

            DataBlob inputBlob = default;
            DataBlob entropyBlob = default;
            DataBlob outputBlob = default;

            try
            {
                inputBlob = CreateBlob(data);
                entropyBlob = CreateBlob(SessionEntropy);

                bool success = protect
                    ? CryptProtectData(ref inputBlob, "YUCP Unity Session", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob)
                    : CryptUnprotectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);

                if (!success)
                {
                    throw new InvalidOperationException("Windows DPAPI operation failed.");
                }

                byte[] result = new byte[outputBlob.cbData];
                Marshal.Copy(outputBlob.pbData, result, 0, outputBlob.cbData);
                return result;
            }
            finally
            {
                FreeBlob(ref inputBlob);
                FreeBlob(ref entropyBlob);
                FreeBlob(ref outputBlob, true);
            }
        }

        private static DataBlob CreateBlob(byte[] data)
        {
            var blob = new DataBlob();
            if (data == null || data.Length == 0)
            {
                return blob;
            }

            blob.cbData = data.Length;
            blob.pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, blob.pbData, data.Length);
            return blob;
        }

        private static void FreeBlob(ref DataBlob blob, bool useLocalFree = false)
        {
            if (blob.pbData == IntPtr.Zero)
            {
                return;
            }

            if (useLocalFree)
            {
                LocalFree(blob.pbData);
            }
            else
            {
                Marshal.FreeHGlobal(blob.pbData);
            }

            blob.pbData = IntPtr.Zero;
            blob.cbData = 0;
        }
#endif

        private static bool IsInvalidGrantResponse(long responseCode, string responseBody)
        {
            if (responseCode != 400 && responseCode != 401)
            {
                return false;
            }

            string error = ExtractJsonString(responseBody, "error");
            if (string.Equals(error, "invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return responseBody.IndexOf("invalid_grant", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static async Task SendSuccessPageAsync(HttpListenerContext ctx)
        {
            byte[] html = Encoding.UTF8.GetBytes(BuildSuccessHtml());
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = html.Length;
            await ctx.Response.OutputStream.WriteAsync(html, 0, html.Length);
            ctx.Response.OutputStream.Close();
        }

        private static async Task SendErrorRedirectAsync(HttpListenerContext ctx, string serverUrl, string errorMessage)
        {
            try
            {
                string errorUrl = $"{serverUrl.TrimEnd('/')}/oauth/error?error={Uri.EscapeDataString(errorMessage)}";
                ctx.Response.Redirect(errorUrl);
                ctx.Response.Close();
            }
            catch
            {
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(BuildErrorHtml(errorMessage));
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    ctx.Response.OutputStream.Close();
                }
                catch
                {
                }
            }
        }

        private static string BuildAuthUrl(string serverUrl, string codeChallenge, string state, string redirectUri)
        {
            return $"{serverUrl.TrimEnd('/')}/api/yucp/oauth/authorize"
                + $"?client_id={Uri.EscapeDataString(ClientId)}"
                + "&response_type=code"
                + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
                + "&code_challenge_method=S256"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + $"&state={Uri.EscapeDataString(state)}"
                + "&scope=cert%3Aissue";
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string trimmedQuery = query?.TrimStart('?');
            if (string.IsNullOrEmpty(trimmedQuery))
            {
                return result;
            }

            foreach (string part in trimmedQuery.Split('&'))
            {
                int separator = part.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }

                result[Uri.UnescapeDataString(part.Substring(0, separator))] =
                    Uri.UnescapeDataString(part.Substring(separator + 1));
            }

            return result;
        }

        private static string ExtractJsonString(string json, string key)
        {
            string needle = $"\"{key}\"";
            int index = json.IndexOf(needle, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            index += needle.Length;
            while (index < json.Length && (json[index] == ' ' || json[index] == ':' || json[index] == '\t'))
            {
                index++;
            }

            if (index >= json.Length || json[index] != '"')
            {
                return null;
            }

            index++;
            var builder = new StringBuilder();
            while (index < json.Length && json[index] != '"')
            {
                if (json[index] == '\\' && index + 1 < json.Length)
                {
                    index++;
                    switch (json[index])
                    {
                        case '"':
                            builder.Append('"');
                            break;
                        case '\\':
                            builder.Append('\\');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        default:
                            builder.Append(json[index]);
                            break;
                    }
                }
                else
                {
                    builder.Append(json[index]);
                }

                index++;
            }

            return builder.ToString();
        }

        private static string ExtractJsonValue(string json, string key)
        {
            string needle = $"\"{key}\"";
            int index = json.IndexOf(needle, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            index += needle.Length;
            while (index < json.Length && (json[index] == ' ' || json[index] == ':' || json[index] == '\t'))
            {
                index++;
            }

            if (index >= json.Length)
            {
                return null;
            }

            var builder = new StringBuilder();
            while (index < json.Length && json[index] != ',' && json[index] != '}' && json[index] != '\r' && json[index] != '\n')
            {
                builder.Append(json[index++]);
            }

            return builder.ToString().Trim().Trim('"');
        }

        private static string ParseJwtClaim(string jwt, string claim)
        {
            try
            {
                string[] parts = jwt.Split('.');
                if (parts.Length < 2)
                {
                    return null;
                }

                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2:
                        payload += "==";
                        break;
                    case 3:
                        payload += "=";
                        break;
                }

                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                return ExtractJsonString(decoded, claim);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildErrorHtml(string errorMessage)
        {
            string escaped = System.Net.WebUtility.HtmlEncode(errorMessage);
            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <title>YUCP – Sign-in Failed</title>
  <style>
    *, *::before, *::after {{ box-sizing: border-box; margin: 0; padding: 0; }}
    body {{
      background: #0d0d0d;
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      display: flex; align-items: center; justify-content: center;
      min-height: 100vh; color: #fff;
    }}
    .card {{
      background: #1a1a1a; border-radius: 18px; border: 1px solid #2a2a2a;
      padding: 52px 44px; max-width: 460px; width: 100%; text-align: center;
      box-shadow: 0 28px 72px rgba(0,0,0,.65);
    }}
    .icon {{
      width: 76px; height: 76px;
      background: rgba(239,68,68,.15); border: 1.5px solid rgba(239,68,68,.3);
      border-radius: 50%; display: flex; align-items: center; justify-content: center;
      margin: 0 auto 28px; font-size: 34px; color: #EF4444;
    }}
    h1  {{ font-size: 22px; font-weight: 700; margin-bottom: 10px; }}
    p   {{ font-size: 14px; color: #808080; line-height: 1.65; margin-bottom: 18px; }}
    .detail {{
      background: rgba(239,68,68,.08); border: 1px solid rgba(239,68,68,.2);
      border-radius: 10px; padding: 12px 14px; text-align: left;
      font-family: Menlo, Consolas, monospace; font-size: 12px; color: #aaa;
      word-break: break-word; margin-bottom: 24px;
    }}
    .hint {{ font-size: 13px; color: #666; line-height: 1.6; }}
  </style>
</head>
<body>
  <div class=""card"">
    <div class=""icon"">&#9888;</div>
    <h1>Sign-in failed</h1>
    <p>An error occurred while connecting to the Unity Editor.</p>
    <div class=""detail"">{escaped}</div>
    <p class=""hint"">Return to Unity, close the signing dialog, and click
    <em>Sign in with Creator Account</em> to try again.</p>
  </div>
</body>
</html>";
        }

        private static string BuildSuccessHtml() => @"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <title>YUCP – Signed In</title>
  <link rel=""preconnect"" href=""https://fonts.googleapis.com"">
  <link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin>
  <link href=""https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@700;800&family=DM+Sans:wght@400;500&display=swap"" rel=""stylesheet"">
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      background: #0d0d0d;
      font-family: 'DM Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
      display: flex; align-items: center; justify-content: center;
      min-height: 100vh; color: #fff; overflow: hidden;
    }
    /* animated background blobs */
    .blobs { position: fixed; inset: 0; z-index: 0; pointer-events: none; }
    .blob {
      position: absolute; border-radius: 50%; filter: blur(90px); opacity: 0.15;
      animation: drift 10s ease-in-out infinite alternate;
    }
    .blob-1 { width:380px;height:380px;background:#36BFB1;top:-120px;left:-80px; animation-delay:0s; }
    .blob-2 { width:300px;height:300px;background:#5865F2;bottom:-60px;right:-60px; animation-delay:-4s; }
    @keyframes drift { from{transform:translate(0,0);} to{transform:translate(18px,22px);} }

    /* card */
    .card {
      position: relative; z-index: 1;
      background: rgba(255,255,255,0.04);
      backdrop-filter: blur(20px); -webkit-backdrop-filter: blur(20px);
      border: 1px solid rgba(255,255,255,0.1);
      border-radius: 28px;
      padding: 56px 48px 48px;
      max-width: 440px; width: 100%; text-align: center;
      box-shadow: 0 32px 80px rgba(0,0,0,0.6), 0 0 0 1px rgba(255,255,255,0.04) inset;
      animation: pop-in 0.4s cubic-bezier(0.22,1,0.36,1) both;
    }
    @keyframes pop-in {
      from { opacity:0; transform:scale(0.92) translateY(20px); }
      to   { opacity:1; transform:scale(1)    translateY(0);    }
    }

    /* check icon */
    .check-ring {
      width: 88px; height: 88px; border-radius: 50%;
      background: rgba(54,191,177,0.15);
      border: 1.5px solid rgba(54,191,177,0.35);
      display: flex; align-items: center; justify-content: center;
      margin: 0 auto 32px;
      animation: ring-glow 2.8s ease-in-out infinite;
    }
    @keyframes ring-glow {
      0%,100% { box-shadow: 0 0 0 0 rgba(54,191,177,0); }
      50%      { box-shadow: 0 0 0 10px rgba(54,191,177,0.1); }
    }
    .check-ring svg { width:40px; height:40px; color:#36BFB1; }

    h1 {
      font-family: 'Plus Jakarta Sans', sans-serif;
      font-size: 1.55rem; font-weight: 800;
      letter-spacing: -0.035em; margin-bottom: 10px;
    }
    .subtitle { font-size: 0.9rem; color: #888; line-height: 1.6; margin-bottom: 28px; }

    /* divider */
    .divider {
      height: 1px; background: rgba(255,255,255,0.07); margin: 0 auto 24px;
    }

    /* instruction row */
    .instruction {
      display: flex; align-items: center; gap: 14px;
      padding: 14px 16px;
      background: rgba(255,255,255,0.03);
      border: 1px solid rgba(255,255,255,0.07);
      border-radius: 14px;
    }
    .instruction-icon {
      flex-shrink: 0; width: 38px; height: 38px; border-radius: 10px;
      background: rgba(54,191,177,0.12); border: 1px solid rgba(54,191,177,0.2);
      display: flex; align-items: center; justify-content: center;
    }
    .instruction-icon svg { width:18px; height:18px; color:#36BFB1; }
    .instruction-text { font-size: 0.87rem; color: #aaa; line-height: 1.45; text-align: left; }
    .instruction-text strong { color: #fff; font-weight: 600; }
  </style>
</head>
<body>
  <div class=""blobs"">
    <div class=""blob blob-1""></div>
    <div class=""blob blob-2""></div>
  </div>

  <div class=""card"">
    <div class=""check-ring"">
      <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor""
           stroke-width=""2.5"" stroke-linecap=""round"" stroke-linejoin=""round"">
        <polyline points=""20 6 9 17 4 12""/>
      </svg>
    </div>

    <h1>You&#x2019;re signed in!</h1>
    <p class=""subtitle"">Your Creator Account is now connected to the Unity Editor.</p>

    <div class=""divider""></div>

    <div class=""instruction"">
      <div class=""instruction-icon"">
        <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor""
             stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
          <rect x=""2"" y=""3"" width=""20"" height=""14"" rx=""2""/>
          <line x1=""8"" y1=""21"" x2=""16"" y2=""21""/>
          <line x1=""12"" y1=""17"" x2=""12"" y2=""21""/>
        </svg>
      </div>
      <div class=""instruction-text"">
        <strong>Return to the Unity Editor</strong> — your signing certificate
        is being activated. You can close this tab.
      </div>
    </div>
  </div>
</body>
</html>";
    }
}
