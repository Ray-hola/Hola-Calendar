using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Hola.Core
{
    /// <summary>
    /// Lenient JSON, read into dictionaries and lists the way the Swift client
    /// reads `[String: Any]`, so a new server field never breaks an old client.
    /// JavaScriptSerializer ships with .NET Framework; no package needed.
    /// </summary>
    public static class Json
    {
        static JavaScriptSerializer Make() => new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        /// <summary>The parsed value, or null when the text is not JSON.</summary>
        public static object Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return Make().DeserializeObject(text); } catch (ArgumentException) { return null; } catch (InvalidOperationException) { return null; }
        }

        public static Dictionary<string, object> Object(string text) => Parse(text) as Dictionary<string, object>;

        public static string Write(object value) => Make().Serialize(value);

        public static Dictionary<string, object> Obj(this Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        public static IEnumerable<Dictionary<string, object>> Objects(this Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || !(v is IEnumerable list) || v is string) yield break;
            foreach (var item in list) if (item is Dictionary<string, object> o) yield return o;
        }

        public static string Str(this Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v as string : null;

        /// <summary>
        /// A bool, or the 0/1 SQLite stores booleans as (the server sends `"admin": 1`).
        /// Swift's `as? Bool` reads both the same way.
        /// </summary>
        public static bool? Bool(this Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v)) return null;
            if (v is bool b) return b;
            if (v is int i && (i == 0 || i == 1)) return i == 1;
            return null;
        }

        /// <summary>A whole number from an int, a double or a numeric string.</summary>
        public static int? Int(this Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return null;
            switch (v)
            {
                case int i: return i;
                case long l when l >= int.MinValue && l <= int.MaxValue: return (int)l;
                case decimal m when m >= int.MinValue && m <= int.MaxValue: return (int)m;
                case double x when x >= int.MinValue && x <= int.MaxValue: return (int)x;
                case string s when int.TryParse(s, out var n): return n;
                default: return null;
            }
        }

        public static List<string> Strings(this Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || !(v is IEnumerable list) || v is string) return null;
            var all = new List<string>();
            foreach (var item in list) { if (item is string s) all.Add(s); else return null; }
            return all;
        }
    }
}
