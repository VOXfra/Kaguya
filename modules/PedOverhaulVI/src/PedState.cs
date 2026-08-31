using GTA;
using GTA.Math;
using GTA.Native;
using System;

namespace VOX.PedOverhaulVI
{
    internal enum AwarenessStage { Unaware=0,Noticed=1,Suspicious=2,Concerned=3,ThreatConfirmed=4,Panic=5 }
    internal enum ReactionMode { None=0,Glance=1,Watch=2,DiscreetLeave=3,AlertNearby=4,Phone=5,Film=6,Freeze=7,Cower=8,Flee=9,Cover=10,Confront=11,Combat=12,Surrender=13,Investigate=14,Evade=15,Assist=16,DriveAway=17 }
    internal enum PedArchetype { Cautious,Average,Curious,Bold,Aggressive,Protective,Detached }

    internal sealed class PedState
    {
        public int Handle,ModelHash,GroupId=-1;
        public int Bravery,Curiosity,Aggression,Alertness,SelfPreservation,Conformity,Empathy;
        public PedArchetype Archetype;
        public float Morale=100f; public int LastHealth; public bool NearbyDeathCounted;
        public float Attention,Suspicion,Certainty,Fear; public AwarenessStage Stage; public ReactionMode Mode;

        public bool SawWeapon,SawMask,SawViolence,SawBody,WasDirectlyAimedAt,HeardGunshot,ThreatSourceKnown,SawCrowdPanic,SawQuietWithdrawal;
        public DistractionKind Distraction; public float DistractionLevel,VisualAttentionScale=1f,HearingAttentionScale=1f,SocialAttentionScale=1f;
        public int LastDistractionProbeAt,DistractionReactionUntil;

        public SceneThreatKind SceneThreatKind; public int ThreatSourceHandle; public float ExternalThreatConfidence;
        public bool SawExternalFight,SawExternalWeapon,HeardExternalGunfire,SawFire,HeardExplosion,SawVehicleHazard;
        public float VehicleHazardTtc=99f; public int LastSceneEventAt;

        // Causal knowledge: what this ped believes happened and why. This is
        // separate from fear so social propagation can lose information instead
        // of cloning omniscient knowledge across a crowd.
        public SceneThreatKind KnownThreatKind;
        public int KnownThreatSourceHandle;
        public float KnownThreatConfidence;
        public bool KnowledgeWasDirect;
        public int KnowledgeHops;
        public int LastKnowledgeAt;
        public int TrustedCompanionHandle;
        public int CompanionCandidateHandle;
        public int CompanionCandidateSince;

        // Player-specific social memory, fed by Interaction Runtime VI and by
        // directly witnessed threats. It survives state changes while streamed.
        public float OpinionOfPlayer;
        public float RecognitionOfPlayer;
        public float PlayerFearAssociation;
        public string LastPlayerInteraction=string.Empty;
        public int LastPlayerInteractionAt;

        public Vector3 LastThreatPosition,LastSafeDirection;
        public int FirstNoticedAt,LastStimulusAt,LastVisualAt,LastConfirmedThreatAt,LastGunshotAt,LastBodySeenAt,LastSocialCueAt,LastDecisionAt,DecisionUntil,LastCognitionAt,LastStageChangeAt,LastEmergencyReplanAt;
        public float SocialThreatConfidence; public int SocialSourceHandle;

        public static PedState Create(Ped ped,Config cfg)
        {
            int seed=unchecked(ped.Handle*397^ped.Model.Hash*17^0x5F3759DF);var r=new Random(seed);
            int bravery=Range(r,cfg.MinBravery,cfg.MaxBravery),curiosity=Range(r,cfg.MinCuriosity,cfg.MaxCuriosity),aggression=Range(r,cfg.MinAggression,cfg.MaxAggression),alertness=Range(r,cfg.MinAlertness,cfg.MaxAlertness),preservation=Range(r,cfg.MinSelfPreservation,cfg.MaxSelfPreservation),conformity=Range(r,cfg.MinConformity,cfg.MaxConformity),empathy=Range(r,cfg.MinEmpathy,cfg.MaxEmpathy),group=-1;
            try{group=Function.Call<int>(Hash.GET_PED_GROUP_INDEX,ped.Handle);}catch{}
            return new PedState{Handle=ped.Handle,ModelHash=ped.Model.Hash,GroupId=group,Bravery=bravery,Curiosity=curiosity,Aggression=aggression,Alertness=alertness,SelfPreservation=preservation,Conformity=conformity,Empathy=empathy,Archetype=PickArchetype(bravery,curiosity,aggression,alertness,preservation,empathy),LastHealth=SafeHealth(ped),Stage=AwarenessStage.Unaware,Mode=ReactionMode.None,Distraction=DistractionKind.None};
        }

