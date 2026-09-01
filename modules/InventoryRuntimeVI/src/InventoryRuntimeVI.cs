using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace VOX.InventoryRuntimeVI
{
    public sealed class InventoryRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\InventoryRuntimeVI";
        private const string LogPath = DataDir + "\\InventoryRuntimeVI.log";
        private const string InventoryPath = DataDir + "\\TrunkInventory.txt";
        private const string MaskPath = DataDir + "\\MaskState.txt";
        private const string TransferDict = "pickup_object";
        private const string TransferAnim = "pickup_low";
        private const Keys TrunkKey = Keys.H;
        private const Keys MaskKey = Keys.M;
        private const int TrunkCapacity = 5;

        private sealed class WeaponEntry { public int Hash; public int Ammo; }
        private sealed class MaskState { public int Drawable; public int Texture; public int Palette; }

        private readonly Dictionary<string, List<WeaponEntry>> _trunks = new Dictionary<string, List<WeaponEntry>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MaskState> _masks = new Dictionary<string, MaskState>(StringComparer.OrdinalIgnoreCase);
        private bool _trunkRequested;
        private bool _maskRequested;
        private string _pendingKind = string.Empty;
        private int _pendingVehicle;
        private int _pendingStarted;
        private int _pendingWeapon;
        private WeaponEntry _pendingEntry;
        private bool _transferAnimationStarted;
        private int _lastHelp;
        private int _lastSave;
        private int _storyYieldUntil;

        public InventoryRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Load();
            LoadMasks();
            Interval = 50;
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
            Log("Inventory Runtime VI 0.3.0 loaded: direct physical trunk + direct mask toggle, no inventory wheel.");
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == TrunkKey) _trunkRequested = true;
            else if (e.KeyCode == MaskKey) _maskRequested = true;
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
                if (player.IsInVehicle()) { _trunkRequested = _maskRequested = false; return; }

                if (_maskRequested)
                {
                    _maskRequested = false;
                    ToggleMask(player);
                }

                Vehicle vehicle = FindNearestRearVehicle(player.Position, 3.4f, out float rearDistance);
                bool validTrunk = vehicle != null && vehicle.Exists() && rearDistance <= 2.25f && !IsMissionEntity(vehicle);
                if (!validTrunk) { _trunkRequested = false; return; }

                string key = VehicleKey(vehicle);
                List<WeaponEntry> trunk = GetTrunk(key);
                int selected = SafeSelectedWeapon(player);
                if (IsArmed(selected))
                    ShowHelp(trunk.Count >= TrunkCapacity ? "Coffre plein" : "[H]  Ranger l'arme dans le coffre");
                else if (trunk.Count > 0)
                    ShowHelp("[H]  Prendre " + WeaponName(trunk[trunk.Count - 1].Hash) + " dans le coffre");

                if (_trunkRequested)
                {
                    _trunkRequested = false;
                    if (IsArmed(selected))
                    {
                        if (trunk.Count >= TrunkCapacity) { ShowHelp("Coffre plein"); return; }
                        BeginPhysicalTransfer(player, vehicle, "store", selected, null);
                    }
                    else if (trunk.Count > 0)
                        BeginPhysicalTransfer(player, vehicle, "retrieve", trunk[trunk.Count - 1].Hash, trunk[trunk.Count - 1]);
                }

                int now = Game.GameTime;
                if (now - _lastSave > 12000) { _lastSave = now; Save(); SaveMasks(); }
            }
            catch (Exception ex) { Log("Tick error: " + ex.Message); }
        }

        private void BeginPhysicalTransfer(Ped player, Vehicle vehicle, string kind, int weaponHash, WeaponEntry entry)
        {
            _pendingKind = kind;
            _pendingVehicle = vehicle.Handle;
            _pendingStarted = Game.GameTime;
            _pendingWeapon = weaponHash;
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
            if (_pendingKind == "store") StoreWeapon(player, key, trunk, _pendingWeapon);
            else RetrieveWeapon(player, key, trunk, _pendingEntry);
            ResetTransfer(player, vehicle);
        }

        private void StoreWeapon(Ped player, string key, List<WeaponEntry> trunk, int weaponHash)
        {
            if (trunk.Count >= TrunkCapacity || !IsArmed(weaponHash)) return;
            int ammo = 0;
            try { ammo = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, player.Handle, weaponHash); } catch { }
            trunk.Add(new WeaponEntry { Hash = weaponHash, Ammo = Math.Max(0, ammo) });
            try { Function.Call(Hash.REMOVE_WEAPON_FROM_PED, player.Handle, weaponHash); } catch { }
            Save();
            Log("Stored weapon " + weaponHash + " ammo=" + ammo + " trunk=" + key + ".");
        }

        private void RetrieveWeapon(Ped player, string key, List<WeaponEntry> trunk, WeaponEntry requested)
        {
            if (requested == null || trunk.Count == 0) return;
            int index = trunk.FindLastIndex(w => object.ReferenceEquals(w, requested) || (w.Hash == requested.Hash && w.Ammo == requested.Ammo));
            if (index < 0) return;
            WeaponEntry entry = trunk[index];
            trunk.RemoveAt(index);
            try { Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, entry.Hash, Math.Max(0, entry.Ammo), false, true); } catch { }
            Save();
            Log("Retrieved weapon " + entry.Hash + " ammo=" + entry.Ammo + " trunk=" + key + ".");
        }

        private void ToggleMask(Ped player)
        {
            string key = CharacterKey(player);
            MaskState state;
            if (!_masks.TryGetValue(key, out state)) { state = new MaskState(); _masks[key] = state; }
            try
            {
                int drawable = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, player.Handle, 1);
                if (drawable > 0)
                {
                    state.Drawable = drawable;
                    state.Texture = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, player.Handle, 1);
                    state.Palette = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, player.Handle, 1);
                    Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle, 1, 0, 0, 0);
                    Log("Mask removed character=" + key + ".");
                }
                else if (state.Drawable > 0)
                {
                    Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle, 1, state.Drawable, state.Texture, state.Palette);
                    Log("Mask restored character=" + key + ".");
                }
                SaveMasks();
            }
            catch (Exception ex) { Log("Mask toggle failed safely: " + ex.Message); }
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

        private static Vector3 RearPoint(Vehicle v)
        {
            try { return Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS, v.Handle, 0f, -2.25f, 0f); }
            catch { return v.Position; }
        }

        private static int SafeSelectedWeapon(Ped p) { try { return Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON, p.Handle); } catch { return 0; } }
        private static bool IsArmed(int hash) { return hash != 0 && hash != SafeHash("WEAPON_UNARMED"); }
        private static int SafeHash(string name) { try { return Function.Call<int>(Hash.GET_HASH_KEY, name); } catch { return 0; } }
        private static string WeaponName(int hash)
        {
            if (hash == SafeHash("WEAPON_PUMPSHOTGUN")) return "le fusil a pompe";
            if (hash == SafeHash("WEAPON_CARBINERIFLE")) return "la carabine";
            if (hash == SafeHash("WEAPON_ASSAULTRIFLE")) return "le fusil d'assaut";
            if (hash == SafeHash("WEAPON_SNIPERRIFLE")) return "le fusil de precision";
            return "l'arme";
        }

        private static string VehicleKey(Vehicle v)
        {
            string plate = string.Empty;
            try { plate = (Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, v.Handle) ?? string.Empty).Trim().ToUpperInvariant(); } catch { }
            return v.Model.Hash.ToString(CultureInfo.InvariantCulture) + ":" + plate;
        }

        private static string CharacterKey(Ped player) { return player.Model.Hash.ToString(CultureInfo.InvariantCulture); }
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
                    string[] p = line.Split('|'); if (p.Length < 3) continue;
                    int hash, ammo; if (!int.TryParse(p[1], out hash) || !int.TryParse(p[2], out ammo)) continue;
                    List<WeaponEntry> trunk = GetTrunk(p[0]); if (trunk.Count < TrunkCapacity) trunk.Add(new WeaponEntry { Hash = hash, Ammo = Math.Max(0, ammo) });
                }
            }
            catch (Exception ex) { Log("Inventory load failed safely: " + ex.Message); }
        }

        private void Save()
        {
            try
            {
                var lines = new List<string>();
                foreach (var pair in _trunks) foreach (WeaponEntry w in pair.Value) lines.Add(pair.Key + "|" + w.Hash + "|" + w.Ammo);
                File.WriteAllLines(InventoryPath, lines.ToArray());
            }
            catch (Exception ex) { Log("Inventory save failed safely: " + ex.Message); }
        }

        private void LoadMasks()
        {
            if (!File.Exists(MaskPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(MaskPath))
                {
                    string[] p = line.Split('|'); if (p.Length < 4) continue;
                    _masks[p[0]] = new MaskState { Drawable = ParseInt(p[1]), Texture = ParseInt(p[2]), Palette = ParseInt(p[3]) };
                }
            }
            catch (Exception ex) { Log("Mask load failed safely: " + ex.Message); }
        }

        private void SaveMasks()
        {
            try
            {
                var lines = new List<string>();
                foreach (var pair in _masks) lines.Add(pair.Key + "|" + pair.Value.Drawable + "|" + pair.Value.Texture + "|" + pair.Value.Palette);
                File.WriteAllLines(MaskPath, lines.ToArray());
            }
            catch (Exception ex) { Log("Mask save failed safely: " + ex.Message); }
        }

        private void ResetTransfer(Ped player, Vehicle vehicle)
        {
            try
            {
                if (player != null && player.Exists()) Function.Call(Hash.STOP_ANIM_TASK, player.Handle, TransferDict, TransferAnim, 1.0f);
                if (vehicle != null && vehicle.Exists()) Function.Call(Hash.SET_VEHICLE_DOOR_SHUT, vehicle.Handle, 5, false);
            }
            catch { }
            _pendingKind = string.Empty; _pendingVehicle = _pendingStarted = _pendingWeapon = 0; _pendingEntry = null; _transferAnimationStarted = false;
        }

        private void ResetAll()
        {
            Ped player = Game.LocalPlayerPed;
            Vehicle vehicle = player != null && player.Exists() ? FindVehicleByHandle(player.Position, 6.0f, _pendingVehicle) : null;
            ResetTransfer(player, vehicle);
            _trunkRequested = _maskRequested = false;
        }

        private static int ParseInt(string value) { int parsed; return int.TryParse(value, out parsed) ? parsed : 0; }
        private static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
        private void OnAborted(object sender, EventArgs e) { Save(); SaveMasks(); ResetAll(); }
        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); }
            catch { }
        }
    }
}
