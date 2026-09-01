using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace VOX.InteriorRuntimeVI
{
    public static class InteriorRuntimeVIBridge
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, bool> Circuits = new Dictionary<string, bool>();
        internal static string PersistencePath = "scripts\\InteriorRuntimeVI\\Circuits.txt";
        internal static bool Dirty;

        public static bool IsCircuitPowered(int interiorId, int roomKey)
        {
            string key = Key(interiorId, roomKey);
            lock (Sync)
            {
                bool powered;
                return !Circuits.TryGetValue(key, out powered) || powered;
            }
        }

        public static void SetCircuitPowered(int interiorId, int roomKey, bool powered)
        {
            if (interiorId == 0) return;
            string key = Key(interiorId, roomKey);
            lock (Sync)
            {
                bool existing;
                if (Circuits.TryGetValue(key, out existing) && existing == powered) return;
                Circuits[key] = powered;
                Dirty = true;
            }
        }

        public static string CircuitKeyForEntity(int entityHandle)
        {
            try
            {
                int interior = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, entityHandle);
                int room = Function.Call<int>(Hash.GET_ROOM_KEY_FROM_ENTITY, entityHandle);
                return Key(interior, room);
            }
            catch { return "0:0"; }
        }

        internal static void Load()
        {
            lock (Sync)
            {
                Circuits.Clear();
                if (!File.Exists(PersistencePath)) return;
                try
                {
                    foreach (string raw in File.ReadAllLines(PersistencePath))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        int split = line.LastIndexOf('=');
                        if (split <= 0) continue;
                        string key = line.Substring(0, split).Trim();
                        bool value;
                        if (bool.TryParse(line.Substring(split + 1).Trim(), out value)) Circuits[key] = value;
                    }
                }
                catch { }
                Dirty = false;
            }
        }

        internal static void Save()
        {
            lock (Sync)
            {
                if (!Dirty) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(PersistencePath));
                    var lines = new List<string> { "# InteriorRuntimeVI room/circuit states" };
                    foreach (var pair in Circuits) lines.Add(pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture));
                    File.WriteAllLines(PersistencePath, lines.ToArray());
                    Dirty = false;
                }
                catch { }
            }
        }

        private static string Key(int interior, int room) { return interior.ToString(CultureInfo.InvariantCulture) + ":" + room.ToString(CultureInfo.InvariantCulture); }
    }

    public sealed class InteriorRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\InteriorRuntimeVI";
        private const string LogPath = DataDir + "\\InteriorRuntimeVI.log";
        private const int InputContext = 51; // E / D-pad Right
        private const string HandleDict = "anim@heists@keycard@";
        private const string HandleAnim = "exit";
        private static readonly Hash DoorFindNative = (Hash)0x589F80B325CC82C5UL;
        private static readonly Hash DoorStateNative = (Hash)0x160AA1B32F6139B8UL;

        private sealed class DoorCandidate
        {
            public int PropHandle;
            public int DoorHash;
            public int State;
            public Vector3 Position;
        }

        private DoorCandidate _door;
        private int _lastScan;
        private bool _contextWasDown;
        private int _attemptStarted;
        private int _attemptProp;
        private int _storyYieldUntil;
        private int _lastSave;
        private MethodInfo _tryClaim;
        private MethodInfo _releaseClaim;
        private int _nextBridgeProbe;

        public InteriorRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            InteriorRuntimeVIBridge.Load();
            Interval = 20;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Interior Runtime VI 0.1.0 loaded: generic DoorSystem discovery, physical locked-handle tests and persistent room-circuit foundation.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            Ped player = Game.LocalPlayerPed;
            if (player == null || !player.Exists() || player.IsDead)
            {
                CancelAttempt(player);
                return;
            }

            if (RockstarOwnsScene())
            {
                _storyYieldUntil = Game.GameTime + 5000;
                CancelAttempt(player);
                _door = null;
                return;
            }
            if (Game.GameTime < _storyYieldUntil)
            {
                CancelAttempt(player);
                _door = null;
                return;
            }

            if (Game.GameTime - _lastSave > 10000 && InteriorRuntimeVIBridge.Dirty)
            {
                _lastSave = Game.GameTime;
                InteriorRuntimeVIBridge.Save();
            }

            if (player.IsInVehicle() || IsAiming())
            {
                CancelAttempt(player);
                _door = null;
                TrackContext(false);
                return;
            }

            if (_attemptStarted > 0)
            {
                UpdateHandleAttempt(player);
                TrackContext(Pressed(InputContext));
                return;
            }

            if (Game.GameTime - _lastScan >= 120)
            {
                _lastScan = Game.GameTime;
                _door = FindClosestDoor(player);
            }

            bool context = Pressed(InputContext);
            bool contextJust = context && !_contextWasDown;
            _contextWasDown = context;
            if (!contextJust || _door == null || !IsLockedState(_door.State)) return;

            if (!TryClaim(InputContext, 65, 1200, "locked-door-handle-test")) return;
            _attemptStarted = Game.GameTime;
            _attemptProp = _door.PropHandle;
            RequestAnim();
            AlignToDoor(player, _door.Position);
            Log("Locked door handle-test started prop=" + _door.PropHandle + " doorHash=" + _door.DoorHash + " state=" + _door.State + ".");
        }

        private DoorCandidate FindClosestDoor(Ped player)
        {
            Prop[] props;
            try { props = World.GetNearbyProps(player.Position, 3.0f); }
            catch { return null; }
            DoorCandidate best = null;
            float bestScore = float.MaxValue;
            Vector3 forward = Forward(player);

            foreach (Prop prop in props)
            {
                if (prop == null || !prop.Exists()) continue;
                try { if (Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, prop.Handle)) continue; } catch { continue; }
                Vector3 p = prop.Position;
                Vector3 delta = p - player.Position;
                float d = Length(delta);
                if (d < 0.25f || d > 2.15f) continue;
                float dot = (forward.X * delta.X + forward.Y * delta.Y) / Math.Max(0.01f, (float)Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y));
                if (dot < 0.18f) continue;

                int doorHash;
                if (!TryFindDoorSystem(prop, out doorHash)) continue;
                int state = DoorState(doorHash);
                if (!IsLockedState(state)) continue;
                float score = d - dot * 0.35f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new DoorCandidate { PropHandle = prop.Handle, DoorHash = doorHash, State = state, Position = p };
                }
            }
            return best;
        }

        private static bool TryFindDoorSystem(Prop prop, out int doorHash)
        {
            doorHash = 0;
            if (prop == null || !prop.Exists()) return false;
            try
            {
                Vector3 p = prop.Position;
                var output = new OutputArgument();
                bool found = Function.Call<bool>(DoorFindNative, p.X, p.Y, p.Z, prop.Model.Hash, output);
                if (!found) return false;
                doorHash = output.GetResult<int>();
                return doorHash != 0;
            }
            catch { return false; }
        }

        private static int DoorState(int doorHash)
        {
            try { return Function.Call<int>(DoorStateNative, doorHash); }
            catch { return 0; }
        }

        private void UpdateHandleAttempt(Ped player)
        {
            Prop prop = null;
            try { prop = Entity.FromHandle(_attemptProp) as Prop; } catch { }
            if (prop == null || !prop.Exists() || Distance(player.Position, prop.Position) > 2.6f)
            {
                CancelAttempt(player);
                return;
            }

            DisableControl(InputContext);
            TryClaim(InputContext, 65, 250, "locked-door-handle-test");
            Vector3 door = prop.Position;
            int elapsed = Game.GameTime - _attemptStarted;
            if (elapsed < 340)
            {
                AlignToDoor(player, door);
                return;
            }

            RequestAnim();
            try
            {
                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, HandleDict) && !Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, player.Handle, HandleDict, HandleAnim, 3))
                    Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, HandleDict, HandleAnim, 4.5f, -3.5f, 780, 16, 0.55f, false, false, false);
            }
            catch { }

            if (elapsed >= 1050)
            {
                int doorHash;
                int state = TryFindDoorSystem(prop, out doorHash) ? DoorState(doorHash) : 0;
                Log("Locked door handle-test completed stateStill=" + state + ". Door state was not modified.");
                CancelAttempt(player);
            }
        }

        private static void AlignToDoor(Ped player, Vector3 door)
        {
            Vector3 away = player.Position - door;
            float len = (float)Math.Sqrt(away.X * away.X + away.Y * away.Y);
            if (len < 0.05f) len = 1f;
            Vector3 stand = new Vector3(door.X + away.X / len * 0.62f, door.Y + away.Y / len * 0.62f, player.Position.Z);
            float heading = HeadingTo(stand, door);
            try
            {
                if (Distance(player.Position, stand) > 0.28f)
                    Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, player.Handle, stand.X, stand.Y, stand.Z, 0.75f, 450, heading, 0.04f);
                else Function.Call(Hash.TASK_TURN_PED_TO_FACE_COORD, player.Handle, door.X, door.Y, door.Z, 220);
            }
            catch { }
        }

        private void CancelAttempt(Ped player)
        {
            if (_attemptStarted <= 0)
            {
                ReleaseClaim(InputContext);
                return;
            }
            if (player != null && player.Exists())
            {
                try { Function.Call(Hash.STOP_ANIM_TASK, player.Handle, HandleDict, HandleAnim, 2.0f); } catch { }
            }
            _attemptStarted = 0;
            _attemptProp = 0;
            ReleaseClaim(InputContext);
        }

        private bool TryClaim(int control, int priority, int ttlMs, string context)
        {
            ResolveControlBridge();
            if (_tryClaim == null) return true;
            try { return (bool)_tryClaim.Invoke(null, new object[] { "InteriorRuntimeVI", control, priority, ttlMs, context }); }
            catch { _tryClaim = null; return true; }
        }

        private void ReleaseClaim(int control)
        {
            ResolveControlBridge();
            if (_releaseClaim == null) return;
            try { _releaseClaim.Invoke(null, new object[] { "InteriorRuntimeVI", control }); }
            catch { _releaseClaim = null; }
        }

        private void ResolveControlBridge()
        {
            if (_tryClaim != null && _releaseClaim != null) return;
            if (Environment.TickCount < _nextBridgeProbe) return;
            _nextBridgeProbe = Environment.TickCount + 5000;
            try
            {
                Type type = Type.GetType("VOX.CoreVI.ControlOwnershipBridge, VOXCoreVI", false);
                if (type == null) return;
                _tryClaim = type.GetMethod("TryClaim", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(string) }, null);
                _releaseClaim = type.GetMethod("Release", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(int) }, null);
            }
            catch { _tryClaim = null; _releaseClaim = null; }
        }

        private static bool IsLockedState(int state) { return state == 1 || state == 2 || state == 4; }
        private static void RequestAnim() { try { Function.Call(Hash.REQUEST_ANIM_DICT, HandleDict); } catch { } }
        private static bool Pressed(int control) { try { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, control) || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, control); } catch { return false; } }
        private static void DisableControl(int control) { try { Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, control, true); } catch { } }
        private static bool IsAiming() { try { return Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle); } catch { return false; } }
        private void TrackContext(bool down) { _contextWasDown = down; }

        private static Vector3 Forward(Entity e)
        {
            try { return Function.Call<Vector3>(Hash.GET_ENTITY_FORWARD_VECTOR, e.Handle); }
            catch { return new Vector3(0f, 1f, 0f); }
        }
        private static float HeadingTo(Vector3 from, Vector3 to) { try { return Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D, to.X - from.X, to.Y - from.Y); } catch { return 0f; } }
        private static float Length(Vector3 v) { return (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z); }
        private static float Distance(Vector3 a, Vector3 b) { double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z; return (float)Math.Sqrt(x * x + y * y + z * z); }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true; } catch { }
            return false;
        }

        private void OnAborted(object sender, EventArgs e)
        {
            CancelAttempt(Game.LocalPlayerPed);
            InteriorRuntimeVIBridge.Save();
        }

        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); } catch { }
        }
    }
}
