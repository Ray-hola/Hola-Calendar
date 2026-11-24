using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Hola.Core
{
    /// <summary>
    /// Session tokens live in Windows Credential Manager (控制面板 → 凭据管理器 →
    /// Windows 凭据), one generic credential per server, never in a file. The
    /// Windows counterpart of the macOS app's Keychain entries.
    /// </summary>
    public static class CredentialStore
    {
        public const string Prefix = "cn.hola.desktop.session:";

        const int CRED_TYPE_GENERIC = 1;
        const int CRED_PERSIST_LOCAL_MACHINE = 2;
        const int ERROR_NOT_FOUND = 1168;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredWrite(ref CREDENTIAL credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        static extern void CredFree(IntPtr buffer);

        static string Target(string account) => Prefix + account;

        public static void Save(string token, string account)
        {
            var blob = Encoding.UTF8.GetBytes(token);
            var mem = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, mem, blob.Length);
                var c = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC, TargetName = Target(account), Comment = "哗啦待办桌面 App 的登录状态",
                    CredentialBlobSize = blob.Length, CredentialBlob = mem, Persist = CRED_PERSIST_LOCAL_MACHINE, UserName = account,
                };
                if (!CredWrite(ref c, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        public static string Load(string account)
        {
            if (!CredRead(Target(account), CRED_TYPE_GENERIC, 0, out var ptr)) return null;
            try
            {
                var c = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                if (c.CredentialBlobSize <= 0 || c.CredentialBlob == IntPtr.Zero) return null;
                var blob = new byte[c.CredentialBlobSize];
                Marshal.Copy(c.CredentialBlob, blob, 0, blob.Length);
                var token = Encoding.UTF8.GetString(blob);
                return token.Length == 0 ? null : token;
            }
            finally { CredFree(ptr); }
        }

        /// <summary>Removes the entry; false only when one was there and could not be removed.</summary>
        public static bool Delete(string account) =>
            CredDelete(Target(account), CRED_TYPE_GENERIC, 0) || Marshal.GetLastWin32Error() == ERROR_NOT_FOUND;
    }
}
