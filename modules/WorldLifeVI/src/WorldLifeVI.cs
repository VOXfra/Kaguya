using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;

namespace VOX.WorldLifeVI
{
    public sealed class WorldLifeVIScript : Script
    {
        private const string ConfigPath = "scripts\\WorldLifeVI.ini";
        private const string DataDirectory = "scripts\\WorldLifeVI";
        private const string LogPath = DataDirectory + "\\WorldLifeVI.log";

        private readonly Random _random = new Random();
        private Config _cfg;
        private int _lastContext;
        private int _lastOnlineCheck;
        private int _storyYieldUntil;
        private float _pedDensity = 1f;
        private float _scenarioPedDensity = 1f;
        private float _vehicleDensity = 1f;
        private float _parkedDensity = 1f;
        private PendingSwap _pending;
        private readonly HashSet<int> _onlineHashes = new HashSet<int>();

        private static readonly Dictionary<int, string[]> OnlinePools = new Dictionary<int, string[]>
        {
            { 0, new [] { "brioso2", "club", "issi7", "weevil", "kanjosj" } },
            { 1, new [] { "tailgater2", "deity", "cinquemila", "rhinehart", "schafter5" } },
            { 2, new [] { "astron", "iwagen", "jubilee", "baller7", "toros", "rebla", "novak", "granger2" } },
            { 3, new [] { "kanjo", "postlude", "previon", "windsor2", "zion3" } },
            { 4, new [] { "gauntlet3", "gauntlet4", "gauntlet5", "dominator3", "dominator7", "dominator8", "buffalo4", "vigero2", "tulip2" } },
            { 5, new [] { "comet3", "comet5", "comet6", "comet7", "fagaloa", "retinue", "retinue2", "savestra" } },
            { 6, new [] { "jester3", "jester4", "euros", "remus", "zr350", "calico", "growler", "vectre", "cypher", "komoda", "jugular", "drafter", "neo", "paragon" } },
            { 7, new [] { "krieger", "emerus", "thrax", "zorrusso", "tigon", "italirsx" } },
            { 9, new [] { "caracara2", "everon", "hellion", "kamacho", "draugur", "boor" } }
        };

        public WorldLifeVIScript()
        {
            Directory.CreateDirectory(DataDirectory);
            _cfg = Config.Load(ConfigPath);
            Interval = 0;
            BuildOnlineHashSet();
            Tick += OnTick;
            Aborted += OnAborted;
            Log("World Life VI 0.3.0 independent hourly population + story-safe Online civilian runtime loaded.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_cfg.Enabled) return;
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { CancelPending(); return; }

                if (_cfg.DisableDuringMissions && StoryOwnsScene())
                {
                    _storyYieldUntil = Game.GameTime + 5000;
                    CancelPending();
                    return;
                }
                if (Game.GameTime < _storyYieldUntil)
                {
                    CancelPending();
                    return;
                }

                if (_cfg.DynamicPopulation)
                {
                    if (Game.GameTime - _lastContext >= Math.Max(500, _cfg.ContextRefreshMs))
                    {
                        _lastContext = Game.GameTime;
                        RefreshDensityContext(player);
                    }
                    ApplyDensityThisFrame();
                }

                if (_cfg.OnlineVehicles && !PlayerWanted()) UpdateOnlineVehicles(player);
                else CancelPending();
            }
            catch (Exception ex) { Log("Tick error: " + ex); }
        }