        public void Decay(Config cfg,int now)
        {
            int age=LastStimulusAt<=0?999999:now-LastStimulusAt;float calmScale=age>cfg.MemoryHoldMs?1f:0.18f;float dt=Math.Max(0.01f,Math.Min(0.35f,cfg.TickIntervalMs/1000f));
            Attention=Clamp(Attention-cfg.AttentionDecayPerSecond*dt*calmScale);Suspicion=Clamp(Suspicion-cfg.SuspicionDecayPerSecond*dt*calmScale);Certainty=Clamp(Certainty-cfg.CertaintyDecayPerSecond*dt*calmScale);Fear=Clamp(Fear-cfg.FearDecayPerSecond*dt*calmScale);
            SocialThreatConfidence=Clamp(SocialThreatConfidence-3f*dt);ExternalThreatConfidence=Clamp(ExternalThreatConfidence-cfg.ExternalConfidenceDecayPerSecond*dt);
            if(LastKnowledgeAt>0&&now-LastKnowledgeAt>cfg.SceneEventMemoryMs){KnownThreatKind=SceneThreatKind.None;KnownThreatSourceHandle=0;KnownThreatConfidence=0f;KnowledgeWasDirect=false;KnowledgeHops=0;}
            if(now-LastGunshotAt>cfg.SensoryMemoryMs)HeardGunshot=false;if(now-LastBodySeenAt>cfg.SensoryMemoryMs)SawBody=false;
            if(now-LastSocialCueAt>cfg.SensoryMemoryMs){SawCrowdPanic=false;SawQuietWithdrawal=false;SocialSourceHandle=0;}
            if(now-LastVisualAt>cfg.SensoryMemoryMs){SawWeapon=false;SawMask=false;WasDirectlyAimedAt=false;}
            if(now-LastSceneEventAt>cfg.SceneEventMemoryMs){SceneThreatKind=SceneThreatKind.None;ThreatSourceHandle=0;SawExternalFight=false;SawExternalWeapon=false;HeardExternalGunfire=false;SawFire=false;HeardExplosion=false;SawVehicleHazard=false;VehicleHazardTtc=99f;ExternalThreatConfidence=0f;}
            // social memories fade slowly; they should outlive momentary fear.
            if(LastPlayerInteractionAt>0&&now-LastPlayerInteractionAt>600000){OpinionOfPlayer*=0.995f;PlayerFearAssociation*=0.997f;RecognitionOfPlayer*=0.999f;}
        }

        public int Roll(int salt){unchecked{int x=Handle*1103515245+ModelHash*12345+salt*265443576;x^=(x>>16);if(x<0)x=-x;return x%100;}}
        public float Roll01(int salt){return Roll(salt)/99f;}
        private static PedArchetype PickArchetype(int b,int c,int a,int al,int p,int e){if(a>=72&&b>=58)return PedArchetype.Aggressive;if(e>=72&&b>=48)return PedArchetype.Protective;if(c>=72&&p<72)return PedArchetype.Curious;if(p>=76||b<=24)return PedArchetype.Cautious;if(b>=74&&p<=62)return PedArchetype.Bold;if(al<=28&&c<=40)return PedArchetype.Detached;return PedArchetype.Average;}
        private static int Range(Random r,int min,int max){if(max<min){int t=min;min=max;max=t;}return r.Next(Math.Max(0,min),Math.Max(1,max)+1);}
        private static float Clamp(float v){return Math.Max(0f,Math.Min(100f,v));}
        public static int SafeHealth(Ped p){try{return p.Health;}catch{return 100;}}
    }
}
