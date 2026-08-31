using GTA;
using GTA.Native;
using System;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class FineMailSystem
    {
        private const string DataDirectory = "scripts\\PoliceOverhaulVI";
        private const string ArchivePath = DataDirectory + "\\TrafficMail.log";

        public void Deliver(CitationRecord citation, int paid, int unpaid, Config cfg, Action<string> log)
        {
            if (citation == null) return;

            string subject = BuildSubject(citation);
            string body = BuildBody(citation, paid, unpaid);

            if (cfg.FineMailArchiveEnabled)
                Archive(citation, subject, body);

            if (cfg.FineMailEnabled)
            {
                try
                {
                    // Native phone/feed presentation. This deliberately avoids
                    // direct writes into version-sensitive appemail globals.
                    Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
                    Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, body);
                    Function.Call<int>(Hash.END_TEXT_COMMAND_THEFEED_POST_MESSAGETEXT,
                        "CHAR_CALL911", "CHAR_CALL911", true, 4,
                        cfg.FineMailSender, subject);
                    if (cfg.FineMailSound)
                        Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Text_Arrive_Tone", "Phone_SoundSet_Default", true);
                    if (log != null) log("Traffic fine mail delivered: " + subject + ".");
                }
                catch (Exception ex)
                {
                    if (log != null) log("Traffic fine mail notification failed: " + ex.Message);
                }
            }
        }

        private static string BuildSubject(CitationRecord c)
        {
            return "Traffic citation - $" + Math.Max(0, c.Amount);
        }

        private static string BuildBody(CitationRecord c, int paid, int unpaid)
        {
            string where = string.IsNullOrWhiteSpace(c.Street) ? "Los Santos County" : c.Street;
            string reason;
            if (string.Equals(c.Reason, "Speeding", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Speeding: " + c.SpeedKph + " km/h in a " + c.LimitKph + " km/h zone (" +
                         (c.OverKph >= 0 ? "+" : string.Empty) + c.OverKph + ").";
            }
            else reason = string.IsNullOrWhiteSpace(c.Reason) ? "Traffic offence." : c.Reason + ".";

            string payment = unpaid <= 0
                ? "Fine paid automatically: $" + Math.Max(0, paid) + "."
                : "Paid: $" + Math.Max(0, paid) + ". Outstanding: $" + Math.Max(0, unpaid) + ".";

            return reason + " Location: " + where + ". " + payment;
        }

        private static void Archive(CitationRecord citation, string subject, string body)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                              " | " + subject +
                              " | source=" + citation.Source +
                              " | " + body + Environment.NewLine;
                File.AppendAllText(ArchivePath, line);
            }
            catch { }
        }
    }
}
