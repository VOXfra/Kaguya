using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.CharacterRuntimeVI
{
    public sealed class CharacterRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\CharacterRuntimeVI";
        private const string LogPath = DataDir + "\\CharacterRuntimeVI.log";
        private const string StatsPath = DataDir + "\\FitnessStats.txt";
        private const string BodyStatePath = DataDir + "\\BodyMorphState.txt";

        private sealed class FitnessProfile
        {
            public int ModelHash;
            public float Strength;
            public float Endurance;
            public float LeanMass;
            public float TrainingLoad;
            public long LastSavedUtcTicks;
        }

        private readonly Dictionary<int, FitnessProfile> _profiles = new Dictionary<int, FitnessProfile>();
        private int _currentModel;
        private FitnessProfile _current;
        private int _lastTick;
        private int _lastSave;
        private int _lastBodyState;
        private int _lastProgressLog;

        public CharacterRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Load();
            FitnessRuntimeBridge.AddTraining = ApplyWorkoutTraining;
            Interval = 50;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Character Runtime VI 0.1.1 loaded: persistent fitness, physical workouts and native body-morph bridge state.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead)
                {
                    _lastTick = 0;
                    WriteBodyState(null, 1f, false, true);
                    return;
                }

                SelectProfile(player);
                int now = Game.GameTime;
                float dt = _lastTick > 0 ? Math.Max(0f, Math.Min(0.20f, (now - _lastTick) / 1000f)) : 0.05f;
                _lastTick = now;

                bool rockstarOwns = RockstarOwnsScene();
                if (!rockstarOwns)
                {
                    UpdateTraining(player, dt);
                    ApplyPerformanceStats();
                }
                else ApplyNeutralPerformanceStats();

                float bodyWidth = BodyWidthScale();
                bool bodySafe = !rockstarOwns && BodyMorphSafe(player);
                if (now - _lastBodyState >= 150)
                {
                    _lastBodyState = now;
                    WriteBodyState(player, bodyWidth, bodySafe, false);
                }

                if (now - _lastSave >= 10000)
                {
                    _lastSave = now;
                    Save();
                }
            }
            catch (Exception ex)
            {
                Log("Tick error: " + ex.Message);
                WriteBodyState(null, 1f, false, true);
            }
        }

        private void SelectProfile(Ped player)
        {
            int model = player.Model.Hash;
            if (_current != null && _currentModel == model) return;
            _currentModel = model;
            if (!_profiles.TryGetValue(model, out _current))
            {
                float strength = BaselineStrength(model);
                _current = new FitnessProfile
                {
                    ModelHash = model,
                    Strength = strength,
                    Endurance = BaselineEndurance(model),
                    LeanMass = Math.Max(10f, strength * 0.62f),
                    TrainingLoad = 0f
                };
                _profiles[model] = _current;
            }
            Log("Fitness profile selected model=" + model + " strength=" + _current.Strength.ToString("0.0", CultureInfo.InvariantCulture) +
                " endurance=" + _current.Endurance.ToString("0.0", CultureInfo.InvariantCulture) +
                " lean=" + _current.LeanMass.ToString("0.0", CultureInfo.InvariantCulture) + ".");
        }

        private void UpdateTraining(Ped player, float dt)
        {
            if (_current == null || dt <= 0f) return;
            bool sprinting = SafeBool(Hash.IS_PED_SPRINTING, player.Handle);
            bool running = sprinting || SafeBool(Hash.IS_PED_RUNNING, player.Handle);
            bool swimming = SafeBool(Hash.IS_PED_SWIMMING, player.Handle) || SafeBool(Hash.IS_PED_SWIMMING_UNDER_WATER, player.Handle);
            bool melee = SafeBool(Hash.IS_PED_IN_MELEE_COMBAT, player.Handle);

            float enduranceWork = 0f;
            float strengthWork = 0f;
            if (sprinting) enduranceWork += 1.00f;
            else if (running) enduranceWork += 0.48f;
            if (swimming) { enduranceWork += 0.72f; strengthWork += 0.18f; }
            if (melee) strengthWork += 1.00f;

            float work = enduranceWork + strengthWork;
            if (work > 0f)
            {
                _current.TrainingLoad = Clamp(_current.TrainingLoad + work * dt * 0.55f, 0f, 100f);
                _current.Endurance = Clamp(_current.Endurance + enduranceWork * dt * 0.012f, 0f, 100f);
                _current.Strength = Clamp(_current.Strength + strengthWork * dt * 0.010f, 0f, 100f);
                float leanGain = (strengthWork * 0.006f + enduranceWork * 0.0015f) * dt;
                leanGain *= 0.45f + _current.TrainingLoad / 100f * 0.55f;
                _current.LeanMass = Clamp(_current.LeanMass + leanGain, 0f, 100f);
            }
            else _current.TrainingLoad = Math.Max(0f, _current.TrainingLoad - dt * 0.020f);

            if (Game.GameTime - _lastProgressLog > 30000 && work > 0f)
            {
                _lastProgressLog = Game.GameTime;
                Log("Training progress strength=" + _current.Strength.ToString("0.00", CultureInfo.InvariantCulture) +
                    " endurance=" + _current.Endurance.ToString("0.00", CultureInfo.InvariantCulture) +
                    " lean=" + _current.LeanMass.ToString("0.00", CultureInfo.InvariantCulture) +
                    " load=" + _current.TrainingLoad.ToString("0.0", CultureInfo.InvariantCulture) + ".");
            }
        }

        private void ApplyWorkoutTraining(float strength, float endurance, float leanMass)
        {
            Ped player = Game.LocalPlayerPed;
            if (player == null || !player.Exists() || player.IsDead) return;
            SelectProfile(player);
            if (_current == null) return;

            _current.Strength = Clamp(_current.Strength + Math.Max(0f, strength), 0f, 100f);
            _current.Endurance = Clamp(_current.Endurance + Math.Max(0f, endurance), 0f, 100f);
            _current.LeanMass = Clamp(_current.LeanMass + Math.Max(0f, leanMass), 0f, 100f);
            _current.TrainingLoad = Clamp(_current.TrainingLoad + Math.Max(0.5f, strength * 2.2f + endurance + leanMass), 0f, 100f);
            Save();

            Log("Physical workout credited: strength=" + _current.Strength.ToString("0.00", CultureInfo.InvariantCulture) +
                " endurance=" + _current.Endurance.ToString("0.00", CultureInfo.InvariantCulture) +
                " lean=" + _current.LeanMass.ToString("0.00", CultureInfo.InvariantCulture) + ".");
        }

        private void ApplyPerformanceStats()
        {
            if (_current == null) return;
            float sprint = 1f + _current.Endurance * 0.00115f;
            float melee = 1f + _current.Strength * 0.0018f;
            try { Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, Game.Player.Handle, Math.Min(1.15f, sprint)); } catch { }
            try { Function.Call(Hash.SET_PLAYER_MELEE_WEAPON_DAMAGE_MODIFIER, Game.Player.Handle, Math.Min(1.20f, melee), true); } catch { }
        }

        private static void ApplyNeutralPerformanceStats()
        {
            try { Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, Game.Player.Handle, 1f); } catch { }
            try { Function.Call(Hash.SET_PLAYER_MELEE_WEAPON_DAMAGE_MODIFIER, Game.Player.Handle, 1f, true); } catch { }
        }

        private float BodyWidthScale()
        {
            if (_current == null) return 1f;
            float physique = Clamp((_current.Strength * 0.42f + _current.LeanMass * 0.58f) / 100f, 0f, 1f);
            return 1f + physique * 0.045f;
        }

        private static bool BodyMorphSafe(Ped player)
        {
            if (player == null || !player.Exists() || player.IsDead || player.IsInVehicle()) return false;
            if (SafeBool(Hash.IS_PED_RAGDOLL, player.Handle) || SafeBool(Hash.IS_PED_FALLING, player.Handle) ||
                SafeBool(Hash.IS_PED_CLIMBING, player.Handle) || SafeBool(Hash.IS_PED_JUMPING, player.Handle) ||
                SafeBool(Hash.IS_PED_SWIMMING, player.Handle) || SafeBool(Hash.IS_PED_SWIMMING_UNDER_WATER, player.Handle)) return false;
            return true;
        }

        private static void WriteBodyState(Ped player, float width, bool enabled, bool force)
        {
            try
            {
                if (!force && player == null) return;
                int handle = player != null && player.Exists() ? player.Handle : 0;
                Vector3 p = player != null && player.Exists() ? player.Position : Vector3.Zero;
                string text =
                    "enabled=" + enabled + Environment.NewLine +
                    "ped=" + handle.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "x=" + p.X.ToString("R", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "y=" + p.Y.ToString("R", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "z=" + p.Z.ToString("R", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "width=" + Clamp(width, 1f, 1.05f).ToString("R", CultureInfo.InvariantCulture) + Environment.NewLine;
                File.WriteAllText(BodyStatePath, text);
            }
            catch { }
        }

        private static float BaselineStrength(int model)
        {
            int michael = SafeHash("player_zero"), franklin = SafeHash("player_one"), trevor = SafeHash("player_two");
            if (model == michael) return 42f;
            if (model == franklin) return 48f;
            if (model == trevor) return 52f;
            return 35f;
        }

        private static float BaselineEndurance(int model)
        {
            int michael = SafeHash("player_zero"), franklin = SafeHash("player_one"), trevor = SafeHash("player_two");
            if (model == michael) return 38f;
            if (model == franklin) return 52f;
            if (model == trevor) return 44f;
            return 35f;
        }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { return Function.Call<bool>(Hash.GET_MISSION_FLAG); } catch { return false; }
        }

        private static bool SafeBool(Hash hash, params InputArgument[] args)
        {
            try { return Function.Call<bool>(hash, args); } catch { return false; }
        }

        private static int SafeHash(string name)
        {
            try { return Function.Call<int>(Hash.GET_HASH_KEY, name); } catch { return 0; }
        }

        private void Load()
        {
            if (!File.Exists(StatsPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(StatsPath))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 5) continue;
                    int model;
                    float strength, endurance, lean, load;
                    if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out model)) continue;
                    if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out strength)) continue;
                    if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out endurance)) continue;
                    if (!float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out lean)) continue;
                    if (!float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out load)) continue;
                    _profiles[model] = new FitnessProfile { ModelHash = model, Strength = Clamp(strength,0,100), Endurance = Clamp(endurance,0,100), LeanMass = Clamp(lean,0,100), TrainingLoad = Clamp(load,0,100) };
                }
            }
            catch (Exception ex) { Log("Fitness load failed safely: " + ex.Message); }
        }

        private void Save()
        {
            try
            {
                var lines = new List<string>();
                foreach (FitnessProfile p in _profiles.Values)
                {
                    p.LastSavedUtcTicks = DateTime.UtcNow.Ticks;
                    lines.Add(p.ModelHash.ToString(CultureInfo.InvariantCulture) + "|" +
                        p.Strength.ToString("0.0000", CultureInfo.InvariantCulture) + "|" +
                        p.Endurance.ToString("0.0000", CultureInfo.InvariantCulture) + "|" +
                        p.LeanMass.ToString("0.0000", CultureInfo.InvariantCulture) + "|" +
                        p.TrainingLoad.ToString("0.0000", CultureInfo.InvariantCulture) + "|" +
                        p.LastSavedUtcTicks.ToString(CultureInfo.InvariantCulture));
                }
                File.WriteAllLines(StatsPath, lines.ToArray());
            }
            catch (Exception ex) { Log("Fitness save failed safely: " + ex.Message); }
        }

        private static float Clamp(float v, float min, float max) { return v < min ? min : (v > max ? max : v); }

        private void OnAborted(object sender, EventArgs e)
        {
            if (FitnessRuntimeBridge.AddTraining == ApplyWorkoutTraining) FitnessRuntimeBridge.AddTraining = null;
            Save();
            ApplyNeutralPerformanceStats();
            WriteBodyState(null, 1f, false, true);
        }

        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); } catch { }
        }
    }
}
