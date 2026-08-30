using GTA;
using GTA.Math;
using GTA.Native;
using System;

namespace VOX.PedOverhaulVI
{
    internal enum AwarenessStage
    {
        Unaware = 0,
        Noticed = 1,
        Suspicious = 2,
        Concerned = 3,
        ThreatConfirmed = 4,
        Panic = 5
    }

    internal enum ReactionMode
    {
        None = 0,
        Glance = 1,
        Watch = 2,
        DiscreetLeave = 3,
        AlertNearby = 4,
        Phone = 5,
        Film = 6,
        Freeze = 7,
        Cower = 8,
        Flee = 9,
        Cover = 10,
        Confront = 11,
        Combat = 12,
        Surrender = 13,
        Investigate = 14,
        Evade = 15,
        Assist = 16,
        DriveAway = 17
    }

    internal enum PedArchetype
    {
        Cautious,
        Average,
        Curious,
        Bold,
        Aggressive,
        Protective,
        Detached
    }

    internal sealed class PedState
    {
        public int Handle;
        public int ModelHash;
        public int GroupId = -1;

        public int Bravery;
        public int Curiosity;
        public int Aggression;
        public int Alertness;
        public int SelfPreservation;
        public int Conformity;
        public int Empathy;
        public PedArchetype Archetype;

        public float Morale = 100f;
        public int LastHealth;
        public bool NearbyDeathCounted;

        public float Attention;
        public float Suspicion;
        public float Certainty;
        public float Fear;
        public AwarenessStage Stage;
        public ReactionMode Mode;

        public bool SawWeapon;
        public bool SawMask;
        public bool SawViolence;
        public bool SawBody;
        public bool WasDirectlyAimedAt;
        public bool HeardGunshot;
        public bool ThreatSourceKnown;
        public bool SawCrowdPanic;
        public bool SawQuietWithdrawal;

        public DistractionKind Distraction;
        public float DistractionLevel;
        public float VisualAttentionScale = 1f;
        public float HearingAttentionScale = 1f;
        public float SocialAttentionScale = 1f;
        public int LastDistractionProbeAt;
        public int DistractionReactionUntil;

        public SceneThreatKind SceneThreatKind;
        public int ThreatSourceHandle;
        public float ExternalThreatConfidence;
        public bool SawExternalFight;
        public bool SawExternalWeapon;
        public bool HeardExternalGunfire;
        public bool SawFire;
        public bool HeardExplosion;
        public bool SawVehicleHazard;
        public float VehicleHazardTtc = 99f;
        public int LastSceneEventAt;

        public Vector3 LastThreatPosition;
        public Vector3 LastSafeDirection;

        public int FirstNoticedAt;
        public int LastStimulusAt;
        public int LastVisualAt;
        public int LastConfirmedThreatAt;
        public int LastGunshotAt;
        public int LastBodySeenAt;
        public int LastSocialCueAt;
        public int LastDecisionAt;
        public int DecisionUntil;
        public int LastCognitionAt;
        public int LastStageChangeAt;
        public int LastEmergencyReplanAt;

        public float SocialThreatConfidence;
        public int SocialSourceHandle;

        public static PedState Create(Ped ped, Config cfg)
        {
            int seed = unchecked(ped.Handle * 397 ^ ped.Model.Hash * 17 ^ 0x5F3759DF);
            var r = new Random(seed);
            int bravery = Range(r, cfg.MinBravery, cfg.MaxBravery);
            int curiosity = Range(r, cfg.MinCuriosity, cfg.MaxCuriosity);
            int aggression = Range(r, cfg.MinAggression, cfg.MaxAggression);
            int alertness = Range(r, cfg.MinAlertness, cfg.MaxAlertness);
            int preservation = Range(r, cfg.MinSelfPreservation, cfg.MaxSelfPreservation);
            int conformity = Range(r, cfg.MinConformity, cfg.MaxConformity);
            int empathy = Range(r, cfg.MinEmpathy, cfg.MaxEmpathy);
            int group = -1;
            try { group = Function.Call<int>(Hash.GET_PED_GROUP_INDEX, ped.Handle); }
            catch { }

            return new PedState
            {
                Handle = ped.Handle,
                ModelHash = ped.Model.Hash,
                GroupId = group,
                Bravery = bravery,
                Curiosity = curiosity,
                Aggression = aggression,
                Alertness = alertness,
                SelfPreservation = preservation,
                Conformity = conformity,
                Empathy = empathy,
                Archetype = PickArchetype(bravery, curiosity, aggression, alertness, preservation, empathy),
                LastHealth = SafeHealth(ped),
                Stage = AwarenessStage.Unaware,
                Mode = ReactionMode.None,
                Distraction = DistractionKind.None
            };
        }

