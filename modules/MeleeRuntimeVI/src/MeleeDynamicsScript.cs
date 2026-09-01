using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.IO;

namespace VOX.MeleeRuntimeVI
{
    public sealed class MeleeDynamicsScript : Script
    {
        private const int InputAttack = 24;      // LMB / RT
        private const int InputMeleeLight = 140; // R / B
        private const int InputMeleeHeavy = 141; // Q / A
        private const int InputBlock = 143;      // SPACE / X
        private static readonly Hash MeleeDefenseNative = (Hash)0xAE540335B4ABC4E2UL;
        private const string DataDir = "scripts\\MeleeRuntimeVI";
        private const string LogPath = DataDir + "\\MeleeRuntimeVI.log";

        private float _stamina = 100f;
        private int _lastTick;
        private int _lastHealth;
        private int _counterUntil;
        private int _lastCounter;
        private int _storyYieldUntil;
        private bool _attackWasDown;
        private bool _modifierOwned;

        public MeleeDynamicsScript()
        {
            Interval = 20;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Melee Runtime VI 0.2.0 dynamics loaded: stamina-driven defense and native-input counter windows.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            Ped player = Game.LocalPlayerPed;
            if (player == null || !player.Exists() || player.IsDead)
            {
                ReleaseModifier();
                _lastTick = 0;
                return;
            }

            if (RockstarOwnsScene())
            {
                _storyYieldUntil = Game.GameTime + 5000;
                ReleaseModifier();
                Prime(player);
                return;
            }
            if (Game.GameTime < _storyYieldUntil)
            {
                ReleaseModifier();
                Prime(player);
                return;
            }

            int now = Game.GameTime;
            float dt = _lastTick > 0 ? Clamp((now - _lastTick) / 1000f, 0.005f, 0.10f) : 0.02f;
            _lastTick = now;

            bool inMelee = SafeBool(Hash.IS_PED_IN_MELEE_COMBAT, player.Handle);
            bool block = Pressed(InputBlock);
            bool attack = Pressed(InputAttack) || Pressed(InputMeleeLight) || Pressed(InputMeleeHeavy);
            bool attackJust = attack && !_attackWasDown;
            _attackWasDown = attack;

            if (!inMelee)
            {
                _stamina = Math.Min(100f, _stamina + 24f * dt);
                _counterUntil = 0;
                ReleaseModifier();
                _lastHealth = SafeHealth(player);
                return;
            }

            if (block) _stamina = Math.Max(0f, _stamina - 9.0f * dt);
            else if (attack) _stamina = Math.Max(0f, _stamina - 5.5f * dt);
            else _stamina = Math.Min(100f, _stamina + 11f * dt);

            float defense = block ? Lerp(0.92f, 0.48f, _stamina / 100f) : Lerp(0.96f, 0.82f, _stamina / 100f);
            try
            {
                Function.Call(MeleeDefenseNative, Game.Player.Handle, defense);
                _modifierOwned = true;
            }
            catch { }

            int health = SafeHealth(player);
            if (_lastHealth <= 0) _lastHealth = health;
            if (block && health < _lastHealth && _stamina >= 12f)
            {
                _counterUntil = now + 560;
                _stamina = Math.Max(0f, _stamina - 11f);
            }
            _lastHealth = health;

            if (attackJust && now <= _counterUntil && now - _lastCounter > 900)
            {
                Ped opponent = NearestMeleeOpponent(player, 2.4f);
                if (opponent != null)
                {
                    _lastCounter = now;
                    _counterUntil = 0;
                    _stamina = Math.Max(0f, _stamina - 18f);
                    ApplyCounterStagger(player, opponent);
                    Log("Context counter stagger ped=" + opponent.Handle + " stamina=" + _stamina.ToString("0") + ".");
                }
            }
        }

        private static Ped NearestMeleeOpponent(Ped player, float radius)
        {
            Ped[] nearby;
            try { nearby = World.GetNearbyPeds(player, radius); } catch { return null; }
            Ped best = null;
            float bestDistance = float.MaxValue;
            foreach (Ped ped in nearby)
            {
                if (ped == null || !ped.Exists() || ped.IsDead || !ped.IsHuman || ped.Handle == player.Handle) continue;
                try { if (Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, ped.Handle)) continue; } catch { continue; }
                bool combat = false;
                try { combat = Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, ped.Handle) || Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped.Handle, player.Handle); } catch { }
                if (!combat) continue;
                float d = Distance(player.Position, ped.Position);
                if (d < bestDistance) { bestDistance = d; best = ped; }
            }
            return best;
        }

        private static void ApplyCounterStagger(Ped player, Ped opponent)
        {
            Vector3 d = opponent.Position - player.Position;
            float len = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len < 0.01f) len = 1f;
            try
            {
                Function.Call(Hash.APPLY_FORCE_TO_ENTITY, opponent.Handle, 1,
                    d.X / len * 0.52f, d.Y / len * 0.52f, 0.12f,
                    0f, 0f, 0f, 0, false, true, true, false, true);
                Function.Call(Hash.SET_PED_TO_RAGDOLL, opponent.Handle, 260, 430, 0, false, false, false);
            }
            catch { }
        }

        private void Prime(Ped p)
        {
            _lastTick = Game.GameTime;
            _lastHealth = p == null || !p.Exists() ? 0 : SafeHealth(p);
            _counterUntil = 0;
            _attackWasDown = false;
        }

        private void ReleaseModifier()
        {
            if (!_modifierOwned) return;
            try { Function.Call(MeleeDefenseNative, Game.Player.Handle, 1f); } catch { }
            _modifierOwned = false;
        }

        private static int SafeHealth(Entity e) { try { return Function.Call<int>(Hash.GET_ENTITY_HEALTH, e.Handle); } catch { return 100; } }
        private static bool SafeBool(Hash h, params InputArgument[] args) { try { return Function.Call<bool>(h, args); } catch { return false; } }
        private static bool Pressed(int c) { try { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, c) || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, c); } catch { return false; } }
        private static float Lerp(float a, float b, float t) { t = Clamp(t, 0f, 1f); return a + (b - a) * t; }
        private static float Clamp(float v, float min, float max) { return v < min ? min : (v > max ? max : v); }
        private static float Distance(Vector3 a, Vector3 b) { double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z; return (float)Math.Sqrt(x * x + y * y + z * z); }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true; } catch { }
            return false;
        }

        private void OnAborted(object sender, EventArgs e) { ReleaseModifier(); }

        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); } catch { }
        }
    }
}
