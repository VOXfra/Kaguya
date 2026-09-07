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
            if (level <= 0 || !owned) return;
            Level = Math.Max(1, Math.Min(6, level));
            Owned = true;
        }

        public static void Clear()
        {
            Level = 0;
            Owned = false;
        }
    }

    public sealed class PoliceOverhaulVIWantedHudScript : Script
    {
        private const string RedStarPath = "scripts\\PoliceOverhaulVI\\UI\\wantedStarRed.png";
        private const string WhiteStarPath = "scripts\\PoliceOverhaulVI\\UI\\wantedStarWhite.png";
        private CustomSprite _redStar;
        private CustomSprite _whiteStar;
        private bool _assetsAttempted;

        public PoliceOverhaulVIWantedHudScript()
        {
            Interval = 0;
            Tick += OnTick;
            Aborted += OnAborted;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (RockstarOwnsScene())
            {
                PoliceWantedHudState.Clear();
                return;
            }
            if (!PoliceWantedHudState.Owned || PoliceWantedHudState.Level <= 0) return;

            int nativeWanted = 0;
            try { nativeWanted = Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle); } catch { }

            // For ordinary 1-5 star pursuit, do not imitate Rockstar's HUD at all:
            // let GTA render its genuine wanted stars. Previous custom replacements
            // could resolve to translucent white squares on Enhanced.
            if (nativeWanted > 0 && PoliceWantedHudState.Level <= 5)
                return;

            if (nativeWanted > 0)
            {
                // GTA only owns five native wanted slots. At the internal sixth tier
                // we replace the row only for the otherwise impossible sixth star.
                try { Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, 1); } catch { }
                DrawOwnedSixStarRow();
            }
            else
            {
                // During VOX-owned search the native wanted display may be absent;
                // keep the dedicated red search treatment instead.
                DrawRedSearchRow();
            }
        }

        private void DrawOwnedSixStarRow()
        {
            EnsureWantedStars();
            if (_whiteStar == null) return;
            DrawSpriteRow(_whiteStar, PoliceWantedHudState.Level, 1152f, 48f, 25f);
        }

        private void DrawRedSearchRow()
        {
            EnsureWantedStars();
            if (_redStar == null) return;
            DrawSpriteRow(_redStar, PoliceWantedHudState.Level, 1152f, 48f, 25f);
        }

        private static void DrawSpriteRow(CustomSprite sprite, int level, float right, float y, float size)
        {
            for (int i = 0; i < level; i++)
            {
                sprite.Position = new PointF(right - i * 28f, y);
                sprite.Size = new SizeF(size, size);
                sprite.ScaledDraw();
            }
        }

        private void EnsureWantedStars()
        {
            if (_assetsAttempted) return;
            _assetsAttempted = true;
            try
            {
                HudAssetInstaller.EnsureHighResolutionAssets();
                if (File.Exists(RedStarPath))
                    _redStar = new CustomSprite(Path.GetFullPath(RedStarPath), new SizeF(25f, 25f), new PointF(0f, 0f), Color.White, 0f, true);
                if (File.Exists(WhiteStarPath))
                    _whiteStar = new CustomSprite(Path.GetFullPath(WhiteStarPath), new SizeF(25f, 25f), new PointF(0f, 0f), Color.White, 0f, true);
            }
            catch { _redStar = null; _whiteStar = null; }
        }

        private static bool RockstarOwnsScene()
        {
            try { if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true; } catch { }
            try { if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return true; } catch { }
            try { if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return true; } catch { }
            try { return Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_IN); }
            catch { return true; }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            PoliceWantedHudState.Clear();
        }
    }
}
