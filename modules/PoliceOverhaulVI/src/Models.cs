using GTA;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PoliceOverhaulVI
{
    internal enum ObservationSource
    {
        None = 0,
        Civilian = 1,
        Police = 2,
        CCTV = 3,
        Tracker = 4,
        ANPR = 5,
        HomeSurveillance = 6
    }

    internal sealed class OutfitSignature
    {
        public int[] Drawables = new int[12];
        public int[] Textures = new int[12];

        public static OutfitSignature Capture(Ped ped)
        {
            var result = new OutfitSignature();
            for (int i = 0; i < 12; i++)
            {
                result.Drawables[i] = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped.Handle, i);
                result.Textures[i] = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, ped.Handle, i);
            }
            return result;
        }

        public bool Matches(Ped ped)
        {
            if (ped == null || !ped.Exists()) return false;
            for (int i = 0; i < 12; i++)
            {
                if (Drawables == null || Textures == null || Drawables.Length <= i || Textures.Length <= i) return false;
                if (Drawables[i] != Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped.Handle, i)) return false;
                if (Textures[i] != Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, ped.Handle, i)) return false;
            }
            return true;
        }

        public static bool FaceObscured(Ped ped)
        {
            if (ped == null || !ped.Exists()) return true;
            try { return Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped.Handle, 1) != 0; }
            catch { return true; }
        }
    }

    internal sealed class VehicleSignature
    {
        public int ModelHash;
        public string Plate = string.Empty;
        public bool PlateKnown;
        public int PrimaryColor = -1;
        public int SecondaryColor = -1;
        public bool TrackerPresent;
        public bool TrackerKnownByPolice;

        public static VehicleSignature Capture(Vehicle vehicle, bool plateKnown)
        {
            if (vehicle == null || !vehicle.Exists()) return null;
            int primary = -1, secondary = -1;
            try
            {
                var p = new OutputArgument(); var s = new OutputArgument();
                Function.Call(Hash.GET_VEHICLE_COLOURS, vehicle.Handle, p, s);
                primary = p.GetResult<int>(); secondary = s.GetResult<int>();
            }
            catch { }
            return new VehicleSignature
            {
                ModelHash = vehicle.Model.Hash,
                Plate = NormalizePlate(Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, vehicle.Handle)),
                PlateKnown = plateKnown,
                PrimaryColor = primary,
                SecondaryColor = secondary
            };
        }

        public bool Matches(Vehicle vehicle, bool requirePlateWhenKnown)
        {
            if (vehicle == null || !vehicle.Exists() || vehicle.Model.Hash != ModelHash) return false;
            if (PlateKnown && requirePlateWhenKnown)
            {
                string currentPlate = NormalizePlate(Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, vehicle.Handle));
                return string.Equals(Plate, currentPlate, StringComparison.OrdinalIgnoreCase);
            }
            int primary = -1, secondary = -1;
            try
            {
                var p = new OutputArgument(); var s = new OutputArgument();
                Function.Call(Hash.GET_VEHICLE_COLOURS, vehicle.Handle, p, s);
                primary = p.GetResult<int>(); secondary = s.GetResult<int>();
            }
            catch { }
            if (PrimaryColor >= 0 && (PrimaryColor != primary || SecondaryColor != secondary)) return false;
            if (PlateKnown)
            {
                string currentPlate = NormalizePlate(Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, vehicle.Handle));
                return string.Equals(Plate, currentPlate, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        public static string NormalizePlate(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace(" ", string.Empty);
        }
    }

    internal sealed class CaseMemory
    {
        public int SuspectModelHash;
        public bool Active;
        public bool FaceKnown;
        public bool OutfitKnown;
        public OutfitSignature Outfit;
        public VehicleSignature Vehicle;
        public bool WeaponKnown;
        public int WeaponHash;
        public bool SuspectCountKnown;
        public int SuspectCount = 1;

        public float FaceConfidence;
        public float OutfitConfidence;
        public float VehicleConfidence;
        public float IdentityConfidence;
        public bool IdentityConfirmed;
        public float Notoriety;
        public bool MostWanted;
        public int MajorHeistsKnown;
        public int SurrenderCount;

        public int ThreatLevel;
        public int HeatPoints;
        public int LastWantedEndedAt;
        public long ExpiresUtcTicks;
        public bool WarrantActive;
        public long WarrantExpiresUtcTicks;
        public float LastKnownX, LastKnownY, LastKnownZ;
        public ObservationSource LastSource;
        public int LastObservedGameTime;
        public int UnpaidFines;

        public GTA.Math.Vector3 LastKnownPosition
        {
            get { return new GTA.Math.Vector3(LastKnownX, LastKnownY, LastKnownZ); }
            set { LastKnownX = value.X; LastKnownY = value.Y; LastKnownZ = value.Z; }
        }

        public bool IsExpiredUtc() { return Active && ExpiresUtcTicks > 0 && DateTime.UtcNow.Ticks >= ExpiresUtcTicks; }
        public bool IsWarrantExpiredUtc() { return WarrantActive && WarrantExpiresUtcTicks > 0 && DateTime.UtcNow.Ticks >= WarrantExpiresUtcTicks; }
        public void Touch(Config cfg) { ExpiresUtcTicks = DateTime.UtcNow.AddHours(Math.Max(1, cfg.CaseMemoryHours)).Ticks; }
        public void IssueWarrant(Config cfg)
        {
            WarrantActive = true;
            WarrantExpiresUtcTicks = DateTime.UtcNow.AddHours(Math.Max(1, cfg.WarrantMemoryHours)).Ticks;
            if (WarrantExpiresUtcTicks > ExpiresUtcTicks) ExpiresUtcTicks = WarrantExpiresUtcTicks;
        }
        public void ClearTransientWanted() { HeatPoints = 0; LastWantedEndedAt = 0; }
        public void ClearAll()
        {
            Active = false; FaceKnown = false; OutfitKnown = false; Outfit = null; Vehicle = null;
            WeaponKnown = false; WeaponHash = 0; SuspectCountKnown = false; SuspectCount = 1;
            FaceConfidence = OutfitConfidence = VehicleConfidence = IdentityConfidence = 0f; IdentityConfirmed = false;
            Notoriety = 0f; MostWanted = false; MajorHeistsKnown = 0; SurrenderCount = 0;
            ThreatLevel = 0; HeatPoints = 0; LastWantedEndedAt = 0; ExpiresUtcTicks = 0;
            WarrantActive = false; WarrantExpiresUtcTicks = 0; LastKnownX = LastKnownY = LastKnownZ = 0f;
            LastSource = ObservationSource.None; LastObservedGameTime = 0; UnpaidFines = 0;
        }
    }

    internal sealed class CitationRecord
    {
        public int SuspectModelHash;
        public int Amount;
        public int IssuedAtGameTime;
        public int DeliverAtGameTime;
        public string Reason = string.Empty;
        public string Source = string.Empty;
        public string Street = string.Empty;
        public string CameraId = string.Empty;
        public string VehiclePlate = string.Empty;
        public int VehicleModelHash;
        public int SpeedKph;
        public int LimitKph;
        public int OverKph;
        public bool Delivered;
    }

    internal sealed class CaseRepository
    {
        private readonly Dictionary<int, CaseMemory> _cases = new Dictionary<int, CaseMemory>();
        public IEnumerable<CaseMemory> Cases { get { return _cases.Values; } }
        public CaseMemory GetOrCreate(int suspectModelHash)
        {
            CaseMemory value;
            if (!_cases.TryGetValue(suspectModelHash, out value))
            {
                value = new CaseMemory { SuspectModelHash = suspectModelHash };
                _cases[suspectModelHash] = value;
            }
            return value;
        }
        public void Put(CaseMemory memory) { if (memory != null) _cases[memory.SuspectModelHash] = memory; }
        public void ClearExpired()
        {
            var remove = new List<int>();
            foreach (var pair in _cases)
            {
                if (pair.Value.IsWarrantExpiredUtc()) { pair.Value.WarrantActive = false; pair.Value.WarrantExpiresUtcTicks = 0; }
                if (pair.Value.IsExpiredUtc() && !pair.Value.WarrantActive && pair.Value.UnpaidFines <= 0) remove.Add(pair.Key);
            }
            foreach (int key in remove) _cases.Remove(key);
        }
    }

    internal static class SuspectSnapshot
    {
        public static int CountVisibleSuspects(Ped player)
        {
            if (player == null || !player.Exists() || !player.IsInVehicle()) return 1;
            Vehicle vehicle = player.CurrentVehicle;
            if (vehicle == null || !vehicle.Exists()) return 1;
            int count = 0;
            try
            {
                Ped driver = vehicle.Driver;
                if (driver != null && driver.Exists() && !driver.IsDead) count++;
                int max = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle.Handle);
                for (int seat = 0; seat < max; seat++)
                {
                    int handle = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, vehicle.Handle, seat, false);
                    if (handle != 0 && Function.Call<bool>(Hash.DOES_ENTITY_EXIST, handle)) count++;
                }
            }
            catch { }
            return Math.Max(1, count);
        }
        public static int CurrentWeaponHash(Ped player)
        {
            if (player == null || !player.Exists()) return 0;
            try { return Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON, player.Handle); }
            catch { return 0; }
        }
    }
}
