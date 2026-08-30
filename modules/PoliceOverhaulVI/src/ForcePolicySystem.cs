using GTA;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class ForcePolicySystem
    {
        private int _lastScan;
        private bool _lastLethal;
        private bool _lastPit;
        private bool _initialized;

        public bool LethalAuthorized { get; private set; }
        public bool PitAuthorized { get; private set; }

        public void Update(Ped player, int wanted, CaseMemory memory, Config cfg, Action<string> log)
        {
            if (!cfg.ProportionalForceEnabled || player == null || !player.Exists() || wanted <= 0)
            {
                LethalAuthorized = false;
                PitAuthorized = false;
                return;
            }

            bool playerShooting = false;
            try { playerShooting = Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle); } catch { }

            float speedKph = 0f;
            if (player.IsInVehicle())
            {
                try { speedKph = Function.Call<float>(Hash.GET_ENTITY_SPEED, player.CurrentVehicle.Handle) * 3.6f; } catch { }
            }

            LethalAuthorized = wanted >= cfg.LethalForceMinimumWanted || playerShooting;
            if (memory != null && memory.ThreatLevel >= 4)
                LethalAuthorized = true;

            PitAuthorized = wanted >= cfg.PitMinimumWanted && player.IsInVehicle() && speedKph >= cfg.PitMinimumSpeedKph;
            if (wanted >= 4)
                PitAuthorized = true;

            if (!_initialized || LethalAuthorized != _lastLethal)
            {
                if (log != null) log(LethalAuthorized ? "Lethal-force authorization granted." : "Lethal force restricted; officers use non-lethal response.");
                _lastLethal = LethalAuthorized;
            }
            if (!_initialized || PitAuthorized != _lastPit)
            {
                if (log != null) log(PitAuthorized ? "PIT authorization granted." : "PIT restricted for current pursuit state.");
                _lastPit = PitAuthorized;
            }
            _initialized = true;

            int now = Game.GameTime;
            if (now - _lastScan < Math.Max(100, cfg.ForcePolicyScanIntervalMs))
                return;
            _lastScan = now;

            Ped[] nearby;
            try { nearby = World.GetNearbyPeds(player, cfg.ForcePolicyRadius); }
            catch { return; }

            foreach (Ped cop in nearby)
            {
                if (!Perception.IsLawPed(cop) || cop == null || !cop.Exists() || cop.IsDead)
                    continue;
                ApplyOfficerPolicy(cop, wanted, LethalAuthorized, PitAuthorized, cfg);
            }
        }

        public void Reset()
        {
            LethalAuthorized = false;
            PitAuthorized = false;
            _initialized = false;
        }

        private static void ApplyOfficerPolicy(Ped cop, int wanted, bool lethal, bool pit, Config cfg)
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
                }
                else
                {
                    string weaponName = wanted >= 4 ? "WEAPON_CARBINERIFLE" : "WEAPON_COMBATPISTOL";
                    int weapon = Function.Call<int>(Hash.GET_HASH_KEY, weaponName);
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, cop.Handle, weapon, 250, false, false);
                    Function.Call(Hash.SET_CURRENT_PED_WEAPON, cop.Handle, weapon, true);
                    Function.Call(Hash.SET_PED_ACCURACY, cop.Handle, wanted >= 4 ? 43 : 31);
                }

                if (cop.IsInVehicle())
                {
                    Vehicle vehicle = cop.CurrentVehicle;
                    if (vehicle != null && vehicle.Exists() && vehicle.Driver != null && vehicle.Driver.Exists() && vehicle.Driver.Handle == cop.Handle)
                    {
                        float aggression = pit ? 0.85f : (wanted <= 1 ? 0.12f : 0.28f);
                        Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, cop.Handle, aggression);
                        Function.Call(Hash.SET_DRIVER_ABILITY, cop.Handle, wanted >= 3 ? 0.95f : 0.72f);
                    }
                }
            }
            catch { }
        }
    }
}
