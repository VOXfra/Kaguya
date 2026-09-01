using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    // Owns the gap between an active chase and a confirmed reacquisition.
    // The vanilla wanted level may remain visible, but cops are temporarily
    // prevented from magically snapping back to the Player entity after LOS is lost.
    public sealed class PoliceSearchOwnershipScript : Script
    {
        private const string ConfigPath = "scripts\\PoliceOverhaulVI.ini";
        private const string DataDirectory = "scripts\\PoliceOverhaulVI";
        private const string LogPath = DataDirectory + "\\PoliceOverhaulVI.log";

        private readonly Config _cfg;
        private int _lastWanted;
        private int _lastScan;
        private int _lastCctvScan;
        private int _lastTrackerScan;
        private int _lastVisualAt;
        private int _lastMissionFlagAt;
        private bool _policeIgnoreApplied;

        public PoliceSearchOwnershipScript()
        {
            Directory.CreateDirectory(DataDirectory);
            _cfg = Config.Load(ConfigPath);
            Interval = 50;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Police Overhaul VI 0.5.0 search-ownership runtime loaded: active signalment + timed reacquisition.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_cfg.Enabled) { ReleasePoliceIgnore(); return; }
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead)
                {
                    ReleasePoliceIgnore();
                    PoliceSearchRuntimeState.ResetSearch(true);
                    _lastWanted = 0;
                    _lastVisualAt = 0;
                    return;
                }

                int wanted = GetWantedLevel();
                if (ShouldYieldToRockstar(wanted))
                {
                    ReleasePoliceIgnore();
                    PoliceSearchRuntimeState.ResetSearch(true);
                    _lastWanted = wanted;
                    return;
                }

                CaseMemory memory = PoliceSearchRuntimeState.CaseFor(player);
                if (memory == null) return;
                if (memory.SuspectModelHash == 0) memory.SuspectModelHash = player.Model.Hash;

                if (wanted > 0 && !memory.Active)
                {
                    memory.Active = true;
                    memory.ThreatLevel = Math.Max(memory.ThreatLevel, wanted);
                    memory.LastKnownPosition = player.Position;
                    memory.Touch(_cfg);
                    Log("Search-ownership safety net opened missing police case for active free-roam wanted level " + wanted + ".");
                }

                if (PoliceSearchRuntimeState.SearchActive)
                    UpdateCustomSearch(player, memory, wanted);
                else if (wanted > 0)
                    UpdateActivePursuit(player, memory, wanted);
                else
                {
                    ReleasePoliceIgnore();
                    PoliceSearchRuntimeState.InvalidateChangedSignalment(player);
                    _lastVisualAt = 0;
                }

                _lastWanted = GetWantedLevel();
            }
            catch (Exception ex)
            {
                Log("Search-ownership tick error: " + ex.Message);
                ReleasePoliceIgnore();
            }
        }

        private void UpdateActivePursuit(Ped player, CaseMemory memory, int wanted)
        {
            int now = Game.GameTime;
            memory.ThreatLevel = Math.Max(memory.ThreatLevel, wanted);
            memory.Touch(_cfg);

            if (_lastWanted <= 0 && _lastVisualAt == 0)
                _lastVisualAt = now;

            if (now - _lastScan < 150) return;
            _lastScan = now;

            WitnessObservation police = Perception.FindSeeingPolice(player, _cfg.PoliceWitnessDistance);
            if (police != null)
            {
                bool plateKnown = _cfg.PoliceANPR && police.Distance <= _cfg.VehicleRecognitionDistance;
                RecordDirectObservation(player, memory, ObservationSource.Police, police.Distance, wanted, plateKnown);
                _lastVisualAt = now;
                return;
            }

            if (_cfg.CctvEnabled && now - _lastCctvScan >= 650)
            {
                _lastCctvScan = now;
                CameraObservation camera = CameraSystem.FindSeeingPlayer(player, _cfg, false);
                if (camera != null)
                {
                    RecordDirectObservation(player, memory, ObservationSource.CCTV, camera.Distance, wanted, true);
                    _lastVisualAt = now;
                    return;
                }
            }

            // A tracker updates the vehicle's last-known position, but it is not
            // visual contact with the driver and therefore does not prevent search mode.
            UpdateTrackerPing(player, memory, now);

            if (_lastVisualAt == 0) _lastVisualAt = now;
            if (now - _lastVisualAt >= Math.Max(800, _cfg.SearchLostContactDelayMs))
                EnterCustomSearch(player, memory, wanted);
        }

        private void EnterCustomSearch(Ped player, CaseMemory memory, int wanted)
        {
            int now = Game.GameTime;
            int threat = Math.Max(1, Math.Max(memory.ThreatLevel, wanted));
            PoliceSearchRuntimeState.SearchActive = true;
            PoliceSearchRuntimeState.ThreatLevel = threat;
            PoliceSearchRuntimeState.SearchStartedAt = now;
            PoliceSearchRuntimeState.SearchDeadlineAt = now + SearchLifetimeForThreat(threat);
            PoliceSearchRuntimeState.LastKnownPosition = memory.LastKnownPosition == Vector3.Zero ? player.Position : memory.LastKnownPosition;
            PoliceSearchRuntimeState.LastDirectObservationAt = memory.LastObservedGameTime;
            PoliceSearchRuntimeState.ResetCandidate();
            ApplyPoliceIgnore();
            SetWantedCentre(PoliceSearchRuntimeState.LastKnownPosition);
            Log("Custom search ownership entered. threat=" + threat + ", lastKnown=" + Format(PoliceSearchRuntimeState.LastKnownPosition) + ". Vanilla direct reacquisition suspended.");
        }

        private void UpdateCustomSearch(Ped player, CaseMemory memory, int nativeWanted)
        {
            int now = Game.GameTime;
            ApplyPoliceIgnore();
            PoliceSearchRuntimeState.InvalidateChangedSignalment(player);

            int threat = Math.Max(1, PoliceSearchRuntimeState.ThreatLevel);
            if (nativeWanted <= 0 && now < PoliceSearchRuntimeState.SearchDeadlineAt)
                SetWantedLevel(Math.Min(5, threat));

            UpdateTrackerPing(player, memory, now);
            if (PoliceSearchRuntimeState.LastKnownPosition != Vector3.Zero)
                SetWantedCentre(PoliceSearchRuntimeState.LastKnownPosition);

            if (now >= PoliceSearchRuntimeState.SearchDeadlineAt)
            {
                EndSearchLost(memory);
                return;
            }

            if (now - _lastScan < 120) return;
            _lastScan = now;

            float scanRadius = Math.Max(_cfg.VehicleRecognitionDistance,
                Math.Max(_cfg.OutfitRecognitionDistance, _cfg.FaceRecognitionDistance));
            WitnessObservation police = Perception.FindSeeingPolice(player, scanRadius);
            if (police != null)
            {
                float confidence = SearchMatchConfidence(player, memory, police.Distance, true);
                if (confidence > 0f)
                {
                    int delay = SearchConfirmationDelay(confidence, false);
                    if (EvaluateCandidate(police.Witness.Handle, confidence, delay))
                    {
                        ConfirmReacquisition(player, memory, ObservationSource.Police, police.Distance, threat);
                        return;
                    }
                }
                else PoliceSearchRuntimeState.ResetCandidate();
                return;
            }

            if (_cfg.CctvEnabled && now - _lastCctvScan >= 450)
            {
                _lastCctvScan = now;
                CameraObservation camera = CameraSystem.FindSeeingPlayer(player, _cfg, false);
                if (camera != null)
                {
                    float confidence = SearchMatchConfidence(player, memory, camera.Distance, false);
                    if (confidence > 0f)
                    {
                        int key = -Math.Abs(camera.CameraHandle == 0 ? 1 : camera.CameraHandle);
                        int delay = SearchConfirmationDelay(confidence, true);
                        if (EvaluateCandidate(key, confidence, delay))
                        {
                            ConfirmReacquisition(player, memory, ObservationSource.CCTV, camera.Distance, threat);
                            return;
                        }
                    }
                    else PoliceSearchRuntimeState.ResetCandidate();
                    return;
                }
            }

            // LOS must remain continuous. A one-frame glimpse between buildings
            // can start a candidate, but losing sight resets the identification timer.
            PoliceSearchRuntimeState.ResetCandidate();
        }

        private void ConfirmReacquisition(Ped player, CaseMemory memory, ObservationSource source, float distance, int threat)
        {
            int wanted = Math.Max(1, Math.Min(5, threat));
            bool plateKnown = source == ObservationSource.CCTV || (_cfg.PoliceANPR && distance <= _cfg.VehicleRecognitionDistance);
            IdentificationSystem.Observe(player, memory, source, distance, wanted, _cfg);
            memory.Active = true;
            memory.ThreatLevel = Math.Max(memory.ThreatLevel, threat);
            memory.LastKnownPosition = player.Position;
            memory.LastSource = source;
            memory.LastObservedGameTime = Game.GameTime;
            memory.Touch(_cfg);
            PoliceSearchRuntimeState.CaptureActiveSignalment(player, memory, plateKnown);
            PoliceSearchRuntimeState.LastKnownPosition = player.Position;
            PoliceSearchRuntimeState.SearchActive = false;
            PoliceSearchRuntimeState.ResetCandidate();
            ReleasePoliceIgnore();
            SetWantedLevel(wanted);
            SetWantedCentre(player.Position);
            _lastVisualAt = Game.GameTime;
            Log("Timed suspect reacquisition confirmed after continuous observation. source=" + source + ", activeVehicle=" + PoliceSearchRuntimeState.ActiveVehicleValid + ", activeOutfit=" + PoliceSearchRuntimeState.ActiveOutfitValid + ", faceKnown=" + memory.FaceKnown + ".");
        }

        private void EndSearchLost(CaseMemory memory)
        {
            int now = Game.GameTime;
            ReleasePoliceIgnore();
            SetWantedLevel(0);
            if (memory != null)
            {
                memory.LastWantedEndedAt = now;
                memory.Touch(_cfg);
            }
            Log("Custom search expired without confirmed identification; active pursuit ended while historical evidence remains.");
            PoliceSearchRuntimeState.ResetSearch(false);
            _lastVisualAt = 0;
        }

        private void RecordDirectObservation(Ped player, CaseMemory memory, ObservationSource source, float distance, int wanted, bool plateKnown)
        {
            IdentificationSystem.Observe(player, memory, source, distance, wanted, _cfg);
            memory.Active = true;
            memory.ThreatLevel = Math.Max(memory.ThreatLevel, wanted);
            memory.LastKnownPosition = player.Position;
            memory.LastSource = source;
            memory.LastObservedGameTime = Game.GameTime;
            memory.Touch(_cfg);
            PoliceSearchRuntimeState.LastKnownPosition = player.Position;
            PoliceSearchRuntimeState.LastDirectObservationAt = Game.GameTime;
            PoliceSearchRuntimeState.CaptureActiveSignalment(player, memory, plateKnown);
            SetWantedCentre(player.Position);
        }

        private void UpdateTrackerPing(Ped player, CaseMemory memory, int now)
        {
            if (!_cfg.TrackersEnabled || now - _lastTrackerScan < Math.Max(900, _cfg.TrackerPingIntervalMs)) return;
            _lastTrackerScan = now;
            if (!TrackerSystem.HasPoliceUsableTracker(memory, player, _cfg)) return;

            memory.LastKnownPosition = player.Position;
            memory.LastSource = ObservationSource.Tracker;
            memory.Touch(_cfg);
            PoliceSearchRuntimeState.LastKnownPosition = player.Position;
            PoliceSearchRuntimeState.LastTrackerPingAt = now;
            Log("Tracker search ping updated the flagged vehicle location; driver identity remains unconfirmed.");
        }

        private float SearchMatchConfidence(Ped player, CaseMemory memory, float distance, bool policeObserver)
        {
            if (player == null || !player.Exists() || memory == null) return 0f;
            bool masked = OutfitSignature.FaceObscured(player);
            float score = 0f;
            bool concrete = false;

            if (memory.FaceKnown && !masked && distance <= _cfg.FaceRecognitionDistance)
            {
                float q = Math.Max(0.25f, 1f - distance / Math.Max(1f, _cfg.FaceRecognitionDistance) * 0.62f);
                score += Math.Max(42f, memory.FaceConfidence * 0.78f) * q;
                concrete = true;
            }

            if (PoliceSearchRuntimeState.ActiveOutfitValid && PoliceSearchRuntimeState.ActiveOutfit != null &&
                distance <= _cfg.OutfitRecognitionDistance && PoliceSearchRuntimeState.ActiveOutfit.Matches(player))
            {
                float q = Math.Max(0.35f, 1f - distance / Math.Max(1f, _cfg.OutfitRecognitionDistance) * 0.55f);
                score += 30f * q;
                concrete = true;
            }

            if (PoliceSearchRuntimeState.ActiveVehicleValid && PoliceSearchRuntimeState.ActiveVehicle != null && player.IsInVehicle() &&
                distance <= _cfg.VehicleRecognitionDistance)
            {
                bool requirePlate = PoliceSearchRuntimeState.ActiveVehicle.PlateKnown && (_cfg.PoliceANPR || !policeObserver);
                if (PoliceSearchRuntimeState.ActiveVehicle.Matches(player.CurrentVehicle, requirePlate))
                {
                    score += requirePlate ? 48f : 31f;
                    concrete = true;
                }
            }

            if (PoliceSearchRuntimeState.MaskDescriptorValid &&
                OutfitSignature.FaceObscured(player) == PoliceSearchRuntimeState.MaskedDescriptor)
                score += 7f;

            if (!concrete) return 0f;
            score += Math.Min(8f, Math.Max(0f, memory.Notoriety) * 0.08f);
            if (memory.MostWanted) score += 5f;
            return Math.Max(0f, Math.Min(100f, score));
        }

        private int SearchConfirmationDelay(float confidence, bool camera)
        {
            float c = Math.Max(0f, Math.Min(100f, confidence));
            int min = Math.Max(900, _cfg.IdentityMinConfirmationMs);
            int max = Math.Max(min + 800, _cfg.IdentityMaxConfirmationMs);
            int delay = max - (int)((max - min) * c / 100f);
            if (camera) delay = Math.Max(delay, Math.Max(1600, _cfg.CctvReacquireDelayMs));
            return Math.Max(900, delay);
        }

        private bool EvaluateCandidate(int key, float confidence, int delay)
        {
            int now = Game.GameTime;
            if (confidence < 55f)
            {
                PoliceSearchRuntimeState.ResetCandidate();
                return false;
            }
            if (PoliceSearchRuntimeState.CandidateKey != key)
            {
                PoliceSearchRuntimeState.CandidateKey = key;
                PoliceSearchRuntimeState.CandidateSince = now;
                PoliceSearchRuntimeState.CandidateConfidence = confidence;
                return false;
            }
            if (confidence + 8f < PoliceSearchRuntimeState.CandidateConfidence)
            {
                PoliceSearchRuntimeState.CandidateSince = now;
                PoliceSearchRuntimeState.CandidateConfidence = confidence;
                return false;
            }
            PoliceSearchRuntimeState.CandidateConfidence = Math.Max(PoliceSearchRuntimeState.CandidateConfidence, confidence);
            return now - PoliceSearchRuntimeState.CandidateSince >= delay;
        }

        private int SearchLifetimeForThreat(int threat)
        {
            int baseMs = Math.Max(60000, _cfg.SearchPhaseLifetimeMs);
            if (threat >= 6) return Math.Max(baseMs * 5, 300000);
            if (threat == 5) return Math.Max(baseMs * 3, 220000);
            if (threat == 4) return Math.Max(baseMs * 2, 160000);
            if (threat == 3) return Math.Max((int)(baseMs * 1.6f), 120000);
            if (threat == 2) return Math.Max((int)(baseMs * 1.25f), 90000);
            return Math.Max(baseMs, 75000);
        }

        private bool ShouldYieldToRockstar(int wanted)
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            bool mission = false, controlOn = true, faded = false;
            try { mission = Function.Call<bool>(Hash.GET_MISSION_FLAG); } catch { }
            try { controlOn = Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle); } catch { }
            try { faded = Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN); } catch { }
            if (mission)
            {
                if (_lastMissionFlagAt == 0) _lastMissionFlagAt = Game.GameTime;
                return true;
            }
            _lastMissionFlagAt = 0;
            return !controlOn || faded;
        }

        private void ApplyPoliceIgnore()
        {
            try
            {
                Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, true);
                _policeIgnoreApplied = true;
            }
            catch { }
        }

        private void ReleasePoliceIgnore()
        {
            if (!_policeIgnoreApplied) return;
            try { Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, false); } catch { }
            _policeIgnoreApplied = false;
        }

        private static int GetWantedLevel()
        {
            try { return Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle); }
            catch { return 0; }
        }

        private static void SetWantedLevel(int level)
        {
            level = Math.Max(0, Math.Min(5, level));
            try
            {
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, Game.Player.Handle, level, false);
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player.Handle, false);
            }
            catch { }
        }

        private static void SetWantedCentre(Vector3 p)
        {
            if (p == Vector3.Zero) return;
            try { Function.Call(Hash.SET_PLAYER_WANTED_CENTRE_POSITION, Game.Player.Handle, p.X, p.Y, p.Z); } catch { }
        }

        private static string Format(Vector3 p)
        {
            return ((int)p.X) + "," + ((int)p.Y) + "," + ((int)p.Z);
        }

        private void OnAborted(object sender, EventArgs e)
        {
            ReleasePoliceIgnore();
            PoliceSearchRuntimeState.ResetSearch(true);
        }

        private static void Log(string text)
        {
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); }
            catch { }
        }
    }
}
