using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    public sealed class PoliceOverhaulVIFieldSurrenderScript : Script
    {
        private const string LogPath="scripts\\PoliceOverhaulVI\\PoliceOverhaulVI.log";
        private const int Context=51;
        private int _holdStarted,_officerHandle,_releaseIgnoreAt;
        private bool _ignoreApplied;

        public PoliceOverhaulVIFieldSurrenderScript(){Interval=40;Tick+=OnTick;Aborted+=OnAborted;Log("Field surrender 0.1.1 loaded: contextual surrender to a visible nearby officer.");}

        private void OnTick(object sender,EventArgs e)
        {
            try
            {
                if(_releaseIgnoreAt>0&&Game.GameTime>=_releaseIgnoreAt){ReleaseIgnore();_releaseIgnoreAt=0;}
                Ped player=Game.LocalPlayerPed;
                if(player==null||!player.Exists()||player.IsDead||RockstarOwnsScene()){ResetHold();return;}
                int wanted=WantedLevel();if(wanted<=0||player.IsInVehicle()){ResetHold();return;}

                bool armed=false,shooting=false,aiming=false;float speed=0f;
                try{armed=Function.Call<bool>(Hash.IS_PED_ARMED,player.Handle,7);shooting=Function.Call<bool>(Hash.IS_PED_SHOOTING,player.Handle);aiming=Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING,Game.Player.Handle);speed=Function.Call<float>(Hash.GET_ENTITY_SPEED,player.Handle);}catch{}
                Ped officer=FindVisibleOfficer(player,16f);
                if(officer==null||!officer.Exists()||shooting||aiming||speed>2.3f){ResetHold();return;}
                if(armed){ShowHelp("Rangez votre arme pour vous rendre.");ResetHold();return;}

                ShowHelp("Maintenez ~INPUT_CONTEXT~ pour vous rendre.");
                bool down=Pressed(Context);if(!down){ResetHold();return;}
                if(_holdStarted==0||_officerHandle!=officer.Handle){_holdStarted=Game.GameTime;_officerHandle=officer.Handle;return;}
                int elapsed=Game.GameTime-_holdStarted;
                if(elapsed>=350&&!_ignoreApplied)
                {
                    try{Function.Call(Hash.TASK_HANDS_UP,player.Handle,3200,officer.Handle,-1,false);Function.Call(Hash.SET_POLICE_IGNORE_PLAYER,Game.Player.Handle,true);_ignoreApplied=true;}catch{}
                }
                if(elapsed<1600)return;

                DispatchSystem.EmergencyCleanup();
                try{Function.Call(Hash.SET_PLAYER_WANTED_LEVEL,Game.Player.Handle,0,false);Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW,Game.Player.Handle,false);}catch{}
                PoliceSearchRuntimeState.ResetSearch(false);PoliceWantedHudState.Clear();SearchHudSystem.NotifyPlayerDeath();
                _releaseIgnoreAt=Game.GameTime+2200;_holdStarted=0;_officerHandle=0;
                try{Function.Call(Hash.TASK_ARREST_PED,officer.Handle,player.Handle);}catch{}
                Notify("Vous vous rendez aux forces de l'ordre.");
                Log("Field surrender accepted; vanilla wanted, search state and VOX military response cleared.");
            }
            catch(Exception ex){Log("Field surrender tick error: "+ex.Message);ResetHold();}
        }

        private static Ped FindVisibleOfficer(Ped player,float radius)
        {
            Ped[] peds;try{peds=World.GetNearbyPeds(player,radius);}catch{return null;}Ped best=null;float bestDistance=radius;
            foreach(Ped p in peds)
            {
                if(p==null||!p.Exists()||p.IsDead||!Perception.IsLawPed(p))continue;float d=Perception.Distance(player.Position,p.Position);if(d>=bestDistance)continue;
                bool los=false;try{los=Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,p.Handle,player.Handle,17);}catch{}if(!los)continue;best=p;bestDistance=d;
            }
            return best;
        }
        private void ResetHold(){_holdStarted=0;_officerHandle=0;if(_releaseIgnoreAt==0)ReleaseIgnore();}
        private void ReleaseIgnore(){if(!_ignoreApplied)return;try{Function.Call(Hash.SET_POLICE_IGNORE_PLAYER,Game.Player.Handle,false);}catch{}_ignoreApplied=false;}
        private static int WantedLevel(){try{return Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL,Game.Player.Handle);}catch{return 0;}}
        private static bool Pressed(int c){try{return Function.Call<bool>(Hash.IS_CONTROL_PRESSED,0,c)||Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED,0,c);}catch{return false;}}
        private static bool RockstarOwnsScene(){try{if(Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE))return true;}catch{}try{if(Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))return true;}catch{}try{if(Function.Call<bool>(Hash.GET_MISSION_FLAG))return true;}catch{}try{if(!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON,Game.Player.Handle))return true;}catch{}return false;}
        private static void ShowHelp(string t){try{Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,t);Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP,0,false,true,-1);}catch{}}
        private static void Notify(string t){try{Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,t);Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER,false,false);}catch{}}
        private void OnAborted(object sender,EventArgs e){_releaseIgnoreAt=0;ResetHold();}
        private static void Log(string t){try{Directory.CreateDirectory("scripts\\PoliceOverhaulVI");File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+t+Environment.NewLine);}catch{}}
    }
}
