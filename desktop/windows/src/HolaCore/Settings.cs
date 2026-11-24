using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
        readonly Dictionary<string, object> values;

        public static string DefaultPath =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HolaDesktop", "settings.json");

        public Settings(string path = null)
        {
            Path = path ?? DefaultPath;
            string text = null;
            try { if (File.Exists(Path)) text = File.ReadAllText(Path, Encoding.UTF8); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            values = Json.Object(text) ?? new Dictionary<string, object>();
        }

        public string Get(string key) => values.Str(key);

        public Dictionary<string, object> GetObject(string key) => values.Obj(key);

        /// <summary>Stores a value (null removes it) and writes the file at once, through a temp file so a crash never leaves half of it.</summary>
        public void Set(string key, object value)
        {
            if (value == null) values.Remove(key); else values[key] = value;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                var temp = Path + ".tmp";
                File.WriteAllText(temp, Json.Write(values), new UTF8Encoding(false));
                if (File.Exists(Path)) File.Replace(temp, Path, null); else File.Move(temp, Path);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
