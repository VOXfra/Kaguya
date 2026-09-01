using GTA;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class ForcePolicySystem
    {
        private bool _lastLethal, _lastPit, _initialized;
        private int _lastTransitionLog;
        public bool LethalAuthorized { get; private set; }
        public bool PitAuthorized { get; private set; }
        public float LastCivilianRisk { get; private set; }
        public float LastPitRisk { get; private set; }

        // RC4 repair: this class computes policy only. It deliberately never calls
        // TASK_*, SET_CURRENT_PED_WEAPON, SET_PED_COMBAT_ATTRIBUTES or driver-task
        // natives on Rockstar police. Those repeated writes were able to fight the
        // game's own dispatch state machine and stall both pursuit driving and combat.
        public void Update(Ped player,int wanted,CaseMemory memory,Config cfg,Action<string> log)
        {
            if(!cfg.ProportionalForceEnabled||player==null||!player.Exists()||wanted<=0)
            {
                LethalAuthorized=false;PitAuthorized=false;LastCivilianRisk=LastPitRisk=0f;LogTransitions(log);return;
            }

            bool shooting=SafeBool(Hash.IS_PED_SHOOTING,player.Handle);
            bool aimingAtLaw=IsAimingAtLawPed(player,cfg.ForcePolicyRadius);
            float speedKph=0f;
            if(player.IsInVehicle())try{speedKph=Function.Call<float>(Hash.GET_ENTITY_SPEED,player.CurrentVehicle.Handle)*3.6f;}catch{}

            bool threatAllowsLethal;
            if(!cfg.LethalForceRequiresCurrentThreat)threatAllowsLethal=wanted>=cfg.LethalForceMinimumWanted||shooting;
            else threatAllowsLethal=shooting||aimingAtLaw||wanted>=Math.Max(cfg.LethalArmedEscalationWanted,4);
            if(wanted<=2&&!shooting&&!aimingAtLaw)threatAllowsLethal=false;

            LastCivilianRisk=cfg.CivilianRiskEnabled?CivilianRiskSystem.EvaluateTargetArea(player,cfg):0f;
            float lethalThreshold=shooting?cfg.CivilianRiskEmergencyThreshold:cfg.CivilianRiskLethalThreshold;
            LethalAuthorized=threatAllowsLethal&&(!cfg.CivilianRiskEnabled||LastCivilianRisk<=lethalThreshold);

            bool pitBase=wanted>=cfg.PitMinimumWanted&&player.IsInVehicle()&&speedKph>=cfg.PitMinimumSpeedKph;
            if(cfg.PitRequiresFleeing&&wanted<=1)pitBase=false;
            LastPitRisk=pitBase&&cfg.CivilianRiskEnabled?CivilianRiskSystem.EvaluatePitRisk(player,cfg):0f;
            float pitThreshold=shooting?cfg.PitEmergencyRiskThreshold:cfg.PitRiskThreshold;
            PitAuthorized=pitBase&&(!cfg.CivilianRiskEnabled||LastPitRisk<=pitThreshold);

            LogTransitions(log);
        }

        public void Reset(){LethalAuthorized=false;PitAuthorized=false;LastCivilianRisk=LastPitRisk=0f;_initialized=false;}

        private void LogTransitions(Action<string> log)
        {
            bool changed=!_initialized||LethalAuthorized!=_lastLethal||PitAuthorized!=_lastPit;
            _lastLethal=LethalAuthorized;_lastPit=PitAuthorized;_initialized=true;
            if(!changed||log==null||Game.GameTime-_lastTransitionLog<1400)return;
            _lastTransitionLog=Game.GameTime;
            log("Force policy (advisory only): lethal="+LethalAuthorized+", pit="+PitAuthorized+", civilianRisk="+(int)LastCivilianRisk+", pitRisk="+(int)LastPitRisk+". Rockstar retains officer driving/combat tasks.");
        }

        private static bool IsAimingAtLawPed(Ped player,float radius)
        {
            try
            {
                if(!Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING,Game.Player.Handle))return false;
                var output=new OutputArgument();if(!Function.Call<bool>(Hash.GET_ENTITY_PLAYER_IS_FREE_AIMING_AT,Game.Player.Handle,output))return false;
                int h=output.GetResult<int>();if(h==0||!Function.Call<bool>(Hash.DOES_ENTITY_EXIST,h))return false;
                Ped p=Entity.FromHandle(h) as Ped;return p!=null&&p.Exists()&&Perception.IsLawPed(p)&&Perception.Distance(player.Position,p.Position)<=radius;
            }
            catch{return false;}
        }
        private static bool SafeBool(Hash h,params InputArgument[] args){try{return Function.Call<bool>(h,args);}catch{return false;}}
    }
}
