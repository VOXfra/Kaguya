using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.IO;

namespace VOX.CharacterRuntimeVI
{
    public sealed class CharacterRuntimeVIWorkoutScript : Script
    {
        private const string DataDir = "scripts\\CharacterRuntimeVI";
        private const string LogPath = DataDir + "\\CharacterRuntimeVI.log";
        private const int ContextControl = 51;
        private const int SessionDurationMs = 15000;

        private static readonly Vector3[] MuscleBeachStations =
        {
            new Vector3(-1202.67f, -1565.53f, 4.61f),
            new Vector3(-1210.31f, -1561.34f, 4.61f),
            new Vector3(-1198.52f, -1564.12f, 4.61f)
        };

        private static readonly string[] WeightProps =
        {
            "prop_barbell_01",
            "prop_barbell_02",
            "prop_barbell_10kg",
            "prop_barbell_20kg",
            "prop_barbell_30kg",
            "prop_barbell_40kg",
            "prop_barbell_50kg",
            "prop_barbell_60kg",
            "prop_barbell_80kg",
            "prop_barbell_100kg",
            "prop_curl_bar_01",
            "prop_weight_bench_02"
        };

        private bool _training;
        private int _trainingStarted;
        private int _cancelAllowedAt;
        private int _lastHelp;
        private int _lastProbe;
        private bool _nearEquipment;
        private Vector3 _equipmentPosition;

        public CharacterRuntimeVIWorkoutScript()
        {
            Directory.CreateDirectory(DataDir);
            Interval = 50;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Workout activity 0.1.2 loaded: persistent free-weight scenario + isolated Context input.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead)
                {
                    Cancel(player, false);
                    return;
                }
                if (RockstarOwnsScene())
                {
                    Cancel(player, false);
                    return;
                }

                if (_training)
                {
                    UpdateSession(player);
                    return;
                }

                if (player.IsInVehicle()) return;
                int now = Game.GameTime;
                if (now - _lastProbe >= 450)
                {
                    _lastProbe = now;
                    _nearEquipment = FindWorkoutEquipment(player.Position, out _equipmentPosition);
                }
                if (!_nearEquipment) return;

                // E is consumed only while the workout affordance is active so the
                // same press cannot repeatedly trigger unrelated world interactions.
                DisableContext();
                ShowHelp("Appuyez sur ~INPUT_CONTEXT~ pour faire une seance de musculation.");
                if (!ContextJustPressed()) return;
                BeginSession(player);
            }
            catch (Exception ex)
            {
                Log("Workout tick error: " + ex.Message);
            }
        }

        private void BeginSession(Ped player)
        {
            if (player == null || !player.Exists()) return;
            try
            {
                // 0 made Enhanced immediately finish/cancel this scenario on some
                // builds. -1 keeps it alive until this runtime explicitly clears it.
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, player.Handle, "WORLD_HUMAN_MUSCLE_FREE_WEIGHTS", -1, true);
                _training = true;
                _trainingStarted = Game.GameTime;
                _cancelAllowedAt = _trainingStarted + 1400;
                Log("Weightlifting session started near " + F(_equipmentPosition) + ".");
            }
            catch (Exception ex)
            {
                Log("Could not start native free-weight scenario: " + ex.Message);
                _training = false;
            }
        }

        private void UpdateSession(Ped player)
        {
            DisableContext();
            int now = Game.GameTime;
            int elapsed = now - _trainingStarted;
            if (now >= _cancelAllowedAt && ContextJustPressed())
            {
                Cancel(player, true);
                return;
            }

            if (elapsed < SessionDurationMs)
            {
                int remaining = Math.Max(0, (SessionDurationMs - elapsed + 999) / 1000);
                ShowHelp("Musculation : " + remaining + " s  |  ~INPUT_CONTEXT~ pour arreter");
                return;
            }

            try { Function.Call(Hash.CLEAR_PED_TASKS, player.Handle); } catch { }
            _training = false;
            _trainingStarted = 0;
            _cancelAllowedAt = 0;

            // Strong enough to make repeated physical sessions visibly matter,
            // while the main runtime still caps final gameplay/body effects.
            FitnessRuntimeBridge.Train(0.85f, 0.12f, 0.50f);
            Notify("Seance terminee : force et masse musculaire en progression.");
            Log("Weightlifting session completed; strength +0.85, endurance +0.12, lean mass +0.50.");
        }

        private void Cancel(Ped player, bool manual)
        {
            if (!_training) return;
            try { if (player != null && player.Exists()) Function.Call(Hash.CLEAR_PED_TASKS, player.Handle); } catch { }
            _training = false;
            _trainingStarted = 0;
            _cancelAllowedAt = 0;
            if (manual) Log("Weightlifting session cancelled; no fitness reward granted.");
        }

        private static bool FindWorkoutEquipment(Vector3 playerPos, out Vector3 equipment)
        {
            foreach (Vector3 fixedStation in MuscleBeachStations)
            {
                if (Distance(playerPos, fixedStation) <= 3.2f)
                {
                    equipment = fixedStation;
                    return true;
                }
            }

            foreach (string modelName in WeightProps)
            {
                int model = SafeHash(modelName);
                if (model == 0) continue;
                int obj = 0;
                try
                {
                    obj = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,
                        playerPos.X, playerPos.Y, playerPos.Z, 3.4f, model, false, false, false);
                }
                catch { }
                if (obj == 0) continue;
                try
                {
                    if (!Function.Call<bool>(Hash.DOES_ENTITY_EXIST, obj)) continue;
                    Vector3 pos = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, obj, true);
                    if (Distance(playerPos, pos) > 3.4f) continue;
                    equipment = pos;
                    return true;
                }
                catch { }
            }

            equipment = Vector3.Zero;
            return false;
        }

        private static void DisableContext()
        {
            try { Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, ContextControl, true); } catch { }
        }

        private static bool ContextJustPressed()
        {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, ContextControl); }
            catch { return false; }
        }

        private void ShowHelp(string text)
        {
            if (Game.GameTime - _lastHelp < 80) return;
            _lastHelp = Game.GameTime;
            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP, 0, false, true, -1);
            }
            catch { }
        }

        private static void Notify(string text)
        {
            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, false);
            }
            catch { }
        }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { return Function.Call<bool>(Hash.GET_MISSION_FLAG); } catch { return false; }
        }

        private static int SafeHash(string name)
        {
            try { return Function.Call<int>(Hash.GET_HASH_KEY, name); }
            catch { return 0; }
        }

        private static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        private static string F(Vector3 p)
        {
            return p.X.ToString("0.0") + "," + p.Y.ToString("0.0") + "," + p.Z.ToString("0.0");
        }

        private void OnAborted(object sender, EventArgs e)
        {
            Cancel(Game.LocalPlayerPed, false);
        }

        private static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine);
            }
            catch { }
        }
    }
}
