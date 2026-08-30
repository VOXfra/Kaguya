using GTA;
using GTA.Native;
using System;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    public sealed class PoliceOverhaulVIScript : Script
    {
        private const string ConfigPath = "scripts\\PoliceOverhaulVI.ini";
        private const string DataDirectory = "scripts\\PoliceOverhaulVI";
        private const string LogPath = DataDirectory + "\\PoliceOverhaulVI.log";
        private const string CasesPath = DataDirectory + "\\Cases.xml";

        private Config _cfg;
        private readonly CaseRepository _repository = new CaseRepository();
        private readonly TrafficSystem _traffic = new TrafficSystem();
        private readonly WarrantSystem _warrants = new WarrantSystem();
        private readonly DispatchSystem _dispatch = new DispatchSystem();
        private CaseMemory _case;
        private int _currentSuspectModel;

        private bool _missionPassthrough;
        private int _postMissionGraceUntil;
        private int _lastWanted;
        private int _internalWanted;
        private int _lastHudRefresh;
        private int _lastKnowledgeScan;
        private int _lastReacquireScan;
        private int _lastCameraScan;
        private int _lastTrackerPing;
        private int _lastPersistenceSave;

        private bool _pending;
        private int _pendingWanted;
        private int _pendingStartedAt;
        private int _pendingSourceSeenAt;
        private ObservationSource _pendingSource;
        private Ped _pendingWitness;
        private float _pendingSourceDistance;
        private int _pendingCameraHandle;
        private int _reacquireCandidateSince;
        private int _reacquireCandidateKey;

        public PoliceOverhaulVIScript()
        {
            Directory.CreateDirectory(DataDirectory);
            _cfg = Config.Load(ConfigPath);
            if (_cfg.PersistenceEnabled)
                Persistence.LoadCases(CasesPath, _repository, Log);
            Tick += OnTick;
            Aborted += OnAborted;
            Interval = 0;
            Log("Police Overhaul VI 0.1.0 loaded.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_cfg.Enabled) return;
            try
            {
                if (_cfg.MissionSafeMode && IsScriptedContext()) { EnterMissionPassthrough(); return; }
                ExitMissionPassthroughIfNeeded(); MaintainHudPolicy();
                Ped player = Game.LocalPlayerPed; if (player == null || !player.Exists() || player.IsDead) return;
                SelectCurrentCase(player); MaintainPersistence(); ExpireCurrentCaseIfNeeded();
                int nativeWanted = GetWantedLevel();
                if (Game.GameTime < _postMissionGraceUntil) { _lastWanted = nativeWanted; _internalWanted = nativeWanted; return; }

                if (_pending)
                {
                    if (nativeWanted > 0) { _pendingWanted = Math.Max(_pendingWanted, nativeWanted); SetWantedLevel(0); nativeWanted = 0; }
                    ProcessPendingIncident(player);
                }
                else if (_cfg.InterceptUnwitnessedWanted && _lastWanted == 0 && nativeWanted > 0)
                {
                    BeginPendingIncident(nativeWanted); SetWantedLevel(0); nativeWanted = 0;
                }

                nativeWanted = GetWantedLevel();
                if (nativeWanted > 0)
                {
                    ObserveActiveWanted(player, nativeWanted);
                    _internalWanted = _dispatch.UpdateTier(player, nativeWanted, _case, _cfg, Log);
                    _dispatch.UpdateResponse(player, _internalWanted, _cfg, Log);
                }
                else
                {
                    if (_lastWanted > 0) FinishWantedPhase();
                    _internalWanted = 0; _dispatch.UpdateResponse(player, 0, _cfg, Log); TryReacquire(player); _traffic.Update(player, _case, _cfg, Log);
                }
                _dispatch.DrawSixthStarIfNeeded(_internalWanted); _lastWanted = GetWantedLevel();
            }
            catch (Exception ex) { Log("Tick error: " + ex); }
        }

        private void SelectCurrentCase(Ped player)
        {
            int model = player.Model.Hash; if (_case != null && model == _currentSuspectModel) return;
            _currentSuspectModel = model; _case = _repository.GetOrCreate(model); _case.SuspectModelHash = model; ResetReacquireCandidate(); ClearPending(); Log("Police memory switched to protagonist model " + model + ".");
        }

        private void BeginPendingIncident(int wanted)
        {
            _pending = true; _pendingWanted = Math.Max(1, Math.Min(5, wanted)); _pendingStartedAt = Game.GameTime; _pendingSourceSeenAt = 0; _pendingSource = ObservationSource.None; _pendingWitness = null; _pendingSourceDistance = float.MaxValue; _pendingCameraHandle = 0;
            Log("Vanilla wanted intercepted; waiting for physical observation. requested=" + _pendingWanted);
        }

        private void ProcessPendingIncident(Ped player)
        {
            int now = Game.GameTime;
            if (_pendingSource != ObservationSource.None)
            {
                if ((_pendingSource == ObservationSource.Civilian || _pendingSource == ObservationSource.Police) && (_pendingWitness == null || !_pendingWitness.Exists() || _pendingWitness.IsDead)) ResetPendingSource();
                else if (now - _pendingSourceSeenAt >= RequiredReportDelay(_pendingSource)) { ConfirmPendingIncident(player); return; }
            }

            if (_pendingSource == ObservationSource.None)
            {
                WitnessObservation witness = Perception.FindBestWitness(player, _cfg);
                if (witness != null)
                {
                    _pendingWitness = witness.Witness; _pendingSource = witness.IsPolice ? ObservationSource.Police : ObservationSource.Civilian; _pendingSourceDistance = witness.Distance; _pendingSourceSeenAt = now;
                    Log(witness.IsPolice ? "Direct police witness acquired." : "Civilian witness began reporting.");
                }
                else if (_cfg.CctvEnabled && _cfg.CctvCanDispatch && now - _lastCameraScan >= 250)
                {
                    _lastCameraScan = now; CameraObservation camera = CameraSystem.FindSeeingPlayer(player, _cfg, false);
                    if (camera != null) { _pendingSource = ObservationSource.CCTV; _pendingCameraHandle = camera.CameraHandle; _pendingSourceDistance = camera.Distance; _pendingSourceSeenAt = now; Log("CCTV recorded the incident and queued a delayed report."); }
                }
            }
            if (now - _pendingStartedAt >= _cfg.PendingIncidentTimeoutMs && _pendingSource == ObservationSource.None) { Log("Incident expired without a witness/camera report; no wanted level issued."); ClearPending(); }
        }

        private int RequiredReportDelay(ObservationSource source)
        {
            switch (source) { case ObservationSource.Police: return Math.Max(0, _cfg.PoliceConfirmDelayMs); case ObservationSource.Civilian: return Math.Max(0, _cfg.CivilianReportDelayMs); case ObservationSource.CCTV: return Math.Max(0, _cfg.CctvCrimeReportDelayMs); default: return 0; }
        }

        private void ConfirmPendingIncident(Ped player)
        {
            int level = Math.Max(1, Math.Min(5, _pendingWanted)); EnsureCase(player, level); CaptureEvidence(player, _pendingSource, _pendingSourceDistance, level); SetWantedLevel(level); SetWantedCentre(_case.LastKnownPosition); _lastWanted = level; _internalWanted = level; Log("Incident confirmed. wanted=" + level + ", source=" + _pendingSource + "."); ClearPending();
        }

        private void ClearPending(){_pending=false;_pendingWanted=0;_pendingStartedAt=0;ResetPendingSource();}
        private void ResetPendingSource(){_pendingSourceSeenAt=0;_pendingSource=ObservationSource.None;_pendingWitness=null;_pendingSourceDistance=float.MaxValue;_pendingCameraHandle=0;}

        private void ObserveActiveWanted(Ped player, int wanted)
        {
            EnsureCase(player,wanted);_case.ThreatLevel=Math.Max(_case.ThreatLevel,wanted);_case.Touch(_cfg);int now=Game.GameTime;
            if(now-_lastKnowledgeScan<250){SetWantedCentre(_case.LastKnownPosition);return;}_lastKnowledgeScan=now;bool observed=false;
            WitnessObservation police=Perception.FindSeeingPolice(player,_cfg.PoliceWitnessDistance);if(police!=null){CaptureEvidence(player,ObservationSource.Police,police.Distance,wanted);observed=true;}
            if(!observed&&_cfg.CctvEnabled&&now-_lastCameraScan>=450){_lastCameraScan=now;CameraObservation camera=CameraSystem.FindSeeingPlayer(player,_cfg,false);if(camera!=null){CaptureEvidence(player,ObservationSource.CCTV,camera.Distance,wanted);observed=true;}}
            if(!observed&&TrackerSystem.HasPoliceUsableTracker(_case,player,_cfg)&&now-_lastTrackerPing>=_cfg.TrackerPingIntervalMs){_lastTrackerPing=now;_case.LastKnownPosition=player.Position;_case.LastSource=ObservationSource.Tracker;_case.LastObservedGameTime=now;_case.Touch(_cfg);observed=true;Log("Tracked vehicle transmitted a location ping.");}
            SetWantedCentre(_case.LastKnownPosition);IssueWarrantIfEligible();
        }

        private void EnsureCase(Ped player,int wanted)
        {
            if(_case==null)SelectCurrentCase(player);if(!_case.Active){_case.Active=true;_case.LastKnownPosition=player.Position;_case.ThreatLevel=Math.Max(1,wanted);_case.Touch(_cfg);Log("Internal police case opened.");}else _case.Touch(_cfg);
        }

        private void CaptureEvidence(Ped player,ObservationSource source,float distance,int wanted)
        {
            if(_case==null||player==null||!player.Exists())return;_case.Active=true;_case.ThreatLevel=Math.Max(_case.ThreatLevel,Math.Max(1,wanted));_case.LastKnownPosition=player.Position;_case.LastSource=source;_case.LastObservedGameTime=Game.GameTime;_case.Touch(_cfg);bool camera=source==ObservationSource.CCTV;
            if(camera||distance<=_cfg.OutfitRecognitionDistance){_case.OutfitKnown=true;_case.Outfit=OutfitSignature.Capture(player);}if((camera?distance<=_cfg.FaceRecognitionDistance*1.25f:distance<=_cfg.FaceRecognitionDistance)&&!OutfitSignature.FaceObscured(player))_case.FaceKnown=true;
            if(player.IsInVehicle()&&(camera||distance<=_cfg.VehicleRecognitionDistance)){bool plateKnown=camera||distance<=_cfg.PlateRecognitionDistance||(_cfg.PoliceANPR&&source==ObservationSource.Police&&distance<=_cfg.VehicleRecognitionDistance);VehicleSignature sig=VehicleSignature.Capture(player.CurrentVehicle,plateKnown);if(sig!=null){if(_case.Vehicle!=null&&_case.Vehicle.ModelHash==sig.ModelHash&&_case.Vehicle.PlateKnown)sig.PlateKnown=true;_case.Vehicle=sig;TrackerSystem.AttachKnowledgeIfApplicable(_case,player,_cfg);}}
            if(camera||distance<=_cfg.WeaponRecognitionDistance){bool armed=false;try{armed=Function.Call<bool>(Hash.IS_PED_ARMED,player.Handle,7);}catch{}if(armed){_case.WeaponKnown=true;_case.WeaponHash=SuspectSnapshot.CurrentWeaponHash(player);}}
            if(camera||distance<=_cfg.SuspectCountRecognitionDistance){_case.SuspectCountKnown=true;_case.SuspectCount=SuspectSnapshot.CountVisibleSuspects(player);}IssueWarrantIfEligible();
        }

        private void IssueWarrantIfEligible(){if(_case==null||!_cfg.WarrantsEnabled||_case.WarrantActive)return;if(_case.FaceKnown&&_case.ThreatLevel>=Math.Max(1,_cfg.WarrantMinimumThreat)){_case.IssueWarrant(_cfg);Log("Arrest warrant issued internally for identified protagonist.");}}
        private void FinishWantedPhase(){if(_case!=null&&_case.Active){_case.LastWantedEndedAt=Game.GameTime;_case.Touch(_cfg);IssueWarrantIfEligible();Log("Active pursuit ended; case/evidence remain in police memory.");}ResetReacquireCandidate();_traffic.ResetTransient();}

        private void TryReacquire(Ped player)
        {
            if(_case==null||(!_case.Active&&!_case.WarrantActive))return;if(_case.IsWarrantExpiredUtc()){_case.WarrantActive=false;_case.WarrantExpiresUtcTicks=0;}if(_case.IsExpiredUtc()&&!_case.WarrantActive)return;int now=Game.GameTime;if(_case.LastWantedEndedAt>0&&now-_case.LastWantedEndedAt<_cfg.ReacquireCooldownMs)return;
            if(_warrants.Update(player,_case,_cfg,Log)){RestoreWantedFromCase(player,ObservationSource.HomeSurveillance);return;}
            if(TrackerSystem.HasPoliceUsableTracker(_case,player,_cfg)){if(EvaluateTimedCandidate(int.MinValue+7,_cfg.TrackerReacquireDelayMs)){_case.LastKnownPosition=player.Position;RestoreWantedFromCase(player,ObservationSource.Tracker);return;}}else if(_reacquireCandidateKey==int.MinValue+7)ResetReacquireCandidate();
            if(now-_lastReacquireScan<300)return;_lastReacquireScan=now;float scanRadius=Math.Max(_cfg.VehicleRecognitionDistance,Math.Max(_cfg.OutfitRecognitionDistance,_cfg.FaceRecognitionDistance));WitnessObservation police=Perception.FindSeeingPolice(player,scanRadius);
            if(police!=null){int delay;if(MatchesCase(player,police.Distance,true,out delay)){if(EvaluateTimedCandidate(police.Witness.Handle,delay)){CaptureEvidence(player,ObservationSource.Police,police.Distance,Math.Max(1,_case.ThreatLevel));RestoreWantedFromCase(player,ObservationSource.Police);return;}}else ResetReacquireCandidate();}
            else if(_cfg.CctvEnabled){CameraObservation camera=CameraSystem.FindSeeingPlayer(player,_cfg,false);if(camera!=null){int delay;if(MatchesCase(player,camera.Distance,false,out delay)){int key=-Math.Abs(camera.CameraHandle==0?1:camera.CameraHandle);if(EvaluateTimedCandidate(key,Math.Max(_cfg.CctvReacquireDelayMs,delay))){CaptureEvidence(player,ObservationSource.CCTV,camera.Distance,Math.Max(1,_case.ThreatLevel));RestoreWantedFromCase(player,ObservationSource.CCTV);return;}}else ResetReacquireCandidate();}else ResetReacquireCandidate();}else ResetReacquireCandidate();
        }

        private bool MatchesCase(Ped player,float distance,bool policeObserver,out int confidenceDelay)
        {
            confidenceDelay=2500;if(_case.FaceKnown&&distance<=_cfg.FaceRecognitionDistance&&!OutfitSignature.FaceObscured(player)){confidenceDelay=850;return true;}if(_case.Vehicle!=null&&player.IsInVehicle()&&distance<=_cfg.VehicleRecognitionDistance){bool requirePlate=_case.Vehicle.PlateKnown&&(_cfg.PoliceANPR||!policeObserver);if(_case.Vehicle.Matches(player.CurrentVehicle,requirePlate)){confidenceDelay=requirePlate?700:1300;return true;}}if(_case.OutfitKnown&&_case.Outfit!=null&&distance<=_cfg.OutfitRecognitionDistance&&_case.Outfit.Matches(player)){confidenceDelay=2300;return true;}return false;
        }

        private bool EvaluateTimedCandidate(int key,int delayMs){int now=Game.GameTime;if(_reacquireCandidateKey!=key){_reacquireCandidateKey=key;_reacquireCandidateSince=now;return false;}return now-_reacquireCandidateSince>=Math.Max(0,delayMs);}
        private void RestoreWantedFromCase(Ped player,ObservationSource source){int restored=Math.Max(1,Math.Min(5,_case.ThreatLevel));_case.LastKnownPosition=player.Position;_case.LastSource=source;_case.LastObservedGameTime=Game.GameTime;_case.Touch(_cfg);SetWantedLevel(restored);SetWantedCentre(player.Position);_lastWanted=restored;_internalWanted=_case.ThreatLevel>=6?6:restored;ResetReacquireCandidate();Log("Police re-identified suspect from retained evidence. source="+source+".");}
        private void ResetReacquireCandidate(){_reacquireCandidateKey=0;_reacquireCandidateSince=0;}
        private void MaintainHudPolicy(){if(_cfg.HidePoliceBlips&&Game.GameTime-_lastHudRefresh>=400){_lastHudRefresh=Game.GameTime;Function.Call(Hash.SET_POLICE_RADAR_BLIPS,false);}_dispatch.DrawSixthStarIfNeeded(_internalWanted);}
        private void SetWantedCentre(GTA.Math.Vector3 p){try{Function.Call(Hash.SET_PLAYER_WANTED_CENTRE_POSITION,Game.Player.Handle,p.X,p.Y,p.Z);}catch{}}
        private bool IsScriptedContext(){bool switching=false;try{switching=Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS);}catch{}return Function.Call<bool>(Hash.GET_MISSION_FLAG)||Function.Call<bool>(Hash.GET_RANDOM_EVENT_FLAG)||Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)||switching;}

        private void EnterMissionPassthrough(){if(!_missionPassthrough){_missionPassthrough=true;ClearPending();ResetReacquireCandidate();_traffic.ResetTransient();_dispatch.CleanupAll();_warrants.Cleanup();SavePersistence();Log("Mission-safe passthrough enabled; Rockstar owns wanted/dispatch.");}if(_cfg.HidePoliceBlips)Function.Call(Hash.SET_POLICE_RADAR_BLIPS,true);_lastWanted=GetWantedLevel();_internalWanted=_lastWanted;}
        private void ExitMissionPassthroughIfNeeded(){if(!_missionPassthrough)return;_missionPassthrough=false;_lastWanted=GetWantedLevel();_internalWanted=_lastWanted;_lastHudRefresh=0;_postMissionGraceUntil=Game.GameTime+Math.Max(0,_cfg.PostMissionGraceMs);Log("Mission-safe passthrough disabled; free-roam systems resume after grace period.");}
        private void ExpireCurrentCaseIfNeeded(){if(_case==null)return;if(_case.IsWarrantExpiredUtc()){_case.WarrantActive=false;_case.WarrantExpiresUtcTicks=0;_warrants.Cleanup();}if(_case.IsExpiredUtc()&&!_case.WarrantActive){int fines=_case.UnpaidFines,model=_case.SuspectModelHash;_case.ClearAll();_case.SuspectModelHash=model;_case.UnpaidFines=fines;Log("Police case expired; evidence forgotten.");}}
        private void MaintainPersistence(){if(!_cfg.PersistenceEnabled||Game.GameTime-_lastPersistenceSave<_cfg.PersistenceSaveIntervalMs)return;_lastPersistenceSave=Game.GameTime;SavePersistence();}
        private void SavePersistence(){if(_cfg!=null&&_cfg.PersistenceEnabled)Persistence.SaveCases(CasesPath,_repository,Log);}
        private int GetWantedLevel(){return Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL,Game.Player.Handle);}private void SetWantedLevel(int level){level=Math.Max(0,Math.Min(5,level));Function.Call(Hash.SET_PLAYER_WANTED_LEVEL,Game.Player.Handle,level,false);Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW,Game.Player.Handle,false);}
        private void OnAborted(object sender,EventArgs e){try{SavePersistence();_dispatch.CleanupAll();_warrants.Cleanup();if(_cfg!=null&&_cfg.HidePoliceBlips)Function.Call(Hash.SET_POLICE_RADAR_BLIPS,true);}catch{}}
        private void Log(string message){if(_cfg!=null&&!_cfg.DebugLogging)return;try{Directory.CreateDirectory(DataDirectory);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+message+Environment.NewLine);}catch{}}
    }
}
