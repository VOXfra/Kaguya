using GTA;
using GTA.Math;

namespace VOX.PoliceOverhaulVI
{
    internal static class PoliceSearchRuntimeState
    {
        private static CaseMemory _boundCase;
        private static int _boundModel;
        private static CaseMemory _fallbackCase;
        private static int _fallbackModel;

        public static bool SearchActive;
        public static int ThreatLevel;
        public static int SearchStartedAt;
        public static int SearchDeadlineAt;
        public static int LastDirectObservationAt;
        public static int LastTrackerPingAt;
        public static Vector3 LastKnownPosition;

        public static OutfitSignature ActiveOutfit;
        public static bool ActiveOutfitValid;
        public static VehicleSignature ActiveVehicle;
        public static bool ActiveVehicleValid;
        public static bool MaskDescriptorValid;
        public static bool MaskedDescriptor;

        public static int CandidateKey;
        public static int CandidateSince;
        public static float CandidateConfidence;

        public static void BindCase(CaseMemory memory)
        {
            if (memory == null) return;
            _boundCase = memory;
            _boundModel = memory.SuspectModelHash;
        }

        public static CaseMemory CaseFor(Ped player)
        {
            if (player == null || !player.Exists()) return _boundCase;
            int model = player.Model.Hash;
            if (_boundCase != null && _boundModel == model) return _boundCase;
            if (_fallbackCase == null || _fallbackModel != model)
            {
                _fallbackModel = model;
                _fallbackCase = new CaseMemory { SuspectModelHash = model };
            }
            return _fallbackCase;
        }

        public static void CaptureActiveSignalment(Ped player, CaseMemory memory, bool plateKnown)
        {
            if (player == null || !player.Exists()) return;
            if (memory != null) BindCase(memory);

            ActiveOutfit = OutfitSignature.Capture(player);
            ActiveOutfitValid = ActiveOutfit != null;
            MaskedDescriptor = OutfitSignature.FaceObscured(player);
            MaskDescriptorValid = true;

            ActiveVehicle = null;
            ActiveVehicleValid = false;
            if (player.IsInVehicle())
            {
                Vehicle vehicle = player.CurrentVehicle;
                if (vehicle != null && vehicle.Exists())
                {
                    ActiveVehicle = VehicleSignature.Capture(vehicle, plateKnown);
                    ActiveVehicleValid = ActiveVehicle != null;
                    if (ActiveVehicle != null && memory != null && memory.Vehicle != null && memory.Vehicle.Matches(vehicle, false))
                    {
                        ActiveVehicle.TrackerPresent = memory.Vehicle.TrackerPresent;
                        ActiveVehicle.TrackerKnownByPolice = memory.Vehicle.TrackerKnownByPolice;
                        if (memory.Vehicle.PlateKnown) ActiveVehicle.PlateKnown = true;
                    }
                }
            }
        }

        public static void InvalidateChangedSignalment(Ped player)
        {
            if (player == null || !player.Exists()) return;

            if (ActiveOutfitValid && (ActiveOutfit == null || !ActiveOutfit.Matches(player)))
                ActiveOutfitValid = false;

            if (MaskDescriptorValid && OutfitSignature.FaceObscured(player) != MaskedDescriptor)
                MaskDescriptorValid = false;

            if (ActiveVehicleValid)
            {
                if (!player.IsInVehicle()) ActiveVehicleValid = false;
                else
                {
                    Vehicle vehicle = player.CurrentVehicle;
                    if (vehicle == null || !vehicle.Exists() || ActiveVehicle == null || !ActiveVehicle.Matches(vehicle, ActiveVehicle.PlateKnown))
                        ActiveVehicleValid = false;
                }
            }
        }

        public static void ResetCandidate()
        {
            CandidateKey = 0;
            CandidateSince = 0;
            CandidateConfidence = 0f;
        }

        public static void ResetSearch(bool clearSignalment)
        {
            SearchActive = false;
            ThreatLevel = 0;
            SearchStartedAt = 0;
            SearchDeadlineAt = 0;
            LastDirectObservationAt = 0;
            LastTrackerPingAt = 0;
            LastKnownPosition = Vector3.Zero;
            ResetCandidate();
            if (!clearSignalment) return;
            ActiveOutfit = null;
            ActiveOutfitValid = false;
            ActiveVehicle = null;
            ActiveVehicleValid = false;
            MaskDescriptorValid = false;
            MaskedDescriptor = false;
        }
    }
}
