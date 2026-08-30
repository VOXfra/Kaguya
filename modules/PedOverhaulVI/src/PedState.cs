using GTA;
using System;

namespace VOX.PedOverhaulVI
{
    internal enum ReactionMode { None, Watch, Film, Cower, Flee, Cover, Surrender }

    internal sealed class PedState
    {
        public int Handle;
        public int ModelHash;
        public int Bravery;
        public int Curiosity;
        public int Aggression;
        public float Morale=100f;
        public int LastHealth;
        public int LastReactionAt;
        public int LastThreatAt;
        public int LastSeenAt;
        public ReactionMode Mode;
        public bool NearbyDeathCounted;

        public static PedState Create(Ped ped, Config cfg)
        {
            int seed = unchecked(ped.Handle * 397 ^ ped.Model.Hash * 17 ^ 0x5F3759DF);
            var r = new Random(seed);
            return new PedState
            {
                Handle = ped.Handle,
                ModelHash = ped.Model.Hash,
                Bravery = Range(r,cfg.MinBravery,cfg.MaxBravery),
                Curiosity = Range(r,cfg.MinCuriosity,cfg.MaxCuriosity),
                Aggression = Range(r,cfg.MinAggression,cfg.MaxAggression),
                LastHealth = SafeHealth(ped)
            };
        }

        public int Roll(int salt)
        {
            unchecked
            {
                int x = Handle * 1103515245 + ModelHash * 12345 + salt * 265443576;
                x ^= (x >> 16); if (x < 0) x = -x; return x % 100;
            }
        }

        private static int Range(Random r,int min,int max){if(max<min){int t=min;min=max;max=t;}return r.Next(Math.Max(0,min),Math.Max(1,max)+1);}
        public static int SafeHealth(Ped p){try{return p.Health;}catch{return 100;}}
    }
}
