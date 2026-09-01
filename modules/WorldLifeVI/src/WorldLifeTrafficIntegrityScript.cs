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
        private readonly Dictionary<int,int> _stalledSince = new Dictionary<int,int>();
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
        private int _lastScan, _storyYieldUntil;

        public WorldLifeVITrafficIntegrityScript()
        {
            foreach(string n in _onlineModels)_onlineHashes.Add(SafeHash(n));
            Interval=250;Tick+=OnTick;Aborted+=OnAborted;
            Log("World Life VI traffic-integrity 0.4.0 loaded: driverless/stalled Online traffic is recovered instead of being left in the road.");
        }

        private void OnTick(object sender,EventArgs e)
        {
            try
            {
                Ped player=Game.LocalPlayerPed;if(player==null||!player.Exists()||player.IsDead){_stalledSince.Clear();return;}
                if(StoryOwnsScene()){_storyYieldUntil=Game.GameTime+5000;_stalledSince.Clear();return;}
                if(Game.GameTime<_storyYieldUntil)return;
                if(Game.GameTime-_lastScan<900)return;_lastScan=Game.GameTime;
                Vehicle[] vehicles;try{vehicles=World.GetNearbyVehicles(player,155f);}catch{return;}
                var live=new HashSet<int>();
                foreach(Vehicle v in vehicles)
                {
                    if(v==null||!v.Exists()||!_onlineHashes.Contains(v.Model.Hash)||IsMission(v))continue;
                    if(player.IsInVehicle()&&player.CurrentVehicle!=null&&player.CurrentVehicle.Handle==v.Handle)continue;
                    if(Distance(player.Position,v.Position)<24f)continue;
                    live.Add(v.Handle);
                    Ped driver=null;try{driver=v.Driver;}catch{}
                    if(driver==null||!driver.Exists()||driver.IsDead){RepairDriver(v);_stalledSince.Remove(v.Handle);continue;}
                    if(IsLaw(driver))continue;
                    float speed=SafeSpeed(v);
                    if(speed>=1.2f){_stalledSince.Remove(v.Handle);continue;}
                    int since;
                    if(!_stalledSince.TryGetValue(v.Handle,out since)){_stalledSince[v.Handle]=Game.GameTime;continue;}
                    if(Game.GameTime-since<11000)continue;
                    RetaskDriver(v,driver);
                    _stalledSince[v.Handle]=Game.GameTime;
                }
                var remove=new List<int>();foreach(int h in _stalledSince.Keys)if(!live.Contains(h))remove.Add(h);foreach(int h in remove)_stalledSince.Remove(h);
            }
            catch(Exception ex){Log("Traffic integrity tick error: "+ex.Message);}
        }

        private void RepairDriver(Vehicle vehicle)
        {
            string modelName=_civilianModels[Math.Abs((vehicle.Handle+Game.GameTime/5000))%_civilianModels.Length];int model=SafeHash(modelName);if(model==0)return;
            try
            {
                Function.Call(Hash.REQUEST_MODEL,model);if(!Function.Call<bool>(Hash.HAS_MODEL_LOADED,model))return;
                int ped=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,vehicle.Handle,26,model,-1,false,false);if(ped==0)return;
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON,vehicle.Handle,true,true,false);Function.Call(Hash.SET_DRIVER_ABILITY,ped,0.84f);Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS,ped,0.28f);
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER,ped,vehicle.Handle,18f,786603);Function.Call(Hash.SET_PED_KEEP_TASK,ped,true);Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED,model);
                Log("Recovered driverless Online traffic vehicle="+vehicle.Handle+" model="+vehicle.Model.Hash+".");
            }
            catch(Exception ex){Log("Driver recovery failed vehicle="+vehicle.Handle+": "+ex.Message);}
        }

        private static void RetaskDriver(Vehicle vehicle,Ped driver)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON,vehicle.Handle,true,true,false);Function.Call(Hash.SET_DRIVER_ABILITY,driver.Handle,0.84f);Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS,driver.Handle,0.28f);
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER,driver.Handle,vehicle.Handle,18f,786603);Function.Call(Hash.SET_PED_KEEP_TASK,driver.Handle,true);
            }
            catch{}
            Log("Retasked stalled Online traffic vehicle="+vehicle.Handle+" driver="+driver.Handle+".");
        }

        private static bool IsMission(Entity e){try{return Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY,e.Handle);}catch{return true;}}
        private static bool IsLaw(Ped p){try{int t=(int)p.PedType;return t==6||t==27||t==29;}catch{return false;}}
        private static int SafeHash(string n){try{return Function.Call<int>(Hash.GET_HASH_KEY,n);}catch{return 0;}}
        private static float SafeSpeed(Entity e){try{return Function.Call<float>(Hash.GET_ENTITY_SPEED,e.Handle);}catch{return 0f;}}
        private static float Distance(GTA.Math.Vector3 a,GTA.Math.Vector3 b){double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return(float)Math.Sqrt(x*x+y*y+z*z);}
        private static bool StoryOwnsScene(){try{if(Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE))return true;}catch{}try{if(Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))return true;}catch{}try{if(Function.Call<bool>(Hash.GET_MISSION_FLAG))return true;}catch{}try{if(!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON,Game.Player.Handle))return true;}catch{}return false;}
        private void OnAborted(object sender,EventArgs e){_stalledSince.Clear();}
        private static void Log(string s){try{Directory.CreateDirectory("scripts\\WorldLifeVI");File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+s+Environment.NewLine);}catch{}}
    }
}