        private void RefreshDensityContext(Ped player)
        {
            int hour = 12;
            string zone = string.Empty;
            try { hour = Function.Call<int>(Hash.GET_CLOCK_HOURS); } catch { }
            try { zone = Function.Call<string>(Hash.GET_NAME_OF_ZONE, player.Position.X, player.Position.Y, player.Position.Z) ?? string.Empty; } catch { }

            bool rural = IsRural(zone);
            bool beach = IsBeach(zone);
            bool busy = IsBusyUrban(zone);
            bool night = hour >= 1 && hour < 6;
            bool evening = hour >= 18 && hour < 24;

            float ambientPedBase;
            float trafficBase;
            if (rural)
            {
                ambientPedBase = night ? _cfg.RuralPedNight : _cfg.RuralPedDay;
                trafficBase = _cfg.RuralTraffic;
            }
            else
            {
                ambientPedBase = night ? _cfg.CityPedNight : (evening ? _cfg.CityPedEvening : _cfg.CityPedDay);
                if (beach && hour >= 9 && hour < 20) ambientPedBase = Math.Max(ambientPedBase, _cfg.BeachPedDay);
                if (busy && hour >= 8 && hour < 23) ambientPedBase += _cfg.BusyPedBonus;
                trafficBase = _cfg.CityTraffic;
            }

            float ped = ambientPedBase * AmbientPedHourFactor(hour, rural);
            float scenario = ambientPedBase * ScenarioPedHourFactor(hour, rural, beach);
            float moving = trafficBase * TrafficHourFactor(hour, rural);
            float parked = _cfg.ParkedVehicle * ParkedHourFactor(hour, rural);

            int pedCount = 0, vehicleCount = 0;
            try { pedCount = World.GetNearbyPeds(player, _cfg.BudgetRadius).Length; } catch { }
            try { vehicleCount = World.GetNearbyVehicles(player, _cfg.BudgetRadius).Length; } catch { }

            ped = ApplyBudget(ped, pedCount, _cfg.SoftPedBudget, _cfg.HardPedBudget);
            scenario = ApplyBudget(scenario, pedCount, _cfg.SoftPedBudget, _cfg.HardPedBudget);
            moving = ApplyBudget(moving, vehicleCount, _cfg.SoftVehicleBudget, _cfg.HardVehicleBudget);
            parked = ApplyBudget(parked, vehicleCount, _cfg.SoftVehicleBudget, _cfg.HardVehicleBudget);

            _pedDensity = Clamp(ped, 0.42f, _cfg.MaxPedMultiplier);
            _scenarioPedDensity = Clamp(scenario, 0.38f, _cfg.MaxPedMultiplier);
            _vehicleDensity = Clamp(moving, 0.55f, _cfg.MaxVehicleMultiplier);
            _parkedDensity = Clamp(parked, 0.50f, _cfg.MaxVehicleMultiplier);
        }

        private static float AmbientPedHourFactor(int hour, bool rural)
        {
            float f;
            if (hour < 5) f = 0.62f;
            else if (hour < 7) f = 0.78f;
            else if (hour < 10) f = 0.94f;
            else if (hour < 17) f = 1.02f;
            else if (hour < 22) f = 1.08f;
            else f = 0.88f;
            return rural ? 0.92f + (f - 1f) * 0.65f : f;
        }

        private static float ScenarioPedHourFactor(int hour, bool rural, bool beach)
        {
            float f;
            if (hour < 6) f = 0.45f;
            else if (hour < 9) f = 0.72f;
            else if (hour < 12) f = 0.96f;
            else if (hour < 18) f = 1.08f;
            else if (hour < 22) f = 1.04f;
            else f = 0.68f;
            if (beach && hour >= 10 && hour < 19) f *= 1.08f;
            if (rural) f *= 0.90f;
            return f;
        }

        private static float TrafficHourFactor(int hour, bool rural)
        {
            float f;
            if (hour < 5) f = 0.70f;
            else if (hour < 7) f = 0.88f;
            else if (hour < 10) f = 1.16f;      // morning commute
            else if (hour < 16) f = 1.00f;
            else if (hour < 20) f = 1.20f;      // evening commute
            else if (hour < 23) f = 1.02f;
            else f = 0.84f;
            return rural ? 0.94f + (f - 1f) * 0.45f : f;
        }

        private static float ParkedHourFactor(int hour, bool rural)
        {
            float f;
            if (hour < 6) f = 1.08f;
            else if (hour < 10) f = 0.92f;
            else if (hour < 17) f = 0.98f;
            else if (hour < 21) f = 0.94f;
            else f = 1.06f;
            return rural ? f * 0.80f : f;
        }

