using GTA;
using GTA.Native;
using System;

namespace VOX.CoreVI
{
    public sealed class ControlOwnershipRuntimeScript : Script
    {
        private int _storyYieldUntil;

        public ControlOwnershipRuntimeScript()
        {
            Interval = 25;
            Tick += OnTick;
            Aborted += OnAborted;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (RockstarOwnsScene())
            {
                _storyYieldUntil = Game.GameTime + 5000;
                ControlOwnershipBridge.ClearAll();
                return;
            }
            if (Game.GameTime < _storyYieldUntil)
            {
                ControlOwnershipBridge.ClearAll();
                return;
            }
            ControlOwnershipBridge.Cleanup();
        }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try
            {
                if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN)) return true;
            }
            catch { }
            return false;
        }

        private void OnAborted(object sender, EventArgs e)
        {
            ControlOwnershipBridge.ClearAll();
        }
    }
}
