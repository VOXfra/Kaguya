using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class CrimeSceneRecord
    {
        public string Id = string.Empty;
        public float X, Y, Z;
        public int Severity;
        public int SuspectModelHash;
        public string Source = string.Empty;
        public int EvidenceCount;
        public bool FirearmEvidence;
        public bool FatalEvidence;
        public long CreatedUtcTicks;
        public long LastUpdatedUtcTicks;
        public long ExpiresUtcTicks;

        public Vector3 Position { get { return new Vector3(X, Y, Z); } }
        public bool Expired { get { return ExpiresUtcTicks > 0 && DateTime.UtcNow.Ticks >= ExpiresUtcTicks; } }
    }

    internal sealed class CrimeSceneSystem
    {
        private const string ConfigPath = "scripts\\PoliceOverhaulVI.ini";
        private const string DataPath = "scripts\\PoliceOverhaulVI\\CrimeScenes.xml";
        private readonly List<CrimeSceneRecord> _scenes = new List<CrimeSceneRecord>();
        private readonly HashSet<int> _seenDeadPeds = new HashSet<int>();
        private readonly CrimeSceneSettings _settings;
        private int _lastViolenceScan;
        private int _lastSave;
        private string _lastNearbyScene = string.Empty;
        private bool _dirty;

        public CrimeSceneSystem(Action<string> log)
        {
            _settings = CrimeSceneSettings.Load(ConfigPath);
            Load(log);
        }

        public void RecordIncident(Vector3 position, int severity, int suspectModelHash, ObservationSource source,
            bool firearm, bool fatal, int evidenceCount, Action<string> log)
        {
            if (!_settings.Enabled || severity < _settings.MinimumSeverity) return;
            CleanupExpired();

            CrimeSceneRecord scene = null;
            foreach (CrimeSceneRecord candidate in _scenes)
            {
                if (candidate == null || candidate.Expired) continue;
                if (Perception.Distance(candidate.Position, position) > _settings.MergeRadius) continue;
                if (candidate.SuspectModelHash != 0 && suspectModelHash != 0 && candidate.SuspectModelHash != suspectModelHash) continue;
                scene = candidate;
                break;
            }

            DateTime now = DateTime.UtcNow;
            if (scene == null)
            {
                scene = new CrimeSceneRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    X = position.X, Y = position.Y, Z = position.Z,
                    Severity = Math.Max(1, severity),
                    SuspectModelHash = suspectModelHash,
                    Source = source.ToString(),
                    EvidenceCount = Math.Max(1, evidenceCount),
                    FirearmEvidence = firearm,
                    FatalEvidence = fatal,
                    CreatedUtcTicks = now.Ticks,
                    LastUpdatedUtcTicks = now.Ticks,
                    ExpiresUtcTicks = now.AddHours(_settings.LifetimeHoursForSeverity(severity)).Ticks
                };
                _scenes.Add(scene);
                if (log != null) log("Crime scene created severity=" + scene.Severity + " evidence=" + scene.EvidenceCount + ".");
            }
            else
            {
                scene.Severity = Math.Max(scene.Severity, severity);
                scene.EvidenceCount = Math.Min(12, scene.EvidenceCount + Math.Max(1, evidenceCount));
                scene.FirearmEvidence |= firearm;
                scene.FatalEvidence |= fatal;
                scene.LastUpdatedUtcTicks = now.Ticks;
                scene.ExpiresUtcTicks = now.AddHours(_settings.LifetimeHoursForSeverity(scene.Severity)).Ticks;
            }

            _dirty = true;
            CoreWorldMemoryBridge.Publish("crime", fatal ? "fatal_scene" : (firearm ? "firearm_scene" : "crime_scene"),
                scene.X, scene.Y, scene.Z, scene.Severity, suspectModelHash, source.ToString(),
                _settings.LifetimeHoursForSeverity(scene.Severity),
                "evidence=" + scene.EvidenceCount + ";firearm=" + scene.FirearmEvidence + ";fatal=" + scene.FatalEvidence);
        }

        public void Update(Ped player, int wanted, int suspectModelHash, Action<string> log)
        {
            if (!_settings.Enabled || player == null || !player.Exists()) return;
            int now = Game.GameTime;

            if (wanted > 0 && now - _lastViolenceScan >= _settings.ViolenceScanIntervalMs)
            {
                _lastViolenceScan = now;
                ScanViolence(player, wanted, suspectModelHash, log);
            }

            CleanupExpired();
            if (wanted == 0) DrawNearbyScene(player, log);

            if (_dirty && now - _lastSave >= 10000)
            {
                _lastSave = now;
                Save(log);
            }
        }

        public void Save(Action<string> log)
        {
            if (!_dirty && File.Exists(DataPath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DataPath));
                var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
                using (XmlWriter w = XmlWriter.Create(DataPath, settings))
                {
                    w.WriteStartDocument(); w.WriteStartElement("CrimeScenes"); w.WriteAttributeString("version", "0.4.0");
                    foreach (CrimeSceneRecord s in _scenes)
                    {
                        if (s == null || s.Expired) continue;
                        w.WriteStartElement("Scene");
                        Write(w, "Id", s.Id); Write(w, "X", s.X); Write(w, "Y", s.Y); Write(w, "Z", s.Z);
                        Write(w, "Severity", s.Severity); Write(w, "SuspectModelHash", s.SuspectModelHash); Write(w, "Source", s.Source);
                        Write(w, "EvidenceCount", s.EvidenceCount); Write(w, "FirearmEvidence", s.FirearmEvidence); Write(w, "FatalEvidence", s.FatalEvidence);
                        Write(w, "CreatedUtcTicks", s.CreatedUtcTicks); Write(w, "LastUpdatedUtcTicks", s.LastUpdatedUtcTicks); Write(w, "ExpiresUtcTicks", s.ExpiresUtcTicks);
                        w.WriteEndElement();
                    }
                    w.WriteEndElement(); w.WriteEndDocument();
                }
                _dirty = false;
            }
            catch (Exception ex) { if (log != null) log("Crime-scene save failed: " + ex.Message); }
        }

        private void ScanViolence(Ped player, int wanted, int suspectModelHash, Action<string> log)
        {
            bool playerShooting = false;
            try { playerShooting = Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle); } catch { }

            foreach (Ped ped in World.GetNearbyPeds(player, 42f))
            {
                if (ped == null || !ped.Exists() || ped.Handle == player.Handle || !ped.IsDead) continue;
                if (!_seenDeadPeds.Add(ped.Handle)) continue;
                RecordIncident(ped.Position, Math.Max(3, wanted), suspectModelHash, ObservationSource.Police,
                    playerShooting || wanted >= 3, true, 3, log);
            }

            if (playerShooting && wanted >= 2)
                RecordIncident(player.Position, wanted, suspectModelHash, ObservationSource.Police, true, false, 1, log);
        }

        private void DrawNearbyScene(Ped player, Action<string> log)
        {
            CrimeSceneRecord nearest = null;
            float best = float.MaxValue;
            foreach (CrimeSceneRecord scene in _scenes)
            {
                if (scene == null || scene.Expired) continue;
                float d = Perception.Distance(scene.Position, player.Position);
                if (d < best) { best = d; nearest = scene; }
            }

            if (nearest == null || best > _settings.ActivationRadius)
            {
                _lastNearbyScene = string.Empty;
                return;
            }

            if (_lastNearbyScene != nearest.Id)
            {
                _lastNearbyScene = nearest.Id;
                if (log != null) log("Player returned to persistent crime scene " + nearest.Id + " severity=" + nearest.Severity + ".");
            }

            Vector3 p = nearest.Position;
            float radius = Math.Min(_settings.MaximumZoneRadius, _settings.BaseZoneRadius + nearest.Severity * _settings.RadiusPerSeverity);
            DrawFlatMarker(p, radius * 2f, 40, 110, 210, 20);

            int count = Math.Max(2, Math.Min(6, nearest.EvidenceCount));
            for (int i = 0; i < count; i++)
            {
                double angle = (Math.PI * 2.0 / count) * i + (nearest.Id.GetHashCode() & 7) * 0.1;
                float r = Math.Min(radius * 0.55f, 3.2f + i * 0.85f);
                Vector3 mark = new Vector3(p.X + (float)Math.Cos(angle) * r, p.Y + (float)Math.Sin(angle) * r, p.Z + 0.03f);
                DrawFlatMarker(mark, 0.55f, 245, 205, 55, 150);
            }
        }

        private static void DrawFlatMarker(Vector3 p, float diameter, int r, int g, int b, int a)
        {
            try
            {
                Function.Call(Hash.DRAW_MARKER,
                    1, p.X, p.Y, p.Z - 0.95f,
                    0f, 0f, 0f,
                    0f, 0f, 0f,
                    diameter, diameter, 0.08f,
                    r, g, b, a,
                    false, false, 2, false,
                    null, null, false);
            }
            catch { }
        }

        private void CleanupExpired()
        {
            int before = _scenes.Count;
            _scenes.RemoveAll(s => s == null || s.Expired);
            if (_scenes.Count != before) _dirty = true;
        }

        private void Load(Action<string> log)
        {
            if (!File.Exists(DataPath)) return;
            try
            {
                var doc = new XmlDocument(); doc.Load(DataPath);
                XmlNodeList nodes = doc.SelectNodes("/CrimeScenes/Scene");
                if (nodes != null)
                {
                    foreach (XmlNode n in nodes)
                    {
                        var s = new CrimeSceneRecord
                        {
                            Id = ReadText(n, "Id", Guid.NewGuid().ToString("N")),
                            X = ReadFloat(n, "X", 0f), Y = ReadFloat(n, "Y", 0f), Z = ReadFloat(n, "Z", 0f),
                            Severity = ReadInt(n, "Severity", 1), SuspectModelHash = ReadInt(n, "SuspectModelHash", 0),
                            Source = ReadText(n, "Source", string.Empty), EvidenceCount = ReadInt(n, "EvidenceCount", 1),
                            FirearmEvidence = ReadBool(n, "FirearmEvidence", false), FatalEvidence = ReadBool(n, "FatalEvidence", false),
                            CreatedUtcTicks = ReadLong(n, "CreatedUtcTicks", 0), LastUpdatedUtcTicks = ReadLong(n, "LastUpdatedUtcTicks", 0),
                            ExpiresUtcTicks = ReadLong(n, "ExpiresUtcTicks", 0)
                        };
                        if (!s.Expired) _scenes.Add(s);
                    }
                }
                if (log != null) log("Persistent crime scenes loaded: " + _scenes.Count + ".");
            }
            catch (Exception ex) { if (log != null) log("Crime-scene load failed safely: " + ex.Message); }
        }

        private static void Write(XmlWriter w, string name, object value)
        {
            string s;
            if (value is float f) s = f.ToString(CultureInfo.InvariantCulture);
            else if (value is bool b) s = b ? "true" : "false";
            else s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            w.WriteElementString(name, s);
        }
        private static string ReadText(XmlNode n, string name, string fallback) { XmlNode c = n == null ? null : n.SelectSingleNode(name); return c == null ? fallback : c.InnerText; }
        private static int ReadInt(XmlNode n, string name, int fallback) { int v; return int.TryParse(ReadText(n, name, string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback; }
        private static long ReadLong(XmlNode n, string name, long fallback) { long v; return long.TryParse(ReadText(n, name, string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback; }
        private static float ReadFloat(XmlNode n, string name, float fallback) { float v; return float.TryParse(ReadText(n, name, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback; }
        private static bool ReadBool(XmlNode n, string name, bool fallback) { bool v; return bool.TryParse(ReadText(n, name, string.Empty), out v) ? v : fallback; }
    }

    internal sealed class CrimeSceneSettings
    {
        public bool Enabled = true;
        public int MinimumSeverity = 2;
        public float MergeRadius = 28f;
        public float ActivationRadius = 170f;
        public float BaseZoneRadius = 8f;
        public float RadiusPerSeverity = 2.2f;
        public float MaximumZoneRadius = 24f;
        public int ViolenceScanIntervalMs = 800;
        public double MinorLifetimeHours = 2.5;
        public double MajorLifetimeHours = 7.0;
        public double FatalLifetimeHours = 12.0;

        public double LifetimeHoursForSeverity(int severity)
        {
            return severity >= 5 ? FatalLifetimeHours : (severity >= 3 ? MajorLifetimeHours : MinorLifetimeHours);
        }

        public static CrimeSceneSettings Load(string path)
        {
            var s = new CrimeSceneSettings();
            try
            {
                string section = string.Empty;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                    if (!string.Equals(section, "CrimeScenes", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('='); if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                    bool bv; int iv; float fv; double dv;
                    if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out bv)) s.Enabled = bv;
                    else if (key.Equals("MinimumSeverity", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out iv)) s.MinimumSeverity = iv;
                    else if (key.Equals("MergeRadius", StringComparison.OrdinalIgnoreCase) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) s.MergeRadius = fv;
                    else if (key.Equals("ActivationRadius", StringComparison.OrdinalIgnoreCase) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) s.ActivationRadius = fv;
                    else if (key.Equals("BaseZoneRadius", StringComparison.OrdinalIgnoreCase) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) s.BaseZoneRadius = fv;
                    else if (key.Equals("RadiusPerSeverity", StringComparison.OrdinalIgnoreCase) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) s.RadiusPerSeverity = fv;
                    else if (key.Equals("MaximumZoneRadius", StringComparison.OrdinalIgnoreCase) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) s.MaximumZoneRadius = fv;
                    else if (key.Equals("ViolenceScanIntervalMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out iv)) s.ViolenceScanIntervalMs = iv;
                    else if (key.Equals("MinorLifetimeHours", StringComparison.OrdinalIgnoreCase) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) s.MinorLifetimeHours = dv;
                    else if (key.Equals("MajorLifetimeHours", StringComparison.OrdinalIgnoreCase) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) s.MajorLifetimeHours = dv;
                    else if (key.Equals("FatalLifetimeHours", StringComparison.OrdinalIgnoreCase) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) s.FatalLifetimeHours = dv;
                }
            }
            catch { }
            return s;
        }
    }
}
