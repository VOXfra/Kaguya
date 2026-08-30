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

        private Config _cfg;
        private readonly CaseMemory _case = new CaseMemory();

        private bool _missionPassthrough;
        private int _lastWanted;
        private int _lastHudRefresh;
        private int _lastKnowledgeScan;
        private int _lastReacquireScan;

        private bool _pending;
        private int _pendingWanted;
        private int _pendingStartedAt;
        private int _pendingWitnessSeenAt;
        private Ped _pendingWitness;
        private bool _pendingWitnessIsPolice;

        private int _reacquireCandidateSince;
        private int _reacquireCandidateHandle;

        public PoliceOverhaulVIScript()
        {
            Directory.CreateDirectory(DataDirectory);
            _cfg = Config.Load(ConfigPath);
            Tick += OnTick;
            Aborted += OnAborted;
            Interval = 0;
            Log("Police Overhaul VI 0.1.0-alpha loaded.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_cfg.Enabled)
                return;

            try
            {
                bool scriptedContext = _cfg.MissionSafeMode && IsScriptedContext();
                if (scriptedContext)
                {
                    EnterMissionPassthrough();
                    return;
                }

                ExitMissionPassthroughIfNeeded();
                MaintainHudPolicy();

                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead)
                    return;

                int wanted = GetWantedLevel();

                if (_pending)
                {
                    if (wanted > 0)
                    {
                        _pendingWanted = Math.Max(_pendingWanted, wanted);
                        SetWantedLevel(0);
                        wanted = 0;
                    }
                    ProcessPendingIncident(player);
                }
                else if (_cfg.InterceptUnwitnessedWanted && _lastWanted == 0 && wanted > 0)
                {
                    BeginPendingIncident(wanted);
                    SetWantedLevel(0);
                    wanted = 0;
                }

                if (wanted > 0)
                {
                    ObserveActiveWanted(player, wanted);
                }
                else
                {
                    if (_lastWanted > 0)
                        FinishWantedPhase();
                    TryReacquire(player);
                }

                _lastWanted = GetWantedLevel();
            }
            catch (Exception ex)
            {
                Log("Tick error: " + ex);
            }
        }

        private void BeginPendingIncident(int wanted)
        {
            _pending = true;
            _pendingWanted = Math.Max(1, Math.Min(5, wanted));
            _pendingStartedAt = Game.GameTime;
            _pendingWitnessSeenAt = 0;
            _pendingWitness = null;
            _pendingWitnessIsPolice = false;
            Log("Vanilla wanted intercepted; waiting for a real witness. level=" + _pendingWanted);
        }

        private void ProcessPendingIncident(Ped player)
        {
            int now = Game.GameTime;

            if (_pendingWitness != null)
            {
                if (!_pendingWitness.Exists() || _pendingWitness.IsDead)
                {
                    _pendingWitness = null;
                    _pendingWitnessSeenAt = 0;
                }
                else
                {
                    int required = _pendingWitnessIsPolice ? _cfg.PoliceConfirmDelayMs : _cfg.CivilianReportDelayMs;
                    if (now - _pendingWitnessSeenAt >= required)
                    {
                        ConfirmPendingIncident(player);
                        return;
                    }
                }
            }

            WitnessObservation observation = Perception.FindBestWitness(player, _cfg);
            if (observation != null)
            {
                if (_pendingWitness == null || _pendingWitness.Handle != observation.Witness.Handle)
                {
                    _pendingWitness = observation.Witness;
                    _pendingWitnessIsPolice = observation.IsPolice;
                    _pendingWitnessSeenAt = now;
                    Log(observation.IsPolice ? "Police witness acquired." : "Civilian witness began reporting.");
                }
            }

            if (now - _pendingStartedAt >= _cfg.PendingIncidentTimeoutMs && _pendingWitness == null)
            {
                Log("Incident expired with no viable witness; no wanted level issued.");
                ClearPending();
            }
        }

        private void ConfirmPendingIncident(Ped player)
        {
            int level = _pendingWanted;
            bool directPolice = _pendingWitnessIsPolice;
            float distance = _pendingWitness != null && _pendingWitness.Exists()
                ? Perception.Distance(_pendingWitness.Position, player.Position)
                : float.MaxValue;

            SetWantedLevel(level);
            EnsureCase(player, level);
            if (directPolice)
            {
                _case.OutfitKnown = true;
                _case.Outfit = OutfitSignature.Capture(player);
                if (distance <= _cfg.FaceRecognitionDistance && !OutfitSignature.FaceObscured(player))
                    _case.FaceKnown = true;
                CaptureVehicleIfPresent(player);
            }

            _lastWanted = level;
            Log("Incident confirmed by observation. wanted=" + level);
            ClearPending();
        }

        private void ClearPending()
        {
            _pending = false;
            _pendingWanted = 0;
            _pendingStartedAt = 0;
            _pendingWitnessSeenAt = 0;
            _pendingWitness = null;
            _pendingWitnessIsPolice = false;
        }

        private void ObserveActiveWanted(Ped player, int wanted)
        {
            EnsureCase(player, wanted);
            _case.ThreatLevel = Math.Max(_case.ThreatLevel, wanted);

            int now = Game.GameTime;
            if (now - _lastKnowledgeScan < 250)
                return;
            _lastKnowledgeScan = now;

            WitnessObservation police = Perception.FindSeeingPolice(player, _cfg.PoliceWitnessDistance);
            if (police == null)
                return;

            _case.LastKnownPosition = player.Position;

            if (police.Distance <= _cfg.OutfitRecognitionDistance)
            {
                _case.OutfitKnown = true;
                _case.Outfit = OutfitSignature.Capture(player);
            }

            if (police.Distance <= _cfg.FaceRecognitionDistance && !OutfitSignature.FaceObscured(player))
                _case.FaceKnown = true;

            if (police.Distance <= _cfg.VehicleRecognitionDistance)
                CaptureVehicleIfPresent(player);
        }

        private void EnsureCase(Ped player, int wanted)
        {
            if (!_case.Active)
            {
                _case.Active = true;
                _case.LastKnownPosition = player.Position;
                _case.ThreatLevel = Math.Max(1, wanted);
                _case.ExpiresAt = Game.GameTime + Math.Max(1, _cfg.CaseMemoryMinutes) * 60 * 1000;
                Log("Internal case opened.");
            }
            else
            {
                _case.ExpiresAt = Game.GameTime + Math.Max(1, _cfg.CaseMemoryMinutes) * 60 * 1000;
            }
        }

        private void CaptureVehicleIfPresent(Ped player)
        {
            if (!player.IsInVehicle())
                return;
            Vehicle vehicle = player.CurrentVehicle;
            VehicleSignature sig = VehicleSignature.Capture(vehicle);
            if (sig != null)
                _case.Vehicle = sig;
        }

        private void FinishWantedPhase()
        {
            if (_case.Active)
            {
                _case.LastWantedEndedAt = Game.GameTime;
                _case.ExpiresAt = Game.GameTime + Math.Max(1, _cfg.CaseMemoryMinutes) * 60 * 1000;
                Log("Active chase ended; case remains in police memory.");
            }
            ResetReacquireCandidate();
        }

        private void TryReacquire(Ped player)
        {
            if (!_case.Active)
                return;

            int now = Game.GameTime;
            if (now >= _case.ExpiresAt)
            {
                Log("Case expired.");
                _case.Clear();
                return;
            }

            if (now - _case.LastWantedEndedAt < _cfg.ReacquireCooldownMs)
                return;
            if (now - _lastReacquireScan < 300)
                return;
            _lastReacquireScan = now;

            float scanRadius = Math.Max(_cfg.VehicleRecognitionDistance, Math.Max(_cfg.OutfitRecognitionDistance, _cfg.FaceRecognitionDistance));
            WitnessObservation police = Perception.FindSeeingPolice(player, scanRadius);
            if (police == null)
            {
                ResetReacquireCandidate();
                return;
            }

            int confidenceDelay;
            bool match = false;

            if (_case.FaceKnown && police.Distance <= _cfg.FaceRecognitionDistance && !OutfitSignature.FaceObscured(player))
            {
                match = true;
                confidenceDelay = 900;
            }
            else if (_case.Vehicle != null && player.IsInVehicle() && police.Distance <= _cfg.VehicleRecognitionDistance && _case.Vehicle.Matches(player.CurrentVehicle))
            {
                match = true;
                confidenceDelay = 1400;
            }
            else if (_case.OutfitKnown && _case.Outfit != null && police.Distance <= _cfg.OutfitRecognitionDistance && _case.Outfit.Matches(player))
            {
                match = true;
                confidenceDelay = 2400;
            }
            else
            {
                ResetReacquireCandidate();
                return;
            }

            if (match)
            {
                if (_reacquireCandidateHandle != police.Witness.Handle)
                {
                    _reacquireCandidateHandle = police.Witness.Handle;
                    _reacquireCandidateSince = now;
                    return;
                }

                if (now - _reacquireCandidateSince >= confidenceDelay)
                {
                    int restored = Math.Max(1, Math.Min(5, _case.ThreatLevel));
                    SetWantedLevel(restored);
                    _lastWanted = restored;
                    _case.LastKnownPosition = player.Position;
                    Log("Police re-identified suspect from retained case knowledge.");
                    ResetReacquireCandidate();
                }
            }
        }

        private void ResetReacquireCandidate()
        {
            _reacquireCandidateHandle = 0;
            _reacquireCandidateSince = 0;
        }

        private void MaintainHudPolicy()
        {
            if (!_cfg.HidePoliceBlips)
                return;
            if (Game.GameTime - _lastHudRefresh < 500)
                return;
            _lastHudRefresh = Game.GameTime;
            Function.Call(Hash.SET_POLICE_RADAR_BLIPS, false);
        }

        private bool IsScriptedContext()
        {
            return Function.Call<bool>(Hash.GET_MISSION_FLAG)
                || Function.Call<bool>(Hash.GET_RANDOM_EVENT_FLAG)
                || Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE);
        }

        private void EnterMissionPassthrough()
        {
            if (!_missionPassthrough)
            {
                _missionPassthrough = true;
                ClearPending();
                ResetReacquireCandidate();
                Log("Mission-safe passthrough enabled.");
            }

            if (_cfg.HidePoliceBlips)
                Function.Call(Hash.SET_POLICE_RADAR_BLIPS, true);
            _lastWanted = GetWantedLevel();
        }

        private void ExitMissionPassthroughIfNeeded()
        {
            if (!_missionPassthrough)
                return;
            _missionPassthrough = false;
            _lastWanted = GetWantedLevel();
            _lastHudRefresh = 0;
            Log("Mission-safe passthrough disabled; free-roam logic resumed.");
        }

        private int GetWantedLevel()
        {
            return Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle);
        }

        private void SetWantedLevel(int level)
        {
            level = Math.Max(0, Math.Min(5, level));
            Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, Game.Player.Handle, level, false);
            Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player.Handle, false);
        }

        private void OnAborted(object sender, EventArgs e)
        {
            try
            {
                if (_cfg != null && _cfg.HidePoliceBlips)
                    Function.Call(Hash.SET_POLICE_RADAR_BLIPS, true);
            }
            catch { }
        }

        private void Log(string message)
        {
            if (_cfg != null && !_cfg.DebugLogging)
                return;
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
