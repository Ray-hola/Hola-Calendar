using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hola.Core;

namespace Hola.Desktop
{
    sealed partial class AppState
    {
        public async Task SignIn(string password)
        {
            if (Syncing || SignedIn) return;
            var url = Server?.Url;
            if (url == null) { ShowConnect = true; Notify(); return; }
            if (Username.Length == 0 || string.IsNullOrEmpty(password)) { Notice = "请输入账户和密码"; return; }
            var client = new HolaApi(url);
            int asked = choice;
            var account = Username;
            var name = Server.Info?.Name ?? ServerName;
            var key = url;
            attempt++;
            var mine = attempt;
            latest[key] = mine;
            Syncing = true; Notify();
            string got = null, failure = null;
            try { got = await client.Login(account, password); }
            catch (Exception e) { failure = e.Message; }
            var newest = latest.TryGetValue(key, out var last) && last == mine;
            // The same server picked again meanwhile, with no newer sign-in, sign-out or 移除 for it: this one still counts.
            var again = choice != asked && key == Server?.Url && !SignedIn && newest;
            if (choice != asked && !again)
            {
                if (got == null) return;
                // Another server was picked: keep this session for when it is picked again.
                // It is closed instead if something newer happened to this server, or it is current and signed in another way.
                var kept = CredentialStore.Load(key);
                if (!newest || key == Server?.Url || !TrySave(got, key))
                {
                    Fire(client.Logout(got));
                }
                else
                {
                    // A session kept from an earlier late sign-in is replaced, so it is closed too.
                    if (kept != null && kept != got) Fire(client.Logout(kept));
                    Remember(new SavedServer(key, name, account));
                }
                return;
            }
            if (again) Username = account;
            Syncing = false;
            if (got != null)
            {
                try { CredentialStore.Save(got, key); }
                catch (Exception e)
                {
                    Notice = "无法保存登录状态：" + e.Message;
                    Fire(client.Logout(got));
                    return;
                }
                token = got; api = client; SignedIn = true; notice = null; Offline = false;
                Remember(new SavedServer(key, name, account));
                StartPolling();
            }
            else notice = failure;
            Notify();
            if (SignedIn) await Refresh();
        }

        static bool TrySave(string t, string key)
        {
            try { CredentialStore.Save(t, key); return true; } catch (Exception) { return false; }
        }

        public void SignOut()
        {
            var oldApi = api; var oldToken = token;
            var left = oldApi?.BaseUrl ?? Server?.Url;
            if (left != null) { attempt++; latest[left] = attempt; }
            StopPolling();
            if (oldApi != null) CredentialStore.Delete(oldApi.BaseUrl);
            token = null; api = null; SignedIn = false; Snapshot = null; Tasks = new List<Todo>(); LastSync = null; Offline = false;
            Syncing = false; Busy = new HashSet<string>(); notice = null;
            if (oldApi != null && oldToken != null) Fire(oldApi.Logout(oldToken));
            Notify();
        }
    }
}
