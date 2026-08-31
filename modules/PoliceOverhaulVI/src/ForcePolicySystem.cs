using GTA;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class ForcePolicySystem
    {
        private int _lastScan;
        private bool _lastLethal, _lastPit, _initialized;
        private int _lastRiskLog;
        public bool LethalAuthorized { get; private set; }
        public bool PitAuthorized { get; private set; }
        public float LastCivilianRisk { get; private set; }
        public float LastPitRisk { get; private set; }

        public void Update(Ped player, int wanted, CaseMemory memory, Config cfg, Action<string> log)
        {
            if (!cfg.ProportionalForceEnabled || player == null || !player.Exists() || wanted <= 0)
            {
                LethalAuthorized = false; PitAuthorized = false; return;
            }

            bool shooting = SafeBool(Hash.IS_PED_SHOOTING, player.Handle);
            bool aimingAtLaw = IsAimingAtLawPed(player, cfg.ForcePolicyRadius);
            float speedKph = 0f;
            if (player.IsInVehicle())
            {
                try { speedKph = Function.Call<float>(Hash.GET_ENTITY_SPEED, player.CurrentVehicle.Handle) * 3.6f; } catch { }
            }

            bool threatAllowsLethal;
            if (!cfg.LethalForceRequiresCurrentThreat)
                threatAllowsLethal = wanted >= cfg.LethalForceMinimumWanted || shooting;
            else
                threatAllowsLethal = shooting || aimingAtLaw || wanted >= Math.Max(cfg.LethalArmedEscalationWanted, 4);
            if (wanted <= 2 && !shooting && !aimingAtLaw) threatAllowsLethal = false;

            LastCivilianRisk = cfg.CivilianRiskEnabled ? CivilianRiskSystem.EvaluateTargetArea(player,cfg) : 0f;
            float generalThreshold = shooting ? cfg.CivilianRiskEmergencyThreshold : cfg.CivilianRiskLethalThreshold;
            LethalAuthorized = threatAllowsLethal && (!cfg.CivilianRiskEnabled || LastCivilianRisk <= generalThreshold);

            bool pitBase = wanted >= cfg.PitMinimumWanted && player.IsInVehicle() && speedKph >= cfg.PitMinimumSpeedKph;
            if (cfg.PitRequiresFleeing && wanted <= 1) pitBase = false;
            LastPitRisk = pitBase && cfg.CivilianRiskEnabled ? CivilianRiskSystem.EvaluatePitRisk(player,cfg) : 0f;
            float pitThreshold = shooting ? cfg.PitEmergencyRiskThreshold : cfg.PitRiskThreshold;
            PitAuthorized = pitBase && (!cfg.CivilianRiskEnabled || LastPitRisk <= pitThreshold);

            LogTransitions(log);
            if (cfg.CivilianRiskEnabled && Game.GameTime-_lastRiskLog>3000 && ((threatAllowsLethal&&!LethalAuthorized)||(pitBase&&!PitAuthorized)))
            {
                _lastRiskLog=Game.GameTime;
                if(threatAllowsLethal&&!LethalAuthorized&&log!=null)log("Lethal engagement withheld/avoided: civilian risk="+(int)LastCivilianRisk+".");
                if(pitBase&&!PitAuthorized&&log!=null)log("PIT withheld/avoided: civilian/traffic risk="+(int)LastPitRisk+".");
            }

            int now = Game.GameTime;
            if (now - _lastScan < Math.Max(100, cfg.ForcePolicyScanIntervalMs)) return;
            _lastScan = now;

            Ped[] nearby;
            try { nearby = World.GetNearbyPeds(player, cfg.ForcePolicyRadius); }
            catch { return; }
            foreach (Ped cop in nearby)
            {
                if (cop == null || !cop.Exists() || cop.IsDead || !Perception.IsLawPed(cop)) continue;
                bool officerLethal=LethalAuthorized;
                float lineRisk=0f;
                if(cfg.CivilianRiskEnabled&&threatAllowsLethal)
                    officerLethal=CivilianRiskSystem.LethalAllowedForOfficer(cop,player,true,cfg,out lineRisk);
                ApplyOfficerPolicy(cop, player, wanted, officerLethal, PitAuthorized, cfg);
            }
        }

        public void Reset()
        {
            LethalAuthorized = false; PitAuthorized = false; LastCivilianRisk=LastPitRisk=0f; _initialized = false;
        }

        private void LogTransitions(Action<string> log)
        {
            if (!_initialized || LethalAuthorized != _lastLethal)
            {
                if (log != null) log(LethalAuthorized ? "Lethal-force authorization granted for current threat and collateral-risk state." : "Lethal force restricted; current response remains non-lethal/containment.");
                _lastLethal = LethalAuthorized;
            }
            if (!_initialized || PitAuthorized != _lastPit)
            {
                if (log != null) log(PitAuthorized ? "PIT authorization granted for fleeing vehicle and safe-enough surroundings." : "PIT restricted for current pursuit/risk state.");
                _lastPit = PitAuthorized;
            }
            _initialized = true;
        }

        private static void ApplyOfficerPolicy(Ped cop, Ped player, int wanted, bool lethal, bool pit, Config cfg)
        {
            try
            {
                if (!lethal)
                {
                    int stun = Function.Call<int>(Hash.GET_HASH_KEY, "WEAPON_STUNGUN");
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, cop.Handle, stun, 20, false, false);
                    Function.Call(Hash.SET_CURRENT_PED_WEAPON, cop.Handle, stun, true);
                    Function.Call(Hash.SET_PED_ACCURACY, cop.Handle, Math.Max(5, cfg.NonLethalAccuracy));
                    Function.Call(Hash.SET_PED_SHOOT_RATE, cop.Handle, Math.Max(10, cfg.NonLethalShootRate));
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, cop.Handle, 5, false);

                    if (wanted <= 2 && !player.IsInVehicle() && Perception.Distance(cop.Position, player.Position) <= 9f)
                    {
                        float playerSpeed = 0f;
                        try { playerSpeed = Function.Call<float>(Hash.GET_ENTITY_SPEED, player.Handle); } catch { }
                        if (playerSpeed < 2.2f && !SafeBool(Hash.IS_PED_SHOOTING, player.Handle))
                            try { Function.Call(Hash.TASK_ARREST_PED, cop.Handle, player.Handle); } catch { }
                    }
                }
                else
                {
                    string weaponName = wanted >= 4 ? "WEAPON_CARBINERIFLE" : "WEAPON_COMBATPISTOL";
                    int weapon = Function.Call<int>(Hash.GET_HASH_KEY, weaponName);
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, cop.Handle, weapon, 250, false, false);
                    Function.Call(Hash.SET_CURRENT_PED_WEAPON, cop.Handle, weapon, true);
                    Function.Call(Hash.SET_PED_ACCURACY, cop.Handle, wanted >= 4 ? 43 : 31);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, cop.Handle, 5, true);
                }

                if (cop.IsInVehicle())
                {
                    Vehicle vehicle = cop.CurrentVehicle;
                    if (vehicle != null && vehicle.Exists() && vehicle.Driver != null && vehicle.Driver.Exists() && vehicle.Driver.Handle == cop.Handle)
                    {
                        float aggression = pit ? 0.82f : (wanted <= 1 ? 0.08f : wanted == 2 ? 0.22f : 0.42f);
                        Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, cop.Handle, aggression);
                        Function.Call(Hash.SET_DRIVER_ABILITY, cop.Handle, wanted >= 3 ? 0.95f : 0.76f);
                    }
                }
            }
            catch { }
        }

        private static bool IsAimingAtLawPed(Ped player, float radius)
        {
            try
            {
                if (!Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle)) return false;
                var output = new OutputArgument();
                if (!Function.Call<bool>(Hash.GET_ENTITY_PLAYER_IS_FREE_AIMING_AT, Game.Player.Handle, output)) return false;
                int h = output.GetResult<int>();
                if (h == 0 || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, h)) return false;
                Entity e = Entity.FromHandle(h); Ped p = e as Ped;
                return p != null && p.Exists() && Perception.IsLawPed(p) && Perception.Distance(player.Position, p.Position) <= radius;
            }
            catch { return false; }
        }

        private static bool SafeBool(Hash h, params InputArgument[] args)
        {
            try { return Function.Call<bool>(h, args); }
            catch { return false; }
        }
    }
}
