using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PedOverhaulVI
{
    internal static class SceneDecisionSystem
    {
        public static bool TryUpdate(Ped player, Ped ped, PedState s, ScenePerception scene, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg, Action<string> log)
        {
            if (ped == null || s == null || scene == null || !scene.HasThreat || !ped.Exists() || ped.IsDead) return false;
            if (scene.Kind == SceneThreatKind.SocialWarning && s.Stage < AwarenessStage.Concerned) return false;
            if (scene.SourceHandle == player.Handle) return false;

            // A real, directly perceived attack by the player must be handled by
            // the player-facing DecisionSystem. Scene logic must never mask it.
            if (PlayerImmediateThreat(player, ped, cfg)) return false;

            if (scene.Kind == SceneThreatKind.Body && GangAwarenessRuntime.TryHandleBody(player, ped, s, scene, nearby, cfg, log))
                return true;
            if (scene.Kind == SceneThreatKind.SocialWarning && GangAwarenessRuntime.TryHandleWarning(player, ped, s, scene, cfg, log))
                return true;

            int now = Game.GameTime;
            bool hardEmergency = scene.Immediate || scene.Kind == SceneThreatKind.Explosion;

            if (now < s.DecisionUntil)
            {
                if (!hardEmergency || IsCommittedSceneMode(s.Mode)) return true;
                if (now - s.LastEmergencyReplanAt < cfg.EmergencyReplanMinMs) return true;
                s.LastEmergencyReplanAt = now;
            }
            else if (!hardEmergency && now - s.LastDecisionAt < cfg.SceneDecisionCooldownMs) return true;

            ReactionMode old = s.Mode;
            switch (scene.Kind)
            {
                case SceneThreatKind.VehicleHazard:
                    ReactVehicleHazard(ped, s, scene, cfg);
                    break;
                case SceneThreatKind.Gunfire:
                    ReactGunfire(ped, s, scene, cfg);
                    break;
                case SceneThreatKind.Fight:
                    ReactFight(ped, s, scene, nearby, states, cfg);
                    break;
                case SceneThreatKind.VisibleWeapon:
                    ReactWeapon(ped, s, scene, nearby, states, cfg);
                    break;
                case SceneThreatKind.Body:
                    ReactBody(ped, s, scene, cfg);
                    break;
                case SceneThreatKind.Fire:
                    ReactFire(ped, s, scene, cfg);
                    break;
                case SceneThreatKind.Explosion:
                    ReactExplosion(ped, s, scene, cfg);
                    break;
                case SceneThreatKind.CrowdFlight:
                case SceneThreatKind.SocialWarning:
                    ReactSocial(ped, s, scene, cfg);
                    break;
                default:
                    return false;
            }

            if (old != s.Mode && cfg.LogStateTransitions && log != null)
                log("Ped " + s.Handle + " scene decision " + old + " -> " + s.Mode + " source=" + scene.Kind + " conf=" + (int)(scene.Confidence * 100f) + " stage=" + s.Stage + ".");
            return true;
        }

        private static bool PlayerImmediateThreat(Ped player, Ped observer, Config cfg)
        {
            if (player == null || observer == null || !player.Exists() || !observer.Exists()) return false;
            if (!SensorySystem.HasVisual(observer, player, cfg.ThreatVisualRadius, cfg.PeripheralVisualFovDegrees)) return false;
            try { if (Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle)) return true; } catch { }
            try
            {
                if (!Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle)) return false;
                var arg = new OutputArgument();
                return Function.Call<bool>(Hash.GET_ENTITY_PLAYER_IS_FREE_AIMING_AT, Game.Player.Handle, arg) && arg.GetResult<int>() == observer.Handle;
            }
            catch { return false; }
        }

        private static void ReactVehicleHazard(Ped ped, PedState s, ScenePerception scene, Config cfg)
        {
            if (ped.IsInVehicle()) return;
            Vector3 v = scene.Velocity;
            float speed = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
            if (speed < 0.1f) return;

            Vector3 dir = new Vector3(v.X / speed, v.Y / speed, 0f);
            Vector3 lateralA = new Vector3(-dir.Y, dir.X, 0f);
            Vector3 lateralB = new Vector3(dir.Y, -dir.X, 0f);
            Vector3 fromVehicle = ped.Position - scene.Position;
            float side = fromVehicle.X * lateralA.X + fromVehicle.Y * lateralA.Y;
            Vector3 lateral = side >= 0f ? lateralA : lateralB;
            float step = scene.Immediate ? cfg.VehicleEmergencySidestepDistance : cfg.VehicleSidestepDistance;
            Vector3 target = ped.Position + lateral * step;

            try
            {
                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, ped.Handle, target.X, target.Y, target.Z, scene.Immediate ? 3.0f : 1.7f, scene.Immediate ? 1400 : 2600, 0f, 0f);
            }
            catch { FleeFromCoord(ped, scene.Position, cfg, true); }
            s.Mode = ReactionMode.Evade;
            Stamp(s, cfg, scene.Immediate ? 1200 : 2100);
        }

        private static void ReactGunfire(Ped ped, PedState s, ScenePerception scene, Config cfg)
        {
            if (!scene.SourceKnown && !scene.Visual)
            {
                int roll = s.Roll(Game.GameTime / 700 + 201);
                if (cfg.SeekCoverWhenThreatened && roll < 42 + s.SelfPreservation / 3)
                {
                    SeekCoverFromCoord(ped, scene.Position, s, cfg);
                    return;
                }
                Freeze(ped, s, cfg, 800, 2200);
                return;
            }

            if (scene.Distance > 38f && s.Fear < 58f && s.Archetype == PedArchetype.Curious)
            {
                LookAtSource(ped, scene, 900 + s.Roll(203) * 9);
                s.Mode = ReactionMode.Watch;
                Stamp(s, cfg, 1200);
                return;
            }

            if (cfg.SeekCoverWhenThreatened && scene.Distance > 15f && s.Roll(Game.GameTime / 650 + 205) < 45 + s.Alertness / 3)
            {
                SeekCoverFromCoord(ped, scene.Position, s, cfg);
                return;
            }
            FleeFromCoord(ped, scene.Position, cfg, true);
            s.Mode = ReactionMode.Flee;
            Stamp(s, cfg, 2600);
        }

        private static void ReactFight(Ped ped, PedState s, ScenePerception scene, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg)
        {
            if (s.Stage <= AwarenessStage.Suspicious)
            {
                LookAtSource(ped, scene, 800 + s.Roll(211) * 10);
                s.Mode = ReactionMode.Watch;
                Stamp(s, cfg, 1100);
                return;
            }

            if (cfg.BystanderInterventionEnabled && s.Archetype == PedArchetype.Protective && s.Bravery >= cfg.InterventionMinBravery && s.Aggression < 65 && scene.Severity <= cfg.InterventionMaxThreatSeverity && scene.Distance <= cfg.InterventionMaxDistance)
            {
                Vector3 away = SafeDirection(scene.Position, ped.Position);
                Vector3 target = scene.Position + away * 4.5f;
                try { Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle, target.X, target.Y, target.Z, 1.2f, 3500, 1.5f, 0, 0f); } catch { }
                s.Mode = ReactionMode.Assist;
                Stamp(s, cfg, 2600);
                return;
            }

            if (s.Curiosity >= 72 && s.Bravery >= 52 && scene.Distance > 20f && scene.Distance < 55f && s.Fear < 48f && s.Roll(Game.GameTime / 1700 + 213) < 32)
            {
                StartScenario(ped, "WORLD_HUMAN_MOBILE_FILM_SHOCKING");
                s.Mode = ReactionMode.Film;
                Stamp(s, cfg, 3500);
                return;
            }
            DiscreetLeaveFromCoord(ped, s, scene.Position, cfg, s.Fear > 55f ? 1.25f : 0.95f);
        }

        private static void ReactWeapon(Ped ped, PedState s, ScenePerception scene, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg)
        {
            if (s.Stage == AwarenessStage.Noticed || s.Stage == AwarenessStage.Suspicious)
            {
                LookAtSource(ped, scene, 650 + s.Roll(221) * 8);
                s.Mode = ReactionMode.Glance;
                Stamp(s, cfg, 900);
                return;
            }
            if (s.Stage == AwarenessStage.Concerned && scene.Distance > 10f)
            {
                DiscreetLeaveFromCoord(ped, s, scene.Position, cfg, 0.9f);
                return;
            }
            if (cfg.SeekCoverWhenThreatened && scene.Distance > 12f && s.Roll(Game.GameTime / 800 + 223) < 40 + s.SelfPreservation / 3)
            {
                SeekCoverFromCoord(ped, scene.Position, s, cfg);
                return;
            }
            FleeFromCoord(ped, scene.Position, cfg, false);
            s.Mode = ReactionMode.Flee;
            Stamp(s, cfg, 2400);
        }

        private static void ReactBody(Ped ped, PedState s, ScenePerception scene, Config cfg)
        {
            if (s.Stage <= AwarenessStage.Suspicious && s.Curiosity >= 45 && s.Fear < 45f)
            {
                LookAtSource(ped, scene, 1000 + s.Roll(231) * 10);
                s.Mode = ReactionMode.Investigate;
                Stamp(s, cfg, 1700);
                return;
            }
            if (cfg.PhoneWhenSafe && s.Empathy >= 45 && s.Fear < 62f && scene.Distance > 10f && s.Roll(Game.GameTime / 1900 + 233) < 45)
            {
                StartScenario(ped, "WORLD_HUMAN_STAND_MOBILE");
                s.Mode = ReactionMode.Phone;
                Stamp(s, cfg, 3600);
                return;
            }
            DiscreetLeaveFromCoord(ped, s, scene.Position, cfg, 0.85f);
        }

        private static void ReactFire(Ped ped, PedState s, ScenePerception scene, Config cfg)
        {
            if (scene.Distance < 16f || s.Stage == AwarenessStage.Panic)
            {
                FleeFromCoord(ped, scene.Position, cfg, true);
                s.Mode = ReactionMode.Flee;
                Stamp(s, cfg, 2800);
                return;
            }
            DiscreetLeaveFromCoord(ped, s, scene.Position, cfg, 1.2f);
        }

        private static void ReactExplosion(Ped ped, PedState s, ScenePerception scene, Config cfg)
        {
            if (cfg.SeekCoverWhenThreatened && s.Roll(Game.GameTime / 600 + 241) < 48 + s.Alertness / 3)
            {
                SeekCoverFromCoord(ped, scene.Position, s, cfg);
                return;
            }
            FleeFromCoord(ped, scene.Position, cfg, true);
            s.Mode = ReactionMode.Flee;
            Stamp(s, cfg, 3200);
        }

        private static void ReactSocial(Ped ped, PedState s, ScenePerception scene, Config cfg)
        {
            if (s.Conformity < 28 && s.Stage <= AwarenessStage.Suspicious)
            {
                if (scene.SourceHandle > 0) LookAtSource(ped, scene, 650 + s.Roll(251) * 7);
                s.Mode = ReactionMode.Glance;
                Stamp(s, cfg, 900);
                return;
            }
            if (s.Stage < AwarenessStage.Panic)
            {
                DiscreetLeaveFromCoord(ped, s, scene.Position, cfg, 0.9f + s.Conformity / 250f);
                return;
            }
            FleeFromCoord(ped, scene.Position, cfg, false);
            s.Mode = ReactionMode.Flee;
            Stamp(s, cfg, 2400);
        }

        private static void DiscreetLeaveFromCoord(Ped ped, PedState s, Vector3 threat, Config cfg, float urgency)
        {
            Vector3 away = SafeDirection(threat, ped.Position);
            float distance = cfg.DiscreetLeaveMinDistance + s.Roll(261) / 99f * Math.Max(1f, cfg.DiscreetLeaveMaxDistance - cfg.DiscreetLeaveMinDistance);
            Vector3 target = ped.Position + away * distance;
            float speed = Math.Max(0.75f, Math.Min(1.55f, cfg.DiscreetWalkSpeed * urgency));
            try { Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle, target.X, target.Y, target.Z, speed, 10000, 1.2f, 0, 0f); } catch { }
            s.Mode = ReactionMode.DiscreetLeave;
            s.LastSafeDirection = away;
            Stamp(s, cfg, 2800);
        }

        private static void FleeFromCoord(Ped ped, Vector3 threat, Config cfg, bool urgent)
        {
            try { Function.Call(Hash.TASK_SMART_FLEE_COORD, ped.Handle, threat.X, threat.Y, threat.Z, cfg.FleeDistance, cfg.FleeDurationMs, false, false); }
            catch
            {
                Vector3 away = SafeDirection(threat, ped.Position);
                Vector3 target = ped.Position + away * Math.Min(60f, cfg.FleeDistance);
                try { Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle, target.X, target.Y, target.Z, urgent ? 2.5f : 1.8f, cfg.FleeDurationMs, 2f, 0, 0f); } catch { }
            }
        }

        private static void SeekCoverFromCoord(Ped ped, Vector3 threat, PedState s, Config cfg)
        {
            try { Function.Call(Hash.TASK_SEEK_COVER_FROM_POS, ped.Handle, threat.X, threat.Y, threat.Z, 6500, false); }
            catch { FleeFromCoord(ped, threat, cfg, false); }
            s.Mode = ReactionMode.Cover;
            Stamp(s, cfg, 3200);
        }

        private static void Freeze(Ped ped, PedState s, Config cfg, int min, int max)
        {
            int duration = min + s.Roll(271) * Math.Max(1, max - min) / 100;
            try { Function.Call(Hash.TASK_STAND_STILL, ped.Handle, duration); } catch { }
            s.Mode = ReactionMode.Freeze;
            Stamp(s, cfg, duration);
        }

        private static void LookAtSource(Ped ped, ScenePerception scene, int duration)
        {
            try
            {
                if (scene.SourceHandle > 0 && Function.Call<bool>(Hash.DOES_ENTITY_EXIST, scene.SourceHandle))
                    Function.Call(Hash.TASK_LOOK_AT_ENTITY, ped.Handle, scene.SourceHandle, duration, 0, 2);
                else
                    Function.Call(Hash.TASK_LOOK_AT_COORD, ped.Handle, scene.Position.X, scene.Position.Y, scene.Position.Z, duration, 0, 2);
            }
            catch { }
        }

        private static void StartScenario(Ped ped, string scenario)
        {
            try { Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, scenario, 0, true); } catch { }
        }

        private static Vector3 SafeDirection(Vector3 threat, Vector3 ped)
        {
            Vector3 d = ped - threat;
            float len = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len < 0.05f) return new Vector3(1f, 0f, 0f);
            return new Vector3(d.X / len, d.Y / len, 0f);
        }

        private static void Stamp(PedState s, Config cfg, int hold)
        {
            int now = Game.GameTime;
            int finalHold = Math.Max(300, hold);
            if (IsCommittedSceneMode(s.Mode))
            {
                int span = Math.Max(0, cfg.SurvivalCommitmentMaxMs - cfg.SurvivalCommitmentMinMs);
                int commitment = cfg.SurvivalCommitmentMinMs + (int)(span * s.Roll01(421 + (int)s.Mode));
                finalHold = Math.Max(finalHold, commitment);
            }
            s.LastDecisionAt = now;
            s.DecisionUntil = now + finalHold;
        }

        private static bool IsCommittedSceneMode(ReactionMode mode)
        {
            return mode == ReactionMode.Evade || mode == ReactionMode.Freeze || mode == ReactionMode.Cower ||
                   mode == ReactionMode.Flee || mode == ReactionMode.Cover || mode == ReactionMode.Surrender ||
                   mode == ReactionMode.Combat || mode == ReactionMode.DriveAway;
        }
    }
}
