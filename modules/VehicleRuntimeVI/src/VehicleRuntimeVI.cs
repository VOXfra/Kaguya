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
        private const int InputContext = 51;

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
        private int _actionVehicle;
        private int _actionStarted;
        private string _actionKind = string.Empty;
        private int _hotwireVehicle;
        private int _hotwireStarted;
        private int _lastSave;
        private int _lastStateWrite;
        private int _lastHelp;

        public VehicleRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            LoadProfiles();
            Interval = 50;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Vehicle Runtime VI 0.1.1 loaded: personal-vehicle normalization + safe running-engine theft state.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { ResetActions(); return; }
                if (RockstarOwnsScene()) { ResetActions(); return; }

                if (player.IsInVehicle())
                {
                    Vehicle current = player.CurrentVehicle;
                    if (current != null && current.Exists()) UpdateInsideVehicle(player, current);
                }
                else
                {
                    _hotwireVehicle = 0;
                    _hotwireStarted = 0;
                    UpdateNearbyVehicleInteraction(player);
                }

                int now = Game.GameTime;
                if (now - _lastSave > 12000) { _lastSave = now; SaveProfiles(); }
                if (now - _lastStateWrite > 1000) { _lastStateWrite = now; WriteActiveState(player); }
            }
            catch (Exception ex) { Log("Tick error: " + ex.Message); }
        }

        private void UpdateNearbyVehicleInteraction(Ped player)
        {
            Vehicle nearest = FindNearestVehicle(player.Position, 4.0f);
            if (nearest == null || !nearest.Exists()) { ResetAction(); return; }
            if (IsMissionEntity(nearest)) { ResetAction(); return; }

            VehicleProfile profile = GetProfile(nearest);
            ApplyDoorState(nearest, profile);

            Vector3 rear = RearPoint(nearest);
            float rearDistance = Distance(player.Position, rear);

            if (profile.Stolen && profile.TrackerPresent && !profile.TrackerDisabled && rearDistance <= 2.2f)
            {
                DisableControl(InputContext);
                ShowHelp("Maintenez ~INPUT_CONTEXT~ derriere le vehicule pour neutraliser le tracker.");
                HandleTimedAction(nearest, profile, "tracker", 3200, IsDisabledControlPressed(InputContext));
                return;
            }

            float distance = Distance(player.Position, nearest.Position);
            if (profile.Locked && !profile.HasKey && !profile.AccessBypassed && distance <= 3.2f)
            {
                DisableControl(InputContext);
                int duration = 1700 + profile.LockTier * 1050;
                ShowHelp("Maintenez ~INPUT_CONTEXT~ pres de la porte pour crocheter la serrure.");
                HandleTimedAction(nearest, profile, "lockpick", duration, IsDisabledControlPressed(InputContext));
                return;
            }

            ResetAction();
        }

        private void HandleTimedAction(Vehicle vehicle, VehicleProfile profile, string kind, int duration, bool pressed)
        {
            if (!pressed)
            {
                if (_actionKind == kind && _actionVehicle == vehicle.Handle) ResetAction();
                return;
            }

            if (_actionVehicle != vehicle.Handle || !string.Equals(_actionKind, kind, StringComparison.Ordinal))
            {
                _actionVehicle = vehicle.Handle;
                _actionKind = kind;
                _actionStarted = Game.GameTime;
                return;
            }

            if (Game.GameTime - _actionStarted < duration) return;

            if (kind == "lockpick")
            {
                profile.AccessBypassed = true;
                profile.Locked = false;
                try { Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, 1); } catch { }
                if (profile.LockTier >= 2 && StableRoll(profile.Key + "alarm") < 58)
                {
                    try
                    {
                        Function.Call(Hash.SET_VEHICLE_ALARM, vehicle.Handle, true);
                        Function.Call(Hash.START_VEHICLE_ALARM, vehicle.Handle);
                    }
                    catch { }
                }
                Log("Vehicle access bypassed key=" + profile.Key + " tier=" + profile.LockTier + ". Release Context, then use the normal enter control.");
            }
            else if (kind == "tracker")
            {
                profile.TrackerDisabled = true;
                Log("Tracker disabled on stolen vehicle key=" + profile.Key + ".");
            }
            SaveProfiles();
            ResetAction();
        }

        private void UpdateInsideVehicle(Ped player, Vehicle vehicle)
        {
            VehicleProfile profile = GetProfile(vehicle);
            bool personal = IsLikelyPersonalVehicle(vehicle);

            if (profile.HasKey || personal)
            {
                NormalizePersonalProfile(profile);
                try { Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, 1); } catch { }
                return;
            }

            profile.Stolen = true;
            profile.Locked = false;
            profile.AccessBypassed = true;

            // Never turn off an engine that was already running when the player got in.
            // A running unattended/stolen car can simply be driven away; there is
            // nothing sensible to hotwire until the engine actually needs starting.
            if (IsEngineRunning(vehicle))
            {
                if (!profile.Hotwired)
                {
                    profile.Hotwired = true;
                    SaveProfiles();
                    Log("Running unkeyed vehicle accepted without forced hotwire key=" + profile.Key + ".");
                }
                _hotwireVehicle = 0;
                _hotwireStarted = 0;
                return;
            }

            if (profile.Hotwired)
            {
                try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false); } catch { }
                return;
            }

            if (_hotwireVehicle != vehicle.Handle)
            {
                _hotwireVehicle = vehicle.Handle;
                _hotwireStarted = Game.GameTime;
                Log("Automatic hotwire started key=" + profile.Key + ".");
            }

            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, false, true, true); } catch { }
            int duration = 2300 + profile.LockTier * 1150;
            ShowHelp("Branchement du vehicule en cours...");
            if (Game.GameTime - _hotwireStarted < duration) return;

            profile.Hotwired = true;
            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false); } catch { }
            SaveProfiles();
            Log("Vehicle hotwired key=" + profile.Key + " tracker=" + profile.TrackerPresent + ".");
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
                // Older builds may have persisted the protagonist's own car as a
                // locked/stolen profile. Re-evaluate ownership every time instead
                // of trusting stale persistence forever.
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
            try { Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, profile.Locked && !profile.AccessBypassed && !profile.HasKey ? 2 : 1); }
            catch { }
        }

        private static bool IsLikelyPersonalVehicle(Vehicle v)
        {
            string[] models = { "tailgater", "buffalo2", "bodhi2" };
            foreach (string name in models)
            {
                try { if (v.Model.Hash == Function.Call<int>(Hash.GET_HASH_KEY, name)) return true; } catch { }
            }
            return false;
        }

        private static bool IsEngineRunning(Vehicle v)
        {
            if (v == null || !v.Exists()) return false;
            try { return Function.Call<bool>(Hash.GET_IS_VEHICLE_ENGINE_RUNNING, v.Handle); }
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

        private static Vehicle FindNearestVehicle(Vector3 pos, float radius)
        {
            Vehicle[] vehicles;
            try { vehicles = World.GetNearbyVehicles(pos, radius); } catch { return null; }
            Vehicle best = null;
            float bestD = float.MaxValue;
            foreach (Vehicle v in vehicles)
            {
                if (v == null || !v.Exists()) continue;
                float d = Distance(pos, v.Position);
                if (d < bestD) { bestD = d; best = v; }
            }
            return best;
        }

        private static Vector3 RearPoint(Vehicle v)
        {
            try { return Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS, v.Handle, 0f, -2.25f, 0f); }
            catch { return v.Position; }
        }

        private static bool IsMissionEntity(Entity e)
        {
            try { return Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, e.Handle); } catch { return false; }
        }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { return Function.Call<bool>(Hash.GET_MISSION_FLAG); } catch { return false; }
        }

        private static void DisableControl(int control)
        {
            try { Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, control, true); } catch { }
        }

        private static bool IsDisabledControlPressed(int control)
        {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, control); } catch { return false; }
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

        private void WriteActiveState(Ped player)
        {
            try
            {
                if (!player.IsInVehicle()) { File.WriteAllText(ActiveStatePath, "none"); return; }
                Vehicle v = player.CurrentVehicle;
                if (v == null || !v.Exists()) { File.WriteAllText(ActiveStatePath, "none"); return; }
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
                        TrackerPresent = ParseBool(p[9]), TrackerDisabled = p.Length > 10 && ParseBool(p[10])
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
                    lines.Add(string.Join("|", p.Key, p.ModelHash, p.Plate, p.HasKey, p.Locked, p.LockTier, p.AccessBypassed, p.Hotwired, p.Stolen, p.TrackerPresent, p.TrackerDisabled));
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

        private static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        private void ResetAction() { _actionVehicle = 0; _actionStarted = 0; _actionKind = string.Empty; }
        private void ResetActions() { ResetAction(); _hotwireVehicle = 0; _hotwireStarted = 0; }
        private static int ParseInt(string s) { int v; return int.TryParse(s, out v) ? v : 0; }
        private static bool ParseBool(string s) { bool v; return bool.TryParse(s, out v) && v; }
        private void OnAborted(object sender, EventArgs e) { SaveProfiles(); ResetActions(); }
        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); }
            catch { }
        }
    }
}
