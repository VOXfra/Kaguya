using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VOX.PedOverhaulVI
{
    public sealed class PedOverhaulVIScript : Script
    {
        private const string ConfigPath="scripts\\PedOverhaulVI.ini";
        private const string DataDirectory="scripts\\PedOverhaulVI";
        private const string LogPath=DataDirectory+"\\PedOverhaulVI.log";
        private Config _cfg;
        private readonly Dictionary<int,PedState> _states=new Dictionary<int,PedState>();
        private readonly List<Ped> _nearby=new List<Ped>();
        private int _lastRefresh;
        private int _cursor;
        private bool _policeModuleLoaded;
        private int _lastModuleProbe;
        private int _missionFlagSince;

        public PedOverhaulVIScript()
        {
            Directory.CreateDirectory(DataDirectory);_cfg=Config.Load(ConfigPath);Interval=Math.Max(10,_cfg.TickIntervalMs);Tick+=OnTick;Aborted+=OnAborted;ProbeModules();Log("Ped Overhaul VI 0.1.0 loaded.");
        }

        private void OnTick(object sender,EventArgs e)
        {
            if(!_cfg.Enabled)return;
            try
            {
                Ped player=Game.LocalPlayerPed;if(player==null||!player.Exists()||player.IsDead)return;
                if(Game.GameTime-_lastModuleProbe>3000)ProbeModules();
                if(_cfg.DisableDuringRockstarMissions&&ShouldYieldToMission())return;
                RefreshNearby(player);
                if(_nearby.Count==0)return;
                int budget=Math.Max(1,Math.Min(8,_cfg.MaxProcessedPeds/4));
                for(int n=0;n<budget&&_nearby.Count>0;n++)
                {
                    if(_cursor>=_nearby.Count)_cursor=0;Ped ped=_nearby[_cursor++];ProcessPed(player,ped);
                }
                CleanupStates();
            }
            catch(Exception ex){Log("Tick error: "+ex);}
        }

        private bool ShouldYieldToMission()
        {
            bool cut=false,switching=false,flag=false;try{cut=Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE);}catch{}try{switching=Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS);}catch{}try{flag=Function.Call<bool>(Hash.GET_MISSION_FLAG);}catch{}
            if(cut||switching)return true;
            int wanted=0;try{wanted=Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL,Game.Player.Handle);}catch{}
            bool shooting=false;try{shooting=Function.Call<bool>(Hash.IS_PED_SHOOTING,Game.LocalPlayerPed.Handle);}catch{}
            if(!flag){_missionFlagSince=0;return false;}
            if(wanted>0||shooting)return false;
            if(_missionFlagSince==0)_missionFlagSince=Game.GameTime;
            return Game.GameTime-_missionFlagSince>1500;
        }

        private void RefreshNearby(Ped player)
        {
            if(Game.GameTime-_lastRefresh<Math.Max(150,_cfg.RefreshNearbyPedsMs))return;_lastRefresh=Game.GameTime;_nearby.Clear();Ped[] peds;try{peds=World.GetNearbyPeds(player,_cfg.ProcessRadius);}catch{return;}
            foreach(Ped p in peds)
            {
                if(_nearby.Count>=Math.Max(6,_cfg.MaxProcessedPeds+8))break;
                if(p==null||!p.Exists()||p.Handle==player.Handle||!p.IsHuman)continue;
                _nearby.Add(p);
                if(!p.IsDead&&UsablePed(p,player)&&!_states.ContainsKey(p.Handle))_states[p.Handle]=PedState.Create(p,_cfg);
            }
        }

        private bool UsablePed(Ped p,Ped player)
        {
            if(p==null||!p.Exists()||p.Handle==player.Handle||p.IsDead||!p.IsHuman)return false;
            if(_cfg.SkipMissionEntities){try{if(Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY,p.Handle))return false;}catch{}}
            if(_cfg.PoliceOverhaulOwnsLawPeds&&_policeModuleLoaded&&IsLawPed(p))return false;
            return true;
        }

        private void ProcessPed(Ped player,Ped ped)
        {
            if(!UsablePed(ped,player))return;PedState s; if(!_states.TryGetValue(ped.Handle,out s)){s=PedState.Create(ped,_cfg);_states[ped.Handle]=s;}
            float distance=Distance(ped.Position,player.Position);bool sees=distance<=_cfg.ThreatVisualRadius&&Facing(ped,player,_cfg.VisualFovDegrees)&&ClearLos(ped,player);bool hearsGun=false,playerShooting=false;
            try{playerShooting=Function.Call<bool>(Hash.IS_PED_SHOOTING,player.Handle);}catch{}hearsGun=playerShooting&&distance<=_cfg.GunshotHearingRadius;
            bool aimedAt=distance<=_cfg.AimThreatRadius&&PlayerAimingAt(ped);bool hostile=IsHostileToPlayer(ped,player);int health=PedState.SafeHealth(ped);int maxHealth=100;try{maxHealth=Function.Call<int>(Hash.GET_ENTITY_MAX_HEALTH,ped.Handle);}catch{}

            if(_cfg.MoraleEnabled&&hostile)
            {
                int lost=Math.Max(0,s.LastHealth-health);if(lost>0)s.Morale-=lost*_cfg.HealthLossMoraleMultiplier;
                if(HasNearbyDeadAlly(ped,player)){if(!s.NearbyDeathCounted){s.Morale-=_cfg.NearbyDeathMoraleLoss;s.NearbyDeathCounted=true;}}
                else s.NearbyDeathCounted=false;
                if(health<=Math.Max(1,maxHealth)*_cfg.LowHealthPercent/100)s.Morale-=4f;
                if(CountNearbyHostiles(player,ped)<2)s.Morale-=_cfg.OutnumberedMoraleLoss*0.08f;
                s.Morale=Math.Max(0f,Math.Min(100f,s.Morale));
                if(s.Morale<=_cfg.MoraleBreakThreshold&&Game.GameTime-s.LastReactionAt>_cfg.ReactionCooldownMs){BreakMorale(player,ped,s);s.LastHealth=health;return;}
            }

            bool danger=(sees&&playerShooting)||hearsGun||aimedAt;
            if(danger)
            {
                s.LastThreatAt=Game.GameTime;s.LastSeenAt=sees?Game.GameTime:s.LastSeenAt;
                if(Game.GameTime-s.LastReactionAt>=_cfg.ReactionCooldownMs)
                {
                    if(hostile)ReactHostile(player,ped,s,distance);
                    else ReactCivilian(player,ped,s,distance,aimedAt);
                }
            }
            else if(_cfg.PanicPropagation&&NearbyPanic(ped))
            {
                s.LastThreatAt=Game.GameTime;if(Game.GameTime-s.LastReactionAt>=_cfg.ReactionCooldownMs)ReactCivilian(player,ped,s,distance,false);
            }
            else if(s.Mode!=ReactionMode.None&&Game.GameTime-s.LastThreatAt>=_cfg.CalmAfterMs)
            {
                ResumeAmbient(ped,s);
            }
            s.LastHealth=health;
        }

        private void ReactCivilian(Ped player,Ped ped,PedState s,float distance,bool aimedAt)
        {
            int fleeScore=_cfg.FleeBaseChance+(50-s.Bravery)/2+(aimedAt?20:0);int cowerScore=_cfg.CowerBaseChance+(35-s.Bravery)/3;
            if(_cfg.FilmFromDistance&&!aimedAt&&s.Curiosity>=_cfg.FilmCuriosityThreshold&&s.Bravery>=45&&distance>=_cfg.FilmMinDistance&&distance<=_cfg.FilmMaxDistance&&s.Roll(Game.GameTime/3000)<s.Curiosity)
            {
                try{Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE,ped.Handle,"WORLD_HUMAN_MOBILE_FILM_SHOCKING",0,true);}catch{}s.Mode=ReactionMode.Film;
            }
            else if(s.Roll(Game.GameTime/1700+7)<Math.Max(5,Math.Min(95,cowerScore)))
            {
                try{Function.Call(Hash.TASK_COWER,ped.Handle,-1);}catch{}s.Mode=ReactionMode.Cower;
            }
            else
            {
                if(_cfg.SeekCoverBeforeFlee&&s.Bravery>=55&&distance>18f){try{Function.Call(Hash.TASK_SEEK_COVER_FROM_PED,ped.Handle,player.Handle,4500,false);}catch{}s.Mode=ReactionMode.Cover;}
                else{try{Function.Call(Hash.TASK_SMART_FLEE_PED,ped.Handle,player.Handle,110f,-1,false,false);}catch{}s.Mode=ReactionMode.Flee;}
            }
            s.LastReactionAt=Game.GameTime;
        }

        private void ReactHostile(Ped player,Ped ped,PedState s,float distance)
        {
            if(s.Morale<=_cfg.MoraleBreakThreshold){BreakMorale(player,ped,s);return;}
            if(s.Bravery<30&&distance<20f){try{Function.Call(Hash.TASK_SMART_FLEE_PED,ped.Handle,player.Handle,90f,10000,false,false);}catch{}s.Mode=ReactionMode.Flee;}
            else if(_cfg.SeekCoverBeforeFlee&&s.Bravery<60){try{Function.Call(Hash.TASK_SEEK_COVER_FROM_PED,ped.Handle,player.Handle,5000,false);}catch{}s.Mode=ReactionMode.Cover;}
            s.LastReactionAt=Game.GameTime;
        }

        private void BreakMorale(Ped player,Ped ped,PedState s)
        {
            int surrenderChance=_cfg.SurrenderBaseChance+(45-s.Aggression)/2+(40-s.Bravery)/3;bool surrender=s.Roll((Game.GameTime/1000)+31)<Math.Max(10,Math.Min(90,surrenderChance));
            try
            {
                if(surrender){Function.Call(Hash.TASK_HANDS_UP,ped.Handle,_cfg.SurrenderDurationMs,player.Handle,-1,true);s.Mode=ReactionMode.Surrender;}
                else{Function.Call(Hash.TASK_SMART_FLEE_PED,ped.Handle,player.Handle,140f,15000,false,false);s.Mode=ReactionMode.Flee;}
            }
            catch{}
            s.LastReactionAt=Game.GameTime;s.LastThreatAt=Game.GameTime;
        }

        private void ResumeAmbient(Ped ped,PedState s)
        {
            try{Function.Call(Hash.CLEAR_PED_TASKS,ped.Handle);Function.Call(Hash.TASK_WANDER_STANDARD,ped.Handle,10f,10);}catch{}s.Mode=ReactionMode.None;s.Morale=Math.Min(100f,s.Morale+20f);s.LastReactionAt=Game.GameTime;
        }

        private bool NearbyPanic(Ped ped)
        {
            foreach(Ped p in _nearby){if(p==null||!p.Exists()||p.Handle==ped.Handle)continue;PedState s;if(!_states.TryGetValue(p.Handle,out s))continue;if((s.Mode==ReactionMode.Flee||s.Mode==ReactionMode.Cower)&&Distance(p.Position,ped.Position)<=_cfg.PanicPropagationRadius)return true;}return false;
        }

        private bool HasNearbyDeadAlly(Ped ped,Ped player)
        {
            foreach(Ped p in _nearby){if(p==null||!p.Exists()||!p.IsDead)continue;if(Distance(p.Position,ped.Position)>_cfg.NearbyDeathRadius)continue;if(SameDisposition(p,ped,player))return true;}return false;
        }

        private int CountNearbyHostiles(Ped player,Ped around)
        {
            int c=0;foreach(Ped p in _nearby){if(p==null||!p.Exists()||p.IsDead)continue;if(Distance(p.Position,around.Position)>24f)continue;if(IsHostileToPlayer(p,player))c++;}return c;
        }

        private static bool SameDisposition(Ped a,Ped b,Ped player){return IsHostileToPlayer(a,player)==IsHostileToPlayer(b,player);}
        private static bool IsHostileToPlayer(Ped p,Ped player){try{if(Function.Call<bool>(Hash.IS_PED_IN_COMBAT,p.Handle,player.Handle))return true;int rel=Function.Call<int>(Hash.GET_RELATIONSHIP_BETWEEN_PEDS,p.Handle,player.Handle);return rel>=4&&rel<=5;}catch{return false;}}
        private static bool IsLawPed(Ped p){try{int t=(int)p.PedType;return t==6||t==27||t==29;}catch{return false;}}
        private static bool ClearLos(Ped a,Ped b){try{return Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,a.Handle,b.Handle,17);}catch{return false;}}
        private static bool Facing(Ped a,Ped b,float fov){Vector3 f=a.ForwardVector,from=a.Position,to=b.Position;double dx=to.X-from.X,dy=to.Y-from.Y,dz=to.Z-from.Z,len=Math.Sqrt(dx*dx+dy*dy+dz*dz);if(len<0.01)return true;double dot=(f.X*dx+f.Y*dy+f.Z*dz)/len;return dot>=Math.Cos(Math.Max(20f,Math.Min(170f,fov))*0.5*Math.PI/180.0);}
        private static float Distance(Vector3 a,Vector3 b){double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return(float)Math.Sqrt(x*x+y*y+z*z);}

        private static bool PlayerAimingAt(Ped target)
        {
            try
            {
                if(!Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING,Game.Player.Handle))return false;
                var arg=new OutputArgument();if(!Function.Call<bool>(Hash.GET_ENTITY_PLAYER_IS_FREE_AIMING_AT,Game.Player.Handle,arg))return false;return arg.GetResult<int>()==target.Handle;
            }
            catch{return false;}
        }

        private void ProbeModules(){_lastModuleProbe=Game.GameTime;try{_policeModuleLoaded=AppDomain.CurrentDomain.GetAssemblies().Any(a=>string.Equals(a.GetName().Name,"PoliceOverhaulVI",StringComparison.OrdinalIgnoreCase));}catch{_policeModuleLoaded=false;}}
        private void CleanupStates(){var live=new HashSet<int>(_nearby.Where(p=>p!=null&&p.Exists()).Select(p=>p.Handle));var remove=_states.Keys.Where(h=>!live.Contains(h)).Take(12).ToList();foreach(int h in remove)_states.Remove(h);}
        private void OnAborted(object sender,EventArgs e){_states.Clear();_nearby.Clear();}
        private void Log(string m){if(_cfg!=null&&!_cfg.DebugLogging)return;try{Directory.CreateDirectory(DataDirectory);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+m+Environment.NewLine);}catch{}}
    }
}
