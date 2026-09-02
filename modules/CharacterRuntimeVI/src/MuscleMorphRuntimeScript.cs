using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.IO;

namespace VOX.CharacterRuntimeVI
{
    public sealed class CharacterRuntimeVIMuscleMorphScript : Script
    {
        private const string DataDir = "scripts\\CharacterRuntimeVI";
        private const string LogPath = DataDir + "\\CharacterRuntimeVI.log";

        private int _pedHandle;
        private PedBone _leftClavicle, _rightClavicle, _leftUpperArm, _rightUpperArm;
        private Vector3 _leftClavicleBase, _rightClavicleBase, _leftUpperArmBase, _rightUpperArmBase;
        private bool _baselineValid;
        private bool _morphApplied;
        private int _storyYieldUntil;
        private int _lastLog;

        public CharacterRuntimeVIMuscleMorphScript()
        {
            Interval = 75;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Muscle morph 0.1.0 loaded: SHVDN ped-bone shoulder/upper-body morph driven by fitness profile.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead)
                {
                    Restore();
                    ClearBaseline();
                    return;
                }

                if (StoryOwnsScene()) _storyYieldUntil = Game.GameTime + 5000;
                bool unsafeState = Game.GameTime < _storyYieldUntil ||
                    SafeBool(Hash.IS_PED_RAGDOLL, player.Handle) || SafeBool(Hash.IS_PED_FALLING, player.Handle) ||
                    SafeBool(Hash.IS_PED_CLIMBING, player.Handle) || SafeBool(Hash.IS_PED_SWIMMING, player.Handle) ||
                    SafeBool(Hash.IS_PED_SWIMMING_UNDER_WATER, player.Handle);

                if (_pedHandle != player.Handle)
                {
                    Restore();
                    CaptureBaseline(player);
                }

                if (unsafeState || !FitnessRuntimeBridge.CurrentProfileValid)
                {
                    Restore();
                    return;
                }

                if (!_baselineValid) CaptureBaseline(player);
                if (!_baselineValid) return;

                float physique = Clamp((FitnessRuntimeBridge.CurrentStrength * 0.44f + FitnessRuntimeBridge.CurrentLeanMass * 0.56f) / 100f, 0f, 1f);
                float clavicleSpread = 0.006f + physique * 0.018f;
                float armSpread = 0.002f + physique * 0.008f;

                _leftClavicle.RelativePosition = Outward(_leftClavicleBase, clavicleSpread, true);
                _rightClavicle.RelativePosition = Outward(_rightClavicleBase, clavicleSpread, false);
                _leftUpperArm.RelativePosition = Outward(_leftUpperArmBase, armSpread, true);
                _rightUpperArm.RelativePosition = Outward(_rightUpperArmBase, armSpread, false);
                _morphApplied = true;

                if (Game.GameTime - _lastLog > 30000)
                {
                    _lastLog = Game.GameTime;
                    Log("Visible muscle morph active physique=" + (physique * 100f).ToString("0.0") + "% shoulderSpread=" + (clavicleSpread * 100f).ToString("0.00") + "cm.");
                }
            }
            catch (Exception ex)
            {
                Log("Muscle morph error; restoring safely: " + ex.Message);
                Restore();
                ClearBaseline();
            }
        }

        private void CaptureBaseline(Ped player)
        {
            ClearBaseline();
            if (player == null || !player.Exists()) return;
            try
            {
                _pedHandle = player.Handle;
                _leftClavicle = player.Bones[Bone.SkelLeftClavicle];
                _rightClavicle = player.Bones[Bone.SkelRightClavicle];
                _leftUpperArm = player.Bones[Bone.SkelLeftUpperArm];
                _rightUpperArm = player.Bones[Bone.SkelRightUpperArm];
                if (!_leftClavicle.IsValid || !_rightClavicle.IsValid || !_leftUpperArm.IsValid || !_rightUpperArm.IsValid)
                {
                    Log("Muscle morph skipped: required protagonist skeleton bones are unavailable.");
                    return;
                }
                _leftClavicleBase = _leftClavicle.RelativePosition;
                _rightClavicleBase = _rightClavicle.RelativePosition;
                _leftUpperArmBase = _leftUpperArm.RelativePosition;
                _rightUpperArmBase = _rightUpperArm.RelativePosition;
                _baselineValid = true;
                Log("Muscle morph baseline captured ped=" + _pedHandle + ".");
            }
            catch (Exception ex)
            {
                Log("Muscle morph baseline failed safely: " + ex.Message);
                ClearBaseline();
            }
        }

        private static Vector3 Outward(Vector3 baseline, float amount, bool left)
        {
            float sign;
            if (Math.Abs(baseline.X) > 0.001f) sign = Math.Sign(baseline.X);
            else sign = left ? -1f : 1f;
            return new Vector3(baseline.X + sign * amount, baseline.Y, baseline.Z);
        }

        private void Restore()
        {
            if (!_morphApplied || !_baselineValid) return;
            try
            {
                if (_leftClavicle != null && _leftClavicle.IsValid) _leftClavicle.RelativePosition = _leftClavicleBase;
                if (_rightClavicle != null && _rightClavicle.IsValid) _rightClavicle.RelativePosition = _rightClavicleBase;
                if (_leftUpperArm != null && _leftUpperArm.IsValid) _leftUpperArm.RelativePosition = _leftUpperArmBase;
                if (_rightUpperArm != null && _rightUpperArm.IsValid) _rightUpperArm.RelativePosition = _rightUpperArmBase;
            }
            catch { }
            _morphApplied = false;
        }

        private void ClearBaseline()
        {
            _pedHandle = 0;
            _leftClavicle = _rightClavicle = _leftUpperArm = _rightUpperArm = null;
            _baselineValid = false;
            _morphApplied = false;
        }

        private static bool StoryOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true; } catch { }
            return false;
        }
        private static bool SafeBool(Hash h, params InputArgument[] args) { try { return Function.Call<bool>(h, args); } catch { return false; } }
        private static float Clamp(float v, float min, float max) { return v < min ? min : (v > max ? max : v); }
        private void OnAborted(object sender, EventArgs e) { Restore(); ClearBaseline(); }
        private static void Log(string text) { try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); } catch { } }
    }
}
