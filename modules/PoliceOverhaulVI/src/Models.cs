using GTA;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class OutfitSignature
    {
        public readonly int[] Drawables = new int[12];
        public readonly int[] Textures = new int[12];

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
            for (int i = 0; i < 12; i++)
            {
                if (Drawables[i] != Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped.Handle, i))
                    return false;
                if (Textures[i] != Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, ped.Handle, i))
                    return false;
            }
            return true;
        }

        public static bool FaceObscured(Ped ped)
        {
            return Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped.Handle, 1) != 0;
        }
    }

    internal sealed class VehicleSignature
    {
        public int ModelHash;
        public string Plate;

        public static VehicleSignature Capture(Vehicle vehicle)
        {
            if (vehicle == null || !vehicle.Exists())
                return null;

            return new VehicleSignature
            {
                ModelHash = vehicle.Model.Hash,
                Plate = NormalizePlate(Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, vehicle.Handle))
            };
        }

        public bool Matches(Vehicle vehicle)
        {
            if (vehicle == null || !vehicle.Exists())
                return false;
            if (vehicle.Model.Hash != ModelHash)
                return false;
            string currentPlate = NormalizePlate(Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, vehicle.Handle));
            return string.Equals(Plate, currentPlate, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePlate(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace(" ", string.Empty);
        }
    }

    internal sealed class CaseMemory
    {
        public bool Active;
        public bool FaceKnown;
        public bool OutfitKnown;
        public OutfitSignature Outfit;
        public VehicleSignature Vehicle;
        public int ThreatLevel;
        public int LastWantedEndedAt;
        public int ExpiresAt;
        public GTA.Math.Vector3 LastKnownPosition;

        public void Clear()
        {
            Active = false;
            FaceKnown = false;
            OutfitKnown = false;
            Outfit = null;
            Vehicle = null;
            ThreatLevel = 0;
            LastWantedEndedAt = 0;
            ExpiresAt = 0;
        }
    }
}
