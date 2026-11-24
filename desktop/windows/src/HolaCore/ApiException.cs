using System;

namespace Hola.Core
{
    public enum ApiErrorKind
    {
        /// <summary>Network layer failure: no route, DNS, refused, timeout. The UI keeps the last list.</summary>
        Offline,
        /// <summary>401 from the server: the session is gone, sign in again.</summary>
        Unauthorized,
        /// <summary>Any other non-200 answer, with the server's own `error` text (403/409/429...).</summary>
        Server,
        BadUrl,
        BadResponse,
        /// <summary>The address answered, but not as a 哗啦待办 server.</summary>
        NotHola,
        /// <summary>The server speaks a newer API than this app knows.</summary>
        Incompatible,
    }

    public sealed class ApiException : Exception
    {
        public ApiErrorKind Kind { get; }
        /// <summary>HTTP status for Server, the server's API version for Incompatible.</summary>
        public int Code { get; }
        /// <summary>The text that came with the error (the server's `error`, or the network's message).</summary>
        public string Detail { get; }

        public ApiException(ApiErrorKind kind, string detail = "", int code = 0) : base(Describe(kind, detail ?? "", code))
        {
            Kind = kind; Detail = detail ?? ""; Code = code;
        }

        static string Describe(ApiErrorKind kind, string s, int code)
        {
            switch (kind)
            {
                case ApiErrorKind.Offline: return s.Length == 0 ? "无法连接服务器" : s;
                case ApiErrorKind.Unauthorized: return s.Length == 0 ? "请重新登录" : s;
                case ApiErrorKind.Server: return s;
                case ApiErrorKind.BadUrl: return "服务器地址无效";
                case ApiErrorKind.BadResponse: return "服务器返回了无法识别的内容";
                case ApiErrorKind.NotHola: return "这个地址不是哗啦待办服务器";
                default: return "服务器版本比这个 App 新，请先更新 App";
            }
        }

        /// <summary>How much a failure says about the address, for picking which one to show.</summary>
        public int Rank
        {
            get
            {
                switch (Kind)
                {
                    case ApiErrorKind.Incompatible: return 4;
                    case ApiErrorKind.NotHola: case ApiErrorKind.BadResponse: return 3;
                    case ApiErrorKind.Server: case ApiErrorKind.Unauthorized: return 2;
                    default: return 1;
                }
            }
        }

        /// <summary>Same kind, code and text. Offline compares by kind only: its text comes from the OS.</summary>
        public bool Same(ApiException other) =>
            other != null && other.Kind == Kind && (Kind == ApiErrorKind.Offline || (other.Code == Code && other.Detail == Detail));

        public override string ToString() => $"{Kind}({Code}, {Detail})";
    }
}
