using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.InventoryRuntimeVI
{
    public sealed class InventoryRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\InventoryRuntimeVI";
        private const string LogPath = DataDir + "\\InventoryRuntimeVI.log";
        private const string InventoryPath = DataDir + "\\TrunkInventory.txt";
        private const string TransferDict = "pickup_object";
        private const string TransferAnim = "pickup_low";
        private const int ContextControl = 51; // E / D-pad right
        private const int TrunkCapacity = 5;

        private sealed class WeaponEntry
        {
            public int Hash;
            public int Ammo;
            public int Tint;
            public readonly List<int> Components = new List<int>();
        }

        // Common Story/Enhanced weapon components. We only persist components the
        // ped actually owns; unsupported component hashes are harmlessly ignored.
        private static readonly string[] KnownComponentNames =
        {
            "COMPONENT_AT_PI_FLSH", "COMPONENT_AT_PI_FLSH_02", "COMPONENT_AT_PI_FLSH_03",
            "COMPONENT_AT_AR_FLSH", "COMPONENT_AT_AR_AFGRIP", "COMPONENT_AT_AR_AFGRIP_02",
            "COMPONENT_AT_SCOPE_MACRO", "COMPONENT_AT_SCOPE_MACRO_02", "COMPONENT_AT_SCOPE_MACRO_02_MK2",
            "COMPONENT_AT_SCOPE_SMALL", "COMPONENT_AT_SCOPE_SMALL_02", "COMPONENT_AT_SCOPE_SMALL_MK2",
            "COMPONENT_AT_SCOPE_MEDIUM", "COMPONENT_AT_SCOPE_MEDIUM_MK2", "COMPONENT_AT_SCOPE_LARGE",
            "COMPONENT_AT_SCOPE_LARGE_FIXED_ZOOM", "COMPONENT_AT_SCOPE_LARGE_MK2", "COMPONENT_AT_SCOPE_MAX",
            "COMPONENT_AT_PI_SUPP", "COMPONENT_AT_PI_SUPP_02", "COMPONENT_AT_AR_SUPP", "COMPONENT_AT_AR_SUPP_02",
            "COMPONENT_AT_SR_SUPP", "COMPONENT_AT_SR_SUPP_03",
            "COMPONENT_PISTOL_CLIP_01", "COMPONENT_PISTOL_CLIP_02", "COMPONENT_COMBATPISTOL_CLIP_01", "COMPONENT_COMBATPISTOL_CLIP_02",
            "COMPONENT_APPISTOL_CLIP_01", "COMPONENT_APPISTOL_CLIP_02", "COMPONENT_PISTOL50_CLIP_01", "COMPONENT_PISTOL50_CLIP_02",
            "COMPONENT_SNSPISTOL_CLIP_01", "COMPONENT_SNSPISTOL_CLIP_02", "COMPONENT_HEAVYPISTOL_CLIP_01", "COMPONENT_HEAVYPISTOL_CLIP_02",
            "COMPONENT_VINTAGEPISTOL_CLIP_01", "COMPONENT_VINTAGEPISTOL_CLIP_02", "COMPONENT_MARKSMANPISTOL_CLIP_01",
            "COMPONENT_MICROSMG_CLIP_01", "COMPONENT_MICROSMG_CLIP_02", "COMPONENT_MICROSMG_CLIP_03",
            "COMPONENT_SMG_CLIP_01", "COMPONENT_SMG_CLIP_02", "COMPONENT_SMG_CLIP_03",
            "COMPONENT_ASSAULTSMG_CLIP_01", "COMPONENT_ASSAULTSMG_CLIP_02", "COMPONENT_MINISMG_CLIP_01", "COMPONENT_MINISMG_CLIP_02",
            "COMPONENT_ASSAULTRIFLE_CLIP_01", "COMPONENT_ASSAULTRIFLE_CLIP_02", "COMPONENT_ASSAULTRIFLE_CLIP_03",
            "COMPONENT_CARBINERIFLE_CLIP_01", "COMPONENT_CARBINERIFLE_CLIP_02", "COMPONENT_CARBINERIFLE_CLIP_03",
            "COMPONENT_ADVANCEDRIFLE_CLIP_01", "COMPONENT_ADVANCEDRIFLE_CLIP_02",
            "COMPONENT_SPECIALCARBINE_CLIP_01", "COMPONENT_SPECIALCARBINE_CLIP_02", "COMPONENT_SPECIALCARBINE_CLIP_03",
            "COMPONENT_BULLPUPRIFLE_CLIP_01", "COMPONENT_BULLPUPRIFLE_CLIP_02",
            "COMPONENT_COMPACTRIFLE_CLIP_01", "COMPONENT_COMPACTRIFLE_CLIP_02", "COMPONENT_COMPACTRIFLE_CLIP_03",
            "COMPONENT_PUMPSHOTGUN_CLIP_01", "COMPONENT_SAWNOFFSHOTGUN_CLIP_01", "COMPONENT_ASSAULTSHOTGUN_CLIP_01", "COMPONENT_ASSAULTSHOTGUN_CLIP_02",
            "COMPONENT_BULLPUPSHOTGUN_CLIP_01", "COMPONENT_HEAVYSHOTGUN_CLIP_01", "COMPONENT_HEAVYSHOTGUN_CLIP_02", "COMPONENT_HEAVYSHOTGUN_CLIP_03",
            "COMPONENT_SNIPERRIFLE_CLIP_01", "COMPONENT_HEAVYSNIPER_CLIP_01", "COMPONENT_MARKSMANRIFLE_CLIP_01", "COMPONENT_MARKSMANRIFLE_CLIP_02"
        };

        private readonly Dictionary<string, List<WeaponEntry>> _trunks = new Dictionary<string, List<WeaponEntry>>(StringComparer.OrdinalIgnoreCase);
        private bool _contextWasDown;
        private int _lastContextAction;
        private string _pendingKind = string.Empty;
        private int _pendingVehicle;
        private int _pendingStarted;
        private WeaponEntry _pendingEntry;
        private bool _transferAnimationStarted;
        private int _lastHelp;
        private int _lastSave;
        private int _storyYieldUntil;

        public InventoryRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Load();
            Interval = 40;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Inventory Runtime VI 0.4.0 trunk runtime loaded: native Context input, debounced actions, attachment-safe storage.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { ResetAll(); return; }
                if (RockstarOwnsScene()) { _storyYieldUntil = Game.GameTime + 5000; ResetAll(); return; }
                if (Game.GameTime < _storyYieldUntil) { ResetAll(); return; }
                if (!string.IsNullOrEmpty(_pendingKind)) { UpdatePhysicalTransfer(player); return; }
                if (player.IsInVehicle()) { _contextWasDown = ReadContext(); return; }

                Vehicle vehicle = FindNearestRearVehicle(player.Position, 3.4f, out float rearDistance);
                bool validTrunk = vehicle != null && vehicle.Exists() && rearDistance <= 2.15f && !IsMissionEntity(vehicle);
                bool contextDown = ReadContext();
                bool justPressed = contextDown && !_contextWasDown;
                _contextWasDown = contextDown;
                if (!validTrunk) return;

                string key = VehicleKey(vehicle);
                List<WeaponEntry> trunk = GetTrunk(key);
                int selected = SafeSelectedWeapon(player);
                bool armed = IsArmed(selected);
                if (armed)
                    ShowHelp(trunk.Count >= TrunkCapacity ? "Coffre plein" : "~INPUT_CONTEXT~  Ranger l'arme");
                else if (trunk.Count > 0)
                    ShowHelp("~INPUT_CONTEXT~  Prendre " + WeaponName(trunk[trunk.Count - 1].Hash));

                if (justPressed && Game.GameTime - _lastContextAction >= 650)
                {
                    _lastContextAction = Game.GameTime;
                    if (armed)
                    {
                        if (trunk.Count >= TrunkCapacity) return;
                        BeginPhysicalTransfer(player, vehicle, "store", CaptureWeapon(player, selected));
                    }
                    else if (trunk.Count > 0)
                        BeginPhysicalTransfer(player, vehicle, "retrieve", Clone(trunk[trunk.Count - 1]));
                }

                int now = Game.GameTime;
                if (now - _lastSave > 12000) { _lastSave = now; Save(); }
            }
            catch (Exception ex) { Log("Tick error: " + ex); }
        }

        private void BeginPhysicalTransfer(Ped player, Vehicle vehicle, string kind, WeaponEntry entry)
        {
            if (entry == null) return;
            _pendingKind = kind;
            _pendingVehicle = vehicle.Handle;
            _pendingStarted = Game.GameTime;
            _pendingEntry = entry;
            _transferAnimationStarted = false;
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, TransferDict);
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, player.Handle, vehicle.Handle, 300);
                Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, vehicle.Handle, 5, false, false);
            }
            catch { }
        }

        private void UpdatePhysicalTransfer(Ped player)
        {
            Vehicle vehicle = FindVehicleByHandle(player.Position, 5.0f, _pendingVehicle);
            if (vehicle == null || !vehicle.Exists() || IsMissionEntity(vehicle) || Distance(player.Position, RearPoint(vehicle)) > 3.0f)
            { ResetTransfer(player, vehicle); return; }

            int elapsed = Game.GameTime - _pendingStarted;
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, TransferDict);
                if (!_transferAnimationStarted && elapsed >= 250 && Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, TransferDict))
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, TransferDict, TransferAnim, 4.0f, -4.0f, 900, 0, 0f, false, false, false);
                    _transferAnimationStarted = true;
                }
            }
            catch { }
            if (elapsed < 850) return;

            string key = VehicleKey(vehicle);
            List<WeaponEntry> trunk = GetTrunk(key);
            if (_pendingKind == "store")
            {
                if (trunk.Count < TrunkCapacity && IsArmed(_pendingEntry.Hash))
                {
                    trunk.Add(Clone(_pendingEntry));
                    try { Function.Call(Hash.REMOVE_WEAPON_FROM_PED, player.Handle, _pendingEntry.Hash); } catch { }
                    Save();
                    Log("Stored weapon " + _pendingEntry.Hash + " ammo=" + _pendingEntry.Ammo + " tint=" + _pendingEntry.Tint + " components=" + _pendingEntry.Components.Count + " trunk=" + key + ".");
                }
            }
            else if (_pendingKind == "retrieve" && trunk.Count > 0)
            {
                int index = FindMatchingEntry(trunk, _pendingEntry);
                if (index >= 0)
                {
                    WeaponEntry entry = trunk[index];
                    trunk.RemoveAt(index);
                    RestoreWeapon(player, entry, true);
                    Save();
                    Log("Retrieved weapon " + entry.Hash + " ammo=" + entry.Ammo + " tint=" + entry.Tint + " components=" + entry.Components.Count + " trunk=" + key + ".");
                }
            }
            ResetTransfer(player, vehicle);
        }

        private static WeaponEntry CaptureWeapon(Ped player, int weaponHash)
        {
            if (player == null || !player.Exists() || !IsArmed(weaponHash)) return null;
            var w = new WeaponEntry { Hash = weaponHash, Tint = 0 };
            try { w.Ammo = Math.Max(0, Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, player.Handle, weaponHash)); } catch { }
            try { w.Tint = Math.Max(0, Function.Call<int>(Hash.GET_PED_WEAPON_TINT_INDEX, player.Handle, weaponHash)); } catch { }
            foreach (string name in KnownComponentNames)
            {
                int c = SafeHash(name);
                if (c == 0) continue;
                try { if (Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON_COMPONENT, player.Handle, weaponHash, c)) w.Components.Add(c); } catch { }
            }
            return w;
        }

        private static void RestoreWeapon(Ped player, WeaponEntry w, bool equip)
        {
            if (player == null || !player.Exists() || w == null || !IsArmed(w.Hash)) return;
            try { Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, w.Hash, Math.Max(0, w.Ammo), false, equip); } catch { }
            try { Function.Call(Hash.SET_PED_WEAPON_TINT_INDEX, player.Handle, w.Hash, Math.Max(0, w.Tint)); } catch { }
            foreach (int c in w.Components)
            {
                try { Function.Call(Hash.GIVE_WEAPON_COMPONENT_TO_PED, player.Handle, w.Hash, c); } catch { }
            }
        }

        private static WeaponEntry Clone(WeaponEntry source)
        {
            if (source == null) return null;
            var w = new WeaponEntry { Hash = source.Hash, Ammo = source.Ammo, Tint = source.Tint };
            w.Components.AddRange(source.Components);
            return w;
        }

        private static int FindMatchingEntry(List<WeaponEntry> list, WeaponEntry target)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                WeaponEntry w = list[i];
                if (w.Hash == target.Hash && w.Ammo == target.Ammo && w.Tint == target.Tint) return i;
            }
            return list.Count - 1;
        }

        private List<WeaponEntry> GetTrunk(string key)
        {
            List<WeaponEntry> list;
            if (!_trunks.TryGetValue(key, out list)) { list = new List<WeaponEntry>(); _trunks[key] = list; }
            return list;
        }

        private static Vehicle FindNearestRearVehicle(Vector3 playerPos, float radius, out float bestRearDistance)
        {
            bestRearDistance = float.MaxValue;
            Vehicle[] vehicles;
            try { vehicles = World.GetNearbyVehicles(playerPos, radius); } catch { return null; }
            Vehicle best = null;
            foreach (Vehicle v in vehicles)
            {
                if (v == null || !v.Exists()) continue;
                float d = Distance(playerPos, RearPoint(v));
                if (d < bestRearDistance) { bestRearDistance = d; best = v; }
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

        internal static Vector3 RearPoint(Vehicle v)
        {
            try { return Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS, v.Handle, 0f, -2.25f, 0f); }
            catch { return v.Position; }
        }

        internal static string VehicleKey(Vehicle v)
        {
            string plate = string.Empty;
            try { plate = (Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, v.Handle) ?? string.Empty).Trim().ToUpperInvariant(); } catch { }
            return v.Model.Hash.ToString(CultureInfo.InvariantCulture) + ":" + plate;
        }

        internal static bool IsMissionEntity(Entity e) { try { return Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, e.Handle); } catch { return true; } }
        private static int SafeSelectedWeapon(Ped p) { try { return Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON, p.Handle); } catch { return 0; } }
        internal static bool IsArmed(int hash) { return hash != 0 && hash != SafeHash("WEAPON_UNARMED"); }
        internal static int SafeHash(string name) { try { return Function.Call<int>(Hash.GET_HASH_KEY, name); } catch { return 0; } }
        private static string WeaponName(int hash)
        {
            if (hash == SafeHash("WEAPON_PUMPSHOTGUN")) return "le fusil a pompe";
            if (hash == SafeHash("WEAPON_CARBINERIFLE")) return "la carabine";
            if (hash == SafeHash("WEAPON_ASSAULTRIFLE")) return "le fusil d'assaut";
            if (hash == SafeHash("WEAPON_SNIPERRIFLE")) return "le fusil de precision";
            return "l'arme";
        }

        private static bool ReadContext()
        {
            try { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, ContextControl) || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, ContextControl); }
            catch { return false; }
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

        private void Load()
        {
            if (!File.Exists(InventoryPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(InventoryPath))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 3) continue;
                    int hash, ammo;
                    if (!int.TryParse(p[1], out hash) || !int.TryParse(p[2], out ammo)) continue;
                    var w = new WeaponEntry { Hash = hash, Ammo = Math.Max(0, ammo) };
                    if (p.Length > 3) int.TryParse(p[3], out w.Tint);
                    if (p.Length > 4 && !string.IsNullOrWhiteSpace(p[4]))
                    {
                        foreach (string c in p[4].Split(',')) { int h; if (int.TryParse(c, out h) && h != 0) w.Components.Add(h); }
                    }
                    List<WeaponEntry> trunk = GetTrunk(p[0]);
                    if (trunk.Count < TrunkCapacity) trunk.Add(w);
                }
            }
            catch (Exception ex) { Log("Inventory load failed safely: " + ex.Message); }
        }

        private void Save()
        {
            try
            {
                var lines = new List<string>();
                foreach (var pair in _trunks)
                {
                    foreach (WeaponEntry w in pair.Value)
                    {
                        string components = string.Join(",", w.Components.ConvertAll(x => x.ToString(CultureInfo.InvariantCulture)).ToArray());
                        lines.Add(pair.Key + "|" + w.Hash + "|" + w.Ammo + "|" + w.Tint + "|" + components);
                    }
                }
                File.WriteAllLines(InventoryPath, lines.ToArray());
            }
            catch (Exception ex) { Log("Inventory save failed safely: " + ex.Message); }
        }

        private void ResetTransfer(Ped player, Vehicle vehicle)
        {
            try
            {
                if (player != null && player.Exists()) Function.Call(Hash.STOP_ANIM_TASK, player.Handle, TransferDict, TransferAnim, 1.0f);
                if (vehicle != null && vehicle.Exists()) Function.Call(Hash.SET_VEHICLE_DOOR_SHUT, vehicle.Handle, 5, false);
            }
            catch { }
            _pendingKind = string.Empty;
            _pendingVehicle = 0;
            _pendingStarted = 0;
            _pendingEntry = null;
            _transferAnimationStarted = false;
        }

        private void ResetAll()
        {
            Ped player = Game.LocalPlayerPed;
            Vehicle vehicle = player != null && player.Exists() ? FindVehicleByHandle(player.Position, 6.0f, _pendingVehicle) : null;
            ResetTransfer(player, vehicle);
            _contextWasDown = ReadContext();
        }

        internal static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true; } catch { }
            return false;
        }

        internal static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        private void OnAborted(object sender, EventArgs e) { Save(); ResetAll(); }
        internal static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); }
            catch { }
        }
    }
}
