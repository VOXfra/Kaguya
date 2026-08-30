using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PedOverhaulVI
{
    internal static class DecisionSystem
    {
        public static void Update(Ped player, Ped ped, PedState s, PerceptionFrame frame, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg, Action<string> log)
        {
            if (player == null || ped == null || s == null || !player.Exists() || !ped.Exists() || ped.IsDead) return;
            int now = Game.GameTime;

            if (SituationModel.IsHostileToPlayer(ped, player))
            {
                UpdateHostile(player, ped, s, frame, nearby, cfg, log);
                return;
            }

            if (s.Stage == AwarenessStage.Unaware)
            {
                if (s.Mode != ReactionMode.None && now - s.LastStimulusAt >= cfg.CalmAfterMs)
                    ResumeAmbient(ped, s);
                return;
            }

            if (DistractionRuntime.ShouldDelayDecision(s, frame, cfg, now)) return;

            bool hardEmergency = frame.DirectlyAimedAt || frame.SeesShooting;

            // A decision is a commitment, not a new dice roll every tick. Only a
            // newly explicit emergency may interrupt a low-level action such as
            // watching/filming/leaving quietly. Once a survival action starts,
            // let the ped actually perform it for a believable amount of time.
            if (now < s.DecisionUntil)
            {
                if (!hardEmergency || IsCommittedSurvivalMode(s.Mode)) return;
                if (now - s.LastEmergencyReplanAt < cfg.EmergencyReplanMinMs) return;
                s.LastEmergencyReplanAt = now;
            }
            else if (!hardEmergency && !DecisionReady(s, cfg, now)) return;

            ReactionMode old = s.Mode;
            switch (s.Stage)
            {
                case AwarenessStage.Noticed:
                    DecideNoticed(player, ped, s, frame, cfg);
                    break;
                case AwarenessStage.Suspicious:
                    DecideSuspicious(player, ped, s, frame, nearby, states, cfg);
                    break;
                case AwarenessStage.Concerned:
                    DecideConcerned(player, ped, s, frame, nearby, states, cfg);
                    break;
                case AwarenessStage.ThreatConfirmed:
                    DecideConfirmed(player, ped, s, frame, cfg);
                    break;
                case AwarenessStage.Panic:
                    DecidePanic(player, ped, s, frame, cfg);
                    break;
            }

            if (old != s.Mode && cfg.LogStateTransitions && log != null)
                log("Ped " + s.Handle + " " + s.Archetype + " decision " + old + " -> " + s.Mode + " stage=" + s.Stage + " suspicion=" + (int)s.Suspicion + " certainty=" + (int)s.Certainty + " fear=" + (int)s.Fear + ".");
        }

        public static void UpdateMorale(Ped player, Ped ped, PedState s, IList<Ped> nearby, Config cfg, Action<string> log)
        {
            if (!cfg.MoraleEnabled || !SituationModel.IsHostileToPlayer(ped, player)) return;
            int health = PedState.SafeHealth(ped);
            int maxHealth = 100;
            try { maxHealth = Function.Call<int>(Hash.GET_ENTITY_MAX_HEALTH, ped.Handle); } catch { }
            int lost = Math.Max(0, s.LastHealth - health);
            if (lost > 0) s.Morale -= lost * cfg.HealthLossMoraleMultiplier;

            if (HasNearbyDeadAlly(ped, player, nearby, cfg))
            {
                if (!s.NearbyDeathCounted)
                {
                    s.Morale -= cfg.NearbyDeathMoraleLoss;
                    s.NearbyDeathCounted = true;
                }
            }
            else s.NearbyDeathCounted = false;

            if (health <= Math.Max(1, maxHealth) * cfg.LowHealthPercent / 100) s.Morale -= 4f;
            if (CountNearbyHostiles(player, ped, nearby) < 2) s.Morale -= cfg.OutnumberedMoraleLoss * 0.08f;
            s.Morale = Math.Max(0f, Math.Min(100f, s.Morale));
            s.LastHealth = health;
        }

        private static void DecideNoticed(Ped player, Ped ped, PedState s, PerceptionFrame f, Config cfg)
        {
            if (s.Archetype == PedArchetype.Detached && !f.SeesWeapon && !f.HearsGunshot) return;
            int duration = LookDuration(s, cfg, 0);
            LookAt(ped, player, duration);
            s.Mode = ReactionMode.Glance;
            StampDecision(s, cfg, duration);
        }

        private static void DecideSuspicious(Ped player, Ped ped, PedState s, PerceptionFrame f, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg)
        {
            bool suspiciousCrimeSetup = s.SawWeapon && (s.SawMask || s.Certainty >= 36f);
            bool socialExit = s.SawQuietWithdrawal && s.Conformity >= 45;

            if ((s.Mode == ReactionMode.Watch || s.Mode == ReactionMode.Glance) && (suspiciousCrimeSetup || socialExit) && cfg.DiscreetWithdrawalEnabled)
            {
                DiscreetLeave(player, ped, s, cfg, 0.75f);
                return;
            }

            if (s.Archetype == PedArchetype.Cautious && (s.Suspicion >= cfg.SuspiciousThreshold + 5f || socialExit) && cfg.DiscreetWithdrawalEnabled)
            {
                DiscreetLeave(player, ped, s, cfg, 0.65f);
                return;
            }

            if (s.Archetype == PedArchetype.Detached && !s.SawWeapon && !s.HeardGunshot)
            {
                s.Mode = ReactionMode.None;
                StampDecision(s, cfg, 1100);
                return;
            }

            if (s.Archetype == PedArchetype.Protective && TryAlertNearby(ped, s, nearby, states, cfg)) return;

            LookAt(ped, player, LookDuration(s, cfg, 1));
            s.Mode = ReactionMode.Watch;
            StampDecision(s, cfg, 900 + s.Roll(11) * 9);
        }

        private static void DecideConcerned(Ped player, Ped ped, PedState s, PerceptionFrame f, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg)
        {
            float distance = f.DistanceToPlayer;
            bool immediate = f.DirectlyAimedAt || f.SeesShooting;
            if (immediate)
            {
                DecideConfirmed(player, ped, s, f, cfg);
                return;
            }

            if (s.Archetype == PedArchetype.Protective && TryAlertNearby(ped, s, nearby, states, cfg)) return;

            if (cfg.FilmFromDistance && s.Curiosity >= cfg.FilmCuriosityThreshold && s.Bravery >= 42 && distance >= cfg.FilmMinDistance && distance <= cfg.FilmMaxDistance && s.Fear < 54f && s.Roll(Game.GameTime / 1700 + 3) < Math.Min(72, s.Curiosity - 8))
            {
                StartScenario(ped, "WORLD_HUMAN_MOBILE_FILM_SHOCKING");
                s.Mode = ReactionMode.Film;
                StampDecision(s, cfg, 4200);
                return;
            }

            if (cfg.PhoneWhenSafe && s.Certainty >= cfg.PhoneMinCertainty && distance > 28f && s.Fear < 60f && s.Archetype != PedArchetype.Detached && s.Roll(Game.GameTime / 2100 + 17) < 38 + s.Alertness / 4)
            {
                StartScenario(ped, "WORLD_HUMAN_STAND_MOBILE");
                s.Mode = ReactionMode.Phone;
                StampDecision(s, cfg, 4500);
                return;
            }

            if (cfg.DiscreetWithdrawalEnabled && (s.SelfPreservation >= 40 || s.SawWeapon || s.HeardGunshot || s.SawQuietWithdrawal))
            {
                DiscreetLeave(player, ped, s, cfg, s.Fear > 52f ? 1.15f : 0.82f);
                return;
            }

            LookAt(ped, player, LookDuration(s, cfg, 2));
            s.Mode = ReactionMode.Watch;
            StampDecision(s, cfg, 1200);
        }

        private static void DecideConfirmed(Ped player, Ped ped, PedState s, PerceptionFrame f, Config cfg)
        {
            if (TryVehicleEscape(player, ped, s, cfg)) return;

            if (f.DirectlyAimedAt && f.DistanceToPlayer < 26f)
            {
                int comply = 40 + s.SelfPreservation / 2 - s.Bravery / 3 - s.Aggression / 4;
                if (s.Roll(Game.GameTime / 700 + 29) < Math.Max(20, Math.Min(92, comply)))
                {
                    try { Function.Call(Hash.TASK_HANDS_UP, ped.Handle, 8000 + s.Roll(31) * 70, player.Handle, -1, true); }
                    catch { TryCower(ped, cfg); }
                    s.Mode = ReactionMode.Surrender;
                    StampDecision(s, cfg, 5000);
                    return;
                }
            }

            if (f.HearsGunshot && !f.ThreatSourceKnown && !f.SeesShooting)
            {
                if (cfg.SeekCoverWhenThreatened && s.SelfPreservation >= 48 && s.Roll(Game.GameTime / 900 + 41) < 58)
                {
                    SeekCover(ped, player, s, cfg);
                    return;
                }
                Freeze(ped, s, cfg);
                return;
            }

            if (s.Fear >= 68f || f.SeesShooting || s.SawViolence)
            {
                DecideSurvival(player, ped, s, f, cfg, false);
                return;
            }

            if ((s.Archetype == PedArchetype.Bold || s.Archetype == PedArchetype.Curious) && f.DistanceToPlayer > 38f && s.Fear < 52f)
            {
                LookAt(ped, player, 1300 + s.Roll(43) * 10);
                s.Mode = ReactionMode.Watch;
                StampDecision(s, cfg, 1400);
                return;
            }

            DiscreetLeave(player, ped, s, cfg, 1.20f);
        }

        private static void DecidePanic(Ped player, Ped ped, PedState s, PerceptionFrame f, Config cfg)
        {
            if (TryVehicleEscape(player, ped, s, cfg)) return;
            DecideSurvival(player, ped, s, f, cfg, true);
        }

        private static void DecideSurvival(Ped player, Ped ped, PedState s, PerceptionFrame f, Config cfg, bool panic)
        {
            float distance = f.DistanceToPlayer;
            int coverBias = (cfg.SeekCoverWhenThreatened ? 22 : 0) + s.Alertness / 4 + (distance > 18f ? 12 : -8);
            int cowerBias = s.SelfPreservation / 3 - s.Bravery / 4 + (distance < 12f ? 24 : 0);
            int freezeBias = 24 + (100 - s.Alertness) / 3 + (panic ? -8 : 10);
            int roll = s.Roll(Game.GameTime / 650 + 59);

            if (!f.DirectlyAimedAt && roll < Math.Max(6, freezeBias))
            {
                Freeze(ped, s, cfg);
                return;
            }

            roll = s.Roll(Game.GameTime / 700 + 61);
            if (cfg.SeekCoverWhenThreatened && roll < Math.Max(10, Math.Min(72, coverBias)))
            {
                SeekCover(ped, player, s, cfg);
                return;
            }

            roll = s.Roll(Game.GameTime / 750 + 67);
            if (roll < Math.Max(7, Math.Min(65, cowerBias)))
            {
                TryCower(ped, cfg);
                s.Mode = ReactionMode.Cower;
                StampDecision(s, cfg, cfg.CowerDurationMs / 2);
                return;
            }

            Flee(player, ped, s, cfg);
        }

        private static void UpdateHostile(Ped player, Ped ped, PedState s, PerceptionFrame f, IList<Ped> nearby, Config cfg, Action<string> log)
        {
            if (cfg.MoraleEnabled && s.Morale <= cfg.MoraleBreakThreshold)
            {
                BreakMorale(player, ped, s, cfg, log);
                return;
            }

            if (s.Stage >= AwarenessStage.ThreatConfirmed && s.Morale > cfg.MoraleBreakThreshold)
            {
                if (Game.GameTime < s.DecisionUntil) return;
                if (s.Bravery < 35 && s.Fear > 65f)
                {
                    Flee(player, ped, s, cfg);
                    return;
                }
                if (cfg.SeekCoverWhenThreatened && s.Bravery < 68 && s.Roll(Game.GameTime / 800 + 73) < 46)
                {
                    SeekCover(ped, player, s, cfg);
                    return;
                }
                try { Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16); }
                catch { }
                s.Mode = ReactionMode.Combat;
                StampDecision(s, cfg, 2500);
            }
        }

        private static void BreakMorale(Ped player, Ped ped, PedState s, Config cfg, Action<string> log)
        {
            if (Game.GameTime < s.DecisionUntil && IsCommittedSurvivalMode(s.Mode)) return;
            int surrenderChance = cfg.SurrenderBaseChance + (45 - s.Aggression) / 2 + (40 - s.Bravery) / 3 + s.SelfPreservation / 5;
            bool surrender = s.Roll(Game.GameTime / 1000 + 79) < Math.Max(10, Math.Min(92, surrenderChance));
            try
            {
                if (surrender)
                {
                    Function.Call(Hash.TASK_HANDS_UP, ped.Handle, cfg.SurrenderDurationMs, player.Handle, -1, true);
                    s.Mode = ReactionMode.Surrender;
                }
                else
                {
                    Function.Call(Hash.TASK_SMART_FLEE_PED, ped.Handle, player.Handle, cfg.FleeDistance, cfg.FleeDurationMs, false, false);
                    s.Mode = ReactionMode.Flee;
                }
            }
            catch { }
            StampDecision(s, cfg, 3500);
            if (log != null) log("Hostile ped " + s.Handle + " morale broke; response=" + s.Mode + ".");
        }

        private static bool TryAlertNearby(Ped ped, PedState s, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg)
        {
            if (s.Mode == ReactionMode.AlertNearby)
            {
                s.Mode = ReactionMode.None;
                return false;
            }
            Ped companion = null;
            float best = 6f;
            if (nearby != null)
            {
                foreach (Ped p in nearby)
                {
                    if (p == null || !p.Exists() || p.IsDead || p.Handle == ped.Handle) continue;
                    float d = SituationModel.Distance(p.Position, ped.Position);
                    if (d >= best) continue;
                    PedState ps;
                    if (!states.TryGetValue(p.Handle, out ps) || ps.Stage >= AwarenessStage.ThreatConfirmed) continue;
                    best = d;
                    companion = p;
                }
            }
            if (companion == null) return false;
            try
            {
                Function.Call(Hash.TASK_LOOK_AT_ENTITY, ped.Handle, companion.Handle, 1200, 0, 2);
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, ped.Handle, companion.Handle, 900);
            }
            catch { }
            s.Mode = ReactionMode.AlertNearby;
            StampDecision(s, cfg, 1200);
            return true;
        }

        private static void DiscreetLeave(Ped player, Ped ped, PedState s, Config cfg, float urgency)
        {
            Vector3 threat = s.LastThreatPosition == Vector3.Zero ? player.Position : s.LastThreatPosition;
            Vector3 away = AwayDestination(ped, threat, s, cfg, urgency);
            try
            {
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle, away.X, away.Y, away.Z, Math.Max(0.75f, cfg.DiscreetWalkSpeed * urgency), 9000, 1.2f, false, 0f);
            }
            catch
            {
                try { Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, ped.Handle, away.X, away.Y, away.Z, Math.Max(0.75f, cfg.DiscreetWalkSpeed * urgency), 9000, 0f, 0f); } catch { }
            }
            s.LastSafeDirection = away;
            s.Mode = ReactionMode.DiscreetLeave;
            StampDecision(s, cfg, 4200);
        }

        private static Vector3 AwayDestination(Ped ped, Vector3 threat, PedState s, Config cfg, float urgency)
        {
            Vector3 p = ped.Position;
            double dx = p.X - threat.X, dy = p.Y - threat.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.1) { dx = Math.Cos(s.Handle); dy = Math.Sin(s.Handle); len = 1.0; }
            dx /= len; dy /= len;
            float distance = cfg.DiscreetLeaveMinDistance + (cfg.DiscreetLeaveMaxDistance - cfg.DiscreetLeaveMinDistance) * s.Roll01(83);
            distance *= Math.Max(0.75f, Math.Min(1.35f, urgency));
            double lateral = (s.Roll01(89) - 0.5f) * distance * 0.72f;
            return new Vector3(p.X + (float)(dx * distance - dy * lateral), p.Y + (float)(dy * distance + dx * lateral), p.Z);
        }

        private static void Freeze(Ped ped, PedState s, Config cfg)
        {
            int duration = cfg.FreezeMinMs + (int)((cfg.FreezeMaxMs - cfg.FreezeMinMs) * s.Roll01(97));
            try { Function.Call(Hash.TASK_STAND_STILL, ped.Handle, duration); } catch { }
            s.Mode = ReactionMode.Freeze;
            StampDecision(s, cfg, duration);
        }

        private static void SeekCover(Ped ped, Ped player, PedState s, Config cfg)
        {
            try { Function.Call(Hash.TASK_SEEK_COVER_FROM_PED, ped.Handle, player.Handle, 6500, false); }
            catch { Flee(player, ped, s, cfg); return; }
            s.Mode = ReactionMode.Cover;
            StampDecision(s, cfg, 4200);
        }

        private static void Flee(Ped player, Ped ped, PedState s, Config cfg)
        {
            try { Function.Call(Hash.TASK_SMART_FLEE_PED, ped.Handle, player.Handle, cfg.FleeDistance, cfg.FleeDurationMs, false, false); }
            catch { }
            s.Mode = ReactionMode.Flee;
            StampDecision(s, cfg, 5000);
        }

        private static void TryCower(Ped ped, Config cfg)
        {
            try { Function.Call(Hash.TASK_COWER, ped.Handle, cfg.CowerDurationMs); }
            catch { }
        }

        private static bool TryVehicleEscape(Ped player, Ped ped, PedState s, Config cfg)
        {
            if (!ped.IsInVehicle()) return false;
            Vehicle v = ped.CurrentVehicle;
            if (v == null || !v.Exists() || v.Driver == null || !v.Driver.Exists() || v.Driver.Handle != ped.Handle) return false;
            try
            {
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, ped.Handle, v.Handle, 24f, 786603);
                s.Mode = ReactionMode.DriveAway;
                StampDecision(s, cfg, 5000);
                return true;
            }
            catch { return false; }
        }

        private static void LookAt(Ped ped, Ped target, int duration)
        {
            try { Function.Call(Hash.TASK_LOOK_AT_ENTITY, ped.Handle, target.Handle, duration, 0, 2); }
            catch { }
        }

        private static int LookDuration(PedState s, Config cfg, int salt)
        {
            int span = Math.Max(0, cfg.MaximumLookMs - cfg.MinimumLookMs);
            int curiousBonus = s.Curiosity / 5;
            return cfg.MinimumLookMs + (int)(span * s.Roll01(101 + salt)) + curiousBonus;
        }

        private static void StartScenario(Ped ped, string scenario)
        {
            try { Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, scenario, 0, true); }
            catch { }
        }

        private static bool DecisionReady(PedState s, Config cfg, int now)
        {
            int span = Math.Max(0, cfg.DecisionCooldownMaxMs - cfg.DecisionCooldownMinMs);
            int baseDelay = cfg.DecisionCooldownMaxMs - (int)(span * s.Alertness / 100f);
            return now - s.LastDecisionAt >= Math.Max(120, baseDelay);
        }

        private static void StampDecision(PedState s, Config cfg, int lockMs)
        {
            int now = Game.GameTime;
            int hold = Math.Max(150, lockMs);
            if (IsCommittedSurvivalMode(s.Mode))
            {
                int span = Math.Max(0, cfg.SurvivalCommitmentMaxMs - cfg.SurvivalCommitmentMinMs);
                int commitment = cfg.SurvivalCommitmentMinMs + (int)(span * s.Roll01(353 + (int)s.Mode));
                hold = Math.Max(hold, commitment);
            }
            s.LastDecisionAt = now;
            s.DecisionUntil = now + hold;
        }

        private static bool IsCommittedSurvivalMode(ReactionMode mode)
        {
            return mode == ReactionMode.Freeze || mode == ReactionMode.Cower || mode == ReactionMode.Flee ||
                   mode == ReactionMode.Cover || mode == ReactionMode.Surrender || mode == ReactionMode.Combat ||
                   mode == ReactionMode.DriveAway || mode == ReactionMode.Evade;
        }

        private static void ResumeAmbient(Ped ped, PedState s)
        {
            try
            {
                if (s.Mode != ReactionMode.Glance && s.Mode != ReactionMode.Watch)
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                    Function.Call(Hash.TASK_WANDER_STANDARD, ped.Handle, 10f, 10);
                }
            }
            catch { }
            s.Mode = ReactionMode.None;
            s.Stage = AwarenessStage.Unaware;
            s.Attention = Math.Min(s.Attention, 5f);
            s.Suspicion = Math.Min(s.Suspicion, 8f);
            s.Certainty = Math.Min(s.Certainty, 8f);
            s.Fear = Math.Min(s.Fear, 8f);
            s.DecisionUntil = 0;
            s.FirstNoticedAt = 0;
        }

        private static bool HasNearbyDeadAlly(Ped ped, Ped player, IList<Ped> nearby, Config cfg)
        {
            if (nearby == null) return false;
            bool disposition = SituationModel.IsHostileToPlayer(ped, player);
            foreach (Ped p in nearby)
            {
                if (p == null || !p.Exists() || !p.IsDead) continue;
                if (SituationModel.Distance(p.Position, ped.Position) > cfg.NearbyDeathRadius) continue;
                if (SituationModel.IsHostileToPlayer(p, player) == disposition) return true;
            }
            return false;
        }

        private static int CountNearbyHostiles(Ped player, Ped around, IList<Ped> nearby)
        {
            int c = 0;
            if (nearby == null) return c;
            foreach (Ped p in nearby)
            {
                if (p == null || !p.Exists() || p.IsDead) continue;
                if (SituationModel.Distance(p.Position, around.Position) > 24f) continue;
                if (SituationModel.IsHostileToPlayer(p, player)) c++;
            }
            return c;
        }
    }
}
