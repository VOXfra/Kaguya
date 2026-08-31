using GTA;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class StoryNotorietySystem
    {
        private sealed class HeistDef { public string Script; public string Label; public float Notoriety; }
        private static readonly HeistDef[] Heists = {
            new HeistDef{Script="jewelry_heist",Label="Vangelico robbery",Notoriety=16f},
            new HeistDef{Script="docks_heista",Label="Merryweather heist A",Notoriety=10f},
            new HeistDef{Script="docks_heistb",Label="Merryweather heist B",Notoriety=10f},
            new HeistDef{Script="agency_heist1",Label="Bureau raid A",Notoriety=20f},
            new HeistDef{Script="agency_heist2",Label="Bureau raid B",Notoriety=20f},
            new HeistDef{Script="finale_heist1",Label="Union Depository A",Notoriety=30f},
            new HeistDef{Script="finale_heist2",Label="Union Depository B",Notoriety=30f}
        };
        private readonly Dictionary<string,int> _activeSince=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _credited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _lastScan;

        public void Update(Ped player,CaseMemory memory,Config cfg,Action<string> log)
        {
            if(!cfg.StoryNotorietyEnabled||player==null||!player.Exists()||memory==null)return;
            if(Game.GameTime-_lastScan<1000)return;_lastScan=Game.GameTime;
            foreach(HeistDef h in Heists)
            {
                bool running=IsScriptRunning(h.Script);
                int since;
                if(running)
                {
                    if(!_activeSince.ContainsKey(h.Script))_activeSince[h.Script]=Game.GameTime;
                    continue;
                }
                if(!_activeSince.TryGetValue(h.Script,out since))continue;
                _activeSince.Remove(h.Script);
                if(_credited.Contains(h.Script))continue;
                int duration=Game.GameTime-since;
                bool missionFlag=false;try{missionFlag=Function.Call<bool>(Hash.GET_MISSION_FLAG);}catch{}
                if(duration<Math.Max(10000,cfg.StoryHeistMinimumActiveMs)||missionFlag)continue;
                _credited.Add(h.Script);
                memory.MajorHeistsKnown++;
                IdentificationSystem.AddNotoriety(memory,h.Notoriety*cfg.StoryNotorietyMultiplier,cfg);
                memory.Touch(cfg);
                if(log!=null)log("Story-heist notoriety recorded: "+h.Label+", notoriety="+(int)memory.Notoriety+".");
            }
        }

        private static bool IsScriptRunning(string name)
        {
            try
            {
                int hash=Function.Call<int>(Hash.GET_HASH_KEY,name);
                return Function.Call<int>(Hash.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH,hash)>0;
            }
            catch{return false;}
        }
    }
}
