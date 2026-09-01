using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class DispatchSystem
    {
        private sealed class CustomUnit
        {
            public int VehicleHandle;
            public readonly List<int> PedHandles = new List<int>();
            public int CreatedAt;
        }

        private readonly List<CustomUnit> _units = new List<CustomUnit>();
        private int _lastHeavySpawn,_lastJetSpawn,_fiveStarStartedAt,_lastFiveStarHeatAt,_lastRiskLog;
        private int _fiveStarHeatEvents;
        private bool _sixthAuthorized;

        public int UpdateTier(Ped player,int nativeWanted,CaseMemory memory,Config cfg,Action<string> log)
        {
            if(nativeWanted<5)
            {
                _fiveStarStartedAt=0;_lastFiveStarHeatAt=0;_fiveStarHeatEvents=0;_sixthAuthorized=false;
                return Math.Max(nativeWanted,memory==null?0:Math.Min(5,memory.ThreatLevel));
            }
            if(_fiveStarStartedAt==0){_fiveStarStartedAt=Game.GameTime;_fiveStarHeatEvents=0;_sixthAuthorized=false;}
            if(player!=null&&player.Exists()&&SafeBool(Hash.IS_PED_SHOOTING,player.Handle)&&Game.GameTime-_lastFiveStarHeatAt>=cfg.FiveStarShootingHeatIntervalMs)
            {
                _lastFiveStarHeatAt=Game.GameTime;_fiveStarHeatEvents++;if(memory!=null)memory.HeatPoints++;
            }
            bool time=Game.GameTime-_fiveStarStartedAt>=Math.Max(10,cfg.SixStarAfterFiveStarSeconds)*1000;
            bool heat=_fiveStarHeatEvents>=Math.Max(1,cfg.SixStarHeatThreshold);
            if(cfg.EnableSixthStar&&!_sixthAuthorized&&time&&heat)
            {
                _sixthAuthorized=true;
                if(memory!=null&&memory.ThreatLevel<6){memory.ThreatLevel=6;IdentificationSystem.AddNotoriety(memory,10f,cfg);memory.Touch(cfg);}
                if(log!=null)log("Sixth-star emergency response authorized.");
            }
            return _sixthAuthorized?6:5;
        }

        public void UpdateResponse(Ped player,int level,Config cfg,Action<string> log)
        {
            ConfigureVanillaDispatch(Math.Min(level,5));
            if(cfg.HidePoliceBlips)try{Function.Call(Hash.SET_POLICE_RADAR_BLIPS,false);}catch{}
            CleanupInvalidUnits(player,level);

            // RC4 repair: Rockstar alone owns normal 1..5-star ground pursuit.
            // No VOX police car is spawned or retasked at these levels.
            if(level<6)
            {
                if(_units.Count>0)CleanupMilitary();
                return;
            }

            // Losing visual contact must actually matter. Explicit TASK_COMBAT_PED /
            // heli chase tasks do not respect SET_POLICE_IGNORE_PLAYER reliably, so
            // military units are removed during the search phase and redeploy only
            // after a real reacquisition.
            if(PoliceSearchRuntimeState.SearchActive)
            {
                if(_units.Count>0)
                {
                    CleanupMilitary();
                    if(log!=null)log("Sixth-star direct-combat units withdrawn during confirmed search; no tunnel/wall omniscience.");
                }
                return;
            }

            if(!cfg.CustomDispatchEnabled||player==null||!player.Exists())return;
            int maxUnits=Math.Max(2,cfg.MaxCustomUnits);
            int now=Game.GameTime;
            if(now-_lastHeavySpawn>=Math.Max(7000,cfg.SixStarHeavyIntervalMs)&&_units.Count<maxUnits)
            {
                float risk=0f;bool explosiveAllowed=!cfg.CivilianRiskEnabled||CivilianRiskSystem.MilitaryEngagementAllowed(player,cfg,out risk);bool spawned=false;
                if(cfg.SixStarMilitaryGround&&_units.Count<maxUnits)spawned|=SpawnMilitaryGround(player,explosiveAllowed,log);
                if(cfg.SixStarAttackHelicopter&&_units.Count<maxUnits)spawned|=explosiveAllowed?SpawnHelicopter(player,"savage",true,log):SpawnHelicopter(player,"polmav",false,log);
                if(!explosiveAllowed&&now-_lastRiskLog>4000){_lastRiskLog=now;if(log!=null)log("Explosive military engagement withheld: civilian risk="+(int)risk+".");}
                if(spawned)_lastHeavySpawn=now;
            }
            if(cfg.SixStarJet&&now-_lastJetSpawn>=90000&&_units.Count<maxUnits)
            {
                float risk=0f;bool allowed=!cfg.CivilianRiskEnabled||CivilianRiskSystem.MilitaryEngagementAllowed(player,cfg,out risk);
                if(allowed&&SpawnJet(player,log))_lastJetSpawn=now;
            }
        }

        public void DrawSixthStarIfNeeded(int level){PoliceWantedHudState.Set(level,level>0);}

        public void CleanupAll()
        {
            CleanupMilitary();_fiveStarStartedAt=0;_lastFiveStarHeatAt=0;_fiveStarHeatEvents=0;_sixthAuthorized=false;_lastHeavySpawn=0;_lastJetSpawn=0;PoliceWantedHudState.Clear();
        }

        private static void ConfigureVanillaDispatch(int level)
        {
            try
            {
                Function.Call(Hash.SET_DISPATCH_COPS_FOR_PLAYER,Game.Player.Handle,level>0);Function.Call(Hash.SET_MAX_WANTED_LEVEL,5);
                for(int service=1;service<=15;service++)Function.Call(Hash.ENABLE_DISPATCH_SERVICE,service,level>0);
            }
            catch{}
        }

        private bool SpawnMilitaryGround(Ped player,bool explosivesAllowed,Action<string> log)
        {
            bool tank=explosivesAllowed&&((Game.GameTime/30000)&1)==1;
            string vehicleName=tank?"rhino":"crusader";int vehicleModel=SafeHash(vehicleName),pedModel=SafeHash("s_m_y_marine_01");
            if(!EnsureModel(vehicleModel)||!EnsureModel(pedModel))return false;
            Vector3 pos=FindGroundSpawn(player,260f+_units.Count*22f);float heading=HeadingTo(pos,player.Position);var unit=new CustomUnit{CreatedAt=Game.GameTime};
            try
            {
                int v=Function.Call<int>(Hash.CREATE_VEHICLE,vehicleModel,pos.X,pos.Y,pos.Z,heading,false,false);if(v==0)return false;unit.VehicleHandle=v;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY,v,true,true);Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY,v);Function.Call(Hash.SET_VEHICLE_ENGINE_ON,v,true,true,false);
                int driver=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,v,6,pedModel,-1,false,false);
                if(driver!=0)
                {
                    unit.PedHandles.Add(driver);SetupMilitaryPed(driver);Function.Call(Hash.SET_DRIVER_ABILITY,driver,1f);Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS,driver,0.84f);
                    if(tank)Function.Call(Hash.TASK_COMBAT_PED,driver,player.Handle,0,16);else Function.Call(Hash.TASK_VEHICLE_CHASE,driver,player.Handle);
                }
                if(!tank)
                {
                    for(int seat=0;seat<2;seat++)
                    {
                        int p=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,v,6,pedModel,seat,false,false);if(p==0)continue;unit.PedHandles.Add(p);SetupMilitaryPed(p);Function.Call(Hash.TASK_COMBAT_PED,p,player.Handle,0,16);
                    }
                }
                _units.Add(unit);if(log!=null)log("Sixth-star military ground unit deployed: "+vehicleName+".");return true;
            }
            catch(Exception ex){if(log!=null)log("Sixth-star ground spawn failed: "+ex.Message);CleanupUnit(unit);return false;}
            finally{ReleaseModel(vehicleModel);ReleaseModel(pedModel);}
        }

        private bool SpawnHelicopter(Ped player,string modelName,bool armed,Action<string> log)
        {
            int vm=SafeHash(modelName),pm=SafeHash(armed?"s_m_y_marine_01":"s_m_y_cop_01");if(!EnsureModel(vm)||!EnsureModel(pm))return false;
            Vector3 p=player.Position;double a=(Game.GameTime%6283)/1000.0;Vector3 spawn=new Vector3(p.X+(float)Math.Cos(a)*430f,p.Y+(float)Math.Sin(a)*430f,p.Z+150f);var unit=new CustomUnit{CreatedAt=Game.GameTime};
            try
            {
                int heli=Function.Call<int>(Hash.CREATE_VEHICLE,vm,spawn.X,spawn.Y,spawn.Z,HeadingTo(spawn,p),false,false);if(heli==0)return false;unit.VehicleHandle=heli;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY,heli,true,true);Function.Call(Hash.SET_VEHICLE_ENGINE_ON,heli,true,true,false);Function.Call(Hash.SET_HELI_BLADES_FULL_SPEED,heli);
                int pilot=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,heli,6,pm,-1,false,false);if(pilot!=0){unit.PedHandles.Add(pilot);SetupMilitaryPed(pilot);Function.Call(Hash.TASK_HELI_CHASE,pilot,player.Handle,0f,0f,0f);}
                if(armed)for(int seat=0;seat<=1;seat++){int gunner=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,heli,6,pm,seat,false,false);if(gunner==0)continue;unit.PedHandles.Add(gunner);SetupMilitaryPed(gunner);Function.Call(Hash.TASK_COMBAT_PED,gunner,player.Handle,0,16);}
                _units.Add(unit);if(log!=null)log(armed?"Sixth-star attack helicopter deployed.":"Sixth-star overwatch helicopter deployed.");return true;
            }
            catch(Exception ex){if(log!=null)log("Helicopter spawn failed: "+ex.Message);CleanupUnit(unit);return false;}
            finally{ReleaseModel(vm);ReleaseModel(pm);}
        }

        private bool SpawnJet(Ped player,Action<string> log)
        {
            int vm=SafeHash("lazer"),pm=SafeHash("s_m_y_marine_01");if(!EnsureModel(vm)||!EnsureModel(pm))return false;Vector3 p=player.Position;Vector3 spawn=new Vector3(p.X-800f,p.Y-500f,p.Z+320f);var unit=new CustomUnit{CreatedAt=Game.GameTime};
            try
            {
                int jet=Function.Call<int>(Hash.CREATE_VEHICLE,vm,spawn.X,spawn.Y,spawn.Z,HeadingTo(spawn,p),false,false);if(jet==0)return false;unit.VehicleHandle=jet;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY,jet,true,true);Function.Call(Hash.SET_VEHICLE_ENGINE_ON,jet,true,true,false);Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED,jet,65f);
                int pilot=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,jet,6,pm,-1,false,false);if(pilot!=0){unit.PedHandles.Add(pilot);SetupMilitaryPed(pilot);Function.Call(Hash.TASK_COMBAT_PED,pilot,player.Handle,0,16);}
                _units.Add(unit);if(log!=null)log("Sixth-star jet deployed.");return true;
            }
            catch(Exception ex){if(log!=null)log("Jet spawn failed: "+ex.Message);CleanupUnit(unit);return false;}
            finally{ReleaseModel(vm);ReleaseModel(pm);}
        }

        private static void SetupMilitaryPed(int h)
        {
            if(!EntityExists(h))return;try{Function.Call(Hash.SET_PED_AS_COP,h,true);Function.Call(Hash.SET_PED_ACCURACY,h,46);Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES,h,46,true);Function.Call(Hash.SET_PED_COMBAT_ABILITY,h,2);int w=SafeHash("WEAPON_CARBINERIFLE");Function.Call(Hash.GIVE_WEAPON_TO_PED,h,w,300,false,true);}catch{}
        }
        private void CleanupInvalidUnits(Ped player,int level)
        {
            for(int i=_units.Count-1;i>=0;i--)
            {
                CustomUnit u=_units[i];bool remove=level<6||!EntityExists(u.VehicleHandle)||Game.GameTime-u.CreatedAt>360000;
                if(!remove&&player!=null&&player.Exists()){Vector3 pos=GetPosition(u.VehicleHandle);if(pos!=Vector3.Zero&&Perception.Distance(pos,player.Position)>2300f)remove=true;}
                if(remove){CleanupUnit(u);_units.RemoveAt(i);}
            }
        }
        private void CleanupMilitary(){foreach(CustomUnit u in _units)CleanupUnit(u);_units.Clear();}
        private static void CleanupUnit(CustomUnit u){if(u==null)return;foreach(int p in u.PedHandles)DeleteEntity(p);DeleteEntity(u.VehicleHandle);}
        private static Vector3 FindGroundSpawn(Ped player,float distance){double a=((Game.GameTime/137)%6283)/1000.0;Vector3 p=player.Position;Vector3 seed=new Vector3(p.X+(float)Math.Cos(a)*distance,p.Y+(float)Math.Sin(a)*distance,p.Z);try{Vector3 s=World.GetNextPositionOnStreet(seed);if(s!=Vector3.Zero)return s;}catch{}return seed;}
        private static bool EnsureModel(int h){if(h==0)return false;try{if(!Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE,h)||!Function.Call<bool>(Hash.IS_MODEL_VALID,h))return false;Function.Call(Hash.REQUEST_MODEL,h);return Function.Call<bool>(Hash.HAS_MODEL_LOADED,h);}catch{return false;}}
        private static void ReleaseModel(int h){try{if(h!=0)Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED,h);}catch{}}
        private static int SafeHash(string n){try{return Function.Call<int>(Hash.GET_HASH_KEY,n);}catch{return 0;}}
        private static float HeadingTo(Vector3 from,Vector3 to){try{return Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D,to.X-from.X,to.Y-from.Y);}catch{return 0f;}}
        private static bool EntityExists(int h){try{return h!=0&&Function.Call<bool>(Hash.DOES_ENTITY_EXIST,h);}catch{return false;}}
        private static Vector3 GetPosition(int h){try{return EntityExists(h)?Function.Call<Vector3>(Hash.GET_ENTITY_COORDS,h,true):Vector3.Zero;}catch{return Vector3.Zero;}}
        private static void DeleteEntity(int h){if(!EntityExists(h))return;try{Entity e=Entity.FromHandle(h);if(e!=null&&e.Exists())e.Delete();}catch{}}
        private static bool SafeBool(Hash h,params InputArgument[] a){try{return Function.Call<bool>(h,a);}catch{return false;}}
    }
}
