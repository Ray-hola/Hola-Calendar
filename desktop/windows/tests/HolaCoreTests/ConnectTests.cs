using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hola.Core;
using static Hola.Tests.T;

namespace Hola.Tests
{
    static class ConnectTests
    {
        const string Info = @"{""product"":""hola-calendar"",""name"":""哗啦待办"",""api"":1}";

        static (int, Dictionary<string, string>, string) Reply(int status, string body) => (status, new Dictionary<string, string>(), body);

        static Task<(HolaApi, ServerInfo)> Connect(string text) { Stub.Clear(); return ServerCheck.Connect(text, () => new Stub()); }

        public static async Task Run()
        {
            await Test("a public host is tried over https first, a LAN host over http first", () =>
            {
                Seq(ServerAddress.Candidates("work.example.com"), new[] { "https://work.example.com", "http://work.example.com" });
                Seq(ServerAddress.Candidates("192.168.1.8:4173"), new[] { "http://192.168.1.8:4173", "https://192.168.1.8:4173" });
                Check(ServerAddress.Candidates("nas:4173").First().StartsWith("http://"));
                Check(ServerAddress.Candidates("macbook.local").First().StartsWith("http://"));
                Seq(ServerAddress.Candidates(" http://work.example.com/x "), new[] { "http://work.example.com" });
                Seq(ServerAddress.Candidates("https://192.168.1.8"), new[] { "https://192.168.1.8" });
                Check(ServerAddress.Candidates("").Count == 0);
                Check(ServerAddress.Candidates("ftp://x.com").Count == 0);
            });

            await Test("private addresses are told apart from public ones", () =>
            {
                foreach (var h in new[] { "localhost", "127.0.0.1", "10.0.0.5", "172.16.0.1", "172.31.255.1", "192.168.0.10", "169.254.1.1", "nas", "mac.local", "::1", "fe80::1", "fd12::1",
                                          "fc00::1", "feb0::1", "100.64.0.1", "100.127.255.1", "::ffff:192.168.1.8", "[fd12::1]" })
                    Check(ServerAddress.IsPrivate(h), h + " should be private");
                foreach (var h in new[] { "work.ovenray.cn", "8.8.8.8", "172.32.0.1", "192.169.0.1", "2001:db8::1", "1.2.3",
                                          "100.128.0.1", "fec0::1", "3232235777", "::ffff:8.8.8.8" })
                    Check(!ServerAddress.IsPrivate(h), h + " should be public");
                Check(ServerAddress.ExposesPassword("http://work.example.com"));
                Check(!ServerAddress.ExposesPassword("https://work.example.com"));
                Check(!ServerAddress.ExposesPassword("http://192.168.1.8:4173"));
                Check(!ServerAddress.ExposesPassword("http://[fd12::1]:4173"));
            });

            await Test("connect reads /api/info and keeps the address that answered", async () =>
            {
                Stub.Handler = r => r.RequestUri.Scheme == "https" && r.RequestUri.AbsolutePath == "/api/info" ? Reply(200, Info) : Reply(404, "");
                var (api, got) = await Connect("work.example.com");
                Eq(api.BaseUrl, "https://work.example.com");
                Eq(got.Name, "哗啦待办");
                Eq(got.Api, 1);
                Check(!got.Legacy);
                Check(Stub.Header(Stub.Requests.First(), "Cookie") == null, "no session goes to a server not yet signed in to");
            });

            await Test("connect falls back to http when https does not answer as 哗啦待办", async () =>
            {
                Stub.Handler = r => r.RequestUri.Scheme == "https" ? Reply(502, "") : Reply(200, Info);
                var (api, _) = await Connect("work.example.com");
                Eq(api.BaseUrl, "http://work.example.com");
            });

            await Test("an older server without /api/info is accepted through /api/health", async () =>
            {
                Stub.Handler = r => r.RequestUri.AbsolutePath == "/api/health"
                    ? Reply(200, @"{""ok"":true,""timezone"":""Asia/Shanghai""}") : Reply(404, @"{""error"":""页面不存在""}");
                var (_, got) = await Connect("https://old.example.com");
                Check(got.Legacy);
                Eq(got.Api, 1);
            });

            await Test("a website that is not 哗啦待办 is refused, even one that answers 200 to everything", async () =>
            {
                Stub.Handler = _ => Reply(404, "<html>Not Found</html>");
                await Throws(new ApiException(ApiErrorKind.NotHola), () => Connect("https://example.com"));
                Stub.Handler = _ => Reply(200, "<html>hello</html>");
                await Throws(new ApiException(ApiErrorKind.NotHola), () => Connect("https://example.com"));
                Stub.Handler = _ => Reply(200, @"{""product"":""something-else"",""api"":1}");
                await Throws(new ApiException(ApiErrorKind.NotHola), () => Connect("https://example.com"));
            });

            await Test("a server newer than the app is refused with a clear reason", async () =>
            {
                Stub.Handler = _ => Reply(200, @"{""product"":""hola-calendar"",""name"":""哗啦待办"",""api"":2}");
                await Throws(new ApiException(ApiErrorKind.Incompatible, "", 2), () => Connect("https://next.example.com"));
            });

            await Test("the most telling failure wins when no address works", async () =>
            {
                // https: a plain website; http: unreachable. "Not 哗啦待办" says more than "offline".
                Stub.Handler = r => r.RequestUri.Scheme == "https" ? Reply(404, "") : Reply(200, "<html>");
                await Throws(new ApiException(ApiErrorKind.NotHola), () => Connect("work.example.com"));
                Stub.Handler = null;
                await Throws(new ApiException(ApiErrorKind.Offline), () => Connect("work.example.com"));
                await Throws(new ApiException(ApiErrorKind.BadUrl), () => Connect("   "));
            });

            await Test("connect links only ever carry a server address", () =>
            {
                Eq(ConnectLink.Server("hola://connect?server=https%3A%2F%2Fwork.example.com"), "https://work.example.com");
                Eq(ConnectLink.Server("hola://connect/?server=https%3A%2F%2Fwork.example.com"), "https://work.example.com");
                Eq(ConnectLink.Server("hola://connect?server=http://192.168.1.8:4173/index.html"), "http://192.168.1.8:4173");
                Check(ConnectLink.Server("hola://connect?server=work.example.com") == null, "a scheme is required");
                Check(ConnectLink.Server("hola://login?server=https://work.example.com") == null);
                Check(ConnectLink.Server("https://connect?server=https://work.example.com") == null);
                Check(ConnectLink.Server("hola://connect?server=javascript:alert(1)") == null);
                Check(ConnectLink.Server("hola://connect?server=https://u:p@work.example.com") == null);
                Check(ConnectLink.Server("hola://connect") == null);
                Check(ConnectLink.Server("not a link") == null);
            });
        }
    }
}
