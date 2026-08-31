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
                float baseConfidence = police ? 40f : 30f;
                float gain = police ? 48f : 38f;
                memory.OutfitConfidence = Math.Max(memory.OutfitConfidence, Clamp100(baseConfidence + gain * q));
            }

            if (!masked && distance <= faceRange)
            {
                float q = Math.Max(0.15f, 1f - distance / faceRange * 0.72f);
                float gain = (camera ? 58f : police ? 56f : 34f) * q * quality;
                memory.FaceConfidence = Math.Max(memory.FaceConfidence, Clamp100(32f + gain));
            }
            else if (masked)
            {
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
                    memory.VehicleConfidence = Math.Max(memory.VehicleConfidence, plateKnown ? 90f : 52f);
                    TrackerSystem.AttachKnowledgeIfApplicable(memory, player, cfg);
                }
            }

            RecalculateIdentity(memory, masked, cfg);
            if (wanted >= 4) AddNotoriety(memory, wanted == 5 ? 2.2f : 1.0f, cfg);
        }

        public static float MatchConfidence(Ped player, CaseMemory memory, float distance, bool policeObserver, Config cfg)
        {
            if (player == null || !player.Exists() || memory == null) return 0f;
            bool masked = OutfitSignature.FaceObscured(player);
            float score = 0f;
            bool faceMatch = false, outfitMatch = false, vehicleMatch = false, plateMatch = false;

            if (!masked && memory.FaceConfidence >= 20f && distance <= cfg.FaceRecognitionDistance)
            {
                float q = Math.Max(0.25f, 1f - distance / Math.Max(1f, cfg.FaceRecognitionDistance) * 0.55f);
                score += memory.FaceConfidence * 0.82f * q;
                faceMatch = true;
            }

            if (memory.Vehicle != null && player.IsInVehicle() && distance <= cfg.VehicleRecognitionDistance)
            {
                bool requirePlate = memory.Vehicle.PlateKnown && (cfg.PoliceANPR || !policeObserver);
                if (memory.Vehicle.Matches(player.CurrentVehicle, requirePlate))
                {
                    vehicleMatch = true;
                    plateMatch = requirePlate;
                    score += requirePlate ? 46f : 24f;
                }
            }

            if (memory.OutfitKnown && memory.Outfit != null && distance <= cfg.OutfitRecognitionDistance && memory.Outfit.Matches(player))
            {
                outfitMatch = true;
                score += Math.Min(40f, memory.OutfitConfidence * 0.46f);
            }

            // A nearby officer seeing the same flagged clothes in the exact
            // flagged car should be able to form a reasonable match even when
            // the original crime never produced a clean face image.
            if (policeObserver && outfitMatch && vehicleMatch)
            {
                score += distance <= 12f ? 18f : 10f;
                if (plateMatch) score += 8f;
            }
            else if (policeObserver && faceMatch && (outfitMatch || vehicleMatch)) score += 8f;

            score += Math.Min(15f, memory.Notoriety * 0.15f);
            if (masked) score -= faceMatch ? 10f : 16f;
            if (memory.MostWanted) score += 10f;
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
            if (memory == null) return Math.Max(55f, cfg.IdentityConfirmationThreshold);
            if (memory.MostWanted) return Math.Max(52f, cfg.MostWantedIdentityThreshold);
            if (memory.Vehicle != null && memory.Vehicle.PlateKnown && memory.OutfitKnown)
                return Math.Min(Math.Max(62f, cfg.IdentityConfirmationThreshold - 16f), 72f);
            if (memory.FaceKnown) return Math.Max(68f, cfg.IdentityConfirmationThreshold - 8f);
            return Math.Max(72f, cfg.IdentityConfirmationThreshold);
        }

        public static void AddNotoriety(CaseMemory memory, float amount, Config cfg)
        {
            if (memory == null || Math.Abs(amount) < 0.001f) return;
            memory.Notoriety = Clamp100(memory.Notoriety + amount);
            memory.MostWanted = memory.Notoriety >= cfg.MostWantedNotorietyThreshold || memory.ThreatLevel >= 6;
        }

        private static void RecalculateIdentity(CaseMemory memory, bool masked, Config cfg)
        {
            float identity = 0f;
            identity += memory.FaceConfidence * 0.68f;
            identity += Math.Min(memory.OutfitConfidence, 90f) * 0.18f;
            identity += Math.Min(memory.VehicleConfidence, 95f) * 0.12f;
            identity += Math.Min(memory.Notoriety, 100f) * 0.10f;
            if (masked) identity -= 20f;
            if (memory.MostWanted) identity += 8f;
            memory.IdentityConfidence = Math.Max(memory.IdentityConfidence, Clamp100(identity));
            memory.FaceKnown = memory.FaceConfidence >= cfg.FaceKnownThreshold;
            memory.IdentityConfirmed = memory.IdentityConfidence >= ConfirmationThreshold(memory, cfg);
        }

        private static float Clamp100(float v) { return Math.Max(0f, Math.Min(100f, v)); }
    }
}
