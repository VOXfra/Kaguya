using GTA;
using GTA.Native;
using GTA.UI;
using System;
using System.Drawing;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    internal static class PoliceWantedHudState
    {
        public static int Level;
        public static bool Owned;

        public static void Set(int level, bool owned)
        {
            Level = Math.Max(0, Math.Min(6, level));
            Owned = owned && Level > 0;
        }

        public static void Clear()
        {
            Level = 0;
            Owned = false;
        }
    }

    public sealed class PoliceOverhaulVIWantedHudScript : Script
    {
        private const string StarPath = "scripts\\PoliceOverhaulVI\\UI\\starRED.png";
        private CustomSprite _star;
        private bool _attempted;

        public PoliceOverhaulVIWantedHudScript()
        {
            Interval = 0;
            Tick += OnTick;
            Aborted += OnAborted;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!PoliceWantedHudState.Owned || PoliceWantedHudState.Level <= 0) return;

            try
            {
                // GTA V has storage/display logic for only five vanilla wanted
                // stars. When VOX owns free-roam wanted state, hide that row and
                // render the entire 1..6 row ourselves so the sixth slot cannot
                // flicker or disappear.
                Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, 1);
            }
            catch { }

            EnsureStar();
            if (_star == null) return;

            float right = 1152f;
            float y = 48f;
            float size = 25f;
            for (int i = 0; i < PoliceWantedHudState.Level; i++)
            {
                _star.Position = new PointF(right - i * 28f, y);
                _star.Size = new SizeF(size, size);
                _star.ScaledDraw();
            }
        }

        private void EnsureStar()
        {
            if (_attempted) return;
            _attempted = true;
            try
            {
                HudAssetInstaller.EnsureHighResolutionAssets();
                if (File.Exists(StarPath))
                    _star = new CustomSprite(Path.GetFullPath(StarPath), new SizeF(25f, 25f), new PointF(0f, 0f), Color.White, 0f, true);
            }
            catch { _star = null; }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            PoliceWantedHudState.Clear();
        }
    }
}
