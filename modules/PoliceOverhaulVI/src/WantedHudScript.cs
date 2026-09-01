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
        private const string CommonMenu="commonmenu";
        private const string RockstarStar="shop_new_star";

        public PoliceOverhaulVIWantedHudScript(){Interval=0;Tick+=OnTick;Aborted+=OnAborted;}

        private void OnTick(object sender,EventArgs e)
        {
            if(RockstarOwnsScene()){PoliceWantedHudState.Clear();return;}
            if(!PoliceWantedHudState.Owned||PoliceWantedHudState.Level<6)return;
            int nativeWanted=0;try{nativeWanted=Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL,Game.Player.Handle);}catch{}
            if(nativeWanted<=0)return;

            // Keep GTA V's real HUD_WANTED_STARS component untouched. The previous
            // implementation hid it and redrew the whole row, which is why even the
            // first five stars no longer looked like GTA. At tier six we add only the
            // missing sixth Rockstar star beside the native five-star row.
            try
            {
                Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT,CommonMenu,false);
                if(!Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED,CommonMenu))return;
                const float x=0.8525f;
                const float y=0.0425f;
                const float width=0.0185f;
                const float height=0.0330f;
                Function.Call(Hash.DRAW_SPRITE,CommonMenu,RockstarStar,x,y,width,height,0f,255,255,255,255,false);
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
