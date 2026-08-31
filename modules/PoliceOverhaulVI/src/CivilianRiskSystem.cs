using GTA;
using GTA.Math;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal static class CivilianRiskSystem
    {
        public static float EvaluateTargetArea(Ped suspect, Config cfg)
        {
            if (suspect == null || !suspect.Exists()) return 0f;
            float risk = 0f;
            Ped[] peds;
            try { peds = World.GetNearbyPeds(suspect, Math.Max(12f,cfg.CivilianRiskRadius)); }
            catch { peds = new Ped[0]; }
            foreach (Ped p in peds)
            {
                if (!IsCivilian(p,suspect)) continue;
                float d = Distance(p.Position,suspect.Position);
                if (d <= 5f) risk += 24f;
                else if (d <= 10f) risk += 15f;
                else risk += 6f;
            }
            Vehicle[] vehicles;
            try { vehicles = World.GetNearbyVehicles(suspect, Math.Max(12f,cfg.CivilianRiskRadius)); }
            catch { vehicles = new Vehicle[0]; }
            foreach (Vehicle v in vehicles)
            {
                if (v == null || !v.Exists() || (suspect.IsInVehicle() && suspect.CurrentVehicle != null && v.Handle == suspect.CurrentVehicle.Handle)) continue;
                int occupants = 0;
                try { occupants = Function.Call<int>(Hash.GET_VEHICLE_NUMBER_OF_PASSENGERS,v.Handle) + (v.Driver != null && v.Driver.Exists() ? 1 : 0); } catch { }
                if (occupants > 0) risk += Math.Min(18f,5f+occupants*5f);
            }
            return Clamp100(risk);
        }

        public static float EvaluateLineOfFire(Ped officer, Ped suspect, Config cfg)
        {
            if (officer == null || !officer.Exists() || suspect == null || !suspect.Exists()) return 100f;
            float risk = EvaluateTargetArea(suspect,cfg) * 0.55f;
            Ped[] peds;
            try { peds = World.GetNearbyPeds(officer, Math.Max(cfg.ForcePolicyRadius,40f)); }
            catch { return Clamp100(risk); }
            Vector3 a=officer.Position,b=suspect.Position;
            foreach(Ped p in peds)
            {
                if(!IsCivilian(p,suspect) || p.Handle==officer.Handle)continue;
                float t,dist=DistanceToSegment(p.Position,a,b,out t);
                if(t>0.05f&&t<1.20f)
                {
                    if(dist<1.4f)risk+=45f;
                    else if(dist<3.0f)risk+=24f;
                    else if(dist<5.0f)risk+=9f;
                }
            }
            return Clamp100(risk);
        }

        public static float EvaluatePitRisk(Ped suspect, Config cfg)
        {
            if (suspect == null || !suspect.Exists() || !suspect.IsInVehicle()) return 100f;
            Vehicle sv=suspect.CurrentVehicle; if(sv==null||!sv.Exists())return 100f;
            float risk=0f; Vector3 p=sv.Position,vel=Vector3.Zero;
            try{vel=Function.Call<Vector3>(Hash.GET_ENTITY_VELOCITY,sv.Handle);}catch{}
            Vector3 projected=p+vel*1.4f;
            Ped[] peds;try{peds=World.GetNearbyPeds(suspect,Math.Max(18f,cfg.PitRiskRadius));}catch{peds=new Ped[0];}
            foreach(Ped ped in peds)if(IsCivilian(ped,suspect)&&Math.Min(Distance(ped.Position,p),Distance(ped.Position,projected))<cfg.PitCivilianClearance)risk+=32f;
            Vehicle[] vehicles;try{vehicles=World.GetNearbyVehicles(suspect,Math.Max(18f,cfg.PitRiskRadius));}catch{vehicles=new Vehicle[0];}
            foreach(Vehicle v in vehicles)
            {
                if(v==null||!v.Exists()||v.Handle==sv.Handle)continue;
                if(Math.Min(Distance(v.Position,p),Distance(v.Position,projected))<cfg.PitVehicleClearance)risk+=18f;
            }
            return Clamp100(risk);
        }

        public static bool LethalAllowedForOfficer(Ped officer,Ped suspect,bool threatAllows,Config cfg,out float risk)
        {
            risk=EvaluateLineOfFire(officer,suspect,cfg);
            if(!threatAllows)return false;
            bool immediate=false;try{immediate=Function.Call<bool>(Hash.IS_PED_SHOOTING,suspect.Handle);}catch{}
            float threshold=immediate?cfg.CivilianRiskEmergencyThreshold:cfg.CivilianRiskLethalThreshold;
            return risk<=threshold;
        }

        public static bool MilitaryEngagementAllowed(Ped suspect,Config cfg,out float risk)
        {
            risk=EvaluateTargetArea(suspect,cfg);
            bool immediate=false;try{immediate=Function.Call<bool>(Hash.IS_PED_SHOOTING,suspect.Handle);}catch{}
            float threshold=immediate?cfg.MilitaryRiskEmergencyThreshold:cfg.MilitaryRiskThreshold;
            return risk<=threshold;
        }

        private static bool IsCivilian(Ped p,Ped suspect)
        {
            if(p==null||!p.Exists()||p.IsDead||!p.IsHuman||p.Handle==suspect.Handle)return false;
            try{int t=(int)p.PedType;if(t==6||t==27||t==29)return false;}catch{}
            return true;
        }
        private static float Distance(Vector3 a,Vector3 b){Vector3 d=a-b;return(float)Math.Sqrt(d.X*d.X+d.Y*d.Y+d.Z*d.Z);}
        private static float DistanceToSegment(Vector3 p,Vector3 a,Vector3 b,out float t)
        {
            Vector3 ab=b-a,ap=p-a;float den=ab.X*ab.X+ab.Y*ab.Y+ab.Z*ab.Z;if(den<0.0001f){t=0f;return Distance(p,a);}t=(ap.X*ab.X+ap.Y*ab.Y+ap.Z*ab.Z)/den;float c=Math.Max(0f,Math.Min(1.25f,t));Vector3 q=a+ab*c;return Distance(p,q);
        }
        private static float Clamp100(float v){return Math.Max(0f,Math.Min(100f,v));}
    }
}
