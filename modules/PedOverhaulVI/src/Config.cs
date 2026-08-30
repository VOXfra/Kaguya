using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.PedOverhaulVI
{
    internal sealed class Config
    {
        // General / performance
        public bool Enabled = true;
        public bool DebugLogging = true;
        public bool LogStateTransitions = true;
        public bool DisableDuringRockstarMissions = true;
        public bool SkipMissionEntities = true;
        public bool PoliceOverhaulOwnsLawPeds = true;
        public int TickIntervalMs = 50;
        public int RefreshNearbyPedsMs = 450;
        public float ProcessRadius = 100f;
        public int MaxProcessedPeds = 34;
        public int PedsPerTick = 6;

        // Perception. Central vision gives strong evidence; peripheral vision
        // mostly creates attention/suspicion and forces the ped to look again.
        public float CentralVisualFovDegrees = 92f;
        public float PeripheralVisualFovDegrees = 155f;
        public float ThreatVisualRadius = 72f;
        public float WeaponRecognitionRadius = 58f;
        public float MaskRecognitionRadius = 30f;
        public float AimThreatRadius = 46f;
        public float GunshotHearingRadius = 115f;
        public float BodyAwarenessRadius = 34f;
        public float SocialAwarenessRadius = 24f;
        public float QuietWithdrawalAwarenessRadius = 10f;
        public int SensoryMemoryMs = 14000;
        public int MinimumLookMs = 700;
        public int MaximumLookMs = 2400;

        // Cognitive thresholds / decay.
        public float NoticedThreshold = 12f;
        public float SuspiciousThreshold = 28f;
        public float ConcernedThreshold = 47f;
        public float ThreatConfirmedThreshold = 67f;
        public float PanicThreshold = 78f;
        public int MemoryHoldMs = 6500;
        public int CalmAfterMs = 30000;
        public float AttentionDecayPerSecond = 12f;
        public float SuspicionDecayPerSecond = 4.0f;
        public float CertaintyDecayPerSecond = 2.4f;
        public float FearDecayPerSecond = 3.5f;
        public int DecisionCooldownMinMs = 650;
        public int DecisionCooldownMaxMs = 2100;

        // Stimulus evidence weights. They combine rather than map to tasks.
        public float MaskSuspicion = 7f;
        public float VisibleWeaponSuspicion = 28f;
        public float MaskWeaponCombinationBonus = 23f;
        public float DirectAimThreat = 68f;
        public float VisibleShootingThreat = 92f;
        public float HeardGunshotThreat = 40f;
        public float DeadBodySuspicion = 36f;
        public float CrowdPanicThreat = 22f;
        public float QuietWithdrawalSuspicion = 10f;
        public float HostileRelationshipSuspicion = 12f;

        // Personality distributions.
        public int MinBravery = 8, MaxBravery = 95;
        public int MinCuriosity = 5, MaxCuriosity = 95;
        public int MinAggression = 4, MaxAggression = 90;
        public int MinAlertness = 12, MaxAlertness = 96;
        public int MinSelfPreservation = 18, MaxSelfPreservation = 98;
        public int MinConformity = 8, MaxConformity = 94;
        public int MinEmpathy = 8, MaxEmpathy = 94;

        // Civilian decisions.
        public bool DiscreetWithdrawalEnabled = true;
        public float DiscreetLeaveMinDistance = 20f;
        public float DiscreetLeaveMaxDistance = 38f;
        public float DiscreetWalkSpeed = 1.0f;
        public bool FilmFromDistance = true;
        public float FilmMinDistance = 30f;
        public float FilmMaxDistance = 68f;
        public int FilmCuriosityThreshold = 72;
        public bool PhoneWhenSafe = true;
        public int PhoneMinCertainty = 55;
        public bool PanicPropagation = true;
        public bool QuietWithdrawalPropagation = true;
        public bool SeekCoverWhenThreatened = true;
        public float FleeDistance = 125f;
        public int FleeDurationMs = 18000;
        public int CowerDurationMs = 9000;
        public int FreezeMinMs = 900;
        public int FreezeMaxMs = 3000;

        // Combat morale for hostile peds.
        public bool MoraleEnabled = true;
        public int MoraleBreakThreshold = 28;
        public float HealthLossMoraleMultiplier = 0.75f;
        public int NearbyDeathMoraleLoss = 24;
        public float NearbyDeathRadius = 15f;
        public int OutnumberedMoraleLoss = 12;
        public int SurrenderBaseChance = 55;
        public int SurrenderDurationMs = 14000;
        public int LowHealthPercent = 32;

        public static Config Load(string path)
        {
            var c = new Config();
            if (!File.Exists(path)) return c;
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;
            foreach (string raw in File.ReadAllLines(path))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith(";") || l.StartsWith("#")) continue;
                if (l.StartsWith("[") && l.EndsWith("]")) { section = l.Substring(1, l.Length - 2).Trim(); continue; }
                int e = l.IndexOf('=');
                if (e > 0) v[section + "." + l.Substring(0, e).Trim()] = l.Substring(e + 1).Trim();
            }

            c.Enabled = B(v,"General.Enabled",c.Enabled); c.DebugLogging = B(v,"General.DebugLogging",c.DebugLogging); c.LogStateTransitions = B(v,"General.LogStateTransitions",c.LogStateTransitions);
            c.DisableDuringRockstarMissions = B(v,"General.DisableDuringRockstarMissions",c.DisableDuringRockstarMissions); c.SkipMissionEntities = B(v,"General.SkipMissionEntities",c.SkipMissionEntities);
            c.TickIntervalMs = I(v,"General.TickIntervalMs",c.TickIntervalMs); c.RefreshNearbyPedsMs = I(v,"General.RefreshNearbyPedsMs",c.RefreshNearbyPedsMs); c.ProcessRadius = F(v,"General.ProcessRadius",c.ProcessRadius); c.MaxProcessedPeds = I(v,"General.MaxProcessedPeds",c.MaxProcessedPeds); c.PedsPerTick = I(v,"General.PedsPerTick",c.PedsPerTick);

            c.CentralVisualFovDegrees = F(v,"Perception.CentralVisualFovDegrees",c.CentralVisualFovDegrees); c.PeripheralVisualFovDegrees = F(v,"Perception.PeripheralVisualFovDegrees",c.PeripheralVisualFovDegrees); c.ThreatVisualRadius = F(v,"Perception.ThreatVisualRadius",c.ThreatVisualRadius);
            c.WeaponRecognitionRadius = F(v,"Perception.WeaponRecognitionRadius",c.WeaponRecognitionRadius); c.MaskRecognitionRadius = F(v,"Perception.MaskRecognitionRadius",c.MaskRecognitionRadius); c.AimThreatRadius = F(v,"Perception.AimThreatRadius",c.AimThreatRadius); c.GunshotHearingRadius = F(v,"Perception.GunshotHearingRadius",c.GunshotHearingRadius); c.BodyAwarenessRadius = F(v,"Perception.BodyAwarenessRadius",c.BodyAwarenessRadius); c.SocialAwarenessRadius = F(v,"Perception.SocialAwarenessRadius",c.SocialAwarenessRadius); c.QuietWithdrawalAwarenessRadius = F(v,"Perception.QuietWithdrawalAwarenessRadius",c.QuietWithdrawalAwarenessRadius); c.SensoryMemoryMs = I(v,"Perception.SensoryMemoryMs",c.SensoryMemoryMs); c.MinimumLookMs = I(v,"Perception.MinimumLookMs",c.MinimumLookMs); c.MaximumLookMs = I(v,"Perception.MaximumLookMs",c.MaximumLookMs);

            c.NoticedThreshold = F(v,"Cognition.NoticedThreshold",c.NoticedThreshold); c.SuspiciousThreshold = F(v,"Cognition.SuspiciousThreshold",c.SuspiciousThreshold); c.ConcernedThreshold = F(v,"Cognition.ConcernedThreshold",c.ConcernedThreshold); c.ThreatConfirmedThreshold = F(v,"Cognition.ThreatConfirmedThreshold",c.ThreatConfirmedThreshold); c.PanicThreshold = F(v,"Cognition.PanicThreshold",c.PanicThreshold); c.MemoryHoldMs = I(v,"Cognition.MemoryHoldMs",c.MemoryHoldMs); c.CalmAfterMs = I(v,"Cognition.CalmAfterMs",c.CalmAfterMs); c.AttentionDecayPerSecond = F(v,"Cognition.AttentionDecayPerSecond",c.AttentionDecayPerSecond); c.SuspicionDecayPerSecond = F(v,"Cognition.SuspicionDecayPerSecond",c.SuspicionDecayPerSecond); c.CertaintyDecayPerSecond = F(v,"Cognition.CertaintyDecayPerSecond",c.CertaintyDecayPerSecond); c.FearDecayPerSecond = F(v,"Cognition.FearDecayPerSecond",c.FearDecayPerSecond); c.DecisionCooldownMinMs = I(v,"Cognition.DecisionCooldownMinMs",c.DecisionCooldownMinMs); c.DecisionCooldownMaxMs = I(v,"Cognition.DecisionCooldownMaxMs",c.DecisionCooldownMaxMs);

            c.MaskSuspicion = F(v,"Stimuli.MaskSuspicion",c.MaskSuspicion); c.VisibleWeaponSuspicion = F(v,"Stimuli.VisibleWeaponSuspicion",c.VisibleWeaponSuspicion); c.MaskWeaponCombinationBonus = F(v,"Stimuli.MaskWeaponCombinationBonus",c.MaskWeaponCombinationBonus); c.DirectAimThreat = F(v,"Stimuli.DirectAimThreat",c.DirectAimThreat); c.VisibleShootingThreat = F(v,"Stimuli.VisibleShootingThreat",c.VisibleShootingThreat); c.HeardGunshotThreat = F(v,"Stimuli.HeardGunshotThreat",c.HeardGunshotThreat); c.DeadBodySuspicion = F(v,"Stimuli.DeadBodySuspicion",c.DeadBodySuspicion); c.CrowdPanicThreat = F(v,"Stimuli.CrowdPanicThreat",c.CrowdPanicThreat); c.QuietWithdrawalSuspicion = F(v,"Stimuli.QuietWithdrawalSuspicion",c.QuietWithdrawalSuspicion); c.HostileRelationshipSuspicion = F(v,"Stimuli.HostileRelationshipSuspicion",c.HostileRelationshipSuspicion);

            c.MinBravery = I(v,"Personality.MinBravery",c.MinBravery); c.MaxBravery = I(v,"Personality.MaxBravery",c.MaxBravery); c.MinCuriosity = I(v,"Personality.MinCuriosity",c.MinCuriosity); c.MaxCuriosity = I(v,"Personality.MaxCuriosity",c.MaxCuriosity); c.MinAggression = I(v,"Personality.MinAggression",c.MinAggression); c.MaxAggression = I(v,"Personality.MaxAggression",c.MaxAggression); c.MinAlertness = I(v,"Personality.MinAlertness",c.MinAlertness); c.MaxAlertness = I(v,"Personality.MaxAlertness",c.MaxAlertness); c.MinSelfPreservation = I(v,"Personality.MinSelfPreservation",c.MinSelfPreservation); c.MaxSelfPreservation = I(v,"Personality.MaxSelfPreservation",c.MaxSelfPreservation); c.MinConformity = I(v,"Personality.MinConformity",c.MinConformity); c.MaxConformity = I(v,"Personality.MaxConformity",c.MaxConformity); c.MinEmpathy = I(v,"Personality.MinEmpathy",c.MinEmpathy); c.MaxEmpathy = I(v,"Personality.MaxEmpathy",c.MaxEmpathy);

            c.DiscreetWithdrawalEnabled = B(v,"Civilians.DiscreetWithdrawalEnabled",c.DiscreetWithdrawalEnabled); c.DiscreetLeaveMinDistance = F(v,"Civilians.DiscreetLeaveMinDistance",c.DiscreetLeaveMinDistance); c.DiscreetLeaveMaxDistance = F(v,"Civilians.DiscreetLeaveMaxDistance",c.DiscreetLeaveMaxDistance); c.DiscreetWalkSpeed = F(v,"Civilians.DiscreetWalkSpeed",c.DiscreetWalkSpeed); c.FilmFromDistance = B(v,"Civilians.FilmFromDistance",c.FilmFromDistance); c.FilmMinDistance = F(v,"Civilians.FilmMinDistance",c.FilmMinDistance); c.FilmMaxDistance = F(v,"Civilians.FilmMaxDistance",c.FilmMaxDistance); c.FilmCuriosityThreshold = I(v,"Civilians.FilmCuriosityThreshold",c.FilmCuriosityThreshold); c.PhoneWhenSafe = B(v,"Civilians.PhoneWhenSafe",c.PhoneWhenSafe); c.PhoneMinCertainty = I(v,"Civilians.PhoneMinCertainty",c.PhoneMinCertainty); c.PanicPropagation = B(v,"Civilians.PanicPropagation",c.PanicPropagation); c.QuietWithdrawalPropagation = B(v,"Civilians.QuietWithdrawalPropagation",c.QuietWithdrawalPropagation); c.SeekCoverWhenThreatened = B(v,"Civilians.SeekCoverWhenThreatened",c.SeekCoverWhenThreatened); c.FleeDistance = F(v,"Civilians.FleeDistance",c.FleeDistance); c.FleeDurationMs = I(v,"Civilians.FleeDurationMs",c.FleeDurationMs); c.CowerDurationMs = I(v,"Civilians.CowerDurationMs",c.CowerDurationMs); c.FreezeMinMs = I(v,"Civilians.FreezeMinMs",c.FreezeMinMs); c.FreezeMaxMs = I(v,"Civilians.FreezeMaxMs",c.FreezeMaxMs);

            c.MoraleEnabled = B(v,"Combat.MoraleEnabled",c.MoraleEnabled); c.MoraleBreakThreshold = I(v,"Combat.MoraleBreakThreshold",c.MoraleBreakThreshold); c.HealthLossMoraleMultiplier = F(v,"Combat.HealthLossMoraleMultiplier",c.HealthLossMoraleMultiplier); c.NearbyDeathMoraleLoss = I(v,"Combat.NearbyDeathMoraleLoss",c.NearbyDeathMoraleLoss); c.NearbyDeathRadius = F(v,"Combat.NearbyDeathRadius",c.NearbyDeathRadius); c.OutnumberedMoraleLoss = I(v,"Combat.OutnumberedMoraleLoss",c.OutnumberedMoraleLoss); c.SurrenderBaseChance = I(v,"Combat.SurrenderBaseChance",c.SurrenderBaseChance); c.SurrenderDurationMs = I(v,"Combat.SurrenderDurationMs",c.SurrenderDurationMs); c.LowHealthPercent = I(v,"Combat.LowHealthPercent",c.LowHealthPercent);
            c.PoliceOverhaulOwnsLawPeds = B(v,"Compatibility.PoliceOverhaulOwnsLawPeds",c.PoliceOverhaulOwnsLawPeds);
            return c;
        }

        private static bool B(Dictionary<string,string> v,string k,bool d){string s;bool x;return v.TryGetValue(k,out s)&&bool.TryParse(s,out x)?x:d;}
        private static int I(Dictionary<string,string> v,string k,int d){string s;int x;return v.TryGetValue(k,out s)&&int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out x)?x:d;}
        private static float F(Dictionary<string,string> v,string k,float d){string s;float x;return v.TryGetValue(k,out s)&&float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out x)?x:d;}
    }
}
