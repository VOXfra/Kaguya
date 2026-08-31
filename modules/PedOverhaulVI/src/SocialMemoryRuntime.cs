using GTA;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PedOverhaulVI
{
    internal static class SocialMemoryRuntime
    {
        public static void Update(Ped ped, PedState state, ScenePerception scene, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg)
        {
            if (ped == null || state == null || !ped.Exists()) return;
            int now = Game.GameTime;
            UpdateCompanion(ped, state, nearby, states, now);
            if (scene == null || !scene.HasThreat) return;

            if (scene.Kind != SceneThreatKind.SocialWarning && scene.Kind != SceneThreatKind.CrowdFlight)
            {
                float confidence = Math.Max(0f, Math.Min(100f, scene.Confidence * 100f));
                bool direct = scene.Visual && scene.SourceKnown;
                Remember(state, scene.Kind, direct ? scene.SourceHandle : 0, confidence, direct, 0, now);
                return;
            }

            PedState sourceState;
            if (scene.SourceHandle > 0 && states != null && states.TryGetValue(scene.SourceHandle, out sourceState) && sourceState.KnownThreatKind != SceneThreatKind.None)
            {
                bool trusted = state.TrustedCompanionHandle == sourceState.Handle || (state.GroupId >= 0 && state.GroupId == sourceState.GroupId);
                float trust = trusted ? 0.78f : 0.48f;
                float transferred = Math.Min(scene.Confidence * 100f, sourceState.KnownThreatConfidence * trust);
                if (transferred >= 14f)
                {
                    // Social propagation carries "something happened around there".
                    // It never hands over a culprit entity handle unless the observer
                    // later obtains that identity through their own senses.
                    Remember(state, sourceState.KnownThreatKind, 0, transferred, false, Math.Min(8, sourceState.KnowledgeHops + 1), now);
                    if (sourceState.LastThreatPosition != GTA.Math.Vector3.Zero)
                        state.LastThreatPosition = sourceState.LastThreatPosition;
                }
            }
            else
            {
                Remember(state, SceneThreatKind.CrowdFlight, 0, Math.Min(42f, scene.Confidence * 55f), false, 1, now);
            }
        }

        private static void Remember(PedState s, SceneThreatKind kind, int source, float confidence, bool direct, int hops, int now)
        {
            bool stronger = confidence >= s.KnownThreatConfidence + 4f;
            bool fresher = s.LastKnowledgeAt <= 0 || now - s.LastKnowledgeAt > 2500;
            bool betterSource = source > 0 && s.KnownThreatSourceHandle == 0;
            if (!stronger && !fresher && !betterSource) return;
            s.KnownThreatKind = kind;
            s.KnownThreatSourceHandle = source;
            s.KnownThreatConfidence = Math.Max(0f, Math.Min(100f, confidence));
            s.KnowledgeWasDirect = direct;
            s.KnowledgeHops = hops;
            s.LastKnowledgeAt = now;
        }

        private static void UpdateCompanion(Ped ped, PedState s, IList<Ped> nearby, IDictionary<int, PedState> states, int now)
        {
            if (nearby == null || states == null) return;
            if (s.TrustedCompanionHandle > 0)
            {
                Ped existing = FindPed(nearby, s.TrustedCompanionHandle);
                if (existing != null && existing.Exists() && Distance(ped, existing) <= 7f && SensorySystem.HasLineOfSight(ped, existing)) return;
                s.TrustedCompanionHandle = 0;
            }

            Ped best = null;
            float bestD = 3.4f;
            foreach (Ped other in nearby)
            {
                if (other == null || !other.Exists() || other.IsDead || other.Handle == ped.Handle) continue;
                PedState os;
                if (!states.TryGetValue(other.Handle, out os)) continue;
                float d = Distance(ped, other);
                if (d > bestD) continue;
                if (!SensorySystem.HasLineOfSight(ped, other)) continue;
                bool sameGroup = s.GroupId >= 0 && s.GroupId == os.GroupId;
                bool facing = FacingEachOther(ped, other);
                if (!sameGroup && !facing) continue;
                best = other;
                bestD = d;
            }
            if (best == null) { s.CompanionCandidateHandle = 0; s.CompanionCandidateSince = 0; return; }
            if (s.CompanionCandidateHandle != best.Handle) { s.CompanionCandidateHandle = best.Handle; s.CompanionCandidateSince = now; return; }
            if (s.CompanionCandidateSince > 0 && now - s.CompanionCandidateSince >= 2200) s.TrustedCompanionHandle = best.Handle;
        }

        private static bool FacingEachOther(Ped a, Ped b)
        {
            try { return Function.Call<bool>(Hash.IS_PED_FACING_PED, a.Handle, b.Handle, 75f) && Function.Call<bool>(Hash.IS_PED_FACING_PED, b.Handle, a.Handle, 75f); }
            catch { return false; }
        }

        private static Ped FindPed(IList<Ped> peds, int handle)
        {
            foreach (Ped p in peds) if (p != null && p.Exists() && p.Handle == handle) return p;
            return null;
        }

        private static float Distance(Ped a, Ped b)
        {
            try { return SituationModel.Distance(a.Position, b.Position); }
            catch { return 999f; }
        }
    }
}
