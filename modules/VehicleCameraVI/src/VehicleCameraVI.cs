using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Globalization;
using System.IO;

namespace VOX.VehicleCameraVI
{
    public sealed class VehicleCameraVIScript : Script
    {
        private const string ConfigPath = "scripts\\VehicleCameraVI.ini";
        private const string DataDirectory = "scripts\\VehicleCameraVI";
        private const string LogPath = DataDirectory + "\\VehicleCameraVI.log";
        private CameraConfig _cfg;
        private bool _shakeActive;
        private float _previousSpeed;
        private float _previousHeading;
        private float _lookOffset;
        private float _lastAppliedOffset;
        private int _lastTime;
        private int _collisionBoostUntil;

        public VehicleCameraVIScript()
        {
            Directory.CreateDirectory(DataDirectory);
            _cfg = CameraConfig.Load(ConfigPath);
            Tick += OnTick;
            Aborted += OnAborted;
            Interval = 0;
            Log("Vehicle Camera VI 0.1.0 motion/inertia runtime loaded.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_cfg.Enabled) { ResetEffects(); return; }
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead || !player.IsInVehicle()) { ResetEffects(); return; }
                Vehicle vehicle = player.CurrentVehicle;
                if (vehicle == null || !vehicle.Exists()) { ResetEffects(); return; }
                if (ShouldYield()) { ResetEffects(); return; }

                int now = Game.GameTime;
                float dt = _lastTime > 0 ? Math.Max(0.001f, Math.Min(0.1f, (now - _lastTime) / 1000f)) : 0.016f;
                _lastTime = now;

                float speed = Math.Max(0f, vehicle.Speed);
                float speedKph = speed * 3.6f;
                float speedT = Clamp01((speedKph - _cfg.MinimumSpeedKph) / Math.Max(1f, _cfg.FullEffectSpeedKph - _cfg.MinimumSpeedKph));

                float acceleration = (speed - _previousSpeed) / dt;
                _previousSpeed = speed;

                Vector3 forward = vehicle.ForwardVector;
                float heading = (float)(Math.Atan2(forward.Y, forward.X) * 180.0 / Math.PI);
                float yawRate = NormalizeAngle(heading - _previousHeading) / dt;
                _previousHeading = heading;

                bool collided = false;
                try { collided = Function.Call<bool>(Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, vehicle.Handle); } catch { }
                if (collided && acceleration < -_cfg.CollisionDecelerationMps2)
                    _collisionBoostUntil = now + _cfg.CollisionBoostMs;

                float accelT = Math.Min(1f, Math.Abs(acceleration) / Math.Max(1f, _cfg.AccelerationForFullEffect));
                float targetShake = Lerp(_cfg.MinShakeAmplitude, _cfg.MaxShakeAmplitude, speedT);
                targetShake += accelT * _cfg.AccelerationShakeAmplitude;
                if (now < _collisionBoostUntil) targetShake += _cfg.CollisionShakeBoost;
                targetShake = Math.Max(0f, Math.Min(_cfg.MaximumTotalShake, targetShake));
                UpdateShake(targetShake, speedT);

                float targetLook = 0f;
                if (speedKph >= _cfg.LookAheadMinimumSpeedKph)
                    targetLook = Math.Max(-_cfg.MaximumLookAheadDegrees, Math.Min(_cfg.MaximumLookAheadDegrees, yawRate * _cfg.LookAheadYawScale));
                if (_cfg.InvertLookAhead) targetLook = -targetLook;

                float smoothing = 1f - (float)Math.Pow(1f - Clamp01(_cfg.LookAheadSmoothing), dt * 60f);
                _lookOffset = Lerp(_lookOffset, targetLook, smoothing);
                ApplyRelativeHeadingOffset(_lookOffset);
            }
            catch (Exception ex)
            {
                Log("Camera tick error: " + ex.Message);
                ResetEffects();
            }
        }

