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
        private const string QuietDict = "veh@break_in@0h@p_m_one@";
        private const string QuietAnim = "low_force_entry_ds";
        private const string ForceDict = "veh@break_in@0h@p_m_zero@";
        private const string ForceAnim = "std_force_entry_ds";
        private const int InputEnter = 23;  // F / Y
        private const int InputAttack = 24; // LMB / RT

        private enum BreakInMode { Undecided, Quiet, Smash }

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
        }

        private readonly Dictionary<string, VehicleProfile> _profiles = new Dictionary<string, VehicleProfile>(StringComparer.OrdinalIgnoreCase);
        private int _breakVehicle;
        private int _breakStarted;
        private int _breakAnimStarted;
        private BreakInMode _breakMode;
        private bool _enterWasDown;
        private bool _attackWasDown;
        private int _hotwireVehicle;
        private int _hotwireStarted;
        private int _lastSave;
        private int _lastStateWrite;
        private int _lastHelp;
        private int _storyYieldUntil;

        public VehicleRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            LoadProfiles();
            Interval = 25;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Vehicle Runtime VI 0.5.0 loaded: Rockstar break-in animation, aligned driver-door approach, quiet/smash choice and stricter ambient locks.");
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
                    StopBreakAnimation(player);
                    _breakVehicle = 0;
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
            catch (Exception ex) { Log("Tick error: " + ex); }
        }

        private void UpdateEntryInteraction(Ped player)
        {
            bool enter = Pressed(InputEnter);
            bool attack = Pressed(InputAttack);
            bool enterJust = enter && !_enterWasDown;
            bool attackJust = attack && !_attackWasDown;
            _enterWasDown = enter;
            _attackWasDown = attack;

            if (_breakVehicle != 0)
            {
                Vehicle active = FindVehicleByHandle(player.Position, 8f, _breakVehicle);
                if (active == null || !active.Exists() || IsMissionEntity(active) || Distance(player.Position, active.Position) > 5.0f)
                {
                    ResetBreakAction(player);
                    return;
                }

                DisableControl(InputEnter);
                VehicleProfile profile = GetProfile(active);
                int elapsed = Game.GameTime - _breakStarted;

                if (_breakMode == BreakInMode.Undecided)
                {
                    ApproachDriverDoor(player, active);
                    ShowHelp("Ouverture discrete...  ~INPUT_ATTACK~ pour briser la vitre");
                    if (attackJust) _breakMode = BreakInMode.Smash;
                    else if (elapsed >= 900) _breakMode = BreakInMode.Quiet;
                    if (_breakMode != BreakInMode.Undecided) _breakAnimStarted = Game.GameTime;
                    else return;
                }

                if (_breakMode == BreakInMode.Smash)
                {
                    PlayBreakAnimation(player, active, ForceDict, ForceAnim);
                    if (Game.GameTime - _breakAnimStarted < 850) return;
                    try
                    {
                        Function.Call(Hash.SMASH_VEHICLE_WINDOW, active.Handle, 0);
                        Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, active.Handle, 1);
                    }
                    catch { }
                    profile.AccessBypassed = true;
                    profile.Locked = false;
                    profile.Stolen = true;
                    TriggerAlarm(active, profile, 82);
                    CompleteEntry(player, active, profile, "forced-window");
                    return;
                }

                PlayBreakAnimation(player, active, QuietDict, QuietAnim);
                int duration = 1700 + Math.Max(1, profile.LockTier) * 850;
                if (Game.GameTime - _breakAnimStarted < duration) return;
                profile.AccessBypassed = true;
                profile.Locked = false;
                profile.Stolen = true;
                try { Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, active.Handle, 1); } catch { }
                TriggerAlarm(active, profile, profile.LockTier >= 3 ? 58 : (profile.LockTier == 2 ? 35 : 18));
                CompleteEntry(player, active, profile, "quiet-lockwork");
                return;
            }

            if (!enterJust) return;
            Vehicle target = IntendedEntryVehicle(player);
            if (target == null || !target.Exists() || IsMissionEntity(target)) return;
            VehicleProfile p = GetProfile(target);
            ApplyDoorState(target, p);
            if (!p.Locked || p.HasKey || p.AccessBypassed) return;

            // Only the actual vanilla Enter attempt is intercepted. Merely standing
            // near a car never creates a theft affordance.
            DisableControl(InputEnter);
            try { Function.Call(Hash.CLEAR_PED_TASKS, player.Handle); } catch { }
            _breakVehicle = target.Handle;
            _breakStarted = Game.GameTime;
            _breakAnimStarted = 0;
            _breakMode = BreakInMode.Undecided;
            RequestBreakAnimations();
            ApproachDriverDoor(player, target);
            Log("Locked entry intercepted vehicle=" + target.Handle + " tier=" + p.LockTier + ". Attack during alignment selects smash; otherwise Rockstar low-force lockwork is used.");
        }

        private void CompleteEntry(Ped player, Vehicle active, VehicleProfile profile, string method)
        {
            SaveProfiles();
            StopBreakAnimation(player);
            _breakVehicle = 0;
            _breakStarted = 0;
            _breakAnimStarted = 0;
            _breakMode = BreakInMode.Undecided;
            try { Function.Call(Hash.TASK_ENTER_VEHICLE, player.Handle, active.Handle, 7000, -1, 1.0f, 1, 0); } catch { }
            Log("Vehicle access completed method=" + method + " key=" + profile.Key + ".");
        }

        private static void TriggerAlarm(Vehicle v, VehicleProfile p, int chance)
        {
            if (StableRoll(p.Key + ":alarm:" + Game.GameTime / 10000) >= chance) return;
            try
            {
                Function.Call(Hash.SET_VEHICLE_ALARM, v.Handle, true);
                Function.Call(Hash.START_VEHICLE_ALARM, v.Handle);
            }
            catch { }
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
            if (IsEngineRunning(vehicle))
            {
                if (!profile.Hotwired)
                {
                    profile.Hotwired = true;
                    SaveProfiles();
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
                Log("Ignition bypass started key=" + profile.Key + ".");
            }

            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, false, true, true); } catch { }
            int duration = 1800 + Math.Max(1, profile.LockTier) * 800;
            if (Game.GameTime - _hotwireStarted < duration) return;
            profile.Hotwired = true;
            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false); } catch { }
            SaveProfiles();
            Log("Ignition bypass completed key=" + profile.Key + " tracker=" + profile.TrackerPresent + ".");
        }

        private void ApproachDriverDoor(Ped player, Vehicle vehicle)
        {
            Vector3 door = DriverDoorPosition(vehicle);
            Vector3 outward = OutwardFromVehicle(vehicle, door);
            Vector3 stand = door + outward * 0.72f;
            float heading = HeadingTo(stand, door);
            if (Distance(player.Position, stand) <= 0.38f)
            {
                try { Function.Call(Hash.TASK_TURN_PED_TO_FACE_COORD, player.Handle, door.X, door.Y, door.Z, 250); } catch { }
                return;
            }
            try { Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, player.Handle, stand.X, stand.Y, stand.Z, 1.0f, 600, heading, 0.08f); } catch { }
        }

        private static Vector3 DriverDoorPosition(Vehicle v)
        {
            try
            {
                int bone = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, v.Handle, "door_dside_f");
                if (bone >= 0) return Function.Call<Vector3>(Hash.GET_WORLD_POSITION_OF_ENTITY_BONE, v.Handle, bone);
            }
            catch { }
            try { return Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS, v.Handle, -0.85f, 0.35f, 0.35f); }
            catch { return v.Position; }
        }

        private static Vector3 OutwardFromVehicle(Vehicle v, Vector3 door)
        {
            try
            {
                Vector3 center = v.Position;
                Vector3 d = door - center;
                float len = (float)Math.Sqrt(d.X*d.X + d.Y*d.Y);
                if (len > 0.05f) return new Vector3(d.X/len, d.Y/len, 0f);
            }
            catch { }
            return new Vector3(-1f,0f,0f);
        }

        private static void PlayBreakAnimation(Ped player, Vehicle vehicle, string dict, string anim)
        {
            RequestAnim(dict);
            Vector3 door = DriverDoorPosition(vehicle);
            try { Function.Call(Hash.TASK_TURN_PED_TO_FACE_COORD, player.Handle, door.X, door.Y, door.Z, 150); } catch { }
            try
            {
                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict) && !Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, player.Handle, dict, anim, 3))
                    Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, dict, anim, 4.0f, -4.0f, -1, 16, 0f, false, false, false);
            }
            catch { }
        }

        private static void RequestBreakAnimations() { RequestAnim(QuietDict); RequestAnim(ForceDict); }
        private static void RequestAnim(string dict) { try { Function.Call(Hash.REQUEST_ANIM_DICT, dict); } catch { } }

        private static void StopBreakAnimation(Ped player)
        {
            if (player == null || !player.Exists()) return;
            try
            {
                Function.Call(Hash.STOP_ANIM_TASK, player.Handle, QuietDict, QuietAnim, 2.0f);
                Function.Call(Hash.STOP_ANIM_TASK, player.Handle, ForceDict, ForceAnim, 2.0f);
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

            bool occupied = false;
            try { occupied = vehicle.Driver != null && vehicle.Driver.Exists(); } catch { }
            int roll = StableRoll(key);
            // Empty street cars should usually be locked. Occupied traffic remains
            // vanilla-carjackable and is not turned into a sealed moving box.
            bool locked = !personal && !occupied && roll < 90;
            p = new VehicleProfile
            {
                Key = key,
                ModelHash = model,
                Plate = plate,
                HasKey = personal,
                Locked = locked,
                LockTier = personal ? 0 : (roll < 35 ? 1 : (roll < 78 ? 2 : 3)),
                TrackerPresent = !personal && StableRoll(key + ":tracker") < (IsPremium(vehicle) ? 74 : 30)
            };
            if (personal) NormalizePersonalProfile(p);
            _profiles[key] = p;
            return p;
        }

        private static void NormalizePersonalProfile(VehicleProfile p)
        {
            if (p == null) return;
            p.HasKey = true; p.Locked = false; p.AccessBypassed = false; p.Hotwired = false; p.Stolen = false; p.TrackerDisabled = false;
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
                    Vehicle v = e as Vehicle;
                    if (v != null && v.Exists()) return v;
                }
            }
            catch { }
            Vehicle[] vehicles;
            try { vehicles = World.GetNearbyVehicles(player, 2.4f); } catch { return null; }
            Vector3 cam = GameplayCamera.Direction;
            Vehicle best = null; float bestScore = float.MinValue;
            foreach (Vehicle v in vehicles)
            {
                if (v == null || !v.Exists()) continue;
                Vector3 d = v.Position - player.Position;
                float len = Length(d);
                if (len < 0.1f || len > 2.4f) continue;
                float dot = Dot(cam, d) / len;
                if (dot < 0.50f) continue;
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
            foreach (string name in models) { try { if (v.Model.Hash == Function.Call<int>(Hash.GET_HASH_KEY, name)) return true; } catch { } }
            return false;
        }
        private static bool IsEngineRunning(Vehicle v) { try { return v != null && v.Exists() && Function.Call<bool>(Hash.GET_IS_VEHICLE_ENGINE_RUNNING, v.Handle); } catch { return false; } }
        private static bool IsPremium(Vehicle v) { try { int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, v.Handle); return cls == 3 || cls == 5 || cls == 6 || cls == 7 || cls == 22; } catch { return false; } }
        private static bool IsMissionEntity(Entity e) { try { return Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, e.Handle); } catch { return true; } }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true; } catch { }
            return false;
        }

        private static void DisableControl(int control) { try { Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, control, true); } catch { } }
        private static bool Pressed(int control) { try { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, control) || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, control); } catch { return false; } }

        private void ShowHelp(string text)
        {
            if (Game.GameTime - _lastHelp < 100) return;
            _lastHelp = Game.GameTime;
            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP, 0, false, true, -1);
            }
            catch { }
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
                    string[] p = line.Split('|'); if (p.Length < 10) continue;
                    var v = new VehicleProfile
                    {
                        Key=p[0], ModelHash=ParseInt(p[1]), Plate=p[2], HasKey=ParseBool(p[3]), Locked=ParseBool(p[4]), LockTier=ParseInt(p[5]),
                        AccessBypassed=ParseBool(p[6]), Hotwired=ParseBool(p[7]), Stolen=ParseBool(p[8]), TrackerPresent=ParseBool(p[9]), TrackerDisabled=p.Length>10 && ParseBool(p[10])
                    };
                    if (!string.IsNullOrWhiteSpace(v.Key)) _profiles[v.Key]=v;
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
                    lines.Add(string.Join("|", p.Key,p.ModelHash,p.Plate,p.HasKey,p.Locked,p.LockTier,p.AccessBypassed,p.Hotwired,p.Stolen,p.TrackerPresent,p.TrackerDisabled));
                File.WriteAllLines(ProfilesPath, lines.ToArray());
            }
            catch (Exception ex) { Log("Profile save failed safely: " + ex.Message); }
        }

        private static string Plate(Vehicle v) { try { return (Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, v.Handle) ?? string.Empty).Trim().ToUpperInvariant(); } catch { return string.Empty; } }
        private static int StableRoll(string text)
        {
            unchecked { int h=17; foreach(char c in text ?? string.Empty) h=h*31+c; if(h==int.MinValue)h=0; return Math.Abs(h)%100; }
        }
        private static float HeadingTo(Vector3 from, Vector3 to) { try { return Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D, to.X-from.X, to.Y-from.Y); } catch { return 0f; } }
        private static float Dot(Vector3 a, Vector3 b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
        private static float Length(Vector3 v) { return (float)Math.Sqrt(v.X*v.X+v.Y*v.Y+v.Z*v.Z); }
        private static float Distance(Vector3 a, Vector3 b) { return Length(a-b); }

        private void ResetBreakAction(Ped player)
        {
            StopBreakAnimation(player);
            _breakVehicle=0; _breakStarted=0; _breakAnimStarted=0; _breakMode=BreakInMode.Undecided;
        }
        private void ResetTransient()
        {
            ResetBreakAction(Game.LocalPlayerPed);
            _hotwireVehicle=0; _hotwireStarted=0; _enterWasDown=Pressed(InputEnter); _attackWasDown=Pressed(InputAttack);
        }
        private static int ParseInt(string s) { int v; return int.TryParse(s,out v)?v:0; }
        private static bool ParseBool(string s) { bool v; return bool.TryParse(s,out v)&&v; }
        private void OnAborted(object sender, EventArgs e) { SaveProfiles(); ResetTransient(); }
        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+text+Environment.NewLine); } catch { }
        }
    }
}
