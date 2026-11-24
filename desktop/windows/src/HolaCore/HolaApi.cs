using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hola.Core
{
    /// <summary>
    /// Talks to a self-hosted 哗啦待办 server exactly the way the browser does:
    /// `wc_session` cookie, `X-Work-Calendar: 1` on every POST, JSON bodies.
    /// Cookie handling is off, so the token never lands in a cookie jar; the
    /// caller keeps it in Windows Credential Manager.
    /// </summary>
    public sealed class HolaApi
    {
        /// <summary>The normalised server address, e.g. `https://work.example.com`.</summary>
        public string BaseUrl { get; }
        readonly HttpClient http;

        static HolaApi()
        {
            // .NET Framework only lets an app pick TLS 1.2+ this way when it targets 4.7+; 4.8 does, so the OS default is used.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;
        }

        public HolaApi(string baseUrl, HttpMessageHandler handler = null)
        {
            BaseUrl = baseUrl;
            var h = handler ?? new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            http = new HttpClient(h, disposeHandler: handler == null) { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>
        /// Normalises what a person types into a server address: adds `http://`
        /// when there is no scheme, drops any path, query and trailing slash.
        /// </summary>
        public static string Normalize(string text)
        {
            var s = (text ?? "").Trim();
            if (s.Length == 0) return null;
            if (!s.Contains("://")) s = "http://" + s;
            if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return null;
            var scheme = u.Scheme.ToLowerInvariant();
            if ((scheme != "http" && scheme != "https") || string.IsNullOrEmpty(u.Host) || u.UserInfo.Length > 0) return null;
            return scheme + "://" + u.Authority;
        }

        // Calls

        /// <summary>Returns the session token found in `Set-Cookie: wc_session=...`.</summary>
        public async Task<string> Login(string username, string password)
        {
            var r = await Send("POST", "/api/login", null, new Dictionary<string, object> { ["username"] = username, ["password"] = password });
            Check(r);
            return r.SetCookies.Select(SessionToken).FirstOrDefault(t => t != null) ?? throw new ApiException(ApiErrorKind.BadResponse);
        }

        public async Task<Snapshot> State(string token)
        {
            var r = await Send("GET", "/api/state", token, null);
            Check(r);
            var obj = Json.Object(r.Body) ?? throw new ApiException(ApiErrorKind.BadResponse);
            return Snapshot.FromJson(obj);
        }

        /// <summary>The same payload the web widget's `pipComplete` posts.</summary>
        public Task Complete(Todo task, string token)
        {
            var payload = new Dictionary<string, object>
            {
                ["id"] = task.Id, ["summary"] = "完成任务", ["actual"] = task.Duration > 0 ? task.Duration : 30, ["feeling"] = "顺利",
            };
            if (task.StackCount > 1) payload["stackPeriods"] = task.StackPeriods;
            return Action("task.complete", payload, token);
        }

        public async Task Action(string name, Dictionary<string, object> data, string token)
        {
            var r = await Send("POST", "/api/action", token, new Dictionary<string, object> { ["action"] = name, ["data"] = data });
            Check(r);
        }

        public async Task Logout(string token)
        {
            var r = await Send("POST", "/api/logout", token, new Dictionary<string, object>());
            Check(r);
        }

        public async Task<bool> Health()
        {
            var r = await Send("GET", "/api/health", null, null);
            Check(r);
            return true;
        }

        // Plumbing

        internal sealed class Reply
        {
            public int Status;
            public string Body;
            public List<string> SetCookies;
        }

        internal async Task<Reply> Send(string method, string path, string token, Dictionary<string, object> body)
        {
            if (!Uri.TryCreate(BaseUrl + path, UriKind.Absolute, out var url)) throw new ApiException(ApiErrorKind.BadUrl);
            var req = new HttpRequestMessage(new HttpMethod(method), url);
            req.Headers.TryAddWithoutValidation("X-Work-Calendar", "1");
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (token != null) req.Headers.TryAddWithoutValidation("Cookie", "wc_session=" + token);
            if (body != null) req.Content = new StringContent(Json.Write(body), Encoding.UTF8, "application/json");
            try
            {
                using (var resp = await http.SendAsync(req).ConfigureAwait(false))
                {
                    var text = resp.Content == null ? "" : await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var cookies = resp.Headers.TryGetValues("Set-Cookie", out var v) ? v.ToList() : new List<string>();
                    return new Reply { Status = (int)resp.StatusCode, Body = text, SetCookies = cookies };
                }
            }
            catch (HttpRequestException e) { throw new ApiException(ApiErrorKind.Offline, (e.InnerException ?? e).Message); }
            catch (TaskCanceledException) { throw new ApiException(ApiErrorKind.Offline, "连接服务器超时"); }
            catch (WebException e) { throw new ApiException(ApiErrorKind.Offline, e.Message); }
        }

        internal static void Check(Reply r)
        {
            if (r.Status == 200) return;
            var msg = Json.Object(r.Body).Str("error") ?? "";
            if (r.Status == 401) throw new ApiException(ApiErrorKind.Unauthorized, msg);
            throw new ApiException(ApiErrorKind.Server, msg.Length == 0 ? $"服务器返回 {r.Status}" : msg, r.Status);
        }

        static readonly Regex Cookie = new Regex(@"wc_session=([^;,\s]*)", RegexOptions.Compiled);

        public static string SessionToken(string setCookie)
        {
            var m = Cookie.Match(setCookie ?? "");
            return m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;
        }
    }
}
