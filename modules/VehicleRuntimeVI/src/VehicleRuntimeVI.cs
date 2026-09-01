using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.VehicleRuntimeVI
{
    public sealed class VehicleRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\VehicleRuntimeVI";
        private const string LogPath = DataDir + "\\VehicleRuntimeVI.log";
        private const string ProfilesPath = DataDir + "\\VehicleProfiles.txt";
        private const string ActiveStatePath = DataDir + "\\ActiveVehicleState.txt";
        private const string LockpickEnterDict = "amb@prop_human_parking_meter@male@enter";
        private const string LockpickBaseDict = "amb@prop_human_parking_meter@male@base";
        private const string LockpickExitDict = "amb@prop_human_parking_meter@male@exit";
        private const int InputEnter = 23;

        private sealed class VehicleProfile
        {
            public string Key = string.Empty;
            public int ModelHash;
            public string Plate = string.Empty;
            public bool HasKey;
            public bool Locked;
            public int LockTier;
            public bool AccessBypassed;
            public bool Hotwired;
            public bool Stolen;
            public bool TrackerPresent;
            public bool TrackerDisabled;
            // Kept only to read old profile rows safely. 0.4 no longer exposes a
            // control-panel wheel for these ordinary vanilla vehicle functions.
            public bool EngineCommandedOff;
            public bool UserLocked;
            public bool InteriorLightOn;
            public bool HeadlightsOn;
            public bool DriverWindowDown;
        }

        private readonly Dictionary<string, VehicleProfile> _profiles = new Dictionary<string, VehicleProfile>(StringComparer.OrdinalIgnoreCase);
        private int _lockVehicle;
        private int _lockStarted;
        private bool _enterControlDown;
        private int _hotwireVehicle;
        private int _hotwireStarted;
        private int _lastSave;
        private int _lastStateWrite;
        private int _storyYieldUntil;

        public VehicleRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            LoadProfiles();
            Interval = 50;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Vehicle Runtime VI 0.4.0 loaded: story-first physical theft, no theft/vehicle menu.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { ResetTransient(); return; }
                if (RockstarOwnsScene())
                {
                    _storyYieldUntil = Game.GameTime + 5000;
                    ResetTransient();
                    return;
                }
                if (Game.GameTime < _storyYieldUntil) { ResetTransient(); return; }

                if (player.IsInVehicle())
                {
                    StopLockAnimation(player);
                    _lockVehicle = 0;
                    _lockStarted = 0;
                    Vehicle current = player.CurrentVehicle;
                    if (current != null && current.Exists() && !IsMissionEntity(current)) UpdateInsideVehicle(player, current);
                }
                else
                {
                    _hotwireVehicle = 0;
                    _hotwireStarted = 0;
                    UpdateEntryInteraction(player);
                }

                int now = Game.GameTime;
                if (now - _lastSave > 12000) { _lastSave = now; SaveProfiles(); }
                if (now - _lastStateWrite > 1000) { _lastStateWrite = now; WriteActiveState(player); }
            }
            catch (Exception ex) { Log("Tick error: " + ex.Message); }
        }

        private void UpdateEntryInteraction(Ped player)
        {
            bool enterPressed = ReadControlPressed(InputEnter);
            bool enterJustPressed = enterPressed && !_enterControlDown;
            _enterControlDown = enterPressed;

            if (_lockVehicle != 0)
            {
                Vehicle active = FindVehicleByHandle(player.Position, 6.0f, _lockVehicle);
                if (active == null || !active.Exists() || IsMissionEntity(active) || Distance(player.Position, active.Position) > 3.8f)
                {
                    ResetLockAction(player);
                    return;
                }

                VehicleProfile profile = GetProfile(active);
                DisableControl(InputEnter);
                MaintainLockAnimation(player, active);
                int duration = 1700 + Math.Max(1, profile.LockTier) * 1050;
                if (Game.GameTime - _lockStarted < duration) return;

                profile.AccessBypassed = true;
                profile.Locked = false;
                profile.Stolen = true;
                try { Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, active.Handle, 1); } catch { }
                if (profile.LockTier >= 2 && StableRoll(profile.Key + ":alarm") < 58)
                {
                    try
                    {
                        Function.Call(Hash.SET_VEHICLE_ALARM, active.Handle, true);
                        Function.Call(Hash.START_VEHICLE_ALARM, active.Handle);
                    }
                    catch { }
                }
                SaveProfiles();
                StopLockAnimation(player);
                _lockVehicle = 0;
                _lockStarted = 0;
                try { Function.Call(Hash.TASK_ENTER_VEHICLE, player.Handle, active.Handle, 7000, -1, 1.0f, 1, 0); } catch { }
                Log("Physical lock bypass completed key=" + profile.Key + " tier=" + profile.LockTier + ".");
                return;
            }

            if (!enterJustPressed) return;
            Vehicle target = IntendedEntryVehicle(player);
            if (target == null || !target.Exists() || IsMissionEntity(target)) return;
            VehicleProfile p = GetProfile(target);
            ApplyDoorState(target, p);
            if (!p.Locked || p.HasKey || p.AccessBypassed) return; // vanilla owns normal entry

            DisableControl(InputEnter);
            try { Function.Call(Hash.CLEAR_PED_TASKS, player.Handle); } catch { }
            _lockVehicle = target.Handle;
            _lockStarted = Game.GameTime;
            BeginLockAnimation(player, target);
            Log("Locked entry attempt became one physical lock-bypass action vehicle=" + target.Handle + ".");
        }

        private void UpdateInsideVehicle(Ped player, Vehicle vehicle)
        {
            VehicleProfile profile = GetProfile(vehicle);
            bool personal = IsLikelyPersonalVehicle(vehicle);
            if (profile.HasKey || personal)
            {
                NormalizePersonalProfile(profile);
                return;
            }

            profile.Stolen = true;
            profile.Locked = false;
            profile.AccessBypassed = true;

            // If the engine was already running, stealing the running vehicle must
            // not magically switch it off just to force a custom mechanic.
            if (IsEngineRunning(vehicle))
            {
                if (!profile.Hotwired)
                {
                    profile.Hotwired = true;
                    SaveProfiles();
                    Log("Running unkeyed vehicle accepted without artificial hotwire key=" + profile.Key + ".");
                }
                _hotwireVehicle = 0;
                _hotwireStarted = 0;
                return;
            }

            if (profile.Hotwired) return;
            if (_hotwireVehicle != vehicle.Handle)
            {
                _hotwireVehicle = vehicle.Handle;
                _hotwireStarted = Game.GameTime;
                Log("Physical ignition bypass started key=" + profile.Key + ".");
            }

            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, false, true, true); } catch { }
            int duration = 2100 + Math.Max(1, profile.LockTier) * 1000;
            if (Game.GameTime - _hotwireStarted < duration) return;

            profile.Hotwired = true;
            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false); } catch { }
            SaveProfiles();
            Log("Ignition bypass completed key=" + profile.Key + " tracker=" + profile.TrackerPresent + ".");
        }

        private static void BeginLockAnimation(Ped player, Vehicle vehicle)
        {
            RequestLockAnimations();
            try { Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, player.Handle, vehicle.Handle, 350); } catch { }
        }

        private void MaintainLockAnimation(Ped player, Vehicle vehicle)
        {
            RequestLockAnimations();
            int elapsed = Math.Max(0, Game.GameTime - _lockStarted);
            if (elapsed < 300) return;
            if (elapsed < 1050)
            {
                if (!IsPlayingAnimation(player, LockpickEnterDict, "enter"))
                { try { Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, LockpickEnterDict, "enter", 4.0f, -4.0f, 850, 0, 0f, false, false, false); } catch { } }
                return;
            }
            if (!IsPlayingAnimation(player, LockpickBaseDict, "base"))
            { try { Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, LockpickBaseDict, "base", 4.0f, -4.0f, -1, 1, 0f, false, false, false); } catch { } }
        }

        private static void RequestLockAnimations()
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, LockpickEnterDict);
                Function.Call(Hash.REQUEST_ANIM_DICT, LockpickBaseDict);
                Function.Call(Hash.REQUEST_ANIM_DICT, LockpickExitDict);
            }
            catch { }
        }

        private static bool IsPlayingAnimation(Ped player, string dict, string name)
        {
            try { return Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, player.Handle, dict, name, 3); }
            catch { return false; }
        }

        private static void StopLockAnimation(Ped player)
        {
            if (player == null || !player.Exists()) return;
            try
            {
                Function.Call(Hash.STOP_ANIM_TASK, player.Handle, LockpickEnterDict, "enter", 2.0f);
                Function.Call(Hash.STOP_ANIM_TASK, player.Handle, LockpickBaseDict, "base", 2.0f);
                Function.Call(Hash.STOP_ANIM_TASK, player.Handle, LockpickExitDict, "exit", 2.0f);
            }
            catch { }
        }

        private VehicleProfile GetProfile(Vehicle vehicle)
        {
            string plate = Plate(vehicle);
            int model = vehicle.Model.Hash;
            string key = model.ToString(CultureInfo.InvariantCulture) + ":" + plate;
            bool personal = IsLikelyPersonalVehicle(vehicle);
            VehicleProfile p;
            if (_profiles.TryGetValue(key, out p))
            {
                if (personal) NormalizePersonalProfile(p);
                return p;
            }

            int roll = StableRoll(key);
            p = new VehicleProfile
            {
                Key = key,
                ModelHash = model,
                Plate = plate,
                HasKey = personal,
                Locked = !personal && roll < 64,
                LockTier = personal ? 0 : (roll < 42 ? 1 : (roll < 82 ? 2 : 3)),
                TrackerPresent = !personal && StableRoll(key + ":tracker") < (IsPremium(vehicle) ? 72 : 28)
            };
            if (personal) NormalizePersonalProfile(p);
            _profiles[key] = p;
            return p;
        }

        private static void NormalizePersonalProfile(VehicleProfile p)
        {
            if (p == null) return;
            p.HasKey = true;
            p.Locked = false;
            p.AccessBypassed = false;
            p.Hotwired = false;
            p.Stolen = false;
            p.TrackerDisabled = false;
        }

        private static void ApplyDoorState(Vehicle vehicle, VehicleProfile profile)
        {
            bool lockIt = profile.Locked && !profile.AccessBypassed && !profile.HasKey;
            try { Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, lockIt ? 2 : 1); } catch { }
        }

        private static Vehicle IntendedEntryVehicle(Ped player)
        {
            try
            {
                int handle = Function.Call<int>(Hash.GET_VEHICLE_PED_IS_TRYING_TO_ENTER, player.Handle);
                if (handle != 0)
                {
                    Entity e = Entity.FromHandle(handle);
                    return e as Vehicle;
                }
            }
            catch { }

            // Enhanced can expose the intended vehicle one frame late. A fallback is
            // intentionally tiny and camera-biased so a nearby unrelated car is not
            // silently selected.
            Vehicle[] vehicles;
            try { vehicles = World.GetNearbyVehicles(player, 2.6f); } catch { return null; }
            Vector3 cam = GameplayCamera.Direction;
            Vehicle best = null;
            float bestScore = float.MinValue;
            foreach (Vehicle v in vehicles)
            {
                if (v == null || !v.Exists()) continue;
                Vector3 d = v.Position - player.Position;
                float len = Length(d);
                if (len < 0.1f || len > 2.6f) continue;
                float dot = Dot(cam, d) / len;
                if (dot < 0.45f) continue;
                float score = dot * 10f - len;
                if (score > bestScore) { bestScore = score; best = v; }
            }
            return best;
        }

        private static Vehicle FindVehicleByHandle(Vector3 pos, float radius, int handle)
        {
            if (handle == 0) return null;
            Vehicle[] vehicles;
            try { vehicles = World.GetNearbyVehicles(pos, radius); } catch { return null; }
            foreach (Vehicle v in vehicles) if (v != null && v.Exists() && v.Handle == handle) return v;
            return null;
        }

        private static bool IsLikelyPersonalVehicle(Vehicle v)
        {
            string[] models = { "tailgater", "buffalo2", "bodhi2" };
            foreach (string name in models)
            { try { if (v.Model.Hash == Function.Call<int>(Hash.GET_HASH_KEY, name)) return true; } catch { } }
            return false;
        }

        private static bool IsEngineRunning(Vehicle v)
        {
            try { return v != null && v.Exists() && Function.Call<bool>(Hash.GET_IS_VEHICLE_ENGINE_RUNNING, v.Handle); }
            catch { return false; }
        }

        private static bool IsPremium(Vehicle v)
        {
            try
            {
                int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, v.Handle);
                return cls == 3 || cls == 5 || cls == 6 || cls == 7 || cls == 22;
            }
            catch { return false; }
        }

        private static bool IsMissionEntity(Entity e)
        {
            try { return Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, e.Handle); }
            catch { return true; }
        }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try
            {
                if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) ||
                    Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true;
            }
            catch { }
            return false;
        }

        private static void DisableControl(int control)
        { try { Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, control, true); } catch { } }

        private static bool ReadControlPressed(int control)
        {
            try { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, control) || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, control); }
            catch { return false; }
        }

        private void WriteActiveState(Ped player)
        {
            try
            {
                if (!player.IsInVehicle()) { File.WriteAllText(ActiveStatePath, "none"); return; }
                Vehicle v = player.CurrentVehicle;
                if (v == null || !v.Exists() || IsMissionEntity(v)) { File.WriteAllText(ActiveStatePath, "none"); return; }
                VehicleProfile p = GetProfile(v);
                File.WriteAllText(ActiveStatePath,
                    "model=" + p.ModelHash + "\nplate=" + p.Plate + "\nstolen=" + p.Stolen + "\nhotwired=" + p.Hotwired +
                    "\ntrackerPresent=" + p.TrackerPresent + "\ntrackerDisabled=" + p.TrackerDisabled + "\n");
            }
            catch { }
        }

        private void LoadProfiles()
        {
            if (!File.Exists(ProfilesPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(ProfilesPath))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 10) continue;
                    var v = new VehicleProfile
                    {
                        Key = p[0], ModelHash = ParseInt(p[1]), Plate = p[2], HasKey = ParseBool(p[3]), Locked = ParseBool(p[4]),
                        LockTier = ParseInt(p[5]), AccessBypassed = ParseBool(p[6]), Hotwired = ParseBool(p[7]), Stolen = ParseBool(p[8]),
                        TrackerPresent = ParseBool(p[9]), TrackerDisabled = p.Length > 10 && ParseBool(p[10]),
                        EngineCommandedOff = p.Length > 11 && ParseBool(p[11]), UserLocked = p.Length > 12 && ParseBool(p[12]),
                        InteriorLightOn = p.Length > 13 && ParseBool(p[13]), HeadlightsOn = p.Length > 14 && ParseBool(p[14]),
                        DriverWindowDown = p.Length > 15 && ParseBool(p[15])
                    };
                    if (!string.IsNullOrWhiteSpace(v.Key)) _profiles[v.Key] = v;
                }
            }
            catch (Exception ex) { Log("Profile load failed safely: " + ex.Message); }
        }

        private void SaveProfiles()
        {
            try
            {
                var lines = new List<string>();
                foreach (VehicleProfile p in _profiles.Values)
                    lines.Add(string.Join("|", p.Key, p.ModelHash, p.Plate, p.HasKey, p.Locked, p.LockTier, p.AccessBypassed, p.Hotwired, p.Stolen,
                        p.TrackerPresent, p.TrackerDisabled, p.EngineCommandedOff, p.UserLocked, p.InteriorLightOn, p.HeadlightsOn, p.DriverWindowDown));
                File.WriteAllLines(ProfilesPath, lines.ToArray());
            }
            catch (Exception ex) { Log("Profile save failed safely: " + ex.Message); }
        }

        private static string Plate(Vehicle v)
        {
            try { return (Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, v.Handle) ?? string.Empty).Trim().ToUpperInvariant(); }
            catch { return string.Empty; }
        }

        private static int StableRoll(string text)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in text ?? string.Empty) h = h * 31 + c;
                if (h == int.MinValue) h = 0;
                return Math.Abs(h) % 100;
            }
        }

        private static float Dot(Vector3 a, Vector3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        private static float Length(Vector3 v) { return (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z); }
        private static float Distance(Vector3 a, Vector3 b) { return Length(a - b); }

        private void ResetLockAction(Ped player)
        {
            StopLockAnimation(player);
            _lockVehicle = 0;
            _lockStarted = 0;
        }

        private void ResetTransient()
        {
            ResetLockAction(Game.LocalPlayerPed);
            _hotwireVehicle = 0;
            _hotwireStarted = 0;
            _enterControlDown = false;
        }

        private static int ParseInt(string s) { int v; return int.TryParse(s, out v) ? v : 0; }
        private static bool ParseBool(string s) { bool v; return bool.TryParse(s, out v) && v; }
        private void OnAborted(object sender, EventArgs e) { SaveProfiles(); ResetTransient(); }
        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); }
            catch { }
        }
    }
}
