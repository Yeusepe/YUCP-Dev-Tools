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
        // Scopes the exporter actually needs, one per route it calls:
        //   cert:issue        POST /v1/certificates, GET /v1/certificates/{me,devices}
        //   products:read     GET /v1/products (Gumroad/Jinxxy pickers)
        //   verification:read GET /v1/me (profile card)
        private const string RequiredCertIssueScope = "cert:issue";
        private const string RequiredProductsReadScope = "products:read";
        private const string RequiredVerificationReadScope = "verification:read";
        // Standard OIDC scope: makes the server issue a (rotating) refresh token so the
        // session renews silently instead of forcing a new sign-in on every expiry.
        private const string OfflineAccessScope = "offline_access";
        private const int CurrentStorageVersion = 5;
        private const int AccessTokenSkewSeconds = 60;

        // Must match the registered redirect URI path. RFC 8252 §7.3 lets the
        // server vary only the loopback port, so scheme, host, and path are fixed.
        private const string CallbackPath = "/callback";
        private const int CallbackListenerStartAttempts = 10;
        private static readonly object SessionLock = new object();
        private static Task _backgroundRefreshTask;
        private static bool _isSignInInProgress;
        private static string _signInStatusMessage;

        // Reading the session means a Credential Manager lookup plus a DPAPI
        // decrypt. IsSignedIn/GetDisplayName/GetProfileImageUrl are called from
        // editor repaint code, so an uncached read runs that per frame. Hold the
        // decrypted session briefly instead; the TTL is short enough that a second
        // Unity instance signing in is still picked up without a domain reload.
        private static readonly long SessionCacheTicks = System.Diagnostics.Stopwatch.Frequency * 5;
        private static OAuthSessionV2 _sessionCache;
        private static long _sessionCacheStamp;
        private static bool _sessionCacheValid;

        // Refresh tokens rotate, so a token may be redeemed exactly once. Several
        // call sites can ask for a valid access token at the same moment, and if
        // each ran its own exchange the losers would replay a consumed token — the
        // server treats that as theft and invalidates the whole family, signing the
        // user out at random. Concurrent callers share one exchange instead.
        private static Task<string> _refreshInFlight;

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
                string resource,
                string sessionFileName,
                string sessionEntropyLabel)
            {
                ClientId = clientId;
                RequestedScopes = requestedScopes;
                Resource = resource;
                SessionFileName = sessionFileName;
                SessionEntropyLabel = sessionEntropyLabel;
            }

            public string ClientId { get; }
            public string[] RequestedScopes { get; }

            /// <summary>
            /// RFC 8707 resource indicator. Sent on both the authorization and token
            /// requests so the server issues an audience-bound JWT for the public API
            /// instead of an opaque token. Without it the API gateway substitutes its
            /// own default, which this client is not authorized for.
            /// </summary>
            public string Resource { get; }
            public string SessionFileName { get; }
            public string SessionEntropyLabel { get; }
            public string RequestedScopeValue => string.Join(" ", RequestedScopes);
        }

        // RFC 8252 §8.4: a native app is a public client with its own client_id.
        // The exporter is a distinct application from the consumer-side package
        // broker and deliberately cannot request `package:operate`.
        private static readonly OAuthDomainConfig Domain = new OAuthDomainConfig(
            clientId: "yucp-package-exporter",
            requestedScopes: new[]
            {
                RequiredCertIssueScope,
                RequiredProductsReadScope,
                RequiredVerificationReadScope,
                OfflineAccessScope,
            },
            resource: "https://api.creators.yucp.club",
            sessionFileName: "unity-exporter-oauth-session-v5.dat",
            sessionEntropyLabel: "YUCP.UnityEditor.PackageExporter.Session.v5");

        public static string ClientId => Domain.ClientId;

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
            // DPoP (RFC 9449): RSA key pair for proof-of-possession binding.
            // Persisted so refresh token exchanges re-prove the same key.
            public string dpopPrivateKeyXml;
            public string dpopPublicKeyJwk;
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
            return TryGetCachedSession(out OAuthSessionV2 session) && !string.IsNullOrEmpty(session.userId)
                ? session.userId
                : null;
        }

        public static string GetDisplayName()
        {
            return TryGetCachedSession(out OAuthSessionV2 session) && !string.IsNullOrEmpty(session.displayName)
                ? session.displayName
                : null;
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
                    return session.accessToken;
                }

                if (!string.IsNullOrEmpty(session.refreshToken))
                {
                    string refreshedAccessToken = await RefreshAccessTokenCoalescedAsync(serverUrl, session);
                    if (!string.IsNullOrEmpty(refreshedAccessToken))
                    {
                        return refreshedAccessToken;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Joins the in-flight refresh when one is already running, so a rotating
        /// refresh token is redeemed exactly once no matter how many callers ask
        /// for a token at the same time.
        /// </summary>
        private static Task<string> RefreshAccessTokenCoalescedAsync(string serverUrl, OAuthSessionV2 currentSession)
        {
            lock (SessionLock)
            {
                if (_refreshInFlight != null && !_refreshInFlight.IsCompleted)
                {
                    return _refreshInFlight;
                }

                _refreshInFlight = RunRefreshAsync(serverUrl, currentSession);
                return _refreshInFlight;
            }
        }

        private static async Task<string> RunRefreshAsync(string serverUrl, OAuthSessionV2 currentSession)
        {
            try
            {
                return await RefreshAccessTokenAsync(serverUrl, currentSession);
            }
            finally
            {
                lock (SessionLock)
                {
                    _refreshInFlight = null;
                }
            }
        }

        /// <summary>
        /// Signs out. When <paramref name="serverUrl"/> is supplied the refresh
        /// token is also revoked server-side per RFC 7009, so the grant dies with
        /// the session instead of staying valid for the rest of its 30-day life.
        /// </summary>
        public static void SignOut(string serverUrl = null)
        {
            if (!string.IsNullOrEmpty(serverUrl) &&
                TryGetCachedSession(out OAuthSessionV2 session) &&
                !string.IsNullOrEmpty(session.refreshToken))
            {
                // Best effort: the local session is cleared either way, so a failed
                // or slow revocation must never block signing out.
                _ = RevokeRefreshTokenAsync(serverUrl, session);
            }

            ClearPersistentSession();
        }

        /// <summary>
        /// RFC 7009 token revocation. The endpoint answers 200 for an unknown or
        /// already-revoked token, so there is nothing to retry on failure.
        /// </summary>
        private static async Task RevokeRefreshTokenAsync(string serverUrl, OAuthSessionV2 session)
        {
            try
            {
                string endpoint = $"{serverUrl.TrimEnd('/')}/api/auth/oauth2/revoke";
                string body = BuildFormUrlEncodedBody(new Dictionary<string, string>
                {
                    ["token"] = session.refreshToken,
                    ["token_type_hint"] = "refresh_token",
                    ["client_id"] = ClientId,
                });

                using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                    downloadHandler = new DownloadHandlerBuffer(),
                };
                request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("Accept-Encoding", "identity");
                if (!string.IsNullOrEmpty(session.dpopPrivateKeyXml))
                {
                    request.SetRequestHeader(
                        "DPoP",
                        CreateDpopProof("POST", endpoint, null, session.dpopPrivateKeyXml, session.dpopPublicKeyJwk));
                }

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning(
                        $"[YUCP OAuth] Refresh token revocation returned {request.responseCode}: {request.error}. " +
                        "The local session was still cleared.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Refresh token revocation failed: {ex.Message}. The local session was still cleared.");
            }
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

                // DPoP (RFC 9449): generate an RSA-2048 key pair for proof-of-possession binding.
                string dpopPrivateKeyXml;
                string dpopPublicKeyJwk;
                GenerateDpopKeyPair(out dpopPrivateKeyXml, out dpopPublicKeyJwk);

                SetSignInStatus("Starting local sign-in callback...");
                var httpListener = StartLoopbackListener(out int port);
                Debug.Log($"[YUCP OAuth] Using loopback port {port}");

                string redirectUri = $"http://127.0.0.1:{port}{CallbackPath}";
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
                        // An ephemeral loopback port also receives requests that have
                        // nothing to do with the callback — browser favicon probes,
                        // link prefetches, local port scanners. Accepting the first
                        // request blindly lets any of those consume the sign-in and
                        // fail it, so serve only the redirect path and answer the
                        // rest with 404. RFC 8252 §7.3.
                        while (true)
                        {
                            Task<HttpListenerContext> contextTask = httpListener.GetContextAsync();
                            Task timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);

                            Task finished = await Task.WhenAny(contextTask, timeoutTask);
                            if (finished != contextTask)
                            {
                                Debug.LogWarning("[YUCP OAuth] Timed out waiting for browser callback.");
                                httpListener.Stop();
                                onError?.Invoke("Sign-in timed out after 2 minutes. Please try again.");
                                return;
                            }

                            HttpListenerContext candidate = await contextTask;
                            string candidatePath = candidate.Request.Url?.AbsolutePath ?? string.Empty;
                            if (string.Equals(candidatePath, CallbackPath, StringComparison.Ordinal))
                            {
                                context = candidate;
                                Debug.Log($"[YUCP OAuth] Callback received: {candidate.Request.Url}");
                                break;
                            }

                            Debug.Log($"[YUCP OAuth] Ignoring unrelated loopback request for '{candidatePath}'.");
                            await SendNotFoundAsync(candidate);
                        }

                        cts.Cancel();
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
                string tokenEndpoint = $"{serverUrl.TrimEnd('/')}/api/auth/oauth2/token";
                Debug.Log($"[YUCP OAuth] Exchanging auth code at {tokenEndpoint}");
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
                tokenReq.SetRequestHeader("DPoP", CreateDpopProof("POST", tokenEndpoint, null, dpopPrivateKeyXml, dpopPublicKeyJwk));

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
                if (session != null)
                {
                    session.dpopPrivateKeyXml = dpopPrivateKeyXml;
                    session.dpopPublicKeyJwk = dpopPublicKeyJwk;
                }

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
            if (!string.IsNullOrEmpty(currentSession.dpopPrivateKeyXml))
            {
                string refreshTokenEndpoint = $"{serverUrl.TrimEnd('/')}/api/auth/oauth2/token";
                tokenReq.SetRequestHeader("DPoP", CreateDpopProof("POST", refreshTokenEndpoint, null, currentSession.dpopPrivateKeyXml, currentSession.dpopPublicKeyJwk));
            }

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

            // DPoP keys persist across token refreshes; set once during initial sign-in.
            string dpopPrivateKeyXml = previousSession?.dpopPrivateKeyXml;
            string dpopPublicKeyJwk = previousSession?.dpopPublicKeyJwk;

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
                dpopPrivateKeyXml = dpopPrivateKeyXml,
                dpopPublicKeyJwk = dpopPublicKeyJwk,
            };
        }

        private static UnityWebRequest CreateTokenRequest(string serverUrl, IReadOnlyDictionary<string, string> fields)
        {
            string endpoint = $"{serverUrl.TrimEnd('/')}/api/auth/oauth2/token";

            // RFC 8707 §2: name the resource on every token request, including
            // refreshes, so the renewed token keeps the same audience.
            var boundFields = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> field in fields)
            {
                boundFields[field.Key] = field.Value;
            }
            boundFields["resource"] = Domain.Resource;

            string body = BuildFormUrlEncodedBody(boundFields);
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

        private static UnityWebRequest CreateProfileRequest(string serverUrl, string accessToken, OAuthSessionV2 session)
        {
            string endpoint = $"{serverUrl.TrimEnd('/')}/v1/me";
            var request = UnityWebRequest.Get(endpoint);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Accept-Encoding", "identity");

            // The session is not persisted yet during sign-in, so the key comes from
            // the in-flight session rather than storage. Scheme rules are the same as
            // ApplyAuthHeaders: DPoP-bound tokens must not be sent as Bearer.
            if (session != null && !string.IsNullOrEmpty(session.dpopPrivateKeyXml))
            {
                request.SetRequestHeader("Authorization", $"DPoP {accessToken}");
                request.SetRequestHeader(
                    "DPoP",
                    CreateDpopProof("GET", endpoint, accessToken, session.dpopPrivateKeyXml, session.dpopPublicKeyJwk));
            }
            else
            {
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            }

            return request;
        }

        private static async Task<OAuthSessionV2> EnrichSessionWithProfileAsync(string serverUrl, OAuthSessionV2 session)
        {
            if (session == null
                || string.IsNullOrEmpty(serverUrl)
                || string.IsNullOrEmpty(session.accessToken))
            {
                return session;
            }

            using var request = CreateProfileRequest(serverUrl, session.accessToken, session);
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
                    return true;
                }
            }

            session = null;
            return false;
        }

        private static bool TryGetCachedSession(out OAuthSessionV2 session)
        {
            lock (SessionLock)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!_sessionCacheValid || now - _sessionCacheStamp > SessionCacheTicks)
                {
                    _sessionCache = LoadPersistentSession();
                    _sessionCacheStamp = now;
                    _sessionCacheValid = true;
                }

                session = _sessionCache;
            }

            return session != null;
        }

        private static void SetCachedSession(OAuthSessionV2 session)
        {
            lock (SessionLock)
            {
                _sessionCache = session;
                _sessionCacheStamp = System.Diagnostics.Stopwatch.GetTimestamp();
                _sessionCacheValid = true;
            }
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
                if (string.Equals(requiredScope, OfflineAccessScope, StringComparison.Ordinal))
                {
                    continue;
                }

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
                if (string.Equals(requiredScope, OfflineAccessScope, StringComparison.Ordinal))
                {
                    continue;
                }

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

            SetCachedSession(session);

            // ponytail: Windows-only session storage. Unity ships no cross-platform
            // secret store, and writing a refresh token to a plain file off Windows
            // is worse than asking the user to sign in again. macOS/Linux editors
            // stay signed in only for the lifetime of the access token.
            if (!SupportsProtectedSessionStorage())
            {
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
            DeleteCredential(GetCredentialTarget());
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
            SetCachedSession(null);

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

        /// <summary>
        /// Closes out a request to the loopback listener that is not the OAuth
        /// callback, so the browser stops waiting and the listener can accept the
        /// real redirect.
        /// </summary>
        private static async Task SendNotFoundAsync(HttpListenerContext ctx)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes("Not found.");
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body, 0, body.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to answer an unrelated loopback request: {ex.Message}");
            }
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
            return $"{serverUrl.TrimEnd('/')}/api/auth/oauth2/authorize"
                + $"?client_id={Uri.EscapeDataString(ClientId)}"
                + "&response_type=code"
                + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
                + "&code_challenge_method=S256"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + $"&state={Uri.EscapeDataString(state)}"
                + $"&resource={Uri.EscapeDataString(Domain.Resource)}"
                + $"&scope={Uri.EscapeDataString(Domain.RequestedScopeValue)}";
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        // ──────────────────────────────────────────────────────────────
        //  DPoP (RFC 9449) — Proof-of-Possession key binding
        // ──────────────────────────────────────────────────────────────

        private static void GenerateDpopKeyPair(out string privateKeyXml, out string publicKeyJwk)
        {
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                rsa.PersistKeyInCsp = false;
                privateKeyXml = rsa.ToXmlString(true);
                RSAParameters parameters = rsa.ExportParameters(false);
                publicKeyJwk = BuildRsaJwk(parameters);
            }
        }

        private static string BuildRsaJwk(RSAParameters parameters)
        {
            string n = Base64UrlEncode(parameters.Modulus);
            string e = Base64UrlEncode(parameters.Exponent);
            return $"{{\"kty\":\"RSA\",\"n\":\"{n}\",\"e\":\"{e}\",\"alg\":\"RS256\"}}";
        }

        private static string CreateDpopProof(string httpMethod, string requestUrl, string accessToken, string privateKeyXml, string publicKeyJwk)
        {
            byte[] jtiBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(jtiBytes);
            }

            string jti = Base64UrlEncode(jtiBytes);
            long iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string htu = StripQueryAndFragment(requestUrl);

            string header = $"{{\"typ\":\"dpop+jwt\",\"alg\":\"RS256\",\"jwk\":{publicKeyJwk}}}";

            var payload = new StringBuilder();
            payload.Append($"{{\"jti\":\"{jti}\"");
            payload.Append($",\"htm\":\"{httpMethod}\"");
            payload.Append($",\"htu\":\"{htu}\"");
            payload.Append($",\"iat\":{iat}");

            if (!string.IsNullOrEmpty(accessToken))
            {
                byte[] tokenHash;
                using (var sha = SHA256.Create())
                {
                    tokenHash = sha.ComputeHash(Encoding.ASCII.GetBytes(accessToken));
                }

                payload.Append($",\"ath\":\"{Base64UrlEncode(tokenHash)}\"");
            }

            payload.Append('}');

            string encodedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
            string encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payload.ToString()));
            string signingInput = $"{encodedHeader}.{encodedPayload}";

            byte[] signature;
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(privateKeyXml);
                using (var sha256 = SHA256.Create())
                {
                    signature = rsa.SignData(Encoding.UTF8.GetBytes(signingInput), sha256);
                }
            }

            return $"{signingInput}.{Base64UrlEncode(signature)}";
        }

        private static string StripQueryAndFragment(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            int fragmentIndex = url.IndexOf('#');
            if (fragmentIndex >= 0)
            {
                url = url.Substring(0, fragmentIndex);
            }

            int queryIndex = url.IndexOf('?');
            if (queryIndex >= 0)
            {
                url = url.Substring(0, queryIndex);
            }

            return url;
        }

        /// <summary>
        /// Creates a DPoP proof JWT for the given HTTP method and URL,
        /// using the current session's key pair. Returns null when no DPoP
        /// key pair is available (e.g. session not established).
        /// </summary>
        public static string CreateDpopProofForRequest(string httpMethod, string requestUrl, string accessToken)
        {
            if (!TryGetCachedSession(out OAuthSessionV2 session) || string.IsNullOrEmpty(session.dpopPrivateKeyXml))
            {
                return null;
            }

            return CreateDpopProof(httpMethod, requestUrl, accessToken, session.dpopPrivateKeyXml, session.dpopPublicKeyJwk);
        }

        /// <summary>
        /// Applies the OAuth authorization headers to a <see cref="UnityWebRequest"/>.
        /// Always use this instead of setting the Authorization header by hand.
        ///
        /// This client registers with dpopBoundAccessTokens, so its access tokens
        /// carry a cnf.jkt confirmation claim. RFC 9449 §7.1 requires such a token to
        /// be presented with the DPoP scheme and an accompanying proof; a server
        /// following the spec rejects the same token sent as Bearer outright. The
        /// proof binds this request's method and URL and carries `ath`, the hash of
        /// the access token being presented.
        ///
        /// Falls back to Bearer only when no DPoP key exists, which is the case for
        /// a session that was never DPoP-bound.
        /// </summary>
        public static void ApplyAuthHeaders(UnityWebRequest request, string accessToken, string httpMethod, string requestUrl)
        {
            string dpopProof = CreateDpopProofForRequest(httpMethod, requestUrl, accessToken);
            if (!string.IsNullOrEmpty(dpopProof))
            {
                request.SetRequestHeader("Authorization", $"DPoP {accessToken}");
                request.SetRequestHeader("DPoP", dpopProof);
                return;
            }

            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
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
