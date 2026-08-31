using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class BoloReportResult
    {
        public bool Triggered;
        public int WantedLevel;
        public Vector3 SeenPosition;
        public int WitnessHandle;
    }

    internal sealed class BoloRecognitionSystem
    {
        private const string ConfigPath = "scripts\\PoliceOverhaulVI.ini";
        private readonly BoloSettings _settings;
        private readonly HashSet<int> _merchantModels = new HashSet<int>();
        private int _candidateHandle;
        private int _candidateSince;
        private int _reportingHandle;
        private int _reportingSince;
        private Vector3 _reportedSeenPosition;
        private int _cooldownUntil;
        private bool _recognitionLogged;

        public BoloRecognitionSystem()
        {
            _settings = BoloSettings.Load(ConfigPath);
            AddMerchant("mp_m_shopkeep_01");
            AddMerchant("s_f_y_shop_low");
            AddMerchant("s_f_y_shop_mid");
            AddMerchant("s_f_m_shop_high");
            AddMerchant("s_m_y_shop_mask");
            AddMerchant("s_m_y_ammucity_01");
            AddMerchant("s_m_m_ammucountry");
        }

        public BoloReportResult Update(Ped player, CaseMemory memory, int wanted, Action<string> log)
        {
            var none = new BoloReportResult();
            if (!_settings.Enabled || player == null || !player.Exists() || player.IsDead || memory == null) { ResetCandidate(); return none; }
            int now = Game.GameTime;
            if (wanted > 0 || now < _cooldownUntil) { ResetCandidate(); return none; }
            if (!memory.Active || !memory.FaceKnown || memory.FaceConfidence < _settings.MinimumFaceConfidence ||
                (memory.ThreatLevel < _settings.MinimumPriorThreat && !memory.MostWanted))
            {
                ResetCandidate(); return none;
            }
            if (OutfitSignature.FaceObscured(player)) { ResetCandidate(); return none; }

            if (_reportingHandle != 0)
            {
                Ped reporter = FindPedByHandle(player, _reportingHandle);
                if (reporter == null || !reporter.Exists() || reporter.IsDead)
                {
                    ResetCandidate(); return none;
                }
                if (now - _reportingSince < _settings.ReportDelayMs) return none;

                int level = memory.MostWanted ? Math.Max(_settings.ReportWantedLevel, 3) : _settings.ReportWantedLevel;
                level = Math.Max(1, Math.Min(5, level));
                _cooldownUntil = now + _settings.ReportCooldownMs;
                var result = new BoloReportResult
                {
                    Triggered = true,
                    WantedLevel = level,
                    SeenPosition = _reportedSeenPosition,
                    WitnessHandle = _reportingHandle
                };
                CoreWorldMemoryBridge.Publish("police", "bolo_shop_report", result.SeenPosition.X, result.SeenPosition.Y, result.SeenPosition.Z,
                    Math.Max(memory.ThreatLevel, level), memory.SuspectModelHash, "Merchant", 4.0, "faceKnown=true");
                if (log != null) log("Merchant BOLO report completed; priorThreat=" + memory.ThreatLevel + " dispatch=" + level + ".");
                ResetCandidate();
                return result;
            }

            Ped best = FindSeeingMerchant(player);
            if (best == null)
            {
                ResetCandidate();
                return none;
            }

            if (_candidateHandle != best.Handle)
            {
                _candidateHandle = best.Handle;
                _candidateSince = now;
                _recognitionLogged = false;
                return none;
            }

            int required = RecognitionTimeFor(memory, best, player);
            if (now - _candidateSince < required) return none;

            if (!_recognitionLogged)
            {
                _recognitionLogged = true;
                if (log != null) log("Merchant recognized BOLO face and began a discreet police call.");
            }
            _reportingHandle = best.Handle;
            _reportingSince = now;
            _reportedSeenPosition = player.Position;
            return none;
        }

        public void Reset()
        {
            _cooldownUntil = 0;
            ResetCandidate();
        }

        private Ped FindSeeingMerchant(Ped player)
        {
            Ped best = null;
            float bestDistance = float.MaxValue;
            foreach (Ped ped in World.GetNearbyPeds(player, _settings.RecognitionDistance))
            {
                if (ped == null || !ped.Exists() || ped.IsDead || !ped.IsHuman || ped.Handle == player.Handle || ped.IsInVehicle()) continue;
                if (!_merchantModels.Contains(ped.Model.Hash)) continue;
                float distance = Perception.Distance(ped.Position, player.Position);
                if (distance > _settings.RecognitionDistance) continue;
                if (!Facing(ped, player, _settings.MinimumFacingDot)) continue;
                bool los = false;
                try { los = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, ped.Handle, player.Handle, 17); } catch { }
                if (!los) continue;
                if (distance < bestDistance) { best = ped; bestDistance = distance; }
            }
            return best;
        }

        private Ped FindPedByHandle(Ped player, int handle)
        {
            foreach (Ped ped in World.GetNearbyPeds(player, _settings.RecognitionDistance * 1.8f))
            {
                if (ped != null && ped.Exists() && ped.Handle == handle) return ped;
            }
            return null;
        }

        private int RecognitionTimeFor(CaseMemory memory, Ped merchant, Ped player)
        {
            float distance = Perception.Distance(merchant.Position, player.Position);
            float distanceT = Math.Min(1f, distance / Math.Max(1f, _settings.RecognitionDistance));
            float confidence = Math.Min(1f, Math.Max(0f, memory.FaceConfidence / 100f));
            float t = 0.62f * distanceT + 0.38f * (1f - confidence);
            return (int)(_settings.MinRecognitionMs + (_settings.MaxRecognitionMs - _settings.MinRecognitionMs) * t);
        }

        private static bool Facing(Ped observer, Ped target, float minimumDot)
        {
            Vector3 a = observer.Position, b = target.Position, f = observer.ForwardVector;
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 0.001) return true;
            return (f.X * dx + f.Y * dy + f.Z * dz) / len >= minimumDot;
        }

        private void AddMerchant(string modelName)
        {
            try { _merchantModels.Add(Function.Call<int>(Hash.GET_HASH_KEY, modelName)); } catch { }
        }

        private void ResetCandidate()
        {
            _candidateHandle = 0;
            _candidateSince = 0;
            _reportingHandle = 0;
            _reportingSince = 0;
            _recognitionLogged = false;
        }
    }

    internal sealed class BoloSettings
    {
        public bool Enabled = true;
        public int MinimumPriorThreat = 4;
        public float MinimumFaceConfidence = 68f;
        public float RecognitionDistance = 16f;
        public float MinimumFacingDot = -0.05f;
        public int MinRecognitionMs = 1300;
        public int MaxRecognitionMs = 3200;
        public int ReportDelayMs = 2500;
        public int ReportWantedLevel = 2;
        public int ReportCooldownMs = 60000;

        public static BoloSettings Load(string path)
        {
            var s = new BoloSettings();
            try
            {
                string section = string.Empty;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                    if (!string.Equals(section, "BOLO", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('='); if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                    bool bv; int iv; float fv;
                    if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out bv)) s.Enabled = bv;
                    else if (key.Equals("MinimumPriorThreat", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out iv)) s.MinimumPriorThreat = iv;
                    else if (key.Equals("MinimumFaceConfidence", StringComparison.OrdinalIgnoreCase) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) s.MinimumFaceConfidence = fv;
                    else if (key.Equals("RecognitionDistance", StringComparison.OrdinalIgnoreCase) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) s.RecognitionDistance = fv;
                    else if (key.Equals("MinimumFacingDot", StringComparison.OrdinalIgnoreCase) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) s.MinimumFacingDot = fv;
                    else if (key.Equals("MinRecognitionMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out iv)) s.MinRecognitionMs = iv;
                    else if (key.Equals("MaxRecognitionMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out iv)) s.MaxRecognitionMs = iv;
                    else if (key.Equals("ReportDelayMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out iv)) s.ReportDelayMs = iv;
                    else if (key.Equals("ReportWantedLevel", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out iv)) s.ReportWantedLevel = iv;
                    else if (key.Equals("ReportCooldownMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out iv)) s.ReportCooldownMs = iv;
                }
            }
            catch { }
            return s;
        }
    }
}
