using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class DispatchSystem
    {
        private sealed class CustomUnit { public int VehicleHandle; public readonly List<int> PedHandles=new List<int>(); public int RequiredLevel; public int CreatedAt; }
        private readonly List<CustomUnit> _units=new List<CustomUnit>();
        private int _lastSupportSpawn,_lastHeavySpawn,_lastJetSpawn,_fiveStarStartedAt,_lastFiveStarHeatAt,_sixthStarTextureRequestAt;

        public int UpdateTier(Ped player,int nativeWanted,CaseMemory memory,Config cfg,Action<string> log)
        {
            if(cfg.EnableSixthStar&&nativeWanted>=5&&memory!=null&&memory.ThreatLevel>=6)return 6;
            if(nativeWanted<5){_fiveStarStartedAt=0;_lastFiveStarHeatAt=0;return Math.Max(nativeWanted,memory==null?0:Math.Min(5,memory.ThreatLevel));}
            if(_fiveStarStartedAt==0)_fiveStarStartedAt=Game.GameTime;
            if(player!=null&&player.Exists()&&SafeBool(Hash.IS_PED_SHOOTING,player.Handle)&&Game.GameTime-_lastFiveStarHeatAt>=cfg.FiveStarShootingHeatIntervalMs)
            {
                _lastFiveStarHeatAt=Game.GameTime;if(memory!=null)memory.HeatPoints++;
            }
            bool time=Game.GameTime-_fiveStarStartedAt>=Math.Max(10,cfg.SixStarAfterFiveStarSeconds)*1000;
            bool heat=memory!=null&&memory.HeatPoints>=Math.Max(1,cfg.SixStarHeatThreshold);
            if(cfg.EnableSixthStar&&(time||heat))
            {
                if(memory!=null&&memory.ThreatLevel<6){memory.ThreatLevel=6;memory.Touch(cfg);if(log!=null)log("Sixth-star emergency response authorized.");}
                return 6;
            }
            return 5;
        }

        public void UpdateResponse(Ped player,int level,Config cfg,Action<string> log)
        {
            ConfigureVanillaDispatch(level);CleanupInvalidUnits(player,level);
            if(!cfg.CustomDispatchEnabled||player==null||!player.Exists()||level<=0||_units.Count>=Math.Max(1,cfg.MaxCustomUnits))return;
            int now=Game.GameTime;
            if(level>=3&&level<=5&&now-_lastSupportSpawn>=cfg.DispatchSupportIntervalMs)
            {
                if(SpawnTacticalGround(player,level,log))_lastSupportSpawn=now;
            }
            if(level>=6&&now-_lastHeavySpawn>=cfg.SixStarHeavyIntervalMs)
            {
                bool spawned=false;
                if(cfg.SixStarMilitaryGround)spawned|=SpawnMilitaryGround(player,log);
                if(cfg.SixStarAttackHelicopter&&_units.Count<cfg.MaxCustomUnits)spawned|=SpawnAttackHelicopter(player,log);
                if(spawned)_lastHeavySpawn=now;
            }
            if(level>=6&&cfg.SixStarJet&&now-_lastJetSpawn>=90000&&_units.Count<cfg.MaxCustomUnits)
            {
                if(SpawnJet(player,log))_lastJetSpawn=now;
            }
        }

        public void DrawSixthStarIfNeeded(int level)
        {
            if(level<6)return;
            try
            {
                if(!Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED,"commonmenu"))
                {
                    if(Game.GameTime-_sixthStarTextureRequestAt>1000){_sixthStarTextureRequestAt=Game.GameTime;Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT,"commonmenu",false);}return;
                }
                float safe=Function.Call<float>(Hash.GET_SAFE_ZONE_SIZE);float right=1f-(1f-safe)*0.5f;
                Function.Call(Hash.DRAW_SPRITE,"commonmenu","leaderboard_star_icon",right-0.118f,0.0455f,0.019f,0.034f,0f,255,255,255,245,false);
            }
            catch{}
        }

        public void CleanupAll(){foreach(CustomUnit u in _units)CleanupUnit(u);_units.Clear();_fiveStarStartedAt=0;_lastFiveStarHeatAt=0;}

        private static void ConfigureVanillaDispatch(int level)
        {
            try
            {
                Function.Call(Hash.SET_DISPATCH_COPS_FOR_PLAYER,Game.Player.Handle,level>0);Function.Call(Hash.SET_MAX_WANTED_LEVEL,5);
                for(int service=1;service<=15;service++)
                {
                    bool enabled=level>0;
                    if((service==2||service==3)&&level<3)enabled=false;
                    Function.Call(Hash.ENABLE_DISPATCH_SERVICE,service,enabled);
                }
            }
            catch{}
        }

        private bool SpawnTacticalGround(Ped player,int level,Action<string> log)
        {
            bool rural=IsRural(player.Position);
            if(level==3)
                return SpawnGroundUnit(player,rural?"sheriff2":"police3",rural?"s_m_y_sheriff_01":"s_m_y_cop_01",level,1,false,false,log);
            return SpawnGroundUnit(player,"riot","s_m_y_swat_01",level,2,false,true,log);
        }

        private bool SpawnMilitaryGround(Ped player,Action<string> log)
        {
            bool tank=((Game.GameTime/30000)&1)==1;
            return SpawnGroundUnit(player,tank?"rhino":"crusader","s_m_y_marine_01",6,tank?0:2,tank,true,log);
        }

        private bool SpawnGroundUnit(Ped player,string vehicleName,string pedName,int level,int passengers,bool tank,bool lethal,Action<string> log)
        {
            int vehicleModel=Function.Call<int>(Hash.GET_HASH_KEY,vehicleName),pedModel=Function.Call<int>(Hash.GET_HASH_KEY,pedName);
            if(!EnsureModel(vehicleModel)||!EnsureModel(pedModel))return false;
            Vector3 pos=FindGroundSpawn(player,230f+_units.Count*18f);float heading=HeadingTo(pos,player.Position);
            var unit=new CustomUnit{RequiredLevel=level,CreatedAt=Game.GameTime};
            try
            {
                int v=Function.Call<int>(Hash.CREATE_VEHICLE,vehicleModel,pos.X,pos.Y,pos.Z,heading,false,false);if(v==0)return false;
                unit.VehicleHandle=v;Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY,v,true,true);Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY,v);Function.Call(Hash.SET_VEHICLE_ENGINE_ON,v,true,true,false);Function.Call(Hash.SET_VEHICLE_SIREN,v,level<6);
                int driver=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,v,6,pedModel,-1,false,false);
                if(driver!=0)
                {
                    unit.PedHandles.Add(driver);SetupResponsePed(driver,lethal);Function.Call(Hash.SET_DRIVER_ABILITY,driver,1f);Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS,driver,level>=5?1f:(level==3?0.45f:0.78f));
                    if(tank)Function.Call(Hash.TASK_COMBAT_PED,driver,player.Handle,0,16);else Function.Call(Hash.TASK_VEHICLE_CHASE,driver,player.Handle);
                }
                for(int seat=0;seat<passengers;seat++)
                {
                    int ped=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,v,6,pedModel,seat,false,false);if(ped==0)continue;
                    unit.PedHandles.Add(ped);SetupResponsePed(ped,lethal);
                    if(lethal)Function.Call(Hash.TASK_COMBAT_PED,ped,player.Handle,0,16);
                    else Function.Call(Hash.TASK_LOOK_AT_ENTITY,ped,player.Handle,6000,0,2);
                }
                _units.Add(unit);if(log!=null)log("Custom dispatch spawned "+vehicleName+" response unit for tier "+level+(lethal?" lethal-ready.":" non-lethal interception."));return true;
            }
            catch(Exception ex){if(log!=null)log("Custom ground dispatch failed: "+ex.Message);CleanupUnit(unit);return false;}
            finally{Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED,vehicleModel);Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED,pedModel);}
        }

        private bool SpawnAttackHelicopter(Ped player,Action<string> log)
        {
            int vm=Function.Call<int>(Hash.GET_HASH_KEY,"savage"),pm=Function.Call<int>(Hash.GET_HASH_KEY,"s_m_y_marine_01");if(!EnsureModel(vm)||!EnsureModel(pm))return false;
            Vector3 p=player.Position;double a=(Game.GameTime%6283)/1000.0;Vector3 spawn=new Vector3(p.X+(float)Math.Cos(a)*420f,p.Y+(float)Math.Sin(a)*420f,p.Z+150f);var unit=new CustomUnit{RequiredLevel=6,CreatedAt=Game.GameTime};
            try
            {
                int heli=Function.Call<int>(Hash.CREATE_VEHICLE,vm,spawn.X,spawn.Y,spawn.Z,HeadingTo(spawn,p),false,false);if(heli==0)return false;unit.VehicleHandle=heli;Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY,heli,true,true);Function.Call(Hash.SET_VEHICLE_ENGINE_ON,heli,true,true,false);Function.Call(Hash.SET_HELI_BLADES_FULL_SPEED,heli);
                int pilot=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,heli,6,pm,-1,false,false);if(pilot!=0){unit.PedHandles.Add(pilot);SetupResponsePed(pilot,true);Function.Call(Hash.TASK_HELI_CHASE,pilot,player.Handle,0f,0f,0f);}
                for(int seat=0;seat<=1;seat++){int gunner=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,heli,6,pm,seat,false,false);if(gunner==0)continue;unit.PedHandles.Add(gunner);SetupResponsePed(gunner,true);Function.Call(Hash.TASK_COMBAT_PED,gunner,player.Handle,0,16);}
                _units.Add(unit);if(log!=null)log("Sixth-star attack helicopter deployed.");return true;
            }
            catch(Exception ex){if(log!=null)log("Attack helicopter dispatch failed: "+ex.Message);CleanupUnit(unit);return false;}
            finally{Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED,vm);Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED,pm);}
        }

        private bool SpawnJet(Ped player,Action<string> log)
        {
            int vm=Function.Call<int>(Hash.GET_HASH_KEY,"lazer"),pm=Function.Call<int>(Hash.GET_HASH_KEY,"s_m_y_marine_01");if(!EnsureModel(vm)||!EnsureModel(pm))return false;
            Vector3 p=player.Position,spawn=new Vector3(p.X-800f,p.Y-500f,p.Z+320f);var unit=new CustomUnit{RequiredLevel=6,CreatedAt=Game.GameTime};
            try
            {
                int jet=Function.Call<int>(Hash.CREATE_VEHICLE,vm,spawn.X,spawn.Y,spawn.Z,HeadingTo(spawn,p),false,false);if(jet==0)return false;unit.VehicleHandle=jet;Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY,jet,true,true);Function.Call(Hash.SET_VEHICLE_ENGINE_ON,jet,true,true,false);Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED,jet,65f);
                int pilot=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,jet,6,pm,-1,false,false);if(pilot!=0){unit.PedHandles.Add(pilot);SetupResponsePed(pilot,true);Function.Call(Hash.TASK_COMBAT_PED,pilot,player.Handle,0,16);}
                _units.Add(unit);if(log!=null)log("Sixth-star air-force jet deployed.");return true;
            }
            catch(Exception ex){if(log!=null)log("Jet dispatch failed: "+ex.Message);CleanupUnit(unit);return false;}
            finally{Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED,vm);Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED,pm);}
        }

        private static void SetupResponsePed(int h,bool lethal)
        {
            if(!EntityExists(h))return;
            try
            {
                Function.Call(Hash.SET_PED_AS_COP,h,true);Function.Call(Hash.SET_PED_ACCURACY,h,lethal?48:18);Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES,h,46,true);Function.Call(Hash.SET_PED_COMBAT_ABILITY,h,lethal?2:1);
                int weapon=Function.Call<int>(Hash.GET_HASH_KEY,lethal?"WEAPON_CARBINERIFLE":"WEAPON_STUNGUN");Function.Call(Hash.GIVE_WEAPON_TO_PED,h,weapon,lethal?300:20,false,true);
            }
            catch{}
        }

        private static bool IsRural(Vector3 p)
        {
            try
            {
                string z=(Function.Call<string>(Hash.GET_NAME_OF_ZONE,p.X,p.Y,p.Z)??string.Empty).ToUpperInvariant();
                switch(z){case "SANDY":case "GRAPES":case "PALETO":case "DESRT":case "ALAMO":case "ZANCUDO":case "HARMO":case "GREATC":case "MTCHIL":case "MTGORDO":case "MTJOSE":return true;default:return false;}
            }
            catch{return false;}
        }

        private static bool EnsureModel(int h){if(h==0||!Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE,h)||!Function.Call<bool>(Hash.IS_MODEL_VALID,h))return false;Function.Call(Hash.REQUEST_MODEL,h);return Function.Call<bool>(Hash.HAS_MODEL_LOADED,h);}
        private static Vector3 FindGroundSpawn(Ped player,float distance){double a=((Game.GameTime/137)%6283)/1000.0;Vector3 p=player.Position,seed=new Vector3(p.X+(float)Math.Cos(a)*distance,p.Y+(float)Math.Sin(a)*distance,p.Z);try{Vector3 street=World.GetNextPositionOnStreet(seed);if(street!=Vector3.Zero)return street;}catch{}return seed;}
        private static float HeadingTo(Vector3 from,Vector3 to){try{return Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D,to.X-from.X,to.Y-from.Y);}catch{return 0f;}}
        private void CleanupInvalidUnits(Ped player,int level){for(int i=_units.Count-1;i>=0;i--){CustomUnit u=_units[i];bool remove=level<=0||level<u.RequiredLevel||!EntityExists(u.VehicleHandle);if(!remove&&player!=null&&player.Exists()){Vector3 pos=GetEntityPosition(u.VehicleHandle);if(pos!=Vector3.Zero&&Perception.Distance(pos,player.Position)>2200f)remove=true;}if(!remove&&Game.GameTime-u.CreatedAt>480000)remove=true;if(remove){CleanupUnit(u);_units.RemoveAt(i);}}}
        private static void CleanupUnit(CustomUnit u){if(u==null)return;foreach(int p in u.PedHandles)DeleteEntity(p);DeleteEntity(u.VehicleHandle);}
        private static bool EntityExists(int h){return h!=0&&Function.Call<bool>(Hash.DOES_ENTITY_EXIST,h);}
        private static Vector3 GetEntityPosition(int h){if(!EntityExists(h))return Vector3.Zero;try{return Function.Call<Vector3>(Hash.GET_ENTITY_COORDS,h,true);}catch{return Vector3.Zero;}}
        private static void DeleteEntity(int h){if(!EntityExists(h))return;try{Entity e=Entity.FromHandle(h);if(e!=null&&e.Exists())e.Delete();}catch{}}
        private static bool SafeBool(Hash h,params InputArgument[] a){try{return Function.Call<bool>(h,a);}catch{return false;}}
    }
}
