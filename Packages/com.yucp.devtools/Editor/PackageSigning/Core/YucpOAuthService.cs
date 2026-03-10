using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    public static class YucpOAuthService
    {
        public const string ClientId = "yucp-unity-editor";

        private const string KeyToken       = "YUCP_OAuth_AccessToken";
        private const string KeyExpiry      = "YUCP_OAuth_TokenExpiry";
        private const string KeyUserId      = "YUCP_OAuth_UserId";
        private const string KeyDisplayName = "YUCP_OAuth_DisplayName";

        public static bool IsSignedIn()
        {
            if (!EditorPrefs.HasKey(KeyToken) || !EditorPrefs.HasKey(KeyExpiry))
                return false;
            string token = EditorPrefs.GetString(KeyToken, "");
            if (string.IsNullOrEmpty(token)) return false;
            long expiry = (long)EditorPrefs.GetInt(KeyExpiry, 0);
            long now    = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return expiry > now + 60;
        }

        public static string GetAccessToken() =>
            IsSignedIn() ? EditorPrefs.GetString(KeyToken, null) : null;

        public static string GetUserId()      => EditorPrefs.GetString(KeyUserId,      null);
        public static string GetDisplayName()
        {
            string name = EditorPrefs.GetString(KeyDisplayName, null);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        public static void SignOut()
        {
            EditorPrefs.DeleteKey(KeyToken);
            EditorPrefs.DeleteKey(KeyExpiry);
            EditorPrefs.DeleteKey(KeyUserId);
            EditorPrefs.DeleteKey(KeyDisplayName);
        }

        public static async Task SignInAsync(string serverUrl, Action onSuccess, Action<string> onError)
        {
            Debug.Log("[YUCP OAuth] SignInAsync started");
            try
            {
                // 1. PKCE code verifier (32 random bytes, Base64Url)
                byte[] verifierBytes = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                    rng.GetBytes(verifierBytes);
                string codeVerifier = Base64UrlEncode(verifierBytes);

                // 2. Code challenge = SHA-256(verifier), Base64Url
                byte[] hashBytes;
                using (var sha = SHA256.Create())
                    hashBytes = sha.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
                string codeChallenge = Base64UrlEncode(hashBytes);

                // 3. CSRF state (16 random bytes, Base64Url)
                byte[] stateBytes = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                    rng.GetBytes(stateBytes);
                string state = Base64UrlEncode(stateBytes);

                // 4. Find a free loopback port
                int port;
                // TcpListener does not implement IDisposable in all Unity/.NET versions,
                // so avoid a "using" statement and stop the listener explicitly.
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                Debug.Log($"[YUCP OAuth] Using loopback port {port}");

                string redirectUri = $"http://127.0.0.1:{port}/callback";
                string authUrl     = BuildAuthUrl(serverUrl, codeChallenge, state, redirectUri);
                Debug.Log($"[YUCP OAuth] Auth URL: {authUrl}");

                // 5. Start HttpListener BEFORE opening the browser so we never
                //    miss the callback even if the browser is extremely fast.
                var httpListener = new HttpListener();
                httpListener.Prefixes.Add($"http://127.0.0.1:{port}/");
                httpListener.Start();
                Debug.Log($"[YUCP OAuth] HttpListener started on http://127.0.0.1:{port}/");

                // 6. Open browser
                Application.OpenURL(authUrl);
                Debug.Log("[YUCP OAuth] Browser opened, waiting for callback…");

                // 7. Await callback with 120-second timeout.
                //    IMPORTANT: do NOT call httpListener.Stop() until AFTER we have
                //    finished writing to context.Response — Stop() disposes the response
                //    objects for all accepted connections, which causes
                //    "Cannot access a disposed object" on the OutputStream.
                HttpListenerContext context = null;
                // authCode must be declared in this outer scope so it remains
                // available after we stop the HttpListener and leave the try/finally.
                string authCode = null;
                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
                    {
                        Task<HttpListenerContext> contextTask = httpListener.GetContextAsync();
                        Task timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);

                        Task finished = await Task.WhenAny(contextTask, timeoutTask);
                        cts.Cancel(); // stop the delay task either way

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

                    // 8. Parse the callback query string BEFORE touching the response so we
                    //    can send the right page for errors too.
                    var qp = ParseQueryString(context.Request.Url?.Query ?? "");
                    Debug.Log($"[YUCP OAuth] Callback params: {string.Join(", ", qp.Keys)}");

                    if (qp.TryGetValue("error", out string cbError))
                    {
                        string desc = qp.TryGetValue("error_description", out string d)
                            ? Uri.UnescapeDataString(d) : cbError;
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

                    // 9. Send the success page BEFORE token exchange so the browser
                    //    gets a response quickly instead of waiting for the round-trip.
                    await SendSuccessPageAsync(context);
                }
                finally
                {
                    // Always stop the listener — but only here, after ALL response
                    // writing is complete.  Moving Stop() any earlier causes
                    // "Cannot access a disposed object" on HttpListenerResponse.
                    try { httpListener.Stop(); } catch { /* already stopped */ }
                    Debug.Log("[YUCP OAuth] HttpListener stopped.");
                }

                // 10. Exchange authorization code for access token
                Debug.Log($"[YUCP OAuth] Exchanging auth code at {serverUrl.TrimEnd('/')}/api/auth/oauth2/token");
                using (var http = new HttpClient())
                {
                    var formContent = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string,string>("grant_type",    "authorization_code"),
                        new KeyValuePair<string,string>("client_id",     ClientId),
                        new KeyValuePair<string,string>("code",          authCode),
                        new KeyValuePair<string,string>("code_verifier", codeVerifier),
                        new KeyValuePair<string,string>("redirect_uri",  redirectUri),
                    });

                    HttpResponseMessage tokenResp = await http.PostAsync(
                        $"{serverUrl.TrimEnd('/')}/api/auth/oauth2/token", formContent);
                    string tokenJson = await tokenResp.Content.ReadAsStringAsync();
                    Debug.Log($"[YUCP OAuth] Token response {(int)tokenResp.StatusCode}: {tokenJson}");

                    if (!tokenResp.IsSuccessStatusCode)
                    {
                        onError?.Invoke($"Token exchange failed ({(int)tokenResp.StatusCode}): {tokenJson}");
                        return;
                    }

                    // 11. Extract token fields (no Newtonsoft)
                    string accessToken = ExtractJsonString(tokenJson, "access_token");
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        onError?.Invoke($"No access_token in server response: {tokenJson}");
                        return;
                    }
                    Debug.Log($"[YUCP OAuth] Access token obtained (length {accessToken.Length}).");

                    string expiresInRaw = ExtractJsonValue(tokenJson, "expires_in");
                    int    expiresIn    = int.TryParse(expiresInRaw, out int ei) ? ei : 3600;
                    long   expiryTs     = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn - 60;

                    // 12. Persist in EditorPrefs
                    EditorPrefs.SetString(KeyToken,  accessToken);
                    EditorPrefs.SetInt(KeyExpiry, (int)expiryTs);

                    // Parse identity directly from the JWT payload claims.
                    // The token includes name, email, sub, auth_user_id as custom claims —
                    // no extra network call needed (Auth0/Okta pattern: embed stable profile
                    // data in the token itself via customAccessTokenClaims on the server).
                    string jwtSub   = ParseJwtClaim(accessToken, "sub");
                    string jwtName  = ParseJwtClaim(accessToken, "name");
                    string jwtEmail = ParseJwtClaim(accessToken, "email");

                    if (!string.IsNullOrEmpty(jwtSub))   EditorPrefs.SetString(KeyUserId, jwtSub);
                    if (!string.IsNullOrEmpty(jwtName))  EditorPrefs.SetString(KeyDisplayName, jwtName);

                    Debug.Log($"[YUCP OAuth] Signed in as '{jwtName}' (sub={jwtSub}, email={jwtEmail}).");
                }

                Debug.Log("[YUCP OAuth] Sign-in complete.");
                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP OAuth] Unhandled exception: {ex}");
                onError?.Invoke($"Sign-in error: {ex.Message}");
            }
        }

        /// <summary>Writes the success HTML to the browser and flushes the response.</summary>
        private static async Task SendSuccessPageAsync(HttpListenerContext ctx)
        {
            byte[] html = Encoding.UTF8.GetBytes(BuildSuccessHtml());
            ctx.Response.ContentType     = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = html.Length;
            await ctx.Response.OutputStream.WriteAsync(html, 0, html.Length);
            ctx.Response.OutputStream.Close();
        }

        // ---------- private helpers ----------

        /// <summary>
        /// Redirect the browser to the YUCP error page, then close the response.
        /// Falls back to a minimal inline HTML page if the redirect fails.
        /// </summary>
        private static async Task SendErrorRedirectAsync(
            HttpListenerContext ctx, string serverUrl, string errorMessage)
        {
            try
            {
                string errorUrl = $"{serverUrl.TrimEnd('/')}/oauth/error"
                                + $"?error={Uri.EscapeDataString(errorMessage)}";
                ctx.Response.Redirect(errorUrl);
                ctx.Response.Close();
            }
            catch
            {
                // Last-resort inline page (response may already be in a bad state)
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(BuildErrorHtml(errorMessage));
                    ctx.Response.ContentType     = "text/html; charset=utf-8";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    ctx.Response.OutputStream.Close();
                }
                catch { /* nothing more we can do */ }
            }
        }

        private static string BuildAuthUrl(
            string serverUrl, string codeChallenge, string state, string redirectUri)
        {
            return $"{serverUrl.TrimEnd('/')}/api/yucp/oauth/authorize"
                 + $"?client_id={Uri.EscapeDataString(ClientId)}"
                 + $"&response_type=code"
                 + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
                 + $"&code_challenge_method=S256"
                 + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                 + $"&state={Uri.EscapeDataString(state)}"
                 + $"&scope=cert%3Aissue";
        }

        private static string Base64UrlEncode(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string q = query?.TrimStart('?');
            if (string.IsNullOrEmpty(q)) return result;
            foreach (string part in q.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                result[Uri.UnescapeDataString(part.Substring(0, eq))] =
                    Uri.UnescapeDataString(part.Substring(eq + 1));
            }
            return result;
        }

        private static string ExtractJsonString(string json, string key)
        {
            string needle = $"\"{key}\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == ':' || json[i] == '\t')) i++;
            if (i >= json.Length || json[i] != '"') return null;
            i++;
            var sb = new StringBuilder();
            while (i < json.Length && json[i] != '"')
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    i++;
                    switch (json[i])
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        default:   sb.Append(json[i]); break;
                    }
                }
                else sb.Append(json[i]);
                i++;
            }
            return sb.ToString();
        }

        private static string ExtractJsonValue(string json, string key)
        {
            string needle = $"\"{key}\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == ':' || json[i] == '\t')) i++;
            if (i >= json.Length) return null;
            var sb = new StringBuilder();
            while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != '\r' && json[i] != '\n')
                sb.Append(json[i++]);
            return sb.ToString().Trim().Trim('"');
        }

        private static string ExtractJsonObject(string json, string key)
        {
            string needle = $"\"{key}\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && json[i] != '{') i++;
            if (i >= json.Length) return null;
            int depth = 0, start = i;
            while (i < json.Length)
            {
                if      (json[i] == '{') depth++;
                else if (json[i] == '}') { if (--depth == 0) return json.Substring(start, i - start + 1); }
                i++;
            }
            return null;
        }

        private static string ParseJwtClaim(string jwt, string claim)
        {
            try
            {
                string[] parts = jwt.Split('.');
                if (parts.Length < 2) return null;
                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "=";  break;
                }
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                return ExtractJsonString(decoded, claim);
            }
            catch { return null; }
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
