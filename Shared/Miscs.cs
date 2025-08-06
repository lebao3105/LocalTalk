using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Reflection;

namespace LocalTalk.Shared
{
    public class StringList: List<string> { }

    public struct Languages
    {
        public static readonly string enUS = "en-US";

        public static bool HasCode(string code)
        {
            return
#if WINDOWS_PHONE_APP
            typeof(Languages).GetRuntimeFields()
#else
            typeof(Languages).GetFields(BindingFlags.Static)
#endif
                             .Any(elm =>
                                    elm.Name.Equals(code.Replace("-", ""), StringComparison.OrdinalIgnoreCase) &&
                                    elm.IsStatic && elm.IsLiteral);
        }
    }

    public enum FileTransferResponses
    {
        Finished = 204,
        InvalidBody = 400,
        InvalidPIN = 401,
        Rejected = 403,
        BlockedByOtherSessions = 409,
        TooManyRequests = 429,
        Unknown = 500
    }
}