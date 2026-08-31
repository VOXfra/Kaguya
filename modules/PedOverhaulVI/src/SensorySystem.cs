using GTA;
using GTA.Math;
using GTA.Native;
using System;

namespace VOX.PedOverhaulVI
{
    internal static class SensorySystem
    {
        private static readonly string[] SuppressorComponents =
        {
            "COMPONENT_AT_PI_SUPP",
            "COMPONENT_AT_PI_SUPP_02",
            "COMPONENT_AT_AR_SUPP",
            "COMPONENT_AT_AR_SUPP_02",
            "COMPONENT_AT_SR_SUPP",
            "COMPONENT_AT_SR_SUPP_03"
        };

        public static bool HasVisual(Ped observer, Entity target, float maxDistance, float fovDegrees)
        {
            if (observer == null || target == null || !observer.Exists() || !target.Exists()) return false;
            return HasVisual(observer, target.Handle, target.Position, maxDistance, fovDegrees);
        }

        public static bool HasVisual(Ped observer, int targetHandle, Vector3 targetPosition, float maxDistance, float fovDegrees)
        {
            if (observer == null || !observer.Exists()) return false;
            float distance = SituationModel.Distance(observer.Position, targetPosition);
            if (distance > Math.Max(0.5f, maxDistance)) return false;

            Vector3 forward = observer.ForwardVector;
            Vector3 delta = targetPosition - observer.Position;
            double len = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
            if (len > 0.05)
            {
                double dot = (forward.X * delta.X + forward.Y * delta.Y + forward.Z * delta.Z) / len;
                double threshold = Math.Cos(Math.Max(20f, Math.Min(179f, fovDegrees)) * 0.5 * Math.PI / 180.0);
                if (dot < threshold) return false;
            }

            if (targetHandle <= 0) return true;
            try { return Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, observer.Handle, targetHandle, 17); }
            catch { return false; }
        }

        public static bool HasLineOfSight(Ped observer, Entity target)
        {
            if (observer == null || target == null || !observer.Exists() || !target.Exists()) return false;
            try { return Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, observer.Handle, target.Handle, 17); }
            catch { return false; }
        }

        public static bool IsWeaponSuppressed(Ped shooter)
        {
            if (shooter == null || !shooter.Exists()) return false;
            int weapon;
            try { weapon = Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON, shooter.Handle); }
            catch { return false; }
            if (weapon == 0) return false;

            foreach (string componentName in SuppressorComponents)
            {
                try
                {
                    int component = Function.Call<int>(Hash.GET_HASH_KEY, componentName);
                    if (component != 0 && Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON_COMPONENT, shooter.Handle, weapon, component))
                        return true;
                }
                catch { }
            }
            return false;
        }

        public static bool CanHearGunshot(Ped observer, Ped shooter, float baseRadius)
        {
            if (observer == null || shooter == null || !observer.Exists() || !shooter.Exists()) return false;
            bool suppressed = IsWeaponSuppressed(shooter);
            float clearRadius = Math.Max(2f, baseRadius * (suppressed ? 0.22f : 1f));
            return CanHearEntitySound(observer, shooter, clearRadius, suppressed ? 0.22f : 0.48f);
        }

        public static bool CanHearEvent(Ped observer, int sourceHandle, Vector3 sourcePosition, float baseRadius, SceneThreatKind kind)
        {
            if (observer == null || !observer.Exists() || baseRadius <= 0f) return false;
            float distance = SituationModel.Distance(observer.Position, sourcePosition);
            if (distance > baseRadius) return false;

            if (sourceHandle > 0)
            {
                try
                {
                    Entity source = Entity.FromHandle(sourceHandle);
                    if (source != null && source.Exists())
                    {
                        if (HasLineOfSight(observer, source)) return true;
                        float occludedFactor = kind == SceneThreatKind.Explosion ? 0.72f :
                            (kind == SceneThreatKind.Gunfire ? 0.48f : 0.34f);
                        return distance <= baseRadius * occludedFactor;
                    }
                }
                catch { }
            }

            // Unknown-source events (for example explosion-field probes) keep a
            // conservative hearing radius because we cannot prove an unobstructed path.
            return distance <= baseRadius * (kind == SceneThreatKind.Explosion ? 0.62f : 0.38f);
        }

        public static bool CanHearVocalCue(Ped observer, Ped source, float radius)
        {
            if (observer == null || source == null || !observer.Exists() || !source.Exists()) return false;
            return CanHearEntitySound(observer, source, Math.Max(3f, radius), 0.32f);
        }

        private static bool CanHearEntitySound(Ped observer, Entity source, float clearRadius, float occludedFactor)
        {
            float distance = SituationModel.Distance(observer.Position, source.Position);
            if (distance > clearRadius) return false;
            if (HasLineOfSight(observer, source)) return true;
            return distance <= clearRadius * Math.Max(0.05f, Math.Min(1f, occludedFactor));
        }
    }
}
