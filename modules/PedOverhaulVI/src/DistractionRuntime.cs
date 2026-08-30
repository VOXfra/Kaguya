using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PedOverhaulVI
{
    internal enum DistractionKind
    {
        None = 0,
        Phone = 1,
        Filming = 2,
        Conversation = 3,
        EatingOrDrinking = 4,
        Smoking = 5
    }

    internal static class DistractionRuntime
    {
        private static readonly string[] PhoneScenarios =
        {
            "WORLD_HUMAN_STAND_MOBILE",
            "WORLD_HUMAN_STAND_MOBILE_UPRIGHT",
            "WORLD_HUMAN_TOURIST_MOBILE"
        };

        private static readonly string[] FilmScenarios =
        {
            "WORLD_HUMAN_MOBILE_FILM_SHOCKING"
        };

        private static readonly string[] EatingScenarios =
        {
            "WORLD_HUMAN_STAND_EATING",
            "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_AA_COFFEE"
        };

        private static readonly string[] SmokingScenarios =
        {
            "WORLD_HUMAN_SMOKING",
            "WORLD_HUMAN_SMOKING_POT"
        };

        public static void Update(Ped ped, PedState state, IList<Ped> nearby, Config cfg, Action<string> log)
        {
            if (ped == null || state == null || !ped.Exists() || !cfg.DistractionEnabled)
            {
                Clear(state);
                return;
            }

            int now = Game.GameTime;
            if (now - state.LastDistractionProbeAt < Math.Max(150, cfg.DistractionProbeIntervalMs)) return;
            state.LastDistractionProbeAt = now;

            DistractionKind kind = Detect(ped, nearby, state);
            if (kind == state.Distraction) return;

            DistractionKind old = state.Distraction;
            state.Distraction = kind;
            ApplyScales(state, kind, cfg);
            state.DistractionReactionUntil = 0;

            if (cfg.LogDistractionTransitions && log != null)
                log("Ped " + state.Handle + " distraction " + old + " -> " + kind + ".");
        }

        public static void ApplyToPerception(Ped ped, PedState state, PerceptionFrame frame, Config cfg)
        {
            if (ped == null || state == null || frame == null || state.Distraction == DistractionKind.None) return;

            frame.WasDistracted = true;
            frame.VisualAttentionScale = Math.Max(0.15f, Math.Min(1f, state.VisualAttentionScale));
            frame.HearingAttentionScale = Math.Max(0.25f, Math.Min(1f, state.HearingAttentionScale));
            frame.SocialAttentionScale = Math.Max(0.20f, Math.Min(1f, state.SocialAttentionScale));
            frame.VisualQuality *= frame.VisualAttentionScale;

            bool hardThreat = frame.DirectlyAimedAt || frame.SeesShooting;
            if (hardThreat)
            {
                state.DistractionReactionUntil = 0;
                return;
            }

            int now = Game.GameTime;
            bool lowKeyStimulus = frame.SeesWeapon || frame.SeesMask || frame.SeesBody || frame.CrowdPanic || frame.QuietWithdrawal;
            if (lowKeyStimulus && state.DistractionReactionUntil == 0)
            {
                int span = Math.Max(0, cfg.DistractionRecognitionDelayMaxMs - cfg.DistractionRecognitionDelayMinMs);
                int delay = cfg.DistractionRecognitionDelayMinMs + (int)(span * state.DistractionLevel * (0.45f + 0.55f * state.Roll01(331)));
                state.DistractionReactionUntil = now + Math.Max(0, delay);
            }

            if (state.DistractionReactionUntil > now)
            {
                // Close/obvious danger still breaks through. Subtle cues wait until
                // the ped actually looks up from what they were doing.
                if (frame.DistanceToPlayer > cfg.DistractionCloseThreatOverrideDistance)
                {
                    frame.SeesWeapon = false;
                    frame.SeesMask = false;
                }
                frame.SeesBody = false;
                frame.CrowdPanic = false;
                frame.QuietWithdrawal = false;
                frame.ThreatSourceKnown = frame.DirectlyAimedAt || frame.SeesShooting;
            }
            else if (state.DistractionReactionUntil != 0)
            {
                state.DistractionReactionUntil = 0;
            }
        }

        public static bool ShouldDelayDecision(PedState state, PerceptionFrame frame, Config cfg, int now)
        {
            if (state == null || frame == null || state.Distraction == DistractionKind.None) return false;
            if (frame.DirectlyAimedAt || frame.SeesShooting) return false;
            if (state.Stage >= AwarenessStage.ThreatConfirmed) return false;
            return state.DistractionReactionUntil > now;
        }

        private static DistractionKind Detect(Ped ped, IList<Ped> nearby, PedState state)
        {
            if (state.Mode == ReactionMode.Phone) return DistractionKind.Phone;
            if (state.Mode == ReactionMode.Film) return DistractionKind.Filming;

            if (UsesAnyScenario(ped, FilmScenarios)) return DistractionKind.Filming;
            if (UsesAnyScenario(ped, PhoneScenarios) || UsesPhoneAnimation(ped)) return DistractionKind.Phone;
            if (UsesAnyScenario(ped, EatingScenarios)) return DistractionKind.EatingOrDrinking;
            if (UsesAnyScenario(ped, SmokingScenarios)) return DistractionKind.Smoking;
            if (LooksLikeConversation(ped, nearby)) return DistractionKind.Conversation;
            return DistractionKind.None;
        }

        private static void ApplyScales(PedState state, DistractionKind kind, Config cfg)
        {
            state.VisualAttentionScale = 1f;
            state.HearingAttentionScale = 1f;
            state.SocialAttentionScale = 1f;
            state.DistractionLevel = 0f;

            switch (kind)
            {
                case DistractionKind.Phone:
                    state.VisualAttentionScale = cfg.PhoneVisualAttentionScale;
                    state.HearingAttentionScale = cfg.PhoneHearingAttentionScale;
                    state.SocialAttentionScale = cfg.PhoneSocialAttentionScale;
                    state.DistractionLevel = 0.82f;
                    break;
                case DistractionKind.Filming:
                    state.VisualAttentionScale = cfg.FilmingVisualAttentionScale;
                    state.HearingAttentionScale = cfg.FilmingHearingAttentionScale;
                    state.SocialAttentionScale = 0.70f;
                    state.DistractionLevel = 0.62f;
                    break;
                case DistractionKind.Conversation:
                    state.VisualAttentionScale = cfg.ConversationVisualAttentionScale;
                    state.HearingAttentionScale = cfg.ConversationHearingAttentionScale;
                    state.SocialAttentionScale = 0.72f;
                    state.DistractionLevel = 0.58f;
                    break;
                case DistractionKind.EatingOrDrinking:
                case DistractionKind.Smoking:
                    state.VisualAttentionScale = cfg.AmbientActivityVisualAttentionScale;
                    state.HearingAttentionScale = cfg.AmbientActivityHearingAttentionScale;
                    state.SocialAttentionScale = 0.88f;
                    state.DistractionLevel = 0.34f;
                    break;
            }
        }

        private static bool UsesAnyScenario(Ped ped, string[] scenarios)
        {
            foreach (string scenario in scenarios)
            {
                try
                {
                    if (Function.Call<bool>(Hash.IS_PED_USING_SCENARIO, ped.Handle, scenario)) return true;
                }
                catch { }
            }
            return false;
        }

        private static bool UsesPhoneAnimation(Ped ped)
        {
            return Playing(ped, "cellphone@", "cellphone_text_read_base") ||
                   Playing(ped, "cellphone@", "cellphone_call_listen_base") ||
                   Playing(ped, "cellphone@", "cellphone_call_to_text") ||
                   Playing(ped, "cellphone@", "cellphone_text_to_call");
        }

        private static bool Playing(Ped ped, string dict, string clip)
        {
            try { return Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, ped.Handle, dict, clip, 3); }
            catch { return false; }
        }

        private static bool LooksLikeConversation(Ped ped, IList<Ped> nearby)
        {
            if (nearby == null) return false;
            float ownSpeed = SafeSpeed(ped);
            if (ownSpeed > 0.85f) return false;

            foreach (Ped other in nearby)
            {
                if (other == null || !other.Exists() || other.IsDead || other.Handle == ped.Handle || other.IsInVehicle()) continue;
                float distance = SituationModel.Distance(ped.Position, other.Position);
                if (distance > 2.6f || SafeSpeed(other) > 0.85f) continue;

                Vector3 toOther = other.Position - ped.Position;
                float len = (float)Math.Sqrt(toOther.X * toOther.X + toOther.Y * toOther.Y);
                if (len < 0.2f) continue;
                toOther = new Vector3(toOther.X / len, toOther.Y / len, 0f);
                Vector3 toPed = new Vector3(-toOther.X, -toOther.Y, 0f);
                float facingA = ped.ForwardVector.X * toOther.X + ped.ForwardVector.Y * toOther.Y;
                float facingB = other.ForwardVector.X * toPed.X + other.ForwardVector.Y * toPed.Y;
                if (facingA > 0.25f && facingB > 0.05f) return true;
            }
            return false;
        }

        private static float SafeSpeed(Entity e)
        {
            try { return Function.Call<float>(Hash.GET_ENTITY_SPEED, e.Handle); }
            catch { return 0f; }
        }

        private static void Clear(PedState state)
        {
            if (state == null) return;
            state.Distraction = DistractionKind.None;
            state.DistractionLevel = 0f;
            state.VisualAttentionScale = 1f;
            state.HearingAttentionScale = 1f;
            state.SocialAttentionScale = 1f;
            state.DistractionReactionUntil = 0;
        }
    }
}
