using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class Config
    {
        public bool Enabled = true;
        public bool HidePoliceBlips = true;
        public bool MissionSafeMode = true;
        public bool DebugLogging = true;
        public bool PersistenceEnabled = true;
        public int PersistenceSaveIntervalMs = 10000;
        public int PostMissionGraceMs = 5000;
        public int MissionFlagConfirmMs = 1800;
        public int MissionFlagExitHoldMs = 2500;
        public int ScriptTickIntervalMs = 25;

        public bool InterceptUnwitnessedWanted = true;
        public float CivilianWitnessDistance = 50f;
        public float PoliceWitnessDistance = 95f;
        public int CivilianReportDelayMs = 4500;
        public int PoliceConfirmDelayMs = 250;
        public int PendingIncidentTimeoutMs = 12000;
        public int PendingWitnessScanIntervalMs = 250;

        public float FaceRecognitionDistance = 20f;
        public float OutfitRecognitionDistance = 35f;
        public float VehicleRecognitionDistance = 70f;
        public float PlateRecognitionDistance = 48f;
        public float WeaponRecognitionDistance = 40f;
        public float SuspectCountRecognitionDistance = 55f;
        public int ReacquireCooldownMs = 6500;
        public int CaseMemoryHours = 12;
        public bool PoliceANPR = true;

        public bool CctvEnabled = true;
        public float CctvScanRadius = 85f;
        public float CctvFovDegrees = 72f;
        public int CctvCrimeReportDelayMs = 9000;
        public int CctvReacquireDelayMs = 1800;
        public bool CctvCanDispatch = true;

        public bool TrackersEnabled = true;
        public bool PoliceVehiclesAlwaysTracked = true;
        public int PremiumVehicleTrackerChance = 70;
        public int RegularVehicleTrackerChance = 28;
        public int TrackerPingIntervalMs = 4500;
        public int TrackerReacquireDelayMs = 5500;

        public bool TrafficEnforcementEnabled = true;
        public bool TrafficCameraEnforcement = true;
        public int UrbanSpeedLimitKph = 80;
        public int HighwaySpeedLimitKph = 121;
        public int SpeedToleranceKph = 10;
        public int SpeedingGraceMs = 2500;
        public int CitationCooldownMs = 30000;
        public int SpeedingBaseFine = 50;
        public int SpeedingFinePerKph = 8;
        public bool AutoDeductFines = true;
        public int FineDeliveryDelayMs = 30000;
        public bool PoliceObservedSpeedingCanEscalate = true;
        public int PoliceSpeedingReportDelayMs = 3500;

        public bool WarrantsEnabled = true;
        public int WarrantMinimumThreat = 3;
        public int WarrantMemoryHours = 48;
        public bool HomeSurveillanceEnabled = true;
        public float HomeSurveillanceActivationRadius = 190f;
        public int HomeSurveillanceRespawnCooldownMs = 90000;

        public bool ProportionalForceEnabled = true;
        public int LethalForceMinimumWanted = 3;
        public int PitMinimumWanted = 2;
        public float PitMinimumSpeedKph = 75f;
        public float ForcePolicyRadius = 135f;
        public int ForcePolicyScanIntervalMs = 250;
        public int NonLethalAccuracy = 12;
        public int NonLethalShootRate = 28;

        public bool SearchHudEnabled = true;
        public bool ShowSearchCircles = true;
        public bool ShowEvidenceIcons = true;
        public float SearchInnerBaseRadius = 120f;
        public float SearchRadiusPerStar = 55f;
        public float SearchOuterExtraRadius = 110f;
        public int SearchInnerAlpha = 72;
        public int SearchOuterAlpha = 34;
        public int SearchCircleObservationGraceMs = 1600;
        public float EvidenceIconSize = 27f;

        public bool EnableSixthStar = true;
        public int SixStarAfterFiveStarSeconds = 50;
        public int SixStarHeatThreshold = 3;
        public int FiveStarShootingHeatIntervalMs = 12000;
        public bool CustomDispatchEnabled = true;
        public int DispatchSupportIntervalMs = 22000;
        public int SixStarHeavyIntervalMs = 30000;
        public int MaxCustomUnits = 6;
        public bool SixStarMilitaryGround = true;
        public bool SixStarAttackHelicopter = true;
        public bool SixStarJet = true;

        public static Config Load(string path)
        {
            var cfg = new Config();
            if (!File.Exists(path)) return cfg;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('='); if (eq <= 0) continue;
                values[section + "." + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            cfg.Enabled=GetBool(values,"General.Enabled",cfg.Enabled); cfg.HidePoliceBlips=GetBool(values,"General.HidePoliceBlips",cfg.HidePoliceBlips); cfg.MissionSafeMode=GetBool(values,"General.MissionSafeMode",cfg.MissionSafeMode); cfg.DebugLogging=GetBool(values,"General.DebugLogging",cfg.DebugLogging); cfg.PersistenceEnabled=GetBool(values,"General.PersistenceEnabled",cfg.PersistenceEnabled); cfg.PersistenceSaveIntervalMs=GetInt(values,"General.PersistenceSaveIntervalMs",cfg.PersistenceSaveIntervalMs); cfg.PostMissionGraceMs=GetInt(values,"General.PostMissionGraceMs",cfg.PostMissionGraceMs); cfg.MissionFlagConfirmMs=GetInt(values,"General.MissionFlagConfirmMs",cfg.MissionFlagConfirmMs); cfg.MissionFlagExitHoldMs=GetInt(values,"General.MissionFlagExitHoldMs",cfg.MissionFlagExitHoldMs); cfg.ScriptTickIntervalMs=GetInt(values,"General.ScriptTickIntervalMs",cfg.ScriptTickIntervalMs);
            cfg.InterceptUnwitnessedWanted=GetBool(values,"Witnesses.InterceptUnwitnessedWanted",cfg.InterceptUnwitnessedWanted); cfg.CivilianWitnessDistance=GetFloat(values,"Witnesses.CivilianWitnessDistance",cfg.CivilianWitnessDistance); cfg.PoliceWitnessDistance=GetFloat(values,"Witnesses.PoliceWitnessDistance",cfg.PoliceWitnessDistance); cfg.CivilianReportDelayMs=GetInt(values,"Witnesses.CivilianReportDelayMs",cfg.CivilianReportDelayMs); cfg.PoliceConfirmDelayMs=GetInt(values,"Witnesses.PoliceConfirmDelayMs",cfg.PoliceConfirmDelayMs); cfg.PendingIncidentTimeoutMs=GetInt(values,"Witnesses.PendingIncidentTimeoutMs",cfg.PendingIncidentTimeoutMs); cfg.PendingWitnessScanIntervalMs=GetInt(values,"Witnesses.ScanIntervalMs",cfg.PendingWitnessScanIntervalMs);
            cfg.FaceRecognitionDistance=GetFloat(values,"Identification.FaceRecognitionDistance",cfg.FaceRecognitionDistance); cfg.OutfitRecognitionDistance=GetFloat(values,"Identification.OutfitRecognitionDistance",cfg.OutfitRecognitionDistance); cfg.VehicleRecognitionDistance=GetFloat(values,"Identification.VehicleRecognitionDistance",cfg.VehicleRecognitionDistance); cfg.PlateRecognitionDistance=GetFloat(values,"Identification.PlateRecognitionDistance",cfg.PlateRecognitionDistance); cfg.WeaponRecognitionDistance=GetFloat(values,"Identification.WeaponRecognitionDistance",cfg.WeaponRecognitionDistance); cfg.SuspectCountRecognitionDistance=GetFloat(values,"Identification.SuspectCountRecognitionDistance",cfg.SuspectCountRecognitionDistance); cfg.ReacquireCooldownMs=GetInt(values,"Identification.ReacquireCooldownMs",cfg.ReacquireCooldownMs); cfg.CaseMemoryHours=GetInt(values,"Identification.CaseMemoryHours",cfg.CaseMemoryHours); cfg.PoliceANPR=GetBool(values,"Identification.PoliceANPR",cfg.PoliceANPR);
            cfg.CctvEnabled=GetBool(values,"CCTV.Enabled",cfg.CctvEnabled); cfg.CctvScanRadius=GetFloat(values,"CCTV.ScanRadius",cfg.CctvScanRadius); cfg.CctvFovDegrees=GetFloat(values,"CCTV.FovDegrees",cfg.CctvFovDegrees); cfg.CctvCrimeReportDelayMs=GetInt(values,"CCTV.CrimeReportDelayMs",cfg.CctvCrimeReportDelayMs); cfg.CctvReacquireDelayMs=GetInt(values,"CCTV.ReacquireDelayMs",cfg.CctvReacquireDelayMs); cfg.CctvCanDispatch=GetBool(values,"CCTV.CanDispatch",cfg.CctvCanDispatch);
            cfg.TrackersEnabled=GetBool(values,"Trackers.Enabled",cfg.TrackersEnabled); cfg.PoliceVehiclesAlwaysTracked=GetBool(values,"Trackers.PoliceVehiclesAlwaysTracked",cfg.PoliceVehiclesAlwaysTracked); cfg.PremiumVehicleTrackerChance=GetInt(values,"Trackers.PremiumVehicleTrackerChance",cfg.PremiumVehicleTrackerChance); cfg.RegularVehicleTrackerChance=GetInt(values,"Trackers.RegularVehicleTrackerChance",cfg.RegularVehicleTrackerChance); cfg.TrackerPingIntervalMs=GetInt(values,"Trackers.TrackerPingIntervalMs",cfg.TrackerPingIntervalMs); cfg.TrackerReacquireDelayMs=GetInt(values,"Trackers.TrackerReacquireDelayMs",cfg.TrackerReacquireDelayMs);
            cfg.TrafficEnforcementEnabled=GetBool(values,"Traffic.Enabled",cfg.TrafficEnforcementEnabled); cfg.TrafficCameraEnforcement=GetBool(values,"Traffic.CameraEnforcement",cfg.TrafficCameraEnforcement); cfg.UrbanSpeedLimitKph=GetInt(values,"Traffic.UrbanSpeedLimitKph",cfg.UrbanSpeedLimitKph); cfg.HighwaySpeedLimitKph=GetInt(values,"Traffic.HighwaySpeedLimitKph",cfg.HighwaySpeedLimitKph); cfg.SpeedToleranceKph=GetInt(values,"Traffic.SpeedToleranceKph",cfg.SpeedToleranceKph); cfg.SpeedingGraceMs=GetInt(values,"Traffic.SpeedingGraceMs",cfg.SpeedingGraceMs); cfg.CitationCooldownMs=GetInt(values,"Traffic.CitationCooldownMs",cfg.CitationCooldownMs); cfg.SpeedingBaseFine=GetInt(values,"Traffic.SpeedingBaseFine",cfg.SpeedingBaseFine); cfg.SpeedingFinePerKph=GetInt(values,"Traffic.SpeedingFinePerKph",cfg.SpeedingFinePerKph); cfg.AutoDeductFines=GetBool(values,"Traffic.AutoDeductFines",cfg.AutoDeductFines); cfg.FineDeliveryDelayMs=GetInt(values,"Traffic.FineDeliveryDelayMs",cfg.FineDeliveryDelayMs); cfg.PoliceObservedSpeedingCanEscalate=GetBool(values,"Traffic.PoliceObservedSpeedingCanEscalate",cfg.PoliceObservedSpeedingCanEscalate); cfg.PoliceSpeedingReportDelayMs=GetInt(values,"Traffic.PoliceSpeedingReportDelayMs",cfg.PoliceSpeedingReportDelayMs);
            cfg.WarrantsEnabled=GetBool(values,"Warrants.Enabled",cfg.WarrantsEnabled); cfg.WarrantMinimumThreat=GetInt(values,"Warrants.MinimumThreat",cfg.WarrantMinimumThreat); cfg.WarrantMemoryHours=GetInt(values,"Warrants.MemoryHours",cfg.WarrantMemoryHours); cfg.HomeSurveillanceEnabled=GetBool(values,"Warrants.HomeSurveillanceEnabled",cfg.HomeSurveillanceEnabled); cfg.HomeSurveillanceActivationRadius=GetFloat(values,"Warrants.HomeSurveillanceActivationRadius",cfg.HomeSurveillanceActivationRadius); cfg.HomeSurveillanceRespawnCooldownMs=GetInt(values,"Warrants.HomeSurveillanceRespawnCooldownMs",cfg.HomeSurveillanceRespawnCooldownMs);
            cfg.ProportionalForceEnabled=GetBool(values,"Force.ProportionalForceEnabled",cfg.ProportionalForceEnabled); cfg.LethalForceMinimumWanted=GetInt(values,"Force.LethalForceMinimumWanted",cfg.LethalForceMinimumWanted); cfg.PitMinimumWanted=GetInt(values,"Force.PitMinimumWanted",cfg.PitMinimumWanted); cfg.PitMinimumSpeedKph=GetFloat(values,"Force.PitMinimumSpeedKph",cfg.PitMinimumSpeedKph); cfg.ForcePolicyRadius=GetFloat(values,"Force.PolicyRadius",cfg.ForcePolicyRadius); cfg.ForcePolicyScanIntervalMs=GetInt(values,"Force.ScanIntervalMs",cfg.ForcePolicyScanIntervalMs); cfg.NonLethalAccuracy=GetInt(values,"Force.NonLethalAccuracy",cfg.NonLethalAccuracy); cfg.NonLethalShootRate=GetInt(values,"Force.NonLethalShootRate",cfg.NonLethalShootRate);
            cfg.SearchHudEnabled=GetBool(values,"SearchHUD.Enabled",cfg.SearchHudEnabled); cfg.ShowSearchCircles=GetBool(values,"SearchHUD.ShowSearchCircles",cfg.ShowSearchCircles); cfg.ShowEvidenceIcons=GetBool(values,"SearchHUD.ShowEvidenceIcons",cfg.ShowEvidenceIcons); cfg.SearchInnerBaseRadius=GetFloat(values,"SearchHUD.InnerBaseRadius",cfg.SearchInnerBaseRadius); cfg.SearchRadiusPerStar=GetFloat(values,"SearchHUD.RadiusPerStar",cfg.SearchRadiusPerStar); cfg.SearchOuterExtraRadius=GetFloat(values,"SearchHUD.OuterExtraRadius",cfg.SearchOuterExtraRadius); cfg.SearchInnerAlpha=GetInt(values,"SearchHUD.InnerAlpha",cfg.SearchInnerAlpha); cfg.SearchOuterAlpha=GetInt(values,"SearchHUD.OuterAlpha",cfg.SearchOuterAlpha); cfg.SearchCircleObservationGraceMs=GetInt(values,"SearchHUD.ObservationGraceMs",cfg.SearchCircleObservationGraceMs); cfg.EvidenceIconSize=GetFloat(values,"SearchHUD.EvidenceIconSize",cfg.EvidenceIconSize);
            cfg.EnableSixthStar=GetBool(values,"Dispatch.EnableSixthStar",cfg.EnableSixthStar); cfg.SixStarAfterFiveStarSeconds=GetInt(values,"Dispatch.SixStarAfterFiveStarSeconds",cfg.SixStarAfterFiveStarSeconds); cfg.SixStarHeatThreshold=GetInt(values,"Dispatch.SixStarHeatThreshold",cfg.SixStarHeatThreshold); cfg.FiveStarShootingHeatIntervalMs=GetInt(values,"Dispatch.FiveStarShootingHeatIntervalMs",cfg.FiveStarShootingHeatIntervalMs); cfg.CustomDispatchEnabled=GetBool(values,"Dispatch.CustomDispatchEnabled",cfg.CustomDispatchEnabled); cfg.DispatchSupportIntervalMs=GetInt(values,"Dispatch.SupportIntervalMs",cfg.DispatchSupportIntervalMs); cfg.SixStarHeavyIntervalMs=GetInt(values,"Dispatch.SixStarHeavyIntervalMs",cfg.SixStarHeavyIntervalMs); cfg.MaxCustomUnits=GetInt(values,"Dispatch.MaxCustomUnits",cfg.MaxCustomUnits); cfg.SixStarMilitaryGround=GetBool(values,"Dispatch.SixStarMilitaryGround",cfg.SixStarMilitaryGround); cfg.SixStarAttackHelicopter=GetBool(values,"Dispatch.SixStarAttackHelicopter",cfg.SixStarAttackHelicopter); cfg.SixStarJet=GetBool(values,"Dispatch.SixStarJet",cfg.SixStarJet);
            return cfg;
        }

        private static bool GetBool(Dictionary<string,string> v,string key,bool fallback){string s;bool r;return v.TryGetValue(key,out s)&&bool.TryParse(s,out r)?r:fallback;}
        private static int GetInt(Dictionary<string,string> v,string key,int fallback){string s;int r;return v.TryGetValue(key,out s)&&int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out r)?r:fallback;}
        private static float GetFloat(Dictionary<string,string> v,string key,float fallback){string s;float r;return v.TryGetValue(key,out s)&&float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out r)?r:fallback;}
    }
}
