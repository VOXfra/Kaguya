using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    public sealed class PoliceOverhaulVIPursuitDriverRecoveryScript : Script
    {
        private const string LogPath = "scripts\\PoliceOverhaulVI\\PoliceOverhaulVI.log";

        private sealed class StallState
        {
            public int VehicleHandle;
            public int DriverHandle;
            public int StalledSince;
            public int LastRetask;
            public Vector3 LastPosition;
        }

        private readonly Dictionary<int, StallState> _states = new Dictionary<int, StallState>();
        private int _lastScan;
        private int _storyYieldUntil;

        public PoliceOverhaulVIPursuitDriverRecoveryScript()
        {
            Interval = 100;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Police pursuit driver recovery 0.7.0 loaded.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { _states.Clear(); return; }
                if (RockstarOwnsScene()) { _storyYieldUntil = Game.GameTime + 6000; _states.Clear(); return; }
                if (Game.GameTime < _storyYieldUntil) { _states.Clear(); return; }

                int wanted = WantedLevel();
                if (wanted <= 0) { _states.Clear(); return; }
                if (Game.GameTime - _lastScan < 450) return;
                _lastScan = Game.GameTime;

                Vehicle[] vehicles;
                try { vehicles = World.GetNearbyVehicles(player, 650f); } catch { return; }
                var live = new HashSet<int>();
                foreach (Vehicle v in vehicles)
                {
                    if (v == null || !v.Exists() || !IsPoliceVehicle(v)) continue;
                    Ped driver = null;
                    try { driver = v.Driver; } catch { }
                    if (driver == null || !driver.Exists() || driver.IsDead || !IsLawPed(driver)) continue;
                    live.Add(v.Handle);
                    MaintainUnit(player, v, driver, wanted);
                }

                var remove = new List<int>();
                foreach (int handle in _states.Keys) if (!live.Contains(handle)) remove.Add(handle);
                foreach (int handle in remove) _states.Remove(handle);
            }
            catch (Exception ex) { Log("Pursuit driver recovery tick error: " + ex.Message); }
        }

        private void MaintainUnit(Ped player, Vehicle vehicle, Ped driver, int wanted)
        {
            float distance = Distance(vehicle.Position, player.Position);
            if (distance < 15f)
            {
                _states.Remove(vehicle.Handle);
                return;
            }

            StallState s;
            if (!_states.TryGetValue(vehicle.Handle, out s) || s.DriverHandle != driver.Handle)
            {
                s = new StallState
                {
                    VehicleHandle = vehicle.Handle,
                    DriverHandle = driver.Handle,
                    StalledSince = 0,
                    LastRetask = 0,
                    LastPosition = vehicle.Position
                };
                _states[vehicle.Handle] = s;
            }

            float speed = SafeSpeed(vehicle);
            float moved = Distance(s.LastPosition, vehicle.Position);
            s.LastPosition = vehicle.Position;

            bool physicallyBlocked = false;
            try { physicallyBlocked = Function.Call<bool>(Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, vehicle.Handle); } catch { }
            bool shouldBeClosing = distance > 28f && distance < 620f;
            bool stalled = shouldBeClosing && speed < 2.2f && moved < 1.0f;

            if (!stalled)
            {
                s.StalledSince = 0;
                return;
            }

            if (s.StalledSince == 0) s.StalledSince = Game.GameTime;
            int stallTime = Game.GameTime - s.StalledSince;
            int minimum = physicallyBlocked ? 4200 : 2600;
            if (stallTime < minimum || Game.GameTime - s.LastRetask < 5500) return;

            s.LastRetask = Game.GameTime;
            s.StalledSince = Game.GameTime;
            RetaskUnit(player, vehicle, driver, wanted, distance);
        }

        private static void RetaskUnit(Ped player, Vehicle vehicle, Ped driver, int wanted, float distance)
        {
            try
            {
                Function.Call(Hash.SET_DRIVER_ABILITY, driver.Handle, 1.0f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver.Handle, wanted >= 4 ? 0.88f : (wanted >= 2 ? 0.72f : 0.55f));
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
                Function.Call(Hash.SET_VEHICLE_SIREN, vehicle.Handle, true);
                Function.Call(Hash.SET_PED_KEEP_TASK, driver.Handle, true);

                if (player.IsInVehicle())
                {
                    Function.Call(Hash.TASK_VEHICLE_CHASE, driver.Handle, player.Handle);
                    Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver.Handle, wanted >= 4 ? 786988 : 786603);
                }
                else if (distance > 55f)
                {
                    Vector3 p = player.Position;
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE, driver.Handle, vehicle.Handle,
                        p.X, p.Y, p.Z, wanted >= 4 ? 34f : 28f, 786603, 16f);
                }
                else Function.Call(Hash.TASK_VEHICLE_CHASE, driver.Handle, player.Handle);
            }
            catch { }
            Log("Retasked stalled police pursuit vehicle=" + vehicle.Handle + " distance=" + (int)distance + " wanted=" + wanted + ".");
        }

        private static bool IsPoliceVehicle(Vehicle v)
        {
            try
            {
                if (Function.Call<bool>(Hash.IS_VEHICLE_SIREN_ON, v.Handle)) return true;
                int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, v.Handle);
                if (cls == 18) return true;
            }
            catch { }
            Ped driver = null;
            try { driver = v.Driver; } catch { }
            return driver != null && driver.Exists() && IsLawPed(driver);
        }

        private static bool IsLawPed(Ped p)
        {
            try { int t = (int)p.PedType; return t == 6 || t == 27 || t == 29; }
            catch { return false; }
        }

        private static int WantedLevel()
        {
            try { return Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle); }
            catch { return 0; }
        }

        private static float SafeSpeed(Entity e)
        {
            try { return Function.Call<float>(Hash.GET_ENTITY_SPEED, e.Handle); }
            catch { return 0f; }
        }

        private static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.X-b.X, y = a.Y-b.Y, z = a.Z-b.Z;
            return (float)Math.Sqrt(x*x+y*y+z*z);
        }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try
            {
                if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true;
            }
            catch { }
            return false;
        }

        private void OnAborted(object sender, EventArgs e) { _states.Clear(); }

        private static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory("scripts\\PoliceOverhaulVI");
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine);
            }
            catch { }
        }
    }
}
