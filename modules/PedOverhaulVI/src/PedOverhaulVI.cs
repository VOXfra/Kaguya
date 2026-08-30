using GTA;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VOX.PedOverhaulVI
{
    public sealed class PedOverhaulVIScript : Script
    {
        private const string ConfigPath = "scripts\\PedOverhaulVI.ini";
        private const string DataDirectory = "scripts\\PedOverhaulVI";
        private const string LogPath = DataDirectory + "\\PedOverhaulVI.log";

        private Config _cfg;
        private readonly Dictionary<int, PedState> _states = new Dictionary<int, PedState>();
        private readonly List<Ped> _nearby = new List<Ped>();
        private readonly SceneRuntime _sceneRuntime = new SceneRuntime();
        private int _lastRefresh;
        private int _cursor;
        private bool _policeModuleLoaded;
        private int _lastModuleProbe;
        private int _missionFlagSince;

        public PedOverhaulVIScript()
        {
            Directory.CreateDirectory(DataDirectory);
            _cfg = Config.Load(ConfigPath);
            Interval = Math.Max(10, _cfg.TickIntervalMs);
            Tick += OnTick;
            Aborted += OnAborted;
            ProbeModules();
            Log("Ped Overhaul VI 0.3.1 vehicle-occupant hotfix loaded.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_cfg.Enabled) return;
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) return;
                if (Game.GameTime - _lastModuleProbe > 3000) ProbeModules();
                if (_cfg.DisableDuringRockstarMissions && ShouldYieldToMission()) return;

                RefreshNearby(player);
                _sceneRuntime.Update(player, _nearby, _states, _cfg, Log);
                if (_nearby.Count == 0) return;

                int budget = Math.Max(1, Math.Min(12, _cfg.PedsPerTick));
                for (int n = 0; n < budget && _nearby.Count > 0; n++)
                {
                    if (_cursor >= _nearby.Count) _cursor = 0;
                    Ped ped = _nearby[_cursor++];
                    ProcessPed(player, ped);
                }
                CleanupStates();
            }
            catch (Exception ex)
            {
                Log("Tick error: " + ex);
            }
        }

        private void ProcessPed(Ped player, Ped ped)
        {
            if (!UsablePed(ped, player)) return;
            PedState state;
            if (!_states.TryGetValue(ped.Handle, out state))
            {
                state = PedState.Create(ped, _cfg);
                _states[ped.Handle] = state;
            }

            AwarenessStage previousStage = state.Stage;

            // First: what this ped individually knows about the player.
            PerceptionFrame playerFrame = SituationModel.Sense(ped, player, _nearby, _states, _cfg);
            SituationModel.UpdateCognition(state, playerFrame, _cfg, Game.GameTime);

            // Second: what is happening in the rest of the scene. This is a
            // shared event model, so a fight between two NPCs, an approaching
            // car or another shooter's gunfire can become the dominant threat.
            ScenePerception scene = _sceneRuntime.Sense(ped, state, _nearby, _states, _cfg);
            _sceneRuntime.ApplyCognition(state, scene, _cfg, Game.GameTime);

            if (previousStage != state.Stage && _cfg.LogStateTransitions)
            {
                Log("Ped " + state.Handle + " " + state.Archetype + " awareness " + previousStage + " -> " + state.Stage +
                    " [att=" + (int)state.Attention + " susp=" + (int)state.Suspicion + " cert=" + (int)state.Certainty + " fear=" + (int)state.Fear +
                    ", playerWeapon=" + state.SawWeapon + ", mask=" + state.SawMask + ", scene=" + state.SceneThreatKind + ", extConf=" + (int)state.ExternalThreatConfidence + "].");
            }

            DecisionSystem.UpdateMorale(player, ped, state, _nearby, _cfg, Log);

            // Non-player scene threats get their own decisions. If no external
            // threat is currently dominant, the existing player-centric system
            // handles the ped as before.
            bool sceneHandled = SceneDecisionSystem.TryUpdate(player, ped, state, scene, _nearby, _states, _cfg, Log);
            if (!sceneHandled)
                DecisionSystem.Update(player, ped, state, playerFrame, _nearby, _states, _cfg, Log);
        }

        private bool ShouldYieldToMission()
        {
            bool cut = false, switching = false, flag = false;
            try { cut = Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE); } catch { }
            try { switching = Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS); } catch { }
            try { flag = Function.Call<bool>(Hash.GET_MISSION_FLAG); } catch { }
            if (cut || switching) return true;

            int wanted = 0;
            try { wanted = Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle); } catch { }
            bool shooting = false;
            try { shooting = Function.Call<bool>(Hash.IS_PED_SHOOTING, Game.LocalPlayerPed.Handle); } catch { }
            if (!flag) { _missionFlagSince = 0; return false; }
            if (wanted > 0 || shooting) return false;
            if (_missionFlagSince == 0) _missionFlagSince = Game.GameTime;
            return Game.GameTime - _missionFlagSince > 1800;
        }

        private void RefreshNearby(Ped player)
        {
            if (Game.GameTime - _lastRefresh < Math.Max(150, _cfg.RefreshNearbyPedsMs)) return;
            _lastRefresh = Game.GameTime;
            _nearby.Clear();
            Ped[] peds;
            try { peds = World.GetNearbyPeds(player, _cfg.ProcessRadius); }
            catch { return; }

            foreach (Ped p in peds)
            {
                if (_nearby.Count >= Math.Max(10, _cfg.MaxProcessedPeds + 10)) break;
                if (p == null || !p.Exists() || p.Handle == player.Handle || !p.IsHuman) continue;

                _nearby.Add(p);

                // 0.3.1 safety boundary: vehicle occupants remain visible to the
                // shared scene scanner, but Ped Overhaul never assigns them an
                // on-foot task. GTA's driving AI retains ownership until the
                // dedicated vehicle-occupant behaviour layer is implemented.
                bool inVehicle = false;
                try { inVehicle = !p.IsDead && p.IsInVehicle(); } catch { }
                if (inVehicle)
                {
                    _states.Remove(p.Handle);
                    continue;
                }

                if (!p.IsDead && UsablePed(p, player) && !_states.ContainsKey(p.Handle))
                    _states[p.Handle] = PedState.Create(p, _cfg);
            }
        }

        private bool UsablePed(Ped p, Ped player)
        {
            if (p == null || !p.Exists() || p.Handle == player.Handle || p.IsDead || !p.IsHuman) return false;

            // Never feed pedestrian/navmesh/cower/flee tasks to an occupant.
            // Those natives can make ambient drivers abandon their vehicles.
            try { if (p.IsInVehicle()) return false; } catch { }

            if (_cfg.SkipMissionEntities)
            {
                try { if (Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, p.Handle)) return false; }
                catch { }
            }
            if (_cfg.PoliceOverhaulOwnsLawPeds && _policeModuleLoaded && IsLawPed(p)) return false;
            return true;
        }

        private static bool IsLawPed(Ped p)
        {
            try
            {
                int t = (int)p.PedType;
                return t == 6 || t == 27 || t == 29;
            }
            catch { return false; }
        }

        private void ProbeModules()
        {
            _lastModuleProbe = Game.GameTime;
            try
            {
                _policeModuleLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => string.Equals(a.GetName().Name, "PoliceOverhaulVI", StringComparison.OrdinalIgnoreCase));
            }
            catch { _policeModuleLoaded = false; }
        }

        private void CleanupStates()
        {
            var live = new HashSet<int>(_nearby.Where(p => p != null && p.Exists() && !p.IsDead && !SafeInVehicle(p)).Select(p => p.Handle));
            var remove = _states.Keys.Where(h => !live.Contains(h)).Take(16).ToList();
            foreach (int h in remove) _states.Remove(h);
        }

        private static bool SafeInVehicle(Ped p)
        {
            try { return p != null && p.Exists() && p.IsInVehicle(); }
            catch { return false; }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _states.Clear();
            _nearby.Clear();
        }

        private void Log(string message)
        {
            if (_cfg != null && !_cfg.DebugLogging) return;
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
