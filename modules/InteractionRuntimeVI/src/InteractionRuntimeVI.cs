using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace VOX.InteractionRuntimeVI
{
    public sealed class InteractionRuntimeVIScript : Script
    {
        private const string ConfigPath="scripts\\InteractionRuntimeVI.ini";
        private const string DataDirectory="scripts\\InteractionRuntimeVI";
        private const string LogPath=DataDirectory+"\\InteractionRuntimeVI.log";
        private readonly Dictionary<int,Memory> _memory=new Dictionary<int,Memory>();
        private Config _cfg;
        private bool _focusDown;
        private int _focusStarted,_lastTargetScan,_lastLookTask,_lastInteraction;
        private Ped _target;
        private MethodInfo _pedBridgeRegister,_pedBridgeFear,_pedBridgeOpinion;
        private int _lastBridgeProbe;

        public InteractionRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDirectory);
            _cfg=Config.Load(ConfigPath);
            Interval=Math.Max(10,_cfg.TickIntervalMs);
            Tick+=OnTick;KeyDown+=OnKeyDown;KeyUp+=OnKeyUp;Aborted+=OnAborted;
            ProbePedBridge();
            Log("Interaction Runtime VI 0.1.0 hold-focus prototype loaded.");
        }

        private void OnKeyDown(object sender,KeyEventArgs e)
        {
            if(e.KeyCode==_cfg.FocusKey)
            {
                if(!_focusDown){_focusDown=true;_focusStarted=Game.GameTime;_target=null;}
                return;
            }
            if(!_focusDown||_target==null||!_target.Exists()||Game.GameTime-_focusStarted<_cfg.FocusHoldMs)return;
            if(Game.GameTime-_lastInteraction<_cfg.InteractionCooldownMs)return;
            if(e.KeyCode==_cfg.PositiveKey)Perform(0);
            else if(e.KeyCode==_cfg.ContextKey)Perform(1);
            else if(e.KeyCode==_cfg.NegativeKey)Perform(2);
        }

        private void OnKeyUp(object sender,KeyEventArgs e)
        {
            if(e.KeyCode!=_cfg.FocusKey)return;
            _focusDown=false;_focusStarted=0;_target=null;
        }

        private void OnTick(object sender,EventArgs e)
        {
            if(!_cfg.Enabled)return;
            try
            {
                Ped player=Game.LocalPlayerPed;
                if(player==null||!player.Exists()||player.IsDead){ResetFocus();return;}
                if(Game.GameTime-_lastBridgeProbe>4000)ProbePedBridge();
                if(ShouldYield()){ResetFocus();return;}
                CleanupMemory();
                if(!_focusDown)return;
                if(Game.GameTime-_lastTargetScan>=120)
                {
                    _lastTargetScan=Game.GameTime;
                    Ped candidate=FindTarget(player);
                    if(candidate==null||_target==null||!_target.Exists()||candidate.Handle!=_target.Handle)
                    {
                        _target=candidate;_focusStarted=Game.GameTime;_lastLookTask=0;
                    }
                }
                if(_target==null||!_target.Exists())return;
                int held=Game.GameTime-_focusStarted;
                if(held>=_cfg.LookAtAfterMs&&Game.GameTime-_lastLookTask>1200)
                {
                    _lastLookTask=Game.GameTime;
                    try{Function.Call(Hash.TASK_LOOK_AT_ENTITY,_target.Handle,player.Handle,1700,0,2);}catch{}
                }
                if(held>=_cfg.FocusHoldMs&&_cfg.ShowControls)DrawInteractionHud(player,_target);
            }
            catch(Exception ex){Log("Tick error: "+ex);}
        }

        private Ped FindTarget(Ped player)
        {
            Ped[] peds;try{peds=World.GetNearbyPeds(player,_cfg.MaxDistance);}catch{return null;}
            Vector3 camPos=GameplayCamera.Position,camDir=GameplayCamera.Direction;
            Ped best=null;float bestScore=float.MinValue;
            foreach(Ped p in peds)
            {
                if(!UsableTarget(p,player))continue;
                Vector3 delta=p.Position-camPos;float len=(float)Math.Sqrt(delta.X*delta.X+delta.Y*delta.Y+delta.Z*delta.Z);if(len<0.1f)continue;
                float dot=(camDir.X*delta.X+camDir.Y*delta.Y+camDir.Z*delta.Z)/len;
                if(dot<_cfg.AcquireConeDot)continue;
                bool los=false;try{los=Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,player.Handle,p.Handle,17);}catch{}
                if(!los)continue;
                float score=dot*100f-len*1.8f;
                if(score>bestScore){bestScore=score;best=p;}
            }
            return best;
        }

        private bool UsableTarget(Ped p,Ped player)
        {
            if(p==null||!p.Exists()||p.Handle==player.Handle||p.IsDead||!p.IsHuman)return false;
            try{if(p.IsInVehicle())return false;}catch{}
            if(_cfg.SkipMissionPeds){try{if(Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY,p.Handle))return false;}catch{}}
            try{int t=(int)p.PedType;if(t==6||t==27||t==29)return false;}catch{}
            return true;
        }

        private void DrawInteractionHud(Ped player,Ped target)
        {
            float fear=GetFear(target),opinion=GetOpinion(target);bool armed=false;
            try{armed=Function.Call<bool>(Hash.IS_PED_ARMED,player.Handle,7);}catch{}
            string a=armed?"[1] Calmer":"[1] Saluer";
            string b=fear>=45f?"[2] Laisser partir":"[2] Interpeller";
            string c=armed?"[3] Menacer":"[3] Provoquer";
            DrawText(0.79f,0.72f,"FOCUS",0.34f);
            DrawText(0.79f,0.755f,a,0.30f);DrawText(0.79f,0.785f,b,0.30f);DrawText(0.79f,0.815f,c,0.30f);
            if(Math.Abs(opinion)>15f||fear>20f)DrawText(0.79f,0.852f,"Memoire sociale active",0.24f);
        }

        private void Perform(int slot)
        {
            Ped player=Game.LocalPlayerPed,target=_target;if(player==null||target==null||!player.Exists()||!target.Exists())return;
            bool armed=false;try{armed=Function.Call<bool>(Hash.IS_PED_ARMED,player.Handle,7);}catch{}
            string intent;
            if(slot==0)intent=armed?"calm":"greet";
            else if(slot==1)intent="context";
            else intent=armed?"threaten":"antagonize";
            _lastInteraction=Game.GameTime;

            Memory m=GetOrCreate(target);m.LastAt=Game.GameTime;m.Recognition=Math.Min(100f,m.Recognition+10f);
            if(intent=="greet"){m.Opinion=Math.Min(100f,m.Opinion+10f);m.Fear=Math.Max(0f,m.Fear-3f);Speak(player,"GENERIC_HI");RespondFriendly(target,m);}
            else if(intent=="calm"){m.Opinion=Math.Min(100f,m.Opinion+4f);m.Fear=Math.Max(0f,m.Fear-14f);Speak(player,"GENERIC_HI");RespondCalm(target,m);}
            else if(intent=="context"){Speak(player,"GENERIC_HI");RespondContext(target,m);}
            else if(intent=="antagonize"){m.Opinion=Math.Max(-100f,m.Opinion-20f);m.Recognition=Math.Min(100f,m.Recognition+15f);Speak(player,"GENERIC_INSULT_HIGH");RespondAntagonize(target,m,false);}
            else if(intent=="threaten"){m.Opinion=Math.Max(-100f,m.Opinion-35f);m.Fear=Math.Min(100f,m.Fear+38f);m.Recognition=Math.Min(100f,m.Recognition+30f);Speak(player,"GENERIC_INSULT_HIGH");RespondAntagonize(target,m,true);}

            SendToPedBridge(target,intent,1f);
            Log("Interaction ped="+target.Handle+" intent="+intent+" opinion="+(int)m.Opinion+" fear="+(int)m.Fear+" recognition="+(int)m.Recognition+".");
        }

        private void RespondFriendly(Ped target,Memory m)
        {
            int roll=Roll(target,11);
            if(m.Opinion<-30f){Speak(target,"GENERIC_INSULT_HIGH");return;}
            if(roll<72){Speak(target,"GENERIC_HI");try{Function.Call(Hash.TASK_LOOK_AT_ENTITY,target.Handle,Game.LocalPlayerPed.Handle,1800,0,2);}catch{}}
            else Speak(target,"GENERIC_NO");
        }

        private void RespondCalm(Ped target,Memory m)
        {
            if(m.Fear>55f){Speak(target,"GENERIC_FRIGHTENED_HIGH");DiscreetLeave(target,Game.LocalPlayerPed);}
            else{Speak(target,"GENERIC_THANKS");try{Function.Call(Hash.TASK_LOOK_AT_ENTITY,target.Handle,Game.LocalPlayerPed.Handle,1300,0,2);}catch{}}
        }

        private void RespondContext(Ped target,Memory m)
        {
            if(m.Fear>45f){Speak(target,"GENERIC_FRIGHTENED_HIGH");DiscreetLeave(target,Game.LocalPlayerPed);}
            else if(m.Opinion<-25f)Speak(target,"GENERIC_INSULT_HIGH");
            else Speak(target,"GENERIC_HI");
        }

        private void RespondAntagonize(Ped target,Memory m,bool armedThreat)
        {
            int bravery=Roll(target,37);
            if(armedThreat&&m.Fear>=45f)
            {
                Speak(target,"GENERIC_FRIGHTENED_HIGH");
                if(bravery<60&&Distance(target,Game.LocalPlayerPed)<8f)
                {
                    try{Function.Call(Hash.TASK_HANDS_UP,target.Handle,3500,Game.LocalPlayerPed.Handle,-1,false);}catch{}
                }
                else DiscreetLeave(target,Game.LocalPlayerPed);
                return;
            }
            if(bravery>68){Speak(target,"GENERIC_INSULT_HIGH");try{Function.Call(Hash.TASK_LOOK_AT_ENTITY,target.Handle,Game.LocalPlayerPed.Handle,2200,0,2);}catch{}}
            else{Speak(target,"GENERIC_SHOCKED_HIGH");DiscreetLeave(target,Game.LocalPlayerPed);}
        }

        private static void DiscreetLeave(Ped ped,Ped player)
        {
            if(ped==null||player==null||!ped.Exists()||!player.Exists())return;
            Vector3 d=ped.Position-player.Position;float len=(float)Math.Sqrt(d.X*d.X+d.Y*d.Y);if(len<0.1f)len=1f;
            Vector3 target=ped.Position+new Vector3(d.X/len,d.Y/len,0f)*18f;
            try{Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD,ped.Handle,target.X,target.Y,target.Z,1.05f,8000,1.2f,0,0f);}catch{}
        }

        private static void Speak(Ped ped,string speech)
        {
            if(ped==null||!ped.Exists())return;
            try{Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE,ped.Handle,speech,"SPEECH_PARAMS_FORCE");}catch{}
        }

        private void ProbePedBridge()
        {
            _lastBridgeProbe=Game.GameTime;_pedBridgeRegister=null;_pedBridgeFear=null;_pedBridgeOpinion=null;
            if(!_cfg.BridgeToPedOverhaul)return;
            try
            {
                Assembly a=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x=>string.Equals(x.GetName().Name,"PedOverhaulVI",StringComparison.OrdinalIgnoreCase));
                Type t=a==null?null:a.GetType("VOX.PedOverhaulVI.PedOverhaulVIBridge",false);
                if(t==null)return;
                _pedBridgeRegister=t.GetMethod("RegisterPlayerInteraction",BindingFlags.Public|BindingFlags.Static);
                _pedBridgeFear=t.GetMethod("GetFearAssociation",BindingFlags.Public|BindingFlags.Static);
                _pedBridgeOpinion=t.GetMethod("GetOpinion",BindingFlags.Public|BindingFlags.Static);
            }
            catch{}
        }

        private void SendToPedBridge(Ped target,string intent,float intensity)
        {
            if(_pedBridgeRegister==null)return;
            try{_pedBridgeRegister.Invoke(null,new object[]{target.Handle,target.Model.Hash,intent,intensity});}catch{}
        }
        private float GetFear(Ped target){float local=GetOrCreate(target).Fear;try{if(_pedBridgeFear!=null)local=Math.Max(local,Convert.ToSingle(_pedBridgeFear.Invoke(null,new object[]{target.Handle})));}catch{}return local;}
        private float GetOpinion(Ped target){float local=GetOrCreate(target).Opinion;try{if(_pedBridgeOpinion!=null){float b=Convert.ToSingle(_pedBridgeOpinion.Invoke(null,new object[]{target.Handle}));if(Math.Abs(b)>Math.Abs(local))local=b;}}catch{}return local;}

        private Memory GetOrCreate(Ped p)
        {
            Memory m;if(!_memory.TryGetValue(p.Handle,out m)||m.ModelHash!=p.Model.Hash){m=new Memory{ModelHash=p.Model.Hash};_memory[p.Handle]=m;}return m;
        }
        private void CleanupMemory(){int cutoff=Game.GameTime-Math.Max(1,_cfg.LocalMemoryMinutes)*60000;var dead=_memory.Where(x=>x.Value.LastAt>0&&x.Value.LastAt<cutoff).Select(x=>x.Key).Take(8).ToList();foreach(int h in dead)_memory.Remove(h);}
        private bool ShouldYield()
        {
            try{if(Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)||Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))return true;}catch{}
            if(_cfg.DisableWhileWanted){try{if(Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL,Game.Player.Handle)>0)return true;}catch{}}
            if(_cfg.DisableDuringMissions){try{if(Function.Call<bool>(Hash.GET_MISSION_FLAG))return true;}catch{}}
            return false;
        }
        private void ResetFocus(){_focusDown=false;_focusStarted=0;_target=null;}
        private void OnAborted(object sender,EventArgs e){ResetFocus();_memory.Clear();}
        private static int Roll(Ped p,int salt){unchecked{int x=p.Handle*1103515245+p.Model.Hash*97+salt*7919;x^=x>>16;if(x<0)x=-x;return x%100;}}
        private static float Distance(Ped a,Ped b){Vector3 d=a.Position-b.Position;return(float)Math.Sqrt(d.X*d.X+d.Y*d.Y+d.Z*d.Z);}
        private static void DrawText(float x,float y,string text,float scale)
        {
            try
            {
                Function.Call(Hash.SET_TEXT_FONT,0);Function.Call(Hash.SET_TEXT_SCALE,0f,scale);Function.Call(Hash.SET_TEXT_COLOUR,255,255,255,230);Function.Call(Hash.SET_TEXT_OUTLINE);
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,text);Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT,x,y,0);
            }
            catch{}
        }
        private void Log(string s){if(!_cfg.DebugLogging)return;try{Directory.CreateDirectory(DataDirectory);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+s+Environment.NewLine);}catch{}}

        private sealed class Memory{public int ModelHash;public float Opinion;public float Fear;public float Recognition;public int LastAt;}

        private sealed class Config
        {
            public bool Enabled=true,DebugLogging=true,DisableDuringMissions=true,SkipMissionPeds=true,DisableWhileWanted=true,BridgeToPedOverhaul=true,ShowControls=true;
            public int TickIntervalMs=25,FocusHoldMs=450,LookAtAfterMs=650,InteractionCooldownMs=900,LocalMemoryMinutes=10;
            public float MaxDistance=12f,AcquireConeDot=0.86f;
            public Keys FocusKey=Keys.E,PositiveKey=Keys.D1,ContextKey=Keys.D2,NegativeKey=Keys.D3;
            public static Config Load(string path)
            {
                var c=new Config();if(!File.Exists(path))return c;string section=string.Empty;
                foreach(string raw in File.ReadAllLines(path))
                {
                    string line=raw.Trim();if(line.Length==0||line.StartsWith(";")||line.StartsWith("#"))continue;
                    if(line.StartsWith("[")&&line.EndsWith("]")){section=line.Substring(1,line.Length-2);continue;}int eq=line.IndexOf('=');if(eq<=0)continue;
                    string key=section+"."+line.Substring(0,eq).Trim(),v=line.Substring(eq+1).Trim();
                    bool b;int i;float f;Keys k;
                    if(key=="General.Enabled"&&bool.TryParse(v,out b))c.Enabled=b;else if(key=="General.DebugLogging"&&bool.TryParse(v,out b))c.DebugLogging=b;else if(key=="General.DisableDuringMissions"&&bool.TryParse(v,out b))c.DisableDuringMissions=b;else if(key=="General.SkipMissionPeds"&&bool.TryParse(v,out b))c.SkipMissionPeds=b;else if(key=="General.DisableWhileWanted"&&bool.TryParse(v,out b))c.DisableWhileWanted=b;else if(key=="General.TickIntervalMs"&&int.TryParse(v,out i))c.TickIntervalMs=i;
                    else if(key=="Focus.FocusKey"&&Enum.TryParse(v,true,out k))c.FocusKey=k;else if(key=="Focus.PositiveKey"&&Enum.TryParse(v,true,out k))c.PositiveKey=k;else if(key=="Focus.ContextKey"&&Enum.TryParse(v,true,out k))c.ContextKey=k;else if(key=="Focus.NegativeKey"&&Enum.TryParse(v,true,out k))c.NegativeKey=k;else if(key=="Focus.MaxDistance"&&float.TryParse(v,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out f))c.MaxDistance=f;else if(key=="Focus.AcquireConeDot"&&float.TryParse(v,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out f))c.AcquireConeDot=f;else if(key=="Focus.FocusHoldMs"&&int.TryParse(v,out i))c.FocusHoldMs=i;else if(key=="Focus.LookAtAfterMs"&&int.TryParse(v,out i))c.LookAtAfterMs=i;else if(key=="Focus.InteractionCooldownMs"&&int.TryParse(v,out i))c.InteractionCooldownMs=i;
                    else if(key=="Memory.LocalMemoryMinutes"&&int.TryParse(v,out i))c.LocalMemoryMinutes=i;else if(key=="Memory.BridgeToPedOverhaul"&&bool.TryParse(v,out b))c.BridgeToPedOverhaul=b;else if(key=="HUD.ShowControls"&&bool.TryParse(v,out b))c.ShowControls=b;
                }
                return c;
            }
        }
    }
}
