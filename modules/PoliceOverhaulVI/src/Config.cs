using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class Config
    {
        public bool Enabled = true;
        public bool HidePoliceBlips = true;
        public bool MissionSafeMode = true;
        public bool DebugLogging = true;

        public bool InterceptUnwitnessedWanted = true;
        public float CivilianWitnessDistance = 45f;
        public float PoliceWitnessDistance = 85f;
        public int CivilianReportDelayMs = 4500;
        public int PoliceConfirmDelayMs = 300;
        public int PendingIncidentTimeoutMs = 9000;

        public float FaceRecognitionDistance = 18f;
        public float OutfitRecognitionDistance = 30f;
        public float VehicleRecognitionDistance = 55f;
        public int ReacquireCooldownMs = 7000;
        public int CaseMemoryMinutes = 30;

        public static Config Load(string path)
        {
            var cfg = new Config();
            if (!File.Exists(path))
                return cfg;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                values[section + "." + key] = value;
            }

            cfg.Enabled = GetBool(values, "General.Enabled", cfg.Enabled);
            cfg.HidePoliceBlips = GetBool(values, "General.HidePoliceBlips", cfg.HidePoliceBlips);
            cfg.MissionSafeMode = GetBool(values, "General.MissionSafeMode", cfg.MissionSafeMode);
            cfg.DebugLogging = GetBool(values, "General.DebugLogging", cfg.DebugLogging);

            cfg.InterceptUnwitnessedWanted = GetBool(values, "Witnesses.InterceptUnwitnessedWanted", cfg.InterceptUnwitnessedWanted);
            cfg.CivilianWitnessDistance = GetFloat(values, "Witnesses.CivilianWitnessDistance", cfg.CivilianWitnessDistance);
            cfg.PoliceWitnessDistance = GetFloat(values, "Witnesses.PoliceWitnessDistance", cfg.PoliceWitnessDistance);
            cfg.CivilianReportDelayMs = GetInt(values, "Witnesses.CivilianReportDelayMs", cfg.CivilianReportDelayMs);
            cfg.PoliceConfirmDelayMs = GetInt(values, "Witnesses.PoliceConfirmDelayMs", cfg.PoliceConfirmDelayMs);
            cfg.PendingIncidentTimeoutMs = GetInt(values, "Witnesses.PendingIncidentTimeoutMs", cfg.PendingIncidentTimeoutMs);

            cfg.FaceRecognitionDistance = GetFloat(values, "Identification.FaceRecognitionDistance", cfg.FaceRecognitionDistance);
            cfg.OutfitRecognitionDistance = GetFloat(values, "Identification.OutfitRecognitionDistance", cfg.OutfitRecognitionDistance);
            cfg.VehicleRecognitionDistance = GetFloat(values, "Identification.VehicleRecognitionDistance", cfg.VehicleRecognitionDistance);
            cfg.ReacquireCooldownMs = GetInt(values, "Identification.ReacquireCooldownMs", cfg.ReacquireCooldownMs);
            cfg.CaseMemoryMinutes = GetInt(values, "Identification.CaseMemoryMinutes", cfg.CaseMemoryMinutes);
            return cfg;
        }

        private static bool GetBool(Dictionary<string, string> v, string key, bool fallback)
        {
            string s;
            bool result;
            return v.TryGetValue(key, out s) && bool.TryParse(s, out result) ? result : fallback;
        }

        private static int GetInt(Dictionary<string, string> v, string key, int fallback)
        {
            string s;
            int result;
            return v.TryGetValue(key, out s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        private static float GetFloat(Dictionary<string, string> v, string key, float fallback)
        {
            string s;
            float result;
            return v.TryGetValue(key, out s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }
    }
}
