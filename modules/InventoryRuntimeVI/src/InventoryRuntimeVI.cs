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
        private const string AccessoryPath = DataDir + "\\AccessoryState.txt";
        private const string TransferDict = "pickup_object";
        private const string TransferAnim = "pickup_low";
        private const Keys TrunkKey = Keys.H;
        private const Keys AccessoryKey = Keys.M;
        private const int TrunkCapacity = 5;
        private const int InputFrontendCancel = 202;
        private const int InputFrontendUp = 172;
        private const int InputFrontendDown = 173;
        private const int InputFrontendLeft = 174;
        private const int InputFrontendRight = 175;

        private sealed class WeaponEntry { public int Hash; public int Ammo; }
        private sealed class WheelAction
        {
            public bool Retrieve;
            public WeaponEntry Entry;
            public int WeaponHash;
            public string Label = string.Empty;
        }
        private sealed class AccessoryState
        {
            public int HatDrawable = -1;
            public int HatTexture;
            public int GlassesDrawable = -1;
            public int GlassesTexture;
            public int MaskDrawable;
            public int MaskTexture;
            public int MaskPalette;
        }

        private readonly Dictionary<string, List<WeaponEntry>> _trunks = new Dictionary<string, List<WeaponEntry>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AccessoryState> _accessories = new Dictionary<string, AccessoryState>(StringComparer.OrdinalIgnoreCase);
        private readonly List<WheelAction> _wheelActions = new List<WheelAction>();
        private bool _trunkKeyDown, _trunkOpenRequested, _trunkReleasePending, _trunkWheelOpen;
        private int _trunkWheelVehicle, _trunkWheelSelection, _trunkWheelOpenedAt;
        private bool _accessoryKeyDown, _accessoryOpenRequested, _accessoryReleasePending, _accessoryWheelOpen;
        private int _accessorySelection, _accessoryOpenedAt;
        private string _pendingKind = string.Empty;
        private int _pendingVehicle, _pendingStarted, _pendingWeapon;
        private WeaponEntry _pendingEntry;
        private bool _transferAnimationStarted;
        private int _lastHelp, _lastSave, _storyYieldUntil;

        public InventoryRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Load();
            LoadAccessories();
            Interval = 50;
            Tick += OnTick;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            Aborted += OnAborted;
            Log("Inventory Runtime VI 0.2.0 loaded: physical trunk wheel + contextual accessory wheel.");
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == TrunkKey && !_trunkKeyDown) { _trunkKeyDown = true; _trunkOpenRequested = true; }
            if (e.KeyCode == AccessoryKey && !_accessoryKeyDown) { _accessoryKeyDown = true; _accessoryOpenRequested = true; }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == TrunkKey) { _trunkKeyDown = false; _trunkReleasePending = true; }
            if (e.KeyCode == AccessoryKey) { _accessoryKeyDown = false; _accessoryReleasePending = true; }
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
                if (_trunkWheelOpen) { UpdateTrunkWheel(player); return; }
                if (_accessoryWheelOpen) { UpdateAccessoryWheel(player); return; }
                if (player.IsInVehicle()) { ClearInputRequests(); return; }

                Vehicle vehicle = FindNearestRearVehicle(player.Position, 3.4f, out float rearDistance);
                bool validTrunk = vehicle != null && vehicle.Exists() && rearDistance <= 2.35f && !IsMissionEntity(vehicle);
                if (validTrunk)
                {
                    List<WeaponEntry> trunk = GetTrunk(VehicleKey(vehicle));
                    int selected = SafeSelectedWeapon(player);
                    if (IsArmed(selected) || trunk.Count > 0)
                        ShowHelp("Maintenez [H] pres du coffre pour gerer l'equipement (" + trunk.Count + "/" + TrunkCapacity + ").");
                    if (_trunkOpenRequested)
                    {
                        _trunkOpenRequested = false;
                        OpenTrunkWheel(vehicle, trunk, selected);
                        return;
                    }
                }
                else { _trunkOpenRequested = false; _trunkReleasePending = false; }

                if (_accessoryOpenRequested) { _accessoryOpenRequested = false; OpenAccessoryWheel(); }
                int now = Game.GameTime;
                if (now - _lastSave > 12000) { _lastSave = now; Save(); SaveAccessories(); }
            }
            catch (Exception ex) { Log("Tick error: " + ex.Message); }
        }

        private void OpenTrunkWheel(Vehicle vehicle, List<WeaponEntry> trunk, int selectedWeapon)
        {
            BuildWheelActions(trunk, selectedWeapon);
            if (_wheelActions.Count == 0) { ShowHelp("Le coffre est vide et aucune arme n'est equipee."); return; }
            _trunkWheelOpen = true;
            _trunkWheelVehicle = vehicle.Handle;
            _trunkWheelSelection = 0;
            _trunkWheelOpenedAt = Game.GameTime;
            _trunkReleasePending = false;
        }

        private void BuildWheelActions(List<WeaponEntry> trunk, int selectedWeapon)
        {
            _wheelActions.Clear();
            if (IsArmed(selectedWeapon))
                _wheelActions.Add(new WheelAction { WeaponHash = selectedWeapon, Label = trunk.Count >= TrunkCapacity ? "COFFRE PLEIN" : "RANGER " + WeaponName(selectedWeapon) });
            for (int i = trunk.Count - 1; i >= 0; i--)
            {
                WeaponEntry entry = trunk[i];
                _wheelActions.Add(new WheelAction { Retrieve = true, Entry = entry, WeaponHash = entry.Hash, Label = "PRENDRE " + WeaponName(entry.Hash) });
            }
        }

        private void UpdateTrunkWheel(Ped player)
        {
            Vehicle vehicle = FindVehicleByHandle(player.Position, 5.0f, _trunkWheelVehicle);
            if (vehicle == null || !vehicle.Exists() || IsMissionEntity(vehicle) || Distance(player.Position, RearPoint(vehicle)) > 2.7f) { CloseTrunkWheel(); return; }
            DisableWheelControls();
            UpdateRadialSelection(_wheelActions.Count, ref _trunkWheelSelection);
            DrawTrunkWheel(vehicle);
            if (ReadControlJustPressed(InputFrontendCancel)) { CloseTrunkWheel(); return; }
            if (!_trunkReleasePending) return;
            _trunkReleasePending = false;
            int held = Game.GameTime - _trunkWheelOpenedAt;
            if (held < 250) { CloseTrunkWheel(); return; }
            WheelAction action = _wheelActions[Math.Max(0, Math.Min(_trunkWheelSelection, _wheelActions.Count - 1))];
            if (!action.Retrieve && GetTrunk(VehicleKey(vehicle)).Count >= TrunkCapacity)
            {
                ShowHelp("Le coffre est plein.");
                CloseTrunkWheel();
                return;
            }
            BeginPhysicalTransfer(player, vehicle, action);
            CloseTrunkWheel(false);
        }

        private void BeginPhysicalTransfer(Ped player, Vehicle vehicle, WheelAction action)
        {
            _pendingKind = action.Retrieve ? "retrieve" : "store";
            _pendingVehicle = vehicle.Handle;
            _pendingStarted = Game.GameTime;
            _pendingWeapon = action.WeaponHash;
            _pendingEntry = action.Entry;
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
            if (vehicle == null || !vehicle.Exists() || IsMissionEntity(vehicle) || Distance(player.Position, RearPoint(vehicle)) > 3.0f) { ResetTransfer(player, vehicle); return; }
            DisableWheelControls();
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
            if (requested == null) return;
            int index = trunk.FindIndex(w => object.ReferenceEquals(w, requested));
            if (index < 0) index = trunk.FindIndex(w => w.Hash == requested.Hash && w.Ammo == requested.Ammo);
            if (index < 0) return;
            WeaponEntry entry = trunk[index];
            trunk.RemoveAt(index);
            try { Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, entry.Hash, Math.Max(0, entry.Ammo), false, true); } catch { }
            Save();
            Log("Retrieved weapon " + entry.Hash + " ammo=" + entry.Ammo + " trunk=" + key + ".");
        }

        private void DrawTrunkWheel(Vehicle vehicle)
        {
            int count = _wheelActions.Count;
            for (int i = 0; i < count; i++)
            {
                double angle = -Math.PI / 2.0 + i * (Math.PI * 2.0 / count);
                float x = 0.50f + (float)Math.Cos(angle) * 0.145f;
                float y = 0.78f + (float)Math.Sin(angle) * 0.115f;
                bool selected = i == _trunkWheelSelection;
                DrawTextCentered(x, y, selected ? "[ " + _wheelActions[i].Label + " ]" : _wheelActions[i].Label, selected ? 0.285f : 0.235f,
                    selected ? 255 : 175, selected ? 255 : 175, selected ? 255 : 175, selected ? 245 : 205);
            }
            DrawTextCentered(0.50f, 0.78f, "COFFRE", 0.29f, 225, 225, 225, 230);
            DrawTextCentered(0.50f, 0.815f, GetTrunk(VehicleKey(vehicle)).Count + "/" + TrunkCapacity, 0.24f, 175, 175, 175, 215);
        }

        private void OpenAccessoryWheel()
        {
            _accessoryWheelOpen = true;
            _accessorySelection = 0;
            _accessoryOpenedAt = Game.GameTime;
            _accessoryReleasePending = false;
        }

        private void UpdateAccessoryWheel(Ped player)
        {
            if (player.IsInVehicle()) { CloseAccessoryWheel(); return; }
            DisableWheelControls();
            UpdateRadialSelection(3, ref _accessorySelection);
            DrawAccessoryWheel(player);
            if (ReadControlJustPressed(InputFrontendCancel)) { CloseAccessoryWheel(); return; }
            if (!_accessoryReleasePending) return;
            _accessoryReleasePending = false;
            int held = Game.GameTime - _accessoryOpenedAt;
            int selected = _accessorySelection;
            CloseAccessoryWheel();
            if (held >= 250) ToggleAccessory(player, selected);
        }

        private void DrawAccessoryWheel(Ped player)
        {
            string[] labels = { IsMaskOn(player) ? "RETIRER MASQUE" : "METTRE MASQUE", IsPropOn(player, 1) ? "RETIRER LUNETTES" : "METTRE LUNETTES", IsPropOn(player, 0) ? "RETIRER COUVRE-CHEF" : "METTRE COUVRE-CHEF" };
            float[] xs = { 0.50f, 0.625f, 0.375f };
            float[] ys = { 0.665f, 0.825f, 0.825f };
            for (int i = 0; i < labels.Length; i++)
            {
                bool selected = i == _accessorySelection;
                DrawTextCentered(xs[i], ys[i], selected ? "[ " + labels[i] + " ]" : labels[i], selected ? 0.29f : 0.24f,
                    selected ? 255 : 175, selected ? 255 : 175, selected ? 255 : 175, selected ? 245 : 205);
            }
            DrawTextCentered(0.50f, 0.78f, "ACCESSOIRES", 0.28f, 225, 225, 225, 230);
        }

        private void ToggleAccessory(Ped player, int selected)
        {
            string key = CharacterKey(player);
            AccessoryState state = GetAccessoryState(key);
            try
            {
                if (selected == 0)
                {
                    int drawable = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, player.Handle, 1);
                    if (drawable > 0)
                    {
                        state.MaskDrawable = drawable;
                        state.MaskTexture = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, player.Handle, 1);
                        state.MaskPalette = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, player.Handle, 1);
                        Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle, 1, 0, 0, 0);
                    }
                    else if (state.MaskDrawable > 0) Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle, 1, state.MaskDrawable, state.MaskTexture, state.MaskPalette);
                    else ShowHelp("Aucun masque memorise pour ce personnage.");
                }
                else
                {
                    int slot = selected == 1 ? 1 : 0;
                    int drawable = Function.Call<int>(Hash.GET_PED_PROP_INDEX, player.Handle, slot);
                    int texture = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, player.Handle, slot);
                    if (drawable >= 0)
                    {
                        if (slot == 0) { state.HatDrawable = drawable; state.HatTexture = texture; }
                        else { state.GlassesDrawable = drawable; state.GlassesTexture = texture; }
                        Function.Call(Hash.CLEAR_PED_PROP, player.Handle, slot);
                    }
                    else
                    {
                        int savedDrawable = slot == 0 ? state.HatDrawable : state.GlassesDrawable;
                        int savedTexture = slot == 0 ? state.HatTexture : state.GlassesTexture;
                        if (savedDrawable >= 0) Function.Call(Hash.SET_PED_PROP_INDEX, player.Handle, slot, savedDrawable, savedTexture, true);
                        else ShowHelp("Aucun accessoire memorise pour ce personnage.");
                    }
                }
                SaveAccessories();
                Log("Accessory toggled character=" + key + " slot=" + selected + ".");
            }
            catch (Exception ex) { Log("Accessory toggle failed safely: " + ex.Message); }
        }

        private AccessoryState GetAccessoryState(string key)
        {
            AccessoryState state;
            if (!_accessories.TryGetValue(key, out state)) { state = new AccessoryState(); _accessories[key] = state; }
            return state;
        }

        private static bool IsMaskOn(Ped player) { try { return Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, player.Handle, 1) > 0; } catch { return false; } }
        private static bool IsPropOn(Ped player, int slot) { try { return Function.Call<int>(Hash.GET_PED_PROP_INDEX, player.Handle, slot) >= 0; } catch { return false; } }
        private static string CharacterKey(Ped player) { return player.Model.Hash.ToString(CultureInfo.InvariantCulture); }

        private static void UpdateRadialSelection(int count, ref int selection)
        {
            if (count <= 0) { selection = 0; return; }
            float x = ReadControlNormal(30), y = ReadControlNormal(31);
            if ((float)Math.Sqrt(x * x + y * y) >= 0.32f)
            {
                double angle = Math.Atan2(x, -y);
                if (angle < 0) angle += Math.PI * 2.0;
                selection = ((int)Math.Round(angle / (Math.PI * 2.0 / count))) % count;
                return;
            }
            if (ReadControlJustPressed(InputFrontendUp)) selection = 0;
            else if (ReadControlJustPressed(InputFrontendRight)) selection = Math.Min(count - 1, Math.Max(1, count / 3));
            else if (ReadControlJustPressed(InputFrontendDown)) selection = Math.Min(count - 1, Math.Max(1, count / 2));
            else if (ReadControlJustPressed(InputFrontendLeft)) selection = count - 1;
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
            string[][] known =
            {
                new[] { "WEAPON_PISTOL", "PISTOLET" }, new[] { "WEAPON_COMBATPISTOL", "PISTOLET COMBAT" }, new[] { "WEAPON_APPISTOL", "PISTOLET AUTO" },
                new[] { "WEAPON_MICROSMG", "MICRO PM" }, new[] { "WEAPON_SMG", "PM" }, new[] { "WEAPON_ASSAULTRIFLE", "FUSIL ASSAUT" },
                new[] { "WEAPON_CARBINERIFLE", "CARABINE" }, new[] { "WEAPON_PUMPSHOTGUN", "FUSIL A POMPE" }, new[] { "WEAPON_SNIPERRIFLE", "FUSIL PRECISION" },
                new[] { "WEAPON_KNIFE", "COUTEAU" }, new[] { "WEAPON_BAT", "BATTE" }, new[] { "WEAPON_GRENADE", "GRENADE" }, new[] { "WEAPON_MOLOTOV", "MOLOTOV" }
            };
            foreach (string[] pair in known) if (SafeHash(pair[0]) == hash) return pair[1];
            return "ARME";
        }

        private static string VehicleKey(Vehicle v)
        {
            string plate = string.Empty;
            try { plate = (Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, v.Handle) ?? string.Empty).Trim().ToUpperInvariant(); } catch { }
            return v.Model.Hash.ToString(CultureInfo.InvariantCulture) + ":" + plate;
        }

        private static bool IsMissionEntity(Entity e) { try { return Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, e.Handle); } catch { return true; } }
        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true; } catch { }
            try { return Function.Call<bool>(Hash.GET_MISSION_FLAG); } catch { return true; }
        }

        private static bool ReadControlJustPressed(int control)
        {
            try { return Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, control) || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, control); }
            catch { return false; }
        }

        private static float ReadControlNormal(int control)
        {
            try
            {
                float disabled = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, control);
                return Math.Abs(disabled) > 0.001f ? disabled : Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, control);
            }
            catch { return 0f; }
        }

        private static void DisableWheelControls()
        {
            int[] controls = { 23, 24, 25, 30, 31, 37, 44, 51, 140, 141, 142 };
            foreach (int control in controls) try { Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, control, true); } catch { }
        }

        private static void DrawTextCentered(float x, float y, string text, float scale, int red, int green, int blue, int alpha)
        {
            try
            {
                Function.Call(Hash.SET_TEXT_FONT, 0);
                Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
                Function.Call(Hash.SET_TEXT_COLOUR, red, green, blue, alpha);
                Function.Call(Hash.SET_TEXT_CENTRE, true);
                Function.Call(Hash.SET_TEXT_OUTLINE);
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y, 0);
            }
            catch { }
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
                    List<WeaponEntry> trunk = GetTrunk(p[0]);
                    if (trunk.Count < TrunkCapacity) trunk.Add(new WeaponEntry { Hash = hash, Ammo = Math.Max(0, ammo) });
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

        private void LoadAccessories()
        {
            if (!File.Exists(AccessoryPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(AccessoryPath))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 8) continue;
                    _accessories[p[0]] = new AccessoryState
                    {
                        HatDrawable = ParseInt(p[1]), HatTexture = ParseInt(p[2]), GlassesDrawable = ParseInt(p[3]), GlassesTexture = ParseInt(p[4]),
                        MaskDrawable = ParseInt(p[5]), MaskTexture = ParseInt(p[6]), MaskPalette = ParseInt(p[7])
                    };
                }
            }
            catch (Exception ex) { Log("Accessory load failed safely: " + ex.Message); }
        }

        private void SaveAccessories()
        {
            try
            {
                var lines = new List<string>();
                foreach (var pair in _accessories)
                {
                    AccessoryState s = pair.Value;
                    lines.Add(string.Join("|", pair.Key, s.HatDrawable, s.HatTexture, s.GlassesDrawable, s.GlassesTexture, s.MaskDrawable, s.MaskTexture, s.MaskPalette));
                }
                File.WriteAllLines(AccessoryPath, lines.ToArray());
            }
            catch (Exception ex) { Log("Accessory save failed safely: " + ex.Message); }
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
            _pendingVehicle = _pendingStarted = _pendingWeapon = 0;
            _pendingEntry = null;
            _transferAnimationStarted = false;
        }

        private void CloseTrunkWheel(bool clearRelease = true)
        {
            _trunkWheelOpen = false;
            _trunkWheelVehicle = _trunkWheelSelection = _trunkWheelOpenedAt = 0;
            _wheelActions.Clear();
            if (clearRelease) _trunkReleasePending = false;
        }

        private void CloseAccessoryWheel()
        {
            _accessoryWheelOpen = false;
            _accessorySelection = _accessoryOpenedAt = 0;
            _accessoryReleasePending = false;
        }

        private void ClearInputRequests()
        {
            _trunkOpenRequested = _trunkReleasePending = _accessoryOpenRequested = _accessoryReleasePending = false;
        }

        private void ResetAll()
        {
            Ped player = Game.LocalPlayerPed;
            Vehicle vehicle = player != null && player.Exists() ? FindVehicleByHandle(player.Position, 6.0f, _pendingVehicle) : null;
            ResetTransfer(player, vehicle);
            CloseTrunkWheel();
            CloseAccessoryWheel();
            ClearInputRequests();
            _trunkKeyDown = _accessoryKeyDown = false;
        }

        private static int ParseInt(string value) { int parsed; return int.TryParse(value, out parsed) ? parsed : 0; }
        private static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        private void OnAborted(object sender, EventArgs e) { Save(); SaveAccessories(); ResetAll(); }
        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); }
            catch { }
        }
    }
}
