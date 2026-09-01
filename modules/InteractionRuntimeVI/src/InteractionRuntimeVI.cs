using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace VOX.InteractionRuntimeVI
{
    public sealed class InteractionRuntimeVIScript : Script
    {
        private const int InputContext = 51;
        private const int InputFrontendUp = 172;
        private const int InputFrontendLeft = 174;
        private const int InputFrontendRight = 175;
        private const string ConfigPath = "scripts\\InteractionRuntimeVI.ini";
        private const string DataDirectory = "scripts\\InteractionRuntimeVI";
        private const string LogPath = DataDirectory + "\\InteractionRuntimeVI.log";

        private readonly Dictionary<int, Memory> _memory = new Dictionary<int, Memory>();
        private Config _cfg;
        private bool _focusDown;
        private int _focusStarted;
        private int _lastTargetScan;
        private int _lastLookTask;
        private int _lastInteraction;
        private int _targetLostSince;
        private int _candidateSince;
        private int _candidateHandle;
        private Ped _target;
        private bool _positiveControlDown;
        private bool _contextControlDown;
        private bool _negativeControlDown;
        private MethodInfo _pedBridgeRegister;
        private MethodInfo _pedBridgeFear;
        private MethodInfo _pedBridgeOpinion;
        private int _lastBridgeProbe;
        private int _storyYieldUntil;

        public InteractionRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDirectory);
            _cfg = Config.Load(ConfigPath);
            Interval = Math.Max(10, _cfg.TickIntervalMs);
            Tick += OnTick;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            Aborted += OnAborted;
            ProbePedBridge();
            Log("Interaction Runtime VI 0.2.1 story-safe contextual focus loaded.");
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!_focusDown || _target == null || !_target.Exists()) return;
            if (Game.GameTime - _focusStarted < _cfg.FocusHoldMs) return;
            if (Game.GameTime - _lastInteraction < _cfg.InteractionCooldownMs) return;
            if (e.KeyCode == _cfg.PositiveKey) Perform(0);
            else if (e.KeyCode == _cfg.ContextKey) Perform(1);
            else if (e.KeyCode == _cfg.NegativeKey) Perform(2);
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == _cfg.FocusKey) ResetFocus(false);
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_cfg.Enabled) return;
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead)
                {
                    ResetFocus(true);
                    return;
                }
                if (Game.GameTime - _lastBridgeProbe > 4000) ProbePedBridge();
                if (ShouldYield())
                {
                    ResetFocus(true);
                    return;
                }

                CleanupMemory();
                if (player.IsInVehicle())
                {
                    ResetFocus(true);
                    return;
                }

                // Passive camera acquisition happens before E is owned. If no stable
                // free-roam ped is under the camera, vanilla E remains untouched.
                UpdateTarget(player);
                SyncFocusInput(player);
                if (!_focusDown) return;
                if (_target == null || !_target.Exists()) { ResetFocus(true); return; }

                int held = Game.GameTime - _focusStarted;
                if (held >= _cfg.FocusHoldMs)
                {
                    SuppressConflictingControls();
                    if (_cfg.ShowControls) DrawInteractionHud(player, _target);
                    PollDirectionalControls();
                }

                if (held >= _cfg.LookAtAfterMs && Game.GameTime - _lastLookTask > 1500)
                {
                    _lastLookTask = Game.GameTime;
                    try { Function.Call(Hash.TASK_LOOK_AT_ENTITY, _target.Handle, player.Handle, 1900, 0, 2); } catch { }
                }
            }
            catch (Exception ex) { Log("Tick error: " + ex); }
        }

        private void SyncFocusInput(Ped player)
        {
            bool pressed = ReadControlPressed(InputContext);
            if (pressed && !_focusDown)
            {
                if (_target == null || !_target.Exists() || !CanOwnContext(player)) return;
                _focusDown = true;
                _focusStarted = Game.GameTime;
                _targetLostSince = 0;
            }
            else if (!pressed && _focusDown) ResetFocus(false);
        }

        private static bool CanOwnContext(Ped player)
        {
            if (player == null || !player.Exists() || player.IsInVehicle()) return false;
            try
            {
                int vehicle = Function.Call<int>(Hash.GET_VEHICLE_PED_IS_TRYING_TO_ENTER, player.Handle);
                if (vehicle != 0) return false;
            }
            catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle)) return false; } catch { }
            return true;
        }

        private void UpdateTarget(Ped player)
        {
            int now = Game.GameTime;
            if (now - _lastTargetScan < Math.Max(50, _cfg.TargetScanMs)) return;
            _lastTargetScan = now;
            Ped candidate = FindTarget(player);

            if (_target != null && _target.Exists() && IsTargetStillLocked(player, _target))
            {
                _targetLostSince = 0;
                _candidateHandle = 0;
                _candidateSince = 0;
                return;
            }

            if (_target != null && _target.Exists())
            {
                if (_targetLostSince == 0) _targetLostSince = now;
                if (_focusDown && now - _targetLostSince < Math.Max(150, _cfg.TargetLostGraceMs)) return;
            }

            if (candidate == null || !candidate.Exists())
            {
                if (!_focusDown || _targetLostSince == 0 || now - _targetLostSince >= Math.Max(150, _cfg.TargetLostGraceMs))
                    _target = null;
                _candidateHandle = 0;
                _candidateSince = 0;
                return;
            }

            if (_candidateHandle != candidate.Handle)
            {
                _candidateHandle = candidate.Handle;
                _candidateSince = now;
                return;
            }
            if (now - _candidateSince < Math.Max(0, _cfg.TargetAcquireStableMs)) return;

            bool changed = _target == null || !_target.Exists() || _target.Handle != candidate.Handle;
            _target = candidate;
            _targetLostSince = 0;
            _candidateHandle = 0;
            _candidateSince = 0;
            if (changed) _lastLookTask = 0;
        }

        private Ped FindTarget(Ped player)
        {
            Ped[] peds;
            try { peds = World.GetNearbyPeds(player, _cfg.MaxDistance); }
            catch { return null; }
            Vector3 camPos = GameplayCamera.Position;
            Vector3 camDir = GameplayCamera.Direction;
            Ped best = null;
            float bestScore = float.MinValue;
            foreach (Ped p in peds)
            {
                if (!UsableTarget(p, player)) continue;
                Vector3 delta = p.Position - camPos;
                float len = Length(delta);
                if (len < 0.1f) continue;
                float dot = Dot(camDir, delta) / len;
                if (dot < _cfg.AcquireConeDot) continue;
                bool los = false;
                try { los = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, player.Handle, p.Handle, 17); } catch { }
                if (!los) continue;
                float score = dot * 120f - len * 2.2f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        private bool IsTargetStillLocked(Ped player, Ped target)
        {
            if (!UsableTarget(target, player)) return false;
            Vector3 delta = target.Position - GameplayCamera.Position;
            float len = Length(delta);
            if (len < 0.1f || len > _cfg.MaxDistance + 1.0f) return false;
            float dot = Dot(GameplayCamera.Direction, delta) / len;
            if (dot < _cfg.ReleaseConeDot) return false;
            try { return Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, player.Handle, target.Handle, 17); }
            catch { return false; }
        }

        private bool UsableTarget(Ped p, Ped player)
        {
            if (p == null || !p.Exists() || p.Handle == player.Handle || p.IsDead || !p.IsHuman) return false;
            try { if (p.IsInVehicle()) return false; } catch { }
            if (_cfg.SkipMissionPeds)
            {
                try { if (Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, p.Handle)) return false; } catch { return false; }
            }
            try
            {
                int t = (int)p.PedType;
                if (t == 6 || t == 27 || t == 29) return false;
            }
            catch { }
            return true;
        }

        private static void SuppressConflictingControls()
        {
            try { Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, InputContext, true); } catch { }
        }

        private void PollDirectionalControls()
        {
            bool positive = ReadControlPressed(InputFrontendLeft);
            bool context = ReadControlPressed(InputFrontendUp);
            bool negative = ReadControlPressed(InputFrontendRight);
            if (positive && !_positiveControlDown) TryPerform(0);
            else if (context && !_contextControlDown) TryPerform(1);
            else if (negative && !_negativeControlDown) TryPerform(2);
            _positiveControlDown = positive;
            _contextControlDown = context;
            _negativeControlDown = negative;
        }

        private void TryPerform(int slot)
        {
            if (_target == null || !_target.Exists()) return;
            if (Game.GameTime - _focusStarted < _cfg.FocusHoldMs) return;
            if (Game.GameTime - _lastInteraction < _cfg.InteractionCooldownMs) return;
            Perform(slot);
        }

        private static bool ReadControlPressed(int control)
        {
            try { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, control) || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, control); }
            catch { return false; }
        }

        private void DrawInteractionHud(Ped player, Ped target)
        {
            float fear = GetFear(target);
            bool armed = false;
            try { armed = Function.Call<bool>(Hash.IS_PED_ARMED, player.Handle, 7); } catch { }
            string a = armed ? "Calmer" : "Saluer";
            string b = fear >= 45f ? "Laisser partir" : "Interpeller";
            string c = armed ? "Menacer" : "Provoquer";
            DrawText(0.785f, 0.720f, "^ " + b, 0.27f);
            DrawText(0.710f, 0.775f, a + " <", 0.27f);
            DrawText(0.855f, 0.775f, "> " + c, 0.27f);
        }

        private void Perform(int slot)
        {
            Ped player = Game.LocalPlayerPed;
            Ped target = _target;
            if (player == null || target == null || !player.Exists() || !target.Exists()) return;
            bool armed = false;
            try { armed = Function.Call<bool>(Hash.IS_PED_ARMED, player.Handle, 7); } catch { }
            string intent = slot == 0 ? (armed ? "calm" : "greet") : slot == 1 ? "context" : (armed ? "threaten" : "antagonize");
            _lastInteraction = Game.GameTime;
            Memory m = GetOrCreate(target);
            m.LastAt = Game.GameTime;
            m.Recognition = Math.Min(100f, m.Recognition + 10f);

            if (intent == "greet")
            {
                m.Opinion = Math.Min(100f, m.Opinion + 10f); m.Fear = Math.Max(0f, m.Fear - 3f);
                Speak(player, "GENERIC_HI"); RespondFriendly(target, m);
            }
            else if (intent == "calm")
            {
                m.Opinion = Math.Min(100f, m.Opinion + 4f); m.Fear = Math.Max(0f, m.Fear - 14f);
                Speak(player, "GENERIC_HI"); RespondCalm(target, m);
            }
            else if (intent == "context") { Speak(player, "GENERIC_HI"); RespondContext(target, m); }
            else if (intent == "antagonize")
            {
                m.Opinion = Math.Max(-100f, m.Opinion - 20f); m.Recognition = Math.Min(100f, m.Recognition + 15f);
                Speak(player, "GENERIC_INSULT_HIGH"); RespondAntagonize(target, m, false);
            }
            else
            {
                m.Opinion = Math.Max(-100f, m.Opinion - 35f); m.Fear = Math.Min(100f, m.Fear + 38f); m.Recognition = Math.Min(100f, m.Recognition + 30f);
                Speak(player, "GENERIC_INSULT_HIGH"); RespondAntagonize(target, m, true);
            }
            SendToPedBridge(target, intent, 1f);
            Log("Interaction ped=" + target.Handle + " intent=" + intent + ".");
        }

        private void RespondFriendly(Ped target, Memory m)
        {
            int roll = Roll(target, 11);
            if (m.Opinion < -30f) { Speak(target, "GENERIC_INSULT_HIGH"); return; }
            if (roll < 72) { Speak(target, "GENERIC_HI"); try { Function.Call(Hash.TASK_LOOK_AT_ENTITY, target.Handle, Game.LocalPlayerPed.Handle, 1800, 0, 2); } catch { } }
            else Speak(target, "GENERIC_NO");
        }

        private void RespondCalm(Ped target, Memory m)
        {
            if (m.Fear > 55f) { Speak(target, "GENERIC_FRIGHTENED_HIGH"); DiscreetLeave(target, Game.LocalPlayerPed); }
            else { Speak(target, "GENERIC_THANKS"); try { Function.Call(Hash.TASK_LOOK_AT_ENTITY, target.Handle, Game.LocalPlayerPed.Handle, 1300, 0, 2); } catch { } }
        }

        private void RespondContext(Ped target, Memory m)
        {
            if (m.Fear > 45f) { Speak(target, "GENERIC_FRIGHTENED_HIGH"); DiscreetLeave(target, Game.LocalPlayerPed); }
            else if (m.Opinion < -25f) Speak(target, "GENERIC_INSULT_HIGH");
            else Speak(target, "GENERIC_HI");
        }

        private void RespondAntagonize(Ped target, Memory m, bool armedThreat)
        {
            int bravery = Roll(target, 37);
            if (armedThreat && m.Fear >= 45f)
            {
                Speak(target, "GENERIC_FRIGHTENED_HIGH");
                if (bravery < 60 && Distance(target, Game.LocalPlayerPed) < 8f)
                { try { Function.Call(Hash.TASK_HANDS_UP, target.Handle, 3500, Game.LocalPlayerPed.Handle, -1, false); } catch { } }
                else DiscreetLeave(target, Game.LocalPlayerPed);
                return;
            }
            if (bravery > 68) { Speak(target, "GENERIC_INSULT_HIGH"); try { Function.Call(Hash.TASK_LOOK_AT_ENTITY, target.Handle, Game.LocalPlayerPed.Handle, 2200, 0, 2); } catch { } }
            else { Speak(target, "GENERIC_SHOCKED_HIGH"); DiscreetLeave(target, Game.LocalPlayerPed); }
        }

        private static void DiscreetLeave(Ped ped, Ped player)
        {
            if (ped == null || player == null || !ped.Exists() || !player.Exists()) return;
            Vector3 d = ped.Position - player.Position;
            float len = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len < 0.1f) len = 1f;
            Vector3 target = ped.Position + new Vector3(d.X / len, d.Y / len, 0f) * 18f;
            try { Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle, target.X, target.Y, target.Z, 1.05f, 8000, 1.2f, 0, 0f); } catch { }
        }

        private static void Speak(Ped ped, string speech)
        {
            if (ped == null || !ped.Exists()) return;
            try { Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle, speech, "SPEECH_PARAMS_FORCE"); } catch { }
        }

        private void ProbePedBridge()
        {
            _lastBridgeProbe = Game.GameTime;
            _pedBridgeRegister = _pedBridgeFear = _pedBridgeOpinion = null;
            if (!_cfg.BridgeToPedOverhaul) return;
            try
            {
                Assembly a = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => string.Equals(x.GetName().Name, "PedOverhaulVI", StringComparison.OrdinalIgnoreCase));
                Type t = a == null ? null : a.GetType("VOX.PedOverhaulVI.PedOverhaulVIBridge", false);
                if (t == null) return;
                _pedBridgeRegister = t.GetMethod("RegisterPlayerInteraction", BindingFlags.Public | BindingFlags.Static);
                _pedBridgeFear = t.GetMethod("GetFearAssociation", BindingFlags.Public | BindingFlags.Static);
                _pedBridgeOpinion = t.GetMethod("GetOpinion", BindingFlags.Public | BindingFlags.Static);
            }
            catch { }
        }

        private void SendToPedBridge(Ped target, string intent, float intensity)
        {
            if (_pedBridgeRegister == null) return;
            try { _pedBridgeRegister.Invoke(null, new object[] { target.Handle, target.Model.Hash, intent, intensity }); } catch { }
        }

        private float GetFear(Ped target)
        {
            float local = GetOrCreate(target).Fear;
            try { if (_pedBridgeFear != null) local = Math.Max(local, Convert.ToSingle(_pedBridgeFear.Invoke(null, new object[] { target.Handle }))); } catch { }
            return local;
        }

        private Memory GetOrCreate(Ped p)
        {
            Memory m;
            if (!_memory.TryGetValue(p.Handle, out m) || m.ModelHash != p.Model.Hash)
            { m = new Memory { ModelHash = p.Model.Hash }; _memory[p.Handle] = m; }
            return m;
        }

        private void CleanupMemory()
        {
            int cutoff = Game.GameTime - Math.Max(1, _cfg.LocalMemoryMinutes) * 60000;
            var dead = _memory.Where(x => x.Value.LastAt > 0 && x.Value.LastAt < cutoff).Select(x => x.Key).Take(8).ToList();
            foreach (int h in dead) _memory.Remove(h);
        }

        private bool ShouldYield()
        {
            bool storyOwns = false;
            try { storyOwns |= Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE); } catch { }
            try { storyOwns |= Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS); } catch { }
            try { storyOwns |= !Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle); } catch { }
            try { storyOwns |= Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN); } catch { }
            if (_cfg.DisableDuringMissions) { try { storyOwns |= Function.Call<bool>(Hash.GET_MISSION_FLAG); } catch { } }
            if (storyOwns) { _storyYieldUntil = Game.GameTime + 5000; return true; }
            if (Game.GameTime < _storyYieldUntil) return true;
            if (_cfg.DisableWhileWanted) { try { if (Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle) > 0) return true; } catch { } }
            return false;
        }

        private void ResetFocus(bool clearTarget)
        {
            _focusDown = false;
            _focusStarted = 0;
            _targetLostSince = 0;
            _positiveControlDown = _contextControlDown = _negativeControlDown = false;
            if (clearTarget)
            {
                _target = null;
                _candidateHandle = 0;
                _candidateSince = 0;
            }
        }

        private void OnAborted(object sender, EventArgs e) { ResetFocus(true); _memory.Clear(); }
        private static int Roll(Ped p, int salt)
        {
            unchecked { int x = p.Handle * 1103515245 + p.Model.Hash * 97 + salt * 7919; x ^= x >> 16; if (x < 0) x = -x; return x % 100; }
        }
        private static float Distance(Ped a, Ped b) { return Length(a.Position - b.Position); }
        private static float Dot(Vector3 a, Vector3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        private static float Length(Vector3 v) { return (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z); }

        private static void DrawText(float x, float y, string text, float scale)
        {
            try
            {
                Function.Call(Hash.SET_TEXT_FONT, 0); Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
                Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, 230); Function.Call(Hash.SET_TEXT_OUTLINE);
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING"); Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y, 0);
            }
            catch { }
        }

        private void Log(string s)
        {
            if (!_cfg.DebugLogging) return;
            try { Directory.CreateDirectory(DataDirectory); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + s + Environment.NewLine); }
            catch { }
        }

        private sealed class Memory
        {
            public int ModelHash; public float Opinion; public float Fear; public float Recognition; public int LastAt;
        }

        private sealed class Config
        {
            public bool Enabled = true, DebugLogging = true, DisableDuringMissions = true, SkipMissionPeds = true, DisableWhileWanted = true, BridgeToPedOverhaul = true, ShowControls = true;
            public int TickIntervalMs = 25, FocusHoldMs = 450, LookAtAfterMs = 650, InteractionCooldownMs = 900, LocalMemoryMinutes = 10, TargetScanMs = 100, TargetAcquireStableMs = 180, TargetLostGraceMs = 650;
            public float MaxDistance = 8f, AcquireConeDot = 0.88f, ReleaseConeDot = 0.80f;
            public Keys FocusKey = Keys.E, PositiveKey = Keys.Left, ContextKey = Keys.Up, NegativeKey = Keys.Right;

            public static Config Load(string path)
            {
                var c = new Config();
                if (!File.Exists(path)) return c;
                string section = string.Empty;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim(); if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                    int eq = line.IndexOf('='); if (eq <= 0) continue;
                    string full = section + "." + line.Substring(0, eq).Trim(); string value = line.Substring(eq + 1).Trim();
                    switch (full)
                    {
                        case "General.Enabled": c.Enabled = B(value, c.Enabled); break;
                        case "General.DebugLogging": c.DebugLogging = B(value, c.DebugLogging); break;
                        case "General.DisableDuringMissions": c.DisableDuringMissions = B(value, c.DisableDuringMissions); break;
                        case "General.SkipMissionPeds": c.SkipMissionPeds = B(value, c.SkipMissionPeds); break;
                        case "General.DisableWhileWanted": c.DisableWhileWanted = B(value, c.DisableWhileWanted); break;
                        case "General.TickIntervalMs": c.TickIntervalMs = I(value, c.TickIntervalMs); break;
                        case "Focus.FocusKey": c.FocusKey = K(value, c.FocusKey); break;
                        case "Focus.PositiveKey": c.PositiveKey = K(value, c.PositiveKey); break;
                        case "Focus.ContextKey": c.ContextKey = K(value, c.ContextKey); break;
                        case "Focus.NegativeKey": c.NegativeKey = K(value, c.NegativeKey); break;
                        case "Focus.MaxDistance": c.MaxDistance = F(value, c.MaxDistance); break;
                        case "Focus.AcquireConeDot": c.AcquireConeDot = F(value, c.AcquireConeDot); break;
                        case "Focus.ReleaseConeDot": c.ReleaseConeDot = F(value, c.ReleaseConeDot); break;
                        case "Focus.FocusHoldMs": c.FocusHoldMs = I(value, c.FocusHoldMs); break;
                        case "Focus.LookAtAfterMs": c.LookAtAfterMs = I(value, c.LookAtAfterMs); break;
                        case "Focus.InteractionCooldownMs": c.InteractionCooldownMs = I(value, c.InteractionCooldownMs); break;
                        case "Focus.TargetScanMs": c.TargetScanMs = I(value, c.TargetScanMs); break;
                        case "Focus.TargetAcquireStableMs": c.TargetAcquireStableMs = I(value, c.TargetAcquireStableMs); break;
                        case "Focus.TargetLostGraceMs": c.TargetLostGraceMs = I(value, c.TargetLostGraceMs); break;
                        case "Memory.LocalMemoryMinutes": c.LocalMemoryMinutes = I(value, c.LocalMemoryMinutes); break;
                        case "Memory.BridgeToPedOverhaul": c.BridgeToPedOverhaul = B(value, c.BridgeToPedOverhaul); break;
                        case "HUD.ShowControls": c.ShowControls = B(value, c.ShowControls); break;
                    }
                }
                c.ReleaseConeDot = Math.Min(c.AcquireConeDot, c.ReleaseConeDot); return c;
            }
            private static bool B(string s, bool d) { bool v; return bool.TryParse(s, out v) ? v : d; }
            private static int I(string s, int d) { int v; return int.TryParse(s, out v) ? v : d; }
            private static float F(string s, float d) { float v; return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : d; }
            private static Keys K(string s, Keys d) { Keys v; return Enum.TryParse(s, true, out v) ? v : d; }
        }
    }
}
