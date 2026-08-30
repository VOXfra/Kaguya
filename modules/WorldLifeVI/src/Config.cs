using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.WorldLifeVI
{
    internal sealed class Config
    {
        public bool Enabled = true;
        public bool DebugLogging = true;
        public bool DisableDuringMissions = true;
        public int ContextRefreshMs = 1600;
        public float BudgetRadius = 95f;

        public bool DynamicPopulation = true;
        public float CityPedDay = 1.30f;
        public float CityPedEvening = 1.38f;
        public float CityPedNight = 0.92f;
        public float BusyPedBonus = 0.12f;
        public float BeachPedDay = 1.45f;
        public float RuralPedDay = 0.82f;
        public float RuralPedNight = 0.62f;
        public float CityTraffic = 1.15f;
        public float RuralTraffic = 0.94f;
        public float ParkedVehicle = 1.12f;
        public int SoftPedBudget = 48;
        public int HardPedBudget = 62;
        public int SoftVehicleBudget = 40;
        public int HardVehicleBudget = 52;
        public float MaxPedMultiplier = 1.55f;
        public float MaxVehicleMultiplier = 1.25f;

        public bool OnlineVehicles = true;
        public int OnlineVehicleCheckMs = 9000;
        public int OnlineVehicleChancePercent = 12;
        public float OnlineVehicleMinDistance = 48f;
        public float OnlineVehicleMaxDistance = 92f;
        public int OnlineVehicleMaxRequestMs = 3500;
        public bool ReplaceMovingTraffic = true;
        public bool ReplaceParkedVehicles = true;

        public static Config Load(string path)
        {
            var c = new Config();
            if (!File.Exists(path)) return c;
            var v = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq > 0) v[section + "." + line.Substring(0,eq).Trim()] = line.Substring(eq+1).Trim();
            }

            c.Enabled=B(v,"General.Enabled",c.Enabled); c.DebugLogging=B(v,"General.DebugLogging",c.DebugLogging); c.DisableDuringMissions=B(v,"General.DisableDuringMissions",c.DisableDuringMissions); c.ContextRefreshMs=I(v,"General.ContextRefreshMs",c.ContextRefreshMs); c.BudgetRadius=F(v,"General.BudgetRadius",c.BudgetRadius);
            c.DynamicPopulation=B(v,"Population.Enabled",c.DynamicPopulation); c.CityPedDay=F(v,"Population.CityPedDay",c.CityPedDay); c.CityPedEvening=F(v,"Population.CityPedEvening",c.CityPedEvening); c.CityPedNight=F(v,"Population.CityPedNight",c.CityPedNight); c.BusyPedBonus=F(v,"Population.BusyPedBonus",c.BusyPedBonus); c.BeachPedDay=F(v,"Population.BeachPedDay",c.BeachPedDay); c.RuralPedDay=F(v,"Population.RuralPedDay",c.RuralPedDay); c.RuralPedNight=F(v,"Population.RuralPedNight",c.RuralPedNight); c.CityTraffic=F(v,"Population.CityTraffic",c.CityTraffic); c.RuralTraffic=F(v,"Population.RuralTraffic",c.RuralTraffic); c.ParkedVehicle=F(v,"Population.ParkedVehicle",c.ParkedVehicle); c.SoftPedBudget=I(v,"Population.SoftPedBudget",c.SoftPedBudget); c.HardPedBudget=I(v,"Population.HardPedBudget",c.HardPedBudget); c.SoftVehicleBudget=I(v,"Population.SoftVehicleBudget",c.SoftVehicleBudget); c.HardVehicleBudget=I(v,"Population.HardVehicleBudget",c.HardVehicleBudget); c.MaxPedMultiplier=F(v,"Population.MaxPedMultiplier",c.MaxPedMultiplier); c.MaxVehicleMultiplier=F(v,"Population.MaxVehicleMultiplier",c.MaxVehicleMultiplier);
            c.OnlineVehicles=B(v,"OnlineVehicles.Enabled",c.OnlineVehicles); c.OnlineVehicleCheckMs=I(v,"OnlineVehicles.CheckIntervalMs",c.OnlineVehicleCheckMs); c.OnlineVehicleChancePercent=I(v,"OnlineVehicles.ChancePercent",c.OnlineVehicleChancePercent); c.OnlineVehicleMinDistance=F(v,"OnlineVehicles.MinDistance",c.OnlineVehicleMinDistance); c.OnlineVehicleMaxDistance=F(v,"OnlineVehicles.MaxDistance",c.OnlineVehicleMaxDistance); c.OnlineVehicleMaxRequestMs=I(v,"OnlineVehicles.MaxRequestMs",c.OnlineVehicleMaxRequestMs); c.ReplaceMovingTraffic=B(v,"OnlineVehicles.ReplaceMovingTraffic",c.ReplaceMovingTraffic); c.ReplaceParkedVehicles=B(v,"OnlineVehicles.ReplaceParkedVehicles",c.ReplaceParkedVehicles);
            return c;
        }

        private static bool B(Dictionary<string,string> v,string k,bool d){string s;bool x;return v.TryGetValue(k,out s)&&bool.TryParse(s,out x)?x:d;}
        private static int I(Dictionary<string,string> v,string k,int d){string s;int x;return v.TryGetValue(k,out s)&&int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out x)?x:d;}
        private static float F(Dictionary<string,string> v,string k,float d){string s;float x;return v.TryGetValue(k,out s)&&float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out x)?x:d;}
    }
}
