using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hola.Core;
using static Hola.Tests.T;

namespace Hola.Tests
{
    static class StoreTests
    {
        public static async Task Run()
        {
            await Test("remembered servers: newest first, one entry per address, bounded", () =>
            {
                var a = new SavedServer("https://a.example.com", "A", "ray");
                var b = new SavedServer("http://192.168.1.8:4173", "B", "ray");
                var list = ServerList.Remember(a, new List<SavedServer>());
                list = ServerList.Remember(b, list);
                Seq(list.Select(s => s.Url), new[] { b.Url, a.Url });
                list = ServerList.Remember(new SavedServer(a.Url, "A", "chen"), list);
                Seq(list.Select(s => s.Url), new[] { a.Url, b.Url });
                Eq(list[0].Username, "chen");
                Seq(ServerList.Forget(b.Url, list).Select(s => s.Url), new[] { a.Url });
                var many = new List<SavedServer>();
                for (var i = 0; i < 12; i++) many = ServerList.Remember(new SavedServer($"https://s{i}.example.com", "", ""), many);
                Eq(many.Count, ServerList.Limit);
                Eq(many.First().Url, "https://s11.example.com");
                // The server in use stays on the list; the oldest other one goes instead.
                var kept = ServerList.Remember(new SavedServer("https://new.example.com", "", ""), many, keeping: "https://s4.example.com");
                Eq(kept.Count, ServerList.Limit);
                Eq(kept.Last().Url, "https://s4.example.com");
                Check(!kept.Any(s => s.Url == "https://s5.example.com"), "s5 should be pushed off instead");
                Seq(ServerList.Remember(a, many, keeping: "https://s11.example.com").Select(s => s.Url), ServerList.Remember(a, many).Select(s => s.Url));
                var back = ServerList.Decode(ServerList.Encode(list));
                Check(back.Count == list.Count && back.Zip(list, (x, y) => x.Same(y)).All(x => x), "encode and decode round-trip");
                Check(ServerList.Decode("not json").Count == 0);
                Check(ServerList.Decode(null).Count == 0);
                Check(ServerList.Decode(@"[{""url"":""x""}]").Count == 0, "a malformed entry drops the list, as on macOS");
                Eq(a.Host, "a.example.com");
                Eq(b.Host, "192.168.1.8:4173");
            });

            await Test("settings survive a write cut short", () =>
            {
                var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hola-tests-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var file = System.IO.Path.Combine(dir, "settings.json");
                    var s = new Settings(file);
                    s.Set("server", "https://a.example.com");
                    s.Set("username", "ray");
                    Eq(new Settings(file).Get("username"), "ray");
                    Check(System.IO.File.Exists(file + ".bak"), "the write before is kept");
                    Check(!System.IO.File.Exists(file + ".tmp"), "no temp file is left");
                    // A power cut can leave the file zero-filled: the copy from the write before takes over.
                    System.IO.File.WriteAllBytes(file, new byte[64]);
                    Eq(new Settings(file).Get("server"), "https://a.example.com");
                    s.Set("look", null);
                    Eq(new Settings(file).Get("username"), "ray");
                }
                finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
            });

            await Test("session tokens round-trip through Credential Manager", () =>
            {
                var account = "https://hola-tests-" + Guid.NewGuid().ToString("N") + ".example";
                try
                {
                    Check(CredentialStore.Load(account) == null);
                    CredentialStore.Save("tok-1", account);
                    Eq(CredentialStore.Load(account), "tok-1");
                    CredentialStore.Save("tok-2", account);
                    Eq(CredentialStore.Load(account), "tok-2");
                    Check(CredentialStore.Delete(account));
                    Check(CredentialStore.Load(account) == null);
                    Check(CredentialStore.Delete(account), "deleting what is not there is fine");
                }
                finally { CredentialStore.Delete(account); }
            });
        }
    }
}
