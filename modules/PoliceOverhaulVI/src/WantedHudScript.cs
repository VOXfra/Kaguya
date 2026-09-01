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
            if (level <= 0 || !owned) { Clear(); return; }
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
        private const string CommonMenu = "commonmenu";
        private const string RockstarStar = "shop_new_star";
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
            if (RockstarOwnsScene()) { PoliceWantedHudState.Clear(); return; }
            if (!PoliceWantedHudState.Owned || PoliceWantedHudState.Level <= 0) return;

            int nativeWanted = 0;
            try { nativeWanted = Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle); } catch { }
            if (nativeWanted > 0 && PoliceWantedHudState.Level <= 5) return;

            if (nativeWanted > 0)
            {
                try { Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, 1); } catch { }
                DrawSixStarRow(false);
            }
            else DrawSixStarRow(true);
        }

        private void DrawSixStarRow(bool searchRed)
        {
            // Use a Rockstar-owned texture first. This avoids Enhanced path/CustomSprite
            // failures that previously made the sixth star silently disappear.
            bool rockstarReady = false;
            try
            {
                Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, CommonMenu, false);
                rockstarReady = Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, CommonMenu);
            }
            catch { }

            if (rockstarReady)
            {
                int r = searchRed ? 215 : 255;
                int g = searchRed ? 45 : 255;
                int b = searchRed ? 45 : 255;
                const float y = 0.0425f;
                const float width = 0.0185f;
                const float height = 0.0330f;
                const float gap = 0.0205f;
                float right = 0.955f;
                for (int i = 0; i < PoliceWantedHudState.Level; i++)
                {
                    float x = right - i * gap;
                    try { Function.Call(Hash.DRAW_SPRITE, CommonMenu, RockstarStar, x, y, width, height, 0f, r, g, b, 255, false); } catch { }
                }
                return;
            }

            EnsureWantedStars();
            CustomSprite sprite = searchRed ? _redStar : _whiteStar;
            if (sprite == null) return;
            DrawSpriteRow(sprite, PoliceWantedHudState.Level, 1195f, 48f, 25f);
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
                if (File.Exists(RedStarPath)) _redStar = new CustomSprite(Path.GetFullPath(RedStarPath), new SizeF(25f,25f), new PointF(0f,0f), Color.White, 0f, true);
                if (File.Exists(WhiteStarPath)) _whiteStar = new CustomSprite(Path.GetFullPath(WhiteStarPath), new SizeF(25f,25f), new PointF(0f,0f), Color.White, 0f, true);
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

        private void OnAborted(object sender, EventArgs e) { PoliceWantedHudState.Clear(); }
    }
}
