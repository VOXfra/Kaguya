using GTA;
using GTA.Native;
using System;
using System.IO;

namespace VOX.WorldLifeVI
{
    public sealed class WorldLifeVITelemetryScript : Script
    {
        private const string ConfigPath = "scripts\\WorldLifeVI.ini";
        private const string DataDirectory = "scripts\\WorldLifeVI";
        private const string LogPath = DataDirectory + "\\DensityTelemetry.log";
        private readonly Config _cfg;
        private int _lastSample;
        private string _lastContext = string.Empty;

        public WorldLifeVITelemetryScript()
        {
            Directory.CreateDirectory(DataDirectory);
            _cfg = Config.Load(ConfigPath);
            Interval = 1000;
            Tick += OnTick;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_cfg.Enabled || !_cfg.DebugLogging || !_cfg.DynamicPopulation) return;
            Ped player = Game.LocalPlayerPed;
            if (player == null || !player.Exists()) return;
            int now = Game.GameTime;
            if (now - _lastSample < 20000) return;
            _lastSample = now;

            int hour = 12; string zone = string.Empty;
            try { hour = Function.Call<int>(Hash.GET_CLOCK_HOURS); } catch { }
            try { zone = Function.Call<string>(Hash.GET_NAME_OF_ZONE, player.Position.X, player.Position.Y, player.Position.Z) ?? string.Empty; } catch { }
            bool rural = IsRural(zone), beach = IsBeach(zone), busy = IsBusyUrban(zone);
            bool night = hour >= 1 && hour < 6, evening = hour >= 18 && hour < 24;

            float ped, veh;
            if (rural) { ped = night ? _cfg.RuralPedNight : _cfg.RuralPedDay; veh = _cfg.RuralTraffic; }
            else
            {
                ped = night ? _cfg.CityPedNight : (evening ? _cfg.CityPedEvening : _cfg.CityPedDay);
                if (beach && hour >= 9 && hour < 20) ped = Math.Max(ped, _cfg.BeachPedDay);
                if (busy && !night) ped += _cfg.BusyPedBonus;
                veh = _cfg.CityTraffic;
            }

            int pedCount=0, vehicleCount=0;
            try { pedCount = World.GetNearbyPeds(player, _cfg.BudgetRadius).Length; } catch { }
            try { vehicleCount = World.GetNearbyVehicles(player, _cfg.BudgetRadius).Length; } catch { }
            ped = Clamp(ApplyBudget(ped,pedCount,_cfg.SoftPedBudget,_cfg.HardPedBudget),0.45f,_cfg.MaxPedMultiplier);
            veh = Clamp(ApplyBudget(veh,vehicleCount,_cfg.SoftVehicleBudget,_cfg.HardVehicleBudget),0.60f,_cfg.MaxVehicleMultiplier);
            float scenario = Clamp(ped*(rural?0.95f:1.03f),0.45f,_cfg.MaxPedMultiplier);
            float parked = Clamp(_cfg.ParkedVehicle*(rural?0.78f:1f),0.55f,_cfg.MaxVehicleMultiplier);

            string context = zone + "/" + hour + "/" + rural + "/" + beach + "/" + busy;
            Append("zone="+zone+" hour="+hour+" peds="+pedCount+" vehicles="+vehicleCount+
                   " multipliers[ped="+ped.ToString("0.00")+",scenario="+scenario.ToString("0.00")+
                   ",traffic="+veh.ToString("0.00")+",parked="+parked.ToString("0.00")+"]"+
                   (context==_lastContext?string.Empty:" contextChanged=true"));
            _lastContext=context;
        }

        private static float ApplyBudget(float target,int count,int soft,int hard)
        {
            if(hard<=soft)return count>=hard?Math.Min(1f,target):target;
            if(count<=soft)return target;if(count>=hard)return Math.Min(1f,target);
            float t=(count-soft)/(float)(hard-soft);return target+(Math.Min(1f,target)-target)*t;
        }
        private static float Clamp(float v,float lo,float hi){return Math.Max(lo,Math.Min(hi,v));}
        private static bool IsRural(string z)
        {
            switch((z??string.Empty).ToUpperInvariant())
            {
                case "SANDY":case "GRAPES":case "PALETO":case "DESRT":case "ALAMO":case "ZANCUDO":case "HARMO":case "GREATC":case "MTCHIL":case "MTGORDO":case "MTJOSE":case "CANNY":case "TATAMO":case "LAGO":case "PALCOV":case "PROCOB":case "ARMYB":case "NCHU":return true;
                default:return false;
            }
        }
        private static bool IsBeach(string z){z=(z??string.Empty).ToUpperInvariant();return z=="DELPE"||z=="BEACH"||z=="VESPU"||z=="VCANA";}
        private static bool IsBusyUrban(string z){z=(z??string.Empty).ToUpperInvariant();return z=="DOWNT"||z=="VINE"||z=="WVINE"||z=="DELPE"||z=="ROCKF"||z=="TEXTI"||z=="HAWICK";}
        private static void Append(string line)
        {
            try{File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" | "+line+Environment.NewLine);}catch{}
        }
    }
}
