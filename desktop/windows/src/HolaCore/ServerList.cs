using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hola.Core
{
    /// <summary>
    /// A server this PC has connected to, remembered so switching is one click.
    /// The session token is never here: it stays in Credential Manager under `Url`.
    /// </summary>
    public sealed class SavedServer
    {
        public string Url, Name, Username;

        public SavedServer(string url, string name, string username) { Url = url; Name = name; Username = username; }

        /// <summary>The address as people say it: host, and the port when there is one.</summary>
        public string Host
        {
            get
            {
                if (!Uri.TryCreate(Url, UriKind.Absolute, out var u) || u.Host.Length == 0) return Url;
                return u.IsDefaultPort ? u.Host : u.Host + ":" + u.Port;
            }
        }

        public bool Same(SavedServer o) => o != null && o.Url == Url && o.Name == Name && o.Username == Username;
    }

    public static class ServerList
    {
        public const int Limit = 8;

        /// <summary>
        /// `server` moves to the front; an older entry for the same address is replaced.
        /// `keeping` (the server in use) is never the one pushed off the end.
        /// </summary>
        public static List<SavedServer> Remember(SavedServer server, List<SavedServer> list, string keeping = null)
        {
            var all = new[] { server }.Concat(list.Where(s => s.Url != server.Url)).ToList();
            var kept = keeping == null ? null : all.Skip(Limit).FirstOrDefault(s => s.Url == keeping);
            if (kept == null) return all.Take(Limit).ToList();
            return all.Take(Limit - 1).Concat(new[] { kept }).ToList();
        }

        public static List<SavedServer> Forget(string url, List<SavedServer> list) => list.Where(s => s.Url != url).ToList();

        public static List<SavedServer> Decode(string json)
        {
            if (!(Json.Parse(json) is object[] items)) return new List<SavedServer>();
            var all = new List<SavedServer>();
            foreach (var item in items)
            {
                var d = item as Dictionary<string, object>;
                string url = d.Str("url"), name = d.Str("name"), user = d.Str("username");
                if (url == null || name == null || user == null) return new List<SavedServer>();
                all.Add(new SavedServer(url, name, user));
            }
            return all;
        }

        public static string Encode(List<SavedServer> list) =>
            Json.Write(list.Select(s => new Dictionary<string, object> { ["url"] = s.Url, ["name"] = s.Name, ["username"] = s.Username }).ToList());
    }

    public static class ServerCheck
    {
        /// <summary>
        /// Asks the server what it is. A server older than /api/info is accepted
        /// when it answers /api/health; anything else is not a 哗啦待办 server.
        /// </summary>
        public static async Task<ServerInfo> Info(this HolaApi api)
        {
            var r = await api.Send("GET", "/api/info", null, null);
            if (r.Status == 404)
            {
                // Any website says 404 here; an older 哗啦待办 also answers {"ok":true} at /api/health.
                var h = await api.Send("GET", "/api/health", null, null);
                if (h.Status != 200 || Json.Object(h.Body).Bool("ok") != true) throw new ApiException(ApiErrorKind.NotHola);
                return new ServerInfo("哗啦待办", 1, legacy: true);
            }
            HolaApi.Check(r);
            var obj = Json.Object(r.Body);
            if (obj.Str("product") != "hola-calendar") throw new ApiException(ApiErrorKind.NotHola);
            var version = obj.Int("api") ?? 1;
            if (version > ServerInfo.SupportedApi) throw new ApiException(ApiErrorKind.Incompatible, "", version);
            return new ServerInfo(obj.Str("name") ?? "哗啦待办", version);
        }

        /// <summary>
        /// Tries each candidate address in order and returns the first that is a
        /// 哗啦待办 server. When none is, the most telling failure is reported:
        /// "not a 哗啦待办 server" or "too new" beats "unreachable".
        /// </summary>
        public static async Task<(HolaApi, ServerInfo)> Connect(string text, Func<HttpMessageHandler> handler = null)
        {
            var urls = ServerAddress.Candidates(text);
            if (urls.Count == 0) throw new ApiException(ApiErrorKind.BadUrl);
            ApiException best = null;
            foreach (var url in urls)
            {
                var api = new HolaApi(url, handler?.Invoke());
                try { return (api, await api.Info()); }
                catch (ApiException e) { if (best == null || e.Rank > best.Rank) best = e; }
            }
            throw best ?? new ApiException(ApiErrorKind.Offline);
        }
    }
}
