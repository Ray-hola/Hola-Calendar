using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hola.Core
{
    /// <summary>What `GET /api/info` says about a server before anyone signs in.</summary>
    public sealed class ServerInfo
    {
        public string Name;
        public int Api;
        /// <summary>A server from before /api/info answered /api/health instead.</summary>
        public bool Legacy;

        public ServerInfo(string name, int api, bool legacy = false) { Name = name; Api = api; Legacy = legacy; }

        /// <summary>The newest server API this app speaks.</summary>
        public const int SupportedApi = 1;
    }

    /// <summary>Turning what a person types into the addresses worth trying.</summary>
    public static class ServerAddress
    {
        /// <summary>
        /// A typed scheme is kept as is. Without one, a public host is tried over
        /// https first so a password never goes out in clear by default; a LAN
        /// address is tried over http first, which is how such servers usually run.
        /// </summary>
        public static List<string> Candidates(string text)
        {
            var typed = (text ?? "").Trim();
            if (typed.Contains("://")) { var one = HolaApi.Normalize(typed); return one == null ? new List<string>() : new List<string> { one }; }
            string http = HolaApi.Normalize("http://" + typed), https = HolaApi.Normalize("https://" + typed);
            if (http == null || https == null) return new List<string>();
            return IsPrivate(HostOf(http)) ? new List<string> { http, https } : new List<string> { https, http };
        }

        /// <summary>The host of a normalised address, without brackets or port.</summary>
        public static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";

        /// <summary>
        /// Loopback, private IPv4 ranges, link-local, `.local` names and bare
        /// machine names: places a password over http stays inside the network.
        /// </summary>
        public static bool IsPrivate(string host)
        {
            var h = (host ?? "").ToLowerInvariant().Trim('[', ']');
            if (h.StartsWith("::ffff:") && h.Contains(".")) h = h.Substring(7);   // IPv4 written as IPv6
            if (h == "localhost" || h.EndsWith(".localhost") || h.EndsWith(".local")) return true;
            if (h.Contains(":"))
            {
                // IPv6: loopback, unique local fc00::/7, link-local fe80::/10.
                if (h == "::1") return true;
                var first = h.Split(':')[0];
                if (!ushort.TryParse(first, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var word)) return false;
                return (word & 0xfe00) == 0xfc00 || (word & 0xffc0) == 0xfe80;
            }
            var labels = h.Split('.');
            var parts = labels.Select(l => int.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1).ToArray();
            if (labels.Length == 4 && parts.All(p => p >= 0 && p <= 255))
            {
                int a = parts[0], b = parts[1];
                if (a == 10 || a == 127 || (a == 192 && b == 168) || (a == 169 && b == 254)) return true;
                if (a == 172 && b >= 16 && b <= 31) return true;
                if (a == 100 && b >= 64 && b <= 127) return true;   // CGNAT, used by Tailscale
                return false;
            }
            // A bare machine name is on this network; a bare number is an IPv4 address in disguise.
            return labels.Length == 1 && h.Length > 0 && !h.All(char.IsDigit);
        }

        /// <summary>A password sent here could be read on the way: plain http to a public host.</summary>
        public static bool ExposesPassword(string url) => url.StartsWith("http://") && !IsPrivate(HostOf(url));
    }

    /// <summary>
    /// `hola://connect?server=https://work.example.com`, the link the web app's
    /// 「连接桌面 App」 opens. It only ever fills in an address; the person still
    /// confirms with 连接 and signs in themselves.
    /// </summary>
    public static class ConnectLink
    {
        public const string Scheme = "hola";

        public static string Server(string link)
        {
            if (!Uri.TryCreate((link ?? "").Trim(), UriKind.Absolute, out var u)) return null;
            if (!u.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase) || !u.Host.Equals("connect", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var pair in u.Query.TrimStart('?').Split('&'))
            {
                var i = pair.IndexOf('=');
                if (i < 0 || Uri.UnescapeDataString(pair.Substring(0, i)) != "server") continue;
                var raw = Uri.UnescapeDataString(pair.Substring(i + 1));
                return raw.Contains("://") ? HolaApi.Normalize(raw) : null;
            }
            return null;
        }
    }
}
