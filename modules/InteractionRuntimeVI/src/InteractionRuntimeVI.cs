using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace VOX.InteractionRuntimeVI
{
    public sealed class InteractionRuntimeVIScript : Script
    {
        private const int Context = 51;      // E / D-pad right
        private const int DpadUp = 172;
        private const int DpadLeft = 174;
        private const int DpadRight = 175;
        private const string ConfigPath = "scripts\\InteractionRuntimeVI.ini";
        private const string DataDir = "scripts\\InteractionRuntimeVI";
        private const string LogPath = DataDir + "\\InteractionRuntimeVI.log";

        private sealed class Memory
        {
            public int ModelHash;
            public float Opinion;
            public float Fear;
            public float Recognition;
            public int Disposition;
            public int ExchangeStage;
            public int NegativeChain;
            public int PositiveChain;
            public int LastAt;
            public string LastIntent = string.Empty;
        }

        private readonly Dictionary<int, Memory> _memory = new Dictionary<int, Memory>();
        private Config _cfg;
        private Ped _target;
        private int _candidateHandle;
        private int _candidateSince;
        private int _targetLostSince;
        private int _lastTargetScan;
        private int _focusStarted;
        private bool _focusDown;
        private bool _leftDown, _upDown, _rightDown;
        private int _lastInteraction;
        private int _lastLookTask;
        private int _storyYieldUntil;
        private MethodInfo _pedBridgeRegister;
        private MethodInfo _pedBridgeFear;
        private MethodInfo _pedBridgeOpinion;
        private int _lastBridgeProbe;

        public InteractionRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            _cfg = Config.Load(ConfigPath);
            Interval = Math.Max(10, _cfg.TickIntervalMs);
            Tick += OnTick;
            Aborted += OnAborted;
            ProbePedBridge();
            Log("Interaction Runtime VI 0.3.0 loaded: native controller-safe focus and multi-exchange contextual conversations.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_cfg.Enabled) return;
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { ResetFocus(true); return; }
                if (Game.GameTime - _lastBridgeProbe > 4000) ProbePedBridge();
                if (ShouldYield()) { ResetFocus(true); return; }
                if (player.IsInVehicle()) { ResetFocus(true); return; }

                CleanupMemory();
                UpdateTarget(player);
                SyncFocus(player);
                if (!_focusDown || _target == null || !_target.Exists()) return;

                int held = Game.GameTime - _focusStarted;
                if (held < _cfg.FocusHoldMs) return;
                SuppressContextOnly();
                DrawInteractionHud(player, _target, GetOrCreate(_target));
                PollDirectional(player);

                if (Game.GameTime - _lastLookTask > 1200)
                {
                    _lastLookTask = Game.GameTime;
                    try { Function.Call(Hash.TASK_LOOK_AT_ENTITY, _target.Handle, player.Handle, 1700, 0, 2); } catch { }
                }
            }
            catch (Exception ex) { Log("Tick error: " + ex); }
        }

        private void SyncFocus(Ped player)
        {
            bool context = Pressed(Context);
            if (context && !_focusDown)
            {
                if (_target == null || !_target.Exists() || !CanOwnContext(player)) return;
                _focusDown = true;
                _focusStarted = Game.GameTime;
                _targetLostSince = 0;
            }
            else if (!context && _focusDown) ResetFocus(false);
        }

        private static bool CanOwnContext(Ped player)
        {
            try { if (Function.Call<int>(Hash.GET_VEHICLE_PED_IS_TRYING_TO_ENTER, player.Handle) != 0) return false; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle)) return false; } catch { }
            return true;
        }

        private void UpdateTarget(Ped player)
        {
            int now=Game.GameTime;
            if(now-_lastTargetScan<Math.Max(60,_cfg.TargetScanMs))return;
            _lastTargetScan=now;

            if(_target!=null&&_target.Exists()&&StillLocked(player,_target))
            {
                _targetLostSince=0;_candidateHandle=0;_candidateSince=0;return;
            }
            if(_target!=null&&_target.Exists())
            {
                if(_targetLostSince==0)_targetLostSince=now;
                if(_focusDown&&now-_targetLostSince<Math.Max(200,_cfg.TargetLostGraceMs))return;
            }

            Ped candidate=FindTarget(player);
            if(candidate==null||!candidate.Exists())
            {
                if(!_focusDown||_targetLostSince==0||now-_targetLostSince>=Math.Max(200,_cfg.TargetLostGraceMs))_target=null;
                _candidateHandle=0;_candidateSince=0;return;
            }
            if(_candidateHandle!=candidate.Handle){_candidateHandle=candidate.Handle;_candidateSince=now;return;}
            if(now-_candidateSince<Math.Max(0,_cfg.TargetAcquireStableMs))return;
            bool changed=_target==null||!_target.Exists()||_target.Handle!=candidate.Handle;
            _target=candidate;_targetLostSince=0;_candidateHandle=0;_candidateSince=0;
            if(changed)_lastLookTask=0;
        }

        private Ped FindTarget(Ped player)
        {
            Ped[] peds;try{peds=World.GetNearbyPeds(player,_cfg.MaxDistance);}catch{return null;}
            Vector3 camPos=GameplayCamera.Position,camDir=GameplayCamera.Direction;
            Ped best=null;float bestScore=float.MinValue;
            foreach(Ped p in peds)
            {
                if(!UsableTarget(p,player))continue;
                Vector3 d=p.Position-camPos;float len=Length(d);if(len<0.1f)continue;
                float dot=Dot(camDir,d)/len;if(dot<_cfg.AcquireConeDot)continue;
                bool los=false;try{los=Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,player.Handle,p.Handle,17);}catch{}
                if(!los)continue;
                float score=dot*125f-len*2.7f;if(score>bestScore){bestScore=score;best=p;}
            }
            return best;
        }

        private bool StillLocked(Ped player,Ped p)
        {
            if(!UsableTarget(p,player))return false;
            Vector3 d=p.Position-GameplayCamera.Position;float len=Length(d);
            if(len<0.1f||len>_cfg.MaxDistance+1f)return false;
            if(Dot(GameplayCamera.Direction,d)/len<_cfg.ReleaseConeDot)return false;
            try{return Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,player.Handle,p.Handle,17);}catch{return false;}
        }

        private bool UsableTarget(Ped p,Ped player)
        {
            if(p==null||!p.Exists()||p.Handle==player.Handle||p.IsDead||!p.IsHuman)return false;
            try{if(p.IsInVehicle())return false;}catch{}
            try{if(Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY,p.Handle))return false;}catch{return false;}
            try{int t=(int)p.PedType;if(t==6||t==27||t==29)return false;}catch{}
            return true;
        }

        private void PollDirectional(Ped player)
        {
            bool l=Pressed(DpadLeft),u=Pressed(DpadUp),r=Pressed(DpadRight);
            if(Game.GameTime-_lastInteraction>=Math.Max(700,_cfg.InteractionCooldownMs))
            {
                if(l&&!_leftDown)Perform(player,_target,0);
                else if(u&&!_upDown)Perform(player,_target,1);
                else if(r&&!_rightDown)Perform(player,_target,2);
            }
            _leftDown=l;_upDown=u;_rightDown=r;
        }

        private void DrawInteractionHud(Ped player,Ped target,Memory m)
        {
            bool armed=IsArmed(player);
            string positive=armed?"Calmer":(m.PositiveChain>0?"Continuer":"Saluer");
            string neutral=m.Fear>48f?"Laisser partir":(m.ExchangeStage>0?"Repondre":"Interpeller");
            string negative=armed?"Menacer":(m.NegativeChain>0?"Insister":"Provoquer");
            DrawText(0.785f,0.720f,"^ "+neutral,0.27f);
            DrawText(0.710f,0.775f,positive+" <",0.27f);
            DrawText(0.855f,0.775f,"> "+negative,0.27f);
        }

        private void Perform(Ped player,Ped target,int slot)
        {
            if(player==null||target==null||!player.Exists()||!target.Exists())return;
            Memory m=GetOrCreate(target);
            if(Game.GameTime-m.LastAt>22000){m.ExchangeStage=0;m.PositiveChain=0;m.NegativeChain=0;}
            m.LastAt=Game.GameTime;
            _lastInteraction=Game.GameTime;
            m.Recognition=Math.Min(100f,m.Recognition+8f);
            bool armed=IsArmed(player);

            FaceEachOther(player,target);
            if(slot==0)PositiveExchange(player,target,m,armed);
            else if(slot==1)ContextExchange(player,target,m,armed);
            else NegativeExchange(player,target,m,armed);

            ReactGroup(target,player,m);
            SendToPedBridge(target,m.LastIntent,1f);
            Log("Interaction ped="+target.Handle+" intent="+m.LastIntent+" stage="+m.ExchangeStage+" opinion="+(int)m.Opinion+" fear="+(int)m.Fear+".");
        }

        private void PositiveExchange(Ped player,Ped target,Memory m,bool armed)
        {
            m.LastIntent=armed?"calm":"greet";
            m.NegativeChain=Math.Max(0,m.NegativeChain-1);
            if(armed)
            {
                Speak(player,m.Fear>50f?"GENERIC_HI":"GENERIC_THANKS");
                m.Fear=Math.Max(0f,m.Fear-13f);m.Opinion=Math.Min(100f,m.Opinion+3f);
                if(m.Fear>45f){Speak(target,"GENERIC_FRIGHTENED_HIGH");DiscreetLeave(target,player);}
                else Speak(target,"GENERIC_THANKS");
                return;
            }

            m.PositiveChain++;
            if(m.PositiveChain==1)
            {
                Speak(player,"GENERIC_HI");
                if(m.Opinion<-35f||m.Disposition<15){Speak(target,"GENERIC_NO");m.Opinion-=2f;}
                else if(m.Disposition>78){Speak(target,"GENERIC_HI");m.Opinion+=10f;m.ExchangeStage=1;}
                else{Speak(target,"GENERIC_HI");m.Opinion+=6f;m.ExchangeStage=1;}
            }
            else if(m.PositiveChain==2&&m.ExchangeStage>0)
            {
                Speak(player,"GENERIC_HOWS_IT_GOING");
                if(m.Disposition>62||m.Opinion>20f){Speak(target,"GENERIC_YES");m.Opinion+=7f;m.ExchangeStage=2;}
                else{Speak(target,"GENERIC_NO");m.ExchangeStage=2;}
            }
            else
            {
                Speak(player,"GENERIC_THANKS");
                Speak(target,m.Opinion>=0f?"GENERIC_THANKS":"GENERIC_NO");
                m.ExchangeStage=Math.Min(3,m.ExchangeStage+1);
            }
            m.Opinion=Clamp(m.Opinion,-100f,100f);
        }

        private void ContextExchange(Ped player,Ped target,Memory m,bool armed)
        {
            m.LastIntent="context";
            if(m.Fear>48f)
            {
                Speak(player,"GENERIC_HI");
                Speak(target,"GENERIC_FRIGHTENED_HIGH");
                DiscreetLeave(target,player);
                m.Fear=Math.Max(0f,m.Fear-5f);return;
            }
            if(m.ExchangeStage==0)
            {
                Speak(player,"GENERIC_HI");
                Speak(target,m.Opinion<-25f?"GENERIC_NO":"GENERIC_HI");
                m.ExchangeStage=1;
            }
            else if(m.NegativeChain>0)
            {
                Speak(player,"GENERIC_SORRY");
                if(m.Disposition>35){Speak(target,"GENERIC_YES");m.Opinion+=4f;m.NegativeChain=Math.Max(0,m.NegativeChain-1);}
                else Speak(target,"GENERIC_NO");
            }
            else
            {
                Speak(player,"GENERIC_YES");
                Speak(target,m.Opinion>=0f?"GENERIC_YES":"GENERIC_NO");
                m.ExchangeStage=Math.Min(3,m.ExchangeStage+1);
            }
        }

        private void NegativeExchange(Ped player,Ped target,Memory m,bool armed)
        {
            m.LastIntent=armed?"threaten":"antagonize";
            m.PositiveChain=0;m.NegativeChain++;
            m.Opinion=Math.Max(-100f,m.Opinion-(armed?30f:16f));
            m.Fear=Math.Min(100f,m.Fear+(armed?34f:8f));
            m.Recognition=Math.Min(100f,m.Recognition+(armed?22f:12f));
            Speak(player,"GENERIC_INSULT_HIGH");

            int bravery=(m.Disposition+Roll(target,37))/2;
            if(armed)
            {
                Speak(target,"GENERIC_FRIGHTENED_HIGH");
                if(bravery<62)
                {
                    if(m.NegativeChain>=2&&Distance(target,player)<7f){try{Function.Call(Hash.TASK_HANDS_UP,target.Handle,4000,player.Handle,-1,false);}catch{}}
                    else DiscreetLeave(target,player);
                }
                else DiscreetLeave(target,player);
                return;
            }

            if(m.NegativeChain==1)
            {
                if(bravery>60){Speak(target,"GENERIC_INSULT_HIGH");m.ExchangeStage=1;}
                else{Speak(target,"GENERIC_SHOCKED_HIGH");if(m.Fear>25f)DiscreetLeave(target,player);}
            }
            else if(m.NegativeChain==2)
            {
                if(bravery>68&&Distance(target,player)<4.5f){Speak(target,"GENERIC_INSULT_HIGH");try{Function.Call(Hash.TASK_COMBAT_PED,target.Handle,player.Handle,0,16);}catch{}}
                else{Speak(target,"GENERIC_NO");DiscreetLeave(target,player);}
            }
            else
            {
                if(bravery>56&&Distance(target,player)<5f){try{Function.Call(Hash.TASK_COMBAT_PED,target.Handle,player.Handle,0,16);}catch{}}
                else DiscreetLeave(target,player);
            }
        }

        private void ReactGroup(Ped target,Ped player,Memory m)
        {
            if(Roll(target,91)>34)return;
            Ped[] peds;try{peds=World.GetNearbyPeds(target,4.2f);}catch{return;}
            foreach(Ped p in peds)
            {
                if(!UsableTarget(p,player)||p.Handle==target.Handle)continue;
                try{Function.Call(Hash.TASK_LOOK_AT_ENTITY,p.Handle,player.Handle,1600,0,2);}catch{}
                if(m.NegativeChain>=2){Speak(p,m.Fear>35f?"GENERIC_SHOCKED_HIGH":"GENERIC_INSULT_HIGH");}
                else if(m.PositiveChain>=2)Speak(p,"GENERIC_HI");
                break;
            }
        }

        private static void FaceEachOther(Ped player,Ped target)
        {
            try{Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY,player.Handle,target.Handle,350);}catch{}
            try{Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY,target.Handle,player.Handle,450);}catch{}
            try{Function.Call(Hash.TASK_LOOK_AT_ENTITY,target.Handle,player.Handle,1900,0,2);}catch{}
        }

        private static bool IsArmed(Ped p){try{return Function.Call<bool>(Hash.IS_PED_ARMED,p.Handle,7);}catch{return false;}}
        private static void Speak(Ped p,string speech){if(p==null||!p.Exists())return;try{Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE,p.Handle,speech,"SPEECH_PARAMS_FORCE");}catch{}}
        private static void DiscreetLeave(Ped p,Ped player)
        {
            if(p==null||player==null||!p.Exists()||!player.Exists())return;
            Vector3 d=p.Position-player.Position;float len=(float)Math.Sqrt(d.X*d.X+d.Y*d.Y);if(len<0.1f)len=1f;
            Vector3 t=p.Position+new Vector3(d.X/len,d.Y/len,0f)*22f;
            try{Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD,p.Handle,t.X,t.Y,t.Z,1.15f,10000,1.2f,0,0f);}catch{}
        }

        private Memory GetOrCreate(Ped p)
        {
            Memory m;
            if(!_memory.TryGetValue(p.Handle,out m)||m.ModelHash!=p.Model.Hash)
            {
                m=new Memory{ModelHash=p.Model.Hash,Disposition=20+Roll(p,17)%70};
                _memory[p.Handle]=m;
            }
            float bridgeFear=GetBridgeFear(p),bridgeOpinion=GetBridgeOpinion(p);
            m.Fear=Math.Max(m.Fear,bridgeFear);
            if(Math.Abs(bridgeOpinion)>Math.Abs(m.Opinion))m.Opinion=bridgeOpinion;
            return m;
        }

        private void CleanupMemory()
        {
            int cutoff=Game.GameTime-Math.Max(1,_cfg.LocalMemoryMinutes)*60000;
            var dead=_memory.Where(x=>x.Value.LastAt>0&&x.Value.LastAt<cutoff).Select(x=>x.Key).Take(12).ToList();
            foreach(int h in dead)_memory.Remove(h);
        }

        private void ProbePedBridge()
        {
            _lastBridgeProbe=Game.GameTime;_pedBridgeRegister=_pedBridgeFear=_pedBridgeOpinion=null;
            if(!_cfg.BridgeToPedOverhaul)return;
            try
            {
                Assembly a=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x=>string.Equals(x.GetName().Name,"PedOverhaulVI",StringComparison.OrdinalIgnoreCase));
                Type t=a==null?null:a.GetType("VOX.PedOverhaulVI.PedOverhaulVIBridge",false);if(t==null)return;
                _pedBridgeRegister=t.GetMethod("RegisterPlayerInteraction",BindingFlags.Public|BindingFlags.Static);
                _pedBridgeFear=t.GetMethod("GetFearAssociation",BindingFlags.Public|BindingFlags.Static);
                _pedBridgeOpinion=t.GetMethod("GetOpinion",BindingFlags.Public|BindingFlags.Static);
            }
            catch{}
        }
        private void SendToPedBridge(Ped p,string intent,float intensity){try{if(_pedBridgeRegister!=null)_pedBridgeRegister.Invoke(null,new object[]{p.Handle,p.Model.Hash,intent,intensity});}catch{}}
        private float GetBridgeFear(Ped p){try{return _pedBridgeFear==null?0f:Convert.ToSingle(_pedBridgeFear.Invoke(null,new object[]{p.Handle}));}catch{return 0f;}}
        private float GetBridgeOpinion(Ped p){try{return _pedBridgeOpinion==null?0f:Convert.ToSingle(_pedBridgeOpinion.Invoke(null,new object[]{p.Handle}));}catch{return 0f;}}

        private bool ShouldYield()
        {
            bool story=false;
            try{story|=Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE);}catch{}
            try{story|=Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS);}catch{}
            try{story|=Function.Call<bool>(Hash.GET_MISSION_FLAG);}catch{}
            try{story|=!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON,Game.Player.Handle);}catch{}
            try{story|=Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_IN);}catch{}
            if(story){_storyYieldUntil=Game.GameTime+5000;return true;}
            if(Game.GameTime<_storyYieldUntil)return true;
            if(_cfg.DisableWhileWanted){try{if(Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL,Game.Player.Handle)>0)return true;}catch{}}
            return false;
        }

        private void ResetFocus(bool clearTarget)
        {
            _focusDown=false;_focusStarted=0;_targetLostSince=0;_leftDown=_upDown=_rightDown=false;
            if(clearTarget){_target=null;_candidateHandle=0;_candidateSince=0;}
        }

        private static void SuppressContextOnly(){try{Function.Call(Hash.DISABLE_CONTROL_ACTION,0,Context,true);}catch{}}
        private static bool Pressed(int c){try{return Function.Call<bool>(Hash.IS_CONTROL_PRESSED,0,c)||Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED,0,c);}catch{return false;}}
        private static int Roll(Ped p,int salt){unchecked{int x=p.Handle*1103515245+p.Model.Hash*97+salt*7919;x^=x>>16;if(x==int.MinValue)x=0;return Math.Abs(x)%100;}}
        private static float Distance(Ped a,Ped b){return Length(a.Position-b.Position);}
        private static float Dot(Vector3 a,Vector3 b){return a.X*b.X+a.Y*b.Y+a.Z*b.Z;}
        private static float Length(Vector3 v){return(float)Math.Sqrt(v.X*v.X+v.Y*v.Y+v.Z*v.Z);}
        private static float Clamp(float v,float min,float max){return v<min?min:(v>max?max:v);}
        private static void DrawText(float x,float y,string text,float scale)
        {
            try{Function.Call(Hash.SET_TEXT_FONT,0);Function.Call(Hash.SET_TEXT_SCALE,0f,scale);Function.Call(Hash.SET_TEXT_COLOUR,255,255,255,230);Function.Call(Hash.SET_TEXT_OUTLINE);Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,text);Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT,x,y,0);}catch{}
        }
        private void OnAborted(object sender,EventArgs e){ResetFocus(true);_memory.Clear();}
        private void Log(string s){if(!_cfg.DebugLogging)return;try{Directory.CreateDirectory(DataDir);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+s+Environment.NewLine);}catch{}}

        private sealed class Config
        {
            public bool Enabled=true,DebugLogging=true,DisableWhileWanted=true,BridgeToPedOverhaul=true;
            public int TickIntervalMs=25,FocusHoldMs=420,InteractionCooldownMs=850,LocalMemoryMinutes=20,TargetScanMs=90,TargetAcquireStableMs=150,TargetLostGraceMs=750;
            public float MaxDistance=8f,AcquireConeDot=0.88f,ReleaseConeDot=0.79f;
            public static Config Load(string path)
            {
                var c=new Config();if(!File.Exists(path))return c;string section="";
                foreach(string raw in File.ReadAllLines(path))
                {
                    string line=raw.Trim();if(line.Length==0||line.StartsWith(";")||line.StartsWith("#"))continue;
                    if(line.StartsWith("[")&&line.EndsWith("]")){section=line.Substring(1,line.Length-2).Trim();continue;}
                    int eq=line.IndexOf('=');if(eq<=0)continue;string k=section+"."+line.Substring(0,eq).Trim(),v=line.Substring(eq+1).Trim();
                    if(k=="General.Enabled")c.Enabled=B(v,c.Enabled);else if(k=="General.DebugLogging")c.DebugLogging=B(v,c.DebugLogging);else if(k=="General.DisableWhileWanted")c.DisableWhileWanted=B(v,c.DisableWhileWanted);else if(k=="General.TickIntervalMs")c.TickIntervalMs=I(v,c.TickIntervalMs);
                    else if(k=="Focus.MaxDistance")c.MaxDistance=F(v,c.MaxDistance);else if(k=="Focus.AcquireConeDot")c.AcquireConeDot=F(v,c.AcquireConeDot);else if(k=="Focus.ReleaseConeDot")c.ReleaseConeDot=F(v,c.ReleaseConeDot);else if(k=="Focus.FocusHoldMs")c.FocusHoldMs=I(v,c.FocusHoldMs);else if(k=="Focus.InteractionCooldownMs")c.InteractionCooldownMs=I(v,c.InteractionCooldownMs);else if(k=="Focus.TargetScanMs")c.TargetScanMs=I(v,c.TargetScanMs);else if(k=="Focus.TargetAcquireStableMs")c.TargetAcquireStableMs=I(v,c.TargetAcquireStableMs);else if(k=="Focus.TargetLostGraceMs")c.TargetLostGraceMs=I(v,c.TargetLostGraceMs);
                    else if(k=="Memory.LocalMemoryMinutes")c.LocalMemoryMinutes=I(v,c.LocalMemoryMinutes);else if(k=="Memory.BridgeToPedOverhaul")c.BridgeToPedOverhaul=B(v,c.BridgeToPedOverhaul);
                }
                return c;
            }
            private static bool B(string s,bool d){bool v;return bool.TryParse(s,out v)?v:d;}
            private static int I(string s,int d){int v;return int.TryParse(s,out v)?v:d;}
            private static float F(string s,float d){float v;return float.TryParse(s,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out v)?v:d;}
        }
    }
}
