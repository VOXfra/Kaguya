using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace VOX.InventoryRuntimeVI
{
    public sealed class InventoryRuntimeVISatchelScript : Script
    {
        private const string DataDir = "scripts\\InventoryRuntimeVI";
        private const string SatchelWeaponsPath = DataDir + "\\SatchelWeapons.txt";
        private const string SatchelItemsPath = DataDir + "\\SatchelItems.txt";
        private const string AccessoryPath = DataDir + "\\AccessoryState.txt";
        private const string OutfitPath = DataDir + "\\VehicleOutfits.txt";
        private const string OwnedAccessoryPath = DataDir + "\\OwnedWeaponAccessories.txt";

        private const int SelectWeapon = 37;   // TAB / LB
        private const int Cover = 44;          // Q / RB -- used only while weapon wheel is held
        private const int Jump = 22;           // SPACE / X -- craft max while craft page is active
        private const int WheelUD = 12;
        private const int WheelLR = 13;
        private const int DpadUp = 172;
        private const int DpadDown = 173;
        private const int DpadLeft = 174;
        private const int DpadRight = 175;
        private const int WeaponWheelHud = 19;

        private sealed class StoredWeapon
        {
            public int Hash;
            public int Ammo;
            public int Tint;
            public readonly List<int> Components = new List<int>();
        }

        private sealed class Items
        {
            public int Rag;
            public int Alcohol;
            public int Bottle;
        }

        private sealed class AccessoryState
        {
            public int MaskDrawable, MaskTexture, MaskPalette;
            public int HatDrawable = -1, HatTexture;
            public int GlassesDrawable = -1, GlassesTexture;
        }

        private sealed class OutfitState
        {
            public readonly int[] Drawables = new int[12];
            public readonly int[] Textures = new int[12];
            public readonly int[] Palettes = new int[12];
            public readonly int[] Props = Enumerable.Repeat(-1, 8).ToArray();
            public readonly int[] PropTextures = new int[8];
        }

        private static readonly string[] Sidearms =
        {
            "WEAPON_PISTOL", "WEAPON_PISTOL_MK2", "WEAPON_COMBATPISTOL", "WEAPON_APPISTOL", "WEAPON_PISTOL50",
            "WEAPON_SNSPISTOL", "WEAPON_SNSPISTOL_MK2", "WEAPON_HEAVYPISTOL", "WEAPON_VINTAGEPISTOL", "WEAPON_MARKSMANPISTOL",
            "WEAPON_REVOLVER", "WEAPON_REVOLVER_MK2", "WEAPON_DOUBLEACTION", "WEAPON_NAVYREVOLVER", "WEAPON_CERAMICPISTOL",
            "WEAPON_GADGETPISTOL", "WEAPON_PISTOLXM3", "WEAPON_STUNGUN"
        };

        private static readonly string[] LongGuns =
        {
            "WEAPON_MICROSMG", "WEAPON_SMG", "WEAPON_SMG_MK2", "WEAPON_ASSAULTSMG", "WEAPON_COMBATPDW", "WEAPON_MACHINEPISTOL", "WEAPON_MINISMG",
            "WEAPON_ASSAULTRIFLE", "WEAPON_ASSAULTRIFLE_MK2", "WEAPON_CARBINERIFLE", "WEAPON_CARBINERIFLE_MK2", "WEAPON_ADVANCEDRIFLE",
            "WEAPON_SPECIALCARBINE", "WEAPON_SPECIALCARBINE_MK2", "WEAPON_BULLPUPRIFLE", "WEAPON_BULLPUPRIFLE_MK2", "WEAPON_COMPACTRIFLE", "WEAPON_TACTICALRIFLE", "WEAPON_BATTLERIFLE", "WEAPON_MILITARYRIFLE", "WEAPON_HEAVYRIFLE",
            "WEAPON_PUMPSHOTGUN", "WEAPON_PUMPSHOTGUN_MK2", "WEAPON_SAWNOFFSHOTGUN", "WEAPON_ASSAULTSHOTGUN", "WEAPON_BULLPUPSHOTGUN", "WEAPON_HEAVYSHOTGUN", "WEAPON_COMBATSHOTGUN", "WEAPON_DBSHOTGUN", "WEAPON_AUTOSHOTGUN",
            "WEAPON_SNIPERRIFLE", "WEAPON_HEAVYSNIPER", "WEAPON_HEAVYSNIPER_MK2", "WEAPON_MARKSMANRIFLE", "WEAPON_MARKSMANRIFLE_MK2", "WEAPON_PRECISIONRIFLE",
            "WEAPON_MG", "WEAPON_COMBATMG", "WEAPON_COMBATMG_MK2", "WEAPON_GUSENBERG"
        };

        private static readonly string[] ComponentNames =
        {
            "COMPONENT_AT_PI_FLSH", "COMPONENT_AT_PI_FLSH_02", "COMPONENT_AT_PI_FLSH_03", "COMPONENT_AT_AR_FLSH",
            "COMPONENT_AT_AR_AFGRIP", "COMPONENT_AT_AR_AFGRIP_02", "COMPONENT_AT_SCOPE_MACRO", "COMPONENT_AT_SCOPE_MACRO_02",
            "COMPONENT_AT_SCOPE_SMALL", "COMPONENT_AT_SCOPE_SMALL_02", "COMPONENT_AT_SCOPE_MEDIUM", "COMPONENT_AT_SCOPE_LARGE",
            "COMPONENT_AT_SCOPE_LARGE_FIXED_ZOOM", "COMPONENT_AT_SCOPE_MAX", "COMPONENT_AT_PI_SUPP", "COMPONENT_AT_PI_SUPP_02",
            "COMPONENT_AT_AR_SUPP", "COMPONENT_AT_AR_SUPP_02", "COMPONENT_AT_SR_SUPP", "COMPONENT_AT_SR_SUPP_03"
        };

        private static readonly string[] SuppressorNames =
        {
            "COMPONENT_AT_PI_SUPP", "COMPONENT_AT_PI_SUPP_02", "COMPONENT_AT_AR_SUPP", "COMPONENT_AT_AR_SUPP_02", "COMPONENT_AT_SR_SUPP", "COMPONENT_AT_SR_SUPP_03"
        };

        // Vanilla general-store cashier positions; this is intentionally small and
        // world-bound. Crafting ingredients are bought in the world, not from a mod menu.
        private static readonly Vector3[] GeneralStores =
        {
            new Vector3(25.06f,-1347.32f,29.50f), new Vector3(-3039.18f,585.13f,7.91f),
            new Vector3(-3242.20f,1000.00f,12.83f), new Vector3(1728.78f,6414.41f,35.04f),
            new Vector3(1698.31f,4924.31f,42.06f), new Vector3(1961.46f,3740.67f,32.34f),
            new Vector3(548.12f,2669.45f,42.16f), new Vector3(2678.85f,3280.17f,55.24f),
            new Vector3(2557.30f,380.75f,108.62f), new Vector3(373.80f,326.18f,103.57f)
        };

        private readonly Dictionary<string, List<StoredWeapon>> _satchelWeapons = new Dictionary<string, List<StoredWeapon>>();
        private readonly Dictionary<string, Items> _items = new Dictionary<string, Items>();
        private readonly Dictionary<string, AccessoryState> _accessories = new Dictionary<string, AccessoryState>();
        private readonly Dictionary<string, OutfitState> _outfits = new Dictionary<string, OutfitState>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ownedWeaponAccessories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _wheelWasDown;
        private bool _coverWasDown;
        private bool _jumpWasDown;
        private int _customPage;
        private int _selected = -1;
        private int _lastCarryScan;
        private int _lastAccessoryProbe;
        private int _lastSave;
        private int _storyYieldUntil;

        public InventoryRuntimeVISatchelScript()
        {
            Directory.CreateDirectory(DataDir);
            LoadAll();
            Interval = 0;
            Tick += OnTick;
            Aborted += OnAborted;
            InventoryRuntimeVIScript.Log("Inventory Runtime VI satchel 0.4.0 loaded: LB/TAB wheel extension, controller parity, carry limits and world-bound crafting.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { ResetWheel(); return; }
                if (InventoryRuntimeVIScript.RockstarOwnsScene())
                {
                    _storyYieldUntil = Game.GameTime + 5000;
                    ResetWheel();
                    return;
                }
                if (Game.GameTime < _storyYieldUntil) { ResetWheel(); return; }

                int now = Game.GameTime;
                if (!player.IsInVehicle() && now - _lastCarryScan >= 1600)
                {
                    _lastCarryScan = now;
                    EnforceCarryLimits(player);
                }
                if (!player.IsInVehicle() && now - _lastAccessoryProbe >= 1000)
                {
                    _lastAccessoryProbe = now;
                    RememberInstalledSuppressor(player);
                }

                UpdateExtendedWeaponWheel(player);

                if (now - _lastSave >= 10000)
                {
                    _lastSave = now;
                    SaveAll();
                }
            }
            catch (Exception ex) { InventoryRuntimeVIScript.Log("Satchel tick error: " + ex); }
        }

        private void UpdateExtendedWeaponWheel(Ped player)
        {
            bool wheel = Pressed(SelectWeapon);
            bool cover = Pressed(Cover);
            bool jump = Pressed(Jump);
            bool coverJust = cover && !_coverWasDown;
            bool jumpJust = jump && !_jumpWasDown;

            if (wheel && coverJust)
            {
                _customPage++;
                if (_customPage > 4) _customPage = 0;
                _selected = -1;
            }

            if (_customPage > 0 && wheel)
            {
                SuppressVanillaWheelControls();
                _selected = ResolveSelection();
                DrawWheel(player, _customPage, _selected);
                if (_customPage == 3 && jumpJust && !NearGeneralStore(player.Position)) CraftMolotov(player, true);
            }

            if (!wheel && _wheelWasDown && _customPage > 0)
            {
                if (_selected >= 0) ExecuteSelection(player, _customPage, _selected);
                ResetWheel();
            }
            else if (!wheel && !_wheelWasDown) _customPage = 0;

            _wheelWasDown = wheel;
            _coverWasDown = cover;
            _jumpWasDown = jump;
        }

        private static void SuppressVanillaWheelControls()
        {
            try
            {
                Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, WeaponWheelHud);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, SelectWeapon, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, Cover, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, Jump, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, DpadUp, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, DpadDown, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, DpadLeft, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, DpadRight, true);
            }
            catch { }
        }

        private int ResolveSelection()
        {
            if (JustPressedDisabled(DpadUp)) return 0;
            if (JustPressedDisabled(DpadRight)) return 2;
            if (JustPressedDisabled(DpadDown)) return 4;
            if (JustPressedDisabled(DpadLeft)) return 6;

            float x = ControlNormal(WheelLR);
            float y = ControlNormal(WheelUD);
            float mag = (float)Math.Sqrt(x * x + y * y);
            if (mag < 0.38f) return _selected;
            double angle = Math.Atan2(x, -y);
            if (angle < 0) angle += Math.PI * 2.0;
            return ((int)Math.Round(angle / (Math.PI * 2.0) * 8.0)) % 8;
        }

        private void DrawWheel(Ped player, int page, int selected)
        {
            string title = page == 1 ? "ACCESSOIRES" : page == 2 ? "SACOCHE" : page == 3 ? (NearGeneralStore(player.Position) ? "FOURNITURES" : "CRAFT") : "COFFRE / TENUE";
            DrawText(0.50f, 0.36f, title, 0.34f, true);
            DrawText(0.50f, 0.405f, "LB/TAB maintenu + RB/Q : page suivante", 0.22f, true);

            string[] labels = LabelsForPage(player, page);
            for (int i = 0; i < 8; i++)
            {
                if (string.IsNullOrEmpty(labels[i])) continue;
                double a = i * Math.PI * 2.0 / 8.0 - Math.PI / 2.0;
                float x = 0.50f + (float)Math.Cos(a) * 0.145f;
                float y = 0.58f + (float)Math.Sin(a) * 0.135f;
                if (i == selected)
                {
                    try { Function.Call(Hash.DRAW_RECT, x, y + 0.012f, 0.125f, 0.040f, 20, 20, 20, 180, false); } catch { }
                }
                DrawText(x, y, labels[i], i == selected ? 0.29f : 0.24f, true);
            }
            if (page == 3 && !NearGeneralStore(player.Position))
            {
                Items it = GetItems(CharacterKey(player));
                DrawText(0.50f, 0.76f, "Chiffon " + it.Rag + "   Alcool " + it.Alcohol + "   Bouteille " + it.Bottle + "   |   X/ESPACE = fabriquer max", 0.22f, true);
            }
        }

        private string[] LabelsForPage(Ped player, int page)
        {
            var labels = new string[8];
            if (page == 1)
            {
                labels[0] = "Masque";
                labels[2] = "Lunettes";
                labels[4] = "Chapeau";
                labels[6] = "Silencieux";
            }
            else if (page == 2)
            {
                List<StoredWeapon> list = GetSatchel(CharacterKey(player));
                for (int i = 0; i < Math.Min(8, list.Count); i++) labels[i] = WeaponLabel(list[i].Hash);
                if (list.Count == 0) labels[0] = "Sacoche vide";
            }
            else if (page == 3)
            {
                if (NearGeneralStore(player.Position))
                {
                    labels[0] = "Chiffon $8";
                    labels[2] = "Alcool $18";
                    labels[4] = "Bouteille $5";
                }
                else labels[0] = "Molotov";
            }
            else
            {
                Vehicle trunk = RearVehicle(player.Position, 2.45f);
                if (trunk != null && trunk.Exists() && !InventoryRuntimeVIScript.IsMissionEntity(trunk))
                {
                    labels[6] = "Sauver tenue";
                    labels[2] = "Porter tenue";
                }
                else labels[0] = "Approcher un coffre";
            }
            return labels;
        }

        private void ExecuteSelection(Ped player, int page, int index)
        {
            if (player == null || !player.Exists() || player.IsInVehicle()) return;
            if (page == 1)
            {
                if (index == 0) ToggleMask(player);
                else if (index == 2) ToggleProp(player, false);
                else if (index == 4) ToggleProp(player, true);
                else if (index == 6) ToggleSuppressor(player);
            }
            else if (page == 2)
            {
                List<StoredWeapon> list = GetSatchel(CharacterKey(player));
                if (index >= 0 && index < list.Count) EquipFromSatchel(player, index);
            }
            else if (page == 3)
            {
                if (NearGeneralStore(player.Position))
                {
                    if (index == 0) BuyItem(player, "rag", 8);
                    else if (index == 2) BuyItem(player, "alcohol", 18);
                    else if (index == 4) BuyItem(player, "bottle", 5);
                }
                else if (index == 0) CraftMolotov(player, false);
            }
            else if (page == 4)
            {
                Vehicle trunk = RearVehicle(player.Position, 2.45f);
                if (trunk == null || !trunk.Exists() || InventoryRuntimeVIScript.IsMissionEntity(trunk)) return;
                if (index == 6) SaveOutfit(player, trunk);
                else if (index == 2) RestoreOutfit(player, trunk);
            }
        }

        private void EnforceCarryLimits(Ped player)
        {
            if (player == null || !player.Exists() || player.IsInVehicle()) return;
            EnforceCategory(player, Sidearms, 2);
            EnforceCategory(player, LongGuns, 2);
        }

        private void EnforceCategory(Ped player, string[] names, int limit)
        {
            int current = SafeSelectedWeapon(player);
            var owned = new List<int>();
            foreach (string n in names)
            {
                int h = InventoryRuntimeVIScript.SafeHash(n);
                if (h == 0) continue;
                try { if (Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, h, false)) owned.Add(h); } catch { }
            }
            if (owned.Count <= limit) return;

            var keep = new HashSet<int>();
            if (owned.Contains(current)) keep.Add(current);
            foreach (int h in owned) { if (keep.Count >= limit) break; keep.Add(h); }

            string key = CharacterKey(player);
            List<StoredWeapon> satchel = GetSatchel(key);
            foreach (int h in owned)
            {
                if (keep.Contains(h)) continue;
                StoredWeapon snapshot = Capture(player, h);
                if (snapshot == null) continue;
                UpsertSatchel(satchel, snapshot);
                try { Function.Call(Hash.REMOVE_WEAPON_FROM_PED, player.Handle, h); } catch { }
                InventoryRuntimeVIScript.Log("Carry limit moved weapon " + h + " into protagonist satchel.");
            }
            SaveSatchelWeapons();
        }

        private void EquipFromSatchel(Ped player, int index)
        {
            string key = CharacterKey(player);
            List<StoredWeapon> satchel = GetSatchel(key);
            if (index < 0 || index >= satchel.Count) return;
            StoredWeapon chosen = satchel[index];
            string[] category = InCategory(chosen.Hash, Sidearms) ? Sidearms : (InCategory(chosen.Hash, LongGuns) ? LongGuns : null);
            int limit = category == Sidearms ? 2 : 2;
            if (category != null)
            {
                var carried = OwnedInCategory(player, category);
                if (carried.Count >= limit)
                {
                    int current = SafeSelectedWeapon(player);
                    int swap = carried.FirstOrDefault(h => h != current);
                    if (swap == 0) swap = carried[0];
                    StoredWeapon moved = Capture(player, swap);
                    if (moved != null)
                    {
                        UpsertSatchel(satchel, moved);
                        try { Function.Call(Hash.REMOVE_WEAPON_FROM_PED, player.Handle, swap); } catch { }
                    }
                }
            }
            Restore(player, chosen, true);
            satchel.RemoveAll(w => w.Hash == chosen.Hash);
            SaveSatchelWeapons();
            InventoryRuntimeVIScript.Log("Satchel equipped weapon " + chosen.Hash + " with preserved components.");
        }

        private List<int> OwnedInCategory(Ped player, string[] names)
        {
            var result = new List<int>();
            foreach (string n in names)
            {
                int h = InventoryRuntimeVIScript.SafeHash(n);
                try { if (h != 0 && Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, h, false)) result.Add(h); } catch { }
            }
            return result;
        }

        private static bool InCategory(int hash, string[] names)
        {
            foreach (string n in names) if (InventoryRuntimeVIScript.SafeHash(n) == hash) return true;
            return false;
        }

        private void ToggleMask(Ped player)
        {
            AccessoryState a = GetAccessory(CharacterKey(player));
            try
            {
                int d = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, player.Handle, 1);
                if (d > 0)
                {
                    a.MaskDrawable = d;
                    a.MaskTexture = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, player.Handle, 1);
                    a.MaskPalette = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, player.Handle, 1);
                    Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle, 1, 0, 0, 0);
                }
                else if (a.MaskDrawable > 0) Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle, 1, a.MaskDrawable, a.MaskTexture, a.MaskPalette);
                SaveAccessoryState();
            }
            catch { }
        }

        private void ToggleProp(Ped player, bool hat)
        {
            AccessoryState a = GetAccessory(CharacterKey(player));
            int prop = hat ? 0 : 1;
            try
            {
                int d = Function.Call<int>(Hash.GET_PED_PROP_INDEX, player.Handle, prop);
                if (d >= 0)
                {
                    int t = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, player.Handle, prop);
                    if (hat) { a.HatDrawable = d; a.HatTexture = t; }
                    else { a.GlassesDrawable = d; a.GlassesTexture = t; }
                    Function.Call(Hash.CLEAR_PED_PROP, player.Handle, prop);
                }
                else
                {
                    int savedD = hat ? a.HatDrawable : a.GlassesDrawable;
                    int savedT = hat ? a.HatTexture : a.GlassesTexture;
                    if (savedD >= 0) Function.Call(Hash.SET_PED_PROP_INDEX, player.Handle, prop, savedD, savedT, true);
                }
                SaveAccessoryState();
            }
            catch { }
        }

        private void RememberInstalledSuppressor(Ped player)
        {
            int weapon = SafeSelectedWeapon(player);
            if (!InventoryRuntimeVIScript.IsArmed(weapon)) return;
            foreach (string n in SuppressorNames)
            {
                int c = InventoryRuntimeVIScript.SafeHash(n);
                try
                {
                    if (c != 0 && Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON_COMPONENT, player.Handle, weapon, c))
                        _ownedWeaponAccessories.Add(CharacterKey(player) + ":" + weapon + ":" + c);
                }
                catch { }
            }
        }

        private void ToggleSuppressor(Ped player)
        {
            int weapon = SafeSelectedWeapon(player);
            if (!InventoryRuntimeVIScript.IsArmed(weapon)) return;
            string charKey = CharacterKey(player);
            foreach (string n in SuppressorNames)
            {
                int c = InventoryRuntimeVIScript.SafeHash(n);
                if (c == 0) continue;
                bool installed = false;
                try { installed = Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON_COMPONENT, player.Handle, weapon, c); } catch { }
                string key = charKey + ":" + weapon + ":" + c;
                if (installed)
                {
                    _ownedWeaponAccessories.Add(key);
                    try { Function.Call(Hash.REMOVE_WEAPON_COMPONENT_FROM_PED, player.Handle, weapon, c); } catch { }
                    SaveOwnedAccessories();
                    return;
                }
            }
            foreach (string n in SuppressorNames)
            {
                int c = InventoryRuntimeVIScript.SafeHash(n);
                string key = charKey + ":" + weapon + ":" + c;
                if (!_ownedWeaponAccessories.Contains(key)) continue;
                try { Function.Call(Hash.GIVE_WEAPON_COMPONENT_TO_PED, player.Handle, weapon, c); } catch { }
                return;
            }
        }

        private void BuyItem(Ped player, string item, int price)
        {
            try
            {
                if (Game.Player.Money < price) { Notify("Pas assez d'argent."); return; }
                Game.Player.Money -= price;
                Items it = GetItems(CharacterKey(player));
                if (item == "rag") it.Rag++;
                else if (item == "alcohol") it.Alcohol++;
                else if (item == "bottle") it.Bottle++;
                SaveItems();
                Notify(item == "rag" ? "Chiffon achete." : item == "alcohol" ? "Alcool achete." : "Bouteille achetee.");
            }
            catch { }
        }

        private void CraftMolotov(Ped player, bool max)
        {
            Items it = GetItems(CharacterKey(player));
            int possible = Math.Min(it.Rag, Math.Min(it.Alcohol, it.Bottle));
            if (possible <= 0) { if (!max) Notify("Molotov : chiffon + alcool + bouteille requis."); return; }
            int count = max ? possible : 1;
            it.Rag -= count; it.Alcohol -= count; it.Bottle -= count;
            int molotov = InventoryRuntimeVIScript.SafeHash("WEAPON_MOLOTOV");
            try { Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, molotov, count, false, false); } catch { }
            SaveItems();
            Notify(count == 1 ? "Molotov fabrique." : count + " Molotov fabriques.");
        }

        private void SaveOutfit(Ped player, Vehicle vehicle)
        {
            var o = new OutfitState();
            try
            {
                for (int i = 0; i < 12; i++)
                {
                    o.Drawables[i] = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, player.Handle, i);
                    o.Textures[i] = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, player.Handle, i);
                    o.Palettes[i] = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, player.Handle, i);
                }
                for (int i = 0; i < 8; i++)
                {
                    o.Props[i] = Function.Call<int>(Hash.GET_PED_PROP_INDEX, player.Handle, i);
                    if (o.Props[i] >= 0) o.PropTextures[i] = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, player.Handle, i);
                }
                _outfits[InventoryRuntimeVIScript.VehicleKey(vehicle)] = o;
                SaveOutfits();
                Notify("Tenue rangee dans le vehicule.");
            }
            catch { }
        }

        private void RestoreOutfit(Ped player, Vehicle vehicle)
        {
            OutfitState o;
            if (!_outfits.TryGetValue(InventoryRuntimeVIScript.VehicleKey(vehicle), out o)) { Notify("Aucune tenue rangee ici."); return; }
            try
            {
                for (int i = 0; i < 12; i++) Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle, i, o.Drawables[i], o.Textures[i], o.Palettes[i]);
                for (int i = 0; i < 8; i++)
                {
                    if (o.Props[i] < 0) Function.Call(Hash.CLEAR_PED_PROP, player.Handle, i);
                    else Function.Call(Hash.SET_PED_PROP_INDEX, player.Handle, i, o.Props[i], o.PropTextures[i], true);
                }
                Notify("Tenue changee depuis le coffre.");
            }
            catch { }
        }

        private static Vehicle RearVehicle(Vector3 pos, float radius)
        {
            Vehicle[] vehicles;
            try { vehicles = World.GetNearbyVehicles(pos, radius + 1.5f); } catch { return null; }
            Vehicle best = null; float bestD = radius;
            foreach (Vehicle v in vehicles)
            {
                if (v == null || !v.Exists()) continue;
                float d = InventoryRuntimeVIScript.Distance(pos, InventoryRuntimeVIScript.RearPoint(v));
                if (d < bestD) { bestD = d; best = v; }
            }
            return best;
        }

        private static bool NearGeneralStore(Vector3 p)
        {
            foreach (Vector3 s in GeneralStores) if (InventoryRuntimeVIScript.Distance(p, s) <= 2.0f) return true;
            return false;
        }

        private static StoredWeapon Capture(Ped player, int weapon)
        {
            if (player == null || !player.Exists() || !InventoryRuntimeVIScript.IsArmed(weapon)) return null;
            var w = new StoredWeapon { Hash = weapon };
            try { w.Ammo = Math.Max(0, Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, player.Handle, weapon)); } catch { }
            try { w.Tint = Math.Max(0, Function.Call<int>(Hash.GET_PED_WEAPON_TINT_INDEX, player.Handle, weapon)); } catch { }
            foreach (string n in ComponentNames)
            {
                int c = InventoryRuntimeVIScript.SafeHash(n);
                try { if (c != 0 && Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON_COMPONENT, player.Handle, weapon, c)) w.Components.Add(c); } catch { }
            }
            return w;
        }

        private static void Restore(Ped player, StoredWeapon w, bool equip)
        {
            if (player == null || !player.Exists() || w == null) return;
            try { Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, w.Hash, Math.Max(0, w.Ammo), false, equip); } catch { }
            try { Function.Call(Hash.SET_PED_WEAPON_TINT_INDEX, player.Handle, w.Hash, w.Tint); } catch { }
            foreach (int c in w.Components) { try { Function.Call(Hash.GIVE_WEAPON_COMPONENT_TO_PED, player.Handle, w.Hash, c); } catch { } }
        }

        private static void UpsertSatchel(List<StoredWeapon> list, StoredWeapon w)
        {
            list.RemoveAll(x => x.Hash == w.Hash);
            list.Add(w);
        }

        private List<StoredWeapon> GetSatchel(string key)
        {
            List<StoredWeapon> list;
            if (!_satchelWeapons.TryGetValue(key, out list)) { list = new List<StoredWeapon>(); _satchelWeapons[key] = list; }
            return list;
        }

        private Items GetItems(string key)
        {
            Items it;
            if (!_items.TryGetValue(key, out it)) { it = new Items(); _items[key] = it; }
            return it;
        }

        private AccessoryState GetAccessory(string key)
        {
            AccessoryState a;
            if (!_accessories.TryGetValue(key, out a)) { a = new AccessoryState(); _accessories[key] = a; }
            return a;
        }

        private static string CharacterKey(Ped player) { return player.Model.Hash.ToString(CultureInfo.InvariantCulture); }
        private static int SafeSelectedWeapon(Ped p) { try { return Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON, p.Handle); } catch { return 0; } }
        private static bool Pressed(int c) { try { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, c) || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, c); } catch { return false; } }
        private static bool JustPressedDisabled(int c) { try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, c); } catch { return false; } }
        private static float ControlNormal(int c) { try { float v = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, c); if (Math.Abs(v) > 0.001f) return v; return Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, c); } catch { return 0f; } }

        private static string WeaponLabel(int h)
        {
            foreach (string n in Sidearms) if (InventoryRuntimeVIScript.SafeHash(n) == h) return PrettyWeaponName(n);
            foreach (string n in LongGuns) if (InventoryRuntimeVIScript.SafeHash(n) == h) return PrettyWeaponName(n);
            return "Arme";
        }
        private static string PrettyWeaponName(string n)
        {
            string s = n.Replace("WEAPON_", string.Empty).Replace("_MK2", " Mk II").Replace("_", " ").ToLowerInvariant();
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s);
        }

        private static void DrawText(float x, float y, string text, float scale, bool centered)
        {
            try
            {
                Function.Call(Hash.SET_TEXT_FONT, 0);
                Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
                Function.Call(Hash.SET_TEXT_COLOUR, 255,255,255,235);
                Function.Call(Hash.SET_TEXT_OUTLINE);
                Function.Call(Hash.SET_TEXT_CENTRE, centered);
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y, 0);
            }
            catch { }
        }

        private static void Notify(string text)
        {
            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, false);
            }
            catch { }
        }

        private void LoadAll()
        {
            LoadSatchelWeapons(); LoadItems(); LoadAccessories(); LoadOutfits(); LoadOwnedAccessories();
        }

        private void SaveAll()
        {
            SaveSatchelWeapons(); SaveItems(); SaveAccessoryState(); SaveOutfits(); SaveOwnedAccessories();
        }

        private void LoadSatchelWeapons()
        {
            if (!File.Exists(SatchelWeaponsPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(SatchelWeaponsPath))
                {
                    string[] p = line.Split('|'); if (p.Length < 5) continue;
                    int hash, ammo, tint; if (!int.TryParse(p[1], out hash) || !int.TryParse(p[2], out ammo) || !int.TryParse(p[3], out tint)) continue;
                    var w = new StoredWeapon { Hash = hash, Ammo = ammo, Tint = tint };
                    foreach (string c in p[4].Split(',')) { int h; if (int.TryParse(c, out h) && h != 0) w.Components.Add(h); }
                    GetSatchel(p[0]).Add(w);
                }
            }
            catch { }
        }

        private void SaveSatchelWeapons()
        {
            try
            {
                var lines = new List<string>();
                foreach (var pair in _satchelWeapons)
                    foreach (StoredWeapon w in pair.Value)
                        lines.Add(pair.Key + "|" + w.Hash + "|" + w.Ammo + "|" + w.Tint + "|" + string.Join(",", w.Components.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToArray()));
                File.WriteAllLines(SatchelWeaponsPath, lines.ToArray());
            }
            catch { }
        }

        private void LoadItems()
        {
            if (!File.Exists(SatchelItemsPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(SatchelItemsPath))
                {
                    string[] p = line.Split('|'); if (p.Length < 4) continue;
                    _items[p[0]] = new Items { Rag = Parse(p[1]), Alcohol = Parse(p[2]), Bottle = Parse(p[3]) };
                }
            }
            catch { }
        }

        private void SaveItems()
        {
            try
            {
                File.WriteAllLines(SatchelItemsPath, _items.Select(x => x.Key + "|" + x.Value.Rag + "|" + x.Value.Alcohol + "|" + x.Value.Bottle).ToArray());
            }
            catch { }
        }

        private void LoadAccessories()
        {
            if (!File.Exists(AccessoryPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(AccessoryPath))
                {
                    string[] p = line.Split('|'); if (p.Length < 8) continue;
                    _accessories[p[0]] = new AccessoryState { MaskDrawable=Parse(p[1]),MaskTexture=Parse(p[2]),MaskPalette=Parse(p[3]),HatDrawable=Parse(p[4]),HatTexture=Parse(p[5]),GlassesDrawable=Parse(p[6]),GlassesTexture=Parse(p[7]) };
                }
            }
            catch { }
        }

        private void SaveAccessoryState()
        {
            try
            {
                File.WriteAllLines(AccessoryPath, _accessories.Select(x => x.Key + "|" + x.Value.MaskDrawable + "|" + x.Value.MaskTexture + "|" + x.Value.MaskPalette + "|" + x.Value.HatDrawable + "|" + x.Value.HatTexture + "|" + x.Value.GlassesDrawable + "|" + x.Value.GlassesTexture).ToArray());
            }
            catch { }
        }

        private void LoadOutfits()
        {
            if (!File.Exists(OutfitPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(OutfitPath))
                {
                    string[] p = line.Split('|'); if (p.Length < 3) continue;
                    var o = new OutfitState();
                    string[] comps = p[1].Split(',');
                    for (int i=0;i<Math.Min(12,comps.Length);i++) { string[] q=comps[i].Split(':'); if(q.Length>=3){o.Drawables[i]=Parse(q[0]);o.Textures[i]=Parse(q[1]);o.Palettes[i]=Parse(q[2]);} }
                    string[] props = p[2].Split(',');
                    for (int i=0;i<Math.Min(8,props.Length);i++) { string[] q=props[i].Split(':'); if(q.Length>=2){o.Props[i]=Parse(q[0]);o.PropTextures[i]=Parse(q[1]);} }
                    _outfits[p[0]] = o;
                }
            }
            catch { }
        }

        private void SaveOutfits()
        {
            try
            {
                var lines = new List<string>();
                foreach (var pair in _outfits)
                {
                    OutfitState o = pair.Value;
                    string comps = string.Join(",", Enumerable.Range(0,12).Select(i => o.Drawables[i] + ":" + o.Textures[i] + ":" + o.Palettes[i]).ToArray());
                    string props = string.Join(",", Enumerable.Range(0,8).Select(i => o.Props[i] + ":" + o.PropTextures[i]).ToArray());
                    lines.Add(pair.Key + "|" + comps + "|" + props);
                }
                File.WriteAllLines(OutfitPath, lines.ToArray());
            }
            catch { }
        }

        private void LoadOwnedAccessories()
        {
            if (!File.Exists(OwnedAccessoryPath)) return;
            try { foreach (string line in File.ReadAllLines(OwnedAccessoryPath)) if (!string.IsNullOrWhiteSpace(line)) _ownedWeaponAccessories.Add(line.Trim()); }
            catch { }
        }

        private void SaveOwnedAccessories()
        {
            try { File.WriteAllLines(OwnedAccessoryPath, _ownedWeaponAccessories.ToArray()); } catch { }
        }

        private static int Parse(string s) { int v; return int.TryParse(s, out v) ? v : 0; }
        private void ResetWheel() { _customPage = 0; _selected = -1; _wheelWasDown = Pressed(SelectWeapon); _coverWasDown = Pressed(Cover); _jumpWasDown = Pressed(Jump); }
        private void OnAborted(object sender, EventArgs e) { SaveAll(); ResetWheel(); }
    }
}
