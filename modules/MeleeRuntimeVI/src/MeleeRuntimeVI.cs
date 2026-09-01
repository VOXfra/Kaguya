using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.MeleeRuntimeVI
{
    public sealed class MeleeRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\MeleeRuntimeVI";
        private const string LogPath = DataDir + "\\MeleeRuntimeVI.log";
        private const string FitnessPath = "scripts\\CharacterRuntimeVI\\FitnessStats.txt";
        private const int InputMeleeLight = 140; // R / B
        private const int InputMeleeHeavy = 141; // Q / A
        private const int InputMeleeAlternate = 142; // LMB / RT
        private const int InputMeleeBlock = 143; // SPACE / X

        private sealed class OpponentState
        {
            public int Handle;
            public int ModelHash;
            public int OriginalMaxHealth;
            public int LastSeen;
            public int LastImpact;
            public int LastHealth;
            public bool CriticalHitsDisabled;
        }

        private readonly Dictionary<int, OpponentState> _opponents = new Dictionary<int, OpponentState>();
        private int _lastScan;
        private int _lastFitnessRead;
        private int _storyYieldUntil;
        private float _strength = 45f;
        private bool _meleeModifierOwned;

        public MeleeRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Interval = 25;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Melee Runtime VI 0.1.0 loaded: longer free-roam fights, native GTA controls, contextual heavy impacts.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead)
                {
                    ReleasePlayerModifier();
                    RestoreAll();
                    return;
                }

                if (RockstarOwnsScene())
                {
                    _storyYieldUntil = Game.GameTime + 5000;
                    ReleasePlayerModifier();
                    RestoreAll();
                    return;
                }
                if (Game.GameTime < _storyYieldUntil)
                {
                    ReleasePlayerModifier();
                    RestoreAll();
                    return;
                }

                if (Game.GameTime - _lastFitnessRead >= 5000)
                {
                    _lastFitnessRead = Game.GameTime;
                    _strength = ReadStrength(player.Model.Hash);
                }

                bool unarmed = IsUnarmed(player);
                bool inMelee = unarmed && IsMeleeCombat(player);
                if (inMelee)
                {
                    // GTA V's default fist damage makes ordinary fights end in only a
                    // handful of hits. Fitness can matter, but never restores vanilla's
                    // very high pacing: 0.68..0.80 keeps punches meaningful without
                    // turning opponents into health sponges.
                    float damage = 0.68f + Clamp(_strength / 100f, 0f, 1f) * 0.12f;
                    try { Function.Call(Hash.SET_PLAYER_MELEE_WEAPON_DAMAGE_MODIFIER, Game.Player.Handle, damage, true); _meleeModifierOwned = true; } catch { }
                    ScanOpponents(player);
                    UpdateOpponents(player);
                }
                else
                {
                    ReleasePlayerModifier();
                    CleanupExpired();
                }
            }
            catch (Exception ex)
            {
                Log("Melee tick error: " + ex.Message);
                ReleasePlayerModifier();
            }
        }

        private void ScanOpponents(Ped player)
        {
            if (Game.GameTime - _lastScan < 300) return;
            _lastScan = Game.GameTime;
            Ped[] peds;
            try { peds = World.GetNearbyPeds(player, 8f); } catch { return; }
            foreach (Ped p in peds)
            {
                if (!EligibleOpponent(p, player)) continue;
                bool fightingPlayer = false;
                try { fightingPlayer = Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, p.Handle) || Function.Call<bool>(Hash.IS_PED_IN_COMBAT, p.Handle, player.Handle); } catch { }
                if (!fightingPlayer && Distance(p.Position, player.Position) > 3.2f) continue;
                EnsureOpponent(p);
            }
        }

        private void EnsureOpponent(Ped ped)
        {
            OpponentState s;
            if (_opponents.TryGetValue(ped.Handle, out s) && s.ModelHash == ped.Model.Hash)
            {
                s.LastSeen = Game.GameTime;
                return;
            }

            int maxHealth = SafeEntityMaxHealth(ped);
            int health = SafeEntityHealth(ped);
            int roll = StableRoll(ped.Handle, ped.Model.Hash);
            int durableMax = 165 + roll % 56; // 165..220, enough for a real exchange rather than 2-3 punches.
            durableMax = Math.Max(maxHealth, durableMax);

            s = new OpponentState
            {
                Handle = ped.Handle,
                ModelHash = ped.Model.Hash,
                OriginalMaxHealth = maxHealth,
                LastSeen = Game.GameTime,
                LastHealth = health,
                CriticalHitsDisabled = false
            };
            _opponents[ped.Handle] = s;

            try
            {
                if (durableMax > maxHealth)
                {
                    float ratio = maxHealth > 0 ? Clamp(health / (float)maxHealth, 0.05f, 1f) : 1f;
                    Function.Call(Hash.SET_ENTITY_MAX_HEALTH, ped.Handle, durableMax);
                    Function.Call(Hash.SET_ENTITY_HEALTH, ped.Handle, Math.Max(1, (int)(durableMax * ratio)), 0);
                }
                Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped.Handle, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, true);
                s.CriticalHitsDisabled = true;
                s.LastHealth = SafeEntityHealth(ped);
            }
            catch { }
            Log("Melee opponent engaged ped=" + ped.Handle + " baseMax=" + maxHealth + " fightMax=" + durableMax + ".");
        }

        private void UpdateOpponents(Ped player)
        {
            bool heavyIntent = Pressed(InputMeleeHeavy) || Pressed(InputMeleeAlternate);
            bool block = Pressed(InputMeleeBlock);
            var remove = new List<int>();
            foreach (var pair in _opponents)
            {
                int handle = pair.Key;
                OpponentState s = pair.Value;
                Ped ped = null;
                try { ped = Entity.FromHandle(handle) as Ped; } catch { }
                if (ped == null || !ped.Exists() || ped.IsDead)
                {
                    remove.Add(handle);
                    continue;
                }
                if (Distance(ped.Position, player.Position) > 12f)
                {
                    if (Game.GameTime - s.LastSeen > 2500) { RestoreOpponent(ped, s); remove.Add(handle); }
                    continue;
                }
                s.LastSeen = Game.GameTime;

                int health = SafeEntityHealth(ped);
                bool damagedByPlayer = false;
                try { damagedByPlayer = Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, ped.Handle, player.Handle, true); } catch { }
                if (damagedByPlayer && health < s.LastHealth)
                {
                    if (heavyIntent && Game.GameTime - s.LastImpact > 650)
                    {
                        s.LastImpact = Game.GameTime;
                        ApplyContextualImpact(player, ped, health);
                    }
                    try { Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, ped.Handle); } catch { }
                }
                s.LastHealth = health;

                // Blocking remains Rockstar's native SPACE/X behavior. We only keep
                // opponents in a more measured combat range instead of injecting a
                // custom parry minigame or extra key.
                if (block)
                {
                    try { Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 1); } catch { }
                }
                else
                {
                    try { Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 2); } catch { }
                }
            }
            foreach (int h in remove) _opponents.Remove(h);
        }

        private static void ApplyContextualImpact(Ped player, Ped ped, int remainingHealth)
        {
            Vector3 delta = ped.Position - player.Position;
            float len = (float)Math.Sqrt(delta.X*delta.X + delta.Y*delta.Y);
            if (len < 0.01f) len = 1f;
            float force = remainingHealth < 70 ? 1.75f : 1.15f;
            bool collided = false;
            try { collided = Function.Call<bool>(Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, ped.Handle); } catch { }
            try
            {
                Function.Call(Hash.APPLY_FORCE_TO_ENTITY, ped.Handle, 1,
                    delta.X / len * force, delta.Y / len * force, collided ? 0.38f : 0.16f,
                    0f, 0f, 0f, 0, false, true, true, false, true);
                if (collided || remainingHealth < 55)
                    Function.Call(Hash.SET_PED_TO_RAGDOLL, ped.Handle, 420, 700, 0, false, false, false);
            }
            catch { }
        }

        private void CleanupExpired()
        {
            var remove = new List<int>();
            foreach (var pair in _opponents)
            {
                OpponentState s = pair.Value;
                if (Game.GameTime - s.LastSeen < 5000) continue;
                Ped ped = null;
                try { ped = Entity.FromHandle(pair.Key) as Ped; } catch { }
                if (ped != null && ped.Exists() && !ped.IsDead) RestoreOpponent(ped, s);
                remove.Add(pair.Key);
            }
            foreach (int h in remove) _opponents.Remove(h);
        }

        private void RestoreAll()
        {
            foreach (var pair in _opponents)
            {
                try
                {
                    Ped ped = Entity.FromHandle(pair.Key) as Ped;
                    if (ped != null && ped.Exists() && !ped.IsDead) RestoreOpponent(ped, pair.Value);
                }
                catch { }
            }
            _opponents.Clear();
        }

        private static void RestoreOpponent(Ped ped, OpponentState s)
        {
            try
            {
                // Do not heal or shrink a living ped mid-scene. Only release critical
                // hit ownership; max-health normalization is left to streaming/despawn.
                if (s.CriticalHitsDisabled) Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped.Handle, true);
                Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 2);
            }
            catch { }
        }

        private void ReleasePlayerModifier()
        {
            if (!_meleeModifierOwned) return;
            try { Function.Call(Hash.SET_PLAYER_MELEE_WEAPON_DAMAGE_MODIFIER, Game.Player.Handle, 1f, true); } catch { }
            _meleeModifierOwned = false;
        }

        private static bool EligibleOpponent(Ped p, Ped player)
        {
            if (p == null || !p.Exists() || p.IsDead || !p.IsHuman || p.Handle == player.Handle) return false;
            try { if (p.IsInVehicle()) return false; } catch { }
            try { if (Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, p.Handle)) return false; } catch { return false; }
            try
            {
                int t = (int)p.PedType;
                if (t == 6 || t == 27 || t == 29) return false;
            }
            catch { }
            return true;
        }

        private static bool IsUnarmed(Ped p)
        {
            try { return Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON, p.Handle) == Function.Call<int>(Hash.GET_HASH_KEY, "WEAPON_UNARMED"); }
            catch { return false; }
        }

        private static bool IsMeleeCombat(Ped p)
        {
            try { return Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, p.Handle); }
            catch { return false; }
        }

        private static int SafeEntityHealth(Entity e) { try { return Function.Call<int>(Hash.GET_ENTITY_HEALTH, e.Handle); } catch { return 100; } }
        private static int SafeEntityMaxHealth(Entity e) { try { return Function.Call<int>(Hash.GET_ENTITY_MAX_HEALTH, e.Handle); } catch { return 100; } }
        private static bool Pressed(int c) { try { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, c) || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, c); } catch { return false; } }
        private static float Distance(Vector3 a, Vector3 b) { double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z; return (float)Math.Sqrt(x*x+y*y+z*z); }
        private static float Clamp(float v,float min,float max){return v<min?min:(v>max?max:v);}
        private static int StableRoll(int a,int b){unchecked{int x=a*397^b; x^=x>>16; if(x==int.MinValue)x=0; return Math.Abs(x)%100;}}

        private float ReadStrength(int modelHash)
        {
            try
            {
                if (!File.Exists(FitnessPath)) return 45f;
                foreach (string line in File.ReadAllLines(FitnessPath))
                {
                    string[] p=line.Split('|'); if(p.Length<2)continue;
                    int m; float s;
                    if(int.TryParse(p[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out m) && m==modelHash &&
                       float.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out s)) return Clamp(s,0f,100f);
                }
            }
            catch { }
            return 45f;
        }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true; } catch { }
            return false;
        }

        private void OnAborted(object sender, EventArgs e) { ReleasePlayerModifier(); RestoreAll(); }
        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+text+Environment.NewLine); } catch { }
        }
    }
}