        private void ApplyDensityThisFrame()
        {
            Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME, _pedDensity);
            Function.Call(Hash.SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME, _scenarioPedDensity, _scenarioPedDensity);
            Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, _vehicleDensity);
            Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, _vehicleDensity);
            Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, _parkedDensity);
        }

        private void UpdateOnlineVehicles(Ped player)
        {
            int now = Game.GameTime;
            if (_pending != null) { ProcessPendingSwap(player, now); return; }
            if (now - _lastOnlineCheck < Math.Max(2500, _cfg.OnlineVehicleCheckMs)) return;
            _lastOnlineCheck = now;
            if (_random.Next(0, 100) >= Math.Max(0, Math.Min(100, _cfg.OnlineVehicleChancePercent))) return;

            Vehicle donor = FindDonor(player);
            if (donor == null) return;
            int vehicleClass = SafeVehicleClass(donor);
            string[] pool;
            if (!OnlinePools.TryGetValue(vehicleClass, out pool) || pool == null || pool.Length == 0) return;

            int start = _random.Next(pool.Length);
            for (int i = 0; i < pool.Length; i++)
            {
                string modelName = pool[(start + i) % pool.Length];
                int hash = Function.Call<int>(Hash.GET_HASH_KEY, modelName);
                if (!IsUsableVehicleModel(hash) || donor.Model.Hash == hash) continue;
                Function.Call(Hash.REQUEST_MODEL, hash);
                _pending = new PendingSwap { DonorHandle = donor.Handle, ModelHash = hash, RequestedAt = now };
                return;
            }
        }

        private void ProcessPendingSwap(Ped player, int now)
        {
            if (_pending == null) return;
            if (StoryOwnsScene() || PlayerWanted()) { CancelPending(); return; }
            if (now - _pending.RequestedAt > Math.Max(1000, _cfg.OnlineVehicleMaxRequestMs)) { CancelPending(); return; }

            bool loaded = false;
            try { loaded = Function.Call<bool>(Hash.HAS_MODEL_LOADED, _pending.ModelHash); } catch { }
            if (!loaded) return;

            Entity donorEntity = null;
            try { donorEntity = Entity.FromHandle(_pending.DonorHandle); } catch { }
            Vehicle donor = donorEntity as Vehicle;
            if (donor == null || !donor.Exists() || !DonorStillSafe(player, donor)) { CancelPending(); return; }

            SwapVehicle(donor, _pending.ModelHash);
            ReleaseModel(_pending.ModelHash);
            _pending = null;
        }

        private Vehicle FindDonor(Ped player)
        {
            Vehicle[] vehicles;
            try { vehicles = World.GetNearbyVehicles(player, _cfg.OnlineVehicleMaxDistance); }
            catch { return null; }

            Vehicle best = null;
            float bestScore = float.MinValue;
            foreach (Vehicle v in vehicles)
            {
                if (v == null || !v.Exists() || v.Handle == SafeCurrentVehicleHandle(player)) continue;
                if (!DonorStillSafe(player, v)) continue;
                float d = Distance(player.Position, v.Position);
                if (d < _cfg.OnlineVehicleMinDistance || d > _cfg.OnlineVehicleMaxDistance) continue;
                float speed = SafeSpeed(v);
                if (speed > 1f && !_cfg.ReplaceMovingTraffic) continue;
                if (speed <= 1f && !_cfg.ReplaceParkedVehicles) continue;
                if (_onlineHashes.Contains(v.Model.Hash) || HasLineOfSight(player, v)) continue;
                float score = d - speed * 0.7f + (v.Driver != null && v.Driver.Exists() ? 4f : 0f);
                if (score > bestScore) { bestScore = score; best = v; }
            }
            return best;
        }

        private bool DonorStillSafe(Ped player, Vehicle v)
        {
            if (v == null || !v.Exists()) return false;
            try { if (Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, v.Handle)) return false; } catch { return false; }
            int cls = SafeVehicleClass(v);
            if (!OnlinePools.ContainsKey(cls)) return false;
            try
            {
                int passengers = Function.Call<int>(Hash.GET_VEHICLE_NUMBER_OF_PASSENGERS, v.Handle);
                if (passengers > 0) return false;
            }
            catch { return false; }

            Ped driver = null;
            try { driver = v.Driver; } catch { }
            if (driver != null && driver.Exists())
            {
                try { if (Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, driver.Handle)) return false; } catch { return false; }
                if (IsLawPed(driver)) return false;
            }

            float d = Distance(player.Position, v.Position);
            return d >= _cfg.OnlineVehicleMinDistance && d <= _cfg.OnlineVehicleMaxDistance + 10f && !HasLineOfSight(player, v);
        }

        private void SwapVehicle(Vehicle donor, int modelHash)
        {
            Vector3 pos = donor.Position;
            Vector3 velocity = Vector3.Zero;
            float heading = 0f;
            int driverHandle = 0;
            try { velocity = Function.Call<Vector3>(Hash.GET_ENTITY_VELOCITY, donor.Handle); } catch { }
            try { heading = Function.Call<float>(Hash.GET_ENTITY_HEADING, donor.Handle); } catch { }
            try { driverHandle = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, donor.Handle, -1, false); } catch { }

            int newHandle = 0;
            try { newHandle = Function.Call<int>(Hash.CREATE_VEHICLE, modelHash, pos.X, pos.Y, pos.Z, heading, false, false, false); } catch { }
            if (newHandle == 0) return;

            try { Function.Call(Hash.SET_ENTITY_VELOCITY, newHandle, velocity.X, velocity.Y, velocity.Z); } catch { }
            try { Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, newHandle, 5.0f); } catch { }
            if (driverHandle != 0)
            {
                try { Function.Call(Hash.SET_PED_INTO_VEHICLE, driverHandle, newHandle, -1); } catch { }
                try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, newHandle, true, true, false); } catch { }
            }

            int oldHandle = donor.Handle;
            try { donor.Delete(); } catch { }
            Log("Online civilian vehicle integrated off-screen donor=" + oldHandle + " model=" + modelHash + ".");
        }

        private static bool StoryOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try
            {
                if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) ||
                    Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true;
            }
            catch { }
            return false;
        }

        private static bool PlayerWanted()
        {
            try { return Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle) > 0; }
            catch { return true; }
        }

        private static bool IsLawPed(Ped p)
        {
            try { int t = (int)p.PedType; return t == 6 || t == 27 || t == 29; }
            catch { return true; }
        }

        private void BuildOnlineHashSet()
        {
            foreach (var pair in OnlinePools)
                foreach (string name in pair.Value)
                    try { _onlineHashes.Add(Function.Call<int>(Hash.GET_HASH_KEY, name)); } catch { }
        }

        private static bool IsUsableVehicleModel(int hash)
        {
            try { return hash != 0 && Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, hash) && Function.Call<bool>(Hash.IS_MODEL_VALID, hash) && Function.Call<bool>(Hash.IS_MODEL_A_VEHICLE, hash); }
            catch { return false; }
        }
        private static int SafeVehicleClass(Vehicle v) { try { return Function.Call<int>(Hash.GET_VEHICLE_CLASS, v.Handle); } catch { return -1; } }
        private static int SafeCurrentVehicleHandle(Ped p) { try { return p.IsInVehicle() && p.CurrentVehicle != null ? p.CurrentVehicle.Handle : 0; } catch { return 0; } }
        private static float SafeSpeed(Entity e) { try { return Function.Call<float>(Hash.GET_ENTITY_SPEED, e.Handle); } catch { return 0f; } }
        private static bool HasLineOfSight(Ped player, Entity e) { try { return Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, player.Handle, e.Handle, 17); } catch { return true; } }

        private static float ApplyBudget(float target, int count, int soft, int hard)
        {
            if (hard <= soft) return count >= hard ? Math.Min(1f, target) : target;
            if (count <= soft) return target;
            if (count >= hard) return Math.Min(1f, target);
            float t = (count - soft) / (float)(hard - soft);
            return target + (Math.Min(1f, target) - target) * t;
        }

        private static bool IsRural(string z)
        {
            switch ((z ?? string.Empty).ToUpperInvariant())
            {
                case "SANDY": case "GRAPES": case "PALETO": case "DESRT": case "ALAMO": case "ZANCUDO":
                case "HARMO": case "GREATC": case "MTCHIL": case "MTGORDO": case "MTJOSE": case "CANNY":
                case "TATAMO": case "LAGO": case "PALCOV": case "PROCOB": case "ARMYB": case "NCHU": return true;
                default: return false;
            }
        }
        private static bool IsBeach(string z) { z = (z ?? string.Empty).ToUpperInvariant(); return z == "DELPE" || z == "BEACH" || z == "VESPU" || z == "VCANA"; }
        private static bool IsBusyUrban(string z) { z = (z ?? string.Empty).ToUpperInvariant(); return z == "DOWNT" || z == "PBOX" || z == "TEXTI" || z == "SKID" || z == "VESP" || z == "DELPE" || z == "VCANA" || z == "HAWICK" || z == "ALTA"; }
        private static float Clamp(float v, float min, float max) { return Math.Max(min, Math.Min(max, v)); }
        private static float Distance(Vector3 a, Vector3 b) { double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z; return (float)Math.Sqrt(x*x + y*y + z*z); }
        private static void ReleaseModel(int hash) { if (hash == 0) return; try { Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, hash); } catch { } }

        private void CancelPending()
        {
            if (_pending == null) return;
            ReleaseModel(_pending.ModelHash);
            _pending = null;
        }
        private void OnAborted(object sender, EventArgs e) { CancelPending(); }
        private void Log(string message)
        {
            if (_cfg != null && !_cfg.DebugLogging) return;
            try { Directory.CreateDirectory(DataDirectory); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + message + Environment.NewLine); } catch { }
        }

        private sealed class PendingSwap { public int DonorHandle; public int ModelHash; public int RequestedAt; }
    }
}
