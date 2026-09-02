using GTA;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    internal static class PoliceWantedHudState
    {
        public static int Level;
        public static bool Owned;
        public static void Set(int level,bool owned){if(level<=0||!owned){Clear();return;}Level=Math.Max(1,Math.Min(6,level));Owned=true;}
        public static void Clear(){Level=0;Owned=false;}
    }

    public sealed class PoliceOverhaulVIWantedHudScript : Script
    {
        // Rockstar's real HUD component. The HUD_WANTED_STARS Scaleform already
        // contains star1..star6 even though normal GTA V gameplay caps wanted at 5.
        private const int HudWantedStars=1;
        private static readonly Hash RequestScaleformScriptHudMovie=(Hash)0x9304881D6F6537EAUL;
        private static readonly Hash HasScaleformScriptHudMovieLoaded=(Hash)0xDF6E5987D2B4D140UL;
        private static readonly Hash BeginScaleformScriptHudMovieMethod=(Hash)0x98C494FD5BDFBFD5UL;
        private static readonly Hash AddParamInt=(Hash)0xC3D0841A0CC546A6UL;
        private static readonly Hash EndScaleformMovieMethod=(Hash)0xC6796A8FFA375E53UL;
        private int _nextRequest;

        public PoliceOverhaulVIWantedHudScript(){Interval=0;Tick+=OnTick;Aborted+=OnAborted;}

        private void OnTick(object sender,EventArgs e)
        {
            if(RockstarOwnsScene()){PoliceWantedHudState.Clear();return;}
            if(!PoliceWantedHudState.Owned||PoliceWantedHudState.Level<6)return;
            int nativeWanted=0;try{nativeWanted=Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL,Game.Player.Handle);}catch{}
            if(nativeWanted<=0)return;

            // Never hide or redraw the native wanted row. We call the public method
            // on Rockstar's own HUD_WANTED_STARS movie, asking that movie to reveal
            // its built-in sixth star. This keeps the exact same artwork, spacing,
            // flashing and aspect-ratio behaviour as the first five stars.
            try
            {
                if(Game.GameTime>=_nextRequest)
                {
                    _nextRequest=Game.GameTime+1000;
                    Function.Call(RequestScaleformScriptHudMovie,HudWantedStars);
                }
                if(!Function.Call<bool>(HasScaleformScriptHudMovieLoaded,HudWantedStars))return;
                bool begun=Function.Call<bool>(BeginScaleformScriptHudMovieMethod,HudWantedStars,"SET_PLAYER_WANTED_LEVEL");
                if(!begun)return;
                Function.Call(AddParamInt,6);
                Function.Call(EndScaleformMovieMethod);
            }
            catch{}
        }

        private static bool RockstarOwnsScene()
        {
            try{if(Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE))return true;}catch{}
            try{if(Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))return true;}catch{}
            try{if(Function.Call<bool>(Hash.GET_MISSION_FLAG))return true;}catch{}
            try{if(!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON,Game.Player.Handle))return true;}catch{}
            try{return Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_IN);}catch{return true;}
        }
        private void OnAborted(object sender,EventArgs e){PoliceWantedHudState.Clear();}
    }
}
