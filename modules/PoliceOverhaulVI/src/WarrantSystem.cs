using GTA;
using GTA.Math;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class WarrantSystem
    {
        private sealed class HomeProfile { public string SuspectModel; public Vector3 Home; public Vector3 Stakeout; public float Heading; }
        private readonly HomeProfile[] _homes = {
            new HomeProfile{SuspectModel="player_zero",Home=new Vector3(-852.4f,160f,65.7f),Stakeout=new Vector3(-834.8f,151.5f,69.7f),Heading=86f},
            new HomeProfile{SuspectModel="player_one",Home=new Vector3(-14.2f,-1442.1f,31.1f),Stakeout=new Vector3(7.5f,-1434f,30.5f),Heading=174f},
            new HomeProfile{SuspectModel="player_one",Home=new Vector3(7.4f,536.8f,176f),Stakeout=new Vector3(-13f,521f,174.6f),Heading=335f},
            new HomeProfile{SuspectModel="player_two",Home=new Vector3(1985.7f,3812.2f,32.2f),Stakeout=new Vector3(1968.5f,3806f,32.1f),Heading=118f}
        };
        private int _vehicleHandle,_driverHandle,_passengerHandle,_lastSpawnAt,_lastDetectionScan;

        public bool Update(Ped player,CaseMemory memory,Config cfg,Action<string> log)
        {
            if(!cfg.WarrantsEnabled||!cfg.HomeSurveillanceEnabled||memory==null||!memory.WarrantActive||!memory.IdentityConfirmed){Cleanup();return false;}
            if(memory.IsWarrantExpiredUtc()){memory.WarrantActive=false;Cleanup();return false;}
            HomeProfile home=FindNearbyHome(player,memory.SuspectModelHash,cfg.HomeSurveillanceActivationRadius);
            if(home==null){if(HasStakeout()&&Perception.Distance(player.Position,GetEntityPosition(_vehicleHandle))>550f)Cleanup();return false;}
            if(!HasStakeout()&&Game.GameTime-_lastSpawnAt>=cfg.HomeSurveillanceRespawnCooldownMs)TrySpawnStakeout(home,log);
            if(!HasStakeout()||Game.GameTime-_lastDetectionScan<350)return false;
            _lastDetectionScan=Game.GameTime;
            int observer=EntityExists(_driverHandle)?_driverHandle:_passengerHandle;
            if(!EntityExists(observer))return false;
            Vector3 observerPos=GetEntityPosition(observer);
            if(Perception.Distance(observerPos,player.Position)>90f||!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,observer,player.Handle,17))return false;

            float confidence=IdentificationSystem.MatchConfidence(player,memory,Perception.Distance(observerPos,player.Position),true,cfg);
            if(!IdentificationSystem.IsConfirmedMatch(confidence,memory,cfg))return false;
            try
            {
                if(EntityExists(_passengerHandle))Function.Call(Hash.TASK_LOOK_AT_ENTITY,_passengerHandle,player.Handle,2500,0,2);
                if(EntityExists(_driverHandle))Function.Call(Hash.TASK_LOOK_AT_ENTITY,_driverHandle,player.Handle,1800,0,2);
            }
            catch { }
            memory.LastKnownPosition=player.Position;memory.LastSource=ObservationSource.HomeSurveillance;memory.LastObservedGameTime=Game.GameTime;
            if(log!=null)log("Home-surveillance unit confirmed warrant match; response authorization deferred to force policy.");
            return true;
        }

        public void Cleanup(){DeleteEntity(ref _driverHandle);DeleteEntity(ref _passengerHandle);DeleteEntity(ref _vehicleHandle);}
        private HomeProfile FindNearbyHome(Ped player,int suspectModelHash,float radius){if(player==null||!player.Exists())return null;foreach(HomeProfile home in _homes){int h=0;try{h=Function.Call<int>(Hash.GET_HASH_KEY,home.SuspectModel);}catch{}if(h==suspectModelHash&&Perception.Distance(player.Position,home.Home)<=radius)return home;}return null;}
        private void TrySpawnStakeout(HomeProfile home,Action<string> log)
        {
            _lastSpawnAt=Game.GameTime;int vehicleModel=Function.Call<int>(Hash.GET_HASH_KEY,"police3"),copModel=Function.Call<int>(Hash.GET_HASH_KEY,"s_m_y_cop_01");Function.Call(Hash.REQUEST_MODEL,vehicleModel);Function.Call(Hash.REQUEST_MODEL,copModel);if(!Function.Call<bool>(Hash.HAS_MODEL_LOADED,vehicleModel)||!Function.Call<bool>(Hash.HAS_MODEL_LOADED,copModel))return;
            try{_vehicleHandle=Function.Call<int>(Hash.CREATE_VEHICLE,vehicleModel,home.Stakeout.X,home.Stakeout.Y,home.Stakeout.Z,home.Heading,false,false);if(_vehicleHandle==0)return;Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY,_vehicleHandle,true,true);Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY,_vehicleHandle);Function.Call(Hash.SET_VEHICLE_ENGINE_ON,_vehicleHandle,false,true,true);Function.Call(Hash.SET_VEHICLE_SIREN,_vehicleHandle,false);Function.Call(Hash.SET_VEHICLE_HANDBRAKE,_vehicleHandle,true);_driverHandle=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,_vehicleHandle,6,copModel,-1,false,false);_passengerHandle=Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,_vehicleHandle,6,copModel,0,false,false);SetupCop(_driverHandle);SetupCop(_passengerHandle);if(log!=null)log("Police stakeout staged near known residence.");}catch(Exception ex){if(log!=null)log("Stakeout spawn failed: "+ex.Message);Cleanup();}finally{Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED,vehicleModel);Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED,copModel);}
        }
        private static void SetupCop(int h){if(!EntityExists(h))return;try{Function.Call(Hash.SET_PED_AS_COP,h,true);Function.Call(Hash.SET_PED_ACCURACY,h,30);int stun=Function.Call<int>(Hash.GET_HASH_KEY,"WEAPON_STUNGUN");Function.Call(Hash.GIVE_WEAPON_TO_PED,h,stun,20,false,true);Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS,h,true);}catch{}}
        private bool HasStakeout(){return EntityExists(_vehicleHandle)&&(EntityExists(_driverHandle)||EntityExists(_passengerHandle));}
        private static bool EntityExists(int h){return h!=0&&Function.Call<bool>(Hash.DOES_ENTITY_EXIST,h);}
        private static Vector3 GetEntityPosition(int h){if(!EntityExists(h))return Vector3.Zero;try{return Function.Call<Vector3>(Hash.GET_ENTITY_COORDS,h,true);}catch{return Vector3.Zero;}}
        private static void DeleteEntity(ref int h){if(h==0)return;try{Entity e=Entity.FromHandle(h);if(e!=null&&e.Exists())e.Delete();}catch{}h=0;}
    }
}
