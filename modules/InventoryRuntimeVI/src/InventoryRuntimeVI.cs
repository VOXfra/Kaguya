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
        private const int InputReload = 45;

        private sealed class WeaponEntry
        {
            public int Hash;
            public int Ammo;
        }

        private readonly Dictionary<string, List<WeaponEntry>> _trunks = new Dictionary<string, List<WeaponEntry>>(StringComparer.OrdinalIgnoreCase);
        private int _actionVehicle;
        private int _actionStarted;
        private bool _actionRetrieve;
        private int _lastHelp;
        private int _lastSave;

        public InventoryRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Load();
            Interval = 50;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Inventory Runtime VI 0.1.0 loaded: persistent physical vehicle-trunk weapon storage.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { ResetAction(); return; }
                if (RockstarOwnsScene()) { ResetAction(); return; }
                if (player.IsInVehicle()) { ResetAction(); return; }

                Vehicle vehicle = FindNearestRearVehicle(player.Position, 3.4f, out float rearDistance);
                if (vehicle == null || !vehicle.Exists() || rearDistance > 2.35f || IsMissionEntity(vehicle))
                {
                    ResetAction();
                    return;
                }

                try { Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, InputReload, true); } catch { }

                string key = VehicleKey(vehicle);
                List<WeaponEntry> trunk = GetTrunk(key);
                int unarmed = SafeHash("WEAPON_UNARMED");
                int selected = SafeSelectedWeapon(player);
                bool armed = selected != 0 && selected != unarmed;

                if (armed)
                    ShowHelp("Maintenez ~INPUT_RELOAD~ pres du coffre pour ranger l'arme equipee.");
                else if (trunk.Count > 0)
                    ShowHelp("Maintenez ~INPUT_RELOAD~ pres du coffre pour recuperer la derniere arme rangee.");
                else
                {
                    ResetAction();
                    return;
                }

                bool pressed = IsDisabledControlPressed(InputReload);
                if (!pressed) { ResetAction(); return; }

                bool retrieve = !armed;
                if (_actionVehicle != vehicle.Handle || _actionRetrieve != retrieve)
                {
                    _actionVehicle = vehicle.Handle;
                    _actionRetrieve = retrieve;
                    _actionStarted = Game.GameTime;
                    return;
                }
                if (Game.GameTime - _actionStarted < 850) return;

                if (retrieve) RetrieveWeapon(player, key, trunk);
                else StoreWeapon(player, key, trunk, selected);
                ResetAction();

                if (Game.GameTime - _lastSave > 1000)
                {
                    _lastSave = Game.GameTime;
                    Save();
                }
            }
            catch (Exception ex) { Log("Tick error: " + ex.Message); }
        }

        private void StoreWeapon(Ped player, string key, List<WeaponEntry> trunk, int weaponHash)
        {
            int ammo = 0;
            try { ammo = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, player.Handle, weaponHash); } catch { }
            trunk.Add(new WeaponEntry { Hash = weaponHash, Ammo = Math.Max(0, ammo) });
            try { Function.Call(Hash.REMOVE_WEAPON_FROM_PED, player.Handle, weaponHash); } catch { }
            Save();
            Log("Stored weapon " + weaponHash + " ammo=" + ammo + " trunk=" + key + ".");
        }

        private void RetrieveWeapon(Ped player, string key, List<WeaponEntry> trunk)
        {
            if (trunk.Count == 0) return;
            int index = trunk.Count - 1;
            WeaponEntry entry = trunk[index];
            trunk.RemoveAt(index);
            try { Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, entry.Hash, Math.Max(0, entry.Ammo), false, true); } catch { }
            Save();
            Log("Retrieved weapon " + entry.Hash + " ammo=" + entry.Ammo + " trunk=" + key + ".");
        }

        private List<WeaponEntry> GetTrunk(string key)
        {
            List<WeaponEntry> list;
            if (!_trunks.TryGetValue(key, out list))
            {
                list = new List<WeaponEntry>();
                _trunks[key] = list;
            }
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
                Vector3 rear;
                try { rear = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS, v.Handle, 0f, -2.25f, 0f); }
                catch { rear = v.Position; }
                float d = Distance(playerPos, rear);
                if (d < bestRearDistance) { bestRearDistance = d; best = v; }
            }
            return best;
        }

        private static int SafeSelectedWeapon(Ped p)
        {
            try { return Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON, p.Handle); } catch { return 0; }
        }

        private static int SafeHash(string name)
        {
            try { return Function.Call<int>(Hash.GET_HASH_KEY, name); } catch { return 0; }
        }

        private static string VehicleKey(Vehicle v)
        {
            string plate = string.Empty;
            try { plate = (Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, v.Handle) ?? string.Empty).Trim().ToUpperInvariant(); } catch { }
            return v.Model.Hash.ToString(CultureInfo.InvariantCulture) + ":" + plate;
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
                    GetTrunk(p[0]).Add(new WeaponEntry { Hash = hash, Ammo = Math.Max(0, ammo) });
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
                    foreach (WeaponEntry w in pair.Value)
                        lines.Add(pair.Key + "|" + w.Hash + "|" + w.Ammo);
                File.WriteAllLines(InventoryPath, lines.ToArray());
            }
            catch (Exception ex) { Log("Inventory save failed safely: " + ex.Message); }
        }

        private static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        private void ResetAction() { _actionVehicle = 0; _actionStarted = 0; _actionRetrieve = false; }
        private void OnAborted(object sender, EventArgs e) { Save(); ResetAction(); }
        private static void Log(string text)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine); }
            catch { }
        }
    }
}
