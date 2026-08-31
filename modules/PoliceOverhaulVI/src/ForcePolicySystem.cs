using GTA;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class ForcePolicySystem
    {
        private int _lastScan;
        private bool _lastLethal, _lastPit, _initialized;
        public bool LethalAuthorized { get; private set; }
        public bool PitAuthorized { get; private set; }

        public void Update(Ped player, int wanted, CaseMemory memory, Config cfg, Action<string> log)
        {
            if (!cfg.ProportionalForceEnabled || player == null || !player.Exists() || wanted <= 0)
            {
                LethalAuthorized = false; PitAuthorized = false; return;
            }

            bool shooting = SafeBool(Hash.IS_PED_SHOOTING, player.Handle);
            bool armed = SafeBool(Hash.IS_PED_ARMED, player.Handle, 7);
            bool aimingAtLaw = IsAimingAtLawPed(player, cfg.ForcePolicyRadius);
            float speedKph = 0f;
            if (player.IsInVehicle())
            {
                try { speedKph = Function.Call<float>(Hash.GET_ENTITY_SPEED, player.CurrentVehicle.Handle) * 3.6f; } catch { }
            }

            if (!cfg.LethalForceRequiresCurrentThreat)
                LethalAuthorized = wanted >= cfg.LethalForceMinimumWanted || shooting;
            else
                LethalAuthorized = shooting || aimingAtLaw || wanted >= Math.Max(cfg.LethalArmedEscalationWanted, 4);

            // A historical weapon description or high old case threat is police
            // intelligence, not permission to shoot a currently compliant suspect.
            if (wanted <= 2 && !shooting && !aimingAtLaw) LethalAuthorized = false;

            PitAuthorized = wanted >= cfg.PitMinimumWanted && player.IsInVehicle() && speedKph >= cfg.PitMinimumSpeedKph;
            if (cfg.PitRequiresFleeing && wanted <= 1) PitAuthorized = false;

            LogTransitions(log);
            int now = Game.GameTime;
            if (now - _lastScan < Math.Max(100, cfg.ForcePolicyScanIntervalMs)) return;
            _lastScan = now;

            Ped[] nearby;
            try { nearby = World.GetNearbyPeds(player, cfg.ForcePolicyRadius); }
            catch { return; }
            foreach (Ped cop in nearby)
            {
                if (cop == null || !cop.Exists() || cop.IsDead || !Perception.IsLawPed(cop)) continue;
                ApplyOfficerPolicy(cop, player, wanted, LethalAuthorized, PitAuthorized, cfg);
            }
        }

        public void Reset()
        {
            LethalAuthorized = false; PitAuthorized = false; _initialized = false;
        }

        private void LogTransitions(Action<string> log)
        {
            if (!_initialized || LethalAuthorized != _lastLethal)
            {
                if (log != null) log(LethalAuthorized ? "Lethal-force authorization granted for current threat." : "Lethal force restricted; current response remains non-lethal.");
                _lastLethal = LethalAuthorized;
            }
            if (!_initialized || PitAuthorized != _lastPit)
            {
                if (log != null) log(PitAuthorized ? "PIT authorization granted for fleeing vehicle." : "PIT restricted for current pursuit state.");
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

                    if (wanted == 1 && !player.IsInVehicle() && Perception.Distance(cop.Position, player.Position) <= 9f)
                    {
                        float playerSpeed = 0f;
                        try { playerSpeed = Function.Call<float>(Hash.GET_ENTITY_SPEED, player.Handle); } catch { }
                        if (playerSpeed < 2.2f && !SafeBool(Hash.IS_PED_SHOOTING, player.Handle))
                        {
                            // Lets low-level encounters resolve as an arrest attempt
                            // instead of forcing a combat task immediately.
                            try { Function.Call(Hash.TASK_ARREST_PED, cop.Handle, player.Handle); } catch { }
                        }
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
                        float aggression = pit ? 0.82f : (wanted <= 1 ? 0.08f : wanted == 2 ? 0.22f : 0.48f);
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