        private bool ShouldYield()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            if (_cfg.DisableWhenAiming)
            {
                try { if (Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle)) return true; } catch { }
            }
            return false;
        }

        private void UpdateShake(float amplitude, float speedT)
        {
            if (amplitude <= 0.001f || speedT <= 0.001f)
            {
                if (_shakeActive)
                {
                    try { Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true); } catch { }
                    _shakeActive = false;
                }
                return;
            }

            if (!_shakeActive)
            {
                try
                {
                    Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "ROAD_VIBRATION_SHAKE", amplitude);
                    _shakeActive = true;
                }
                catch { return; }
            }
            try { Function.Call(Hash.SET_GAMEPLAY_CAM_SHAKE_AMPLITUDE, amplitude); } catch { }
        }

        // Preserve the player's own mouse/stick movement: only apply the delta
        // between our previous injected offset and the new desired offset.
        private void ApplyRelativeHeadingOffset(float desired)
        {
            try
            {
                float current = Function.Call<float>(Hash.GET_GAMEPLAY_CAM_RELATIVE_HEADING);
                float delta = desired - _lastAppliedOffset;
                if (Math.Abs(delta) > 0.0001f)
                    Function.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, current + delta);
                _lastAppliedOffset = desired;
            }
            catch { _lastAppliedOffset = 0f; }
        }

        private void ResetEffects()
        {
            if (_shakeActive)
            {
                try { Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true); } catch { }
                _shakeActive = false;
            }
            if (Math.Abs(_lastAppliedOffset) > 0.0001f)
            {
                try
                {
                    float current = Function.Call<float>(Hash.GET_GAMEPLAY_CAM_RELATIVE_HEADING);
                    Function.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, current - _lastAppliedOffset);
                }
                catch { }
            }
            _lastAppliedOffset = 0f;
            _lookOffset = 0f;
            _previousSpeed = 0f;
            _previousHeading = 0f;
            _lastTime = 0;
            _collisionBoostUntil = 0;
        }

        private void OnAborted(object sender, EventArgs e) { ResetEffects(); }
        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        private static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        private static float NormalizeAngle(float a) { while (a > 180f) a -= 360f; while (a < -180f) a += 360f; return a; }
        private static void Log(string text) { try { File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + text + Environment.NewLine); } catch { } }
    }

    internal sealed class CameraConfig
    {
        public bool Enabled = true;
        public bool DisableWhenAiming = true;
        public float MinimumSpeedKph = 20f;
        public float FullEffectSpeedKph = 190f;
        public float MinShakeAmplitude = 0.012f;
        public float MaxShakeAmplitude = 0.085f;
        public float MaximumTotalShake = 0.16f;
        public float AccelerationForFullEffect = 9f;
        public float AccelerationShakeAmplitude = 0.025f;
        public float CollisionDecelerationMps2 = 9f;
        public float CollisionShakeBoost = 0.07f;
        public int CollisionBoostMs = 260;
        public float LookAheadMinimumSpeedKph = 28f;
        public float MaximumLookAheadDegrees = 3.2f;
        public float LookAheadYawScale = 0.010f;
        public float LookAheadSmoothing = 0.10f;
        public bool InvertLookAhead = false;

        public static CameraConfig Load(string path)
        {
            var c = new CameraConfig();
            try
            {
                string section = string.Empty;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                    if (!string.Equals(section, "Camera", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('='); if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    bool bv; int iv; float fv;
                    if (k.Equals("Enabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(v, out bv)) c.Enabled = bv;
                    else if (k.Equals("DisableWhenAiming", StringComparison.OrdinalIgnoreCase) && bool.TryParse(v, out bv)) c.DisableWhenAiming = bv;
                    else if (k.Equals("MinimumSpeedKph", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.MinimumSpeedKph = fv;
                    else if (k.Equals("FullEffectSpeedKph", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.FullEffectSpeedKph = fv;
                    else if (k.Equals("MinShakeAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.MinShakeAmplitude = fv;
                    else if (k.Equals("MaxShakeAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.MaxShakeAmplitude = fv;
                    else if (k.Equals("MaximumTotalShake", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.MaximumTotalShake = fv;
                    else if (k.Equals("AccelerationForFullEffect", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.AccelerationForFullEffect = fv;
                    else if (k.Equals("AccelerationShakeAmplitude", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.AccelerationShakeAmplitude = fv;
                    else if (k.Equals("CollisionDecelerationMps2", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.CollisionDecelerationMps2 = fv;
                    else if (k.Equals("CollisionShakeBoost", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.CollisionShakeBoost = fv;
                    else if (k.Equals("CollisionBoostMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out iv)) c.CollisionBoostMs = iv;
                    else if (k.Equals("LookAheadMinimumSpeedKph", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.LookAheadMinimumSpeedKph = fv;
                    else if (k.Equals("MaximumLookAheadDegrees", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.MaximumLookAheadDegrees = fv;
                    else if (k.Equals("LookAheadYawScale", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.LookAheadYawScale = fv;
                    else if (k.Equals("LookAheadSmoothing", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out fv)) c.LookAheadSmoothing = fv;
                    else if (k.Equals("InvertLookAhead", StringComparison.OrdinalIgnoreCase) && bool.TryParse(v, out bv)) c.InvertLookAhead = bv;
                }
            }
            catch { }
            return c;
        }
        private static bool TryFloat(string v, out float f) { return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f); }
    }
}
