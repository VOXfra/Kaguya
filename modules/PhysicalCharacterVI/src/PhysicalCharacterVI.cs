using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.IO;

namespace VOX.PhysicalCharacterVI
{
    public sealed class PhysicalCharacterVIScript : Script
    {
        private const string DataDir = "scripts\\PhysicalCharacterVI";
        private const string LogPath = DataDir + "\\PhysicalCharacterVI.log";

        private Vector3 _lastVelocity;
        private float _lastHeading;
        private int _lastTick;
        private int _lastStumble;
        private int _storyYieldUntil;
        private bool _ownedMoveRate;
        private int _lastIkMode = -1;

        public PhysicalCharacterVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Interval = 20;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Physical Character VI 0.1.0 loaded: full leg IK, slope footing, turn inertia and collision bracing.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            Ped player = Game.LocalPlayerPed;
            if (player == null || !player.Exists() || player.IsDead)
            {
                ResetOwnership(player);
                _lastTick = 0;
                return;
            }

            if (RockstarOwnsScene())
            {
                _storyYieldUntil = Game.GameTime + 5000;
                ResetOwnership(player);
                Prime(player);
                return;
            }
            if (Game.GameTime < _storyYieldUntil)
            {
                ResetOwnership(player);
                Prime(player);
                return;
            }

            if (player.IsInVehicle() || UnsafeLocomotionState(player))
            {
                ResetMovementRate(player);
                SetLegIk(player, IsMelee(player) ? 3 : 2);
                Prime(player);
                return;
            }

            int now = Game.GameTime;
            float dt = _lastTick > 0 ? Clamp((now - _lastTick) / 1000f, 0.005f, 0.12f) : 0.02f;
            Vector3 velocity = SafeVelocity(player);
            float planarSpeed = PlanarLength(velocity);
            float heading = SafeHeading(player);

            SetLegIk(player, IsMelee(player) ? 3 : 2);
            ApplySlopeFooting(player, planarSpeed);

            if (_lastTick > 0)
            {
                ApplyTurnInertia(player, velocity, planarSpeed, heading, dt);
                ApplyImpactBracing(player, velocity, planarSpeed, dt);
            }

            _lastVelocity = velocity;
            _lastHeading = heading;
            _lastTick = now;
        }

