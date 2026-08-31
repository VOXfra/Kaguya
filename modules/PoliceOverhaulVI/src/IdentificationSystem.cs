using GTA;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal static class IdentificationSystem
    {
        public static void Observe(Ped player, CaseMemory memory, ObservationSource source, float distance, int wanted, Config cfg)
        {
            if (player == null || !player.Exists() || memory == null) return;
            bool camera = source == ObservationSource.CCTV;
            bool police = source == ObservationSource.Police;
            bool masked = OutfitSignature.FaceObscured(player);

            float quality = camera ? 1.08f : (police ? 1.0f : 0.72f);
            float faceRange = Math.Max(1f, cfg.FaceRecognitionDistance * (camera ? 1.25f : 1f));
            float outfitRange = Math.Max(1f, cfg.OutfitRecognitionDistance);

            if (distance <= outfitRange || camera)
            {
                float q = camera ? 0.86f : Math.Max(0.20f, 1f - distance / outfitRange * 0.65f);
                memory.OutfitKnown = true;
                memory.Outfit = OutfitSignature.Capture(player);
                memory.OutfitConfidence = Math.Max(memory.OutfitConfidence, Clamp100(30f + 38f * q));
            }

            if (!masked && distance <= faceRange)
            {
                float q = Math.Max(0.15f, 1f - distance / faceRange * 0.72f);
                float gain = (camera ? 58f : police ? 52f : 34f) * q * quality;
                memory.FaceConfidence = Math.Max(memory.FaceConfidence, Clamp100(32f + gain));
            }
            else if (masked)
            {
                // A mask prevents a face match. Clothing, exposed hands/skin and
                // body shape remain descriptive evidence, never unique identity.
                memory.FaceConfidence = Math.Min(memory.FaceConfidence, cfg.MaskedFaceConfidenceCap);
            }

            if (player.IsInVehicle() && distance <= Math.Max(cfg.VehicleRecognitionDistance, camera ? cfg.CctvScanRadius : 0f))
            {
                bool plateKnown = camera || distance <= cfg.PlateRecognitionDistance ||
                                  (cfg.PoliceANPR && police && distance <= cfg.VehicleRecognitionDistance);
                VehicleSignature sig = VehicleSignature.Capture(player.CurrentVehicle, plateKnown);
                if (sig != null)
                {
                    if (memory.Vehicle != null && memory.Vehicle.ModelHash == sig.ModelHash && memory.Vehicle.PlateKnown)
                        sig.PlateKnown = true;
                    memory.Vehicle = sig;
                    memory.VehicleConfidence = Math.Max(memory.VehicleConfidence, plateKnown ? 82f : 48f);
                    TrackerSystem.AttachKnowledgeIfApplicable(memory, player, cfg);
                }
            }

            float identity = 0f;
            identity += memory.FaceConfidence * 0.72f;
            identity += Math.Min(memory.OutfitConfidence, 55f) * 0.13f;
            identity += Math.Min(memory.VehicleConfidence, 85f) * 0.08f;
            identity += Math.Min(memory.Notoriety, 100f) * 0.10f;
            if (masked) identity -= 24f;
            if (memory.MostWanted) identity += 8f;
            memory.IdentityConfidence = Math.Max(memory.IdentityConfidence, Clamp100(identity));
            memory.FaceKnown = memory.FaceConfidence >= cfg.FaceKnownThreshold;
            memory.IdentityConfirmed = memory.IdentityConfidence >= ConfirmationThreshold(memory, cfg);

            if (wanted >= 4)
                AddNotoriety(memory, wanted == 5 ? 2.2f : 1.0f, cfg);
        }

        public static float MatchConfidence(Ped player, CaseMemory memory, float distance, bool policeObserver, Config cfg)
        {
            if (player == null || !player.Exists() || memory == null) return 0f;
            bool masked = OutfitSignature.FaceObscured(player);
            float score = 0f;

            if (!masked && memory.FaceConfidence >= 20f && distance <= cfg.FaceRecognitionDistance)
            {
                float q = Math.Max(0.25f, 1f - distance / Math.Max(1f, cfg.FaceRecognitionDistance) * 0.55f);
                score += memory.FaceConfidence * 0.78f * q;
            }

            if (memory.Vehicle != null && player.IsInVehicle() && distance <= cfg.VehicleRecognitionDistance)
            {
                bool requirePlate = memory.Vehicle.PlateKnown && (cfg.PoliceANPR || !policeObserver);
                if (memory.Vehicle.Matches(player.CurrentVehicle, requirePlate))
                    score += requirePlate ? 37f : 18f;
            }

            if (memory.OutfitKnown && memory.Outfit != null && distance <= cfg.OutfitRecognitionDistance && memory.Outfit.Matches(player))
                score += Math.Min(24f, memory.OutfitConfidence * 0.32f);

            score += Math.Min(15f, memory.Notoriety * 0.15f);
            if (masked) score -= 20f;
            if (memory.MostWanted) score += 8f;
            return Clamp100(score);
        }

        public static bool IsConfirmedMatch(float matchConfidence, CaseMemory memory, Config cfg)
        {
            return matchConfidence >= ConfirmationThreshold(memory, cfg);
        }

        public static int ConfirmationDelay(float matchConfidence, Config cfg)
        {
            float c = Clamp100(matchConfidence);
            int span = Math.Max(0, cfg.IdentityMaxConfirmationMs - cfg.IdentityMinConfirmationMs);
            return cfg.IdentityMaxConfirmationMs - (int)(span * c / 100f);
        }

        public static float ConfirmationThreshold(CaseMemory memory, Config cfg)
        {
            if (memory != null && memory.MostWanted) return Math.Max(45f, cfg.MostWantedIdentityThreshold);
            return Math.Max(55f, cfg.IdentityConfirmationThreshold);
        }

        public static void AddNotoriety(CaseMemory memory, float amount, Config cfg)
        {
            if (memory == null || Math.Abs(amount) < 0.001f) return;
            memory.Notoriety = Clamp100(memory.Notoriety + amount);
            memory.MostWanted = memory.Notoriety >= cfg.MostWantedNotorietyThreshold || memory.ThreatLevel >= 6;
        }

        private static float Clamp100(float v) { return Math.Max(0f, Math.Min(100f, v)); }
    }
}
