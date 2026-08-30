using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.PedOverhaulVI
{
    internal sealed class Config
    {
        public bool Enabled=true, DebugLogging=true, DisableDuringRockstarMissions=true, SkipMissionEntities=true;
        public int TickIntervalMs=50, RefreshNearbyPedsMs=500, MaxProcessedPeds=28;
        public float ProcessRadius=95f;
        public float VisualFovDegrees=105f, GunshotHearingRadius=85f, ThreatVisualRadius=65f, AimThreatRadius=38f, BodyAwarenessRadius=26f;
        public int ReactionCooldownMs=1800, CalmAfterMs=22000;
        public int MinBravery=8,MaxBravery=95,MinCuriosity=5,MaxCuriosity=95,MinAggression=5,MaxAggression=90;
        public int FleeBaseChance=70,CowerBaseChance=18,FilmCuriosityThreshold=72;
        public bool FilmFromDistance=true,PanicPropagation=true,SeekCoverBeforeFlee=true,MoraleEnabled=true,PoliceOverhaulOwnsLawPeds=true;
        public float FilmMinDistance=28f,FilmMaxDistance=62f,PanicPropagationRadius=18f;
        public int MoraleBreakThreshold=28,NearbyDeathMoraleLoss=24,OutnumberedMoraleLoss=12,SurrenderBaseChance=55,SurrenderDurationMs=12000,LowHealthPercent=32;
        public float HealthLossMoraleMultiplier=0.75f,NearbyDeathRadius=14f;

        public static Config Load(string path)
        {
            var c=new Config(); if(!File.Exists(path))return c; var v=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); string s="";
            foreach(string raw in File.ReadAllLines(path)){string l=raw.Trim();if(l.Length==0||l.StartsWith(";")||l.StartsWith("#"))continue;if(l.StartsWith("[")&&l.EndsWith("]")){s=l.Substring(1,l.Length-2).Trim();continue;}int e=l.IndexOf('=');if(e>0)v[s+"."+l.Substring(0,e).Trim()]=l.Substring(e+1).Trim();}
            c.Enabled=B(v,"General.Enabled",c.Enabled);c.DebugLogging=B(v,"General.DebugLogging",c.DebugLogging);c.DisableDuringRockstarMissions=B(v,"General.DisableDuringRockstarMissions",c.DisableDuringRockstarMissions);c.TickIntervalMs=I(v,"General.TickIntervalMs",c.TickIntervalMs);c.RefreshNearbyPedsMs=I(v,"General.RefreshNearbyPedsMs",c.RefreshNearbyPedsMs);c.ProcessRadius=F(v,"General.ProcessRadius",c.ProcessRadius);c.MaxProcessedPeds=I(v,"General.MaxProcessedPeds",c.MaxProcessedPeds);c.SkipMissionEntities=B(v,"General.SkipMissionEntities",c.SkipMissionEntities);
            c.VisualFovDegrees=F(v,"Perception.VisualFovDegrees",c.VisualFovDegrees);c.GunshotHearingRadius=F(v,"Perception.GunshotHearingRadius",c.GunshotHearingRadius);c.ThreatVisualRadius=F(v,"Perception.ThreatVisualRadius",c.ThreatVisualRadius);c.AimThreatRadius=F(v,"Perception.AimThreatRadius",c.AimThreatRadius);c.BodyAwarenessRadius=F(v,"Perception.BodyAwarenessRadius",c.BodyAwarenessRadius);c.ReactionCooldownMs=I(v,"Perception.ReactionCooldownMs",c.ReactionCooldownMs);c.CalmAfterMs=I(v,"Perception.CalmAfterMs",c.CalmAfterMs);
            c.MinBravery=I(v,"Personality.MinBravery",c.MinBravery);c.MaxBravery=I(v,"Personality.MaxBravery",c.MaxBravery);c.MinCuriosity=I(v,"Personality.MinCuriosity",c.MinCuriosity);c.MaxCuriosity=I(v,"Personality.MaxCuriosity",c.MaxCuriosity);c.MinAggression=I(v,"Personality.MinAggression",c.MinAggression);c.MaxAggression=I(v,"Personality.MaxAggression",c.MaxAggression);
            c.FleeBaseChance=I(v,"Civilians.FleeBaseChance",c.FleeBaseChance);c.CowerBaseChance=I(v,"Civilians.CowerBaseChance",c.CowerBaseChance);c.FilmFromDistance=B(v,"Civilians.FilmFromDistance",c.FilmFromDistance);c.FilmMinDistance=F(v,"Civilians.FilmMinDistance",c.FilmMinDistance);c.FilmMaxDistance=F(v,"Civilians.FilmMaxDistance",c.FilmMaxDistance);c.FilmCuriosityThreshold=I(v,"Civilians.FilmCuriosityThreshold",c.FilmCuriosityThreshold);c.PanicPropagation=B(v,"Civilians.PanicPropagation",c.PanicPropagation);c.PanicPropagationRadius=F(v,"Civilians.PanicPropagationRadius",c.PanicPropagationRadius);
            c.MoraleEnabled=B(v,"Combat.MoraleEnabled",c.MoraleEnabled);c.MoraleBreakThreshold=I(v,"Combat.MoraleBreakThreshold",c.MoraleBreakThreshold);c.HealthLossMoraleMultiplier=F(v,"Combat.HealthLossMoraleMultiplier",c.HealthLossMoraleMultiplier);c.NearbyDeathMoraleLoss=I(v,"Combat.NearbyDeathMoraleLoss",c.NearbyDeathMoraleLoss);c.NearbyDeathRadius=F(v,"Combat.NearbyDeathRadius",c.NearbyDeathRadius);c.OutnumberedMoraleLoss=I(v,"Combat.OutnumberedMoraleLoss",c.OutnumberedMoraleLoss);c.SurrenderBaseChance=I(v,"Combat.SurrenderBaseChance",c.SurrenderBaseChance);c.SurrenderDurationMs=I(v,"Combat.SurrenderDurationMs",c.SurrenderDurationMs);c.LowHealthPercent=I(v,"Combat.LowHealthPercent",c.LowHealthPercent);c.SeekCoverBeforeFlee=B(v,"Combat.SeekCoverBeforeFlee",c.SeekCoverBeforeFlee);c.PoliceOverhaulOwnsLawPeds=B(v,"Compatibility.PoliceOverhaulOwnsLawPeds",c.PoliceOverhaulOwnsLawPeds);return c;
        }
        private static bool B(Dictionary<string,string>v,string k,bool d){string s;bool x;return v.TryGetValue(k,out s)&&bool.TryParse(s,out x)?x:d;}private static int I(Dictionary<string,string>v,string k,int d){string s;int x;return v.TryGetValue(k,out s)&&int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out x)?x:d;}private static float F(Dictionary<string,string>v,string k,float d){string s;float x;return v.TryGetValue(k,out s)&&float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out x)?x:d;}
    }
}
