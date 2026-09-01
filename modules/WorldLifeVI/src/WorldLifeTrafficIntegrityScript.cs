using GTA;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;

namespace VOX.WorldLifeVI
{
    public sealed class WorldLifeVITrafficIntegrityScript : Script
    {
        private const string LogPath = "scripts\\WorldLifeVI\\WorldLifeVI.log";
        private readonly HashSet<int> _onlineHashes = new HashSet<int>();
        private readonly string[] _onlineModels =
        {
            "brioso2","club","issi7","weevil","kanjosj","tailgater2","deity","cinquemila","rhinehart","schafter5",
            "astron","iwagen","jubilee","baller7","toros","rebla","novak","granger2","kanjo","postlude","previon",
            "windsor2","zion3","gauntlet3","gauntlet4","gauntlet5","dominator3","dominator7","dominator8","buffalo4",
            "vigero2","tulip2","comet3","comet5","comet6","comet7","fagaloa","retinue","retinue2","savestra",
            "jester3","jester4","euros","remus","zr350","calico","growler","vectre","cypher","komoda","jugular",
            "drafter","neo","paragon","krieger","emerus","thrax","zorrusso","tigon","italirsx","caracara2","everon",
            "hellion","kamacho","draugur","boor"
        };
        private readonly string[] _civilianModels = { "a_m_y_business_01", "a_m_y_stbla_02", "a_m_m_genfat_01", "a_f_y_business_02", "a_f_y_hipster_01", "a_m_y_hipster_02" };
        private int _lastScan;
        private int _storyYieldUntil;

        public WorldLifeVITrafficIntegrityScript()
        {
            foreach (string n in _onlineModels) _onlineHashes.Add(SafeHash(n));
            Interval = 250;
            Tick += OnTick;
            Log("World Life VI traffic-integrity 0.3.1 loaded: moving Online replacements may never remain driverless.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            Ped player = Game.LocalPlayerPed;
            if (player == null || !player.Exists() || player.IsDead) return;
            if (StoryOwnsScene()) { _storyYieldUntil = Game.GameTime + 5000; return; }
            if (Game.GameTime < _storyYieldUntil) return;
            if (Game.GameTime - _lastScan < 1200) return;
            _lastScan = Game.GameTime;

            Vehicle[] vehicles;
            try { vehicles = World.GetNearbyVehicles(player, 145f); } catch { return; }
            foreach (Vehicle v in vehicles)
            {
                if (v == null || !v.Exists() || !_onlineHashes.Contains(v.Model.Hash)) continue;
                if (IsMission(v) || (player.IsInVehicle() && player.CurrentVehicle != null && player.CurrentVehicle.Handle == v.Handle)) continue;
                float speed = 0f;
                bool engine = false;
                try { speed = Function.Call<float>(Hash.GET_ENTITY_SPEED, v.Handle); } catch { }
                try { engine = Function.Call<bool>(Hash.GET_IS_VEHICLE_ENGINE_RUNNING, v.Handle); } catch { }
                if (speed < 0.75f && !engine) continue;
                Ped driver = null;
                try { driver = v.Driver; } catch { }
                if (driver != null && driver.Exists() && !driver.IsDead) continue;
                if (Distance(player.Position, v.Position) < 28f) continue;
                RepairDriver(v, speed);
            }
        }

        private void RepairDriver(Vehicle vehicle, float previousSpeed)
        {
            string modelName = _civilianModels[Math.Abs((vehicle.Handle + Game.GameTime / 5000)) % _civilianModels.Length];
            int model = SafeHash(modelName);
            if (model == 0) return;
            try
            {
                Function.Call(Hash.REQUEST_MODEL, model);
                if (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, model)) return;
                int ped = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, vehicle.Handle, 26, model, -1, false, false);
                if (ped == 0) return;
                Function.Call(Hash.SET_DRIVER_ABILITY, ped, 0.78f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, ped, 0.25f);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, ped, vehicle.Handle, Math.Max(11f, Math.Min(24f, previousSpeed + 4f)), 786603);
                Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, model);
                Log("Repaired driverless moving Online traffic vehicle=" + vehicle.Handle + " model=" + vehicle.Model.Hash + ".");
            }
            catch { }
        }

        private static bool IsMission(Entity e) { try { return Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, e.Handle); } catch { return true; } }
        private static int SafeHash(string n) { try { return Function.Call<int>(Hash.GET_HASH_KEY, n); } catch { return 0; } }
        private static float Distance(GTA.Math.Vector3 a, GTA.Math.Vector3 b)
        {
            double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z; return (float)Math.Sqrt(x*x+y*y+z*z);
        }
        private static bool StoryOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            return false;
        }
        private static void Log(string s)
        {
            try { Directory.CreateDirectory("scripts\\WorldLifeVI"); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+s+Environment.NewLine); } catch { }
        }
    }
}
