using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PedOverhaulVI
{
    internal static class GangAwarenessRuntime
    {
        private static readonly string[] GangGroups =
        {
            "AMBIENT_GANG_BALLAS",
            "AMBIENT_GANG_FAMILY",
            "AMBIENT_GANG_MEXICAN",
            "AMBIENT_GANG_SALVA",
            "AMBIENT_GANG_LOST",
            "AMBIENT_GANG_WEICHENG",
            "AMBIENT_GANG_MARABUNTE",
            "AMBIENT_GANG_CULT"
        };

        public static bool TryHandleBody(Ped player, Ped ped, PedState state, ScenePerception scene, IList<Ped> nearby, Config cfg, Action<string> log)
        {
            if (ped == null || state == null || scene == null || !scene.HasThreat || !scene.Visual) return false;
            if (scene.Kind != SceneThreatKind.Body || scene.SourceHandle <= 0) return false;
            if (!IsGangMember(ped)) return false;

            Ped corpse = null;
            try
            {
                Entity e = Entity.FromHandle(scene.SourceHandle);
                corpse = e as Ped;
            }
            catch { }
            if (corpse == null || !corpse.Exists() || !SameGang(ped, corpse)) return false;

            // A direct attack witnessed by this ped outranks body-search behaviour.
            if (PlayerIsDirectlyPerceivedThreat(player, ped, cfg)) return false;

            int now = Game.GameTime;
            if (now < state.DecisionUntil && (state.Mode == ReactionMode.Investigate || state.Mode == ReactionMode.AlertNearby))
                return true;

            state.KnownThreatKind = SceneThreatKind.Body;
            state.KnownThreatSourceHandle = 0;
            state.KnownThreatConfidence = Math.Max(state.KnownThreatConfidence, 74f);
            state.KnowledgeWasDirect = true;
            state.KnowledgeHops = 0;
            state.LastKnowledgeAt = now;
            state.LastThreatPosition = scene.Position;
            state.ThreatSourceKnown = false;

            // If vanilla ambient gang AI already snapped to the Player entity after
            // the death, discard that task. The replacement task searches the scene,
            // not an omniscient target handle.
            try { Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle); } catch { }

            bool canAlert = HasVisibleGangMate(ped, nearby, 14f);
            if (canAlert && state.Roll(now / 1300 + 731) < 58)
            {
                try
                {
                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle, "GENERIC_SHOCKED_HIGH", "SPEECH_PARAMS_FORCE_SHOUTED");
                }
                catch { }
                state.Mode = ReactionMode.AlertNearby;
            }
            else state.Mode = ReactionMode.Investigate;

            Vector3 search = SearchPoint(scene.Position, state.Handle, now);
            try
            {
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle, search.X, search.Y, search.Z, 1.15f, 6000, 1.6f, 0, 0f);
            }
            catch
            {
                try { Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, ped.Handle, search.X, search.Y, search.Z, 1.05f, 5000, 0f, 0f); }
                catch { }
            }

            state.LastDecisionAt = now;
            state.DecisionUntil = now + 3200 + state.Roll(733) * 24;
            if (log != null)
                log("Gang member " + state.Handle + " discovered same-faction body; local search started without culprit lock.");
            return true;
        }

        public static bool TryHandleWarning(Ped player, Ped ped, PedState state, ScenePerception scene, Config cfg, Action<string> log)
        {
            if (ped == null || state == null || scene == null || scene.Kind != SceneThreatKind.SocialWarning || scene.SourceHandle <= 0) return false;
            if (!IsGangMember(ped)) return false;

            Ped source = null;
            try { source = Entity.FromHandle(scene.SourceHandle) as Ped; } catch { }
            if (source == null || !source.Exists() || !SameGang(ped, source)) return false;
            if (PlayerIsDirectlyPerceivedThreat(player, ped, cfg)) return false;

            int now = Game.GameTime;
            if (now < state.DecisionUntil && state.Mode == ReactionMode.Investigate) return true;

            state.KnownThreatSourceHandle = 0;
            state.KnowledgeWasDirect = false;
            state.KnowledgeHops = Math.Max(1, state.KnowledgeHops);
            state.LastThreatPosition = scene.Position;
            state.ThreatSourceKnown = false;

            Vector3 search = SearchPoint(scene.Position, state.Handle, now);
            try { Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle); } catch { }
            try { Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle, search.X, search.Y, search.Z, 1.2f, 6000, 1.8f, 0, 0f); }
            catch { }
            state.Mode = ReactionMode.Investigate;
            state.LastDecisionAt = now;
            state.DecisionUntil = now + 2800 + state.Roll(739) * 22;
            if (log != null)
                log("Gang warning propagated locally to " + state.Handle + "; searching reported area, culprit still unknown.");
            return true;
        }

        public static bool IsGangMember(Ped ped)
        {
            if (ped == null || !ped.Exists()) return false;
            int group = RelationshipGroup(ped);
            if (group == 0) return false;
            foreach (string name in GangGroups)
            {
                try { if (group == Function.Call<int>(Hash.GET_HASH_KEY, name)) return true; }
                catch { }
            }
            return false;
        }

        private static bool SameGang(Ped a, Ped b)
        {
            int ga = RelationshipGroup(a), gb = RelationshipGroup(b);
            return ga != 0 && ga == gb && IsGangMember(a);
        }

        private static int RelationshipGroup(Ped ped)
        {
            try { return Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle); }
            catch { return 0; }
        }

        private static bool HasVisibleGangMate(Ped ped, IList<Ped> nearby, float radius)
        {
            if (nearby == null) return false;
            foreach (Ped other in nearby)
            {
                if (other == null || !other.Exists() || other.IsDead || other.Handle == ped.Handle) continue;
                if (!SameGang(ped, other)) continue;
                if (SituationModel.Distance(ped.Position, other.Position) > radius) continue;
                if (SensorySystem.HasVisual(ped, other, radius, 170f)) return true;
            }
            return false;
        }

        private static bool PlayerIsDirectlyPerceivedThreat(Ped player, Ped observer, Config cfg)
        {
            if (player == null || observer == null || !player.Exists() || !observer.Exists()) return false;
            if (!SensorySystem.HasVisual(observer, player, cfg.ThreatVisualRadius, cfg.PeripheralVisualFovDegrees)) return false;
            try { if (Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle)) return true; } catch { }
            try
            {
                if (Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle))
                {
                    var arg = new OutputArgument();
                    if (Function.Call<bool>(Hash.GET_ENTITY_PLAYER_IS_FREE_AIMING_AT, Game.Player.Handle, arg) && arg.GetResult<int>() == observer.Handle)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static Vector3 SearchPoint(Vector3 origin, int handle, int now)
        {
            unchecked
            {
                int seed = handle * 1103515245 + (now / 2400) * 12345;
                if (seed < 0) seed = -seed;
                double angle = (seed % 6283) / 1000.0;
                float radius = 8f + (seed % 1700) / 100f;
                return new Vector3(origin.X + (float)Math.Cos(angle) * radius, origin.Y + (float)Math.Sin(angle) * radius, origin.Z);
            }
        }
    }
}
