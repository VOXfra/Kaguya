using GTA;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class FineMailSystem
    {
        private const string DataDirectory = "scripts\\PoliceOverhaulVI";
        private const string ArchivePath = DataDirectory + "\\TrafficMail.log";
        private int _lastHistoryModel;
        private int _lastHistoryRefreshAt;

        public void MaintainPersistentInbox(Config cfg, Action<string> log)
        {
            if (!cfg.FineMailEnabled || !cfg.FineMailArchiveEnabled || !File.Exists(ArchivePath)) return;
            int now = Game.GameTime;
            if (now - _lastHistoryRefreshAt < 5000) return;
            _lastHistoryRefreshAt = now;

            int model = 0;
            try
            {
                Ped p = Game.LocalPlayerPed;
                if (p != null && p.Exists()) model = p.Model.Hash;
            }
            catch { }

            // The native inbox is per protagonist. Re-upsert after a character
            // switch, otherwise leave the row alone until a new citation arrives.
            if (model != 0 && model == _lastHistoryModel) return;
            string history = BuildHistoryBody();
            if (string.IsNullOrWhiteSpace(history)) return;

            bool ok = EnhancedEmailInbox.UpsertTrafficHistory("LSPD Traffic Division - Citation history", history, log);
            if (ok)
            {
                _lastHistoryModel = model;
                if (log != null) log("Persistent iFruit traffic-mail history restored for current protagonist.");
            }
        }

        public void Deliver(CitationRecord citation, int paid, int unpaid, Config cfg, Action<string> log)
        {
            if (citation == null) return;
            string subject = BuildSubject(citation);
            string body = BuildBody(citation, paid, unpaid);
            if (cfg.FineMailArchiveEnabled) Archive(citation, subject, body);

            // Keep one real inbox message containing recent citation history.
            // The helper is build-gated; unsupported builds safely fall back to
            // the normal feed notification + disk archive below.
            if (cfg.FineMailEnabled && cfg.FineMailArchiveEnabled)
            {
                string history = BuildHistoryBody();
                if (!string.IsNullOrWhiteSpace(history))
                {
                    if (EnhancedEmailInbox.UpsertTrafficHistory("LSPD Traffic Division - Citation history", history, log))
                    {
                        try
                        {
                            Ped p = Game.LocalPlayerPed;
                            if (p != null && p.Exists()) _lastHistoryModel = p.Model.Hash;
                        }
                        catch { }
                    }
                }
            }

            if (cfg.FineMailEnabled)
            {
                try
                {
                    Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
                    Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, body);
                    Function.Call<int>(Hash.END_TEXT_COMMAND_THEFEED_POST_MESSAGETEXT,
                        "CHAR_CALL911", "CHAR_CALL911", true, 4, cfg.FineMailSender, subject);
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
                reason = "Speeding: " + c.SpeedKph + " km/h in a " + c.LimitKph + " km/h zone (" + (c.OverKph >= 0 ? "+" : string.Empty) + c.OverKph + ").";
            else if (string.Equals(c.Reason, "Reckless speeding", StringComparison.OrdinalIgnoreCase))
                reason = "Reckless speeding: " + c.SpeedKph + " km/h in a " + c.LimitKph + " km/h zone (" + (c.OverKph >= 0 ? "+" : string.Empty) + c.OverKph + ").";
            else
                reason = string.IsNullOrWhiteSpace(c.Reason) ? "Traffic offence." : c.Reason + ".";

            string camera = string.IsNullOrWhiteSpace(c.CameraId) ? string.Empty : " Camera: " + c.CameraId + ".";
            string payment = unpaid <= 0
                ? "Fine paid automatically: $" + Math.Max(0, paid) + "."
                : "Paid: $" + Math.Max(0, paid) + ". Outstanding: $" + Math.Max(0, unpaid) + ".";
            return reason + " Location: " + where + "." + camera + " " + payment;
        }

        private static string BuildHistoryBody()
        {
            try
            {
                if (!File.Exists(ArchivePath)) return string.Empty;
                string[] lines = File.ReadAllLines(ArchivePath);
                if (lines.Length == 0) return string.Empty;

                var entries = new List<string>();
                for (int i = lines.Length - 1; i >= 0 && entries.Count < 5; i--)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] parts = line.Split(new[] { " | " }, StringSplitOptions.None);
                    if (parts.Length < 2) continue;

                    string stamp = parts[0].Length >= 16 ? parts[0].Substring(5, 11) : parts[0];
                    string subject = parts[1];
                    string details = parts.Length >= 4 ? parts[parts.Length - 1] : string.Empty;
                    if (details.Length > 170) details = details.Substring(0, 170) + "...";
                    entries.Add(stamp + " - " + subject + (details.Length > 0 ? "\n" + details : string.Empty));
                }

                var sb = new StringBuilder();
                sb.Append("Recent LSPD traffic citations:\n\n");
                for (int i = 0; i < entries.Count; i++)
                {
                    if (i > 0) sb.Append("\n\n");
                    sb.Append(entries[i]);
                    if (sb.Length > 850) break;
                }
                if (sb.Length > 900) sb.Length = 900;
                return sb.ToString();
            }
            catch { return string.Empty; }
        }

        private static void Archive(CitationRecord citation, string subject, string body)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + subject +
                              " | source=" + citation.Source + " | camera=" + (citation.CameraId ?? string.Empty) +
                              " | " + body + Environment.NewLine;
                File.AppendAllText(ArchivePath, line);
            }
            catch { }
        }
    }
}
