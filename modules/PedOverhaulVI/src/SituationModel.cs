using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PedOverhaulVI
{
    internal sealed class PerceptionFrame
    {
        public float DistanceToPlayer;
        public float VisualQuality;
        public bool SeesPlayer;
        public bool SeesWeapon;
        public bool SeesMask;
        public bool DirectlyAimedAt;
        public bool SeesShooting;
        public bool HearsGunshot;
        public bool SeesBody;
        public bool CrowdPanic;
        public bool QuietWithdrawal;
        public bool HostileRelationship;
        public bool ThreatSourceKnown;
        public int SocialSourceHandle;
        public Vector3 ThreatPosition;

        public bool HasAnyStimulus
        {
            get
            {
                return SeesMask || SeesWeapon || DirectlyAimedAt || SeesShooting || HearsGunshot || SeesBody || CrowdPanic || QuietWithdrawal || HostileRelationship;
            }
        }
    }

    internal static class SituationModel
    {
        public static PerceptionFrame Sense(Ped observer, Ped player, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg)
        {
            var f = new PerceptionFrame();
            if (observer == null || !observer.Exists() || player == null || !player.Exists()) return f;

            f.DistanceToPlayer = Distance(observer.Position, player.Position);
            f.VisualQuality = VisualQuality(observer, player, cfg, f.DistanceToPlayer);
            f.SeesPlayer = f.VisualQuality > 0.01f;

            bool playerShooting = SafeBool(Hash.IS_PED_SHOOTING, player.Handle);
            bool playerArmed = VisibleWeaponDrawn(player);
            bool playerMasked = FaceObscured(player);

            if (f.SeesPlayer)
            {
                f.SeesWeapon = playerArmed && f.DistanceToPlayer <= cfg.WeaponRecognitionRadius;
                f.SeesMask = playerMasked && f.DistanceToPlayer <= cfg.MaskRecognitionRadius;
                f.SeesShooting = playerShooting;
                f.ThreatSourceKnown = f.SeesWeapon || f.SeesShooting;
                f.ThreatPosition = player.Position;
            }

            f.DirectlyAimedAt = f.DistanceToPlayer <= cfg.AimThreatRadius && PlayerAimingAt(observer);
            if (f.DirectlyAimedAt)
            {
                f.ThreatSourceKnown = true;
                f.ThreatPosition = player.Position;
            }

            f.HearsGunshot = playerShooting && f.DistanceToPlayer <= cfg.GunshotHearingRadius;
            if (f.HearsGunshot && !f.ThreatSourceKnown)
                f.ThreatPosition = ApproximateSoundSource(observer, player, f.DistanceToPlayer);

            f.SeesBody = SeesRelevantBody(observer, player, nearby, cfg);
            SenseSocialCues(observer, nearby, states, cfg, f);
            f.HostileRelationship = IsHostileToPlayer(observer, player);
            return f;
        }

        public static AwarenessStage UpdateCognition(PedState s, PerceptionFrame f, Config cfg, int now)
        {
            if (s == null) return AwarenessStage.Unaware;
            s.Decay(cfg, now);

            AwarenessStage old = s.Stage;
            float dt = s.LastStimulusAt > 0 ? Math.Max(0.08f, Math.Min(0.55f, (now - s.LastStimulusAt) / 1000f)) : 0.25f;
            float alertScale = 0.72f + s.Alertness / 180f;
            float proximity = 1f - Math.Min(1f, f.DistanceToPlayer / Math.Max(1f, cfg.ThreatVisualRadius));
            float selfProtection = 0.65f + s.SelfPreservation / 170f;
            float braveryResistance = 1.22f - s.Bravery / 180f;
            bool meaningful = false;

            if (f.SeesPlayer)
            {
                s.Attention = Add(s.Attention, (4f + 10f * f.VisualQuality) * alertScale * dt);
                s.LastVisualAt = now;
            }

            if (f.SeesMask)
            {
                meaningful = true;
                s.SawMask = true;
                s.Suspicion = Add(s.Suspicion, cfg.MaskSuspicion * f.VisualQuality * alertScale * dt);
                s.Certainty = Add(s.Certainty, 3f * f.VisualQuality * dt);
            }

            if (f.SeesWeapon)
            {
                meaningful = true;
                s.SawWeapon = true;
                s.Suspicion = Add(s.Suspicion, cfg.VisibleWeaponSuspicion * f.VisualQuality * alertScale * dt);
                s.Certainty = Add(s.Certainty, 22f * f.VisualQuality * dt);
                s.Fear = Add(s.Fear, (13f + 25f * proximity) * selfProtection * braveryResistance * dt);
            }

            if (f.SeesMask && f.SeesWeapon)
            {
                // This combination is the Lucia example: suspicious enough to
                // change behaviour before a shot is fired, but not necessarily panic.
                meaningful = true;
                s.Suspicion = Add(s.Suspicion, cfg.MaskWeaponCombinationBonus * f.VisualQuality * dt);
                s.Certainty = Add(s.Certainty, 16f * f.VisualQuality * dt);
            }

            if (f.DirectlyAimedAt)
            {
                meaningful = true;
                s.WasDirectlyAimedAt = true;
                s.Certainty = Math.Max(s.Certainty, 91f);
                s.Suspicion = Math.Max(s.Suspicion, cfg.ThreatConfirmedThreshold + 10f);
                s.Fear = Add(s.Fear, cfg.DirectAimThreat * selfProtection * braveryResistance * (0.55f + 0.45f * proximity) * dt);
                s.LastConfirmedThreatAt = now;
            }

            if (f.SeesShooting)
            {
                meaningful = true;
                s.SawViolence = true;
                s.Certainty = Math.Max(s.Certainty, 98f);
                s.Suspicion = Math.Max(s.Suspicion, 92f);
                s.Fear = Add(s.Fear, cfg.VisibleShootingThreat * selfProtection * braveryResistance * (0.45f + 0.55f * proximity) * dt);
                s.LastConfirmedThreatAt = now;
            }

            if (f.HearsGunshot)
            {
                meaningful = true;
                s.HeardGunshot = true;
                s.LastGunshotAt = now;
                float hearingCertainty = f.ThreatSourceKnown ? 24f : 10f;
                s.Attention = Add(s.Attention, 35f * alertScale * dt);
                s.Suspicion = Add(s.Suspicion, cfg.HeardGunshotThreat * alertScale * dt);
                s.Certainty = Add(s.Certainty, hearingCertainty * dt);
                s.Fear = Add(s.Fear, cfg.HeardGunshotThreat * selfProtection * braveryResistance * (0.35f + 0.65f * proximity) * dt);
            }

            if (f.SeesBody)
            {
                meaningful = true;
                s.SawBody = true;
                s.LastBodySeenAt = now;
                s.Attention = Add(s.Attention, 25f * dt);
                s.Suspicion = Add(s.Suspicion, cfg.DeadBodySuspicion * alertScale * dt);
                s.Certainty = Add(s.Certainty, 18f * dt);
                s.Fear = Add(s.Fear, 22f * selfProtection * braveryResistance * dt);
            }

            if (f.CrowdPanic)
            {
                meaningful = true;
                s.SawCrowdPanic = true;
                s.LastSocialCueAt = now;
                s.SocialSourceHandle = f.SocialSourceHandle;
                s.SocialThreatConfidence = Math.Max(s.SocialThreatConfidence, 45f + s.Conformity * 0.35f);
                s.Attention = Add(s.Attention, 18f * dt);
                s.Suspicion = Add(s.Suspicion, cfg.CrowdPanicThreat * (0.55f + s.Conformity / 200f) * dt);
                s.Fear = Add(s.Fear, cfg.CrowdPanicThreat * selfProtection * (0.55f + s.Conformity / 190f) * dt);
            }

            if (f.QuietWithdrawal)
            {
                meaningful = true;
                s.SawQuietWithdrawal = true;
                s.LastSocialCueAt = now;
                s.SocialSourceHandle = f.SocialSourceHandle;
                s.SocialThreatConfidence = Math.Max(s.SocialThreatConfidence, 18f + s.Conformity * 0.25f);
                s.Suspicion = Add(s.Suspicion, cfg.QuietWithdrawalSuspicion * (0.55f + s.Conformity / 200f) * dt);
            }

            if (f.HostileRelationship)
            {
                meaningful = true;
                s.Suspicion = Add(s.Suspicion, cfg.HostileRelationshipSuspicion * dt);
            }

            if (meaningful)
            {
                if (s.FirstNoticedAt == 0) s.FirstNoticedAt = now;
                s.LastStimulusAt = now;
                if (f.ThreatPosition != Vector3.Zero) s.LastThreatPosition = f.ThreatPosition;
                if (f.ThreatSourceKnown) s.ThreatSourceKnown = true;
            }

            s.Stage = DetermineStage(s, cfg);
            return old;
        }

        public static AwarenessStage DetermineStage(PedState s, Config cfg)
        {
            float threatBelief = Math.Max(s.Suspicion, s.Certainty * 0.88f);
            if (s.Fear >= cfg.PanicThreshold && s.Certainty >= cfg.ConcernedThreshold) return AwarenessStage.Panic;
            if (s.Certainty >= cfg.ThreatConfirmedThreshold || s.SawViolence || s.WasDirectlyAimedAt) return AwarenessStage.ThreatConfirmed;
            if (threatBelief >= cfg.ConcernedThreshold || s.Fear >= cfg.ConcernedThreshold) return AwarenessStage.Concerned;
            if (threatBelief >= cfg.SuspiciousThreshold) return AwarenessStage.Suspicious;
            if (s.Attention >= cfg.NoticedThreshold || threatBelief >= cfg.NoticedThreshold) return AwarenessStage.Noticed;
            return AwarenessStage.Unaware;
        }

        public static bool VisibleWeaponDrawn(Ped player)
        {
            if (player == null || !player.Exists()) return false;
            try
            {
                int current = Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON, player.Handle);
                int unarmed = Function.Call<int>(Hash.GET_HASH_KEY, "WEAPON_UNARMED");
                return current != 0 && current != unarmed && Function.Call<bool>(Hash.IS_PED_ARMED, player.Handle, 7);
            }
            catch { return false; }
        }

        public static bool FaceObscured(Ped ped)
        {
            try
            {
                int mask = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped.Handle, 1);
                int helmet = Function.Call<int>(Hash.GET_PED_PROP_INDEX, ped.Handle, 0);
                return mask != 0 || helmet > 0;
            }
            catch { return false; }
        }

        public static bool IsHostileToPlayer(Ped p, Ped player)
        {
            try
            {
                if (Function.Call<bool>(Hash.IS_PED_IN_COMBAT, p.Handle, player.Handle)) return true;
                int rel = Function.Call<int>(Hash.GET_RELATIONSHIP_BETWEEN_PEDS, p.Handle, player.Handle);
                return rel >= 4 && rel <= 5;
            }
            catch { return false; }
        }

        private static float VisualQuality(Ped observer, Ped target, Config cfg, float distance)
        {
            if (distance > cfg.ThreatVisualRadius) return 0f;
            try { if (!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, observer.Handle, target.Handle, 17)) return 0f; }
            catch { return 0f; }

            Vector3 f = observer.ForwardVector;
            Vector3 from = observer.Position;
            Vector3 to = target.Position;
            double dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 0.01) return 1f;
            double dot = (f.X * dx + f.Y * dy + f.Z * dz) / len;
            double central = Math.Cos(Math.Max(20f, Math.Min(170f, cfg.CentralVisualFovDegrees)) * 0.5 * Math.PI / 180.0);
            double peripheral = Math.Cos(Math.Max(30f, Math.Min(179f, cfg.PeripheralVisualFovDegrees)) * 0.5 * Math.PI / 180.0);
            float distanceQuality = Math.Max(0.20f, 1f - distance / Math.Max(1f, cfg.ThreatVisualRadius) * 0.72f);
            if (dot >= central) return distanceQuality;
            if (dot >= peripheral) return distanceQuality * 0.42f;
            return 0f;
        }

        private static bool SeesRelevantBody(Ped observer, Ped player, IList<Ped> nearby, Config cfg)
        {
            if (nearby == null) return false;
            foreach (Ped p in nearby)
            {
                if (p == null || !p.Exists() || !p.IsDead || !p.IsHuman || p.Handle == observer.Handle) continue;
                if (Distance(observer.Position, p.Position) > cfg.BodyAwarenessRadius) continue;
                try { if (Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, observer.Handle, p.Handle, 17)) return true; }
                catch { }
            }
            return false;
        }

        private static void SenseSocialCues(Ped observer, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg, PerceptionFrame f)
        {
            if (nearby == null || states == null) return;
            float best = float.MaxValue;
            int bestHandle = 0;
            bool panic = false;
            bool quiet = false;
            foreach (Ped other in nearby)
            {
                if (other == null || !other.Exists() || other.Handle == observer.Handle || other.IsDead) continue;
                float d = Distance(observer.Position, other.Position);
                if (d > cfg.SocialAwarenessRadius) continue;
                PedState os;
                if (!states.TryGetValue(other.Handle, out os)) continue;

                bool otherPanicking = os.Stage == AwarenessStage.Panic || os.Mode == ReactionMode.Flee || os.Mode == ReactionMode.Cower || os.Mode == ReactionMode.Cover;
                bool otherQuiet = os.Mode == ReactionMode.DiscreetLeave || os.Mode == ReactionMode.AlertNearby;
                if (!otherPanicking && !otherQuiet) continue;

                bool visible = d <= 5f;
                if (!visible)
                {
                    try { visible = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, observer.Handle, other.Handle, 17); }
                    catch { }
                }
                if (!visible) continue;

                if (d < best)
                {
                    best = d;
                    bestHandle = other.Handle;
                    panic = otherPanicking;
                    quiet = !panic && otherQuiet && d <= cfg.QuietWithdrawalAwarenessRadius;
                }
            }
            f.CrowdPanic = panic;
            f.QuietWithdrawal = quiet;
            f.SocialSourceHandle = bestHandle;
        }

        private static bool PlayerAimingAt(Ped target)
        {
            try
            {
                if (!Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle)) return false;
                var arg = new OutputArgument();
                if (!Function.Call<bool>(Hash.GET_ENTITY_PLAYER_IS_FREE_AIMING_AT, Game.Player.Handle, arg)) return false;
                return arg.GetResult<int>() == target.Handle;
            }
            catch { return false; }
        }

        private static bool SafeBool(Hash h, int entity)
        {
            try { return Function.Call<bool>(h, entity); }
            catch { return false; }
        }

        private static Vector3 ApproximateSoundSource(Ped observer, Ped player, float distance)
        {
            Vector3 p = player.Position;
            int seed = unchecked(observer.Handle * 1103515245 + Game.GameTime / 1000 * 97);
            double angle = (seed & 1023) / 1023.0 * Math.PI * 2.0;
            float error = Math.Min(16f, 3f + distance * 0.12f);
            return new Vector3(p.X + (float)Math.Cos(angle) * error, p.Y + (float)Math.Sin(angle) * error, p.Z);
        }

        public static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        private static float Add(float value, float amount)
        {
            return Math.Max(0f, Math.Min(100f, value + amount));
        }
    }
}
