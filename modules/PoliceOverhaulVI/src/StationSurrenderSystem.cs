using GTA;
using GTA.Math;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class StationSurrenderSystem
    {
        private sealed class Station { public string Name; public Vector3 Position; }
        private static readonly Station[] Stations = {
            new Station{Name="Mission Row LSPD",Position=new Vector3(425.1f,-979.5f,30.7f)},
            new Station{Name="Sandy Shores Sheriff",Position=new Vector3(1853.3f,3686.0f,34.2f)},
            new Station{Name="Paleto Bay Sheriff",Position=new Vector3(-445.0f,6013.9f,31.7f)}
        };
        private int _holdStarted;
        private string _holdingStation=string.Empty;
        private int _lastSuccess;

        public bool Update(Ped player,CaseMemory memory,int wanted,Config cfg,Action<string> log)
        {
            if(!cfg.StationSurrenderEnabled||player==null||!player.Exists()||player.IsDead){Reset();return false;}
            if(memory==null||(!memory.Active&&!memory.WarrantActive&&wanted<=0)){Reset();return false;}
            if(Game.GameTime-_lastSuccess<5000)return false;
            try{if(player.IsInVehicle()){Reset();return false;}}catch{}
            bool shooting=false,armed=false;
            try{shooting=Function.Call<bool>(Hash.IS_PED_SHOOTING,player.Handle);armed=Function.Call<bool>(Hash.IS_PED_ARMED,player.Handle,7);}catch{}
            if(shooting){Reset();return false;}

            Station nearest=null;float best=float.MaxValue;
            foreach(Station s in Stations){float d=Distance(player.Position,s.Position);if(d<best){best=d;nearest=s;}}
            if(nearest==null||best>Math.Max(2f,cfg.StationSurrenderRadius)){Reset();return false;}

            string msg=armed?"Rangez votre arme puis maintenez ~INPUT_CONTEXT~ pour vous rendre.":"Maintenez ~INPUT_CONTEXT~ pour vous rendre au commissariat.";
            ShowHelp(msg);
            if(armed){Reset();return false;}
            bool pressed=false;try{pressed=Function.Call<bool>(Hash.IS_CONTROL_PRESSED,0,51);}catch{}
            if(!pressed){Reset();return false;}
            if(_holdStarted==0||!string.Equals(_holdingStation,nearest.Name,StringComparison.Ordinal)){_holdStarted=Game.GameTime;_holdingStation=nearest.Name;return false;}
            if(Game.GameTime-_holdStarted<Math.Max(500,cfg.StationSurrenderHoldMs))return false;

            try{Function.Call(Hash.TASK_HANDS_UP,player.Handle,2200,0,-1,false);}catch{}
            memory.WarrantActive=false;memory.WarrantExpiresUtcTicks=0;memory.LastWantedEndedAt=Game.GameTime;memory.SurrenderCount++;
            memory.ThreatLevel=Math.Max(0,memory.ThreatLevel-1);memory.HeatPoints=0;memory.Touch(cfg);
            IdentificationSystem.AddNotoriety(memory,Math.Max(0f,cfg.SurrenderNotorietyReduction)*-1f,cfg);
            _lastSuccess=Game.GameTime;
            Notify("Vous vous êtes rendu à " + nearest.Name + ". Le mandat actif est levé; le dossier policier reste archivé.");
            if(log!=null)log("Voluntary station surrender accepted at "+nearest.Name+".");
            Reset();return true;
        }

        private void Reset(){_holdStarted=0;_holdingStation=string.Empty;}
        private static float Distance(Vector3 a,Vector3 b){Vector3 d=a-b;return(float)Math.Sqrt(d.X*d.X+d.Y*d.Y+d.Z*d.Z);}
        private static void ShowHelp(string text){try{Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,text);Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP,0,false,true,-1);}catch{}}
        private static void Notify(string text){try{Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,text);Function.Call<int>(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER,false,true);}catch{}}
    }
}
