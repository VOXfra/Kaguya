using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace VOX.AcousticRuntimeVI
{
    public sealed class AcousticEventSnapshot
    {
        public int Sequence;
        public string Type = string.Empty;
        public float X;
        public float Y;
        public float Z;
        public float BaseEnergy;
        public float RoomPressure;
        public bool Suppressed;
        public int Interior;
        public int Room;
        public int CreatedAt;
    }

    public static class AcousticRuntimeVIBridge
    {
        private static readonly object Sync = new object();
        private static AcousticEventSnapshot _latest;
        private static int _sequence;

        internal static AcousticEventSnapshot Publish(string type, Vector3 p, float baseEnergy, float roomPressure, bool suppressed, int interior, int room)
        {
            lock (Sync)
            {
                _sequence++;
                _latest = new AcousticEventSnapshot
                {
                    Sequence = _sequence,
                    Type = type ?? string.Empty,
                    X = p.X,
                    Y = p.Y,
                    Z = p.Z,
                    BaseEnergy = baseEnergy,
                    RoomPressure = roomPressure,
                    Suppressed = suppressed,
                    Interior = interior,
                    Room = room,
                    CreatedAt = Game.GameTime
                };
                return _latest;
            }
        }

        public static AcousticEventSnapshot Latest()
        {
            lock (Sync)
            {
                if (_latest == null) return null;
                return new AcousticEventSnapshot
                {
                    Sequence = _latest.Sequence,
                    Type = _latest.Type,
                    X = _latest.X,
                    Y = _latest.Y,
                    Z = _latest.Z,
                    BaseEnergy = _latest.BaseEnergy,
                    RoomPressure = _latest.RoomPressure,
                    Suppressed = _latest.Suppressed,
                    Interior = _latest.Interior,
                    Room = _latest.Room,
                    CreatedAt = _latest.CreatedAt
                };
            }
        }

        public static float EstimatePerceivedEnergy(int listenerHandle, float sourceX, float sourceY, float sourceZ, float baseEnergy)
        {
            try
            {
                Entity entity = Entity.FromHandle(listenerHandle);
                if (entity == null || !entity.Exists()) return 0f;
                return AcousticMath.Estimate(entity, new Vector3(sourceX, sourceY, sourceZ), baseEnergy);
            }
            catch { return 0f; }
        }
    }

    internal static class AcousticMath
    {
        public static float Estimate(Entity listener, Vector3 source, float baseEnergy)
        {
            if (listener == null || !listener.Exists()) return 0f;
            Vector3 lp = listener.Position;
            float distance = Distance(lp, source);
            float energy = Math.Max(0f, baseEnergy) / (1f + distance * 0.055f + distance * distance * 0.00125f);

            bool line = false;
            try
            {
                // Source is usually the player, but a source coordinate is more
                // useful for future explosions/fire. A ray probe lets the same
                // attenuation model work without magical listener knowledge.
                int test = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                    source.X, source.Y, source.Z + 0.15f,
                    lp.X, lp.Y, lp.Z + 0.75f,
                    1 | 16 | 256, 0, 7);
                var hit = new OutputArgument();
                var end = new OutputArgument();
                var normal = new OutputArgument();
                var entity = new OutputArgument();
                Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT, test, hit, end, normal, entity);
                line = !hit.GetResult<bool>();
            }
            catch { }
            if (!line) energy *= 0.46f;

            int listenerInterior = Interior(listener);
            int listenerRoom = Room(listener);
            int playerInterior = 0;
            int playerRoom = 0;
            Ped player = Game.LocalPlayerPed;
            if (player != null && player.Exists())
            {
                playerInterior = Interior(player);
                playerRoom = Room(player);
            }

            if (listenerInterior != 0 || playerInterior != 0)
            {
                if (listenerInterior == playerInterior && listenerInterior != 0)
                {
                    if (listenerRoom != 0 && playerRoom != 0 && listenerRoom != playerRoom) energy *= 0.61f;
                    else energy *= 1.08f;
                }
                else energy *= 0.38f;
            }
            return Math.Max(0f, energy);
        }

        public static int Interior(Entity e)
        {
            try { return Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, e.Handle); } catch { return 0; }
        }

        public static int Room(Entity e)
        {
            try { return Function.Call<int>(Hash.GET_ROOM_KEY_FROM_ENTITY, e.Handle); } catch { return 0; }
        }

        public static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
    }

    public sealed class AcousticRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\AcousticRuntimeVI";
        private const string LogPath = DataDir + "\\AcousticRuntimeVI.log";
        private int _lastShot;
        private int _lastScan;
        private int _storyYieldUntil;
        private int _lastPublishedSequence;
        private MethodInfo _corePublish;
        private int _nextCoreProbe;

        private readonly HashSet<int> _recentListeners = new HashSet<int>();

        public AcousticRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Interval = 30;
            Tick += OnTick;
            Log("Acoustic Runtime VI 0.1.0 loaded: room/portal-aware gunshot pressure model and occluded NPC hearing.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            Ped player = Game.LocalPlayerPed;
            if (player == null || !player.Exists() || player.IsDead) return;
            if (RockstarOwnsScene())
            {
                _storyYieldUntil = Game.GameTime + 5000;
                return;
            }
            if (Game.GameTime < _storyYieldUntil) return;

            bool shooting = false;
            try { shooting = Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle); } catch { }
            if (!shooting) return;

            int now = Game.GameTime;
            if (now - _lastShot < 95) return;
            _lastShot = now;

            int weapon = 0;
            try { weapon = Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON, player.Handle); } catch { }
            bool suppressed = HasSuppressor(player, weapon);
            float baseEnergy = BaseWeaponEnergy(weapon) * (suppressed ? 0.46f : 1f);
            int interior = AcousticMath.Interior(player);
            int room = AcousticMath.Room(player);
            float roomPressure = interior != 0 ? (suppressed ? 1.08f : 1.32f) : 0.88f;
            Vector3 source = player.Position + new Vector3(0f, 0f, 0.85f);
            AcousticEventSnapshot evt = AcousticRuntimeVIBridge.Publish("gunshot", source, baseEnergy, roomPressure, suppressed, interior, room);
            _lastPublishedSequence = evt.Sequence;
            PublishToCore(evt);
            PropagateToPeds(player, evt);
        }

        private void PropagateToPeds(Ped player, AcousticEventSnapshot evt)
        {
            if (Game.GameTime - _lastScan < 80) return;
            _lastScan = Game.GameTime;
            _recentListeners.Clear();
            Ped[] nearby;
            try { nearby = World.GetNearbyPeds(player, evt.Suppressed ? 90f : 170f); }
            catch { return; }

            Vector3 source = new Vector3(evt.X, evt.Y, evt.Z);
            foreach (Ped ped in nearby)
            {
                if (!EligibleListener(ped, player)) continue;
                if (!_recentListeners.Add(ped.Handle)) continue;
                float perceived = AcousticMath.Estimate(ped, source, evt.BaseEnergy * evt.RoomPressure);
                if (perceived < 0.78f) continue;

                float d = AcousticMath.Distance(ped.Position, source);
                bool combat = false;
                try { combat = Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped.Handle, player.Handle); } catch { }
                if (combat) continue;

                try
                {
                    if (perceived >= 2.6f && d < 48f)
                    {
                        // Strong nearby report: the listener only flees from the
                        // source coordinate it could acoustically infer, not from a
                        // magically identified player entity.
                        Function.Call(Hash.TASK_SMART_FLEE_COORD, ped.Handle, source.X, source.Y, source.Z, 45f, 5500, false, false);
                    }
                    else
                    {
                        Function.Call(Hash.TASK_TURN_PED_TO_FACE_COORD, ped.Handle, source.X, source.Y, source.Z, 900);
                    }
                }
                catch { }
            }
        }

        private void PublishToCore(AcousticEventSnapshot evt)
        {
            try
            {
                if (_corePublish == null && Environment.TickCount >= _nextCoreProbe)
                {
                    _nextCoreProbe = Environment.TickCount + 5000;
                    Type t = Type.GetType("VOX.CoreVI.WorldMemoryBridge, VOXCoreVI", false);
                    if (t != null)
                        _corePublish = t.GetMethod("Publish", BindingFlags.Public | BindingFlags.Static, null,
                            new[] { typeof(string), typeof(string), typeof(float), typeof(float), typeof(float), typeof(int), typeof(int), typeof(string), typeof(double), typeof(string) }, null);
                }
                if (_corePublish == null) return;
                int suspect = 0;
                Ped player = Game.LocalPlayerPed;
                if (player != null && player.Exists()) suspect = player.Model.Hash;
                _corePublish.Invoke(null, new object[]
                {
                    "acoustic", evt.Type, evt.X, evt.Y, evt.Z,
                    evt.Suppressed ? 1 : 2, suspect, "AcousticRuntimeVI", 0.05,
                    "energy=" + evt.BaseEnergy.ToString("0.00") + ";pressure=" + evt.RoomPressure.ToString("0.00") + ";interior=" + evt.Interior + ";room=" + evt.Room + ";suppressed=" + evt.Suppressed
                });
            }
            catch
            {
                _corePublish = null;
                _nextCoreProbe = Environment.TickCount + 10000;
            }
        }

        private static bool EligibleListener(Ped ped, Ped player)
        {
            if (ped == null || !ped.Exists() || ped.IsDead || !ped.IsHuman || ped.Handle == player.Handle) return false;
            try { if (Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, ped.Handle)) return false; } catch { return false; }
            try
            {
                int t = (int)ped.PedType;
                if (t == 6 || t == 27 || t == 29) return false;
            }
            catch { }
            return true;
        }

        private static float BaseWeaponEnergy(int weapon)
        {
            string[] quiet = { "WEAPON_STUNGUN", "WEAPON_SNOWBALL", "WEAPON_BZGAS" };
            foreach (string name in quiet) if (weapon == SafeHash(name)) return 0.8f;
            string[] heavy = { "WEAPON_PUMPSHOTGUN", "WEAPON_HEAVYSHOTGUN", "WEAPON_SNIPERRIFLE", "WEAPON_HEAVYSNIPER", "WEAPON_MUSKET" };
            foreach (string name in heavy) if (weapon == SafeHash(name)) return 8.2f;
            string[] rifles = { "WEAPON_ASSAULTRIFLE", "WEAPON_CARBINERIFLE", "WEAPON_SPECIALCARBINE", "WEAPON_BULLPUPRIFLE", "WEAPON_COMPACTRIFLE" };
            foreach (string name in rifles) if (weapon == SafeHash(name)) return 6.3f;
            string[] smg = { "WEAPON_SMG", "WEAPON_MICROSMG", "WEAPON_ASSAULTSMG", "WEAPON_COMBATPDW", "WEAPON_MACHINEPISTOL" };
            foreach (string name in smg) if (weapon == SafeHash(name)) return 4.7f;
            return 4.1f;
        }

        private static bool HasSuppressor(Ped player, int weapon)
        {
            string[] components =
            {
                "COMPONENT_AT_PI_SUPP", "COMPONENT_AT_PI_SUPP_02",
                "COMPONENT_AT_AR_SUPP", "COMPONENT_AT_AR_SUPP_02",
                "COMPONENT_AT_SR_SUPP", "COMPONENT_AT_SR_SUPP_03"
            };
            foreach (string component in components)
            {
                int hash = SafeHash(component);
                try { if (hash != 0 && Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON_COMPONENT, player.Handle, weapon, hash)) return true; } catch { }
            }
            return false;
        }

        private static int SafeHash(string name) { try { return Function.Call<int>(Hash.GET_HASH_KEY, name); } catch { return 0; } }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true; } catch { }
            return false;
        }

        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); } catch { }
        }
    }
}
