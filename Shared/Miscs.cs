using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Reflection;

#if WINDOWS_UWP
using UWP = Windows.UI.Xaml.Controls;
#endif

namespace Shared
{
    public class StringList: List<string> { }

    public struct Languages
    {
        public static readonly string enUS = "en-US";

        public static bool HasCode(string code)
            => typeof(Languages).GetFields(BindingFlags.Static)
                                .Any(elm => elm.Name.Equals(code));
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

namespace System.Windows.Controls
{
#if WINDOWS_PHONE
    public class ListView : Microsoft.Phone.Controls.LongListSelector { }
#else
    public class ListView : UWP.ListView { }
#endif
}