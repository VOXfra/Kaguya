using GTA;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace VOX.CoreVI
{
    public sealed class WorldEventRecord
    {
        public string Id = string.Empty;
        public string Category = string.Empty;
        public string Type = string.Empty;
        public float X;
        public float Y;
        public float Z;
        public int Severity;
        public int SuspectModelHash;
        public string Source = string.Empty;
        public string Tags = string.Empty;
        public long CreatedUtcTicks;
        public long ExpiresUtcTicks;

        public bool IsExpiredUtc()
        {
            return ExpiresUtcTicks > 0 && DateTime.UtcNow.Ticks >= ExpiresUtcTicks;
        }
    }

    public static class WorldMemoryBridge
    {
        private static readonly object Sync = new object();
        private static readonly List<WorldEventRecord> Events = new List<WorldEventRecord>();
        private static string _path = "scripts\\VOXCoreVI\\WorldMemory.xml";
        private static bool _loaded;
        private static bool _dirty;

        internal static void Initialize(string path, Action<string> log)
        {
            lock (Sync)
            {
                if (!string.IsNullOrWhiteSpace(path)) _path = path;
                if (_loaded) return;
                LoadInternal(log);
                _loaded = true;
            }
        }

        public static string Publish(string category, string type, float x, float y, float z, int severity,
            int suspectModelHash, string source, double ttlHours, string tags)
        {
            lock (Sync)
            {
                if (!_loaded) Initialize(_path, null);
                CleanupExpiredInternal();

                var now = DateTime.UtcNow;
                var evt = new WorldEventRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Category = category ?? string.Empty,
                    Type = type ?? string.Empty,
                    X = x,
                    Y = y,
                    Z = z,
                    Severity = Math.Max(0, severity),
                    SuspectModelHash = suspectModelHash,
                    Source = source ?? string.Empty,
                    Tags = tags ?? string.Empty,
                    CreatedUtcTicks = now.Ticks,
                    ExpiresUtcTicks = ttlHours > 0 ? now.AddHours(ttlHours).Ticks : 0
                };

                Events.Add(evt);
                _dirty = true;
                return evt.Id;
            }
        }

        public static WorldEventRecord[] Snapshot()
        {
            lock (Sync)
            {
                CleanupExpiredInternal();
                return Events.ToArray();
            }
        }

        public static WorldEventRecord[] Nearby(float x, float y, float z, float radius, string category)
        {
            lock (Sync)
            {
                CleanupExpiredInternal();
                float r2 = Math.Max(0f, radius) * Math.Max(0f, radius);
                var result = new List<WorldEventRecord>();
                foreach (WorldEventRecord evt in Events)
                {
                    if (!string.IsNullOrEmpty(category) && !string.Equals(evt.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
                    float dx = evt.X - x, dy = evt.Y - y, dz = evt.Z - z;
                    if (dx * dx + dy * dy + dz * dz <= r2) result.Add(evt);
                }
                return result.ToArray();
            }
        }

        internal static bool IsDirty
        {
            get { lock (Sync) return _dirty; }
        }

        internal static void Save(Action<string> log)
        {
            lock (Sync)
            {
                if (!_loaded) Initialize(_path, log);
                CleanupExpiredInternal();
                if (!_dirty) return;
                try
                {
                    string dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
                    using (XmlWriter w = XmlWriter.Create(_path, settings))
                    {
                        w.WriteStartDocument();
                        w.WriteStartElement("VOXCoreVI");
                        w.WriteAttributeString("version", "0.1.0");
                        w.WriteStartElement("Events");
                        foreach (WorldEventRecord evt in Events)
                        {
                            w.WriteStartElement("Event");
                            Write(w, "Id", evt.Id); Write(w, "Category", evt.Category); Write(w, "Type", evt.Type);
                            Write(w, "X", evt.X); Write(w, "Y", evt.Y); Write(w, "Z", evt.Z);
                            Write(w, "Severity", evt.Severity); Write(w, "SuspectModelHash", evt.SuspectModelHash);
                            Write(w, "Source", evt.Source); Write(w, "Tags", evt.Tags);
                            Write(w, "CreatedUtcTicks", evt.CreatedUtcTicks); Write(w, "ExpiresUtcTicks", evt.ExpiresUtcTicks);
                            w.WriteEndElement();
                        }
                        w.WriteEndElement(); w.WriteEndElement(); w.WriteEndDocument();
                    }
                    _dirty = false;
                }
                catch (Exception ex)
                {
                    if (log != null) log("World memory save failed: " + ex.Message);
                }
            }
        }

        internal static void CleanupExpired()
        {
            lock (Sync) CleanupExpiredInternal();
        }

        private static void CleanupExpiredInternal()
        {
            int before = Events.Count;
            Events.RemoveAll(e => e == null || e.IsExpiredUtc());
            if (Events.Count != before) _dirty = true;
        }

        private static void LoadInternal(Action<string> log)
        {
            Events.Clear();
            if (!File.Exists(_path)) return;
            try
            {
                var doc = new XmlDocument(); doc.Load(_path);
                XmlNodeList nodes = doc.SelectNodes("/VOXCoreVI/Events/Event");
                if (nodes != null)
                {
                    foreach (XmlNode n in nodes)
                    {
                        var evt = new WorldEventRecord
                        {
                            Id = ReadText(n, "Id", Guid.NewGuid().ToString("N")),
                            Category = ReadText(n, "Category", string.Empty),
                            Type = ReadText(n, "Type", string.Empty),
                            X = ReadFloat(n, "X", 0f), Y = ReadFloat(n, "Y", 0f), Z = ReadFloat(n, "Z", 0f),
                            Severity = ReadInt(n, "Severity", 0),
                            SuspectModelHash = ReadInt(n, "SuspectModelHash", 0),
                            Source = ReadText(n, "Source", string.Empty),
                            Tags = ReadText(n, "Tags", string.Empty),
                            CreatedUtcTicks = ReadLong(n, "CreatedUtcTicks", 0),
                            ExpiresUtcTicks = ReadLong(n, "ExpiresUtcTicks", 0)
                        };
                        if (!evt.IsExpiredUtc()) Events.Add(evt);
                    }
                }
                if (log != null) log("World memory loaded: " + Events.Count + " active events.");
            }
            catch (Exception ex)
            {
                if (log != null) log("World memory load failed safely: " + ex.Message);
            }
        }

        private static void Write(XmlWriter w, string name, object value)
        {
            string s;
            if (value is float f) s = f.ToString(CultureInfo.InvariantCulture);
            else if (value is double d) s = d.ToString(CultureInfo.InvariantCulture);
            else s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            w.WriteElementString(name, s);
        }
        private static string ReadText(XmlNode n, string name, string fallback) { XmlNode c = n == null ? null : n.SelectSingleNode(name); return c == null ? fallback : c.InnerText; }
        private static int ReadInt(XmlNode n, string name, int fallback) { int v; return int.TryParse(ReadText(n, name, string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback; }
        private static long ReadLong(XmlNode n, string name, long fallback) { long v; return long.TryParse(ReadText(n, name, string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback; }
        private static float ReadFloat(XmlNode n, string name, float fallback) { float v; return float.TryParse(ReadText(n, name, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback; }
    }

    public sealed class VOXCoreVIScript : Script
    {
        private const string DataDirectory = "scripts\\VOXCoreVI";
        private const string MemoryPath = DataDirectory + "\\WorldMemory.xml";
        private const string LogPath = DataDirectory + "\\VOXCoreVI.log";
        private int _lastSave;

        public VOXCoreVIScript()
        {
            Directory.CreateDirectory(DataDirectory);
            WorldMemoryBridge.Initialize(MemoryPath, Log);
            Tick += OnTick;
            Aborted += OnAborted;
            Interval = 500;
            Log("VOX Core VI 0.1.0 shared persistent world-memory runtime loaded.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            WorldMemoryBridge.CleanupExpired();
            if (Game.GameTime - _lastSave >= 10000 && WorldMemoryBridge.IsDirty)
            {
                _lastSave = Game.GameTime;
                WorldMemoryBridge.Save(Log);
            }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            WorldMemoryBridge.Save(Log);
        }

        private static void Log(string text)
        {
            try { File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine); }
            catch { }
        }
    }
}
