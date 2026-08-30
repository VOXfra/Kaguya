using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PedOverhaulVI
{
    internal enum SceneThreatKind
    {
        None = 0,
        Gunfire = 1,
        Fight = 2,
        VisibleWeapon = 3,
        Body = 4,
        Fire = 5,
        VehicleHazard = 6,
        Explosion = 7,
        CrowdFlight = 8,
        SocialWarning = 9
    }

    internal sealed class SceneEventInfo
    {
        public SceneThreatKind Kind;
        public int SourceHandle;
        public int TargetHandle;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Severity;
        public float VisualRadius;
        public float AudibleRadius;
        public int CreatedAt;
        public int ExpiresAt;
        public bool SourceKnown;
    }

    internal sealed class ScenePerception
    {
        public bool HasThreat;
        public SceneThreatKind Kind;
        public int SourceHandle;
        public int TargetHandle;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Distance;
        public float Confidence;
        public float Severity;
        public bool Visual;
        public bool Audible;
        public bool Immediate;
        public bool SourceKnown;
        public float TimeToImpact;
    }

    internal sealed class SceneRuntime
    {
        private readonly List<SceneEventInfo> _events = new List<SceneEventInfo>();
        private int _lastScan;
        private int _lastExplosionSeen;

        public IList<SceneEventInfo> Events { get { return _events; } }

        public void Update(Ped player, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg, Action<string> log)
        {
            int now = Game.GameTime;
            for (int i = _events.Count - 1; i >= 0; i--)
                if (_events[i] == null || now >= _events[i].ExpiresAt) _events.RemoveAt(i);

            if (!cfg.SceneAwarenessEnabled || player == null || !player.Exists()) return;
            if (now - _lastScan < Math.Max(120, cfg.SceneScanIntervalMs)) return;
            _lastScan = now;

            ScanPeds(nearby, states, cfg, now);
            ScanVehicles(player, cfg, now);
            ScanExplosionField(player, cfg, now);

            if (_events.Count > cfg.MaxSceneEvents)
                _events.RemoveRange(0, _events.Count - cfg.MaxSceneEvents);
        }

        public ScenePerception Sense(Ped observer, PedState state, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg)
        {
            var best = new ScenePerception();
            if (!cfg.SceneAwarenessEnabled || observer == null || !observer.Exists() || state == null) return best;

            float bestScore = 0f;
            foreach (SceneEventInfo e in _events)
            {
                if (e == null || e.SourceHandle == observer.Handle) continue;
                ScenePerception p = EvaluateEvent(observer, state, e, cfg);
                if (p == null || !p.HasThreat) continue;
                float score = p.Severity * p.Confidence * (p.Immediate ? 1.55f : 1f) * (0.75f + state.Alertness / 200f);
                if (score <= bestScore) continue;
                bestScore = score;
                best = p;
            }

            ScenePerception social = SenseDirectWarning(observer, state, nearby, states, cfg);
            if (social != null && social.HasThreat)
            {
                float socialScore = social.Severity * social.Confidence * (0.72f + state.Conformity / 220f);
                if (socialScore > bestScore) best = social;
            }
            return best;
        }

        public void ApplyCognition(PedState s, ScenePerception p, Config cfg, int now)
        {
            if (s == null || p == null || !p.HasThreat) return;
            float alert = 0.72f + s.Alertness / 180f;
            float preservation = 0.62f + s.SelfPreservation / 165f;
            float brave = 1.20f - s.Bravery / 190f;
            float confidence = Math.Max(0.05f, Math.Min(1f, p.Confidence));
            float proximity = 1f - Math.Min(1f, p.Distance / Math.Max(1f, cfg.ProcessRadius));

            s.LastSceneEventAt = now;
            s.SceneThreatKind = p.Kind;
            s.ThreatSourceHandle = p.SourceHandle;
            s.ThreatSourceKnown = p.SourceKnown;
            s.ExternalThreatConfidence = Math.Max(s.ExternalThreatConfidence, confidence * 100f);
            if (p.Position != Vector3.Zero) s.LastThreatPosition = p.Position;

            switch (p.Kind)
            {
                case SceneThreatKind.Fight:
                    s.Attention = Add(s.Attention, 18f * alert * confidence);
                    s.Suspicion = Add(s.Suspicion, cfg.FightSuspicion * alert * confidence);
                    s.Certainty = Add(s.Certainty, 22f * confidence);
                    s.Fear = Add(s.Fear, (8f + 18f * proximity) * preservation * brave * confidence);
                    s.SawExternalFight = true;
                    break;
                case SceneThreatKind.VisibleWeapon:
                    s.Attention = Add(s.Attention, 22f * alert * confidence);
                    s.Suspicion = Add(s.Suspicion, cfg.ExternalWeaponSuspicion * alert * confidence);
                    s.Certainty = Add(s.Certainty, 34f * confidence);
                    s.Fear = Add(s.Fear, (12f + 28f * proximity) * preservation * brave * confidence);
                    s.SawExternalWeapon = true;
                    break;
                case SceneThreatKind.Gunfire:
                    s.Attention = Add(s.Attention, 45f * alert * confidence);
                    s.Suspicion = Math.Max(s.Suspicion, 60f * confidence);
                    s.Certainty = Add(s.Certainty, p.Visual ? 62f * confidence : 28f * confidence);
                    s.Fear = Add(s.Fear, (35f + 50f * proximity) * preservation * brave * confidence);
                    s.HeardExternalGunfire = true;
                    if (p.Visual) s.SawViolence = true;
                    break;
                case SceneThreatKind.Body:
                    s.Attention = Add(s.Attention, 28f * alert * confidence);
                    s.Suspicion = Add(s.Suspicion, cfg.ExternalBodySuspicion * confidence);
                    s.Certainty = Add(s.Certainty, 24f * confidence);
                    s.Fear = Add(s.Fear, (14f + 18f * proximity) * preservation * brave * confidence);
                    s.SawBody = true;
                    break;
                case SceneThreatKind.Fire:
                    s.Attention = Add(s.Attention, 45f * confidence);
                    s.Suspicion = Math.Max(s.Suspicion, 52f);
                    s.Certainty = Math.Max(s.Certainty, 70f * confidence);
                    s.Fear = Add(s.Fear, (28f + 50f * proximity) * preservation * brave * confidence);
                    s.SawFire = true;
                    break;
                case SceneThreatKind.VehicleHazard:
                    s.Attention = Math.Max(s.Attention, 90f);
                    s.Suspicion = Math.Max(s.Suspicion, 80f);
                    s.Certainty = Math.Max(s.Certainty, p.Immediate ? 100f : 78f);
                    s.Fear = Add(s.Fear, (p.Immediate ? 82f : 48f) * preservation * brave);
                    s.VehicleHazardTtc = p.TimeToImpact;
                    s.SawVehicleHazard = true;
                    break;
                case SceneThreatKind.Explosion:
                    s.Attention = Math.Max(s.Attention, 90f);
                    s.Suspicion = Math.Max(s.Suspicion, 85f);
                    s.Certainty = Math.Max(s.Certainty, p.Visual ? 96f : 72f);
                    s.Fear = Add(s.Fear, 75f * preservation * brave * confidence);
                    s.HeardExplosion = true;
                    break;
                case SceneThreatKind.CrowdFlight:
                    s.Attention = Add(s.Attention, 20f * confidence);
                    s.Suspicion = Add(s.Suspicion, cfg.CrowdFlightSuspicion * (0.60f + s.Conformity / 180f) * confidence);
                    s.Fear = Add(s.Fear, 14f * preservation * (0.55f + s.Conformity / 190f) * confidence);
                    break;
                case SceneThreatKind.SocialWarning:
                    s.Attention = Add(s.Attention, 28f * confidence);
                    s.Suspicion = Add(s.Suspicion, cfg.DirectWarningSuspicion * confidence);
                    s.Certainty = Add(s.Certainty, 22f * confidence);
                    s.SocialThreatConfidence = Math.Max(s.SocialThreatConfidence, confidence * 100f);
                    break;
            }

            s.FirstNoticedAt = s.FirstNoticedAt == 0 ? now : s.FirstNoticedAt;
            s.LastStimulusAt = now;
            s.Stage = SituationModel.DetermineStage(s, cfg);
        }

        private void ScanPeds(IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg, int now)
        {
            if (nearby == null) return;
            int count = 0;
            foreach (Ped p in nearby)
            {
                if (p == null || !p.Exists() || !p.IsHuman) continue;
                if (++count > cfg.MaxProcessedPeds + 10) break;

                if (p.IsDead)
                {
                    AddOrRefresh(SceneThreatKind.Body, p.Handle, 0, p.Position, Vector3.Zero, 58f, cfg.ExternalBodyVisualRadius, 0f, now, cfg.SceneEventMemoryMs, true);
                    continue;
                }

                bool shooting = SafeBool(Hash.IS_PED_SHOOTING, p.Handle);
                bool melee = SafeBool(Hash.IS_PED_IN_MELEE_COMBAT, p.Handle);
                bool combat = SafeBool(Hash.IS_PED_IN_COMBAT, p.Handle, Game.LocalPlayerPed != null ? Game.LocalPlayerPed.Handle : 0);
                bool onFire = SafeBool(Hash.IS_ENTITY_ON_FIRE, p.Handle);
                bool armed = VisibleWeaponDrawn(p);

                if (shooting)
                    AddOrRefresh(SceneThreatKind.Gunfire, p.Handle, 0, p.Position, Vector3.Zero, 96f, cfg.ExternalGunfireVisualRadius, cfg.ExternalGunfireAudibleRadius, now, 2600, true);
                else if (melee)
                    AddOrRefresh(SceneThreatKind.Fight, p.Handle, 0, p.Position, Vector3.Zero, 46f, cfg.FightVisualRadius, cfg.FightAudibleRadius, now, 1800, true);

                if (armed && (melee || combat || shooting))
                    AddOrRefresh(SceneThreatKind.VisibleWeapon, p.Handle, 0, p.Position, Vector3.Zero, shooting ? 88f : 56f, cfg.ExternalWeaponVisualRadius, 0f, now, 2200, true);

                if (onFire)
                    AddOrRefresh(SceneThreatKind.Fire, p.Handle, 0, p.Position, Vector3.Zero, 94f, cfg.FireAwarenessRadius, cfg.FireAwarenessRadius * 0.45f, now, 2600, true);

                PedState state;
                if (states != null && states.TryGetValue(p.Handle, out state) &&
                    (state.Mode == ReactionMode.Flee || state.Mode == ReactionMode.Cower || state.Mode == ReactionMode.Cover || state.Mode == ReactionMode.Evade) &&
                    state.Stage >= AwarenessStage.Concerned)
                {
                    AddOrRefresh(SceneThreatKind.CrowdFlight, p.Handle, 0, p.Position, Vector3.Zero, 34f, cfg.SocialAwarenessRadius, 0f, now, 1600, false);
                }
            }
        }

        private void ScanVehicles(Ped player, Config cfg, int now)
        {
            Vehicle[] vehicles;
            try { vehicles = World.GetNearbyVehicles(player.Position, cfg.SceneVehicleScanRadius); }
            catch { return; }
            int count = 0;
            foreach (Vehicle v in vehicles)
            {
                if (v == null || !v.Exists()) continue;
                if (++count > cfg.MaxSceneVehicles) break;
                float speed = 0f;
                Vector3 velocity = Vector3.Zero;
                try
                {
                    speed = Function.Call<float>(Hash.GET_ENTITY_SPEED, v.Handle);
                    velocity = Function.Call<Vector3>(Hash.GET_ENTITY_VELOCITY, v.Handle);
                }
                catch { }
                if (speed < cfg.VehicleHazardMinSpeedMps) continue;
                AddOrRefresh(SceneThreatKind.VehicleHazard, v.Handle, 0, v.Position, velocity, Math.Min(100f, 35f + speed * 4f), cfg.VehicleHazardVisualRadius, cfg.VehicleHazardAudibleRadius, now, 700, true);
            }
        }

        private void ScanExplosionField(Ped player, Config cfg, int now)
        {
            if (!cfg.ExplosionAwarenessEnabled || now - _lastExplosionSeen < 900) return;
            Vector3 p = player.Position;
            bool found = false;
            for (int type = 0; type <= 36; type++)
            {
                try
                {
                    if (Function.Call<bool>(Hash.IS_EXPLOSION_IN_SPHERE, type, p.X, p.Y, p.Z, cfg.ExplosionDetectionRadius))
                    {
                        found = true;
                        break;
                    }
                }
                catch { }
            }
            if (!found) return;
            _lastExplosionSeen = now;
            AddOrRefresh(SceneThreatKind.Explosion, -777, 0, p, Vector3.Zero, 100f, cfg.ExplosionDetectionRadius, cfg.ExplosionAwarenessRadius, now, 1800, false);
        }

        private ScenePerception EvaluateEvent(Ped observer, PedState state, SceneEventInfo e, Config cfg)
        {
            Vector3 eventPos = ResolvePosition(e);
            float d = Distance(observer.Position, eventPos);
            bool visual = false;
            bool audible = e.AudibleRadius > 0f && d <= e.AudibleRadius;
            float confidence = 0f;

            if (e.Kind == SceneThreatKind.VehicleHazard)
            {
                float tti, miss;
                if (!VehicleTrajectoryThreat(observer.Position, eventPos, e.Velocity, cfg, out tti, out miss)) return null;
                visual = d <= e.VisualRadius && HasVisualAccess(observer, e.SourceHandle, eventPos, cfg.PeripheralVisualFovDegrees);
                audible = audible || d <= cfg.VehicleHazardAudibleRadius;
                if (!visual && !audible) return null;
                confidence = visual ? 0.95f : 0.62f;
                return new ScenePerception { HasThreat = true, Kind = e.Kind, SourceHandle = e.SourceHandle, Position = eventPos, Velocity = e.Velocity, Distance = d, Confidence = confidence, Severity = e.Severity, Visual = visual, Audible = audible, Immediate = tti <= cfg.VehicleImmediateTtcSeconds, SourceKnown = visual, TimeToImpact = tti };
            }

            if (e.VisualRadius > 0f && d <= e.VisualRadius)
                visual = HasVisualAccess(observer, e.SourceHandle, eventPos, cfg.PeripheralVisualFovDegrees);
            if (!visual && !audible) return null;

            if (visual)
                confidence = Math.Max(0.30f, 1f - d / Math.Max(1f, e.VisualRadius) * 0.62f);
            else if (audible)
                confidence = Math.Max(0.22f, 1f - d / Math.Max(1f, e.AudibleRadius) * 0.70f);

            bool immediate = e.Kind == SceneThreatKind.Gunfire && d < 24f || e.Kind == SceneThreatKind.Fire && d < 12f || e.Kind == SceneThreatKind.Explosion && d < 28f;
            return new ScenePerception
            {
                HasThreat = true,
                Kind = e.Kind,
                SourceHandle = e.SourceHandle,
                TargetHandle = e.TargetHandle,
                Position = eventPos,
                Velocity = e.Velocity,
                Distance = d,
                Confidence = confidence,
                Severity = e.Severity,
                Visual = visual,
                Audible = audible,
                Immediate = immediate,
                SourceKnown = visual && e.SourceKnown,
                TimeToImpact = 99f
            };
        }

        private ScenePerception SenseDirectWarning(Ped observer, PedState state, IList<Ped> nearby, IDictionary<int, PedState> states, Config cfg)
        {
            if (nearby == null || states == null) return null;
            PedState best = null;
            Ped bestPed = null;
            float bestTrust = 0f;
            foreach (Ped other in nearby)
            {
                if (other == null || !other.Exists() || other.IsDead || other.Handle == observer.Handle) continue;
                float d = Distance(observer.Position, other.Position);
                if (d > cfg.GroupCommunicationRadius) continue;
                PedState os;
                if (!states.TryGetValue(other.Handle, out os) || os.LastStimulusAt <= 0 || os.Certainty < cfg.ConcernedThreshold) continue;
                bool explicitWarning = os.Mode == ReactionMode.AlertNearby || os.Mode == ReactionMode.Phone;
                bool sameGroup = state.GroupId >= 0 && state.GroupId == os.GroupId;
                bool sharedVehicle = SameVehicle(observer, other);
                if (!explicitWarning && !sameGroup && !sharedVehicle) continue;
                float trust = sameGroup || sharedVehicle ? cfg.SameGroupInformationTrust : cfg.StrangerWarningTrust;
                trust *= Math.Max(0.35f, os.Certainty / 100f);
                if (trust <= bestTrust) continue;
                bestTrust = trust;
                best = os;
                bestPed = other;
            }
            if (best == null || bestPed == null) return null;
            return new ScenePerception
            {
                HasThreat = true,
                Kind = SceneThreatKind.SocialWarning,
                SourceHandle = bestPed.Handle,
                Position = best.LastThreatPosition,
                Distance = Distance(observer.Position, bestPed.Position),
                Confidence = bestTrust,
                Severity = 48f + best.Certainty * 0.35f,
                Visual = true,
                Audible = true,
                Immediate = best.Stage == AwarenessStage.Panic,
                SourceKnown = best.ThreatSourceKnown,
                TimeToImpact = 99f
            };
        }

        private void AddOrRefresh(SceneThreatKind kind, int source, int target, Vector3 pos, Vector3 velocity, float severity, float visual, float audible, int now, int ttl, bool sourceKnown)
        {
            for (int i = _events.Count - 1; i >= 0; i--)
            {
                SceneEventInfo old = _events[i];
                if (old.Kind != kind || old.SourceHandle != source) continue;
                old.Position = pos;
                old.Velocity = velocity;
                old.Severity = severity;
                old.VisualRadius = visual;
                old.AudibleRadius = audible;
                old.ExpiresAt = now + Math.Max(300, ttl);
                old.SourceKnown = sourceKnown;
                return;
            }
            _events.Add(new SceneEventInfo { Kind = kind, SourceHandle = source, TargetHandle = target, Position = pos, Velocity = velocity, Severity = severity, VisualRadius = visual, AudibleRadius = audible, CreatedAt = now, ExpiresAt = now + Math.Max(300, ttl), SourceKnown = sourceKnown });
        }

        private static Vector3 ResolvePosition(SceneEventInfo e)
        {
            if (e.SourceHandle > 0)
            {
                try
                {
                    Entity entity = Entity.FromHandle(e.SourceHandle);
                    if (entity != null && entity.Exists()) return entity.Position;
                }
                catch { }
            }
            return e.Position;
        }

        private static bool VisibleWeaponDrawn(Ped p)
        {
            if (p == null || !p.Exists()) return false;
            try
            {
                int weapon = Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON, p.Handle);
                int unarmed = Function.Call<int>(Hash.GET_HASH_KEY, "WEAPON_UNARMED");
                return weapon != 0 && weapon != unarmed && Function.Call<bool>(Hash.IS_PED_ARMED, p.Handle, 7);
            }
            catch { return false; }
        }

        private static bool HasVisualAccess(Ped observer, int sourceHandle, Vector3 position, float fov)
        {
            Vector3 forward = observer.ForwardVector;
            Vector3 from = observer.Position;
            Vector3 delta = position - from;
            double len = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
            if (len > 0.05)
            {
                double dot = (forward.X * delta.X + forward.Y * delta.Y + forward.Z * delta.Z) / len;
                double threshold = Math.Cos(Math.Max(30f, Math.Min(179f, fov)) * 0.5 * Math.PI / 180.0);
                if (dot < threshold) return false;
            }
            if (sourceHandle > 0)
            {
                try { return Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, observer.Handle, sourceHandle, 17); }
                catch { }
            }
            return true;
        }

        private static bool VehicleTrajectoryThreat(Vector3 pedPos, Vector3 vehiclePos, Vector3 velocity, Config cfg, out float t, out float miss)
        {
            t = 99f;
            miss = 999f;
            float speedSq = velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z;
            if (speedSq < cfg.VehicleHazardMinSpeedMps * cfg.VehicleHazardMinSpeedMps) return false;
            Vector3 toPed = pedPos - vehiclePos;
            float projected = (toPed.X * velocity.X + toPed.Y * velocity.Y + toPed.Z * velocity.Z) / speedSq;
            if (projected <= 0f || projected > cfg.VehicleHazardHorizonSeconds) return false;
            Vector3 closest = vehiclePos + velocity * projected;
            miss = Distance(closest, pedPos);
            t = projected;
            return miss <= cfg.VehicleCollisionMargin;
        }

        private static bool SameVehicle(Ped a, Ped b)
        {
            try
            {
                if (!a.IsInVehicle() || !b.IsInVehicle()) return false;
                Vehicle av = a.CurrentVehicle, bv = b.CurrentVehicle;
                return av != null && bv != null && av.Exists() && bv.Exists() && av.Handle == bv.Handle;
            }
            catch { return false; }
        }

        private static bool SafeBool(Hash h, params InputArgument[] args)
        {
            try { return Function.Call<bool>(h, args); }
            catch { return false; }
        }

        private static float Add(float current, float amount)
        {
            return Math.Max(0f, Math.Min(100f, current + amount));
        }

        private static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
