using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Hola.Core
{
    /// <summary>
    /// Non-secret preferences (server address, remembered servers, user name,
    /// style, pomodoro) in %LOCALAPPDATA%\HolaDesktop\settings.json, the
    /// Windows stand-in for the macOS app's UserDefaults. Tokens never go here.
    /// </summary>
    public sealed class Settings
    {
        public string Path { get; }
        string Backup => Path + ".bak";
        readonly Dictionary<string, object> values;
        /// <summary>The main file did not parse; it must not be swapped into .bak over the good copy.</summary>
        bool mainBroken;

        public static string DefaultPath =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HolaDesktop", "settings.json");

        public Settings(string path = null)
        {
            Path = path ?? DefaultPath;
            // A file left unreadable by a power cut falls back to the copy from the write before.
            var text = Read(Path);
            var main = Json.Object(text);
            // Locked for a moment (antivirus, indexer) is not broken: only text that will not parse is.
            mainBroken = text != null && main == null;
            values = main ?? Json.Object(Read(Backup)) ?? new Dictionary<string, object>();
        }

        static string Read(string file)
        {
            try { return File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : null; }
            catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
        }

        public string Get(string key) => values.Str(key);

        public Dictionary<string, object> GetObject(string key) => values.Obj(key);

        /// <summary>
        /// Stores a value (null removes it) and writes the file at once: to a temp
        /// file flushed to disk, then swapped in with the old file kept as .bak,
        /// so neither a crash nor a power cut leaves a half-written file behind.
        /// </summary>
        public void Set(string key, object value)
        {
            if (value == null) values.Remove(key); else values[key] = value;
            var temp = Path + ".tmp";
            var bytes = new UTF8Encoding(false).GetBytes(Json.Write(values));
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                    using (var f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        f.Write(bytes, 0, bytes.Length);
                        f.Flush(flushToDisk: true);
                    }
                    // A broken main file is swapped out without becoming the backup, in one step.
                    if (File.Exists(Path)) File.Replace(temp, Path, mainBroken ? null : Backup, ignoreMetadataErrors: true); else File.Move(temp, Path);
                    mainBroken = false;
                    return;
                }
                // An antivirus scan or the indexer can hold the file for a moment.
                catch (IOException) { Thread.Sleep(50); }
                catch (UnauthorizedAccessException) { Thread.Sleep(50); }
            }
        }
    }
}
