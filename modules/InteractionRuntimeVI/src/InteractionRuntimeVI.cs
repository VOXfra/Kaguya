using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;

namespace VOX.InteractionRuntimeVI
{
    public sealed class InteractionRuntimeVIScript : Script
    {
        private const int Context=51;
        private const int Left=174;
        private const int Up=172;
        private const int Right=175;
        private const string DataDir="scripts\\InteractionRuntimeVI";
        private const string LogPath=DataDir+"\\InteractionRuntimeVI.log";

        private sealed class Memory
        {
            public float Opinion;
            public float Fear;
            public int PositiveChain;
            public int NegativeChain;
            public int LastInteraction;
            public int LastSeen;
            public int Temper;
            public bool Robbed;
            public bool Warned;
        }

        private readonly Dictionary<int,Memory> _memory=new Dictionary<int,Memory>();
        private Ped _target;
        private int _candidate,_candidateSince,_lastScan,_focusStarted,_storyYieldUntil,_lastInteraction;
        private bool _focusDown,_leftDown,_upDown,_rightDown;

        public InteractionRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);Interval=25;Tick+=OnTick;Aborted+=OnAborted;
            Log("Interaction Runtime VI 0.4.0 loaded: RDR2-style contextual greet/defuse, antagonize/threaten and rob/respond state model.");
        }

        private void OnTick(object sender,EventArgs e)
        {
            try
            {
                Ped player=Game.LocalPlayerPed;
                if(player==null||!player.Exists()||player.IsDead){ResetFocus();return;}
                if(RockstarOwnsScene()){_storyYieldUntil=Game.GameTime+5000;ResetFocus();return;}
                if(Game.GameTime<_storyYieldUntil||player.IsInVehicle()){ResetFocus();return;}
                if(IsAiming(player)||TryingToEnter(player)){ResetFocus();return;}

                UpdateTarget(player);
                bool context=Pressed(Context);
                if(context&&!_focusDown&&_target!=null&&_target.Exists()){_focusDown=true;_focusStarted=Game.GameTime;}
                if(!context&&_focusDown){ResetFocusInput();return;}
                if(!_focusDown||_target==null||!_target.Exists())return;
                if(Game.GameTime-_focusStarted<230)return;

                DisableContext();
                Memory m=GetMemory(_target);
                DrawActions(player,_target,m);
                PollActions(player,_target,m);
                try{Function.Call(Hash.TASK_LOOK_AT_ENTITY,_target.Handle,player.Handle,650,0,2);}catch{}
            }
            catch(Exception ex){Log("Tick error: "+ex.Message);}
        }

        private void UpdateTarget(Ped player)
        {
            if(Game.GameTime-_lastScan<90)return;_lastScan=Game.GameTime;
            if(_target!=null&&_target.Exists()&&Usable(_target,player)&&WithinFocusCone(_target,player,0.50f,8.0f)){GetMemory(_target).LastSeen=Game.GameTime;return;}
            Ped best=FindTarget(player);
            if(best==null){_target=null;_candidate=0;_candidateSince=0;return;}
            if(_candidate!=best.Handle){_candidate=best.Handle;_candidateSince=Game.GameTime;return;}
            if(Game.GameTime-_candidateSince<180)return;
            _target=best;GetMemory(best).LastSeen=Game.GameTime;_candidate=0;_candidateSince=0;
        }

        private Ped FindTarget(Ped player)
        {
            Ped[] peds;try{peds=World.GetNearbyPeds(player,8.0f);}catch{return null;}Ped best=null;float scoreBest=float.MinValue;
            foreach(Ped p in peds)
            {
                if(!Usable(p,player)||!WithinFocusCone(p,player,0.64f,8.0f))continue;
                float d=Distance(player.Position,p.Position);float dot=CameraDot(p);float score=dot*100f-d*3f;if(score>scoreBest){scoreBest=score;best=p;}
            }
            return best;
        }

        private static bool Usable(Ped p,Ped player)
        {
            if(p==null||!p.Exists()||p.Handle==player.Handle||p.IsDead||!p.IsHuman)return false;
            try{if(p.IsInVehicle()||Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY,p.Handle))return false;}catch{return false;}
            try{int t=(int)p.PedType;if(t==6||t==27||t==29)return false;}catch{}
            return true;
        }

        private static bool WithinFocusCone(Ped p,Ped player,float minDot,float maxDistance)
        {
            float d=Distance(player.Position,p.Position);if(d>maxDistance)return false;
            try{if(!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,player.Handle,p.Handle,17))return false;}catch{return false;}
            return CameraDot(p)>=minDot;
        }

        private static float CameraDot(Ped p)
        {
            Vector3 d=p.Position-GameplayCamera.Position;float len=Length(d);if(len<0.01f)return 1f;Vector3 c=GameplayCamera.Direction;return(c.X*d.X+c.Y*d.Y+c.Z*d.Z)/len;
        }

        private void DrawActions(Ped player,Ped target,Memory m)
        {
            bool armed=Armed(player);bool tense=m.NegativeChain>0||m.Fear>35f||m.Opinion<-20f;
            string positive=tense?"Désamorcer":"Saluer";
            string contextual=armed&&!m.Robbed?"Braquer":(m.Robbed?"Répondre":"Interpeller");
            string negative=armed?"Menacer":"Provoquer";
            DrawText(0.785f,0.718f,"^  "+contextual,0.29f);
            DrawText(0.705f,0.772f,positive+"  <",0.29f);
            DrawText(0.853f,0.772f,">  "+negative,0.29f);
        }

        private void PollActions(Ped player,Ped target,Memory m)
        {
            bool l=Pressed(Left),u=Pressed(Up),r=Pressed(Right);
            if(Game.GameTime-_lastInteraction>650)
            {
                if(l&&!_leftDown){_lastInteraction=Game.GameTime;Positive(player,target,m);}
                else if(u&&!_upDown){_lastInteraction=Game.GameTime;Contextual(player,target,m);}
                else if(r&&!_rightDown){_lastInteraction=Game.GameTime;Negative(player,target,m);}
            }
            _leftDown=l;_upDown=u;_rightDown=r;
        }

        private void Positive(Ped player,Ped target,Memory m)
        {
            bool tense=m.NegativeChain>0||m.Fear>35f||m.Opinion<-20f;
            Face(player,target);
            if(tense)
            {
                Speak(player,"GENERIC_SORRY");
                if(m.Temper<65||m.Fear>50f){Speak(target,"GENERIC_YES");m.Fear=Math.Max(0,m.Fear-16f);m.Opinion=Math.Min(100,m.Opinion+8f);m.NegativeChain=Math.Max(0,m.NegativeChain-1);m.Warned=false;}
                else{Speak(target,"GENERIC_NO");m.Opinion-=3f;}
                Record(target,m,"defuse");return;
            }

            m.PositiveChain++;m.NegativeChain=0;
            if(m.PositiveChain==1){Speak(player,"GENERIC_HI");Speak(target,m.Temper<78?"GENERIC_HI":"GENERIC_YES");m.Opinion+=6f;}
            else if(m.PositiveChain==2){Speak(player,"GENERIC_HOWS_IT_GOING");Speak(target,m.Opinion>=0?"GENERIC_YES":"GENERIC_NO");m.Opinion+=m.Opinion>=0?5f:1f;}
            else{Speak(player,"GENERIC_THANKS");Speak(target,m.Opinion>10?"GENERIC_THANKS":"GENERIC_HI");m.Opinion+=2f;}
            m.Opinion=Clamp(m.Opinion,-100,100);Record(target,m,"greet"+m.PositiveChain);ReactBystander(target,player,false);
        }

        private void Contextual(Ped player,Ped target,Memory m)
        {
            Face(player,target);
            if(Armed(player)&&!m.Robbed)
            {
                m.Robbed=true;m.Fear=Math.Min(100,m.Fear+48);m.Opinion=Math.Max(-100,m.Opinion-42);m.NegativeChain=Math.Max(2,m.NegativeChain+1);
                Speak(player,"GENERIC_INSULT_HIGH");Speak(target,"GENERIC_FRIGHTENED_HIGH");
                try{Function.Call(Hash.TASK_HANDS_UP,target.Handle,4200,player.Handle,-1,false);}catch{}
                Record(target,m,"rob");ReactBystander(target,player,true);return;
            }

            if(m.Robbed)
            {
                Speak(player,"GENERIC_HI");
                if(m.Fear>60){Speak(target,"GENERIC_FRIGHTENED_HIGH");Flee(target,player);}else Speak(target,"GENERIC_NO");
                Record(target,m,"respond");return;
            }

            Speak(player,"GENERIC_HI");
            bool scenario=false;try{scenario=Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO,target.Handle);}catch{}
            if(scenario){Speak(target,m.Temper<70?"GENERIC_HI":"GENERIC_NO");m.Opinion+=2f;}
            else{Speak(target,m.Opinion>=0?"GENERIC_YES":"GENERIC_NO");}
            Record(target,m,"context");
        }

        private void Negative(Ped player,Ped target,Memory m)
        {
            bool armed=Armed(player);Face(player,target);m.PositiveChain=0;m.NegativeChain++;m.Opinion=Math.Max(-100,m.Opinion-(armed?28f:15f));m.Fear=Math.Min(100,m.Fear+(armed?35f:8f));
            Speak(player,"GENERIC_INSULT_HIGH");
            int courage=m.Temper+(int)(m.Opinion*0.12f)-(int)(m.Fear*0.18f);
            if(armed)
            {
                Speak(target,"GENERIC_FRIGHTENED_HIGH");
                if(courage<58){if(m.NegativeChain>=2)try{Function.Call(Hash.TASK_HANDS_UP,target.Handle,3500,player.Handle,-1,false);}catch{}else Flee(target,player);}else if(m.NegativeChain>=2)Flee(target,player);
                Record(target,m,"threaten");ReactBystander(target,player,true);return;
            }

            if(m.NegativeChain==1){Speak(target,courage>58?"GENERIC_INSULT_HIGH":"GENERIC_SHOCKED_HIGH");m.Warned=true;}
            else if(m.NegativeChain==2)
            {
                Speak(target,courage>50?"GENERIC_INSULT_HIGH":"GENERIC_NO");
                if(courage<42)Flee(target,player);else m.Warned=true;
            }
            else
            {
                if(courage>48&&Distance(target.Position,player.Position)<4.5f)try{Function.Call(Hash.TASK_COMBAT_PED,target.Handle,player.Handle,0,16);}catch{}
                else Flee(target,player);
            }
            Record(target,m,"antagonize"+m.NegativeChain);ReactBystander(target,player,true);
        }

        private void ReactBystander(Ped target,Ped player,bool hostile)
        {
            Ped[] peds;try{peds=World.GetNearbyPeds(target,5.0f);}catch{return;}
            foreach(Ped p in peds)
            {
                if(!Usable(p,player)||p.Handle==target.Handle)continue;
                try{Function.Call(Hash.TASK_LOOK_AT_ENTITY,p.Handle,player.Handle,1400,0,2);}catch{}
                if(hostile){Speak(p,"GENERIC_SHOCKED_HIGH");if(Armed(player))Flee(p,player);}else if(Game.GameTime%3==0)Speak(p,"GENERIC_HI");break;
            }
        }

        private Memory GetMemory(Ped p)
        {
            Memory m;if(_memory.TryGetValue(p.Handle,out m))return m;
            int seed=Math.Abs(unchecked(p.Handle*1103515245+p.Model.Hash));m=new Memory{Temper=25+seed%70,Opinion=(seed%21)-10,Fear=0,LastSeen=Game.GameTime};_memory[p.Handle]=m;return m;
        }

        private void Record(Ped target,Memory m,string action){m.LastInteraction=Game.GameTime;m.LastSeen=Game.GameTime;Log("RDR2-state interaction ped="+target.Handle+" action="+action+" opinion="+(int)m.Opinion+" fear="+(int)m.Fear+" posChain="+m.PositiveChain+" negChain="+m.NegativeChain+".");}
        private static void Face(Ped player,Ped target){try{Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY,player.Handle,target.Handle,300);}catch{}try{Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY,target.Handle,player.Handle,420);}catch{}}
        private static void Flee(Ped p,Ped player){try{Function.Call(Hash.TASK_SMART_FLEE_PED,p.Handle,player.Handle,45f,10000,false,false);}catch{}}
        private static void Speak(Ped p,string speech){if(p==null||!p.Exists())return;try{Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE,p.Handle,speech,"SPEECH_PARAMS_FORCE_NORMAL_CLEAR");}catch{}}
        private static bool Armed(Ped p){try{return Function.Call<bool>(Hash.IS_PED_ARMED,p.Handle,7);}catch{return false;}}
        private static bool IsAiming(Ped p){try{return Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING,Game.Player.Handle);}catch{return false;}}
        private static bool TryingToEnter(Ped p){try{return Function.Call<int>(Hash.GET_VEHICLE_PED_IS_TRYING_TO_ENTER,p.Handle)!=0;}catch{return false;}}
        private static bool Pressed(int c){try{return Function.Call<bool>(Hash.IS_CONTROL_PRESSED,0,c)||Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED,0,c);}catch{return false;}}
        private static void DisableContext(){try{Function.Call(Hash.DISABLE_CONTROL_ACTION,0,Context,true);}catch{}}

        private static void DrawText(float x,float y,string text,float scale)
        {
            try{Function.Call(Hash.SET_TEXT_FONT,0);Function.Call(Hash.SET_TEXT_SCALE,1f,scale);Function.Call(Hash.SET_TEXT_COLOUR,255,255,255,235);Function.Call(Hash.SET_TEXT_OUTLINE);Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,text);Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT,x,y,0);}catch{}
        }
        private static bool RockstarOwnsScene(){try{if(Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE))return true;}catch{}try{if(Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))return true;}catch{}try{if(Function.Call<bool>(Hash.GET_MISSION_FLAG))return true;}catch{}try{if(!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON,Game.Player.Handle))return true;}catch{}try{if(Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_IN))return true;}catch{}return false;}
        private static float Distance(Vector3 a,Vector3 b){return Length(a-b);}private static float Length(Vector3 v){return(float)Math.Sqrt(v.X*v.X+v.Y*v.Y+v.Z*v.Z);}private static float Clamp(float v,float a,float b){return v<a?a:v>b?b:v;}
        private void ResetFocusInput(){_focusDown=false;_focusStarted=0;_leftDown=_upDown=_rightDown=false;}
        private void ResetFocus(){ResetFocusInput();_target=null;_candidate=0;_candidateSince=0;}
        private void OnAborted(object sender,EventArgs e){ResetFocus();}
        private static void Log(string s){try{Directory.CreateDirectory(DataDir);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+s+Environment.NewLine);}catch{}}
    }
}
