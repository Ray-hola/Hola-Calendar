using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hola.Core;
using static Hola.Tests.T;

namespace Hola.Tests
{
    static class ApiTests
    {
        static HolaApi Client() { Stub.Clear(); return new HolaApi("http://hola.test:4173", new Stub()); }

        static Dictionary<string, object> Body(int i) => Json.Object(Stub.Bodies[i]) ?? new Dictionary<string, object>();

        static (int, Dictionary<string, string>, string) Reply(int status, string body, Dictionary<string, string> headers = null) =>
            (status, headers ?? new Dictionary<string, string>(), body);

        public static async Task Run()
        {
            await Test("normalizes typed server addresses", () =>
            {
                Eq(HolaApi.Normalize("192.168.1.8:4173"), "http://192.168.1.8:4173");
                Eq(HolaApi.Normalize(" https://todo.example.com/ "), "https://todo.example.com");
                Eq(HolaApi.Normalize("https://todo.example.com/index.html?x=1"), "https://todo.example.com");
                Eq(HolaApi.Normalize("HTTPS://Todo.Example.com:443"), "https://todo.example.com");
                Eq(HolaApi.Normalize("[fd12::1]:4173"), "http://[fd12::1]:4173");
                Check(HolaApi.Normalize("") == null);
                Check(HolaApi.Normalize("http://") == null);
                Check(HolaApi.Normalize("ftp://todo.example.com") == null);
                Check(HolaApi.Normalize("https://user:password@todo.example.com") == null);
            });

            await Test("login sends the X-Work-Calendar header, no Origin, and reads the cookie", async () =>
            {
                Stub.Handler = _ => Reply(200, @"{""ok"":true}", new Dictionary<string, string>
                {
                    ["Set-Cookie"] = "wc_session=abc.DEF-123; HttpOnly; SameSite=Strict; Path=/; Max-Age=604800", ["Content-Type"] = "application/json",
                });
                var api = Client();
                Eq(await api.Login("ray", "pw"), "abc.DEF-123");
                var r = Stub.Requests[0];
                Eq(r.RequestUri.AbsolutePath, "/api/login");
                Eq(r.Method.Method, "POST");
                Eq(Stub.Header(r, "X-Work-Calendar"), "1");
                Check(Stub.Header(r, "Origin") == null);
                Check(Stub.Header(r, "Cookie") == null, "no session goes along with a sign-in");
                Eq(Body(0).Str("username"), "ray");
                Eq(Body(0).Str("password"), "pw");
            });

            await Test("cookie parsing variants", () =>
            {
                Eq(HolaApi.SessionToken("wc_session=tok; HttpOnly"), "tok");
                Eq(HolaApi.SessionToken("other=1, wc_session=tok2; Path=/"), "tok2");
                Check(HolaApi.SessionToken("wc_session=; Max-Age=0") == null);
                Check(HolaApi.SessionToken("nothing=here") == null);
            });

            await Test("a sign-in answered without a session is a bad response", async () =>
            {
                Stub.Handler = _ => Reply(200, @"{""ok"":true}");
                await Throws(new ApiException(ApiErrorKind.BadResponse), () => Client().Login("ray", "pw"));
            });

            await Test("state sends the cookie and decodes", async () =>
            {
                Stub.Handler = r => Stub.Header(r, "Cookie") != "wc_session=tok"
                    ? Reply(401, @"{""error"":""请登录工作台""}")
                    : Reply(200, @"{""user"":{""id"":""ray"",""name"":""Ray"",""admin"":false},""today"":""2026-11-15"",""tasks"":[{""id"":""t"",""name"":""n"",""assignee"":""ray"",""date"":""2026-11-15""}],""projects"":[]}");
                var snap = await Client().State("tok");
                Eq(snap.User.Id, "ray");
                Eq(snap.Tasks.Count, 1);
                Eq(Stub.Requests[0].Method.Method, "GET");
            });

            await Test("complete posts the web widget's payload", async () =>
            {
                Stub.Handler = _ => Reply(200, @"{""id"":""t""}");
                var api = Client();
                await api.Complete(new Todo("t", "n", "ray", "2026-11-15", duration: 45, stackCount: 2, stackPeriods: new List<string> { "a", "b" }), "tok");
                Eq(Body(0).Str("action"), "task.complete");
                var data = Body(0).Obj("data");
                Eq(data.Str("id"), "t");
                Eq(data.Str("summary"), "完成任务");
                Eq(data.Int("actual"), 45);
                Eq(data.Str("feeling"), "顺利");
                Seq(data.Strings("stackPeriods"), new[] { "a", "b" });
                Eq(Stub.Header(Stub.Requests[0], "Cookie"), "wc_session=tok");

                Stub.Clear();
                await api.Complete(new Todo("s", "n", "ray", "2026-11-15", duration: 0), "tok");
                var d2 = Body(0).Obj("data");
                Eq(d2.Int("actual"), 30);
                Check(!d2.ContainsKey("stackPeriods"));
            });

            await Test("maps server refusals to typed errors with the server's text", async () =>
            {
                Stub.Handler = _ => Reply(403, @"{""error"":""只能记录自己的任务""}");
                await Throws(new ApiException(ApiErrorKind.Server, "只能记录自己的任务", 403), () => Client().Complete(new Todo("t", "n", "x", "2026-11-15"), "tok"));
                Stub.Handler = _ => Reply(401, @"{""error"":""请登录工作台""}");
                await Throws(new ApiException(ApiErrorKind.Unauthorized, "请登录工作台"), () => Client().State("stale"));
                Stub.Handler = _ => Reply(409, @"{""error"":""本次任务已记录，不能重复提交""}");
                await Throws(new ApiException(ApiErrorKind.Server, "本次任务已记录，不能重复提交", 409), () => Client().Action("task.complete", new Dictionary<string, object>(), "tok"));
                Stub.Handler = _ => Reply(500, "");
                await Throws(new ApiException(ApiErrorKind.Server, "服务器返回 500", 500), () => Client().Health());
                // A redirect is not followed; the message says where it pointed.
                Stub.Handler = _ => Reply(301, "", new Dictionary<string, string> { ["Location"] = "https://hola.test/api/health" });
                await Throws(new ApiException(ApiErrorKind.Server, "服务器把请求转到了 https://hola.test/api/health，请改填这个地址", 301), () => Client().Health());
                Stub.Handler = _ => Reply(302, "", new Dictionary<string, string> { ["Location"] = "/login" });
                await Throws(new ApiException(ApiErrorKind.Server, "服务器把请求转到了 http://hola.test:4173/login，请改填这个地址", 302), () => Client().Health());
                Eq(new ApiException(ApiErrorKind.Server, "只能记录自己的任务", 403).Message, "只能记录自己的任务");
            });

            await Test("a network failure is reported as offline", async () =>
            {
                Stub.Handler = null;
                await Throws(new ApiException(ApiErrorKind.Offline), () => Client().State("tok"));
                // Any other failure on the way counts as the network too, so connect still tries the next address.
                Stub.Handler = _ => throw new System.InvalidOperationException("bad charset");
                await Throws(new ApiException(ApiErrorKind.Offline), () => Client().State("tok"));
            });
        }
    }
}
