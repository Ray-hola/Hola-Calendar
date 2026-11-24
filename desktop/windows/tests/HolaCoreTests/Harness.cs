using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Hola.Core;

namespace Hola.Tests
{
    /// <summary>A deliberately tiny harness, the same shape as the macOS one: no test framework to download.</summary>
    static class T
    {
        public static int Passed, Failed;
        static string current = "";

        public static void Check(bool cond, string msg = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (cond) { Passed++; return; }
            Failed++;
            Console.WriteLine($"  FAIL {current} {System.IO.Path.GetFileName(file)}:{line} {msg}");
        }

        public static void Eq<V>(V got, V want, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
            Check(EqualityComparer<V>.Default.Equals(got, want), $"expected {Show(want)}, got {Show(got)}", file, line);

        public static void Seq<V>(IEnumerable<V> got, IEnumerable<V> want, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
            Check(got != null && got.SequenceEqual(want), $"expected [{string.Join(", ", want)}], got [{(got == null ? "null" : string.Join(", ", got))}]", file, line);

        static string Show(object v) => v == null ? "null" : v.ToString();

        public static async Task Throws(ApiException expected, Func<Task> body, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            try { await body(); Check(false, $"expected {expected} to be thrown", file, line); }
            catch (ApiException e) { Check(e.Same(expected), $"expected {expected}, got {e}", file, line); }
            catch (Exception e) { Check(false, $"expected {expected}, got {e.GetType().Name}: {e.Message}", file, line); }
        }

        public static async Task Test(string name, Func<Task> body)
        {
            current = name;
            try { await body(); } catch (Exception e) { Failed++; Console.WriteLine($"  FAIL {name}: threw {e.GetType().Name}: {e.Message}"); }
        }

        public static Task Test(string name, Action body) => Test(name, () => { body(); return Task.CompletedTask; });
    }

    /// <summary>Stands in for the network so the client's wire format is checked without a server.</summary>
    sealed class Stub : HttpMessageHandler
    {
        public static Func<HttpRequestMessage, (int status, Dictionary<string, string> headers, string body)> Handler;
        public static readonly List<HttpRequestMessage> Requests = new List<HttpRequestMessage>();
        public static readonly List<string> Bodies = new List<string>();

        public static void Clear() { Requests.Clear(); Bodies.Clear(); }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync());
            if (Handler == null) throw new HttpRequestException("无法连接到远程服务器");
            var (status, headers, body) = Handler(request);
            var resp = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body ?? ""), RequestMessage = request };
            foreach (var h in headers ?? new Dictionary<string, string>())
                if (!resp.Headers.TryAddWithoutValidation(h.Key, h.Value)) { resp.Content.Headers.Remove(h.Key); resp.Content.Headers.TryAddWithoutValidation(h.Key, h.Value); }
            return resp;
        }

        public static string Header(HttpRequestMessage r, string name) =>
            r.Headers.TryGetValues(name, out var v) ? string.Join(",", v) : null;
    }

    static class Program
    {
        static int Main()
        {
            Run().GetAwaiter().GetResult();
            Console.WriteLine($"{T.Passed} checks passed, {T.Failed} failed");
            return T.Failed == 0 ? 0 : 1;
        }

        static async Task Run()
        {
            await PomodoroTests.Run();
            await TodayFilterTests.Run();
            await ApiTests.Run();
            await ConnectTests.Run();
            await StoreTests.Run();
        }
    }
}
