using GTA;
using GTA.Math;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class WitnessObservation
    {
        public Ped Witness;
        public bool IsPolice;
        public float Distance;
    }

    internal static class Perception
    {
        public static WitnessObservation FindBestWitness(Ped player, Config cfg)
        {
            float radius = Math.Max(cfg.PoliceWitnessDistance, cfg.CivilianWitnessDistance);
            Ped[] nearby = World.GetNearbyPeds(player, radius);
            WitnessObservation bestPolice = null;
            WitnessObservation bestCivilian = null;

            foreach (Ped ped in nearby)
            {
                if (!IsUsableWitness(ped, player))
                    continue;

                float distance = Distance(ped.Position, player.Position);
                bool police = IsLawPed(ped);
                float max = police ? cfg.PoliceWitnessDistance : cfg.CivilianWitnessDistance;
                if (distance > max)
                    continue;

                if (!IsFacing(ped, player, police ? -0.20f : 0.15f))
                    continue;
                if (!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, ped.Handle, player.Handle, 17))
                    continue;

                var obs = new WitnessObservation { Witness = ped, IsPolice = police, Distance = distance };
                if (police)
                {
                    if (bestPolice == null || distance < bestPolice.Distance)
                        bestPolice = obs;
                }
                else if (bestCivilian == null || distance < bestCivilian.Distance)
                {
                    bestCivilian = obs;
                }
            }

            return bestPolice ?? bestCivilian;
        }

        public static WitnessObservation FindSeeingPolice(Ped player, float radius)
        {
            WitnessObservation best = null;
            foreach (Ped ped in World.GetNearbyPeds(player, radius))
            {
                if (!IsUsableWitness(ped, player) || !IsLawPed(ped))
                    continue;

                float distance = Distance(ped.Position, player.Position);
                if (!IsFacing(ped, player, -0.25f))
                    continue;
                if (!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, ped.Handle, player.Handle, 17))
                    continue;

                if (best == null || distance < best.Distance)
                    best = new WitnessObservation { Witness = ped, IsPolice = true, Distance = distance };
            }
            return best;
        }

        public static bool IsLawPed(Ped ped)
        {
            int type = (int)ped.PedType;
            return type == 6 || type == 27 || type == 29;
        }

        private static bool IsUsableWitness(Ped ped, Ped player)
        {
            return ped != null && ped.Exists() && ped.Handle != player.Handle && !ped.IsDead && ped.IsHuman;
        }

        private static bool IsFacing(Ped observer, Ped target, float minimumDot)
        {
            Vector3 from = observer.Position;
            Vector3 to = target.Position;
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double dz = to.Z - from.Z;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 0.001)
                return true;

            Vector3 f = observer.ForwardVector;
            double dot = (f.X * dx + f.Y * dy + f.Z * dz) / len;
            return dot >= minimumDot;
        }

        public static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.X - b.X;
            double y = a.Y - b.Y;
            double z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
