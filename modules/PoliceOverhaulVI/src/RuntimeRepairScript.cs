using GTA;
using GTA.Native;
using System;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    public sealed class PoliceOverhaulVIRuntimeRepairScript : Script
    {
        private const string LogPath="scripts\\PoliceOverhaulVI\\PoliceOverhaulVI.log";
        private bool _deathHandled;
        private int _lastClampLog;

        public PoliceOverhaulVIRuntimeRepairScript()
        {
            Interval=100;Tick+=OnTick;Aborted+=OnAborted;
            Log("Police runtime repair 0.8.2 loaded: death reset, post-death reacquire grace and bounded searches.");
        }

        private void OnTick(object sender,EventArgs e)
        {
            try
            {
                Ped player=Game.LocalPlayerPed;
                bool dead=player==null||!player.Exists()||player.IsDead;
                if(dead){if(!_deathHandled){_deathHandled=true;ResetActivePursuitOnDeath();}return;}
                _deathHandled=false;

                if(PoliceSearchRuntimeState.SearchActive)
                {
                    int threat=Math.Max(1,Math.Min(6,PoliceSearchRuntimeState.ThreatLevel));int maximum=SearchLifetimeMs(threat);int cap=PoliceSearchRuntimeState.SearchStartedAt+maximum;
                    if(PoliceSearchRuntimeState.SearchStartedAt>0&&PoliceSearchRuntimeState.SearchDeadlineAt>cap){PoliceSearchRuntimeState.SearchDeadlineAt=cap;if(Game.GameTime-_lastClampLog>5000){_lastClampLog=Game.GameTime;Log("Search deadline bounded: threat="+threat+" lifetime="+(maximum/1000)+"s.");}}
                }
                else try{if(Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL,Game.Player.Handle)<=0)Function.Call(Hash.SET_POLICE_IGNORE_PLAYER,Game.Player.Handle,false);}catch{}
            }
            catch(Exception ex){Log("Runtime repair tick error: "+ex.Message);}
        }

        private static int SearchLifetimeMs(int threat){switch(threat){case 1:return 75000;case 2:return 90000;case 3:return 120000;case 4:return 150000;case 5:return 180000;default:return 210000;}}

        private static void ResetActivePursuitOnDeath()
        {
            try{Function.Call(Hash.SET_PLAYER_WANTED_LEVEL,Game.Player.Handle,0,false);Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW,Game.Player.Handle,false);}catch{}
            try{Function.Call(Hash.SET_POLICE_IGNORE_PLAYER,Game.Player.Handle,false);}catch{}
            try{Function.Call(Hash.SET_DISPATCH_COPS_FOR_PLAYER,Game.Player.Handle,false);}catch{}
            DispatchSystem.EmergencyCleanup();
            PoliceSearchRuntimeState.NotifyPlayerDeath();
            PoliceWantedHudState.Clear();
            SearchHudSystem.NotifyPlayerDeath();
            Log("Player death: active pursuit cleared and reacquisition suppressed through hospital respawn grace.");
        }

        private void OnAborted(object sender,EventArgs e){try{Function.Call(Hash.SET_POLICE_IGNORE_PLAYER,Game.Player.Handle,false);}catch{}DispatchSystem.EmergencyCleanup();}
        private static void Log(string s){try{Directory.CreateDirectory("scripts\\PoliceOverhaulVI");File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+s+Environment.NewLine);}catch{}}
    }
}
