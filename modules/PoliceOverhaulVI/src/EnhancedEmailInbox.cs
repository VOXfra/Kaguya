using System;

namespace VOX.PoliceOverhaulVI
{
    // GTA V's Story Mode appEmail stores integer EMSTR ids, not arbitrary runtime
    // strings. SHVDNE does not expose FiveM/Cfx ADD_TEXT_ENTRY, so injecting a
    // dynamic subject/body here would either fail to compile or require an
    // unsafe build-specific text-table hook. Keep this bridge explicit and safe:
    // callers retain the normal iFruit notification + persistent TrafficMail.log
    // archive until a dedicated Enhanced GXT2/text-bank package is installed.
    internal static class EnhancedEmailInbox
    {
        private static bool _logged;

        public static bool UpsertTrafficHistory(string subject, string body, Action<string> log)
        {
            if (!_logged && log != null)
            {
                _logged = true;
                log("Vanilla Mail history bridge unavailable without an Enhanced GXT2 text bank; using iFruit feed + TrafficMail.log archive safely.");
            }
            return false;
        }
    }
}
