using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class DispatchSystem
    {
        private sealed class CustomUnit
        {
            public int VehicleHandle;
            public readonly List<int> PedHandles = new List<int>();
            public int RequiredLevel;
            public int CreatedAt;
        }

        private readonly List<CustomUnit> _units = new List<CustomUnit>();
        private int _lastSupportSpawn, _lastHeavySpawn, _lastJetSpawn, _fiveStarStartedAt, _lastFiveStarHeatAt, _sixthStarTextureRequestAt, _lastRiskLog, _lastLevel;

        private static readonly string[] Tier3Urban = { "police3", "police5", "polgauntlet", "polterminus", "poldorado", "polfaction2" };
        private static readonly string[] Tier4Urban = { "police3", "polgauntlet", "poldominator10", "polcaracara", "polcaracara2", "polcoquette4", "polbuffalo6" };
        private static readonly string[] Tier5Urban = { "polbuffalo6", "poldominator10", "polcoquette4", "polignus", "riot" };

        public int UpdateTier(Ped player, int nativeWanted, CaseMemory memory, Config cfg, Action<string> log)
        {
            if (cfg.EnableSixthStar && nativeWanted >= 5 && memory != null && memory.ThreatLevel >= 6) return 6;
            if (nativeWanted < 5)
            {
                _fiveStarStartedAt = 0;
                _lastFiveStarHeatAt = 0;
                return Math.Max(nativeWanted, memory == null ? 0 : Math.Min(5, memory.ThreatLevel));
            }

            if (_fiveStarStartedAt == 0) _fiveStarStartedAt = Game.GameTime;
            if (player != null && player.Exists() && SafeBool(Hash.IS_PED_SHOOTING, player.Handle) && Game.GameTime - _lastFiveStarHeatAt >= cfg.FiveStarShootingHeatIntervalMs)
            {
                _lastFiveStarHeatAt = Game.GameTime;
                if (memory != null) memory.HeatPoints++;
            }

            bool time = Game.GameTime - _fiveStarStartedAt >= Math.Max(10, cfg.SixStarAfterFiveStarSeconds) * 1000;
            bool heat = memory != null && memory.HeatPoints >= Math.Max(1, cfg.SixStarHeatThreshold);
            if (cfg.EnableSixthStar && (time || heat))
            {
                if (memory != null && memory.ThreatLevel < 6)
                {
                    memory.ThreatLevel = 6;
                    IdentificationSystem.AddNotoriety(memory, 10f, cfg);
                    memory.Touch(cfg);
                    if (log != null) log("Sixth-star emergency response authorized.");
                }
                return 6;
            }
            return 5;
        }

        public void UpdateResponse(Ped player, int level, Config cfg, Action<string> log)
        {
            ConfigureVanillaDispatch(level);
            if (cfg.HidePoliceBlips)
            {
                try { Function.Call(Hash.SET_POLICE_RADAR_BLIPS, false); } catch { }
            }

            if (level >= 6 && _lastLevel < 6)
            {
                // Do not let tier 3–5 custom units consume every slot before
                // the actual sixth-star response gets a chance to exist.
                CleanupBelowTier(6);
                _lastHeavySpawn = 0;
                _lastJetSpawn = 0;
                if (log != null) log("Sixth-star transition: lower-tier custom dispatch cleared for military response.");
            }
            _lastLevel = level;

            CleanupInvalidUnits(player, level);
            if (!cfg.CustomDispatchEnabled || player == null || !player.Exists() || level <= 0) return;

            int maxUnits = Math.Max(1, cfg.MaxCustomUnits) + (level >= 6 ? 2 : 0);
            if (_units.Count >= maxUnits) return;
            int now = Game.GameTime;

            if (level >= 3 && level <= 5 && now - _lastSupportSpawn >= cfg.DispatchSupportIntervalMs)
            {
                if (SpawnTacticalGround(player, level, cfg, log)) _lastSupportSpawn = now;
            }

            if (level >= 6 && now - _lastHeavySpawn >= Math.Max(5000, cfg.SixStarHeavyIntervalMs))
            {
                float risk = 0f;
                bool explosiveAllowed = !cfg.CivilianRiskEnabled || CivilianRiskSystem.MilitaryEngagementAllowed(player, cfg, out risk);
                bool spawned = false;

                // Ground military response is mandatory at six stars. Civilian
                // risk changes whether it may use a tank, not whether Marines exist.
                if (cfg.SixStarMilitaryGround && _units.Count < maxUnits)
                    spawned |= SpawnMilitaryGround(player, explosiveAllowed, log);

                if (cfg.SixStarAttackHelicopter && _units.Count < maxUnits)
                    spawned |= explosiveAllowed ? SpawnAttackHelicopter(player, log) : SpawnOverwatchHelicopter(player, log);

                if (!explosiveAllowed && now - _lastRiskLog > 4000)
                {
                    _lastRiskLog = now;
                    if (log != null) log("Explosive military engagement withheld: civilian risk=" + (int)risk + "; Marines/overwatch remain active.");
                }
                if (spawned) _lastHeavySpawn = now;
            }

            if (level >= 6 && cfg.SixStarJet && now - _lastJetSpawn >= 90000 && _units.Count < maxUnits)
            {
                float risk = 0f;
                bool allowed = !cfg.CivilianRiskEnabled || CivilianRiskSystem.MilitaryEngagementAllowed(player, cfg, out risk);
                if (allowed && SpawnJet(player, log)) _lastJetSpawn = now;
                else if (!allowed && now - _lastRiskLog > 4000)
                {
                    _lastRiskLog = now;
                    if (log != null) log("Jet attack withheld due to civilian collateral risk=" + (int)risk + ".");
                }
            }
        }

        public void DrawSixthStarIfNeeded(int level)
        {
            if (level < 6) return;
            try
            {
                if (!Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, "commonmenu"))
                {
                    if (Game.GameTime - _sixthStarTextureRequestAt > 500)
                    {
                        _sixthStarTextureRequestAt = Game.GameTime;
                        Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, "commonmenu", false);
                    }
                    return;
                }

                float safe = Function.Call<float>(Hash.GET_SAFE_ZONE_SIZE);
                float right = 1f - (1f - safe) * 0.5f;
                // Vanilla displays only five stars internally. Draw the sixth
                // immediately to the left of that row instead of the old large
                // hard-coded offset that was visibly detached on ultrawide.
                float x = right - 0.074f;
                float y = 0.0438f;
                Function.Call(Hash.DRAW_SPRITE, "commonmenu", "leaderboard_star_icon", x, y, 0.0158f, 0.0285f, 0f, 255, 255, 255, 245, false);
            }
            catch { }
        }

        public void CleanupAll()
        {
            foreach (CustomUnit u in _units) CleanupUnit(u);
            _units.Clear();
            _fiveStarStartedAt = 0;
            _lastFiveStarHeatAt = 0;
            _lastLevel = 0;
        }

        private static void ConfigureVanillaDispatch(int level)
        {
            try
            {
                Function.Call(Hash.SET_DISPATCH_COPS_FOR_PLAYER, Game.Player.Handle, level > 0);
                Function.Call(Hash.SET_MAX_WANTED_LEVEL, 5);
                for (int service = 1; service <= 15; service++)
                {
                    bool enabled = level > 0;
                    if ((service == 2 || service == 3) && level < 3) enabled = false;
                    Function.Call(Hash.ENABLE_DISPATCH_SERVICE, service, enabled);
                }
            }
            catch { }
        }

        private bool SpawnTacticalGround(Ped player, int level, Config cfg, Action<string> log)
        {
            bool rural = IsRural(player.Position);
            if (rural && level == 3) return SpawnGroundUnit(player, "sheriff2", "s_m_y_sheriff_01", level, 1, false, false, log);
            string vehicle = "police3";
            if (cfg.OnlinePoliceVehicles)
            {
                string[] pool = level == 3 ? Tier3Urban : (level == 4 ? Tier4Urban : Tier5Urban);
                vehicle = ChooseAvailableModel(pool, "police3");
            }
            if (level >= 5 && string.Equals(vehicle, "riot", StringComparison.OrdinalIgnoreCase))
                return SpawnGroundUnit(player, vehicle, "s_m_y_swat_01", level, 2, false, false, log);
            string pedName = level >= 4 ? "s_m_y_swat_01" : "s_m_y_cop_01";
            return SpawnGroundUnit(player, vehicle, pedName, level, level >= 4 ? 2 : 1, false, false, log);
        }

        private bool SpawnMilitaryGround(Ped player, bool explosivesAllowed, Action<string> log)
        {
            bool tank = explosivesAllowed && ((Game.GameTime / 30000) & 1) == 1;
            return SpawnGroundUnit(player, tank ? "rhino" : "crusader", "s_m_y_marine_01", 6, tank ? 0 : 2, tank, true, log);
        }

        private bool SpawnGroundUnit(Ped player, string vehicleName, string pedName, int level, int passengers, bool tank, bool lethal, Action<string> log)
        {
            int vehicleModel = Function.Call<int>(Hash.GET_HASH_KEY, vehicleName);
            int pedModel = Function.Call<int>(Hash.GET_HASH_KEY, pedName);
            if (!EnsureModel(vehicleModel) || !EnsureModel(pedModel)) return false;
            Vector3 pos = FindGroundSpawn(player, 230f + _units.Count * 18f);
            float heading = HeadingTo(pos, player.Position);
            var unit = new CustomUnit { RequiredLevel = level, CreatedAt = Game.GameTime };
            try
            {
                int v = Function.Call<int>(Hash.CREATE_VEHICLE, vehicleModel, pos.X, pos.Y, pos.Z, heading, false, false);
                if (v == 0) return false;
                unit.VehicleHandle = v;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, v, true, true);
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, true, true, false);
                Function.Call(Hash.SET_VEHICLE_SIREN, v, level < 6);

                int driver = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, v, 6, pedModel, -1, false, false);
                if (driver != 0)
                {
                    unit.PedHandles.Add(driver);
                    SetupResponsePed(driver, lethal);
                    Function.Call(Hash.SET_DRIVER_ABILITY, driver, 1f);
                    Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver, level >= 5 ? 0.78f : (level == 3 ? 0.42f : 0.62f));
                    if (tank) Function.Call(Hash.TASK_COMBAT_PED, driver, player.Handle, 0, 16);
                    else Function.Call(Hash.TASK_VEHICLE_CHASE, driver, player.Handle);
                }

                for (int seat = 0; seat < passengers; seat++)
                {
                    int ped = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, v, 6, pedModel, seat, false, false);
                    if (ped == 0) continue;
                    unit.PedHandles.Add(ped);
                    SetupResponsePed(ped, lethal);
                    if (lethal) Function.Call(Hash.TASK_COMBAT_PED, ped, player.Handle, 0, 16);
                    else Function.Call(Hash.TASK_LOOK_AT_ENTITY, ped, player.Handle, 6000, 0, 2);
                }

                _units.Add(unit);
                if (log != null) log("Custom dispatch spawned " + vehicleName + " response unit for tier " + level + (lethal ? " lethal-ready." : " containment/interception."));
                return true;
            }
            catch (Exception ex)
            {
                if (log != null) log("Custom ground dispatch failed: " + ex.Message);
                CleanupUnit(unit);
                return false;
            }
            finally
            {
                Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, vehicleModel);
                Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, pedModel);
            }
        }

        private bool SpawnAttackHelicopter(Ped player, Action<string> log) { return SpawnHelicopter(player, "savage", true, log); }
        private bool SpawnOverwatchHelicopter(Ped player, Action<string> log) { return SpawnHelicopter(player, "polmav", false, log); }

        private bool SpawnHelicopter(Ped player, string modelName, bool armed, Action<string> log)
        {
            int vm = Function.Call<int>(Hash.GET_HASH_KEY, modelName);
            int pm = Function.Call<int>(Hash.GET_HASH_KEY, armed ? "s_m_y_marine_01" : "s_m_y_cop_01");
            if (!EnsureModel(vm) || !EnsureModel(pm)) return false;
            Vector3 p = player.Position;
            double a = (Game.GameTime % 6283) / 1000.0;
            Vector3 spawn = new Vector3(p.X + (float)Math.Cos(a) * 420f, p.Y + (float)Math.Sin(a) * 420f, p.Z + 150f);
            var unit = new CustomUnit { RequiredLevel = 6, CreatedAt = Game.GameTime };
            try
            {
                int heli = Function.Call<int>(Hash.CREATE_VEHICLE, vm, spawn.X, spawn.Y, spawn.Z, HeadingTo(spawn, p), false, false);
                if (heli == 0) return false;
                unit.VehicleHandle = heli;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, heli, true, true);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, heli, true, true, false);
                Function.Call(Hash.SET_HELI_BLADES_FULL_SPEED, heli);
                int pilot = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, heli, 6, pm, -1, false, false);
                if (pilot != 0)
                {
                    unit.PedHandles.Add(pilot);
                    SetupResponsePed(pilot, armed);
                    Function.Call(Hash.TASK_HELI_CHASE, pilot, player.Handle, 0f, 0f, 0f);
                }
                if (armed)
                {
                    for (int seat = 0; seat <= 1; seat++)
                    {
                        int gunner = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, heli, 6, pm, seat, false, false);
                        if (gunner == 0) continue;
                        unit.PedHandles.Add(gunner);
                        SetupResponsePed(gunner, true);
                        Function.Call(Hash.TASK_COMBAT_PED, gunner, player.Handle, 0, 16);
                    }
                }
                _units.Add(unit);
                if (log != null) log(armed ? "Sixth-star attack helicopter deployed after collateral check." : "Sixth-star police overwatch helicopter deployed; explosive engagement withheld.");
                return true;
            }
            catch (Exception ex)
            {
                if (log != null) log("Helicopter dispatch failed: " + ex.Message);
                CleanupUnit(unit);
                return false;
            }
            finally
            {
                Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, vm);
                Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, pm);
            }
        }

        private bool SpawnJet(Ped player, Action<string> log)
        {
            int vm = Function.Call<int>(Hash.GET_HASH_KEY, "lazer");
            int pm = Function.Call<int>(Hash.GET_HASH_KEY, "s_m_y_marine_01");
            if (!EnsureModel(vm) || !EnsureModel(pm)) return false;
            Vector3 p = player.Position;
            Vector3 spawn = new Vector3(p.X - 800f, p.Y - 500f, p.Z + 320f);
            var unit = new CustomUnit { RequiredLevel = 6, CreatedAt = Game.GameTime };
            try
            {
                int jet = Function.Call<int>(Hash.CREATE_VEHICLE, vm, spawn.X, spawn.Y, spawn.Z, HeadingTo(spawn, p), false, false);
                if (jet == 0) return false;
                unit.VehicleHandle = jet;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, jet, true, true);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, jet, true, true, false);
                Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, jet, 65f);
                int pilot = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, jet, 6, pm, -1, false, false);
                if (pilot != 0)
                {
                    unit.PedHandles.Add(pilot);
                    SetupResponsePed(pilot, true);
                    Function.Call(Hash.TASK_COMBAT_PED, pilot, player.Handle, 0, 16);
                }
                _units.Add(unit);
                if (log != null) log("Sixth-star air-force jet deployed after collateral check.");
                return true;
            }
            catch (Exception ex)
            {
                if (log != null) log("Jet dispatch failed: " + ex.Message);
                CleanupUnit(unit);
                return false;
            }
            finally
            {
                Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, vm);
                Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, pm);
            }
        }

        private static string ChooseAvailableModel(string[] pool, string fallback)
        {
            if (pool == null || pool.Length == 0) return fallback;
            int start = Math.Abs((Game.GameTime / 7000) % pool.Length);
            for (int n = 0; n < pool.Length; n++)
            {
                string name = pool[(start + n) % pool.Length];
                int h = 0;
                try
                {
                    h = Function.Call<int>(Hash.GET_HASH_KEY, name);
                    if (h != 0 && Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, h) && Function.Call<bool>(Hash.IS_MODEL_VALID, h) && Function.Call<bool>(Hash.IS_MODEL_A_VEHICLE, h)) return name;
                }
                catch { }
            }
            return fallback;
        }

        private static void SetupResponsePed(int h, bool lethal)
        {
            if (!EntityExists(h)) return;
            try
            {
                Function.Call(Hash.SET_PED_AS_COP, h, true);
                Function.Call(Hash.SET_PED_ACCURACY, h, lethal ? 48 : 18);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, true);
                Function.Call(Hash.SET_PED_COMBAT_ABILITY, h, lethal ? 2 : 1);
                int weapon = Function.Call<int>(Hash.GET_HASH_KEY, lethal ? "WEAPON_CARBINERIFLE" : "WEAPON_STUNGUN");
                Function.Call(Hash.GIVE_WEAPON_TO_PED, h, weapon, lethal ? 300 : 20, false, true);
            }
            catch { }
        }

        private static bool IsRural(Vector3 p)
        {
            try
            {
                string z = (Function.Call<string>(Hash.GET_NAME_OF_ZONE, p.X, p.Y, p.Z) ?? string.Empty).ToUpperInvariant();
                switch (z)
                {
                    case "SANDY": case "GRAPES": case "PALETO": case "DESRT": case "ALAMO": case "ZANCUDO": case "HARMO": case "GREATC": case "MTCHIL": case "MTGORDO": case "MTJOSE": return true;
                    default: return false;
                }
            }
            catch { return false; }
        }

        private static bool EnsureModel(int h)
        {
            if (h == 0 || !Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, h) || !Function.Call<bool>(Hash.IS_MODEL_VALID, h)) return false;
            Function.Call(Hash.REQUEST_MODEL, h);
            return Function.Call<bool>(Hash.HAS_MODEL_LOADED, h);
        }

        private static Vector3 FindGroundSpawn(Ped player, float distance)
        {
            double a = ((Game.GameTime / 137) % 6283) / 1000.0;
            Vector3 p = player.Position;
            Vector3 seed = new Vector3(p.X + (float)Math.Cos(a) * distance, p.Y + (float)Math.Sin(a) * distance, p.Z);
            try
            {
                Vector3 street = World.GetNextPositionOnStreet(seed);
                if (street != Vector3.Zero) return street;
            }
            catch { }
            return seed;
        }

        private static float HeadingTo(Vector3 from, Vector3 to)
        {
            try { return Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D, to.X - from.X, to.Y - from.Y); }
            catch { return 0f; }
        }

        private void CleanupBelowTier(int tier)
        {
            for (int i = _units.Count - 1; i >= 0; i--)
            {
                if (_units[i].RequiredLevel >= tier) continue;
                CleanupUnit(_units[i]);
                _units.RemoveAt(i);
            }
        }

        private void CleanupInvalidUnits(Ped player, int level)
        {
            for (int i = _units.Count - 1; i >= 0; i--)
            {
                CustomUnit u = _units[i];
                bool remove = level <= 0 || level < u.RequiredLevel || !EntityExists(u.VehicleHandle);
                if (!remove && player != null && player.Exists())
                {
                    Vector3 pos = GetEntityPosition(u.VehicleHandle);
                    if (pos != Vector3.Zero && Perception.Distance(pos, player.Position) > 2200f) remove = true;
                }
                if (!remove && Game.GameTime - u.CreatedAt > 480000) remove = true;
                if (remove)
                {
                    CleanupUnit(u);
                    _units.RemoveAt(i);
                }
            }
        }

        private static void CleanupUnit(CustomUnit u)
        {
            if (u == null) return;
            foreach (int p in u.PedHandles) DeleteEntity(p);
            DeleteEntity(u.VehicleHandle);
        }

        private static bool EntityExists(int h) { return h != 0 && Function.Call<bool>(Hash.DOES_ENTITY_EXIST, h); }
        private static Vector3 GetEntityPosition(int h)
        {
            if (!EntityExists(h)) return Vector3.Zero;
            try { return Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, h, true); }
            catch { return Vector3.Zero; }
        }
        private static void DeleteEntity(int h)
        {
            if (!EntityExists(h)) return;
            try
            {
                Entity e = Entity.FromHandle(h);
                if (e != null && e.Exists()) e.Delete();
            }
            catch { }
        }
        private static bool SafeBool(Hash h, params InputArgument[] a)
        {
            try { return Function.Call<bool>(h, a); }
            catch { return false; }
        }
    }
}
