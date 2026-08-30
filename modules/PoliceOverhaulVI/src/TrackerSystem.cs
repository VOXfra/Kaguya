using GTA;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal static class TrackerSystem
    {
        public static void AttachKnowledgeIfApplicable(CaseMemory memory,Ped player,Config cfg){if(!cfg.TrackersEnabled||memory==null||memory.Vehicle==null||player==null||!player.Exists()||!player.IsInVehicle())return;Vehicle vehicle=player.CurrentVehicle;if(vehicle==null||!vehicle.Exists()||!memory.Vehicle.Matches(vehicle,false))return;bool present=DetermineTrackerPresent(vehicle,cfg);memory.Vehicle.TrackerPresent=present;if(present)memory.Vehicle.TrackerKnownByPolice=true;}
        public static bool HasPoliceUsableTracker(CaseMemory memory,Ped player,Config cfg){if(!cfg.TrackersEnabled||memory==null||memory.Vehicle==null||!memory.Vehicle.TrackerKnownByPolice)return false;if(player==null||!player.Exists()||!player.IsInVehicle())return false;Vehicle vehicle=player.CurrentVehicle;return vehicle!=null&&vehicle.Exists()&&memory.Vehicle.Matches(vehicle,true);}
        public static bool DetermineTrackerPresent(Vehicle vehicle,Config cfg){if(vehicle==null||!vehicle.Exists())return false;int vehicleClass=-1;try{vehicleClass=Function.Call<int>(Hash.GET_VEHICLE_CLASS,vehicle.Handle);}catch{}if(cfg.PoliceVehiclesAlwaysTracked&&vehicleClass==18)return true;int chance=IsPremiumClass(vehicleClass)?cfg.PremiumVehicleTrackerChance:cfg.RegularVehicleTrackerChance;chance=Math.Max(0,Math.Min(100,chance));if(chance<=0)return false;string plate=VehicleSignature.NormalizePlate(Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT,vehicle.Handle));unchecked{int seed=vehicle.Model.Hash*397;for(int i=0;i<plate.Length;i++)seed=(seed*31)^plate[i];int roll=Math.Abs(seed%100);return roll<chance;}}
        private static bool IsPremiumClass(int vehicleClass){return vehicleClass==3||vehicleClass==5||vehicleClass==6||vehicleClass==7||vehicleClass==22;}
    }
}
