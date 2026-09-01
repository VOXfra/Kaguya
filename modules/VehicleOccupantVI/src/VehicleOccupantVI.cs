using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;

namespace VOX.VehicleOccupantVI
{
    public sealed class VehicleOccupantVIScript : Script
    {
        private const string DataDir = "scripts\\VehicleOccupantVI";
        private const string LogPath = DataDir + "\\VehicleOccupantVI.log";

        private sealed class PendingReaction
        {
            public int PedHandle;
            public int VehicleHandle;
            public int ExecuteAt;
            public int Kind;
        }

        private readonly List<PendingReaction> _pending = new List<PendingReaction>();
        private int _lastPlayerVehicle;
        private bool _wasDriver;
        private int _storyYieldUntil;

        public VehicleOccupantVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Interval = 80;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Vehicle Occupant VI 0.1.0 loaded: staggered passenger reactions to free-roam carjackings.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { Reset(); return; }
                if (RockstarOwnsScene()) { _storyYieldUntil = Game.GameTime + 5000; Reset(); return; }
                if (Game.GameTime < _storyYieldUntil) { Reset(); return; }

                bool driverNow = false;
                Vehicle v = null;
                try
                {
                    if (player.IsInVehicle())
                    {
                        v = player.CurrentVehicle;
                        if (v != null && v.Exists())
                        {
                            int d = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, v.Handle, -1, false);
                            driverNow = d == player.Handle;
                        }
                    }
                }
                catch { }

                if (driverNow && v != null && v.Exists() && !IsMission(v) && !IsPersonal(v) && (!_wasDriver || _lastPlayerVehicle != v.Handle))
                    QueuePassengerReactions(player, v);

                _wasDriver = driverNow;
                _lastPlayerVehicle = v != null && v.Exists() ? v.Handle : 0;
                ProcessPending(player);
            }
            catch (Exception ex) { Log("Occupant tick error: " + ex.Message); }
        }

        private void QueuePassengerReactions(Ped player, Vehicle vehicle)
        {
            int maxPassengers = 0;
            try { maxPassengers = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle.Handle); } catch { }
            int queued = 0;
            for (int seat = 0; seat < Math.Min(15, Math.Max(0, maxPassengers)); seat++)
            {
                int ph = 0;
                try { ph = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, vehicle.Handle, seat, false); } catch { }
                if (ph == 0 || ph == player.Handle) continue;
                Ped p = null;
                try { p = Entity.FromHandle(ph) as Ped; } catch { }
                if (!EligiblePassenger(p)) continue;
                int roll = StableRoll(ph, vehicle.Handle);
                int kind = roll < 72 ? 0 : (roll < 91 ? 1 : 2); // flee / freeze-then-flee / rare confront
                _pending.Add(new PendingReaction
                {
                    PedHandle = ph,
                    VehicleHandle = vehicle.Handle,
                    ExecuteAt = Game.GameTime + 250 + queued * 220 + roll * 7,
                    Kind = kind
                });
                queued++;
            }
            if (queued > 0) Log("Queued " + queued + " passenger reactions for vehicle=" + vehicle.Handle + ".");
        }

        private void ProcessPending(Ped player)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingReaction r = _pending[i];
                if (Game.GameTime < r.ExecuteAt) continue;
                _pending.RemoveAt(i);
                Ped p = null;
                Vehicle v = null;
                try { p = Entity.FromHandle(r.PedHandle) as Ped; v = Entity.FromHandle(r.VehicleHandle) as Vehicle; } catch { }
                if (!EligiblePassenger(p) || v == null || !v.Exists() || IsMission(v)) continue;

                try
                {
                    if (r.Kind == 1)
                    {
                        Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, p.Handle, "GENERIC_SHOCKED_HIGH", "SPEECH_PARAMS_FORCE");
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, p.Handle, v.Handle, 0);
                        _pending.Add(new PendingReaction { PedHandle=p.Handle, VehicleHandle=v.Handle, ExecuteAt=Game.GameTime+1500, Kind=0 });
                    }
                    else if (r.Kind == 2)
                    {
                        Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, p.Handle, "GENERIC_INSULT_HIGH", "SPEECH_PARAMS_FORCE");
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, p.Handle, v.Handle, 256);
                        _pending.Add(new PendingReaction { PedHandle=p.Handle, VehicleHandle=v.Handle, ExecuteAt=Game.GameTime+900, Kind=3 });
                    }
                    else if (r.Kind == 3)
                    {
                        if (!p.IsInVehicle() && Distance(p.Position, player.Position) < 8f)
                            Function.Call(Hash.TASK_COMBAT_PED, p.Handle, player.Handle, 0, 16);
                        else Function.Call(Hash.TASK_SMART_FLEE_PED, p.Handle, player.Handle, 35f, 10000, false, false);
                    }
                    else
                    {
                        if (p.IsInVehicle())
                        {
                            Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, p.Handle, "GENERIC_FRIGHTENED_HIGH", "SPEECH_PARAMS_FORCE");
                            Function.Call(Hash.TASK_LEAVE_VEHICLE, p.Handle, v.Handle, 0);
                            _pending.Add(new PendingReaction { PedHandle=p.Handle, VehicleHandle=v.Handle, ExecuteAt=Game.GameTime+850, Kind=4 });
                        }
                        else Function.Call(Hash.TASK_SMART_FLEE_PED, p.Handle, player.Handle, 45f, 12000, false, false);
                    }
                    if (r.Kind == 4 && !p.IsInVehicle()) Function.Call(Hash.TASK_SMART_FLEE_PED, p.Handle, player.Handle, 45f, 12000, false, false);
                }
                catch { }
            }
        }

        private static bool EligiblePassenger(Ped p)
        {
            if (p == null || !p.Exists() || p.IsDead || !p.IsHuman) return false;
            try { if (Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, p.Handle)) return false; } catch { return false; }
            try { int t=(int)p.PedType; if(t==6||t==27||t==29) return false; } catch { }
            return true;
        }

        private static bool IsPersonal(Vehicle v)
        {
            int h=v.Model.Hash;
            return h==SafeHash("tailgater") || h==SafeHash("buffalo2") || h==SafeHash("bodhi2");
        }
        private static bool IsMission(Entity e){try{return Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY,e.Handle);}catch{return true;}}
        private static int SafeHash(string n){try{return Function.Call<int>(Hash.GET_HASH_KEY,n);}catch{return 0;}}
        private static int StableRoll(int a,int b){unchecked{int x=a*1103515245+b*397; x^=x>>16; if(x==int.MinValue)x=0; return Math.Abs(x)%100;}}
        private static float Distance(Vector3 a,Vector3 b){double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return(float)Math.Sqrt(x*x+y*y+z*z);}
        private static bool RockstarOwnsScene()
        {
            try{if(Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE))return true;}catch{}
            try{if(Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))return true;}catch{}
            try{if(Function.Call<bool>(Hash.GET_MISSION_FLAG))return true;}catch{}
            try{if(!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON,Game.Player.Handle))return true;}catch{}
            try{if(Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_IN))return true;}catch{}
            return false;
        }
        private void Reset(){_pending.Clear();_lastPlayerVehicle=0;_wasDriver=false;}
        private void OnAborted(object sender,EventArgs e){Reset();}
        private static void Log(string s){try{Directory.CreateDirectory(DataDir);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+s+Environment.NewLine);}catch{}}
    }
}
