using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    public sealed class TrackedVehicleSearchBridge : Script
    {
        private const string ConfigPath = "scripts\\PoliceOverhaulVI.ini";
        private const string DataDirectory = "scripts\\PoliceOverhaulVI";
        private const string LogPath = DataDirectory + "\\PoliceOverhaulVI.log";

        private readonly Config _cfg;
        private int _trackedVehicleHandle;
        private int _boundModel;
        private int _lastPingLog;

        public TrackedVehicleSearchBridge()
        {
            Directory.CreateDirectory(DataDirectory);
            _cfg = Config.Load(ConfigPath);
            Interval = 200;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Tracked-vehicle search bridge loaded: search centre follows the flagged car, not the replacement driver position.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead)
                {
                    _trackedVehicleHandle = 0;
                    _boundModel = 0;
                    return;
                }
                if (RockstarOwnsScene()) return;

                if (_boundModel != player.Model.Hash)
                {
                    _boundModel = player.Model.Hash;
                    _trackedVehicleHandle = 0;
                }

                CaseMemory memory = PoliceSearchRuntimeState.CaseFor(player);
                if (memory == null || memory.Vehicle == null || !memory.Vehicle.TrackerKnownByPolice)
                {
                    _trackedVehicleHandle = 0;
                    return;
                }

                CaptureCurrentTrackedVehicle(player, memory);
                if (!PoliceSearchRuntimeState.SearchActive) return;

                Vehicle tracked = ResolveTrackedVehicle(memory);
                if (tracked == null || !tracked.Exists()) return;

                Vector3 p = tracked.Position;
                PoliceSearchRuntimeState.LastKnownPosition = p;
                PoliceSearchRuntimeState.LastTrackerPingAt = Game.GameTime;
                memory.LastKnownPosition = p;
                memory.LastSource = ObservationSource.Tracker;
                memory.Touch(_cfg);

                if (Game.GameTime - _lastPingLog > 5000)
                {
                    _lastPingLog = Game.GameTime;
                    bool playerStillInside = player.IsInVehicle() && player.CurrentVehicle != null && player.CurrentVehicle.Exists() && player.CurrentVehicle.Handle == tracked.Handle;
                    Log("Tracker position update: vehicle=" + tracked.Handle + ", playerInside=" + playerStillInside + ". Search centre follows vehicle only.");
                }
            }
            catch (Exception ex)
            {
                Log("Tracked-vehicle bridge error: " + ex.Message);
            }
        }

        private void CaptureCurrentTrackedVehicle(Ped player, CaseMemory memory)
        {
            if (!player.IsInVehicle()) return;
            Vehicle v = player.CurrentVehicle;
            if (v == null || !v.Exists()) return;
            if (!memory.Vehicle.Matches(v, true)) return;
            _trackedVehicleHandle = v.Handle;
        }

        private Vehicle ResolveTrackedVehicle(CaseMemory memory)
        {
            if (_trackedVehicleHandle > 0)
            {
                try
                {
                    Vehicle v = Entity.FromHandle(_trackedVehicleHandle) as Vehicle;
                    if (v != null && v.Exists() && memory.Vehicle.Matches(v, true)) return v;
                }
                catch { }
                _trackedVehicleHandle = 0;
            }

            Vector3 centre = PoliceSearchRuntimeState.LastKnownPosition;
            if (centre == Vector3.Zero) return null;
            Vehicle[] nearby;
            try { nearby = World.GetNearbyVehicles(centre, 220f); }
            catch { return null; }
            foreach (Vehicle v in nearby)
            {
                if (v == null || !v.Exists()) continue;
                try
                {
                    if (!memory.Vehicle.Matches(v, true)) continue;
                    _trackedVehicleHandle = v.Handle;
                    return v;
                }
                catch { }
            }
            return null;
        }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            bool mission = false, control = true;
            try { mission = Function.Call<bool>(Hash.GET_MISSION_FLAG); } catch { }
            try { control = Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle); } catch { }
            if (mission || !control) return true;
            try { return Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN); }
            catch { return true; }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _trackedVehicleHandle = 0;
        }

        private static void Log(string text)
        {
            try { File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + text + Environment.NewLine); }
            catch { }
        }
    }
}
