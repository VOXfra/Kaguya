using GTA;
using GTA.Native;
using System;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    // A deliberately separate script inside PoliceOverhaulVI.dll. It owns slow,
    // persistent world-continuity systems so the pursuit brain stays small and
    // mission-sensitive logic is not mixed with scene rendering/persistence.
    public sealed class PoliceWorldContinuityScript : Script
    {
        private const string DataDirectory = "scripts\\PoliceOverhaulVI";
        private const string CasesPath = DataDirectory + "\\Cases.xml";
        private const string LogPath = DataDirectory + "\\PoliceOverhaulVI.log";
        private CaseRepository _repository = new CaseRepository();
        private readonly CrimeSceneSystem _crimeScenes;
        private readonly BoloRecognitionSystem _bolo = new BoloRecognitionSystem();
        private DateTime _lastCasesWriteUtc = DateTime.MinValue;
        private int _lastCaseReload;
        private int _lastWanted;
        private int _currentModel;

        public PoliceWorldContinuityScript()
        {
            Directory.CreateDirectory(DataDirectory);
            _crimeScenes = new CrimeSceneSystem(Log);
            ReloadCases(true);
            Tick += OnTick;
            Aborted += OnAborted;
            Interval = 100;
            Log("Police Overhaul VI 0.4.0 world continuity loaded: persistent crime scenes + merchant BOLO recognition.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) return;
                if (IsUnambiguousRockstarOwnership()) return;

                int now = Game.GameTime;
                if (now - _lastCaseReload >= 2500)
                {
                    _lastCaseReload = now;
                    ReloadCases(false);
                }

                int wanted = GetWantedLevel();
                int model = player.Model.Hash;
                CaseMemory memory = _repository.GetOrCreate(model);

                if (_currentModel != model)
                {
                    _currentModel = model;
                    _bolo.Reset();
                }

                // A newly confirmed wanted event leaves a persistent origin even
                // if the player escapes before any body/firearm scan can see it.
                if (_lastWanted == 0 && wanted >= 2)
                {
                    bool shooting = false;
                    try { shooting = Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle); } catch { }
                    _crimeScenes.RecordIncident(player.Position, wanted, model, ObservationSource.Police,
                        shooting || wanted >= 3, false, Math.Max(1, wanted - 1), Log);
                }

                _crimeScenes.Update(player, wanted, model, Log);

                if (wanted == 0)
                {
                    BoloReportResult report = _bolo.Update(player, memory, wanted, Log);
                    if (report != null && report.Triggered)
                    {
                        // Feed the ordinary Police Overhaul witness pipeline rather
                        // than creating an omniscient custom chase. The merchant is
                        // still physically present when this vanilla wanted pulse is
                        // emitted, so the main runtime can confirm the report.
                        SetWantedLevel(report.WantedLevel);
                    }
                }

                _lastWanted = GetWantedLevel();
            }
            catch (Exception ex)
            {
                Log("World-continuity tick error: " + ex);
            }
        }

        private void ReloadCases(bool force)
        {
            try
            {
                if (!File.Exists(CasesPath)) return;
                DateTime write = File.GetLastWriteTimeUtc(CasesPath);
                if (!force && write <= _lastCasesWriteUtc) return;
                var next = new CaseRepository();
                Persistence.LoadCases(CasesPath, next, null);
                _repository = next;
                _lastCasesWriteUtc = write;
            }
            catch { }
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

        private static bool IsUnambiguousRockstarOwnership()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            return false;
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _crimeScenes.Save(Log);
        }

        private static void Log(string text)
        {
            try { File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + text + Environment.NewLine); }
            catch { }
        }
    }
}
