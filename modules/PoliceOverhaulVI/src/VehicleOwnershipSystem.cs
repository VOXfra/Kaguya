using GTA;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal static class VehicleOwnershipSystem
    {
        private const string PlayerVehicleDecorator = "Player_Vehicle";

        public static bool IsRegisteredToCurrentPlayer(Ped player, Vehicle vehicle)
        {
            if (player == null || !player.Exists() || vehicle == null || !vehicle.Exists()) return false;

            // A vehicle explicitly marked stolen never bills the current driver
            // through automated owner-based enforcement.
            try { if (Function.Call<bool>(Hash.IS_VEHICLE_STOLEN, vehicle.Handle)) return false; } catch { }

            // Rockstar game code uses the Player_Vehicle decorator on vehicles
            // treated as player vehicles. Prefer it when present.
            try
            {
                if (Function.Call<bool>(Hash.DECOR_EXIST_ON, vehicle.Handle, PlayerVehicleDecorator)) return true;
            }
            catch { }

            // Story protagonists' canonical personal vehicles remain a safe
            // fallback when the decorator is not exposed on a particular build.
            int pedModel = player.Model.Hash;
            int vehicleModel = vehicle.Model.Hash;
            try
            {
                int michael = Function.Call<int>(Hash.GET_HASH_KEY, "player_zero");
                int franklin = Function.Call<int>(Hash.GET_HASH_KEY, "player_one");
                int trevor = Function.Call<int>(Hash.GET_HASH_KEY, "player_two");
                if (pedModel == michael && vehicleModel == Function.Call<int>(Hash.GET_HASH_KEY, "tailgater")) return true;
                if (pedModel == franklin && (vehicleModel == Function.Call<int>(Hash.GET_HASH_KEY, "buffalo2") || vehicleModel == Function.Call<int>(Hash.GET_HASH_KEY, "bagger"))) return true;
                if (pedModel == trevor && vehicleModel == Function.Call<int>(Hash.GET_HASH_KEY, "bodhi2")) return true;
            }
            catch { }

            return false;
        }
    }
}
