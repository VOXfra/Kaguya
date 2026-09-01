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
    public sealed class InventoryRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\InventoryRuntimeVI";
        private const string LogPath = DataDir + "\\InventoryRuntimeVI.log";
        private const string TrunkPath = DataDir + "\\TrunkInventory.txt";
        private const string ArmoryPath = DataDir + "\\OwnedArmory.txt";
        private const string LoadoutPath = DataDir + "\\Loadout.txt";
        private const string MaterialPath = DataDir + "\\Materials.txt";
        private const string OutfitPath = DataDir + "\\VehicleOutfits.txt";
        private const string AccessoryPath = DataDir + "\\AccessoryState.txt";
        private const string LegacySatchelPath = DataDir + "\\SatchelWeapons.txt";

        private const int Context = 51;
        private const int SelectWeapon = 37;
        private const int Reload = 45;
        private const int FrontendUp = 188;
        private const int FrontendDown = 187;
        private const int FrontendAccept = 191;
        private const int FrontendCancel = 194;
        private const int Attack = 24;
        private const int Aim = 25;
        private const int TrunkCapacity = 8;

        private enum WeaponSlot { None = -1, LongGun = 0, Sidearm = 1, Melee = 2, Throwable = 3 }

        private sealed class WeaponDef
        {
            public string Name;
            public string Label;
            public WeaponSlot Slot;
            public WeaponDef(string name, string label, WeaponSlot slot) { Name = name; Label = label; Slot = slot; }
        }

        private sealed class WeaponEntry
        {
            public int Hash;
            public int Ammo;
            public int Tint;
            public readonly List<int> Components = new List<int>();
        }

        private sealed class MaterialState { public int Cloth; public int Alcohol; }

        private sealed class OutfitState
        {
            public string Name = string.Empty;
            public readonly int[] Drawables = new int[12];
            public readonly int[] Textures = new int[12];
            public readonly int[] Palettes = new int[12];
            public readonly int[] Props = Enumerable.Repeat(-1, 8).ToArray();
            public readonly int[] PropTextures = new int[8];
        }

        private sealed class AccessoryState
        {
            public int MaskDrawable;
            public int MaskTexture;
            public int MaskPalette;
            public int HatDrawable = -1;
            public int HatTexture;
            public int GlassesDrawable = -1;
            public int GlassesTexture;
        }

        private sealed class MenuEntry
        {
            public string Label = string.Empty;
            public string Kind = string.Empty;
            public int Index = -1;
        }

        private sealed class PendingTransfer
        {
            public string Kind = string.Empty;
            public int VehicleHandle;
            public int WeaponIndex = -1;
            public int WeaponHash;
            public int ExecuteAt;
        }

        private static readonly WeaponDef[] Weapons =
        {
            new WeaponDef("WEAPON_PISTOL","Pistolet",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_PISTOL_MK2","Pistolet Mk II",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_COMBATPISTOL","Pistolet de combat",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_APPISTOL","Pistolet perforant",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_PISTOL50","Pistolet .50",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_SNSPISTOL","Pistolet SNS",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_SNSPISTOL_MK2","Pistolet SNS Mk II",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_HEAVYPISTOL","Pistolet lourd",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_VINTAGEPISTOL","Pistolet vintage",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_REVOLVER","Revolver lourd",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_REVOLVER_MK2","Revolver lourd Mk II",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_DOUBLEACTION","Revolver double action",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_NAVYREVOLVER","Revolver Navy",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_CERAMICPISTOL","Pistolet céramique",WeaponSlot.Sidearm),
            new WeaponDef("WEAPON_PISTOLXM3","Pistolet WM 29",WeaponSlot.Sidearm),

            new WeaponDef("WEAPON_MICROSMG","Micro SMG",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_SMG","SMG",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_SMG_MK2","SMG Mk II",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_ASSAULTSMG","SMG d'assaut",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_COMBATPDW","PDW de combat",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_MACHINEPISTOL","Pistolet-mitrailleur",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_MINISMG","Mini SMG",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_ASSAULTRIFLE","Fusil d'assaut",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_ASSAULTRIFLE_MK2","Fusil d'assaut Mk II",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_CARBINERIFLE","Carabine",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_CARBINERIFLE_MK2","Carabine Mk II",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_ADVANCEDRIFLE","Fusil avancé",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_SPECIALCARBINE","Carabine spéciale",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_SPECIALCARBINE_MK2","Carabine spéciale Mk II",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_BULLPUPRIFLE","Fusil bullpup",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_BULLPUPRIFLE_MK2","Fusil bullpup Mk II",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_COMPACTRIFLE","Fusil compact",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_TACTICALRIFLE","Carabine de service",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_HEAVYRIFLE","Fusil lourd",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_BATTLERIFLE","Fusil de combat",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_PUMPSHOTGUN","Fusil à pompe",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_PUMPSHOTGUN_MK2","Fusil à pompe Mk II",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_SAWNOFFSHOTGUN","Fusil à canon scié",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_ASSAULTSHOTGUN","Fusil à pompe d'assaut",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_BULLPUPSHOTGUN","Fusil à pompe bullpup",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_HEAVYSHOTGUN","Fusil à pompe lourd",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_COMBATSHOTGUN","Fusil à pompe de combat",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_DBSHOTGUN","Fusil à double canon",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_AUTOSHOTGUN","Fusil à pompe auto",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_SNIPERRIFLE","Fusil de précision",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_HEAVYSNIPER","Sniper lourd",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_HEAVYSNIPER_MK2","Sniper lourd Mk II",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_MARKSMANRIFLE","Fusil de précision",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_MARKSMANRIFLE_MK2","Fusil de précision Mk II",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_PRECISIONRIFLE","Fusil de précision",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_MG","Mitrailleuse",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_COMBATMG","Mitrailleuse de combat",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_COMBATMG_MK2","Mitrailleuse de combat Mk II",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_GUSENBERG","Balayeuse Gusenberg",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_GRENADELAUNCHER","Lance-grenades",WeaponSlot.LongGun),
            new WeaponDef("WEAPON_RPG","Lance-roquettes",WeaponSlot.LongGun),

            new WeaponDef("WEAPON_KNIFE","Couteau",WeaponSlot.Melee),
            new WeaponDef("WEAPON_NIGHTSTICK","Matraque",WeaponSlot.Melee),
            new WeaponDef("WEAPON_HAMMER","Marteau",WeaponSlot.Melee),
            new WeaponDef("WEAPON_BAT","Batte",WeaponSlot.Melee),
            new WeaponDef("WEAPON_CROWBAR","Pied-de-biche",WeaponSlot.Melee),
            new WeaponDef("WEAPON_GOLFCLUB","Club de golf",WeaponSlot.Melee),
            new WeaponDef("WEAPON_BOTTLE","Bouteille",WeaponSlot.Melee),
            new WeaponDef("WEAPON_DAGGER","Dague",WeaponSlot.Melee),
            new WeaponDef("WEAPON_HATCHET","Hachette",WeaponSlot.Melee),
            new WeaponDef("WEAPON_KNUCKLE","Poing américain",WeaponSlot.Melee),
            new WeaponDef("WEAPON_MACHETE","Machette",WeaponSlot.Melee),
            new WeaponDef("WEAPON_FLASHLIGHT","Lampe torche",WeaponSlot.Melee),
            new WeaponDef("WEAPON_SWITCHBLADE","Cran d'arrêt",WeaponSlot.Melee),
            new WeaponDef("WEAPON_BATTLEAXE","Hache de combat",WeaponSlot.Melee),
            new WeaponDef("WEAPON_POOLCUE","Queue de billard",WeaponSlot.Melee),
            new WeaponDef("WEAPON_WRENCH","Clé anglaise",WeaponSlot.Melee),
            new WeaponDef("WEAPON_STONE_HATCHET","Hachette en pierre",WeaponSlot.Melee),

            new WeaponDef("WEAPON_GRENADE","Grenade",WeaponSlot.Throwable),
            new WeaponDef("WEAPON_BZGAS","Gaz lacrymogène",WeaponSlot.Throwable),
            new WeaponDef("WEAPON_MOLOTOV","Molotov",WeaponSlot.Throwable),
            new WeaponDef("WEAPON_STICKYBOMB","Bombe collante",WeaponSlot.Throwable),
            new WeaponDef("WEAPON_PROXMINE","Mine de proximité",WeaponSlot.Throwable),
            new WeaponDef("WEAPON_PIPEBOMB","Bombe artisanale",WeaponSlot.Throwable),
            new WeaponDef("WEAPON_SNOWBALL","Boule de neige",WeaponSlot.Throwable),
            new WeaponDef("WEAPON_BALL","Balle",WeaponSlot.Throwable)
        };

        private static readonly string[] KnownComponents =
        {
            "COMPONENT_AT_PI_FLSH","COMPONENT_AT_PI_FLSH_02","COMPONENT_AT_PI_FLSH_03","COMPONENT_AT_AR_FLSH",
            "COMPONENT_AT_AR_AFGRIP","COMPONENT_AT_AR_AFGRIP_02","COMPONENT_AT_SCOPE_MACRO","COMPONENT_AT_SCOPE_MACRO_02",
            "COMPONENT_AT_SCOPE_MACRO_02_MK2","COMPONENT_AT_SCOPE_SMALL","COMPONENT_AT_SCOPE_SMALL_02","COMPONENT_AT_SCOPE_SMALL_MK2",
            "COMPONENT_AT_SCOPE_MEDIUM","COMPONENT_AT_SCOPE_MEDIUM_MK2","COMPONENT_AT_SCOPE_LARGE","COMPONENT_AT_SCOPE_LARGE_FIXED_ZOOM",
            "COMPONENT_AT_SCOPE_LARGE_MK2","COMPONENT_AT_SCOPE_MAX","COMPONENT_AT_PI_SUPP","COMPONENT_AT_PI_SUPP_02",
            "COMPONENT_AT_AR_SUPP","COMPONENT_AT_AR_SUPP_02","COMPONENT_AT_SR_SUPP","COMPONENT_AT_SR_SUPP_03",
            "COMPONENT_PISTOL_CLIP_01","COMPONENT_PISTOL_CLIP_02","COMPONENT_COMBATPISTOL_CLIP_01","COMPONENT_COMBATPISTOL_CLIP_02",
            "COMPONENT_APPISTOL_CLIP_01","COMPONENT_APPISTOL_CLIP_02","COMPONENT_PISTOL50_CLIP_01","COMPONENT_PISTOL50_CLIP_02",
            "COMPONENT_SNSPISTOL_CLIP_01","COMPONENT_SNSPISTOL_CLIP_02","COMPONENT_HEAVYPISTOL_CLIP_01","COMPONENT_HEAVYPISTOL_CLIP_02",
            "COMPONENT_VINTAGEPISTOL_CLIP_01","COMPONENT_VINTAGEPISTOL_CLIP_02","COMPONENT_MICROSMG_CLIP_01","COMPONENT_MICROSMG_CLIP_02",
            "COMPONENT_MICROSMG_CLIP_03","COMPONENT_SMG_CLIP_01","COMPONENT_SMG_CLIP_02","COMPONENT_SMG_CLIP_03",
            "COMPONENT_ASSAULTSMG_CLIP_01","COMPONENT_ASSAULTSMG_CLIP_02","COMPONENT_MINISMG_CLIP_01","COMPONENT_MINISMG_CLIP_02",
            "COMPONENT_ASSAULTRIFLE_CLIP_01","COMPONENT_ASSAULTRIFLE_CLIP_02","COMPONENT_ASSAULTRIFLE_CLIP_03",
            "COMPONENT_CARBINERIFLE_CLIP_01","COMPONENT_CARBINERIFLE_CLIP_02","COMPONENT_CARBINERIFLE_CLIP_03",
            "COMPONENT_ADVANCEDRIFLE_CLIP_01","COMPONENT_ADVANCEDRIFLE_CLIP_02","COMPONENT_SPECIALCARBINE_CLIP_01",
            "COMPONENT_SPECIALCARBINE_CLIP_02","COMPONENT_SPECIALCARBINE_CLIP_03","COMPONENT_BULLPUPRIFLE_CLIP_01",
            "COMPONENT_BULLPUPRIFLE_CLIP_02","COMPONENT_COMPACTRIFLE_CLIP_01","COMPONENT_COMPACTRIFLE_CLIP_02",
            "COMPONENT_COMPACTRIFLE_CLIP_03","COMPONENT_PUMPSHOTGUN_CLIP_01","COMPONENT_ASSAULTSHOTGUN_CLIP_01",
            "COMPONENT_ASSAULTSHOTGUN_CLIP_02","COMPONENT_HEAVYSHOTGUN_CLIP_01","COMPONENT_HEAVYSHOTGUN_CLIP_02",
            "COMPONENT_HEAVYSHOTGUN_CLIP_03","COMPONENT_SNIPERRIFLE_CLIP_01","COMPONENT_HEAVYSNIPER_CLIP_01",
            "COMPONENT_MARKSMANRIFLE_CLIP_01","COMPONENT_MARKSMANRIFLE_CLIP_02"
        };

        private static readonly string[] Suppressors =
        {
            "COMPONENT_AT_PI_SUPP","COMPONENT_AT_PI_SUPP_02","COMPONENT_AT_AR_SUPP","COMPONENT_AT_AR_SUPP_02","COMPONENT_AT_SR_SUPP","COMPONENT_AT_SR_SUPP_03"
        };

        private static readonly Vector3[] AmmuNations =
        {
            new Vector3(22.1f,-1107.3f,29.8f), new Vector3(252.3f,-50.0f,69.9f), new Vector3(842.4f,-1033.4f,28.2f),
            new Vector3(-662.1f,-935.3f,21.8f), new Vector3(-1306.2f,-394.0f,36.7f), new Vector3(-3171.9f,1087.1f,20.8f),
            new Vector3(-1117.5f,2698.6f,18.5f), new Vector3(2567.6f,294.4f,108.7f), new Vector3(-330.2f,6083.8f,31.4f),
            new Vector3(1693.4f,3760.2f,34.7f)
        };

        private static readonly Vector3[] GeneralStores =
        {
            new Vector3(25.06f,-1347.32f,29.50f),new Vector3(-3039.18f,585.13f,7.91f),new Vector3(-3242.20f,1000.00f,12.83f),
            new Vector3(1728.78f,6414.41f,35.04f),new Vector3(1698.31f,4924.31f,42.06f),new Vector3(1961.46f,3740.67f,32.34f),
            new Vector3(548.12f,2669.45f,42.16f),new Vector3(2678.85f,3280.17f,55.24f),new Vector3(2557.30f,380.75f,108.62f),
            new Vector3(373.80f,326.18f,103.57f)
        };

        private readonly Dictionary<string,List<WeaponEntry>> _trunks = new Dictionary<string,List<WeaponEntry>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,Dictionary<int,WeaponEntry>> _armory = new Dictionary<string,Dictionary<int,WeaponEntry>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,int[]> _loadouts = new Dictionary<string,int[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,MaterialState> _materials = new Dictionary<string,MaterialState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,List<OutfitState>> _outfits = new Dictionary<string,List<OutfitState>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,AccessoryState> _accessories = new Dictionary<string,AccessoryState>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ownedSuppressors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _contextWasDown, _wheelWasDown, _reloadWasDown, _upWasDown, _downWasDown, _acceptWasDown, _cancelWasDown;
        private int _contextHoldStarted;
        private int _lastScan, _lastEnforce, _lastSave, _lastHelp, _storyYieldUntil;
        private bool _loadoutEdit, _vendorMenu, _trunkOpen, _keyboardOpen;
        private int _trunkVehicle, _menuIndex;
        private PendingTransfer _pending;
        private string _keyboardVehicleKey = string.Empty;

        public InventoryRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            LoadAll();
            ArchiveLegacySatchel();
            Interval = 25;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Inventory Runtime VI 0.5.0 loaded: native weapon wheel preserved, four-slot loadout, local trunk inventory and wheel-integrated crafting.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { CloseAll(player); return; }
                if (RockstarOwnsScene()) { _storyYieldUntil = Game.GameTime + 5000; CloseAll(player); return; }
                if (Game.GameTime < _storyYieldUntil) { CloseAll(player); return; }

                if (_keyboardOpen) { UpdateKeyboard(player); return; }
                if (_pending != null) { UpdatePending(player); return; }
                if (_trunkOpen) { UpdateTrunkMenu(player); return; }
                if (_vendorMenu) { UpdateVendorMenu(player); return; }

                int now = Game.GameTime;
                string charKey = CharacterKey(player);
                if (!player.IsInVehicle() && now - _lastScan >= 1100)
                {
                    _lastScan = now;
                    ObserveOwnedWeapons(player, charKey);
                    RememberSuppressors(player, charKey);
                }

                UpdateWeaponWheelContext(player, charKey);

                if (_loadoutEdit) UpdateLoadoutEditor(player, charKey);
                else
                {
                    if (!player.IsInVehicle() && now - _lastEnforce >= 900) { _lastEnforce = now; EnforceFourSlots(player, charKey); }
                    UpdateWorldContexts(player, charKey);
                }

                if (now - _lastSave >= 10000) { _lastSave = now; SaveAll(); }
            }
            catch (Exception ex) { Log("Tick error: " + ex); }
        }

        private void UpdateWorldContexts(Ped player, string charKey)
        {
            bool context = Pressed(Context);
            bool justPressed = context && !_contextWasDown;
            _contextWasDown = context;

            if (player.IsInVehicle()) { _contextHoldStarted = 0; return; }

            Vehicle trunk = RearVehicle(player.Position, 3.0f);
            bool nearTrunk = trunk != null && trunk.Exists() && Distance(player.Position, RearPoint(trunk)) <= 2.1f && !IsMission(trunk);
            if (nearTrunk)
            {
                _contextHoldStarted = 0;
                ShowHelp("~INPUT_CONTEXT~  Ouvrir le coffre");
                if (justPressed) OpenTrunk(player, trunk);
                return;
            }

            bool ammu = NearAny(player.Position, AmmuNations, 10.5f);
            bool store = NearAny(player.Position, GeneralStores, 2.4f);
            if (!ammu && !store) { _contextHoldStarted = 0; return; }
            if (!context) { _contextHoldStarted = 0; return; }
            if (_contextHoldStarted == 0) _contextHoldStarted = Game.GameTime;
            int held = Game.GameTime - _contextHoldStarted;

            if (ammu)
            {
                ShowHelp("Maintenir ~INPUT_CONTEXT~ : configurer l'equipement");
                if (held >= 800) { _contextHoldStarted = 0; EnterLoadoutEditor(player, charKey); }
            }
            else
            {
                ShowHelp("Maintenir ~INPUT_CONTEXT~ : fournitures");
                if (held >= 800) { _contextHoldStarted = 0; _vendorMenu = true; _menuIndex = 0; PrimeMenuEdges(); }
            }
        }

        private void EnterLoadoutEditor(Ped player, string charKey)
        {
            _loadoutEdit = true;
            RestoreArmoryForEditing(player, charKey);
            _wheelWasDown = Pressed(SelectWeapon);
            ShowHelp("Ammu-Nation : ouvrez la roue GTA et choisissez une arme de chaque categorie.");
            Log("Loadout editor entered for " + charKey + "; only previously observed owned weapons restored.");
        }

        private void UpdateLoadoutEditor(Ped player, string charKey)
        {
            if (player.IsInVehicle() || !NearAny(player.Position, AmmuNations, 13.5f)) { ExitLoadoutEditor(player, charKey); return; }
            bool context = Pressed(Context);
            bool contextJust = context && !_contextWasDown;
            _contextWasDown = context;
            if (contextJust) { ExitLoadoutEditor(player, charKey); return; }

            bool wheel = Pressed(SelectWeapon);
            if (!wheel && _wheelWasDown)
            {
                int selected = SelectedWeapon(player);
                WeaponSlot slot = SlotOf(selected);
                if (slot != WeaponSlot.None)
                {
                    int[] l = GetLoadout(charKey);
                    l[(int)slot] = selected;
                    CaptureToArmory(player, charKey, selected);
                    Notify("Equipement : " + SlotLabel(slot) + " = " + WeaponLabel(selected));
                    SaveLoadouts();
                    Log("Loadout slot " + slot + " set to " + selected + " for " + charKey + ".");
                }
            }
            _wheelWasDown = wheel;
            ShowHelp("Roue GTA : choisissez une arme. ~INPUT_CONTEXT~  Terminer");
        }

        private void ExitLoadoutEditor(Ped player, string charKey)
        {
            _loadoutEdit = false;
            EnforceFourSlots(player, charKey);
            _contextWasDown = Pressed(Context);
            Notify("Equipement enregistre.");
            SaveAll();
        }

        private void ObserveOwnedWeapons(Ped player, string charKey)
        {
            foreach (WeaponDef def in Weapons)
            {
                int h = SafeHash(def.Name);
                if (h == 0 || !HasWeapon(player, h)) continue;
                CaptureToArmory(player, charKey, h);
                int[] loadout = GetLoadout(charKey);
                if (loadout[(int)def.Slot] == 0) loadout[(int)def.Slot] = h;
            }
        }

        private void CaptureToArmory(Ped player, string charKey, int hash)
        {
            if (hash == 0 || !HasWeapon(player, hash)) return;
            Dictionary<int,WeaponEntry> a = GetArmory(charKey);
            WeaponEntry current = CaptureWeapon(player, hash);
            if (current == null) return;
            WeaponEntry old;
            if (a.TryGetValue(hash, out old))
            {
                current.Ammo = Math.Max(current.Ammo, old.Ammo);
                foreach (int c in old.Components) if (!current.Components.Contains(c)) current.Components.Add(c);
            }
            a[hash] = current;
        }

        private void RestoreArmoryForEditing(Ped player, string charKey)
        {
            Dictionary<int,WeaponEntry> a = GetArmory(charKey);
            foreach (WeaponEntry w in a.Values) RestoreWeapon(player, w, false);
        }

        private void EnforceFourSlots(Ped player, string charKey)
        {
            if (player == null || !player.Exists() || player.IsInVehicle()) return;
            int[] loadout = GetLoadout(charKey);
            Dictionary<int,WeaponEntry> a = GetArmory(charKey);

            foreach (WeaponDef def in Weapons)
            {
                int h = SafeHash(def.Name);
                if (h == 0 || !HasWeapon(player, h)) continue;
                CaptureToArmory(player, charKey, h);
                int wanted = loadout[(int)def.Slot];
                if (wanted != 0 && h == wanted) continue;
                try { Function.Call(Hash.REMOVE_WEAPON_FROM_PED, player.Handle, h); } catch { }
            }

            for (int i = 0; i < 4; i++)
            {
                int h = loadout[i];
                if (h == 0 || HasWeapon(player, h)) continue;
                WeaponEntry w;
                if (a.TryGetValue(h, out w)) RestoreWeapon(player, w, false);
            }

            int molotov = SafeHash("WEAPON_MOLOTOV");
            if (loadout[(int)WeaponSlot.Throwable] == molotov && a.ContainsKey(molotov) && !HasWeapon(player, molotov))
            {
                try { Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, molotov, 0, false, false); } catch { }
            }
        }

        private void UpdateWeaponWheelContext(Ped player, string charKey)
        {
            bool wheel = Pressed(SelectWeapon);
            bool reload = Pressed(Reload);
            bool reloadJust = reload && !_reloadWasDown;
            _reloadWasDown = reload;
            if (!wheel) { _wheelWasDown = false; return; }

            int weapon = SelectedWeapon(player);
            int molotov = SafeHash("WEAPON_MOLOTOV");
            if (weapon == molotov)
            {
                int ammo = Ammo(player, molotov);
                MaterialState m = GetMaterials(charKey);
                if (ammo <= 0)
                {
                    ShowHelp("~INPUT_RELOAD~  Fabriquer Molotov : Tissu + Alcool   [" + m.Cloth + "/" + m.Alcohol + "]");
                    if (reloadJust)
                    {
                        DisableControl(Reload);
                        if (m.Cloth <= 0 || m.Alcohol <= 0) Notify("Il faut du tissu et de l'alcool.");
                        else
                        {
                            m.Cloth--; m.Alcohol--;
                            Dictionary<int,WeaponEntry> a = GetArmory(charKey);
                            WeaponEntry w;
                            if (!a.TryGetValue(molotov, out w)) { w = new WeaponEntry { Hash = molotov, Ammo = 0 }; a[molotov] = w; }
                            try
                            {
                                if (!HasWeapon(player, molotov)) Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, molotov, 1, false, true);
                                else Function.Call(Hash.ADD_AMMO_TO_PED, player.Handle, molotov, 1);
                            }
                            catch { }
                            w.Ammo = Math.Max(w.Ammo, Ammo(player, molotov));
                            SaveMaterials(); SaveArmory(); Notify("Molotov fabrique.");
                        }
                    }
                }
            }

            if (SlotOf(weapon) == WeaponSlot.LongGun || SlotOf(weapon) == WeaponSlot.Sidearm)
            {
                string action = SuppressorAction(player, charKey, weapon);
                if (!string.IsNullOrEmpty(action))
                {
                    ShowHelp("~INPUT_RELOAD~  " + action);
                    if (reloadJust) { DisableControl(Reload); ToggleSuppressor(player, charKey, weapon); }
                }
            }
            _wheelWasDown = true;
        }

        private string SuppressorAction(Ped player, string charKey, int weapon)
        {
            foreach (string name in Suppressors)
            {
                int c = SafeHash(name); if (c == 0) continue;
                bool installed = HasComponent(player, weapon, c);
                string key = charKey + ":" + weapon + ":" + c;
                if (installed) return "Retirer le silencieux";
                if (_ownedSuppressors.Contains(key)) return "Installer le silencieux";
            }
            return string.Empty;
        }

        private void RememberSuppressors(Ped player, string charKey)
        {
            int weapon = SelectedWeapon(player);
            if (SlotOf(weapon) != WeaponSlot.LongGun && SlotOf(weapon) != WeaponSlot.Sidearm) return;
            foreach (string name in Suppressors)
            {
                int c = SafeHash(name);
                if (c != 0 && HasComponent(player, weapon, c)) _ownedSuppressors.Add(charKey + ":" + weapon + ":" + c);
            }
        }

        private void ToggleSuppressor(Ped player, string charKey, int weapon)
        {
            foreach (string name in Suppressors)
            {
                int c = SafeHash(name); if (c == 0) continue;
                string key = charKey + ":" + weapon + ":" + c;
                if (HasComponent(player, weapon, c))
                {
                    _ownedSuppressors.Add(key);
                    try { Function.Call(Hash.REMOVE_WEAPON_COMPONENT_FROM_PED, player.Handle, weapon, c); } catch { }
                    SaveArmory(); Notify("Silencieux retire."); return;
                }
            }
            foreach (string name in Suppressors)
            {
                int c = SafeHash(name); string key = charKey + ":" + weapon + ":" + c;
                if (c == 0 || !_ownedSuppressors.Contains(key)) continue;
                try { Function.Call(Hash.GIVE_WEAPON_COMPONENT_TO_PED, player.Handle, weapon, c); } catch { }
                CaptureToArmory(player, charKey, weapon); Notify("Silencieux installe."); return;
            }
        }

        private void OpenTrunk(Ped player, Vehicle vehicle)
        {
            _trunkOpen = true; _trunkVehicle = vehicle.Handle; _menuIndex = 0; PrimeMenuEdges();
            try
            {
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, player.Handle, vehicle.Handle, 350);
                Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, vehicle.Handle, 5, false, false);
            }
            catch { }
            Log("Trunk session opened vehicle=" + vehicle.Handle + ".");
        }

        private void UpdateTrunkMenu(Ped player)
        {
            Vehicle vehicle = FindVehicleByHandle(player.Position, 5.0f, _trunkVehicle);
            if (vehicle == null || !vehicle.Exists() || IsMission(vehicle) || Distance(player.Position, RearPoint(vehicle)) > 3.2f) { CloseTrunk(player, vehicle); return; }
            DisableMenuControls();
            List<MenuEntry> entries = BuildTrunkEntries(player, vehicle);
            if (entries.Count == 0) entries.Add(new MenuEntry { Label = "Coffre vide", Kind = "none" });
            if (_menuIndex >= entries.Count) _menuIndex = entries.Count - 1;
            if (_menuIndex < 0) _menuIndex = 0;
            DrawListMenu("COFFRE", entries, _menuIndex, "Armes et tenues de ce vehicule uniquement");

            bool up = Pressed(FrontendUp), down = Pressed(FrontendDown), accept = Pressed(FrontendAccept), cancel = Pressed(FrontendCancel);
            if (up && !_upWasDown) _menuIndex = (_menuIndex - 1 + entries.Count) % entries.Count;
            if (down && !_downWasDown) _menuIndex = (_menuIndex + 1) % entries.Count;
            if (cancel && !_cancelWasDown) { CloseTrunk(player, vehicle); PrimeMenuEdges(); return; }
            if (accept && !_acceptWasDown) ExecuteTrunkEntry(player, vehicle, entries[_menuIndex]);
            _upWasDown = up; _downWasDown = down; _acceptWasDown = accept; _cancelWasDown = cancel;
        }

        private List<MenuEntry> BuildTrunkEntries(Ped player, Vehicle vehicle)
        {
            string key = VehicleKey(vehicle);
            List<WeaponEntry> trunk = GetTrunk(key);
            List<OutfitState> outfits = GetOutfits(key);
            var list = new List<MenuEntry>();
            int selected = SelectedWeapon(player);
            if (SlotOf(selected) != WeaponSlot.None && trunk.Count < TrunkCapacity) list.Add(new MenuEntry { Label = "+ Ranger : " + WeaponLabel(selected), Kind = "store", Index = selected });
            for (int i = 0; i < trunk.Count; i++) list.Add(new MenuEntry { Label = "Arme : " + WeaponLabel(trunk[i].Hash), Kind = "weapon", Index = i });
            list.Add(new MenuEntry { Label = "+ Sauvegarder la tenue...", Kind = "saveoutfit" });
            for (int i = 0; i < outfits.Count; i++) list.Add(new MenuEntry { Label = "Tenue : " + outfits[i].Name, Kind = "outfit", Index = i });
            list.Add(new MenuEntry { Label = "Accessoire : masque", Kind = "mask" });
            list.Add(new MenuEntry { Label = "Accessoire : chapeau", Kind = "hat" });
            list.Add(new MenuEntry { Label = "Accessoire : lunettes", Kind = "glasses" });
            return list;
        }

        private void ExecuteTrunkEntry(Ped player, Vehicle vehicle, MenuEntry e)
        {
            if (e == null || e.Kind == "none") return;
            if (e.Kind == "store")
            {
                WeaponEntry w = CaptureWeapon(player, e.Index); if (w == null) return;
                _pending = new PendingTransfer { Kind = "store", VehicleHandle = vehicle.Handle, WeaponHash = w.Hash, ExecuteAt = Game.GameTime + 650 }; PlayTransfer(player, vehicle);
            }
            else if (e.Kind == "weapon") { _pending = new PendingTransfer { Kind = "retrieve", VehicleHandle = vehicle.Handle, WeaponIndex = e.Index, ExecuteAt = Game.GameTime + 650 }; PlayTransfer(player, vehicle); }
            else if (e.Kind == "saveoutfit")
            {
                _keyboardOpen = true; _keyboardVehicleKey = VehicleKey(vehicle);
                try { Function.Call(Hash.DISPLAY_ONSCREEN_KEYBOARD, 1, "FMMC_KEY_TIP8", "", "Tenue " + (GetOutfits(_keyboardVehicleKey).Count + 1), "", "", "", 24); } catch { _keyboardOpen = false; }
            }
            else if (e.Kind == "outfit") RestoreOutfit(player, vehicle, e.Index);
            else if (e.Kind == "mask") ToggleMask(player);
            else if (e.Kind == "hat") ToggleProp(player, true);
            else if (e.Kind == "glasses") ToggleProp(player, false);
        }

        private void UpdatePending(Ped player)
        {
            Vehicle vehicle = FindVehicleByHandle(player.Position, 5f, _pending.VehicleHandle);
            if (vehicle == null || !vehicle.Exists()) { _pending = null; return; }
            if (Game.GameTime < _pending.ExecuteAt) return;
            string key = VehicleKey(vehicle); List<WeaponEntry> trunk = GetTrunk(key);
            if (_pending.Kind == "store")
            {
                WeaponEntry w = CaptureWeapon(player, _pending.WeaponHash);
                if (w != null && trunk.Count < TrunkCapacity)
                {
                    trunk.Add(Clone(w));
                    try { Function.Call(Hash.REMOVE_WEAPON_FROM_PED, player.Handle, w.Hash); } catch { }
                    Log("Stored exact weapon " + w.Hash + " components=" + w.Components.Count + " trunk=" + key + ".");
                }
            }
            else if (_pending.Kind == "retrieve" && _pending.WeaponIndex >= 0 && _pending.WeaponIndex < trunk.Count)
            {
                WeaponEntry w = trunk[_pending.WeaponIndex]; trunk.RemoveAt(_pending.WeaponIndex); RestoreWeapon(player, w, true); CaptureToArmory(player, CharacterKey(player), w.Hash);
                int[] l = GetLoadout(CharacterKey(player)); WeaponSlot slot = SlotOf(w.Hash); if (slot != WeaponSlot.None) l[(int)slot] = w.Hash;
                Log("Retrieved selected weapon " + w.Hash + " components=" + w.Components.Count + " trunk=" + key + ".");
            }
            _pending = null; SaveAll();
        }

        private static void PlayTransfer(Ped player, Vehicle vehicle)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, "pickup_object"); Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, player.Handle, vehicle.Handle, 250);
                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, "pickup_object")) Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, "pickup_object", "pickup_low", 4f, -4f, 750, 0, 0f, false, false, false);
            }
            catch { }
        }

        private void UpdateKeyboard(Ped player)
        {
            int state = 0; try { state = Function.Call<int>(Hash.UPDATE_ONSCREEN_KEYBOARD); } catch { state = 2; }
            if (state == 0) return;
            _keyboardOpen = false;
            if (state == 1)
            {
                string name = null; try { name = Function.Call<string>(Hash.GET_ONSCREEN_KEYBOARD_RESULT); } catch { }
                if (string.IsNullOrWhiteSpace(name)) name = "Tenue " + (GetOutfits(_keyboardVehicleKey).Count + 1);
                SaveOutfit(player, _keyboardVehicleKey, name.Trim());
            }
            _keyboardVehicleKey = string.Empty; PrimeMenuEdges();
        }

        private void SaveOutfit(Ped player, string vehicleKey, string name)
        {
            var o = new OutfitState { Name = name };
            try
            {
                for (int i = 0; i < 12; i++) { o.Drawables[i] = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, player.Handle, i); o.Textures[i] = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, player.Handle, i); o.Palettes[i] = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, player.Handle, i); }
                for (int i = 0; i < 8; i++) { o.Props[i] = Function.Call<int>(Hash.GET_PED_PROP_INDEX, player.Handle, i); if (o.Props[i] >= 0) o.PropTextures[i] = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, player.Handle, i); }
                List<OutfitState> list = GetOutfits(vehicleKey); int existing = list.FindIndex(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)); if (existing >= 0) list[existing] = o; else list.Add(o);
                SaveOutfits(); Notify("Tenue \"" + name + "\" rangee dans le coffre.");
            }
            catch { }
        }

        private void RestoreOutfit(Ped player, Vehicle vehicle, int index)
        {
            List<OutfitState> list = GetOutfits(VehicleKey(vehicle)); if (index < 0 || index >= list.Count) return; OutfitState o = list[index];
            try
            {
                for (int i = 0; i < 12; i++) Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle, i, o.Drawables[i], o.Textures[i], o.Palettes[i]);
                for (int i = 0; i < 8; i++) { if (o.Props[i] < 0) Function.Call(Hash.CLEAR_PED_PROP, player.Handle, i); else Function.Call(Hash.SET_PED_PROP_INDEX, player.Handle, i, o.Props[i], o.PropTextures[i], true); }
                Notify("Tenue : " + o.Name);
            }
            catch { }
        }

        private void ToggleMask(Ped player)
        {
            AccessoryState a = GetAccessory(CharacterKey(player));
            try
            {
                int d = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, player.Handle, 1);
                if (d > 0) { a.MaskDrawable = d; a.MaskTexture = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, player.Handle, 1); a.MaskPalette = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, player.Handle, 1); Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle, 1, 0, 0, 0); }
                else if (a.MaskDrawable > 0) Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle, 1, a.MaskDrawable, a.MaskTexture, a.MaskPalette);
                SaveAccessories();
            }
            catch { }
        }

        private void ToggleProp(Ped player, bool hat)
        {
            AccessoryState a = GetAccessory(CharacterKey(player)); int prop = hat ? 0 : 1;
            try
            {
                int d = Function.Call<int>(Hash.GET_PED_PROP_INDEX, player.Handle, prop);
                if (d >= 0) { int t = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, player.Handle, prop); if (hat) { a.HatDrawable = d; a.HatTexture = t; } else { a.GlassesDrawable = d; a.GlassesTexture = t; } Function.Call(Hash.CLEAR_PED_PROP, player.Handle, prop); }
                else { int sd = hat ? a.HatDrawable : a.GlassesDrawable; int st = hat ? a.HatTexture : a.GlassesTexture; if (sd >= 0) Function.Call(Hash.SET_PED_PROP_INDEX, player.Handle, prop, sd, st, true); }
                SaveAccessories();
            }
            catch { }
        }

        private void UpdateVendorMenu(Ped player)
        {
            if (player.IsInVehicle() || !NearAny(player.Position, GeneralStores, 3.2f)) { _vendorMenu = false; return; }
            DisableMenuControls();
            var entries = new List<MenuEntry> { new MenuEntry{Label="Tissu                         $8",Kind="cloth"}, new MenuEntry{Label="Alcool                       $18",Kind="alcohol"} };
            DrawListMenu("FOURNITURES", entries, _menuIndex, "Materiaux de fabrication");
            bool up=Pressed(FrontendUp),down=Pressed(FrontendDown),accept=Pressed(FrontendAccept),cancel=Pressed(FrontendCancel);
            if(up&&!_upWasDown)_menuIndex=(_menuIndex-1+entries.Count)%entries.Count;
            if(down&&!_downWasDown)_menuIndex=(_menuIndex+1)%entries.Count;
            if(cancel&&!_cancelWasDown){_vendorMenu=false;PrimeMenuEdges();return;}
            if(accept&&!_acceptWasDown)
            {
                int price=_menuIndex==0?8:18;
                if(Game.Player.Money<price)Notify("Pas assez d'argent.");
                else { Game.Player.Money-=price; MaterialState m=GetMaterials(CharacterKey(player)); if(_menuIndex==0)m.Cloth++;else m.Alcohol++; SaveMaterials(); Notify(_menuIndex==0?"Tissu achete.":"Alcool achete."); }
            }
            _upWasDown=up;_downWasDown=down;_acceptWasDown=accept;_cancelWasDown=cancel;
        }

        private void CloseTrunk(Ped player, Vehicle vehicle)
        {
            try { if (vehicle != null && vehicle.Exists()) Function.Call(Hash.SET_VEHICLE_DOOR_SHUT, vehicle.Handle, 5, false); } catch { }
            _trunkOpen=false;_trunkVehicle=0;_menuIndex=0;_pending=null;PrimeMenuEdges(); Log("Trunk session closed.");
        }

        private void CloseAll(Ped player)
        {
            Vehicle v = player != null && player.Exists() ? FindVehicleByHandle(player.Position, 6f, _trunkVehicle) : null;
            if (_trunkOpen) CloseTrunk(player, v);
            _loadoutEdit=false;_vendorMenu=false;_keyboardOpen=false;_pending=null;_contextHoldStarted=0;
            _contextWasDown=Pressed(Context);_wheelWasDown=Pressed(SelectWeapon);_reloadWasDown=Pressed(Reload);
        }

        private void DisableMenuControls() { DisableControl(Attack); DisableControl(Aim); DisableControl(SelectWeapon); DisableControl(Context); }

        private static void DrawListMenu(string title, List<MenuEntry> entries, int selected, string subtitle)
        {
            float x=0.165f,y=0.19f,w=0.27f; DrawRect(x,y,w,0.052f,10,10,10,220); DrawText(x-w/2f+0.012f,y-0.016f,title,0.39f,true); DrawText(x-w/2f+0.012f,y+0.018f,subtitle,0.23f,false);
            int first=Math.Max(0,Math.Min(selected-4,Math.Max(0,entries.Count-8))); int last=Math.Min(entries.Count,first+8);
            for(int i=first;i<last;i++){float yy=0.25f+(i-first)*0.038f;if(i==selected)DrawRect(x,yy,w,0.036f,235,235,235,220);DrawText(x-w/2f+0.012f,yy-0.012f,entries[i].Label,0.29f,i!=selected);}
        }

        private static void DrawRect(float x,float y,float w,float h,int r,int g,int b,int a) { try{Function.Call(Hash.DRAW_RECT,x,y,w,h,r,g,b,a,false);}catch{} }
        private static void DrawText(float x,float y,string text,float scale,bool white)
        {
            try { Function.Call(Hash.SET_TEXT_FONT,0);Function.Call(Hash.SET_TEXT_SCALE,1f,scale);Function.Call(Hash.SET_TEXT_COLOUR,white?255:225,white?255:225,white?255:225,255);Function.Call(Hash.SET_TEXT_OUTLINE);Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,text);Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT,x,y,0); }
            catch{}
        }

        private static WeaponEntry CaptureWeapon(Ped player, int weapon)
        {
            if (player == null || !player.Exists() || weapon == 0 || SlotOf(weapon) == WeaponSlot.None || !HasWeapon(player, weapon)) return null;
            var w = new WeaponEntry { Hash = weapon, Ammo = Ammo(player, weapon) };
            try { w.Tint = Math.Max(0,Function.Call<int>(Hash.GET_PED_WEAPON_TINT_INDEX,player.Handle,weapon)); } catch { }
            foreach(string name in KnownComponents){int c=SafeHash(name);if(c!=0&&HasComponent(player,weapon,c))w.Components.Add(c);} return w;
        }

        private static void RestoreWeapon(Ped player, WeaponEntry w, bool equip)
        {
            if(player==null||!player.Exists()||w==null||SlotOf(w.Hash)==WeaponSlot.None)return;
            try{Function.Call(Hash.GIVE_WEAPON_TO_PED,player.Handle,w.Hash,Math.Max(0,w.Ammo),false,equip);}catch{}
            try{Function.Call(Hash.SET_PED_WEAPON_TINT_INDEX,player.Handle,w.Hash,Math.Max(0,w.Tint));}catch{}
            foreach(int c in w.Components){try{Function.Call(Hash.GIVE_WEAPON_COMPONENT_TO_PED,player.Handle,w.Hash,c);}catch{}}
        }

        private static WeaponEntry Clone(WeaponEntry s) { if(s==null)return null;var w=new WeaponEntry{Hash=s.Hash,Ammo=s.Ammo,Tint=s.Tint};w.Components.AddRange(s.Components);return w; }
        private Dictionary<int,WeaponEntry> GetArmory(string key){Dictionary<int,WeaponEntry> a;if(!_armory.TryGetValue(key,out a)){a=new Dictionary<int,WeaponEntry>();_armory[key]=a;}return a;}
        private int[] GetLoadout(string key){int[] l;if(!_loadouts.TryGetValue(key,out l)){l=new int[4];_loadouts[key]=l;}return l;}
        private List<WeaponEntry> GetTrunk(string key){List<WeaponEntry> l;if(!_trunks.TryGetValue(key,out l)){l=new List<WeaponEntry>();_trunks[key]=l;}return l;}
        private List<OutfitState> GetOutfits(string key){List<OutfitState> l;if(!_outfits.TryGetValue(key,out l)){l=new List<OutfitState>();_outfits[key]=l;}return l;}
        private MaterialState GetMaterials(string key){MaterialState m;if(!_materials.TryGetValue(key,out m)){m=new MaterialState();_materials[key]=m;}return m;}
        private AccessoryState GetAccessory(string key){AccessoryState a;if(!_accessories.TryGetValue(key,out a)){a=new AccessoryState();_accessories[key]=a;}return a;}

        private static WeaponSlot SlotOf(int hash){if(hash==0)return WeaponSlot.None;foreach(WeaponDef d in Weapons)if(SafeHash(d.Name)==hash)return d.Slot;return WeaponSlot.None;}
        private static string WeaponLabel(int hash){foreach(WeaponDef d in Weapons)if(SafeHash(d.Name)==hash)return d.Label;return "Arme "+hash;}
        private static string SlotLabel(WeaponSlot s){return s==WeaponSlot.LongGun?"arme longue":s==WeaponSlot.Sidearm?"pistolet":s==WeaponSlot.Melee?"melee":s==WeaponSlot.Throwable?"projectile":"arme";}
        private static int SelectedWeapon(Ped p){try{return Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON,p.Handle);}catch{return 0;}}
        private static bool HasWeapon(Ped p,int h){try{return Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON,p.Handle,h,false);}catch{return false;}}
        private static int Ammo(Ped p,int h){try{return Math.Max(0,Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON,p.Handle,h));}catch{return 0;}}
        private static bool HasComponent(Ped p,int w,int c){try{return Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON_COMPONENT,p.Handle,w,c);}catch{return false;}}
        private static int SafeHash(string n){try{return Function.Call<int>(Hash.GET_HASH_KEY,n);}catch{return 0;}}
        private static bool IsMission(Entity e){try{return Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY,e.Handle);}catch{return true;}}

        private static Vehicle RearVehicle(Vector3 pos,float radius)
        {
            Vehicle[] vs;try{vs=World.GetNearbyVehicles(pos,radius+1.5f);}catch{return null;} Vehicle best=null;float bd=radius;
            foreach(Vehicle v in vs){if(v==null||!v.Exists())continue;float d=Distance(pos,RearPoint(v));if(d<bd){bd=d;best=v;}} return best;
        }
        private static Vehicle FindVehicleByHandle(Vector3 pos,float radius,int handle){if(handle==0)return null;Vehicle[] vs;try{vs=World.GetNearbyVehicles(pos,radius);}catch{return null;}foreach(Vehicle v in vs)if(v!=null&&v.Exists()&&v.Handle==handle)return v;return null;}
        private static Vector3 RearPoint(Vehicle v){try{return Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS,v.Handle,0f,-2.25f,0f);}catch{return v.Position;}}
        private static string VehicleKey(Vehicle v){string plate="";try{plate=(Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT,v.Handle)??"").Trim().ToUpperInvariant();}catch{}return v.Model.Hash.ToString(CultureInfo.InvariantCulture)+":"+plate;}
        private static string CharacterKey(Ped p){return p==null||!p.Exists()?"unknown":p.Model.Hash.ToString(CultureInfo.InvariantCulture);}
        private static bool NearAny(Vector3 p,Vector3[] list,float radius){foreach(Vector3 q in list)if(Distance(p,q)<=radius)return true;return false;}
        private static float Distance(Vector3 a,Vector3 b){double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return(float)Math.Sqrt(x*x+y*y+z*z);}

        private static bool Pressed(int c){try{return Function.Call<bool>(Hash.IS_CONTROL_PRESSED,0,c)||Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED,0,c);}catch{return false;}}
        private static void DisableControl(int c){try{Function.Call(Hash.DISABLE_CONTROL_ACTION,0,c,true);}catch{}}
        private void PrimeMenuEdges(){_upWasDown=Pressed(FrontendUp);_downWasDown=Pressed(FrontendDown);_acceptWasDown=Pressed(FrontendAccept);_cancelWasDown=Pressed(FrontendCancel);}

        private void ShowHelp(string text){if(Game.GameTime-_lastHelp<80)return;_lastHelp=Game.GameTime;try{Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,text);Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP,0,false,true,-1);}catch{}}
        private static void Notify(string text){try{Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,text);Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER,false,false);}catch{}}

        private static bool RockstarOwnsScene()
        {
            try{if(Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE))return true;}catch{}
            try{if(Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))return true;}catch{}
            try{if(Function.Call<bool>(Hash.GET_MISSION_FLAG))return true;}catch{}
            try{if(!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON,Game.Player.Handle))return true;}catch{}
            try{if(Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_IN))return true;}catch{}
            return false;
        }

        private void ArchiveLegacySatchel()
        {
            try
            {
                if(!File.Exists(LegacySatchelPath))return; string archived=LegacySatchelPath+".disabled"; if(File.Exists(archived))File.Delete(archived); File.Move(LegacySatchelPath,archived);
                Log("Legacy SatchelWeapons.txt disabled. Its contents are never granted to the player.");
            }
            catch(Exception ex){Log("Legacy satchel disable warning: "+ex.Message);}
        }

        private void LoadAll(){LoadArmory();LoadLoadouts();LoadTrunks();LoadMaterials();LoadOutfits();LoadAccessories();}
        private void SaveAll(){SaveArmory();SaveLoadouts();SaveTrunks();SaveMaterials();SaveOutfits();SaveAccessories();}

        private void LoadArmory()
        {
            if(!File.Exists(ArmoryPath))return;
            try{foreach(string line in File.ReadAllLines(ArmoryPath)){if(line.StartsWith("#SUPP|",StringComparison.Ordinal)){_ownedSuppressors.Add(line.Substring(6));continue;}string[] p=line.Split('|');if(p.Length<5)continue;int h,a,t;if(!int.TryParse(p[1],out h)||!int.TryParse(p[2],out a)||!int.TryParse(p[3],out t))continue;var w=new WeaponEntry{Hash=h,Ammo=Math.Max(0,a),Tint=Math.Max(0,t)};foreach(string s in p[4].Split(',')){int c;if(int.TryParse(s,out c)&&c!=0)w.Components.Add(c);}GetArmory(p[0])[h]=w;}}
            catch(Exception ex){Log("Armory load failed safely: "+ex.Message);}
        }
        private void SaveArmory(){try{var lines=new List<string>();foreach(var pair in _armory)foreach(WeaponEntry w in pair.Value.Values)lines.Add(pair.Key+"|"+w.Hash+"|"+w.Ammo+"|"+w.Tint+"|"+string.Join(",",w.Components));foreach(string s in _ownedSuppressors)lines.Add("#SUPP|"+s);File.WriteAllLines(ArmoryPath,lines);}catch(Exception ex){Log("Armory save failed safely: "+ex.Message);}}
        private void LoadLoadouts(){if(!File.Exists(LoadoutPath))return;try{foreach(string line in File.ReadAllLines(LoadoutPath)){string[] p=line.Split('|');if(p.Length<5)continue;int[] l=new int[4];for(int i=0;i<4;i++)int.TryParse(p[i+1],out l[i]);_loadouts[p[0]]=l;}}catch{}}
        private void SaveLoadouts(){try{File.WriteAllLines(LoadoutPath,_loadouts.Select(p=>p.Key+"|"+string.Join("|",p.Value)).ToArray());}catch{}}
        private void LoadTrunks(){if(!File.Exists(TrunkPath))return;try{foreach(string line in File.ReadAllLines(TrunkPath)){string[] p=line.Split('|');if(p.Length<5)continue;int h,a,t;if(!int.TryParse(p[1],out h)||!int.TryParse(p[2],out a)||!int.TryParse(p[3],out t))continue;var w=new WeaponEntry{Hash=h,Ammo=a,Tint=t};foreach(string s in p[4].Split(',')){int c;if(int.TryParse(s,out c)&&c!=0)w.Components.Add(c);}if(GetTrunk(p[0]).Count<TrunkCapacity)GetTrunk(p[0]).Add(w);}}catch{}}
        private void SaveTrunks(){try{var lines=new List<string>();foreach(var p in _trunks)foreach(WeaponEntry w in p.Value)lines.Add(p.Key+"|"+w.Hash+"|"+w.Ammo+"|"+w.Tint+"|"+string.Join(",",w.Components));File.WriteAllLines(TrunkPath,lines);}catch{}}
        private void LoadMaterials(){if(!File.Exists(MaterialPath))return;try{foreach(string l in File.ReadAllLines(MaterialPath)){string[] p=l.Split('|');if(p.Length<3)continue;int c,a;if(int.TryParse(p[1],out c)&&int.TryParse(p[2],out a))_materials[p[0]]=new MaterialState{Cloth=Math.Max(0,c),Alcohol=Math.Max(0,a)};}}catch{}}
        private void SaveMaterials(){try{File.WriteAllLines(MaterialPath,_materials.Select(p=>p.Key+"|"+p.Value.Cloth+"|"+p.Value.Alcohol).ToArray());}catch{}}
        private void LoadOutfits(){if(!File.Exists(OutfitPath))return;try{foreach(string line in File.ReadAllLines(OutfitPath)){string[] p=line.Split('|');if(p.Length<8)continue;var o=new OutfitState{Name=Decode(p[1])};ParseArray(p[2],o.Drawables);ParseArray(p[3],o.Textures);ParseArray(p[4],o.Palettes);ParseArray(p[5],o.Props);ParseArray(p[6],o.PropTextures);GetOutfits(p[0]).Add(o);}}catch{}}
        private void SaveOutfits(){try{var lines=new List<string>();foreach(var p in _outfits)foreach(OutfitState o in p.Value)lines.Add(p.Key+"|"+Encode(o.Name)+"|"+Join(o.Drawables)+"|"+Join(o.Textures)+"|"+Join(o.Palettes)+"|"+Join(o.Props)+"|"+Join(o.PropTextures)+"|v1");File.WriteAllLines(OutfitPath,lines);}catch{}}
        private void LoadAccessories(){if(!File.Exists(AccessoryPath))return;try{foreach(string l in File.ReadAllLines(AccessoryPath)){string[] p=l.Split('|');if(p.Length<9)continue;_accessories[p[0]]=new AccessoryState{MaskDrawable=PI(p[1]),MaskTexture=PI(p[2]),MaskPalette=PI(p[3]),HatDrawable=PI(p[4]),HatTexture=PI(p[5]),GlassesDrawable=PI(p[6]),GlassesTexture=PI(p[7])};}}catch{}}
        private void SaveAccessories(){try{File.WriteAllLines(AccessoryPath,_accessories.Select(p=>p.Key+"|"+p.Value.MaskDrawable+"|"+p.Value.MaskTexture+"|"+p.Value.MaskPalette+"|"+p.Value.HatDrawable+"|"+p.Value.HatTexture+"|"+p.Value.GlassesDrawable+"|"+p.Value.GlassesTexture+"|v1").ToArray());}catch{}}
        private static string Join(int[] a){return string.Join(",",a);}
        private static void ParseArray(string s,int[] a){string[] p=s.Split(',');for(int i=0;i<a.Length&&i<p.Length;i++)int.TryParse(p[i],out a[i]);}
        private static int PI(string s){int v;return int.TryParse(s,out v)?v:0;}
        private static string Encode(string s){return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s??""));}
        private static string Decode(string s){try{return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));}catch{return "Tenue";}}

        private void OnAborted(object sender,EventArgs e){SaveAll();CloseAll(Game.LocalPlayerPed);}
        private static void Log(string text){try{Directory.CreateDirectory(DataDir);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+text+Environment.NewLine);}catch{}}
    }
}
