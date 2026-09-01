using GTA;
using GTA.Native;
using System;
using System.Globalization;
using System.IO;

namespace VOX.CameraRuntimeVI
{
    public sealed class CameraRuntimeVIScript : Script
    {
        private const string ConfigPath = "scripts\\CameraRuntimeVI.ini";
        private const string DataDirectory = "scripts\\CameraRuntimeVI";
        private const string LogPath = DataDirectory + "\\CameraRuntimeVI.log";

        private readonly CameraConfig _cfg;
        private bool _shakeActive;
        private string _shakeName = string.Empty;
        private float _lastAmplitude;
        private float _smoothedAmplitude;
        private float _previousSpeed;
        private int _lastTime;
        private int _collisionBoostUntil;
        private int _lastCollisionBoostAt;
        private bool _wasColliding;
        private bool _wasYielding;
        private int _storyYieldUntil;

        public CameraRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDirectory);
            _cfg = CameraConfig.Load(ConfigPath);
            Interval = 0;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Camera Runtime VI 0.2.0 story-safe secondary motion runtime loaded; player always owns camera direction.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_cfg.Enabled) { ResetEffects(); return; }
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { ResetEffects(); return; }

                if (ShouldYield(player))
                {
                    if (!_wasYielding) ResetEffects();
                    _wasYielding = true;
                    return;
                }
                _wasYielding = false;

                int now = Game.GameTime;
                float dt = _lastTime > 0 ? Clamp((now - _lastTime) / 1000f, 0.001f, 0.10f) : 0.016f;
                _lastTime = now;

                float targetAmplitude;
                string shake;
                if (player.IsInVehicle()) ComputeVehicleMotion(player, dt, now, out shake, out targetAmplitude);
                else ComputeOnFootMotion(player, out shake, out targetAmplitude);

                targetAmplitude *= IsFirstPerson(player) ? _cfg.FirstPersonMultiplier : 1f;
                targetAmplitude = Clamp(targetAmplitude, 0f, _cfg.MaximumAmplitude);

                // Smooth only the secondary amplitude. Direction is never written.
                float smoothing = 1f - (float)Math.Exp(-dt * 10f);
                _smoothedAmplitude = Lerp(_smoothedAmplitude, targetAmplitude, smoothing);
                ApplyShake(shake, _smoothedAmplitude);
            }
            catch (Exception ex)
            {
                Log("Camera tick error: " + ex.Message);
                ResetEffects();
            }
        }

        private void ComputeVehicleMotion(Ped player, float dt, int now, out string shake, out float amplitude)
        {
            shake = "ROAD_VIBRATION_SHAKE";
            amplitude = 0f;
            Vehicle vehicle = player.CurrentVehicle;
            if (vehicle == null || !vehicle.Exists()) return;

            float speed = Math.Max(0f, vehicle.Speed);
            float kph = speed * 3.6f;
            float speedT = SmoothStep(_cfg.VehicleMinimumSpeedKph, _cfg.VehicleFullEffectSpeedKph, kph);
            float acceleration = (speed - _previousSpeed) / Math.Max(0.001f, dt);
            _previousSpeed = speed;

            bool motorcycle = false;
            try
            {
                int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, vehicle.Handle);
                motorcycle = cls == 8 || cls == 13;
            }
            catch { }

            amplitude = Lerp(_cfg.VehicleMinAmplitude, _cfg.VehicleMaxAmplitude, speedT);
            amplitude += Math.Min(1f, Math.Abs(acceleration) / Math.Max(1f, _cfg.AccelerationForFullEffect)) * _cfg.AccelerationAmplitude;
            if (motorcycle) amplitude *= _cfg.MotorcycleMultiplier;

            bool collided = false;
            try { collided = Function.Call<bool>(Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, vehicle.Handle); } catch { }
            if (collided && !_wasColliding && acceleration < -Math.Max(1f, _cfg.CollisionDecelerationMps2) && now - _lastCollisionBoostAt > 650)
            {
                _lastCollisionBoostAt = now;
                _collisionBoostUntil = now + Math.Max(50, _cfg.CollisionBoostMs);
            }
            _wasColliding = collided;
            if (now < _collisionBoostUntil) amplitude += _cfg.CollisionBoostAmplitude;
        }

        private void ComputeOnFootMotion(Ped player, out string shake, out float amplitude)
        {
            shake = "HAND_SHAKE";
            amplitude = 0f;
            _previousSpeed = 0f;
            _wasColliding = false;

            float speed = Math.Max(0f, player.Speed);
            if (speed < 0.35f) return;

            bool sprinting = false, running = false, falling = false, ragdoll = false;
            try { sprinting = Function.Call<bool>(Hash.IS_PED_SPRINTING, player.Handle); } catch { }
            try { running = Function.Call<bool>(Hash.IS_PED_RUNNING, player.Handle); } catch { }
            try { falling = Function.Call<bool>(Hash.IS_PED_FALLING, player.Handle); } catch { }
            try { ragdoll = Function.Call<bool>(Hash.IS_PED_RAGDOLL, player.Handle); } catch { }
            running |= sprinting;
            falling |= ragdoll;

            if (falling) amplitude = _cfg.FallAmplitude;
            else if (sprinting) amplitude = _cfg.SprintAmplitude;
            else if (running) amplitude = _cfg.RunAmplitude;
            else amplitude = _cfg.WalkAmplitude;
        }

        private bool ShouldYield(Ped player)
        {
            bool storyOwns = false;
            try { storyOwns |= Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE); } catch { }
            try { storyOwns |= Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS); } catch { }
            try { storyOwns |= Function.Call<bool>(Hash.GET_MISSION_FLAG); } catch { }
            try { storyOwns |= !Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle); } catch { }
            try
            {
                storyOwns |= Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) ||
                             Function.Call<bool>(Hash.IS_SCREEN_FADING_IN);
            }
            catch { }
            if (storyOwns)
            {
                _storyYieldUntil = Game.GameTime + 5000;
                return true;
            }
            if (Game.GameTime < _storyYieldUntil) return true;

            try { if (Function.Call<bool>(Hash.IS_CINEMATIC_CAM_RENDERING)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_GAMEPLAY_CAM_RENDERING)) return true; } catch { }

            if (_cfg.DisableWhenAiming)
            {
                try { if (Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle)) return true; } catch { }
                try { if (Function.Call<bool>(Hash.IS_AIM_CAM_ACTIVE)) return true; } catch { }
            }

            if (_cfg.DisableOnManualLook)
            {
                if (Math.Abs(ControlValue(1)) > _cfg.ManualLookDeadzone) return true;
                if (Math.Abs(ControlValue(2)) > _cfg.ManualLookDeadzone) return true;
                if (ControlPressed(26)) return true;
            }
            return false;
        }

        private bool IsFirstPerson(Ped player)
        {
            try
            {
                int mode = player.IsInVehicle() ? Function.Call<int>(Hash.GET_FOLLOW_VEHICLE_CAM_VIEW_MODE) : Function.Call<int>(Hash.GET_FOLLOW_PED_CAM_VIEW_MODE);
                return mode == 4;
            }
            catch { return false; }
        }

        private void ApplyShake(string name, float amplitude)
        {
            if (amplitude <= 0.0005f || string.IsNullOrEmpty(name)) { StopShake(); return; }
            if (!_shakeActive || !string.Equals(_shakeName, name, StringComparison.Ordinal))
            {
                StopShake();
                try
                {
                    Function.Call(Hash.SHAKE_GAMEPLAY_CAM, name, amplitude);
                    _shakeActive = true;
                    _shakeName = name;
                    _lastAmplitude = amplitude;
                }
                catch { }
                return;
            }
            if (Math.Abs(amplitude - _lastAmplitude) > 0.001f)
            {
                try { Function.Call(Hash.SET_GAMEPLAY_CAM_SHAKE_AMPLITUDE, amplitude); } catch { }
                _lastAmplitude = amplitude;
            }
        }

        private void StopShake()
        {
            if (_shakeActive) { try { Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true); } catch { } }
            _shakeActive = false;
            _shakeName = string.Empty;
            _lastAmplitude = 0f;
        }

        private void ResetEffects()
        {
            StopShake();
            _smoothedAmplitude = 0f;
            _previousSpeed = 0f;
            _lastTime = 0;
            _collisionBoostUntil = 0;
            _wasColliding = false;
        }

        private static float ControlValue(int control) { try { return Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, control); } catch { return 0f; } }
        private static bool ControlPressed(int control) { try { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, control); } catch { return false; } }
        private void OnAborted(object sender, EventArgs e) { ResetEffects(); }
        private static float Clamp(float v, float min, float max) { return v < min ? min : (v > max ? max : v); }
        private static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp(t, 0f, 1f); }
        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Clamp((x - edge0) / Math.Max(0.001f, edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
        private static void Log(string text)
        {
            try { File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + text + Environment.NewLine); }
            catch { }
        }
    }

    internal sealed class CameraConfig
    {
        public bool Enabled = true;
        public bool DisableWhenAiming = true;
        public bool DisableOnManualLook = true;
        public float ManualLookDeadzone = 0.035f;
        public float FirstPersonMultiplier = 0.45f;
        public float MaximumAmplitude = 0.12f;
        public float WalkAmplitude = 0.006f;
        public float RunAmplitude = 0.012f;
        public float SprintAmplitude = 0.020f;
        public float FallAmplitude = 0.035f;
        public float VehicleMinimumSpeedKph = 18f;
        public float VehicleFullEffectSpeedKph = 190f;
        public float VehicleMinAmplitude = 0.004f;
        public float VehicleMaxAmplitude = 0.052f;
        public float AccelerationForFullEffect = 9f;
        public float AccelerationAmplitude = 0.016f;
        public float MotorcycleMultiplier = 1.18f;
        public float CollisionDecelerationMps2 = 9f;
        public float CollisionBoostAmplitude = 0.045f;
        public int CollisionBoostMs = 240;

        public static CameraConfig Load(string path)
        {
            var c = new CameraConfig();
            try
            {
                if (!File.Exists(path)) return c;
                string section = string.Empty;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                    int eq = line.IndexOf('='); if (eq <= 0) continue;
                    string key = section + "." + line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    bool bv; int iv; float fv;
                    if (key.Equals("General.Enabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out bv)) c.Enabled = bv;
                    else if (key.Equals("General.DisableWhenAiming", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out bv)) c.DisableWhenAiming = bv;
                    else if (key.Equals("General.DisableOnManualLook", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out bv)) c.DisableOnManualLook = bv;
                    else if (key.Equals("General.ManualLookDeadzone", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.ManualLookDeadzone = fv;
                    else if (key.Equals("General.FirstPersonMultiplier", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.FirstPersonMultiplier = fv;
                    else if (key.Equals("General.MaximumAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.MaximumAmplitude = fv;
                    else if (key.Equals("OnFoot.WalkAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.WalkAmplitude = fv;
                    else if (key.Equals("OnFoot.RunAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.RunAmplitude = fv;
                    else if (key.Equals("OnFoot.SprintAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.SprintAmplitude = fv;
                    else if (key.Equals("OnFoot.FallAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.FallAmplitude = fv;
                    else if (key.Equals("Vehicle.MinimumSpeedKph", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.VehicleMinimumSpeedKph = fv;
                    else if (key.Equals("Vehicle.FullEffectSpeedKph", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.VehicleFullEffectSpeedKph = fv;
                    else if (key.Equals("Vehicle.MinAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.VehicleMinAmplitude = fv;
                    else if (key.Equals("Vehicle.MaxAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.VehicleMaxAmplitude = fv;
                    else if (key.Equals("Vehicle.AccelerationForFullEffect", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.AccelerationForFullEffect = fv;
                    else if (key.Equals("Vehicle.AccelerationAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.AccelerationAmplitude = fv;
                    else if (key.Equals("Vehicle.MotorcycleMultiplier", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.MotorcycleMultiplier = fv;
                    else if (key.Equals("Vehicle.CollisionDecelerationMps2", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.CollisionDecelerationMps2 = fv;
                    else if (key.Equals("Vehicle.CollisionBoostAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(value, out fv)) c.CollisionBoostAmplitude = fv;
                    else if (key.Equals("Vehicle.CollisionBoostMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out iv)) c.CollisionBoostMs = iv;
                }
            }
            catch { }
            return c;
        }
        private static bool TryFloat(string value, out float result) { return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result); }
    }
}
