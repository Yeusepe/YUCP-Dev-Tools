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
        private const string RequiredCertificateScope = "cert:issue";
        private const string RequiredProfileScope = "profile:read";
        private const string RequiredProductsScope = "products:read";
        // Standard OIDC scope: makes the server issue a (rotating) refresh token so the
        // session renews silently instead of forcing a new sign-in on every expiry.
        private const string OfflineAccessScope = "offline_access";
        private const int CurrentStorageVersion = 3;
        private const string CurrentSessionVersion = "3";
        private const string LegacySharedStoragePrefix = "YUCP_OAuth";
        private const string LegacySharedSessionFileName = "unity-oauth-session-v2.dat";
        private const int AccessTokenSkewSeconds = 60;
        private const int CallbackListenerStartAttempts = 10;
        private static readonly object SessionLock = new object();
        private static Task _backgroundRefreshTask;
        private static bool _isSignInInProgress;
        private static string _signInStatusMessage;

        public static event Action SignInStateChanged;

        public static bool IsSignInInProgress
        {
            get
            {
                lock (SessionLock)
                {
                    return _isSignInInProgress;
                }
            }
        }

        public static string SignInStatusMessage
        {
            get
            {
                lock (SessionLock)
                {
                    return _signInStatusMessage;
                }
            }
        }

        private sealed class OAuthDomainConfig
        {
            public OAuthDomainConfig(
                string clientId,
                string[] requestedScopes,
                string editorPrefsPrefix,
                string sessionFileName,
                string sessionEntropyLabel)
            {
                ClientId = clientId;
                RequestedScopes = requestedScopes;
                EditorPrefsPrefix = editorPrefsPrefix;
                SessionFileName = sessionFileName;
                SessionEntropyLabel = sessionEntropyLabel;
            }

            public string ClientId { get; }
            public string[] RequestedScopes { get; }
            public string EditorPrefsPrefix { get; }
            public string SessionFileName { get; }
            public string SessionEntropyLabel { get; }
            public string RequestedScopeValue => string.Join(" ", RequestedScopes);

            public string GetEditorPrefKey(string suffix)
            {
                return $"{EditorPrefsPrefix}_{suffix}";
            }
        }

        private static readonly OAuthDomainConfig Domain = new OAuthDomainConfig(
            clientId: "yucp-unity-creator",
            requestedScopes: new[] { RequiredCertificateScope, RequiredProfileScope, RequiredProductsScope, OfflineAccessScope },
            editorPrefsPrefix: "YUCP_CreatorOAuth",
            sessionFileName: "unity-creator-oauth-session-v2.dat",
            sessionEntropyLabel: "YUCP.UnityEditor.Creator.Session.v2");

        public static string ClientId => Domain.ClientId;

        private static string KeyToken => Domain.GetEditorPrefKey("AccessToken");
        private static string KeyExpiry => Domain.GetEditorPrefKey("TokenExpiry");
        private static string KeyUserId => Domain.GetEditorPrefKey("UserId");
        private static string KeyDisplayName => Domain.GetEditorPrefKey("DisplayName");
        private static string KeySessionVersion => Domain.GetEditorPrefKey("SessionVersion");
        private static readonly byte[] SessionEntropy = Encoding.UTF8.GetBytes(Domain.SessionEntropyLabel);

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

        // Windows Credential Manager (advapi32) — the OS secret store. The session
        // (already DPAPI-protected) is kept here instead of a loose file so the
        // sensitive tokens live in the user's protected credential vault.
        private const uint CredTypeGeneric = 1;
        private const uint CredPersistLocalMachine = 2;
        // CRED_MAX_CREDENTIAL_BLOB_SIZE: generic credential blobs cannot exceed this.
        private const int CredMaxBlobSize = 5 * 512;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CredentialNative
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
        private static extern bool CredWrite(ref CredentialNative credential, uint flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
        private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CredFree(IntPtr buffer);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW")]
        private static extern bool CredDelete(string targetName, uint type, uint flags);
#endif

        [Serializable]
        private class OAuthSessionV2
        {
            public int storageVersion = CurrentStorageVersion;
            public string accessToken;
            public long accessTokenExpiresAt;
            public string refreshToken;
            public long refreshTokenExpiresAt;
            public string userId;
            public string displayName;
            public string imageUrl;
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

        public static string GetProfileImageUrl()
        {
            return TryGetCachedSession(out OAuthSessionV2 session) && !string.IsNullOrEmpty(session.imageUrl)
                ? session.imageUrl
                : null;
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
                Debug.LogWarning(
                    $"[YUCP OAuth] Discarding legacy shared Unity session because it cannot prove required Unity scopes '{Domain.RequestedScopeValue}'.");
                ClearLegacySharedSessionArtifacts();
            }

            return null;
        }

        public static void SignOut()
        {
            ClearPersistentSession();
            ClearCurrentDomainKeys();
            ClearLegacySharedSessionArtifacts();
        }

        public static async Task SignInAsync(string serverUrl, Action onSuccess, Action<string> onError)
        {
            if (!TryBeginSignIn())
            {
                Debug.LogWarning("[YUCP OAuth] Ignoring duplicate sign-in request because another sign-in is already in progress.");
                return;
            }

            Debug.Log("[YUCP OAuth] SignInAsync started");
            try
            {
                SetSignInStatus("Preparing Creator Assistant sign-in...");
                await Task.Yield();

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

                byte[] stateBytes = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(stateBytes);
                }
                string state = Base64UrlEncode(stateBytes);

                SetSignInStatus("Starting local sign-in callback...");
                var httpListener = StartLoopbackListener(out int port);
                Debug.Log($"[YUCP OAuth] Using loopback port {port}");

                string redirectUri = $"http://127.0.0.1:{port}/callback";
                string authUrl = BuildAuthUrl(serverUrl, codeChallenge, state, redirectUri);
                Debug.Log($"[YUCP OAuth] Auth URL: {authUrl}");

                Debug.Log($"[YUCP OAuth] HttpListener started on http://127.0.0.1:{port}/");

                SetSignInStatus("Opening Creator Assistant in your browser...");
                Application.OpenURL(authUrl);
                Debug.Log("[YUCP OAuth] Browser opened, waiting for callback...");
                SetSignInStatus("Waiting for browser sign-in...");

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
                        string msg = BuildAuthorizationErrorMessage(desc, Domain.RequestedScopeValue);
                        Debug.LogError($"[YUCP OAuth] {msg}");
                        await SendErrorRedirectAsync(context, serverUrl, msg);
                        onError?.Invoke(msg);
                        return;
                    }

                    if (!qp.TryGetValue("state", out string returnedState) || returnedState != state)
                    {
                        const string msg = "State mismatch during sign-in. Please try again.";
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
                    SetSignInStatus("Completing secure sign-in...");
                    await SendSuccessPageAsync(context);
                }
                finally
                {
                    try { httpListener.Stop(); } catch { }
                    try { httpListener.Close(); } catch { }
                    Debug.Log("[YUCP OAuth] HttpListener stopped.");
                }

                SetSignInStatus("Exchanging sign-in code...");
                Debug.Log($"[YUCP OAuth] Exchanging auth code at {serverUrl.TrimEnd('/')}/api/auth/oauth2/token");
                using var tokenReq = CreateTokenRequest(
                    serverUrl,
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "authorization_code",
                        ["client_id"] = ClientId,
                        ["code"] = authCode,
                        ["code_verifier"] = codeVerifier,
                        ["redirect_uri"] = redirectUri,
                    });

                var op = tokenReq.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }

                string tokenJson = tokenReq.downloadHandler.text;
                Debug.Log($"[YUCP OAuth] Token response {tokenReq.responseCode}: {DescribeTokenResponse(tokenJson)}");

                if (tokenReq.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(BuildTokenExchangeErrorMessage(tokenReq.responseCode, tokenReq.error, tokenJson));
                    return;
                }

                OAuthSessionV2 session = BuildSessionFromTokenResponse(tokenJson, null);
                if (session == null || string.IsNullOrEmpty(session.accessToken))
                {
                    onError?.Invoke($"No access_token in server response: {DescribeTokenResponse(tokenJson)}");
                    return;
                }

                if (!HasRequiredUnityScopes(session.scope))
                {
                    string missingScopes = GetMissingRequiredScopes(session.scope);
                    onError?.Invoke($"Sign-in token is missing required Unity scope(s): {missingScopes}. Please sign out and try again.");
                    return;
                }

                SetSignInStatus("Loading creator profile...");
                session = await EnrichSessionWithProfileAsync(serverUrl, session);
                SetSignInStatus("Finalizing signing trust...");
                var signingService = new PackageSigningService(serverUrl);
                if (!await signingService.FetchAndCacheRootPublicKeyAsync())
                {
                    onError?.Invoke("The signing server did not advertise a pinned YUCP trust root.");
                    return;
                }

                PersistSession(session);
                QueueFocusRelevantWindows();
                Debug.Log($"[YUCP OAuth] Access token obtained (length {session.accessToken.Length}).");
                Debug.Log($"[YUCP OAuth] Signed in as '{session.displayName}' (sub={session.userId}).");

                Debug.Log("[YUCP OAuth] Sign-in complete.");
                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP OAuth] Unhandled exception: {ex}");
                onError?.Invoke($"Sign-in error: {ex.Message}");
            }
            finally
            {
                EndSignIn();
            }
        }

        private static bool TryBeginSignIn()
        {
            lock (SessionLock)
            {
                if (_isSignInInProgress)
                    return false;

                _isSignInInProgress = true;
                _signInStatusMessage = "Preparing Creator Assistant sign-in...";
            }

            QueueSignInStateChanged();
            return true;
        }

        private static void EndSignIn()
        {
            lock (SessionLock)
            {
                if (!_isSignInInProgress)
                    return;

                _isSignInInProgress = false;
                _signInStatusMessage = null;
            }

            QueueSignInStateChanged();
        }

        private static void SetSignInStatus(string message)
        {
            bool changed;
            lock (SessionLock)
            {
                changed = !string.Equals(_signInStatusMessage, message, StringComparison.Ordinal);
                _signInStatusMessage = message;
            }

            if (changed)
            {
                QueueSignInStateChanged();
            }
        }

        private static void QueueSignInStateChanged()
        {
            EditorApplication.delayCall += () => SignInStateChanged?.Invoke();
        }

        private static HttpListener StartLoopbackListener(out int port)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= CallbackListenerStartAttempts; attempt++)
            {
                port = ReserveEphemeralLoopbackPort();
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");

                try
                {
                    listener.Start();
                    return listener;
                }
                catch (Exception ex) when (
                    ex is HttpListenerException ||
                    ex is SocketException ||
                    ex is ObjectDisposedException)
                {
                    lastException = ex;
                    try { listener.Close(); } catch { }

                    Debug.LogWarning(
                        $"[YUCP OAuth] Failed to start loopback listener on port {port} " +
                        $"(attempt {attempt}/{CallbackListenerStartAttempts}): {ex.Message}");
                }
            }

            port = 0;
            throw new InvalidOperationException(
                "Unity could not start the local sign-in callback listener. " +
                "Check whether security software or another process is blocking loopback callbacks.",
                lastException);
        }

        private static int ReserveEphemeralLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                probe.Start();
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        private static void QueueFocusRelevantWindows()
        {
            EditorApplication.delayCall += () =>
            {
                EditorWindow.FocusWindowIfItsOpen<YUCP.DevTools.Editor.PackageSigning.UI.SigningSettingsWindow>();
                EditorWindow.FocusWindowIfItsOpen<YUCP.DevTools.Editor.PackageExporter.YUCPPackageExporterWindow>();
            };
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

            using var tokenReq = CreateTokenRequest(
                serverUrl,
                new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = ClientId,
                    ["refresh_token"] = currentSession.refreshToken,
                });

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

            if (!HasRequiredUnityScopes(refreshedSession.scope))
            {
                string missingScopes = GetMissingRequiredScopes(refreshedSession.scope);
                Debug.LogWarning(
                    $"[YUCP OAuth] Refreshed session is missing required Unity scope(s): {missingScopes}. Clearing the current auth domain session.");
                SignOut();
                return null;
            }

            refreshedSession = await EnrichSessionWithProfileAsync(serverUrl, refreshedSession);
            PersistSession(refreshedSession);
            return refreshedSession.accessToken;
        }

        private static OAuthSessionV2 BuildSessionFromTokenResponse(string tokenJson, OAuthSessionV2 previousSession)
        {
            string accessToken = ExtractJsonStringAny(tokenJson, "access_token", "accessToken");
            if (string.IsNullOrEmpty(accessToken))
            {
                return null;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long accessTokenExpiresAt = ResolveExpiryTimestamp(
                ExtractJsonValueAny(tokenJson, "access_token_expires_at", "accessTokenExpiresAt", "expires_at", "expiresAt"),
                ExtractJsonValueAny(tokenJson, "expires_in", "expiresIn"),
                now + 3600 - AccessTokenSkewSeconds);

            string refreshToken = ExtractJsonStringAny(tokenJson, "refresh_token", "refreshToken");
            if (string.IsNullOrEmpty(refreshToken))
            {
                refreshToken = previousSession?.refreshToken;
            }

            long refreshTokenExpiresAt = ResolveRefreshExpiryTimestamp(
                ExtractJsonValueAny(tokenJson, "refresh_token_expires_at", "refreshTokenExpiresAt"),
                ExtractJsonValueAny(tokenJson, "refresh_token_expires_in", "refreshTokenExpiresIn"),
                previousSession?.refreshTokenExpiresAt ?? 0);
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

            string imageUrl = previousSession?.imageUrl;

            return new OAuthSessionV2
            {
                storageVersion = CurrentStorageVersion,
                accessToken = accessToken,
                accessTokenExpiresAt = accessTokenExpiresAt,
                refreshToken = refreshToken,
                refreshTokenExpiresAt = refreshTokenExpiresAt,
                userId = userId,
                displayName = displayName,
                imageUrl = imageUrl,
                scope = scope,
            };
        }

        private static UnityWebRequest CreateTokenRequest(string serverUrl, IReadOnlyDictionary<string, string> fields)
        {
            string endpoint = $"{serverUrl.TrimEnd('/')}/api/auth/oauth2/token";
            string body = BuildFormUrlEncodedBody(fields);
            var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Accept-Encoding", "identity");
            return request;
        }

        private static UnityWebRequest CreateProfileRequest(string serverUrl, string accessToken)
        {
            string endpoint = $"{serverUrl.TrimEnd('/')}/v1/me";
            var request = UnityWebRequest.Get(endpoint);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Accept-Encoding", "identity");
            return request;
        }

        private static async Task<OAuthSessionV2> EnrichSessionWithProfileAsync(string serverUrl, OAuthSessionV2 session)
        {
            if (session == null
                || string.IsNullOrEmpty(serverUrl)
                || string.IsNullOrEmpty(session.accessToken)
                || !HasRequiredScope(session.scope, RequiredProfileScope))
            {
                return session;
            }

            using var request = CreateProfileRequest(serverUrl, session.accessToken);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            string profileJson = request.downloadHandler?.text ?? string.Empty;
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[YUCP OAuth] Profile fetch failed ({request.responseCode}): {request.error}. Response: {BuildProfileResponseSummary(profileJson)}");
                return session;
            }

            string profileUserId = ExtractJsonStringAny(profileJson, "authUserId", "sub");
            if (!string.IsNullOrEmpty(profileUserId))
            {
                session.userId = profileUserId;
            }

            string profileName = ExtractJsonString(profileJson, "name");
            if (!string.IsNullOrEmpty(profileName))
            {
                session.displayName = profileName;
            }

            session.imageUrl = ExtractJsonString(profileJson, "image");
            return session;
        }

        private static string BuildFormUrlEncodedBody(IReadOnlyDictionary<string, string> fields)
        {
            var builder = new StringBuilder();
            foreach (KeyValuePair<string, string> field in fields)
            {
                if (builder.Length > 0)
                {
                    builder.Append('&');
                }

                builder.Append(EncodeFormComponent(field.Key));
                builder.Append('=');
                builder.Append(EncodeFormComponent(field.Value));
            }

            return builder.ToString();
        }

        private static string EncodeFormComponent(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty).Replace("%20", "+");
        }

        private static long ResolveExpiryTimestamp(string absoluteExpiryRaw, string expiresInRaw, long fallback)
        {
            if (long.TryParse(absoluteExpiryRaw, out long absoluteExpiry) && absoluteExpiry > 0)
            {
                return absoluteExpiry;
            }

            if (int.TryParse(expiresInRaw, out int expiresInSeconds) && expiresInSeconds > 0)
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds - AccessTokenSkewSeconds;
            }

            return fallback;
        }

        private static long ResolveRefreshExpiryTimestamp(string absoluteExpiryRaw, string expiresInRaw, long previousValue)
        {
            if (long.TryParse(absoluteExpiryRaw, out long absoluteExpiry) && absoluteExpiry > 0)
            {
                return absoluteExpiry;
            }

            if (int.TryParse(expiresInRaw, out int expiresInSeconds) && expiresInSeconds > 0)
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds;
            }

            return previousValue;
        }

        private static bool TryGetActiveSession(out OAuthSessionV2 session)
        {
            if (TryGetCachedSession(out session))
            {
                if (HasUsableAccessToken(session) || (IsRefreshableSession(session) && HasRequiredUnityScopes(session.scope)))
                {
                    PersistPresenceHints(session);
                    return true;
                }
            }

            if (TryGetLegacyAccessToken(out string legacyToken, out long legacyExpiry))
            {
                Debug.LogWarning(
                    $"[YUCP OAuth] Clearing legacy shared Unity session because it cannot prove required Unity scopes '{Domain.RequestedScopeValue}'.");
                ClearLegacySharedSessionArtifacts();
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

            if (!EditorPrefs.HasKey(GetLegacySharedKey("AccessToken")) || !EditorPrefs.HasKey(GetLegacySharedKey("TokenExpiry")))
            {
                return false;
            }

            token = EditorPrefs.GetString(GetLegacySharedKey("AccessToken"), string.Empty);
            expiry = EditorPrefs.GetInt(GetLegacySharedKey("TokenExpiry"), 0);
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
                && HasRequiredUnityScopes(session.scope)
                && session.accessTokenExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + AccessTokenSkewSeconds;
        }

        private static bool HasRequiredUnityScopes(string scopeValue)
        {
            foreach (string requiredScope in Domain.RequestedScopes)
            {
                if (!HasRequiredScope(scopeValue, requiredScope))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetMissingRequiredScopes(string scopeValue)
        {
            var missingScopes = new List<string>();
            foreach (string requiredScope in Domain.RequestedScopes)
            {
                if (!HasRequiredScope(scopeValue, requiredScope))
                {
                    missingScopes.Add(requiredScope);
                }
            }

            return missingScopes.Count == 0 ? "none" : string.Join(" ", missingScopes.ToArray());
        }

        private static bool HasRequiredScope(string scopeValue, string requiredScope)
        {
            if (string.IsNullOrWhiteSpace(scopeValue) || string.IsNullOrWhiteSpace(requiredScope))
            {
                return false;
            }

            string[] scopes = scopeValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string scope in scopes)
            {
                if (string.Equals(scope.Trim(), requiredScope, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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

            ClearCurrentDomainKeys();
            ClearLegacySharedSessionArtifacts();
            PersistPresenceHints(session);

            if (!SupportsProtectedSessionStorage())
            {
                if (HasUsableAccessToken(session))
                {
                    EditorPrefs.SetString(KeyToken, session.accessToken);
                    EditorPrefs.SetInt(KeyExpiry, (int)session.accessTokenExpiresAt);
                }
                return;
            }

#if UNITY_EDITOR_WIN
            string sessionJson = JsonUtility.ToJson(session);
            byte[] sessionBytes = Encoding.UTF8.GetBytes(sessionJson);
            byte[] protectedBytes = ProtectForCurrentUser(sessionBytes);

            // Preferred store: the Windows Credential Manager vault.
            if (protectedBytes.Length <= CredMaxBlobSize && TryWriteCredential(GetCredentialTarget(), protectedBytes))
            {
                DeleteSessionFile();
                return;
            }

            // Fallback (e.g. blob exceeds the credential size cap): the DPAPI-protected file.
            WriteSessionFile(protectedBytes);
#endif
        }

        private static OAuthSessionV2 LoadPersistentSession()
        {
            if (!SupportsProtectedSessionStorage())
            {
                return null;
            }

#if UNITY_EDITOR_WIN
            try
            {
                // Prefer the credential vault; fall back to a pre-existing DPAPI file (migration).
                byte[] protectedBytes = TryReadCredential(GetCredentialTarget()) ?? ReadSessionFile();
                if (protectedBytes == null)
                {
                    return null;
                }

                byte[] sessionBytes = UnprotectForCurrentUser(protectedBytes);
                string sessionJson = Encoding.UTF8.GetString(sessionBytes);
                var session = JsonUtility.FromJson<OAuthSessionV2>(sessionJson);
                if (session == null || session.storageVersion < CurrentStorageVersion)
                {
                    ClearPersistentSession();
                    return null;
                }

                return session;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to read persistent session: {ex.Message}");
                ClearPersistentSession();
                return null;
            }
#else
            return null;
#endif
        }

        private static void ClearPersistentSession()
        {
            if (!SupportsProtectedSessionStorage())
            {
                return;
            }

#if UNITY_EDITOR_WIN
            try
            {
                DeleteCredential(GetCredentialTarget());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to clear credential session: {ex.Message}");
            }

            DeleteSessionFile();
#endif
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

        private static void ClearCurrentDomainKeys()
        {
            EditorPrefs.DeleteKey(KeyToken);
            EditorPrefs.DeleteKey(KeyExpiry);
            EditorPrefs.DeleteKey(KeyUserId);
            EditorPrefs.DeleteKey(KeyDisplayName);
            EditorPrefs.DeleteKey(KeySessionVersion);
        }

        private static string GetLegacySharedKey(string suffix)
        {
            return $"{LegacySharedStoragePrefix}_{suffix}";
        }

        private static void ClearLegacySharedSessionArtifacts()
        {
            EditorPrefs.DeleteKey(GetLegacySharedKey("AccessToken"));
            EditorPrefs.DeleteKey(GetLegacySharedKey("TokenExpiry"));
            EditorPrefs.DeleteKey(GetLegacySharedKey("UserId"));
            EditorPrefs.DeleteKey(GetLegacySharedKey("DisplayName"));
            EditorPrefs.DeleteKey(GetLegacySharedKey("SessionVersion"));

            if (!SupportsProtectedSessionStorage())
            {
                return;
            }

            try
            {
                string legacySessionPath = GetLegacySharedSessionFilePath();
                if (File.Exists(legacySessionPath))
                {
                    File.Delete(legacySessionPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to clear legacy shared session: {ex.Message}");
            }
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
            return Path.Combine(localAppData, "YUCP", "Auth", Domain.SessionFileName);
        }

        private static string GetLegacySharedSessionFilePath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "YUCP", "Auth", LegacySharedSessionFileName);
        }

#if UNITY_EDITOR_WIN
        // Unique vault key per auth domain so the user and creator sessions never collide.
        private static string GetCredentialTarget()
        {
            return $"YUCP/{Domain.ClientId}/session";
        }

        private static bool TryWriteCredential(string target, byte[] blob)
        {
            IntPtr blobPtr = IntPtr.Zero;
            try
            {
                blobPtr = Marshal.AllocHGlobal(blob.Length);
                Marshal.Copy(blob, 0, blobPtr, blob.Length);

                var credential = new CredentialNative
                {
                    Type = CredTypeGeneric,
                    TargetName = target,
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = blobPtr,
                    Persist = CredPersistLocalMachine,
                    UserName = Domain.ClientId,
                };

                if (!CredWrite(ref credential, 0))
                {
                    Debug.LogWarning($"[YUCP OAuth] CredWrite failed (Win32 error {Marshal.GetLastWin32Error()}).");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to write credential: {ex.Message}");
                return false;
            }
            finally
            {
                if (blobPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(blobPtr);
                }
            }
        }

        private static byte[] TryReadCredential(string target)
        {
            IntPtr credPtr = IntPtr.Zero;
            try
            {
                if (!CredRead(target, CredTypeGeneric, 0, out credPtr))
                {
                    return null;
                }

                var credential = (CredentialNative)Marshal.PtrToStructure(credPtr, typeof(CredentialNative));
                if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
                {
                    return null;
                }

                byte[] blob = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, blob, 0, (int)credential.CredentialBlobSize);
                return blob;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to read credential: {ex.Message}");
                return null;
            }
            finally
            {
                if (credPtr != IntPtr.Zero)
                {
                    CredFree(credPtr);
                }
            }
        }

        private static void DeleteCredential(string target)
        {
            if (!CredDelete(target, CredTypeGeneric, 0))
            {
                int err = Marshal.GetLastWin32Error();
                // 1168 == ERROR_NOT_FOUND, expected when no session was stored yet.
                if (err != 1168)
                {
                    Debug.LogWarning($"[YUCP OAuth] CredDelete failed (Win32 error {err}).");
                }
            }
        }

        private static void WriteSessionFile(byte[] protectedBytes)
        {
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
        }

        private static byte[] ReadSessionFile()
        {
            string sessionPath = GetSessionFilePath();
            return File.Exists(sessionPath) ? File.ReadAllBytes(sessionPath) : null;
        }

        private static void DeleteSessionFile()
        {
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
                Debug.LogWarning($"[YUCP OAuth] Failed to delete legacy session file: {ex.Message}");
            }
        }
#endif

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

        private static string BuildTokenExchangeErrorMessage(long responseCode, string requestError, string tokenJson)
        {
            string oauthError = ExtractJsonString(tokenJson, "error");
            string oauthDescription = ExtractJsonString(tokenJson, "error_description");
            string detail = !string.IsNullOrEmpty(oauthDescription)
                ? oauthDescription
                : !string.IsNullOrEmpty(oauthError)
                    ? oauthError
                    : requestError;
            return $"Token exchange failed ({responseCode}): {detail}";
        }

        private static string DescribeTokenResponse(string tokenJson)
        {
            bool hasAccessToken = !string.IsNullOrEmpty(ExtractJsonStringAny(tokenJson, "access_token", "accessToken"));
            bool hasRefreshToken = !string.IsNullOrEmpty(ExtractJsonStringAny(tokenJson, "refresh_token", "refreshToken"));
            string scope = ExtractJsonString(tokenJson, "scope") ?? string.Empty;
            return $"{{ hasAccessToken: {hasAccessToken.ToString().ToLowerInvariant()}, hasRefreshToken: {hasRefreshToken.ToString().ToLowerInvariant()}, scope: \"{scope}\" }}";
        }

        private static string BuildProfileResponseSummary(string profileJson)
        {
            string authUserId = ExtractJsonStringAny(profileJson, "authUserId", "sub") ?? string.Empty;
            string name = ExtractJsonString(profileJson, "name") ?? string.Empty;
            bool hasImage = !string.IsNullOrEmpty(ExtractJsonString(profileJson, "image"));
            return $"{{ authUserId: \"{authUserId}\", hasName: {(!string.IsNullOrEmpty(name)).ToString().ToLowerInvariant()}, hasImage: {hasImage.ToString().ToLowerInvariant()} }}";
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
            byte[] bytes = Encoding.UTF8.GetBytes(BuildErrorHtml(errorMessage));
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
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
                + $"&scope={Uri.EscapeDataString(Domain.RequestedScopeValue)}";
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

        private static string ExtractJsonStringAny(string json, params string[] keys)
        {
            if (keys == null)
            {
                return null;
            }

            foreach (string key in keys)
            {
                string value = ExtractJsonString(json, key);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return null;
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

        private static string ExtractJsonValueAny(string json, params string[] keys)
        {
            if (keys == null)
            {
                return null;
            }

            foreach (string key in keys)
            {
                string value = ExtractJsonValue(json, key);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return null;
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

        private static string BuildAuthorizationErrorMessage(string description, string expectedScope)
        {
            string normalized = NormalizeAuthorizationDescription(description);
            if (TryExtractInvalidScope(normalized, out string invalidScope))
            {
                string scopeLabel = string.IsNullOrEmpty(invalidScope) ? expectedScope : invalidScope;
                return $"Authorization error: This YUCP server is not ready for Unity package signing yet. The deployment rejected the required Unity scope '{scopeLabel}'. Return to Unity and try again later.";
            }

            return $"Authorization error: {normalized}";
        }

        private static string NormalizeAuthorizationDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return "The server returned an unknown authorization error.";
            }

            return description.Replace('+', ' ').Trim();
        }

        private static bool TryExtractInvalidScope(string description, out string scope)
        {
            const string marker = "The following scopes are invalid:";
            int index = description.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                scope = null;
                return false;
            }

            string remainder = description.Substring(index + marker.Length).Trim();
            if (string.IsNullOrEmpty(remainder))
            {
                scope = null;
                return true;
            }

            int separator = remainder.IndexOfAny(new[] { ',', ';' });
            scope = (separator >= 0 ? remainder.Substring(0, separator) : remainder).Trim();
            return true;
        }

        private static string BuildErrorHtml(string errorMessage)
        {
            string escaped = WebUtility.HtmlEncode(errorMessage);
            string details = $"<div class=\"detail-card\"><span class=\"detail-label\">Details</span><div class=\"detail-body\">{escaped}</div></div>";
            return BuildOAuthPageHtml(
                "Sign-in failed",
                "We could not finish the YUCP sign-in",
                "Return to Unity, review the details below, and try again once the server is ready.",
                details,
                "#fb7185",
                "#f59e0b");
        }

        private static string BuildSuccessHtml()
        {
            return BuildOAuthPageHtml(
                "Connected",
                "Creator signing is connected",
                "Return to Unity. Your YUCP package signing tools are ready to request or restore this device certificate.",
                "<div class=\"detail-card detail-card-success\"><span class=\"detail-label\">Next</span><div class=\"detail-body\">You can close this tab and continue in Unity.</div></div>",
                "#36bfb1",
                "#2da89c");
        }

        private static string BuildOAuthPageHtml(string badge, string title, string message, string detailHtml, string accentStart, string accentEnd)
        {
            string escapedBadge = WebUtility.HtmlEncode(badge);
            string escapedTitle = WebUtility.HtmlEncode(title);
            string escapedMessage = WebUtility.HtmlEncode(message);
            string html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <title>YUCP Creator Identity</title>
  <link rel=""preconnect"" href=""https://fonts.googleapis.com"">
  <link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin>
  <link href=""https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@700;800&family=DM+Sans:wght@400;500&display=swap"" rel=""stylesheet"">
  <svg style=""display:none"" aria-hidden=""true"">
    <filter id=""liq-sm"" x=""-8%"" y=""-8%"" width=""116%"" height=""116%"" color-interpolation-filters=""sRGB"">
      <feTurbulence type=""fractalNoise"" baseFrequency=""0.018 0.024"" numOctaves=""3"" seed=""7"" result=""noise"" />
      <feGaussianBlur in=""noise"" stdDeviation=""2.5"" result=""smooth"" />
      <feDisplacementMap in=""SourceGraphic"" in2=""smooth"" scale=""6"" xChannelSelector=""R"" yChannelSelector=""G"" />
    </filter>
  </svg>
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      min-height: 100vh;
      font-family: 'DM Sans', 'Segoe UI', system-ui, sans-serif;
      color: rgba(255,255,255,0.92);
      background: #779dc3;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 24px;
      overflow-x: hidden;
    }
    /* Static sky placeholder with white blobs */
    .sky {
      position: fixed;
      inset: -8%;
      pointer-events: none;
      z-index: 0;
    }
    .sky::before {
      content: """";
      position: absolute;
      inset: 0;
      background:
        radial-gradient(circle at 20% 54%, rgba(255,255,255,0.42) 0, rgba(255,255,255,0.42) 10%, rgba(255,255,255,0.18) 16%, transparent 24%),
        radial-gradient(circle at 76% 28%, rgba(255,255,255,0.34) 0, rgba(255,255,255,0.34) 8%,  rgba(255,255,255,0.12) 14%, transparent 21%),
        radial-gradient(circle at 48% 78%, rgba(255,255,255,0.28) 0, rgba(255,255,255,0.28) 12%, rgba(255,255,255,0.10) 18%, transparent 26%);
      filter: blur(28px);
      opacity: 0.95;
    }
    .sky::after {
      content: """";
      position: absolute;
      inset: 0;
      background:
        radial-gradient(ellipse 32% 20% at 14% 48%, rgba(255,255,255,0.55) 0%, transparent 100%),
        radial-gradient(ellipse 22% 14% at 74% 26%, rgba(255,255,255,0.50) 0%, transparent 100%),
        radial-gradient(ellipse 44% 24% at 46% 70%, rgba(255,255,255,0.45) 0%, transparent 100%);
      filter: blur(20px);
      opacity: 0.55;
    }
    /* Card shell */
    .shell {
      position: relative;
      z-index: 1;
      width: min(380px, 100%);
      animation: fadein 0.5s cubic-bezier(0.22,1,0.36,1) both;
    }
    @keyframes fadein {
      from { opacity: 0; transform: translateY(14px) scale(0.984); }
      to   { opacity: 1; transform: none; }
    }
    /* Glass card */
    .card {
      position: relative;
      background: rgba(0,0,0,0.28);
      backdrop-filter: blur(24px) saturate(160%);
      -webkit-backdrop-filter: blur(24px) saturate(160%);
      border: 1px solid rgba(255,255,255,0.13);
      box-shadow:
        0 24px 64px rgba(0,0,0,0.45),
        inset 0 1px 0 rgba(255,255,255,0.10),
        inset 0 -1px 0 rgba(0,0,0,0.15);
      border-radius: 28px;
      padding: 40px 36px 32px;
      overflow: hidden;
      text-align: center;
    }
    .card::before {
      content: """";
      position: absolute;
      top: 0; left: 50%;
      transform: translateX(-50%);
      width: 55%; height: 1px;
      background: linear-gradient(90deg, transparent, __ACCENT_START__, transparent);
      opacity: 0.6;
      pointer-events: none;
    }
    .card::after {
      content: """";
      position: absolute;
      inset: 0;
      border-radius: inherit;
      pointer-events: none;
      background:
        linear-gradient(180deg, rgba(255,255,255,0.18) 0%, rgba(255,255,255,0.06) 14%, transparent 34%),
        radial-gradient(circle at 22% 0%, rgba(255,255,255,0.14), transparent 34%);
      filter: url(#liq-sm);
      opacity: 0.9;
    }
    h1 {
      font-family: 'Plus Jakarta Sans', 'Segoe UI', system-ui, sans-serif;
      font-size: 20px;
      font-weight: 800;
      letter-spacing: -0.04em;
      color: #fff;
      line-height: 1.12;
      margin: 0 0 8px;
    }
    .body-copy {
      font-size: 13px;
      line-height: 1.65;
      color: rgba(255,255,255,0.5);
      margin: 0 0 20px;
    }
    .divider {
      width: 100%; height: 1px;
      background: rgba(255,255,255,0.07);
      margin: 0 0 18px;
    }
    .detail-card {
      border-radius: 12px;
      border: 1px solid rgba(255,255,255,0.08);
      background: rgba(255,255,255,0.04);
      padding: 14px 16px;
    }
    .detail-card-success {
      background: rgba(54,191,177,0.07);
      border-color: rgba(54,191,177,0.22);
    }
    .detail-card-error {
      background: rgba(239,68,68,0.07);
      border-color: rgba(239,68,68,0.22);
    }
    .detail-label {
      display: block;
      font-size: 9px;
      font-weight: 700;
      letter-spacing: 0.14em;
      text-transform: uppercase;
      color: rgba(255,255,255,0.32);
      margin-bottom: 7px;
      font-family: 'Plus Jakarta Sans', 'Segoe UI', system-ui, sans-serif;
    }
    .detail-body {
      font-size: 13px;
      line-height: 1.6;
      color: rgba(255,255,255,0.78);
      word-break: break-word;
    }
    .logo-wrap {
      display: flex;
      justify-content: center;
      margin-top: 18px;
    }
    .logo-wrap img {
      width: min(220px, 72%);
      height: auto;
      object-fit: contain;
      filter: drop-shadow(0 10px 28px rgba(0,0,0,0.22));
    }
    @media (prefers-reduced-motion: reduce) {
      .shell { animation: none; }
    }
  </style>
</head>
<body>
  <div class=""sky""></div>
  <div class=""shell"">
    <div class=""card"">
      <h1>__TITLE__</h1>
      <p class=""body-copy"">__MESSAGE__</p>
      <div class=""divider""></div>
      __DETAIL_HTML__
    </div>
    <div class=""logo-wrap"">
      <img src=""https://raw.githubusercontent.com/Yeusepe/YUCP-Creator-Assistant/refs/heads/main/apps/web/public/Icons/MainLogo.png"" alt=""YUCP"" />
    </div>
  </div>
</body>
</html>";

            return html
                .Replace("__BADGE__", escapedBadge)
                .Replace("__TITLE__", escapedTitle)
                .Replace("__MESSAGE__", escapedMessage)
                .Replace("__DETAIL_HTML__", detailHtml)
                .Replace("__ACCENT_START__", accentStart)
                .Replace("__ACCENT_END__", accentEnd);
        }

    }
}
