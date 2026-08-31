using GTA;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace VOX.PedOverhaulVI
{
    // Narrow public bridge used by Interaction Runtime VI through reflection.
    // No private PedState objects cross assembly boundaries.
    public static class PedOverhaulVIBridge
    {
        private sealed class Memory
        {
            public int ModelHash;
            public float Opinion;
            public float Fear;
            public float Recognition;
            public string LastIntent=string.Empty;
            public int LastAt;
        }
        private static readonly Dictionary<int,Memory> Memories=new Dictionary<int,Memory>();
        private static MethodInfo _corePublish;
        private static int _nextCoreResolve;

        public static void RegisterPlayerInteraction(int pedHandle,int modelHash,string intent,float intensity)
        {
            if(pedHandle<=0)return;
            Memory m;
            if(!Memories.TryGetValue(pedHandle,out m)||m.ModelHash!=modelHash)
            {
                m=new Memory{ModelHash=modelHash};Memories[pedHandle]=m;
            }
            float k=Math.Max(0.25f,Math.Min(2f,intensity));
            string i=(intent??string.Empty).Trim().ToLowerInvariant();
            if(i=="greet"||i=="friendly"){m.Opinion+=10f*k;m.Recognition+=9f*k;m.Fear-=3f*k;}
            else if(i=="calm"){m.Opinion+=4f*k;m.Fear-=14f*k;m.Recognition+=8f*k;}
            else if(i=="help"){m.Opinion+=28f*k;m.Fear-=18f*k;m.Recognition+=28f*k;}
            else if(i=="antagonize"||i=="insult"){m.Opinion-=20f*k;m.Fear+=5f*k;m.Recognition+=16f*k;}
            else if(i=="threaten"||i=="rob"){m.Opinion-=35f*k;m.Fear+=38f*k;m.Recognition+=35f*k;}
            else{m.Recognition+=5f*k;}
            m.Opinion=ClampSigned(m.Opinion);m.Fear=Clamp(m.Fear);m.Recognition=Clamp(m.Recognition);m.LastIntent=intent??string.Empty;m.LastAt=Game.GameTime;
            PublishInteraction(pedHandle,modelHash,i,k);
        }

        public static float GetOpinion(int pedHandle){Memory m;return Memories.TryGetValue(pedHandle,out m)?m.Opinion:0f;}
        public static float GetFearAssociation(int pedHandle){Memory m;return Memories.TryGetValue(pedHandle,out m)?m.Fear:0f;}
        public static float GetRecognition(int pedHandle){Memory m;return Memories.TryGetValue(pedHandle,out m)?m.Recognition:0f;}

        internal static void ObserveAndApply(PedState s,PerceptionFrame frame,int now)
        {
            if(s==null)return;
            Memory m;
            if(Memories.TryGetValue(s.Handle,out m)&&m.ModelHash==s.ModelHash)
            {
                s.OpinionOfPlayer=m.Opinion;s.PlayerFearAssociation=m.Fear;s.RecognitionOfPlayer=m.Recognition;s.LastPlayerInteraction=m.LastIntent;s.LastPlayerInteractionAt=m.LastAt;
                if(frame!=null&&frame.SeesPlayer&&m.Recognition>=45f)
                {
                    s.Attention=Math.Max(s.Attention,12f+m.Recognition*0.18f);
                    if(m.Fear>=45f&&!frame.SeesWeapon&&!frame.DirectlyAimedAt&&!frame.SeesShooting)
                    {
                        s.Suspicion=Math.Max(s.Suspicion,Math.Min(55f,12f+m.Fear*0.42f));
                        s.Fear=Math.Max(s.Fear,Math.Min(58f,m.Fear*0.55f));
                    }
                    else if(m.Opinion>=35f&&!frame.HasAnyStimulus)
                    {
                        s.Suspicion=Math.Max(0f,s.Suspicion-1.5f);s.Fear=Math.Max(0f,s.Fear-2f);
                    }
                }
            }

            if(frame==null)return;
            float fearGain=0f,recognitionGain=0f;
            if(frame.DirectlyAimedAt){fearGain=32f;recognitionGain=30f;}
            else if(frame.SeesShooting){fearGain=38f;recognitionGain=28f;}
            else if(frame.SeesWeapon){fearGain=5f;recognitionGain=5f;}
            if(fearGain<=0f)return;
            if(!Memories.TryGetValue(s.Handle,out m)||m.ModelHash!=s.ModelHash){m=new Memory{ModelHash=s.ModelHash};Memories[s.Handle]=m;}
            m.Fear=Clamp(Math.Max(m.Fear,fearGain));m.Recognition=Clamp(Math.Max(m.Recognition,recognitionGain));m.LastAt=now;
            s.PlayerFearAssociation=m.Fear;s.RecognitionOfPlayer=m.Recognition;
        }

        internal static void Cleanup(ISet<int> live)
        {
            var remove=new List<int>();
            foreach(var pair in Memories)
            {
                if((live==null||!live.Contains(pair.Key))&&Game.GameTime-pair.Value.LastAt>600000)remove.Add(pair.Key);
            }
            foreach(int h in remove)Memories.Remove(h);
        }

        private static void PublishInteraction(int pedHandle,int modelHash,string intent,float intensity)
        {
            try
            {
                int now=Environment.TickCount;
                if(_corePublish==null&&now>=_nextCoreResolve)
                {
                    _nextCoreResolve=now+5000;
                    Type t=Type.GetType("VOX.CoreVI.WorldMemoryBridge, VOXCoreVI",false);
                    if(t!=null)_corePublish=t.GetMethod("Publish",BindingFlags.Public|BindingFlags.Static,null,
                        new[]{typeof(string),typeof(string),typeof(float),typeof(float),typeof(float),typeof(int),typeof(int),typeof(string),typeof(double),typeof(string)},null);
                }
                if(_corePublish==null)return;
                Ped target=new Ped(pedHandle);if(target==null||!target.Exists())return;
                Ped player=Game.LocalPlayerPed;int suspect=player!=null&&player.Exists()?player.Model.Hash:0;
                int severity=(intent=="rob"||intent=="threaten")?3:((intent=="antagonize"||intent=="insult")?1:0);
                var p=target.Position;
                _corePublish.Invoke(null,new object[]{"social","player_interaction",p.X,p.Y,p.Z,severity,suspect,"InteractionRuntimeVI",2.0,"intent="+intent+";targetModel="+modelHash+";intensity="+intensity.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)});
            }
            catch{_corePublish=null;_nextCoreResolve=Environment.TickCount+10000;}
        }

        private static float Clamp(float v){return Math.Max(0f,Math.Min(100f,v));}
        private static float ClampSigned(float v){return Math.Max(-100f,Math.Min(100f,v));}
    }
}
