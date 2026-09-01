using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;

namespace VOX.AnimationRuntimeVI
{
    public sealed class AnimationRequestSnapshot
    {
        public string Owner = string.Empty;
        public int PedHandle;
        public string Dict = string.Empty;
        public string Anim = string.Empty;
        public int Priority;
        public int ExpiresAt;
        public int DurationMs;
        public int Flags;
        public bool Align;
        public float TargetX;
        public float TargetY;
        public float TargetZ;
        public float StandOff;
    }

    public static class AnimationRuntimeVIBridge
    {
        private sealed class Request
        {
            public string Owner = string.Empty;
            public int PedHandle;
            public string Dict = string.Empty;
            public string Anim = string.Empty;
            public int Priority;
            public int CreatedAt;
            public int ExpiresAt;
            public int DurationMs;
            public int Flags;
            public float BlendIn;
            public float BlendOut;
            public float PlaybackRate;
            public bool Align;
            public Vector3 Target;
            public float StandOff;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<int, Request> Requests = new Dictionary<int, Request>();

        public static bool RequestAnimation(string owner, int pedHandle, string dict, string anim,
            int priority, int ttlMs, int durationMs, int flags,
            float blendIn, float blendOut, float playbackRate,
            bool align, float targetX, float targetY, float targetZ, float standOff)
        {
            if (string.IsNullOrWhiteSpace(owner) || pedHandle == 0 || string.IsNullOrWhiteSpace(dict) || string.IsNullOrWhiteSpace(anim)) return false;
            int now = Game.GameTime;
            lock (Sync)
            {
                CleanupInternal(now);
                Request current;
                if (Requests.TryGetValue(pedHandle, out current) && current != null &&
                    !string.Equals(current.Owner, owner, StringComparison.OrdinalIgnoreCase) && priority <= current.Priority)
                    return false;

                Requests[pedHandle] = new Request
                {
                    Owner = owner,
                    PedHandle = pedHandle,
                    Dict = dict,
                    Anim = anim,
                    Priority = priority,
                    CreatedAt = now,
                    ExpiresAt = now + Math.Max(100, Math.Min(10000, ttlMs)),
                    DurationMs = durationMs,
                    Flags = flags,
                    BlendIn = blendIn <= 0f ? 4f : blendIn,
                    BlendOut = blendOut >= 0f ? -Math.Max(1f, blendOut) : blendOut,
                    PlaybackRate = playbackRate <= 0f ? 1f : Math.Min(1f, playbackRate),
                    Align = align,
                    Target = new Vector3(targetX, targetY, targetZ),
                    StandOff = Math.Max(0.25f, Math.Min(1.5f, standOff))
                };
                return true;
            }
        }

        public static void Cancel(string owner, int pedHandle)
        {
            if (string.IsNullOrWhiteSpace(owner)) return;
            lock (Sync)
            {
                if (pedHandle != 0)
                {
                    Request current;
                    if (Requests.TryGetValue(pedHandle, out current) && string.Equals(current.Owner, owner, StringComparison.OrdinalIgnoreCase)) Requests.Remove(pedHandle);
                    return;
                }
                var remove = new List<int>();
                foreach (var pair in Requests)
                    if (string.Equals(pair.Value.Owner, owner, StringComparison.OrdinalIgnoreCase)) remove.Add(pair.Key);
                foreach (int h in remove) Requests.Remove(h);
            }
        }

        public static AnimationRequestSnapshot Current(int pedHandle)
        {
            lock (Sync)
            {
                CleanupInternal(Game.GameTime);
                Request r;
                if (!Requests.TryGetValue(pedHandle, out r) || r == null) return null;
                return Snapshot(r);
            }
        }

        internal static AnimationRequestSnapshot[] SnapshotAll()
        {
            lock (Sync)
            {
                CleanupInternal(Game.GameTime);
                var result = new List<AnimationRequestSnapshot>();
                foreach (Request r in Requests.Values) if (r != null) result.Add(Snapshot(r));
                return result.ToArray();
            }
        }

        internal static bool TryGetInternal(int pedHandle, out AnimationRequestSnapshot snap)
        {
            snap = Current(pedHandle);
            return snap != null;
        }

        internal static void Cleanup()
        {
            lock (Sync) CleanupInternal(Game.GameTime);
        }

        internal static void ClearAll()
        {
            lock (Sync) Requests.Clear();
        }

        private static AnimationRequestSnapshot Snapshot(Request r)
        {
            return new AnimationRequestSnapshot
            {
                Owner = r.Owner,
                PedHandle = r.PedHandle,
                Dict = r.Dict,
                Anim = r.Anim,
                Priority = r.Priority,
                ExpiresAt = r.ExpiresAt,
                DurationMs = r.DurationMs,
                Flags = r.Flags,
                Align = r.Align,
                TargetX = r.Target.X,
                TargetY = r.Target.Y,
                TargetZ = r.Target.Z,
                StandOff = r.StandOff
            };
        }

        private static void CleanupInternal(int now)
        {
            var remove = new List<int>();
            foreach (var pair in Requests)
                if (pair.Value == null || now >= pair.Value.ExpiresAt) remove.Add(pair.Key);
            foreach (int h in remove) Requests.Remove(h);
        }
    }

