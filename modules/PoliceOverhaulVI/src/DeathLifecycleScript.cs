using GTA;
using System;

namespace VOX.PoliceOverhaulVI
{
    // SHVDN loads each public Script subclass in the assembly. This tiny watcher
    // intentionally remains independent from PoliceOverhaulVIScript so the HUD
    // can be cleaned while the main runtime yields during the death screen.
    public sealed class PoliceOverhaulVIDeathLifecycleScript : Script
    {
        private bool _deathHandled;

        public PoliceOverhaulVIDeathLifecycleScript()
        {
            Interval = 100;
            Tick += OnTick;
        }

        private void OnTick(object sender, EventArgs e)
        {
            Ped player = Game.LocalPlayerPed;
            bool dead = player == null || !player.Exists() || player.IsDead;
            if (dead)
            {
                if (_deathHandled) return;
                _deathHandled = true;
                SearchHudSystem.NotifyPlayerDeath();
                return;
            }

            _deathHandled = false;
        }
    }
}
