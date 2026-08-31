using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class Config
    {
        public bool Enabled=true,HidePoliceBlips=true,MissionSafeMode=true,DebugLogging=true,PersistenceEnabled=true;
        public int PersistenceSaveIntervalMs=10000,PostMissionGraceMs=5000,MissionFlagConfirmMs=1800,MissionFlagExitHoldMs=2500,ScriptTickIntervalMs=25;

        public bool InterceptUnwitnessedWanted=true;
        public float CivilianWitnessDistance=50f,PoliceWitnessDistance=95f;
        public int CivilianReportDelayMs=4500,PoliceConfirmDelayMs=250,PendingIncidentTimeoutMs=12000,PendingWitnessScanIntervalMs=250;

        public float FaceRecognitionDistance=20f,OutfitRecognitionDistance=35f,VehicleRecognitionDistance=70f,PlateRecognitionDistance=48f,WeaponRecognitionDistance=40f,SuspectCountRecognitionDistance=55f;
        public int ReacquireCooldownMs=6500,CaseMemoryHours=12;
        public bool PoliceANPR=true;
        public float FaceKnownThreshold=68f,IdentityConfirmationThreshold=82f,MostWantedIdentityThreshold=64f,MostWantedNotorietyThreshold=82f,MaskedFaceConfidenceCap=12f;
        public int IdentityMinConfirmationMs=650,IdentityMaxConfirmationMs=4200;

        public bool CctvEnabled=true,CctvCanDispatch=true;
        public float CctvScanRadius=85f,CctvFovDegrees=72f;
        public int CctvCrimeReportDelayMs=9000,CctvReacquireDelayMs=1800;

        public bool TrackersEnabled=true,PoliceVehiclesAlwaysTracked=true;
        public int PremiumVehicleTrackerChance=70,RegularVehicleTrackerChance=28,TrackerPingIntervalMs=4500,TrackerReacquireDelayMs=5500;

        public bool TrafficEnforcementEnabled=true,TrafficCameraEnforcement=true,AutoDeductFines=true,PoliceObservedSpeedingCanEscalate=true;
        public int UrbanSpeedLimitKph=80,HighwaySpeedLimitKph=121,SpeedToleranceKph=10,SpeedingGraceMs=2500,CitationCooldownMs=30000;
        public int SpeedingBaseFine=50,SpeedingFinePerKph=8,FineDeliveryDelayMs=30000,PoliceSpeedingReportDelayMs=3500;
        public int RecklessSpeedOverKph=45,RecklessFineBonus=250;
        public bool FineMailEnabled=true,FineMailArchiveEnabled=true,FineMailSound=true;
        public string FineMailSender="LSPD Traffic Division";
        public float FixedRadarRange=70f,FixedRadarFovDegrees=105f;
        public int FixedRadarDiscoveryIntervalMs=1800;

        public bool WarrantsEnabled=true,HomeSurveillanceEnabled=true;
        public int WarrantMinimumThreat=3,WarrantMemoryHours=48,HomeSurveillanceRespawnCooldownMs=90000;
        public float HomeSurveillanceActivationRadius=190f;

        public bool StationSurrenderEnabled=true;
        public float StationSurrenderRadius=7.5f,SurrenderNotorietyReduction=3f;
        public int StationSurrenderHoldMs=1600;

        public bool StoryNotorietyEnabled=true;
        public int StoryHeistMinimumActiveMs=60000;
        public float StoryNotorietyMultiplier=1f;

        public bool ProportionalForceEnabled=true,LethalForceRequiresCurrentThreat=true,PitRequiresFleeing=true,CivilianRiskEnabled=true;
        public int LethalForceMinimumWanted=3,LethalArmedEscalationWanted=4,PitMinimumWanted=2;
        public float PitMinimumSpeedKph=75f,ForcePolicyRadius=135f;
        public int ForcePolicyScanIntervalMs=250,NonLethalAccuracy=12,NonLethalShootRate=28;
        public float CivilianRiskRadius=28f,CivilianRiskLethalThreshold=34f,CivilianRiskEmergencyThreshold=68f;
        public float PitRiskRadius=30f,PitCivilianClearance=13f,PitVehicleClearance=11f,PitRiskThreshold=34f,PitEmergencyRiskThreshold=65f;
        public float MilitaryRiskThreshold=22f,MilitaryRiskEmergencyThreshold=52f;

        public bool SearchHudEnabled=true,ShowSearchCircles=true,ShowEvidenceIcons=true;
        public float SearchInnerBaseRadius=120f,SearchRadiusPerStar=55f,SearchOuterExtraRadius=110f,SearchUncertaintyGrowthPerSecond=3.5f,SearchMaxGrowth=170f;
        public int SearchInnerAlpha=72,SearchOuterAlpha=34,SearchCircleObservationGraceMs=1600,SearchLostContactDelayMs=2600,SearchPhaseLifetimeMs=60000;
        public float EvidenceIconSize=27f;

        public bool EnableSixthStar=true,CustomDispatchEnabled=true,SixStarMilitaryGround=true,SixStarAttackHelicopter=true,SixStarJet=true,OnlinePoliceVehicles=true;
        public int SixStarAfterFiveStarSeconds=50,SixStarHeatThreshold=3,FiveStarShootingHeatIntervalMs=12000,DispatchSupportIntervalMs=22000,SixStarHeavyIntervalMs=30000,MaxCustomUnits=6;

        public static Config Load(string path)
        {
            var cfg=new Config(); if(!File.Exists(path))return cfg;
            var v=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); string section=string.Empty;
            foreach(string raw in File.ReadAllLines(path))
            {
                string line=raw.Trim(); if(line.Length==0||line.StartsWith(";")||line.StartsWith("#"))continue;
                if(line.StartsWith("[")&&line.EndsWith("]")){section=line.Substring(1,line.Length-2).Trim();continue;}
                int eq=line.IndexOf('='); if(eq<=0)continue; v[section+"."+line.Substring(0,eq).Trim()]=line.Substring(eq+1).Trim();
            }
            cfg.Enabled=B(v,"General.Enabled",cfg.Enabled);cfg.HidePoliceBlips=B(v,"General.HidePoliceBlips",cfg.HidePoliceBlips);cfg.MissionSafeMode=B(v,"General.MissionSafeMode",cfg.MissionSafeMode);cfg.DebugLogging=B(v,"General.DebugLogging",cfg.DebugLogging);cfg.PersistenceEnabled=B(v,"General.PersistenceEnabled",cfg.PersistenceEnabled);cfg.PersistenceSaveIntervalMs=I(v,"General.PersistenceSaveIntervalMs",cfg.PersistenceSaveIntervalMs);cfg.PostMissionGraceMs=I(v,"General.PostMissionGraceMs",cfg.PostMissionGraceMs);cfg.MissionFlagConfirmMs=I(v,"General.MissionFlagConfirmMs",cfg.MissionFlagConfirmMs);cfg.MissionFlagExitHoldMs=I(v,"General.MissionFlagExitHoldMs",cfg.MissionFlagExitHoldMs);cfg.ScriptTickIntervalMs=I(v,"General.ScriptTickIntervalMs",cfg.ScriptTickIntervalMs);
            cfg.InterceptUnwitnessedWanted=B(v,"Witnesses.InterceptUnwitnessedWanted",cfg.InterceptUnwitnessedWanted);cfg.CivilianWitnessDistance=F(v,"Witnesses.CivilianWitnessDistance",cfg.CivilianWitnessDistance);cfg.PoliceWitnessDistance=F(v,"Witnesses.PoliceWitnessDistance",cfg.PoliceWitnessDistance);cfg.CivilianReportDelayMs=I(v,"Witnesses.CivilianReportDelayMs",cfg.CivilianReportDelayMs);cfg.PoliceConfirmDelayMs=I(v,"Witnesses.PoliceConfirmDelayMs",cfg.PoliceConfirmDelayMs);cfg.PendingIncidentTimeoutMs=I(v,"Witnesses.PendingIncidentTimeoutMs",cfg.PendingIncidentTimeoutMs);cfg.PendingWitnessScanIntervalMs=I(v,"Witnesses.ScanIntervalMs",cfg.PendingWitnessScanIntervalMs);
            cfg.FaceRecognitionDistance=F(v,"Identification.FaceRecognitionDistance",cfg.FaceRecognitionDistance);cfg.OutfitRecognitionDistance=F(v,"Identification.OutfitRecognitionDistance",cfg.OutfitRecognitionDistance);cfg.VehicleRecognitionDistance=F(v,"Identification.VehicleRecognitionDistance",cfg.VehicleRecognitionDistance);cfg.PlateRecognitionDistance=F(v,"Identification.PlateRecognitionDistance",cfg.PlateRecognitionDistance);cfg.WeaponRecognitionDistance=F(v,"Identification.WeaponRecognitionDistance",cfg.WeaponRecognitionDistance);cfg.SuspectCountRecognitionDistance=F(v,"Identification.SuspectCountRecognitionDistance",cfg.SuspectCountRecognitionDistance);cfg.ReacquireCooldownMs=I(v,"Identification.ReacquireCooldownMs",cfg.ReacquireCooldownMs);cfg.CaseMemoryHours=I(v,"Identification.CaseMemoryHours",cfg.CaseMemoryHours);cfg.PoliceANPR=B(v,"Identification.PoliceANPR",cfg.PoliceANPR);cfg.FaceKnownThreshold=F(v,"Identification.FaceKnownThreshold",cfg.FaceKnownThreshold);cfg.IdentityConfirmationThreshold=F(v,"Identification.IdentityConfirmationThreshold",cfg.IdentityConfirmationThreshold);cfg.MostWantedIdentityThreshold=F(v,"Identification.MostWantedIdentityThreshold",cfg.MostWantedIdentityThreshold);cfg.MostWantedNotorietyThreshold=F(v,"Identification.MostWantedNotorietyThreshold",cfg.MostWantedNotorietyThreshold);cfg.MaskedFaceConfidenceCap=F(v,"Identification.MaskedFaceConfidenceCap",cfg.MaskedFaceConfidenceCap);cfg.IdentityMinConfirmationMs=I(v,"Identification.MinConfirmationMs",cfg.IdentityMinConfirmationMs);cfg.IdentityMaxConfirmationMs=I(v,"Identification.MaxConfirmationMs",cfg.IdentityMaxConfirmationMs);
            cfg.CctvEnabled=B(v,"CCTV.Enabled",cfg.CctvEnabled);cfg.CctvScanRadius=F(v,"CCTV.ScanRadius",cfg.CctvScanRadius);cfg.CctvFovDegrees=F(v,"CCTV.FovDegrees",cfg.CctvFovDegrees);cfg.CctvCrimeReportDelayMs=I(v,"CCTV.CrimeReportDelayMs",cfg.CctvCrimeReportDelayMs);cfg.CctvReacquireDelayMs=I(v,"CCTV.ReacquireDelayMs",cfg.CctvReacquireDelayMs);cfg.CctvCanDispatch=B(v,"CCTV.CanDispatch",cfg.CctvCanDispatch);
            cfg.TrackersEnabled=B(v,"Trackers.Enabled",cfg.TrackersEnabled);cfg.PoliceVehiclesAlwaysTracked=B(v,"Trackers.PoliceVehiclesAlwaysTracked",cfg.PoliceVehiclesAlwaysTracked);cfg.PremiumVehicleTrackerChance=I(v,"Trackers.PremiumVehicleTrackerChance",cfg.PremiumVehicleTrackerChance);cfg.RegularVehicleTrackerChance=I(v,"Trackers.RegularVehicleTrackerChance",cfg.RegularVehicleTrackerChance);cfg.TrackerPingIntervalMs=I(v,"Trackers.TrackerPingIntervalMs",cfg.TrackerPingIntervalMs);cfg.TrackerReacquireDelayMs=I(v,"Trackers.TrackerReacquireDelayMs",cfg.TrackerReacquireDelayMs);
            cfg.TrafficEnforcementEnabled=B(v,"Traffic.Enabled",cfg.TrafficEnforcementEnabled);cfg.TrafficCameraEnforcement=B(v,"Traffic.CameraEnforcement",cfg.TrafficCameraEnforcement);cfg.UrbanSpeedLimitKph=I(v,"Traffic.UrbanSpeedLimitKph",cfg.UrbanSpeedLimitKph);cfg.HighwaySpeedLimitKph=I(v,"Traffic.HighwaySpeedLimitKph",cfg.HighwaySpeedLimitKph);cfg.SpeedToleranceKph=I(v,"Traffic.SpeedToleranceKph",cfg.SpeedToleranceKph);cfg.SpeedingGraceMs=I(v,"Traffic.SpeedingGraceMs",cfg.SpeedingGraceMs);cfg.CitationCooldownMs=I(v,"Traffic.CitationCooldownMs",cfg.CitationCooldownMs);cfg.SpeedingBaseFine=I(v,"Traffic.SpeedingBaseFine",cfg.SpeedingBaseFine);cfg.SpeedingFinePerKph=I(v,"Traffic.SpeedingFinePerKph",cfg.SpeedingFinePerKph);cfg.AutoDeductFines=B(v,"Traffic.AutoDeductFines",cfg.AutoDeductFines);cfg.FineDeliveryDelayMs=I(v,"Traffic.FineDeliveryDelayMs",cfg.FineDeliveryDelayMs);cfg.PoliceObservedSpeedingCanEscalate=B(v,"Traffic.PoliceObservedSpeedingCanEscalate",cfg.PoliceObservedSpeedingCanEscalate);cfg.PoliceSpeedingReportDelayMs=I(v,"Traffic.PoliceSpeedingReportDelayMs",cfg.PoliceSpeedingReportDelayMs);cfg.RecklessSpeedOverKph=I(v,"Traffic.RecklessSpeedOverKph",cfg.RecklessSpeedOverKph);cfg.RecklessFineBonus=I(v,"Traffic.RecklessFineBonus",cfg.RecklessFineBonus);cfg.FineMailEnabled=B(v,"Traffic.FineMailEnabled",cfg.FineMailEnabled);cfg.FineMailArchiveEnabled=B(v,"Traffic.FineMailArchiveEnabled",cfg.FineMailArchiveEnabled);cfg.FineMailSound=B(v,"Traffic.FineMailSound",cfg.FineMailSound);cfg.FineMailSender=S(v,"Traffic.FineMailSender",cfg.FineMailSender);cfg.FixedRadarRange=F(v,"Traffic.FixedRadarRange",cfg.FixedRadarRange);cfg.FixedRadarFovDegrees=F(v,"Traffic.FixedRadarFovDegrees",cfg.FixedRadarFovDegrees);cfg.FixedRadarDiscoveryIntervalMs=I(v,"Traffic.FixedRadarDiscoveryIntervalMs",cfg.FixedRadarDiscoveryIntervalMs);
            cfg.WarrantsEnabled=B(v,"Warrants.Enabled",cfg.WarrantsEnabled);cfg.WarrantMinimumThreat=I(v,"Warrants.MinimumThreat",cfg.WarrantMinimumThreat);cfg.WarrantMemoryHours=I(v,"Warrants.MemoryHours",cfg.WarrantMemoryHours);cfg.HomeSurveillanceEnabled=B(v,"Warrants.HomeSurveillanceEnabled",cfg.HomeSurveillanceEnabled);cfg.HomeSurveillanceActivationRadius=F(v,"Warrants.HomeSurveillanceActivationRadius",cfg.HomeSurveillanceActivationRadius);cfg.HomeSurveillanceRespawnCooldownMs=I(v,"Warrants.HomeSurveillanceRespawnCooldownMs",cfg.HomeSurveillanceRespawnCooldownMs);
            cfg.StationSurrenderEnabled=B(v,"Surrender.Enabled",cfg.StationSurrenderEnabled);cfg.StationSurrenderRadius=F(v,"Surrender.StationRadius",cfg.StationSurrenderRadius);cfg.StationSurrenderHoldMs=I(v,"Surrender.HoldMs",cfg.StationSurrenderHoldMs);cfg.SurrenderNotorietyReduction=F(v,"Surrender.NotorietyReduction",cfg.SurrenderNotorietyReduction);
            cfg.StoryNotorietyEnabled=B(v,"StoryNotoriety.Enabled",cfg.StoryNotorietyEnabled);cfg.StoryHeistMinimumActiveMs=I(v,"StoryNotoriety.HeistMinimumActiveMs",cfg.StoryHeistMinimumActiveMs);cfg.StoryNotorietyMultiplier=F(v,"StoryNotoriety.Multiplier",cfg.StoryNotorietyMultiplier);
            cfg.ProportionalForceEnabled=B(v,"Force.ProportionalForceEnabled",cfg.ProportionalForceEnabled);cfg.LethalForceMinimumWanted=I(v,"Force.LethalForceMinimumWanted",cfg.LethalForceMinimumWanted);cfg.LethalArmedEscalationWanted=I(v,"Force.LethalArmedEscalationWanted",cfg.LethalArmedEscalationWanted);cfg.LethalForceRequiresCurrentThreat=B(v,"Force.LethalForceRequiresCurrentThreat",cfg.LethalForceRequiresCurrentThreat);cfg.PitMinimumWanted=I(v,"Force.PitMinimumWanted",cfg.PitMinimumWanted);cfg.PitMinimumSpeedKph=F(v,"Force.PitMinimumSpeedKph",cfg.PitMinimumSpeedKph);cfg.PitRequiresFleeing=B(v,"Force.PitRequiresFleeing",cfg.PitRequiresFleeing);cfg.ForcePolicyRadius=F(v,"Force.PolicyRadius",cfg.ForcePolicyRadius);cfg.ForcePolicyScanIntervalMs=I(v,"Force.ScanIntervalMs",cfg.ForcePolicyScanIntervalMs);cfg.NonLethalAccuracy=I(v,"Force.NonLethalAccuracy",cfg.NonLethalAccuracy);cfg.NonLethalShootRate=I(v,"Force.NonLethalShootRate",cfg.NonLethalShootRate);cfg.CivilianRiskEnabled=B(v,"Force.CivilianRiskEnabled",cfg.CivilianRiskEnabled);cfg.CivilianRiskRadius=F(v,"Force.CivilianRiskRadius",cfg.CivilianRiskRadius);cfg.CivilianRiskLethalThreshold=F(v,"Force.CivilianRiskLethalThreshold",cfg.CivilianRiskLethalThreshold);cfg.CivilianRiskEmergencyThreshold=F(v,"Force.CivilianRiskEmergencyThreshold",cfg.CivilianRiskEmergencyThreshold);cfg.PitRiskRadius=F(v,"Force.PitRiskRadius",cfg.PitRiskRadius);cfg.PitCivilianClearance=F(v,"Force.PitCivilianClearance",cfg.PitCivilianClearance);cfg.PitVehicleClearance=F(v,"Force.PitVehicleClearance",cfg.PitVehicleClearance);cfg.PitRiskThreshold=F(v,"Force.PitRiskThreshold",cfg.PitRiskThreshold);cfg.PitEmergencyRiskThreshold=F(v,"Force.PitEmergencyRiskThreshold",cfg.PitEmergencyRiskThreshold);cfg.MilitaryRiskThreshold=F(v,"Force.MilitaryRiskThreshold",cfg.MilitaryRiskThreshold);cfg.MilitaryRiskEmergencyThreshold=F(v,"Force.MilitaryRiskEmergencyThreshold",cfg.MilitaryRiskEmergencyThreshold);
            cfg.SearchHudEnabled=B(v,"SearchHUD.Enabled",cfg.SearchHudEnabled);cfg.ShowSearchCircles=B(v,"SearchHUD.ShowSearchCircles",cfg.ShowSearchCircles);cfg.ShowEvidenceIcons=B(v,"SearchHUD.ShowEvidenceIcons",cfg.ShowEvidenceIcons);cfg.SearchInnerBaseRadius=F(v,"SearchHUD.InnerBaseRadius",cfg.SearchInnerBaseRadius);cfg.SearchRadiusPerStar=F(v,"SearchHUD.RadiusPerStar",cfg.SearchRadiusPerStar);cfg.SearchOuterExtraRadius=F(v,"SearchHUD.OuterExtraRadius",cfg.SearchOuterExtraRadius);cfg.SearchInnerAlpha=I(v,"SearchHUD.InnerAlpha",cfg.SearchInnerAlpha);cfg.SearchOuterAlpha=I(v,"SearchHUD.OuterAlpha",cfg.SearchOuterAlpha);cfg.SearchCircleObservationGraceMs=I(v,"SearchHUD.ObservationGraceMs",cfg.SearchCircleObservationGraceMs);cfg.SearchLostContactDelayMs=I(v,"SearchHUD.LostContactDelayMs",cfg.SearchLostContactDelayMs);cfg.SearchPhaseLifetimeMs=I(v,"SearchHUD.SearchPhaseLifetimeMs",cfg.SearchPhaseLifetimeMs);cfg.SearchUncertaintyGrowthPerSecond=F(v,"SearchHUD.UncertaintyGrowthPerSecond",cfg.SearchUncertaintyGrowthPerSecond);cfg.SearchMaxGrowth=F(v,"SearchHUD.MaxGrowth",cfg.SearchMaxGrowth);cfg.EvidenceIconSize=F(v,"SearchHUD.EvidenceIconSize",cfg.EvidenceIconSize);
            cfg.EnableSixthStar=B(v,"Dispatch.EnableSixthStar",cfg.EnableSixthStar);cfg.SixStarAfterFiveStarSeconds=I(v,"Dispatch.SixStarAfterFiveStarSeconds",cfg.SixStarAfterFiveStarSeconds);cfg.SixStarHeatThreshold=I(v,"Dispatch.SixStarHeatThreshold",cfg.SixStarHeatThreshold);cfg.FiveStarShootingHeatIntervalMs=I(v,"Dispatch.FiveStarShootingHeatIntervalMs",cfg.FiveStarShootingHeatIntervalMs);cfg.CustomDispatchEnabled=B(v,"Dispatch.CustomDispatchEnabled",cfg.CustomDispatchEnabled);cfg.DispatchSupportIntervalMs=I(v,"Dispatch.SupportIntervalMs",cfg.DispatchSupportIntervalMs);cfg.SixStarHeavyIntervalMs=I(v,"Dispatch.SixStarHeavyIntervalMs",cfg.SixStarHeavyIntervalMs);cfg.MaxCustomUnits=I(v,"Dispatch.MaxCustomUnits",cfg.MaxCustomUnits);cfg.SixStarMilitaryGround=B(v,"Dispatch.SixStarMilitaryGround",cfg.SixStarMilitaryGround);cfg.SixStarAttackHelicopter=B(v,"Dispatch.SixStarAttackHelicopter",cfg.SixStarAttackHelicopter);cfg.SixStarJet=B(v,"Dispatch.SixStarJet",cfg.SixStarJet);cfg.OnlinePoliceVehicles=B(v,"Dispatch.OnlinePoliceVehicles",cfg.OnlinePoliceVehicles);
            return cfg;
        }
        private static bool B(Dictionary<string,string>v,string k,bool d){string s;bool r;return v.TryGetValue(k,out s)&&bool.TryParse(s,out r)?r:d;}
        private static int I(Dictionary<string,string>v,string k,int d){string s;int r;return v.TryGetValue(k,out s)&&int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out r)?r:d;}
        private static float F(Dictionary<string,string>v,string k,float d){string s;float r;return v.TryGetValue(k,out s)&&float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out r)?r:d;}
        private static string S(Dictionary<string,string>v,string k,string d){string s;return v.TryGetValue(k,out s)&&!string.IsNullOrWhiteSpace(s)?s:d;}
    }
}