    public sealed class AnimationRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\AnimationRuntimeVI";
        private const string LogPath = DataDir + "\\AnimationRuntimeVI.log";
        private int _storyYieldUntil;
        private string _activeOwner = string.Empty;
        private string _activeDict = string.Empty;
        private string _activeAnim = string.Empty;
        private int _activePed;
        private int _startedAt;

        public AnimationRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Interval = 15;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Animation Runtime VI 0.1.0 loaded: central task ownership, alignment and interruption-safe animation bridge.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (RockstarOwnsScene())
            {
                _storyYieldUntil = Game.GameTime + 5000;
                StopActive();
                AnimationRuntimeVIBridge.ClearAll();
                return;
            }
            if (Game.GameTime < _storyYieldUntil)
            {
                StopActive();
                AnimationRuntimeVIBridge.ClearAll();
                return;
            }

            AnimationRuntimeVIBridge.Cleanup();
            Ped player = Game.LocalPlayerPed;
            if (player == null || !player.Exists() || player.IsDead)
            {
                StopActive();
                return;
            }

            AnimationRequestSnapshot request = AnimationRuntimeVIBridge.Current(player.Handle);
            if (request == null)
            {
                StopActive();
                return;
            }
            if (UnsafeForScriptedAnimation(player))
            {
                StopActive();
                return;
            }

            bool changed = _activePed != request.PedHandle ||
                           !string.Equals(_activeOwner, request.Owner, StringComparison.Ordinal) ||
                           !string.Equals(_activeDict, request.Dict, StringComparison.Ordinal) ||
                           !string.Equals(_activeAnim, request.Anim, StringComparison.Ordinal);
            if (changed)
            {
                StopActive();
                _activePed = request.PedHandle;
                _activeOwner = request.Owner;
                _activeDict = request.Dict;
                _activeAnim = request.Anim;
                _startedAt = Game.GameTime;
                Log("Animation claim owner=" + _activeOwner + " dict=" + _activeDict + " anim=" + _activeAnim + ".");
            }

            if (request.Align)
            {
                Vector3 target = new Vector3(request.TargetX, request.TargetY, request.TargetZ);
                Align(player, target, request.StandOff);
                if (Distance(player.Position, target) > request.StandOff + 0.32f) return;
            }

            try { Function.Call(Hash.REQUEST_ANIM_DICT, request.Dict); } catch { }
            bool loaded = false;
            try { loaded = Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, request.Dict); } catch { }
            if (!loaded) return;

            bool playing = false;
            try { playing = Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, player.Handle, request.Dict, request.Anim, 3); } catch { }
            if (!playing)
            {
                try
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, request.Dict, request.Anim,
                        4.0f, -4.0f, request.DurationMs, request.Flags, 1.0f, false, false, false);
                }
                catch { }
            }
        }

        private static void Align(Ped ped, Vector3 target, float standOff)
        {
            Vector3 delta = ped.Position - target;
            float planar = (float)Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            if (planar < 0.01f) planar = 1f;
            Vector3 stand = new Vector3(target.X + delta.X / planar * standOff, target.Y + delta.Y / planar * standOff, ped.Position.Z);
            float heading = HeadingTo(stand, target);
            try
            {
                if (Distance(ped.Position, stand) > 0.22f)
                    Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, ped.Handle, stand.X, stand.Y, stand.Z, 0.82f, 450, heading, 0.035f);
                else Function.Call(Hash.TASK_TURN_PED_TO_FACE_COORD, ped.Handle, target.X, target.Y, target.Z, 180);
            }
            catch { }
        }

        private static bool UnsafeForScriptedAnimation(Ped p)
        {
            if (p.IsInVehicle()) return true;
            try { if (Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PED_RAGDOLL, p.Handle) || Function.Call<bool>(Hash.IS_PED_FALLING, p.Handle) || Function.Call<bool>(Hash.IS_PED_JUMPING, p.Handle) || Function.Call<bool>(Hash.IS_PED_CLIMBING, p.Handle)) return true; } catch { }
            return false;
        }

        private void StopActive()
        {
            if (_activePed != 0 && !string.IsNullOrEmpty(_activeDict) && !string.IsNullOrEmpty(_activeAnim))
            {
                try
                {
                    Entity e = Entity.FromHandle(_activePed);
                    if (e != null && e.Exists()) Function.Call(Hash.STOP_ANIM_TASK, _activePed, _activeDict, _activeAnim, 2.0f);
                }
                catch { }
            }
            _activePed = 0;
            _activeOwner = string.Empty;
            _activeDict = string.Empty;
            _activeAnim = string.Empty;
            _startedAt = 0;
        }

        private static float HeadingTo(Vector3 from, Vector3 to) { try { return Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D, to.X - from.X, to.Y - from.Y); } catch { return 0f; } }
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

        private void OnAborted(object sender, EventArgs e)
        {
            StopActive();
            AnimationRuntimeVIBridge.ClearAll();
        }

        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); } catch { }
        }
    }
}
