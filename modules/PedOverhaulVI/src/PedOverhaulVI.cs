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
            Log("Ped Overhaul VI 0.5.0 causal-social-memory runtime loaded.");
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

            int now = Game.GameTime;
            AwarenessStage previousStage = state.Stage;

            DistractionRuntime.Update(ped, state, _nearby, _cfg, Log);

            PerceptionFrame playerFrame = SituationModel.Sense(ped, player, _nearby, _states, _cfg);
            DistractionRuntime.ApplyToPerception(ped, state, playerFrame, _cfg);
            SituationModel.UpdateCognition(state, playerFrame, _cfg, now);
            PedOverhaulVIBridge.ObserveAndApply(state, playerFrame, now);

            ScenePerception scene = _sceneRuntime.Sense(ped, state, _nearby, _states, _cfg);
            DistractionRuntime.ApplyToScenePerception(state, scene, _cfg);
            if (ShouldApplySceneCognition(state, scene, now))
                _sceneRuntime.ApplyCognition(state, scene, _cfg, now);

            SocialMemoryRuntime.Update(ped, state, scene, _nearby, _states, _cfg);
            StabilizeCognition(state, playerFrame, scene);

            if (previousStage != state.Stage && _cfg.LogStateTransitions)
            {
                Log("Ped " + state.Handle + " " + state.Archetype + " awareness " + previousStage + " -> " + state.Stage +
                    " [att=" + (int)state.Attention + " susp=" + (int)state.Suspicion + " cert=" + (int)state.Certainty + " fear=" + (int)state.Fear +
                    ", playerWeapon=" + state.SawWeapon + ", mask=" + state.SawMask + ", distraction=" + state.Distraction +
                    ", scene=" + state.SceneThreatKind + ", cause=" + state.KnownThreatKind + ":" + state.KnownThreatSourceHandle +
                    ", causeConf=" + (int)state.KnownThreatConfidence + ", hops=" + state.KnowledgeHops +
                    ", playerRecognition=" + (int)state.RecognitionOfPlayer + ", extConf=" + (int)state.ExternalThreatConfidence + "].");
            }

            DecisionSystem.UpdateMorale(player, ped, state, _nearby, _cfg, Log);

            bool sceneHandled = PreservePanicSurvivalAction(state, scene);
            if (!sceneHandled)
                sceneHandled = SceneDecisionSystem.TryUpdate(player, ped, state, scene, _nearby, _states, _cfg, Log);
            if (!sceneHandled && !IsPureAmbientNotice(state, playerFrame, scene))
                DecisionSystem.Update(player, ped, state, playerFrame, _nearby, _states, _cfg, Log);
        }

        private bool ShouldApplySceneCognition(PedState state, ScenePerception scene, int now)
        {
            if (state == null || scene == null || !scene.HasThreat) return false;
            int interval;
            switch (scene.Kind)
            {
                case SceneThreatKind.VehicleHazard: interval = 350; break;
                case SceneThreatKind.Explosion: interval = 450; break;
                case SceneThreatKind.Gunfire: interval = 550; break;
                case SceneThreatKind.Fire: interval = 650; break;
                case SceneThreatKind.Fight: interval = 800; break;
                case SceneThreatKind.VisibleWeapon: interval = 950; break;
                case SceneThreatKind.Body: interval = 1400; break;
                case SceneThreatKind.CrowdFlight:
                case SceneThreatKind.SocialWarning: interval = 1200; break;
                default: interval = 1000; break;
            }
            if (state.LastSceneEventAt <= 0) return true;
            int elapsed = now - state.LastSceneEventAt;
            if (elapsed >= interval) return true;
            if (scene.Immediate && elapsed >= Math.Min(interval, Math.Max(300, _cfg.EmergencyReplanMinMs))) return true;
            return false;
        }

        private void StabilizeCognition(PedState state, PerceptionFrame frame, ScenePerception scene)
        {
            if (state == null || frame == null) return;
            bool hasScene = scene != null && scene.HasThreat;
            bool recentViolentContext = state.SawViolence || state.HeardGunshot || state.HeardExternalGunfire || state.WasDirectlyAimedAt || state.HeardExplosion || state.SawFire || state.SawVehicleHazard;
            bool recentHardContext = recentViolentContext || state.SawWeapon || state.SawExternalWeapon;

            bool maskOnly = frame.SeesMask && !frame.SeesWeapon && !frame.DirectlyAimedAt && !frame.SeesShooting && !frame.HearsGunshot && !frame.SeesBody && !frame.CrowdPanic && !frame.QuietWithdrawal && !frame.HostileRelationship && !hasScene && !recentHardContext;
            if (maskOnly)
            {
                state.Suspicion = Math.Min(state.Suspicion, 6f);
                state.Certainty = Math.Min(state.Certainty, 8f);
                state.Fear = Math.Min(state.Fear, 4f);
            }

            if (hasScene)
            {
                if (scene.Kind == SceneThreatKind.Body && !recentHardContext)
                {
                    state.Certainty = Math.Min(state.Certainty, _cfg.ThreatConfirmedThreshold - 5f);
                    state.Fear = Math.Min(state.Fear, _cfg.PanicThreshold - 10f);
                }
                if (scene.Kind == SceneThreatKind.VisibleWeapon && !recentViolentContext)
                    state.Fear = Math.Min(state.Fear, _cfg.PanicThreshold - 8f);
                // Hearsay cannot create more certainty than the source knowledge
                // that reached this ped. Each social hop therefore has a hard cap.
                if ((scene.Kind == SceneThreatKind.SocialWarning || scene.Kind == SceneThreatKind.CrowdFlight) && state.KnowledgeHops > 0)
                {
                    float cap = Math.Max(_cfg.NoticedThreshold, state.KnownThreatConfidence + 12f);
                    state.Certainty = Math.Min(state.Certainty, cap);
                }
            }

            bool pureAmbient = !frame.HasAnyStimulus && !hasScene && !recentHardContext && state.Suspicion < _cfg.NoticedThreshold && state.Certainty < _cfg.NoticedThreshold && state.Fear < _cfg.NoticedThreshold;
            if (pureAmbient)
            {
                state.Attention = Math.Min(state.Attention, _cfg.NoticedThreshold - 2f);
                state.Stage = AwarenessStage.Unaware;
                return;
            }
            state.Stage = SituationModel.DetermineStage(state, _cfg);
        }

        private static bool PreservePanicSurvivalAction(PedState state, ScenePerception scene)
        {
            if (state == null || scene == null || !scene.HasThreat || state.Stage != AwarenessStage.Panic) return false;
            if (scene.Kind != SceneThreatKind.Body) return false;
            switch (state.Mode)
            {
                case ReactionMode.Freeze: case ReactionMode.Cower: case ReactionMode.Flee: case ReactionMode.Cover:
                case ReactionMode.Surrender: case ReactionMode.Combat: case ReactionMode.Evade: case ReactionMode.DriveAway: return true;
                default: return false;
            }
        }

        private static bool IsPureAmbientNotice(PedState state, PerceptionFrame frame, ScenePerception scene)
        {
            if (state == null || frame == null) return false;
            if (state.Stage != AwarenessStage.Noticed && state.Stage != AwarenessStage.Unaware) return false;
            if (frame.HasAnyStimulus) return false;
            if (scene != null && scene.HasThreat) return false;
            if (state.SawWeapon || state.SawViolence || state.HeardGunshot || state.WasDirectlyAimedAt || state.SawExternalWeapon || state.HeardExternalGunfire || state.SawFire || state.HeardExplosion || state.SawVehicleHazard) return false;
            return state.Suspicion < 12f && state.Certainty < 12f && state.Fear < 12f;
        }

        private bool ShouldYieldToMission()
        {
            bool cut=false,switching=false,flag=false;
            try{cut=Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE);}catch{}
            try{switching=Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS);}catch{}
            try{flag=Function.Call<bool>(Hash.GET_MISSION_FLAG);}catch{}
            if(cut||switching)return true;
            int wanted=0;try{wanted=Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL,Game.Player.Handle);}catch{}
            bool shooting=false;try{shooting=Function.Call<bool>(Hash.IS_PED_SHOOTING,Game.LocalPlayerPed.Handle);}catch{}
            if(!flag){_missionFlagSince=0;return false;}if(wanted>0||shooting)return false;if(_missionFlagSince==0)_missionFlagSince=Game.GameTime;return Game.GameTime-_missionFlagSince>1800;
        }

        private void RefreshNearby(Ped player)
        {
            if(Game.GameTime-_lastRefresh<Math.Max(150,_cfg.RefreshNearbyPedsMs))return;_lastRefresh=Game.GameTime;_nearby.Clear();
            Ped[] peds;try{peds=World.GetNearbyPeds(player,_cfg.ProcessRadius);}catch{return;}
            foreach(Ped p in peds)
            {
                if(_nearby.Count>=Math.Max(10,_cfg.MaxProcessedPeds+10))break;
                if(p==null||!p.Exists()||p.Handle==player.Handle||!p.IsHuman)continue;
                _nearby.Add(p);
                bool inVehicle=false;try{inVehicle=!p.IsDead&&p.IsInVehicle();}catch{}
                if(inVehicle){_states.Remove(p.Handle);continue;}
                if(!p.IsDead&&UsablePed(p,player)&&!_states.ContainsKey(p.Handle))_states[p.Handle]=PedState.Create(p,_cfg);
            }
        }

        private bool UsablePed(Ped p,Ped player)
        {
            if(p==null||!p.Exists()||p.Handle==player.Handle||p.IsDead||!p.IsHuman)return false;try{if(p.IsInVehicle())return false;}catch{}
            if(_cfg.SkipMissionEntities){try{if(Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY,p.Handle))return false;}catch{}}
            if(_cfg.PoliceOverhaulOwnsLawPeds&&_policeModuleLoaded&&IsLawPed(p))return false;return true;
        }

        private static bool IsLawPed(Ped p){try{int t=(int)p.PedType;return t==6||t==27||t==29;}catch{return false;}}
        private void ProbeModules(){_lastModuleProbe=Game.GameTime;try{_policeModuleLoaded=AppDomain.CurrentDomain.GetAssemblies().Any(a=>string.Equals(a.GetName().Name,"PoliceOverhaulVI",StringComparison.OrdinalIgnoreCase));}catch{_policeModuleLoaded=false;}}

        private void CleanupStates()
        {
            var live=new HashSet<int>(_nearby.Where(p=>p!=null&&p.Exists()&&!p.IsDead&&!SafeInVehicle(p)).Select(p=>p.Handle));
            var remove=_states.Keys.Where(h=>!live.Contains(h)).Take(16).ToList();foreach(int h in remove)_states.Remove(h);
            PedOverhaulVIBridge.Cleanup(live);
        }
        private static bool SafeInVehicle(Ped p){try{return p!=null&&p.Exists()&&p.IsInVehicle();}catch{return false;}}
        private void OnAborted(object sender,EventArgs e){_states.Clear();_nearby.Clear();}
        private void Log(string message){if(_cfg!=null&&!_cfg.DebugLogging)return;try{Directory.CreateDirectory(DataDirectory);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+message+Environment.NewLine);}catch{}}
    }
}