        public void Decay(Config cfg, int now)
        {
            int age = LastStimulusAt <= 0 ? 999999 : now - LastStimulusAt;
            float calmScale = age > cfg.MemoryHoldMs ? 1.0f : 0.18f;
            float dt = Math.Max(0.01f, Math.Min(0.35f, cfg.TickIntervalMs / 1000f));
            Attention = Clamp(Attention - cfg.AttentionDecayPerSecond * dt * calmScale);
            Suspicion = Clamp(Suspicion - cfg.SuspicionDecayPerSecond * dt * calmScale);
            Certainty = Clamp(Certainty - cfg.CertaintyDecayPerSecond * dt * calmScale);
            Fear = Clamp(Fear - cfg.FearDecayPerSecond * dt * calmScale);
            SocialThreatConfidence = Clamp(SocialThreatConfidence - 3f * dt);
            ExternalThreatConfidence = Clamp(ExternalThreatConfidence - cfg.ExternalConfidenceDecayPerSecond * dt);

            if (now - LastGunshotAt > cfg.SensoryMemoryMs) HeardGunshot = false;
            if (now - LastBodySeenAt > cfg.SensoryMemoryMs) SawBody = false;
            if (now - LastSocialCueAt > cfg.SensoryMemoryMs)
            {
                SawCrowdPanic = false;
                SawQuietWithdrawal = false;
                SocialSourceHandle = 0;
            }
            if (now - LastVisualAt > cfg.SensoryMemoryMs)
            {
                SawWeapon = false;
                SawMask = false;
                WasDirectlyAimedAt = false;
            }
            if (now - LastSceneEventAt > cfg.SceneEventMemoryMs)
            {
                SceneThreatKind = SceneThreatKind.None;
                ThreatSourceHandle = 0;
                SawExternalFight = false;
                SawExternalWeapon = false;
                HeardExternalGunfire = false;
                SawFire = false;
                HeardExplosion = false;
                SawVehicleHazard = false;
                VehicleHazardTtc = 99f;
                ExternalThreatConfidence = 0f;
            }
        }

        public int Roll(int salt)
        {
            unchecked
            {
                int x = Handle * 1103515245 + ModelHash * 12345 + salt * 265443576;
                x ^= (x >> 16);
                if (x < 0) x = -x;
                return x % 100;
            }
        }

        public float Roll01(int salt)
        {
            return Roll(salt) / 99f;
        }

        private static PedArchetype PickArchetype(int bravery, int curiosity, int aggression, int alertness, int preservation, int empathy)
        {
            if (aggression >= 72 && bravery >= 58) return PedArchetype.Aggressive;
            if (empathy >= 72 && bravery >= 48) return PedArchetype.Protective;
            if (curiosity >= 72 && preservation < 72) return PedArchetype.Curious;
            if (preservation >= 76 || bravery <= 24) return PedArchetype.Cautious;
            if (bravery >= 74 && preservation <= 62) return PedArchetype.Bold;
            if (alertness <= 28 && curiosity <= 40) return PedArchetype.Detached;
            return PedArchetype.Average;
        }

        private static int Range(Random r, int min, int max)
        {
            if (max < min) { int t = min; min = max; max = t; }
            return r.Next(Math.Max(0, min), Math.Max(1, max) + 1);
        }

        private static float Clamp(float value)
        {
            return Math.Max(0f, Math.Min(100f, value));
        }

        public static int SafeHealth(Ped p)
        {
            try { return p.Health; }
            catch { return 100; }
        }
    }
}