        private void ApplySlopeFooting(Ped player, float speed)
        {
            Vector3 normal;
            if (!TryGroundNormal(player.Position, out normal))
            {
                ResetMovementRate(player);
                return;
            }

            float nz = Clamp(normal.Z, -1f, 1f);
            float steepness = 1f - Math.Max(0f, nz);
            if (steepness < 0.16f || speed < 1.2f)
            {
                ResetMovementRate(player);
                return;
            }

            float overrideRate = 1f - Clamp((steepness - 0.16f) * 0.42f, 0f, 0.10f);
            try
            {
                Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, player.Handle, overrideRate);
                _ownedMoveRate = true;
            }
            catch { }
        }

        private void ApplyTurnInertia(Ped player, Vector3 velocity, float speed, float heading, float dt)
        {
            if (speed < 3.4f || (!SafeBool(Hash.IS_PED_RUNNING, player.Handle) && !SafeBool(Hash.IS_PED_SPRINTING, player.Handle))) return;
            if (IsMelee(player) || IsAiming()) return;

            float headingDelta = AbsAngleDelta(heading, _lastHeading);
            float turnRate = headingDelta / Math.Max(0.005f, dt);
            if (turnRate < 95f) return;

            float previousSpeed = PlanarLength(_lastVelocity);
            if (previousSpeed < 3.0f) return;
            Vector3 previousDir = new Vector3(_lastVelocity.X / previousSpeed, _lastVelocity.Y / previousSpeed, 0f);
            Vector3 currentDir = speed > 0.01f ? new Vector3(velocity.X / speed, velocity.Y / speed, 0f) : previousDir;
            float dot = Clamp(previousDir.X * currentDir.X + previousDir.Y * currentDir.Y, -1f, 1f);
            float directionChange = 1f - dot;
            if (directionChange < 0.16f) return;

            // Tiny continuation force: enough to stop 180-degree input changes from
            // looking frictionless, far below the force needed to slide the player.
            float force = Clamp(directionChange * speed * 0.020f, 0.025f, 0.16f);
            try
            {
                Function.Call(Hash.APPLY_FORCE_TO_ENTITY, player.Handle, 1,
                    previousDir.X * force, previousDir.Y * force, 0f,
                    0f, 0f, 0f, 0, false, true, true, false, true);
            }
            catch { }
        }

        private void ApplyImpactBracing(Ped player, Vector3 velocity, float speed, float dt)
        {
            float oldSpeed = PlanarLength(_lastVelocity);
            float decel = (oldSpeed - speed) / Math.Max(0.005f, dt);
            if (oldSpeed < 5.8f || decel < 28f) return;

            bool collided = false;
            try { collided = Function.Call<bool>(Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, player.Handle); } catch { }
            if (!collided) return;

            int now = Game.GameTime;
            if (now - _lastStumble < 1500) return;
            _lastStumble = now;

            if (oldSpeed >= 8.8f && !IsMelee(player) && !IsAiming())
            {
                int ragdollMs = oldSpeed >= 11.5f ? 800 : 430;
                try { Function.Call(Hash.SET_PED_TO_RAGDOLL, player.Handle, ragdollMs, ragdollMs + 250, 0, false, false, false); } catch { }
                Log("Impact stumble speed=" + oldSpeed.ToString("0.0") + " decel=" + decel.ToString("0") + ".");
            }
            else
            {
                // Lower impacts keep feet planted but preserve some momentum instead
                // of instantly snapping the capsule to a stop.
                float len = Math.Max(0.01f, oldSpeed);
                float force = Clamp(oldSpeed * 0.010f, 0.035f, 0.10f);
                try
                {
                    Function.Call(Hash.APPLY_FORCE_TO_ENTITY, player.Handle, 1,
                        _lastVelocity.X / len * force, _lastVelocity.Y / len * force, 0.025f,
                        0f, 0f, 0f, 0, false, true, true, false, true);
                }
                catch { }
            }
        }

        private static bool TryGroundNormal(Vector3 p, out Vector3 normal)
        {
            normal = new Vector3(0f, 0f, 1f);
            try
            {
                int test = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                    p.X, p.Y, p.Z + 0.45f,
                    p.X, p.Y, p.Z - 1.55f,
                    1, 0, 7);
                var hit = new OutputArgument();
                var end = new OutputArgument();
                var n = new OutputArgument();
                var entity = new OutputArgument();
                Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT, test, hit, end, n, entity);
                if (!hit.GetResult<bool>()) return false;
                normal = n.GetResult<Vector3>();
                return true;
            }
            catch { return false; }
        }

        private void SetLegIk(Ped player, int mode)
        {
            if (_lastIkMode == mode) return;
            try
            {
                Function.Call(Hash.SET_PED_LEG_IK_MODE, player.Handle, mode);
                _lastIkMode = mode;
            }
            catch { }
        }

        private void ResetMovementRate(Ped player)
        {
            if (!_ownedMoveRate || player == null || !player.Exists()) return;
            try { Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, player.Handle, 1f); } catch { }
            _ownedMoveRate = false;
        }

        private void ResetOwnership(Ped player)
        {
            ResetMovementRate(player);
            if (player != null && player.Exists())
            {
                try { Function.Call(Hash.SET_PED_LEG_IK_MODE, player.Handle, 2); } catch { }
            }
            _lastIkMode = -1;
        }

        private void Prime(Ped player)
        {
            if (player == null || !player.Exists()) { _lastTick = 0; return; }
            _lastVelocity = SafeVelocity(player);
            _lastHeading = SafeHeading(player);
            _lastTick = Game.GameTime;
        }

        private static bool UnsafeLocomotionState(Ped player)
        {
            return SafeBool(Hash.IS_PED_RAGDOLL, player.Handle) ||
                   SafeBool(Hash.IS_PED_FALLING, player.Handle) ||
                   SafeBool(Hash.IS_PED_JUMPING, player.Handle) ||
                   SafeBool(Hash.IS_PED_CLIMBING, player.Handle) ||
                   SafeBool(Hash.IS_PED_SWIMMING, player.Handle) ||
                   SafeBool(Hash.IS_PED_SWIMMING_UNDER_WATER, player.Handle);
        }

        private static bool IsMelee(Ped player)
        {
            try { return Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, player.Handle); } catch { return false; }
        }

        private static bool IsAiming()
        {
            try { return Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle); } catch { return false; }
        }

        private static Vector3 SafeVelocity(Entity e)
        {
            try { return Function.Call<Vector3>(Hash.GET_ENTITY_VELOCITY, e.Handle); } catch { return Vector3.Zero; }
        }

        private static float SafeHeading(Entity e)
        {
            try { return Function.Call<float>(Hash.GET_ENTITY_HEADING, e.Handle); } catch { return 0f; }
        }

        private static float PlanarLength(Vector3 v) { return (float)Math.Sqrt(v.X * v.X + v.Y * v.Y); }
        private static float AbsAngleDelta(float a, float b)
        {
            float d = (a - b) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return Math.Abs(d);
        }
        private static float Clamp(float v, float min, float max) { return v < min ? min : (v > max ? max : v); }
        private static bool SafeBool(Hash h, params InputArgument[] args) { try { return Function.Call<bool>(h, args); } catch { return false; } }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true; } catch { }
            return false;
        }

        private void OnAborted(object sender, EventArgs e)
        {
            ResetOwnership(Game.LocalPlayerPed);
        }

        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); } catch { }
        }
    }
}
